using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Scanning;

namespace SpaceLens.ViewModels;

/// <summary>A file or folder row in a table or list.</summary>
public sealed partial class StorageItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isChecked;

    public StorageItemViewModel(StorageNode node, NodePresenter presenter, DriveSummary? drive = null, long? parentSize = null)
    {
        Node = node;
        Name = NodePresenter.GetDisplayName(node, drive);
        Icon = presenter.GetIcon(node);
        TypeName = FileTypeClassifier.GetTypeName(node);
        var assessment = presenter.Assess(node);
        Risk = assessment.Risk;
        RiskReason = assessment.Reason;
        var total = parentSize ?? node.Parent?.Size ?? 0;
        ShareOfParent = total > 0 ? Math.Clamp((double)node.Size / total, 0, 1) : 0;
    }

    public StorageNode Node { get; }

    public string Name { get; }

    public string FullPath => Node.FullPath;

    /// <summary>The containing folder, shown for search results that span many folders.</summary>
    public string Location => Path.GetDirectoryName(Node.FullPath) ?? "";

    public bool IsFolder => Node.IsFolder;

    public long Size => Node.Size;

    public string SizeText => ByteSize.Format(Node.Size);

    public string TypeName { get; }

    public DateTime Modified => Node.LastWriteUtc.ToLocalTime();

    public string ModifiedText => Node.LastWriteUtc.Year < 1981 ? "" : Modified.ToString("MMM d, yyyy");

    public NodeIcon Icon { get; }

    public CleanupRisk Risk { get; }

    public string RiskLabel => Risk.ToLabel();

    /// <summary>Compact badge text for tables; the full label and reason are in the tooltip.</summary>
    public string RiskShortLabel => Risk switch
    {
        CleanupRisk.Low => "Low",
        CleanupRisk.Review => "Review",
        CleanupRisk.High => "High",
        _ => "Protected"
    };

    public string RiskTooltip => $"{RiskLabel}: {RiskReason}";

    public string RiskReason { get; }

    public bool CanRemove => Risk is CleanupRisk.Low or CleanupRisk.Review;

    public double ShareOfParent { get; }

    public string ShareText => ShareOfParent >= 0.001 ? $"{ShareOfParent:P0}" : "<1%";
}
