using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Repositories;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Session;
using SpaceLens.Core.Services.Storage;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>Compares two saved scans of the same location: what grew, what shrank, which large files appeared.</summary>
public sealed partial class CompareViewModel : ObservableObject, IRecipient<HistoryClearedMessage>
{
    private const string NoComparisonYet =
        "Scan the same drive or folder twice and SpaceLens shows what changed in between. Completed scans are saved automatically.";

    private readonly IStorageHistoryService _history;
    private readonly IScanSession _session;
    private readonly IMessenger _messenger;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private StorageSnapshot? _before;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CompareCommand))]
    private StorageSnapshot? _after;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasComparison), nameof(NetChangeText), nameof(PeriodText), nameof(IsGrowing))]
    private SnapshotComparison? _comparison;

    [ObservableProperty]
    private IReadOnlyList<ChangeRowViewModel> _grew = [];

    [ObservableProperty]
    private IReadOnlyList<ChangeRowViewModel> _shrank = [];

    [ObservableProperty]
    private IReadOnlyList<ChangeRowViewModel> _newFiles = [];

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _isBusy;

    public CompareViewModel(IStorageHistoryService history, IScanSession session, IMessenger messenger)
    {
        _history = history;
        _session = session;
        _messenger = messenger;
        session.PropertyChanged += OnSessionChanged;
        messenger.Register(this);
    }

    public ObservableCollection<StorageSnapshot> Snapshots { get; } = [];

    public bool HasComparison => Comparison is not null;

    public bool IsGrowing => Comparison?.NetChange > 0;

    public string NetChangeText => Comparison is null ? "" : ByteSize.FormatDelta(Comparison.NetChange);

    public string PeriodText
    {
        get
        {
            if (Comparison is null)
            {
                return "";
            }

            var before = Comparison.Before.CreatedUtc.ToLocalTime();
            var after = Comparison.After.CreatedUtc.ToLocalTime();
            return $"{before:MMM d, HH:mm}  →  {after:MMM d, HH:mm}  ·  {Comparison.Before.RootPath}";
        }
    }

    public void Receive(HistoryClearedMessage message)
    {
        Comparison = null;
        _ = ReloadAsync();
    }

    /// <summary>Opens the history database. History is optional, so a failure only shows a message on this page.</summary>
    public async Task InitializeAsync()
    {
        try
        {
            await _history.InitializeAsync();
        }
        catch (HistoryStoreException exception)
        {
            Message = "Scan history is unavailable: " + exception.Message;
            return;
        }

        await ReloadAsync();
    }

    [RelayCommand]
    public async Task ReloadAsync()
    {
        try
        {
            var snapshots = await _history.GetSnapshotsAsync();
            Snapshots.Clear();
            foreach (var snapshot in snapshots)
            {
                Snapshots.Add(snapshot);
            }

            // Default to the two most recent scans of whatever location is loaded now.
            var root = _session.Current?.RootPath ?? snapshots.FirstOrDefault()?.RootPath;
            var sameRoot = snapshots.Where(s => root is not null && PathRules.IsSame(s.RootPath, root)).ToList();
            After = sameRoot.FirstOrDefault();
            Before = sameRoot.Skip(1).FirstOrDefault();
            Message = Before is null ? NoComparisonYet : null;

            if (Before is not null && After is not null)
            {
                await CompareAsync();
            }
        }
        catch (HistoryStoreException exception)
        {
            Message = "Scan history couldn't be read: " + exception.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private async Task CompareAsync()
    {
        if (Before is null || After is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var comparison = await _history.CompareAsync(Before.Id, After.Id);
            Comparison = comparison;
            Grew = comparison.Grew
                .Select(c => new ChangeRowViewModel(c.Path, c.BeforeBytes, c.AfterBytes, c.IsNew ? "New folder" : ""))
                .ToList();
            Shrank = comparison.Shrank
                .Select(c => new ChangeRowViewModel(c.Path, c.BeforeBytes, c.AfterBytes, c.IsGone ? "Removed" : ""))
                .ToList();
            NewFiles = comparison.NewLargeFiles
                .Select(f => new ChangeRowViewModel(f.Path, f.BeforeBytes, f.AfterBytes, f.IsNew ? "New file" : "Grew"))
                .ToList();
            Message = null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or HistoryStoreException)
        {
            Comparison = null;
            Message = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanCompare() => Before is not null && After is not null && Before.Id != After.Id;

    [RelayCommand]
    private void Locate(ChangeRowViewModel? row)
    {
        var node = row is null ? null : _session.Current?.Root.Find(row.Path);
        if (node is null)
        {
            const string NotInScan = "That item isn't in the scan that's loaded now (it may have been removed).";
            _messenger.Send(new NotificationMessage(NotInScan, IsError: true));
            return;
        }

        _messenger.Send(new ShowInExplorerMessage(node));
    }

    [RelayCommand]
    private void AskAboutChanges()
    {
        if (Comparison is null)
        {
            return;
        }

        var question = Comparison.NetChange >= 0
            ? "What used the extra space since my previous scan, and is any of it safe to clean?"
            : "What changed since my previous scan?";
        _messenger.Send(new AskAssistantMessage(question));
    }

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A new history entry was just saved; show it.
        if (e.PropertyName == nameof(IScanSession.Growth))
        {
            _ = ReloadAsync();
        }
    }
}
