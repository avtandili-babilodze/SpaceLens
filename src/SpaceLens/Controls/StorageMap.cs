using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Scanning;

namespace SpaceLens.Controls;

/// <summary>
/// A treemap of one folder: every item is a tile whose area matches its size, colored by file type. Folders with room
/// for it show their own contents inside a frame, one level deep. Click selects an item, double-click opens it.
/// </summary>
public sealed class StorageMap : FrameworkElement
{
    public static readonly DependencyProperty FolderProperty = DependencyProperty.Register(
        nameof(Folder), typeof(FolderNode), typeof(StorageMap), new FrameworkPropertyMetadata(null, OnLayoutChanged));

    public static readonly DependencyProperty SelectedNodeProperty = DependencyProperty.Register(
        nameof(SelectedNode), typeof(StorageNode), typeof(StorageMap), new FrameworkPropertyMetadata(null, OnLookChanged));

    public static readonly DependencyProperty SelectCommandProperty = DependencyProperty.Register(
        nameof(SelectCommand), typeof(ICommand), typeof(StorageMap));

    public static readonly DependencyProperty OpenCommandProperty = DependencyProperty.Register(
        nameof(OpenCommand), typeof(ICommand), typeof(StorageMap));

    private const int MaxTiles = 400;
    private const int MaxNestedTiles = 150;
    private const double HeaderHeight = 20;
    private const double MinNestedWidth = 90;
    private const double MinNestedHeight = 64;
    private const double MinLabelWidth = 44;
    private const double MinLabelHeight = 30;

    private static readonly Brush FolderFill = Frozen("#34404D");
    private static readonly Brush FrameFill = Frozen("#1D2228");
    private static readonly Brush FrameText = Frozen("#C9CED4");
    private static readonly Brush LightText = Frozen("#F4F5F7");
    private static readonly Brush DarkText = Frozen("#111418");
    private static readonly Pen Gap = FrozenPen("#14171B", 1);
    private static readonly Pen HoverPen = FrozenPen("#ECEEF0", 1.5);
    private static readonly Pen SelectedPen = FrozenPen("#5C9DFF", 2.5);
    private static readonly Dictionary<FileCategory, Brush> CategoryFills = Enum.GetValues<FileCategory>()
        .ToDictionary(c => c, c => Frozen(FileTypeClassifier.GetCategoryColor(c)));

    private readonly List<Tile> _tiles = [];
    private readonly ToolTip _toolTip;
    private readonly TextBlock _toolTipText = new() { MaxWidth = 460, TextWrapping = TextWrapping.Wrap };
    private bool _isDirty = true;
    private Tile? _hovered;

    public StorageMap()
    {
        _toolTip = new ToolTip { Content = _toolTipText, Placement = PlacementMode.Mouse, PlacementTarget = this };
        Focusable = true;
        FocusVisualStyle = null;
    }

    public FolderNode? Folder
    {
        get => (FolderNode?)GetValue(FolderProperty);
        set => SetValue(FolderProperty, value);
    }

    public StorageNode? SelectedNode
    {
        get => (StorageNode?)GetValue(SelectedNodeProperty);
        set => SetValue(SelectedNodeProperty, value);
    }

