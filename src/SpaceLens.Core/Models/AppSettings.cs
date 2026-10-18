namespace SpaceLens.Core.Models;

/// <summary>Settings that are not secret. The API key lives separately, encrypted with DPAPI.</summary>
public sealed record AppSettings
{
    public const string DefaultModel = "claude-haiku-5-5";

    /// <summary>The user agreed to send folder and file names, paths, sizes and dates to Anthropic.</summary>
    public bool ShareScanDetails { get; init; }

    public string Model { get; init; } = DefaultModel;

    /// <summary>Folders the user never wants suggested for cleanup; everything inside them counts as protected.</summary>
    public IReadOnlyList<string> ExcludedFolders { get; init; } = [];

    /// <summary>Alert when a drive has less than this share of its space free.</summary>
    public int LowSpacePercent { get; init; } = 10;

    /// <summary>Alert when the system drive grew by at least this much since the previous daily check.</summary>
    public int GrowthAlertGb { get; init; } = 5;
}
