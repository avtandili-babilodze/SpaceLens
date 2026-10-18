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

    /// <summary>Measured from the scan or on disk when the app's folder is known, otherwise what the installer reported.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText), nameof(SizeTooltip))]
    private long _sizeBytes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SizeText), nameof(SizeTooltip))]
    private bool _isMeasuring;

    private bool _isMeasured;
    private bool _isMeasuredOnDisk;

    public AppItemViewModel(InstalledApp app, long? measuredBytes, DateTime nowUtc)
    {
        App = app;
        _sizeBytes = measuredBytes ?? app.SizeBytes ?? 0;
        _isMeasured = measuredBytes is not null;
        IsUnused = app.LastUsedUtc is { } lastUsed && nowUtc - lastUsed > UnusedAfter;
        LastUsedText = app.LastUsedUtc is { } used ? DescribeAge(nowUtc - used) : "Not recorded";
    }

    public InstalledApp App { get; }

    public string Name => App.Name;

    public string Details => string.Join("  ·  ", new[] { App.Publisher, App.Version }.Where(t => t.Length > 0));

    /// <summary>Neither the scan nor the installer gave a size, but the app's folder is known, so it can be measured on disk.</summary>
    public bool NeedsMeasuring => !_isMeasured && SizeBytes == 0 && App.InstallFolder is not null;

    public string SizeText => IsMeasuring ? "Measuring…" : SizeBytes > 0 ? ByteSize.Format(SizeBytes) : "—";

    public string SizeTooltip
    {
        get
        {
            if (IsMeasuring)
            {
                return $"Adding up the files in {App.InstallFolder}…";
            }

            if (!_isMeasured)
            {
                return SizeBytes > 0
                    ? "Size the installer reported to Windows; it can be out of date."
                    : "Windows has no size or install folder on record for this app.";
            }

            var source = _isMeasuredOnDisk ? $"Measured from {App.InstallFolder} on disk." : $"Measured from your scan of {App.InstallFolder}.";
            return App.IsInstallFolderGuessed
                ? source + " The installer didn't record where the app is, so this is the folder its uninstaller and program are in."
                : source;
        }
    }

    /// <summary>Shows the size of the app's folder measured on disk; nothing changes when the folder couldn't be read.</summary>
    public void ShowMeasuredSize(long? bytes)
    {
        if (bytes is { } measured)
        {
            _isMeasured = true;
            _isMeasuredOnDisk = true;
            SizeBytes = measured;
        }

        IsMeasuring = false;
    }

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
        < 60 => "1 month ago",
        < 365 => $"{(int)(age.TotalDays / 30)} months ago",
        _ => $"{age.TotalDays / 365:0.#} years ago"
    };
}
