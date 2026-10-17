using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Session;

namespace SpaceLens.ViewModels;

/// <summary>The four summary cards: drive usage, low-risk cleanup, large files and growth since the last scan.</summary>
public sealed partial class DashboardViewModel : ObservableObject
{
    private readonly IScanSession _session;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UsedFraction), nameof(UsedPercentText), nameof(UsedText), nameof(CapacityText))]
    private DriveSummary? _drive;

    [ObservableProperty]
    private string _largeFileCountText = "—";

    public DashboardViewModel(IScanSession session)
    {
        _session = session;
        session.PropertyChanged += OnSessionChanged;
        session.ContentsChanged += (_, _) => _ = RefreshLargeFilesAsync();
    }

    public double UsedFraction => Drive?.UsedFraction ?? 0;

    public string UsedPercentText => Drive is null ? "—" : $"{Drive.UsedFraction:P0}".Replace(" ", "");

    public string UsedText => Drive is null ? "—" : ByteSize.Format(Drive.UsedBytes);

    public string CapacityText => Drive is null ? "No drive selected" : $"of {ByteSize.Format(Drive.TotalBytes)}";

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
}
