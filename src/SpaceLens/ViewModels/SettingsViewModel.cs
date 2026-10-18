using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Configuration;
using SpaceLens.Core.Repositories;
using SpaceLens.Core.Services.AI;
using SpaceLens.Core.Services.Alerts;
using SpaceLens.Core.Services.Integration;
using SpaceLens.Core.Services.Interaction;
using SpaceLens.Core.Services.Security;
using SpaceLens.Core.Services.Settings;
using SpaceLens.Core.Services.Storage;
using SpaceLens.Infrastructure;
using SpaceLens.Services;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>
/// AI assistant configuration (API key, consent, connection test), never-touch folders, daily alerts, how SpaceLens is
/// set up on this PC (install, right-click menu) and local data.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private const string ConsoleKeysUrl = "https://console.anthropic.com/settings/keys";

    private readonly ICredentialStore _credentials;
    private readonly IAiAssistantService _claude;
    private readonly ISettingsStore _settings;
    private readonly IStorageHistoryService _history;
    private readonly IConfirmationService _dialogs;
    private readonly IShellService _shell;
    private readonly IMessenger _messenger;
    private readonly IFolderPicker _folderPicker;
    private readonly IDailyCheckScheduler _scheduler;
    private readonly IDriveCheck _driveCheck;
    private readonly IAlertPresenter _alerts;
    private readonly IShellIntegration _integration;
    private readonly Installer _installer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKey), nameof(StatusTitle))]
    private ConnectionState _state;

    [ObservableProperty]
    private string _statusDetail = "";

    [ObservableProperty]
    private string? _keyHint;

    [ObservableProperty]
    private bool _shareScanDetails;

    [ObservableProperty]
    private bool _isDailyCheckEnabled;

    [ObservableProperty]
    private int _lowSpacePercent;

    [ObservableProperty]
    private int _growthAlertGb;

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _isContextMenuEnabled;

    public SettingsViewModel(
        ICredentialStore credentials,
        IAiAssistantService claude,
        ISettingsStore settings,
        IStorageHistoryService history,
        IConfirmationService dialogs,
        IShellService shell,
        IMessenger messenger,
        AppPaths paths,
        IFolderPicker folderPicker,
        IDailyCheckScheduler scheduler,
        IDriveCheck driveCheck,
        IAlertPresenter alerts,
        IShellIntegration integration,
        Installer installer)
    {
        _credentials = credentials;
        _claude = claude;
        _settings = settings;
        _history = history;
        _dialogs = dialogs;
        _shell = shell;
        _messenger = messenger;
        _folderPicker = folderPicker;
        _scheduler = scheduler;
        _driveCheck = driveCheck;
        _alerts = alerts;
        _integration = integration;
        _installer = installer;
        DataFolder = paths.Root;
        _shareScanDetails = settings.Current.ShareScanDetails;
        _lowSpacePercent = settings.Current.LowSpacePercent;
        _growthAlertGb = settings.Current.GrowthAlertGb;
        _isDailyCheckEnabled = scheduler.IsEnabled;
        _isContextMenuEnabled = integration.ContextMenuTarget is not null;
        settings.Changed += (_, _) => OnSettingsChanged();
        OnSettingsChanged();
        RefreshKeyState();
    }

    /// <summary>Folders SpaceLens never suggests for cleanup.</summary>
    public ObservableCollection<string> ExcludedFolders { get; } = [];

    public IReadOnlyList<int> LowSpaceChoices { get; } = [5, 10, 15, 20, 25];

    public IReadOnlyList<int> GrowthChoices { get; } = [2, 5, 10, 20, 50];

    public bool IsInstalled => Installer.IsRunningInstalledCopy;

    public bool CanInstall => Installer.CanInstall && !Installer.IsRunningInstalledCopy;

    public string InstallStatus => Installer.IsRunningInstalledCopy
        ? $"Installed for your account in {Installer.InstallFolder}. Uninstall it from Settings › Apps like any other app."
        : Installer.CanInstall
            ? "Running without installing. Install to get desktop and Start menu shortcuts and an entry in Settings › Apps."
            : "This is a development build. Run package.cmd to make the SpaceLens.zip you can install or send to someone.";

    public string Model => _settings.Current.Model;

    public string DataFolder { get; }

    public bool HasKey => State != ConnectionState.NoKey;

    public string StatusTitle => State switch
    {
        ConnectionState.NoKey => "Not connected",
        ConnectionState.NotTested => "Key saved",
        ConnectionState.Testing => "Testing connection…",
        ConnectionState.Connected => "Connected",
        _ => "Connection failed"
    };

    /// <summary>Encrypts and stores a pasted key. The view passes the PasswordBox text and clears it right away.</summary>
    [RelayCommand]
    private async Task SaveKeyAsync(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            State = HasKey ? State : ConnectionState.NoKey;
            StatusDetail = "Paste your Anthropic API key first.";
            return;
        }

        try
        {
            await _credentials.SaveApiKeyAsync(apiKey);
            RefreshKeyState();
            StatusDetail = "Encrypted for your Windows account. Test the connection to check it works.";
            _messenger.Send(new AiConfigurationChangedMessage());
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or CryptographicException)
        {
            StatusDetail = exception.Message;
        }
    }

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        if (!_credentials.HasApiKey)
        {
            StatusDetail = "Save an API key first.";
            return;
        }

        State = ConnectionState.Testing;
        StatusDetail = "Sending a tiny test request to Claude Haiku 5.5…";
        try
        {
            await _claude.TestConnectionAsync(cancellationToken);
            State = ConnectionState.Connected;
            StatusDetail = $"Claude Haiku 5.5 answered at {DateTime.Now:t}. The assistant is ready.";
        }
        catch (OperationCanceledException)
        {
            State = ConnectionState.NotTested;
            StatusDetail = "Test cancelled.";
        }
        catch (Exception exception) when (exception is AiServiceException or InvalidOperationException)
        {
            State = ConnectionState.Failed;
            StatusDetail = exception.Message;
        }
    }

    [RelayCommand]
    private void RemoveKey()
    {
        var confirmed = _dialogs.Confirm(new ConfirmationRequest(
            "Remove the saved API key?",
            "The assistant will answer offline until you add a key again.",
            "Remove key"));
        if (!confirmed)
        {
            return;
        }

        _credentials.RemoveApiKey();
        RefreshKeyState();
        StatusDetail = "The key was deleted from this PC.";
        _messenger.Send(new AiConfigurationChangedMessage());
    }

    [RelayCommand]
    private void OpenConsole()
    {
        try
        {
            _shell.OpenUrl(ConsoleKeysUrl);
        }
        catch (InvalidOperationException exception)
        {
            _messenger.Send(new NotificationMessage(exception.Message, IsError: true));
        }
    }

    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        var confirmed = _dialogs.Confirm(new ConfirmationRequest(
            "Delete scan history?",
            "Saved scans are used to show growth over time. Your files aren't affected.",
            "Delete history",
            IsDestructive: true));
        if (!confirmed)
        {
            return;
        }

        try
        {
            await _history.DeleteAllAsync();
            _messenger.Send(new HistoryClearedMessage());
            _messenger.Send(new NotificationMessage("Scan history deleted."));
        }
        catch (HistoryStoreException exception)
        {
            _messenger.Send(new NotificationMessage("Scan history couldn't be deleted: " + exception.Message, IsError: true));
        }
    }

    [RelayCommand]
    private void AddExcludedFolder()
    {
        var folder = _folderPicker.PickFolder("Choose a folder SpaceLens should never suggest for cleanup");
        if (folder is null || ExcludedFolders.Contains(folder, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _settings.Save(_settings.Current with { ExcludedFolders = [.. _settings.Current.ExcludedFolders, folder] });
    }

    [RelayCommand]
    private void RemoveExcludedFolder(string? folder)
    {
        if (folder is null)
        {
            return;
        }

        var remaining = _settings.Current.ExcludedFolders
            .Where(f => !string.Equals(f, folder, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _settings.Save(_settings.Current with { ExcludedFolders = remaining });
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        IsChecking = true;
        try
        {
            var alerts = await _driveCheck.RunAsync(CancellationToken.None);
            if (alerts.Count > 0)
            {
                _alerts.Show(alerts);
            }
            else
            {
                _messenger.Send(new NotificationMessage(
                    $"All good: every drive has more than {LowSpacePercent}% free, and the system drive didn't grow by {GrowthAlertGb} GB or more."));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _messenger.Send(new NotificationMessage("The check couldn't finish: " + exception.Message, IsError: true));
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private void Install() => _installer.InstallAndSwitch();

    partial void OnIsDailyCheckEnabledChanged(bool value)
    {
        try
        {
            if (value)
            {
                _scheduler.Enable(PreferredExecutable());
                _messenger.Send(new NotificationMessage("SpaceLens will check your drives every day around noon (or soon after the PC starts)."));
            }
            else
            {
                _scheduler.Disable();
            }
        }
        catch (InvalidOperationException exception)
        {
            _messenger.Send(new NotificationMessage(exception.Message, IsError: true));
            SetProperty(ref _isDailyCheckEnabled, _scheduler.IsEnabled, nameof(IsDailyCheckEnabled));
        }
    }

    partial void OnIsContextMenuEnabledChanged(bool value)
    {
        try
        {
            if (value)
            {
                _integration.RegisterContextMenu(PreferredExecutable());
            }
            else
            {
                _integration.UnregisterContextMenu();
            }
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            _messenger.Send(new NotificationMessage("Windows didn't allow changing the right-click menu: " + exception.Message, IsError: true));
        }
    }

    partial void OnLowSpacePercentChanged(int value)
    {
        if (_settings.Current.LowSpacePercent != value)
        {
            _settings.Save(_settings.Current with { LowSpacePercent = value });
        }
    }

    partial void OnGrowthAlertGbChanged(int value)
    {
        if (_settings.Current.GrowthAlertGb != value)
        {
            _settings.Save(_settings.Current with { GrowthAlertGb = value });
        }
    }

    partial void OnShareScanDetailsChanged(bool value)
    {
        if (_settings.Current.ShareScanDetails != value)
        {
            _settings.Save(_settings.Current with { ShareScanDetails = value });
        }
    }

    /// <summary>Scheduled checks and the right-click menu should start the installed copy when there is one.</summary>
    private static string PreferredExecutable() =>
        File.Exists(Installer.InstalledPath) ? Installer.InstalledPath : Environment.ProcessPath!;

    private void OnSettingsChanged()
    {
        ShareScanDetails = _settings.Current.ShareScanDetails;
        ExcludedFolders.Clear();
        foreach (var folder in _settings.Current.ExcludedFolders)
        {
            ExcludedFolders.Add(folder);
        }
    }

    private void RefreshKeyState()
    {
        KeyHint = _credentials.ApiKeyHint is { } hint ? $"sk-ant-…{hint}" : null;
        State = _credentials.HasApiKey ? ConnectionState.NotTested : ConnectionState.NoKey;
        if (State == ConnectionState.NoKey)
        {
            StatusDetail = "Add a key to ask Claude about your storage. Everything else works offline.";
        }
    }
}
