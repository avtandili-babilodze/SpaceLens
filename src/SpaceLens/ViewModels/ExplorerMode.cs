namespace SpaceLens.ViewModels;

/// <summary>What the item table is listing.</summary>
public enum ExplorerMode
{
    /// <summary>The contents of the current folder.</summary>
    Folder,

    /// <summary>Name matches from the whole scan.</summary>
    Search,

    /// <summary>The largest files anywhere below the current folder.</summary>
    LargestFiles
}
