using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels.Messages;

/// <summary>List the largest files anywhere below a folder.</summary>
public sealed record ShowLargestFilesMessage(FolderNode Folder);
