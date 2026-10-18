using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Apps;
using SpaceLens.Core.Services.Interaction;
using SpaceLens.Core.Services.Session;
using SpaceLens.Infrastructure;
using SpaceLens.Services;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>
/// The Apps page: installed programs with their size and when they were last used, so unused ones can be uninstalled
/// with their own uninstaller. Sizes come from the scan when it covers the app's folder.
/// </summary>
public sealed partial class AppsViewModel : ObservableObject
{
    private const string AppsSettingsUrl = "ms-settings:appsfeatures";

    private readonly IInstalledAppsService _apps;
    private readonly IScanSession _session;
    private readonly IConfirmationService _dialogs;
    private readonly IShellService _shell;
    private readonly IMessenger _messenger;
    private readonly TimeProvider _clock;
    private List<AppItemViewModel> _all = [];
    private int _generation;
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private string _summary = "";

    public AppsViewModel(
        IInstalledAppsService apps,
        IScanSession session,
        IConfirmationService dialogs,
        IShellService shell,
        IMessenger messenger,
        TimeProvider clock)
    {
        _apps = apps;
        _session = session;
        _dialogs = dialogs;
        _shell = shell;
        _messenger = messenger;
        _clock = clock;

        // A new scan can measure app folders the installers didn't report.
        session.ScanLoaded += (_, _) => _isLoaded = false;
    }

    public ObservableCollection<AppItemViewModel> Apps { get; } = [];

    public Task EnsureLoadedAsync() => _isLoaded || IsLoading ? Task.CompletedTask : RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        var generation = ++_generation;
        IsLoading = true;
        try
        {
            var installed = await Task.Run(_apps.GetInstalledApps);
            var measured = _session.HasScan
                ? await _session.ReadAsync(scan => installed.Select(app => AppSize.Measure(app, scan)).ToList())
                : installed.Select(_ => (long?)null).ToList();
            var now = _clock.GetUtcNow().UtcDateTime;
            _all = installed
                .Select((app, i) => new AppItemViewModel(app, measured[i], now))
                .OrderByDescending(a => a.SizeBytes)
                .ToList();
            _isLoaded = true;
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }

        var measuring = MeasureMissingSizesAsync(generation);

        // Icons come from each app's files, so they load in the background without holding up the list.
        foreach (var item in _all.ToList())
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            if (generation != _generation)
            {
                return;
            }

            item.Icon = AppIconLoader.Load(item.App.IconPath);
        }

        await measuring;
    }

    /// <summary>
    /// Apps whose installer reported no size and whose folder isn't in the scan (an app on another drive, say) are
    /// measured on disk, one at a time, and the list is sorted again once they are known.
    /// </summary>
    private async Task MeasureMissingSizesAsync(int generation)
    {
        var missing = _all.Where(a => a.NeedsMeasuring).ToList();
        foreach (var item in missing)
        {
            item.IsMeasuring = true;
        }

        foreach (var item in missing)
        {
            var folder = item.App.InstallFolder!;
            var bytes = await Task.Run(() => AppSize.MeasureOnDisk(folder, CancellationToken.None));
            if (generation != _generation)
            {
                return;
            }

            item.ShowMeasuredSize(bytes);
        }

        if (missing.Count > 0)
        {
            _all = [.. _all.OrderByDescending(a => a.SizeBytes)];
            ApplyFilter();
        }
    }

    [RelayCommand]
    private void Uninstall(AppItemViewModel? item)
    {
        if (item?.App.UninstallCommand is not { } command)
        {
            return;
        }

        var size = item.SizeBytes > 0 ? $" It uses about {item.SizeText}." : "";
        var confirmed = _dialogs.Confirm(new ConfirmationRequest(
            $"Uninstall {item.Name}?",
            $"SpaceLens will start {item.Name}'s own uninstaller.{size} Follow its steps to finish.",
            "Start uninstaller",
            Footnote: item.App.IsMachineWide
                ? "It's installed for everyone on this PC, so Windows may ask for administrator permission. Documents you made with it are usually kept."
                : "Documents you made with it are usually kept.",
            IsDestructive: true));
        if (!confirmed)
        {
            return;
        }

        try
        {
            var (fileName, arguments) = UninstallCommandLine.Split(command);
            _shell.Run(fileName, arguments);
            _messenger.Send(new NotificationMessage($"{item.Name}'s uninstaller is running. Click Refresh when it's done."));
        }
        catch (InvalidOperationException exception)
        {
            _messenger.Send(new NotificationMessage(exception.Message, IsError: true));
        }
    }

    [RelayCommand]
    private void OpenAppsSettings()
    {
        try
        {
            _shell.OpenUrl(AppsSettingsUrl);
        }
        catch (InvalidOperationException exception)
        {
            _messenger.Send(new NotificationMessage(exception.Message, IsError: true));
        }
    }

    [RelayCommand]
    private void AskAi() => _messenger.Send(new AskAssistantMessage(
        "Which of my installed apps could I uninstall to free space? Look at their size and when I last used them.",
        null));

    partial void OnFilterTextChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var filter = FilterText.Trim();
        Apps.Clear();
        foreach (var item in _all.Where(a => filter.Length == 0
            || a.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase)
            || a.App.Publisher.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            Apps.Add(item);
        }

        var total = _all.Sum(a => a.SizeBytes);
        var unused = _all.Count(a => a.IsUnused);
        Summary = $"{_all.Count:N0} apps  ·  {ByteSize.Format(total)}"
            + (unused > 0 ? $"  ·  {unused:N0} not used in 6 months" : "");
    }
}
