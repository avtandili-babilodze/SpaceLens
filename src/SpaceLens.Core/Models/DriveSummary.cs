namespace SpaceLens.Core.Models;

public sealed record DriveSummary(string RootPath, string Label, string Format, DriveType Type, long TotalBytes, long FreeBytes)
{
    public long UsedBytes => Math.Max(0, TotalBytes - FreeBytes);

    public double UsedFraction => TotalBytes > 0 ? (double)UsedBytes / TotalBytes : 0;

    public string Letter => RootPath.TrimEnd('\\');

    public string DisplayName => $"{(string.IsNullOrWhiteSpace(Label) ? DefaultLabel : Label)} ({Letter})";

    public string Description => $"{ByteSize.Format(TotalBytes)}  •  {Format}";

    private string DefaultLabel => Type == DriveType.Removable ? "USB Drive" : "Local Disk";

    public override string ToString() => DisplayName;
}
