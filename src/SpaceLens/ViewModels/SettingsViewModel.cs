using System.IO;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Configuration;
using SpaceLens.Core.Repositories;
using SpaceLens.Core.Services.AI;
using SpaceLens.Core.Services.Interaction;
using SpaceLens.Core.Services.Security;
using SpaceLens.Core.Services.Settings;
using SpaceLens.Core.Services.Storage;
using SpaceLens.Services;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>AI assistant configuration (API key, consent, connection test) and local data.</summary>
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasKey), nameof(StatusTitle))]
    private ConnectionState _state;

    [ObservableProperty]
    private string _statusDetail = "";

    [ObservableProperty]
    private string? _keyHint;

    [ObservableProperty]
    private bool _shareScanDetails;

    public SettingsViewModel(
        ICredentialStore credentials,
        IAiAssistantService claude,
        ISettingsStore settings,
        IStorageHistoryService history,
        IConfirmationService dialogs,
        IShellService shell,
        IMessenger messenger,
        AppPaths paths)
    {
        _credentials = credentials;
        _claude = claude;
        _settings = settings;
        _history = history;
        _dialogs = dialogs;
        _shell = shell;
        _messenger = messenger;
        DataFolder = paths.Root;
        _shareScanDetails = settings.Current.ShareScanDetails;
        settings.Changed += (_, _) => ShareScanDetails = settings.Current.ShareScanDetails;
        RefreshKeyState();
    }

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

    partial void OnShareScanDetailsChanged(bool value)
    {
        if (_settings.Current.ShareScanDetails != value)
        {
            _settings.Save(_settings.Current with { ShareScanDetails = value });
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
