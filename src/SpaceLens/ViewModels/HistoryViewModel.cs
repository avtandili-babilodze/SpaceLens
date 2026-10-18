using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Interaction;

namespace SpaceLens.ViewModels;

/// <summary>The History page: everything SpaceLens moved or removed, and how much that cleared this month and overall.</summary>
public sealed partial class HistoryViewModel : ObservableObject
{
    private readonly ICleanupLog _log;
    private readonly IConfirmationService _dialogs;
    private readonly TimeProvider _clock;
    private readonly SynchronizationContext? _ui = SynchronizationContext.Current;

    [ObservableProperty]
    private string _monthText = "";

    [ObservableProperty]
    private string _monthCaption = "";

    [ObservableProperty]
    private string _totalText = "";

    [ObservableProperty]
    private string _totalCaption = "";

    public HistoryViewModel(ICleanupLog log, IConfirmationService dialogs, TimeProvider clock)
    {
        _log = log;
        _dialogs = dialogs;
        _clock = clock;
        log.Changed += (_, _) => OnUiThread(Rebuild);
        Rebuild();
    }

    public ObservableCollection<HistoryEntryViewModel> Entries { get; } = [];

    public bool HasEntries => Entries.Count > 0;

    [RelayCommand]
    private void Clear()
    {
        var confirmed = _dialogs.Confirm(new ConfirmationRequest(
            "Clear the cleanup history?",
            "This only forgets the list below. Nothing on your PC changes, and items in the Recycle Bin stay there.",
            "Clear history"));
        if (confirmed)
        {
            _log.Clear();
        }
    }

    private void OnUiThread(Action action)
    {
        if (_ui is null || SynchronizationContext.Current == _ui)
        {
            action();
        }
        else
        {
            _ui.Post(_ => action(), null);
        }
    }

    private void Rebuild()
    {
        var entries = _log.Entries;
        Entries.Clear();
        foreach (var entry in entries)
        {
            Entries.Add(new HistoryEntryViewModel(entry));
        }

        var now = _clock.GetUtcNow().ToLocalTime();
        var thisMonth = entries.Where(e => e.TimeUtc.ToLocalTime() is var local && local.Year == now.Year && local.Month == now.Month).ToList();
        MonthText = ByteSize.Format(thisMonth.Sum(e => e.ClearedBytes));
        MonthCaption = $"Cleaned up in {now:MMMM}  ·  {thisMonth.Count(e => e.ClearedBytes > 0):N0} actions";
        TotalText = ByteSize.Format(entries.Sum(e => e.ClearedBytes));
        TotalCaption = entries.Count == 0
            ? "Nothing yet"
            : $"Since {entries[^1].TimeUtc.ToLocalTime():MMM d, yyyy}  ·  {entries.Sum(e => e.ItemCount):N0} items";
        OnPropertyChanged(nameof(HasEntries));
    }
}
