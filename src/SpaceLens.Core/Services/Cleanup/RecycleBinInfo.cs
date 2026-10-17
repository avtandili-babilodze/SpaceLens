namespace SpaceLens.Core.Services.Cleanup;

/// <summary>How much the Recycle Bin of one drive holds.</summary>
public readonly record struct RecycleBinInfo(long Bytes, long ItemCount);
