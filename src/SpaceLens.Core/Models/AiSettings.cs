namespace SpaceLens.Core.Models;

/// <summary>Settings that are not secret. The API key lives separately, encrypted with DPAPI.</summary>
public sealed record AiSettings
{
    public const string DefaultModel = "claude-haiku-5-5";

    /// <summary>The user agreed to send folder and file names, paths, sizes and dates to Anthropic.</summary>
    public bool ShareScanDetails { get; init; }

    public string Model { get; init; } = DefaultModel;
}
