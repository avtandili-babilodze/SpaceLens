namespace SpaceLens.Core.Models;

/// <summary>
/// Space Windows itself uses and manages. <see cref="Bytes"/> is null when Windows doesn't let a normal user
/// measure it; <see cref="Exists"/> is false when the feature is off (no hibernation file, no previous Windows).
/// </summary>
public sealed record SystemSpaceItem(SystemSpaceKind Kind, long? Bytes, bool Exists);
