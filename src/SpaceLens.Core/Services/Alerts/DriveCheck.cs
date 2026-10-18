using Microsoft.Extensions.Logging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Repositories;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Settings;
using SpaceLens.Core.Services.Storage;

namespace SpaceLens.Core.Services.Alerts;

/// <summary>The daily check behind low-space and fast-growth alerts. Thresholds come from settings.</summary>
public sealed class DriveCheck(
    IDriveService drives,
    IDiskScanner scanner,
    IStorageHistoryService history,
    ISettingsStore settings,
    ILogger<DriveCheck> logger) : IDriveCheck
{
    private const long BytesPerGb = 1024L * 1024 * 1024;

    public async Task<IReadOnlyList<DriveAlert>> RunAsync(CancellationToken cancellationToken)
    {
        var alerts = new List<DriveAlert>();
        var threshold = Math.Clamp(settings.Current.LowSpacePercent, 1, 50);
        foreach (var drive in drives.GetDrives().Where(d => d.Type == DriveType.Fixed && d.TotalBytes > 0))
        {
            var freePercent = 100.0 * drive.FreeBytes / drive.TotalBytes;
            if (freePercent < threshold)
            {
                alerts.Add(new DriveAlert(
                    $"{drive.DisplayName} is almost full",
                    $"Only {ByteSize.Format(drive.FreeBytes)} free ({freePercent:0}%). Open SpaceLens to see what's using the space."));
            }
        }

        if (await CheckGrowthAsync(cancellationToken) is { } growth)
        {
            alerts.Add(growth);
        }

        return alerts;
    }

    private async Task<DriveAlert?> CheckGrowthAsync(CancellationToken cancellationToken)
    {
        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
        try
        {
            await history.InitializeAsync(cancellationToken);
            var scan = await scanner.ScanAsync(systemDrive, progress: null, cancellationToken);
            if (!scan.IsComplete)
            {
                return null;
            }

            var comparison = await history.SaveAsync(history.CreateRecord(scan), cancellationToken);
            var limit = Math.Max(1, settings.Current.GrowthAlertGb) * BytesPerGb;
            if (comparison is null || comparison.NetChange < limit)
            {
                return null;
            }

            var since = comparison.Before.CreatedUtc.ToLocalTime();
            var top = comparison.Grew.OrderByDescending(c => c.Delta).FirstOrDefault();
            var detail = top is null ? "" : $" Most of it is in {top.Path} ({ByteSize.FormatDelta(top.Delta)}).";
            return new DriveAlert(
                $"{systemDrive.TrimEnd('\\')} grew by {ByteSize.Format(comparison.NetChange)}",
                $"Since {since:MMM d}.{detail}");
        }
        catch (Exception exception) when (exception is HistoryStoreException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "The daily growth check couldn't scan or save {Drive}", systemDrive);
            return null;
        }
    }
}
