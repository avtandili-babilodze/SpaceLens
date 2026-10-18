using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Session;

namespace SpaceLens.ViewModels;

/// <summary>
/// The Overview summary: the drive's capacity bar (its largest folders, the rest and free space) and the figures
/// beside it: low-risk cleanup, large files and growth since the last scan.
/// </summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private const int LargestShown = 3;
    private const string UsedColor = "#3D8BFF";

    // A part thinner than this share of the drive wouldn't be visible in the bar.
    private const double MinimumShare = 0.002;

    private readonly IScanSession _session;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsedFraction), nameof(UsedPercentText), nameof(UsedText), nameof(CapacityText), nameof(FreeText), nameof(IsLowOnSpace))]
    private DriveSummary? _drive;

    [ObservableProperty]
    private string _largeFileCountText = "—";

    [ObservableProperty]
    private IReadOnlyList<CapacitySegment> _segments = [];

    public DashboardViewModel(IScanSession session)
    {
        _session = session;
        session.PropertyChanged += OnSessionChanged;
        session.ContentsChanged += (_, _) =>
        {
            _ = RefreshLargeFilesAsync();
            _ = RefreshSegmentsAsync();
        };
    }

    public double UsedFraction => Drive?.UsedFraction ?? 0;

    public string UsedPercentText => Drive is null ? "—" : $"{Drive.UsedFraction:P0}".Replace(" ", "");

    public string UsedText => Drive is null ? "—" : ByteSize.Format(Drive.UsedBytes);

    public string CapacityText => Drive is null ? "No drive selected" : $"of {ByteSize.Format(Drive.TotalBytes)}";

    public string FreeText => Drive is null ? "" : $"{ByteSize.Format(Drive.FreeBytes)} free";

    /// <summary>Less than a tenth of the drive is free, the point where Windows itself starts warning.</summary>
    public bool IsLowOnSpace => Drive is { TotalBytes: > 0 } drive && (double)drive.FreeBytes / drive.TotalBytes < 0.1;

    public string RecoverableText => _session.HasScan ? ByteSize.Format(_session.Cleanup.LowRiskBytes) : "—";

    public string RecoverableCaption => _session.HasScan ? "Low risk to clean" : "Scan to find out";

    public string RecoverableDetail
    {
        get
        {
            if (!_session.HasScan)
            {
                return "Scan a drive to find caches, temporary files and other leftovers.";
            }

            var lowRisk = ByteSize.Format(_session.Cleanup.LowRiskBytes);
            var review = ByteSize.Format(_session.Cleanup.ReviewBytes);
            return $"{lowRisk} is low risk to clean. Another {review} could be freed after you review it on the Cleanup page.";
        }
    }

    public string GrowthText => _session.Growth is { } growth ? ByteSize.FormatDelta(growth.NetChange) : "—";

    public bool IsGrowing => _session.Growth?.NetChange > 0;

    public string GrowthCaption => _session.Growth is { } growth
        ? $"since {growth.Before.CreatedUtc.ToLocalTime():MMM d}"
        : _session.HasScan ? "First scan here" : "Scan again later";

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(IScanSession.Current):
                LargeFileCountText = "—";
                RaiseCleanupChanged();
                OnPropertyChanged(nameof(GrowthCaption));
                _ = RefreshLargeFilesAsync();
                _ = RefreshSegmentsAsync();
                break;
            case nameof(IScanSession.Cleanup):
                RaiseCleanupChanged();
                break;
            case nameof(IScanSession.Growth):
                OnPropertyChanged(nameof(GrowthText));
                OnPropertyChanged(nameof(GrowthCaption));
                OnPropertyChanged(nameof(IsGrowing));
                break;
        }
    }

    partial void OnDriveChanged(DriveSummary? value) => _ = RefreshSegmentsAsync();

    private void RaiseCleanupChanged()
    {
        OnPropertyChanged(nameof(RecoverableText));
        OnPropertyChanged(nameof(RecoverableCaption));
        OnPropertyChanged(nameof(RecoverableDetail));
    }

    private async Task RefreshLargeFilesAsync()
    {
        if (!_session.HasScan)
        {
            return;
        }

        try
        {
            var count = await _session.ReadAsync(scan => StorageAnalyzer.CountFilesAtLeast(scan.Root, ByteSize.GB));
            LargeFileCountText = count.ToString("N0");
        }
        catch (InvalidOperationException)
        {
            LargeFileCountText = "—";
        }
    }

    /// <summary>
    /// The scan's largest folders as parts of the drive, then whatever used space the scan didn't see: system and
    /// unreadable files on a whole-drive scan, the rest of the drive on a folder scan.
    /// </summary>
    private async Task RefreshSegmentsAsync()
    {
        if (Drive is not { TotalBytes: > 0 } drive)
        {
            Segments = [];
            return;
        }

        var scan = _session.Current;
        if (scan is null || !string.Equals(Path.GetPathRoot(scan.RootPath), drive.RootPath, StringComparison.OrdinalIgnoreCase))
        {
            Segments = [new CapacitySegment("Used", ByteSize.Format(drive.UsedBytes), drive.UsedFraction, UsedColor)];
            return;
        }

        List<BreakdownSegment> parts;
        try
        {
            parts = await _session.ReadAsync(s => StorageAnalyzer.ChildBreakdown(s.Root, LargestShown));
        }
        catch (InvalidOperationException)
        {
            return;
        }

        if (!ReferenceEquals(drive, Drive))
        {
            return;
        }

        var total = (double)drive.TotalBytes;
        var segments = parts
            .Where(p => p.Bytes / total >= MinimumShare)
            .Select(p => new CapacitySegment(p.Label, p.SizeText, p.Bytes / total, p.Color))
            .ToList();

        var unseen = drive.UsedBytes - parts.Sum(p => p.Bytes);
        if (unseen / total >= MinimumShare)
        {
            var label = scan.IsDriveRoot ? "System & unreadable" : "Rest of the drive";
            segments.Add(new CapacitySegment(label, ByteSize.Format(unseen), unseen / total, StorageAnalyzer.UnaccountedColor));
        }

        Segments = segments;
    }
}
