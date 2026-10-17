using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Cleanup;

public interface ICleanupRiskAnalyzer
{
    /// <summary>Classifies how risky it is to remove an item, with a reason the user can act on.</summary>
    RiskAssessment Assess(StorageNode node);

    /// <summary>
    /// Items inside <paramref name="folder"/> that hold data which can't be recreated (repositories, databases,
    /// mailboxes, virtual disks), up to <paramref name="limit"/>. Used before a whole folder is recycled.
    /// </summary>
    IReadOnlyList<StorageNode> FindIrreplaceableContents(FolderNode folder, int limit);
}
