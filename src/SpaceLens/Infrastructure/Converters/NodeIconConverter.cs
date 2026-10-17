using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SpaceLens.ViewModels;

namespace SpaceLens.Infrastructure.Converters;

/// <summary>
/// Maps a <see cref="NodeIcon"/> to its glyph (Segoe Fluent Icons), its colour, or whether it uses the folder shape.
/// The parameter picks the output: "Glyph", "Brush" or "FolderVisibility" / "GlyphVisibility".
/// </summary>
public sealed class NodeIconConverter : IValueConverter
{
    private static readonly Dictionary<NodeIcon, (string Glyph, string Color)> Visuals = new()
    {
        [NodeIcon.Folder] = ("", "#F5C451"),
        [NodeIcon.Drive] = ("\uEDA2", "#B7C6DA"),
        [NodeIcon.UserFolder] = ("\uE77B", "#4EA1FF"),
        [NodeIcon.Desktop] = ("\uE7F4", "#4EA1FF"),
        [NodeIcon.Documents] = ("\uE8A5", "#6FB1FF"),
        [NodeIcon.Downloads] = ("\uE896", "#4EA1FF"),
        [NodeIcon.Pictures] = ("\uEB9F", "#38BDF8"),
        [NodeIcon.Videos] = ("\uE714", "#A78BFA"),
        [NodeIcon.Music] = ("\uEC4F", "#F472B6"),
        [NodeIcon.Link] = ("\uE71B", "#8FA3BF"),
        [NodeIcon.Document] = ("\uE8A5", "#7DB4F5"),
        [NodeIcon.Pdf] = ("\uE8A5", "#F87171"),
        [NodeIcon.Spreadsheet] = ("\uE80A", "#34D399"),
        [NodeIcon.Presentation] = ("\uE8A5", "#FB923C"),
        [NodeIcon.Image] = ("\uEB9F", "#38BDF8"),
        [NodeIcon.Video] = ("\uE714", "#A78BFA"),
        [NodeIcon.Audio] = ("\uEC4F", "#F472B6"),
        [NodeIcon.Archive] = ("\uF012", "#F5C451"),
        [NodeIcon.DiskImage] = ("\uE958", "#B7C6DA"),
        [NodeIcon.Application] = ("\uECAA", "#4EA1FF"),
        [NodeIcon.Code] = ("\uE943", "#34D399"),
        [NodeIcon.Database] = ("\uEE94", "#2DD4BF"),
        [NodeIcon.System] = ("\uE9F5", "#94A3B8"),
        [NodeIcon.CloudFile] = ("\uEBD3", "#7DD3FC"),
        [NodeIcon.File] = ("\uE7C3", "#94A3B8")
    };

    private static readonly ColorToBrushConverter Brushes = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var icon = value is NodeIcon nodeIcon ? nodeIcon : NodeIcon.File;
        var (glyph, color) = Visuals.GetValueOrDefault(icon, ("\uE7C3", "#94A3B8"));
        var isFolderShape = icon == NodeIcon.Folder;

        return (parameter as string) switch
        {
            "Brush" => Brushes.Convert(color, typeof(Brush), null, culture),
            "FolderVisibility" => isFolderShape ? Visibility.Visible : Visibility.Collapsed,
            "GlyphVisibility" => isFolderShape ? Visibility.Collapsed : Visibility.Visible,
            _ => glyph
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
