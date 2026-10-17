using System.Text.Json;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Session;

namespace SpaceLens.Core.Services.AI;

/// <summary>
/// Picks the small slice of scan metadata Claude needs to start answering: totals, the selected item, the biggest
/// folders and files, cleanup groups and recent growth. Claude asks for more through tools, so the full
/// inventory never leaves the PC. No file contents are ever included.
/// </summary>
public sealed class AiContextBuilder(IScanSession session, ICleanupRiskAnalyzer riskAnalyzer)
{
    private const int ListLimit = 10;

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<string> BuildAsync(StorageNode? focus, CancellationToken cancellationToken)
    {
        var cleanup = session.Cleanup;
        var growth = session.Growth;
        // Serialize inside the read so every lazy query runs while the tree can't change.
        return await session.ReadAsync(scan => JsonSerializer.Serialize(new
        {
            Scan = DescribeScan(scan),
            Drive = scan.Drive is { } drive
                ? new
                {
                    Name = drive.DisplayName,
                    Capacity = Size(drive.TotalBytes),
                    Used = Size(drive.UsedBytes),
                    Free = Size(drive.FreeBytes),
                    UsedPercent = Math.Round(drive.UsedFraction * 100)
                }
                : null,
            Selected = focus is null || ReferenceEquals(focus, scan.Root) ? null : DescribeItem(focus, includeChildren: true),
            LargestInScan = scan.Root.Children
                .OrderByDescending(c => c.Size)
                .Take(ListLimit)
                .Select(c => DescribeItem(c, includeChildren: false)),
            LargestFiles = StorageAnalyzer.LargestFiles(scan.Root, ListLimit).Select(f => DescribeItem(f, includeChildren: false)),
            CleanupOpportunities = cleanup.Opportunities.Select(o => new
            {
                o.Title,
                Risk = o.Risk.ToLabel(),
                Size = Size(o.Bytes),
                Items = o.ItemCount,
                HowToClean = o.Action switch
                {
                    CleanupAction.EmptyRecycleBin => "empty the Recycle Bin",
                    CleanupAction.OpenStorageSettings => "Windows Settings › System › Storage",
                    _ => "review and move to the Recycle Bin in SpaceLens"
                }
            }),
            SincePreviousScan = growth is null
                ? null
                : new
                {
                    PreviousScan = growth.Before.CreatedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    NetChange = ByteSize.FormatDelta(growth.NetChange),
                    Grew = growth.Grew.Take(5).Select(c => new { c.Path, Change = ByteSize.FormatDelta(c.Delta) }),
                    NewLargeFiles = growth.NewLargeFiles.Take(5).Select(f => new { f.Path, Size = Size(f.AfterBytes) })
                }
        }, JsonOptions), cancellationToken);
    }

    internal static string Size(long bytes) => ByteSize.Format(bytes);

    private static object DescribeScan(ScanResult scan) => new
    {
        Root = scan.RootPath,
        ScannedAt = scan.CompletedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
        Complete = scan.IsComplete,
        TotalSize = Size(scan.Root.Size),
        Files = scan.Root.FileCount,
        Folders = scan.Root.FolderCount,
        UnreadableFolders = scan.UnreadableFolderCount,
        LinksNotFollowed = scan.SkippedLinkCount,
        UsedButNotScanned = scan.UnaccountedBytes > 0 ? Size(scan.UnaccountedBytes) : null
    };

    private object DescribeItem(StorageNode node, bool includeChildren)
    {
        var risk = riskAnalyzer.Assess(node);
        var folder = node as FolderNode;
        return new
        {
            Path = node.FullPath,
            Type = FileTypeClassifier.GetTypeName(node),
            Size = Size(node.Size),
            Modified = node.LastWriteUtc.ToLocalTime().ToString("yyyy-MM-dd"),
            Risk = risk.Label,
            RiskReason = includeChildren ? risk.Reason : null,
            Files = folder?.FileCount,
            Unreadable = folder?.IsUnreadable == true ? true : (bool?)null,
            LargestChildren = includeChildren && folder is not null
                ? folder.Children.OrderByDescending(c => c.Size).Take(ListLimit).Select(c => new
                {
                    c.Name,
                    Type = FileTypeClassifier.GetTypeName(c),
                    Size = Size(c.Size),
                    Risk = riskAnalyzer.Assess(c).Label
                })
                : null
        };
    }
}
