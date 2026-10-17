using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels.Messages;

/// <summary>Open the explorer at a node (its folder, or the file's folder with the file selected).</summary>
public sealed record ShowInExplorerMessage(StorageNode Node);
