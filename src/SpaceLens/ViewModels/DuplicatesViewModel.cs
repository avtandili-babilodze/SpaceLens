using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Duplicates;
using SpaceLens.Core.Services.Session;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>Finds identical files (same size, same SHA-256) and lets the user recycle extra copies.</summary>
public sealed partial class DuplicatesViewModel : ObservableObject
{
    private const string IntroText = "Find files with identical contents. Files are only read, never changed.";

    private readonly IDuplicateFinder _finder;
    private readonly IScanSession _session;
    private readonly ICleanupExecutor _executor;
    private readonly ICleanupRiskAnalyzer _riskAnalyzer;
    private readonly NodePresenter _presenter;
    private readonly IMessenger _messenger;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private string _status = IntroText;

    [ObservableProperty]
    private string _summary = "";

    public DuplicatesViewModel(
        IDuplicateFinder finder,
        IScanSession session,
        ICleanupExecutor executor,
        ICleanupRiskAnalyzer riskAnalyzer,
        NodePresenter presenter,
        IMessenger messenger)
    {
        _finder = finder;
        _session = session;
        _executor = executor;
        _riskAnalyzer = riskAnalyzer;
        _presenter = presenter;
        _messenger = messenger;
        session.ScanLoaded += (_, _) =>
        {
            FindCommand.Cancel();
            Groups.Clear();
            Summary = "";
            Status = IntroText;
        };
    }

    public ObservableCollection<DuplicateGroupViewModel> Groups { get; } = [];

    public bool HasScan => _session.HasScan;

    [RelayCommand(IncludeCancelCommand = true)]
    private async Task FindAsync(CancellationToken cancellationToken)
    {
        if (!_session.HasScan)
        {
            Status = "Scan a drive or folder first.";
            return;
        }

        IsSearching = true;
        Groups.Clear();
        Summary = "";
        Progress = 0;
        Status = "Collecting files of the same size…";
        try
        {
            // Only files the user could act on: system and app files are left out, and so are their hard links.
            var candidates = await _session.ReadAsync(
                scan => scan.Root.DescendantFiles()
                    .Where(f => f.Size >= DuplicateFinder.MinimumSize)
                    .GroupBy(f => f.Size)
                    .Where(g => g.Count() > 1)
                    .SelectMany(g => g)
                    .Where(f => _riskAnalyzer.Assess(f).AllowsRemoval)
                    .ToList(),
                cancellationToken);

            var progress = new Progress<DuplicateProgress>(p =>
            {
                Progress = p.Total == 0 ? 0 : 100.0 * p.Hashed / p.Total;
                Status = $"Comparing contents… {p.Hashed:N0} of {p.Total:N0} files";
            });
            var result = await _finder.FindAsync(candidates, progress, cancellationToken);

            foreach (var group in result.Groups)
            {
                Groups.Add(new DuplicateGroupViewModel(group, _presenter));
            }

            Summary = result.Groups.Count == 0 ? "" : $"{ByteSize.Format(result.RedundantBytes)} in extra copies";
            Status = result.Groups.Count == 0
                ? $"No identical files found among {result.CandidateCount:N0} same-size files of 1 MB or more."
                : $"{result.Groups.Count:N0} groups of identical files. The oldest copy in each group is kept unless you untick it.";
            if (result.SkippedCount > 0)
            {
                Status += $" {result.SkippedCount:N0} files were skipped (in use, changed since the scan, or hard links).";
            }
        }
        catch (OperationCanceledException)
        {
            Status = "Stopped. Nothing was changed.";
        }
        finally
        {
            IsSearching = false;
        }
    }

    [RelayCommand]
    private async Task RecycleSelectedAsync()
    {
        // Never let a selection remove the last copy of a file: groups with every copy ticked are skipped.
        var skippedGroups = Groups.Count(g => g.WouldRemoveEveryCopy);
        var selected = Groups.Where(g => !g.WouldRemoveEveryCopy).SelectMany(g => g.SelectedCopies).ToList();
        if (skippedGroups > 0)
        {
            var message = $"{skippedGroups:N0} group(s) had every copy selected and were skipped. Leave at least one copy unticked.";
            _messenger.Send(new NotificationMessage(message, IsError: true));
        }

        if (selected.Count == 0)
        {
            return;
        }

        var outcome = await _executor.RecycleAsync(selected);
        if (outcome.RecycledCount == 0)
        {
            return;
        }

        _messenger.Send(new NotificationMessage(outcome.Describe(), IsError: outcome.FailedCount > 0));
        foreach (var group in Groups.ToList())
        {
            var remaining = group.Files.Count(f => f.Node is FileNode file && file.Parent?.Files.Contains(file) == true);
            if (remaining < 2)
            {
                Groups.Remove(group);
            }
        }
    }

    [RelayCommand]
    private void ShowInExplorer(StorageItemViewModel? item)
    {
        if (item is not null)
        {
            _messenger.Send(new ShowInExplorerMessage(item.Node));
        }
    }
}
