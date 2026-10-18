using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Interaction;
using SpaceLens.Core.Services.Session;
using SpaceLens.Core.Services.SystemSpace;
using SpaceLens.Services;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>
/// The System space page: the big files and folders Windows keeps for itself, measured, with the Windows tool that can
/// shrink each one safely. SpaceLens never deletes any of it directly.
/// </summary>
public sealed partial class SystemSpaceViewModel : ObservableObject
{
    private readonly ISystemSpaceService _service;
    private readonly IScanSession _session;
    private readonly IConfirmationService _dialogs;
    private readonly IShellService _shell;
    private readonly IMessenger _messenger;
    private bool _isLoaded;

    [ObservableProperty]
    private bool _isMeasuring;

    [ObservableProperty]
    private string _summary = "";

    public SystemSpaceViewModel(
        ISystemSpaceService service,
        IScanSession session,
        IConfirmationService dialogs,
        IShellService shell,
        IMessenger messenger)
    {
        _service = service;
        _session = session;
        _dialogs = dialogs;
        _shell = shell;
        _messenger = messenger;
        session.ScanLoaded += (_, _) => _isLoaded = false;
    }

    public ObservableCollection<SystemSpaceItemViewModel> Items { get; } = [];

    public Task EnsureLoadedAsync() => _isLoaded || IsMeasuring ? Task.CompletedTask : RefreshAsync();

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsMeasuring = true;
        try
        {
            var items = await _service.MeasureAsync(_session.Current, CancellationToken.None);
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(new SystemSpaceItemViewModel(item));
            }

            var drive = (Path.GetPathRoot(Environment.SystemDirectory) ?? "C:").TrimEnd('\\');
            Summary = $"Windows keeps at least {ByteSize.Format(Items.Sum(i => i.Bytes))} of these on {drive}";
            _isLoaded = true;
        }
        finally
        {
            IsMeasuring = false;
        }
    }

    [RelayCommand]
    private void RunAction(SystemSpaceItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        try
        {
            switch (item.Kind, item.Item.Exists)
            {
                case (SystemSpaceKind.Hibernation, true):
                    if (Confirm(
                        "Turn off hibernation?",
                        $"Windows deletes the hibernation file ({item.SizeText}). Hibernate and Fast Startup stop working until you turn it back on here.",
                        "Turn off"))
                    {
                        _shell.Run("powercfg.exe", "/hibernate off", elevated: true);
                        _ = RefreshLaterAsync();
                    }

                    break;
                case (SystemSpaceKind.Hibernation, false):
                    _shell.Run("powercfg.exe", "/hibernate on", elevated: true);
                    _ = RefreshLaterAsync();
                    break;
                case (SystemSpaceKind.PageFile, _):
                    _shell.Run("SystemPropertiesPerformance.exe");
                    _messenger.Send(new NotificationMessage("In Performance Options, open Advanced › Virtual memory › Change."));
                    break;
                case (SystemSpaceKind.ComponentStore, _):
                    if (Confirm(
                        "Clean up the component store?",
                        "Windows removes component versions that newer updates replaced. It runs in a command window and can take several minutes. "
                        + "Afterwards, those updates can no longer be uninstalled.",
                        "Start cleanup"))
                    {
                        _shell.Run("cmd.exe", "/k Dism.exe /Online /Cleanup-Image /StartComponentCleanup", elevated: true);
                    }

                    break;
                case (SystemSpaceKind.RestorePoints, _):
                    _shell.Run("SystemPropertiesProtection.exe");
                    _messenger.Send(new NotificationMessage("In System Protection, select your drive and choose Configure to delete restore points or limit their space."));
                    break;
                default:
                    _shell.OpenStorageSettings();
                    break;
            }
        }
        catch (InvalidOperationException exception)
        {
            _messenger.Send(new NotificationMessage(exception.Message, IsError: true));
        }
    }

    private bool Confirm(string title, string message, string confirmText) =>
        _dialogs.Confirm(new ConfirmationRequest(title, message, confirmText, Footnote: "Windows will ask for administrator permission."));

    /// <summary>powercfg finishes a moment after it starts; measure again once it has.</summary>
    private async Task RefreshLaterAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(3));
        await RefreshAsync();
    }
}