    /// <summary>Runs with the clicked <see cref="StorageNode"/>.</summary>
    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    /// <summary>Runs with the double-clicked <see cref="StorageNode"/>.</summary>
    public ICommand? OpenCommand
    {
        get => (ICommand?)GetValue(OpenCommandProperty);
        set => SetValue(OpenCommandProperty, value);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        // A transparent background makes the whole area respond to the mouse.
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        if (_isDirty)
        {
            BuildTiles();
            _isDirty = false;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(TextElement.GetFontFamily(this), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        var bold = new Typeface(typeface.FontFamily, FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
        foreach (var tile in _tiles)
        {
            if (tile.IsFrame)
            {
                DrawFrame(drawingContext, tile, bold, dpi);
            }
            else
            {
                DrawTile(drawingContext, tile, typeface, bold, dpi);
            }
        }

        if (SelectedNode is { } selected && _tiles.FirstOrDefault(t => ReferenceEquals(t.Node, selected)) is { } selectedTile)
        {
            drawingContext.DrawRectangle(null, SelectedPen, Inset(selectedTile.Bounds, 1.5));
        }

        if (_hovered is not null)
        {
            drawingContext.DrawRectangle(null, HoverPen, Inset(_hovered.Bounds, 1));
        }
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _isDirty = true;
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var tile = HitTest(e.GetPosition(this));
        if (ReferenceEquals(tile, _hovered))
        {
            return;
        }

        _hovered = tile;
        _toolTip.IsOpen = false;
        if (tile is not null)
        {
            _toolTipText.Text = Describe(tile.Node);
            _toolTip.IsOpen = true;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        _hovered = null;
        _toolTip.IsOpen = false;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (HitTest(e.GetPosition(this)) is not { } tile)
        {
            return;
        }

        var command = e.ClickCount >= 2 ? OpenCommand : SelectCommand;
        _toolTip.IsOpen = false;
        if (command?.CanExecute(tile.Node) == true)
        {
            command.Execute(tile.Node);
        }

        e.Handled = true;
    }

    /// <summary>Selects what's under the mouse first, so the context menu acts on it.</summary>
    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        _toolTip.IsOpen = false;
        if (HitTest(e.GetPosition(this)) is { } tile && SelectCommand?.CanExecute(tile.Node) == true)
        {
            SelectCommand.Execute(tile.Node);
        }
    }

    private static void OnLayoutChanged(DependencyObject element, DependencyPropertyChangedEventArgs e)
    {
        var map = (StorageMap)element;
        map._isDirty = true;
        map._hovered = null;
        map.InvalidateVisual();
    }

    private static void OnLookChanged(DependencyObject element, DependencyPropertyChangedEventArgs e) =>
        ((StorageMap)element).InvalidateVisual();

    private static Brush Frozen(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(string color, double thickness)
    {
        var pen = new Pen(Frozen(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Brush FillFor(StorageNode node) => node switch
    {
        FileNode file => CategoryFills[FileTypeClassifier.GetCategory(FileTypeClassifier.GetKind(file.Extension))],
        _ => FolderFill
    };

    private static bool IsLight(Brush brush) =>
        brush is SolidColorBrush { Color: var c } && (0.299 * c.R) + (0.587 * c.G) + (0.114 * c.B) > 150;

    private static Rect Inset(Rect rect, double amount) =>
        rect.Width > amount * 2 && rect.Height > amount * 2
            ? new Rect(rect.X + amount, rect.Y + amount, rect.Width - (amount * 2), rect.Height - (amount * 2))
            : rect;

    private static string Describe(StorageNode node)
    {
        var kind = node is FolderNode folder ? $"Folder · {folder.FileCount:N0} files" : FileTypeClassifier.GetTypeName(node);
        return $"{node.Name}\n{ByteSize.Format(node.Size)} · {kind}\n{node.FullPath}";
    }

    private static List<StorageNode> Largest(FolderNode folder, int limit) =>
        folder.Children.Where(c => c.Size > 0).OrderByDescending(c => c.Size).Take(limit).ToList();

    private FormattedText Text(string text, Typeface typeface, double size, Brush brush, double maxWidth, double dpi) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection, typeface, size, brush, dpi)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

    private void BuildTiles()
    {
        _tiles.Clear();
        if (Folder is not { } folder || ActualWidth < 4 || ActualHeight < 4)
        {
            return;
        }

        var children = Largest(folder, MaxTiles);
        var rects = SquarifiedLayout.Arrange(children.Select(c => (double)c.Size).ToList(), new Rect(RenderSize));
        for (var i = 0; i < children.Count; i++)
        {
            var node = children[i];
            var rect = rects[i];
            if (rect.Width < 1 || rect.Height < 1)
            {
                continue;
            }

            var canNest = node is FolderNode { IsLink: false, ChildCount: > 0 }
                && rect.Width >= MinNestedWidth
                && rect.Height >= MinNestedHeight;
            if (!canNest)
            {
                _tiles.Add(new Tile(node, rect, FillFor(node), IsFrame: false));
                continue;
            }

            // Frames come before their contents, so drawing paints the contents on top and hit-testing (newest first) finds them.
            _tiles.Add(new Tile(node, rect, FrameFill, IsFrame: true));
            var inner = new Rect(rect.X + 2, rect.Y + HeaderHeight, rect.Width - 4, rect.Height - HeaderHeight - 2);
            var nested = Largest((FolderNode)node, MaxNestedTiles);
            var nestedRects = SquarifiedLayout.Arrange(nested.Select(c => (double)c.Size).ToList(), inner);
            for (var j = 0; j < nested.Count; j++)
            {
                if (nestedRects[j].Width >= 1 && nestedRects[j].Height >= 1)
                {
                    _tiles.Add(new Tile(nested[j], nestedRects[j], FillFor(nested[j]), IsFrame: false));
                }
            }
        }
    }

    private Tile? HitTest(Point point)
    {
        for (var i = _tiles.Count - 1; i >= 0; i--)
        {
            if (_tiles[i].Bounds.Contains(point))
            {
                return _tiles[i];
            }
        }

        return null;
    }

    private void DrawFrame(DrawingContext drawingContext, Tile tile, Typeface bold, double dpi)
    {
        var rect = tile.Bounds;
        drawingContext.DrawRectangle(tile.Fill, Gap, rect);
        var label = $"{tile.Node.Name}  ·  {ByteSize.Format(tile.Node.Size)}";
        var text = Text(label, bold, 11, FrameText, rect.Width - 10, dpi);
        drawingContext.DrawText(text, new Point(rect.X + 6, rect.Y + ((HeaderHeight - text.Height) / 2)));
    }

    private void DrawTile(DrawingContext drawingContext, Tile tile, Typeface typeface, Typeface bold, double dpi)
    {
        var rect = tile.Bounds;
        drawingContext.DrawRectangle(tile.Fill, rect.Width > 3 && rect.Height > 3 ? Gap : null, rect);
        if (rect.Width < MinLabelWidth || rect.Height < MinLabelHeight)
        {
            return;
        }

        var foreground = IsLight(tile.Fill) ? DarkText : LightText;
        var name = Text(tile.Node.Name, bold, 11.5, foreground, rect.Width - 10, dpi);
        drawingContext.DrawText(name, new Point(rect.X + 5, rect.Y + 4));
        if (rect.Height >= MinLabelHeight + 12)
        {
            var size = Text(ByteSize.Format(tile.Node.Size), typeface, 10.5, foreground, rect.Width - 10, dpi);
            drawingContext.PushOpacity(0.8);
            drawingContext.DrawText(size, new Point(rect.X + 5, rect.Y + 5 + name.Height));
            drawingContext.Pop();
        }
    }

    private sealed record Tile(StorageNode Node, Rect Bounds, Brush Fill, bool IsFrame);
}
