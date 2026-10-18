namespace SpaceLens.ViewModels;

/// <summary>One part of the drive's capacity bar: a large folder, the rest of the scan, or used space the scan didn't see.</summary>
public sealed record CapacitySegment(string Label, string SizeText, double Fraction, string Color);
