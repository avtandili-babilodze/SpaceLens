using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>One row on the Apps page.</summary>
public sealed partial class AppItemViewModel : ObservableObject
{
    /// <summary>Apps not started for this long are called out as unused.</summary>
    public static readonly TimeSpan UnusedAfter = TimeSpan.FromDays(180);

    [ObservableProperty]
    private ImageSource? _icon;

    public AppItemViewModel(InstalledApp app, long? measuredBytes, DateTime nowUtc)
    {
        App = app;
        SizeBytes = measuredBytes ?? app.SizeBytes ?? 0;
        IsMeasured = measuredBytes is not null;
        IsUnused = app.LastUsedUtc is { } lastUsed && nowUtc - lastUsed > UnusedAfter;
        LastUsedText = app.LastUsedUtc is { } used ? DescribeAge(nowUtc - used) : "Not recorded";
    }

    public InstalledApp App { get; }

    public string Name => App.Name;

    public string Details => string.Join("  ·  ", new[] { App.Publisher, App.Version }.Where(t => t.Length > 0));

    /// <summary>Measured from the scan when the app's folder is in it, otherwise what the installer reported.</summary>
    public long SizeBytes { get; }

    public bool IsMeasured { get; }

    public string SizeText => SizeBytes > 0 ? ByteSize.Format(SizeBytes) : "—";

    public string SizeTooltip => IsMeasured
        ? "Measured from your scan of the app's folder."
        : SizeBytes > 0 ? "Size the installer reported to Windows; it can be out of date." : "The installer didn't report a size.";

    /// <summary>For sorting; apps with no record sort as the least recently used.</summary>
    public DateTime LastUsed => App.LastUsedUtc ?? DateTime.MinValue;

    public string LastUsedText { get; }

    public bool IsUnused { get; }

    public string InstalledText => App.InstalledOn is { } date ? date.ToString("MMM d, yyyy") : "—";

    public DateTime Installed => App.InstalledOn ?? DateTime.MinValue;

    public bool CanUninstall => App.CanUninstall;

    private static string DescribeAge(TimeSpan age) => age.TotalDays switch
    {
        < 1 => "Today",
        < 2 => "Yesterday",
        < 30 => $"{(int)age.TotalDays} days ago",
        < 365 => $"{(int)(age.TotalDays / 30)} months ago",
        _ => $"{age.TotalDays / 365:0.#} years ago"
    };
}
