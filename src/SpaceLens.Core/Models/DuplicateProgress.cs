namespace SpaceLens.Core.Models;

/// <summary>A progress report from the duplicate search.</summary>
public readonly record struct DuplicateProgress(int Hashed, int Total, int Skipped);
