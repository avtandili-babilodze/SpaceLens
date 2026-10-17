using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>One step of the path shown above the item table.</summary>
public sealed record Breadcrumb(string Name, FolderNode Folder);
