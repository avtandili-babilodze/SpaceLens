using System.Windows;
using System.Windows.Media;
using SpaceLens.ViewModels;

namespace SpaceLens.Controls;

/// <summary>
/// The drive drawn as one bar, like the drive bar in File Explorer: each segment is as wide as its share of the
/// drive's capacity, in order, and the track left over is free space.
/// </summary>
public sealed class CapacityBar : FrameworkElement
{
    /// <summary>Track-coloured space between segments, so neighbours with similar colours stay apart.</summary>
    private const double Gap = 2;

    public static readonly DependencyProperty SegmentsProperty = DependencyProperty.Register(
        nameof(Segments), typeof(IReadOnlyList<CapacitySegment>), typeof(CapacityBar),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(CapacityBar),
        new FrameworkPropertyMetadata(Brushes.DimGray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.Register(
        nameof(CornerRadius), typeof(double), typeof(CapacityBar),
        new FrameworkPropertyMetadata(4d, FrameworkPropertyMetadataOptions.AffectsRender));

    private static readonly Dictionary<string, Brush> Fills = [];

    public IReadOnlyList<CapacitySegment>? Segments
    {
        get => (IReadOnlyList<CapacitySegment>?)GetValue(SegmentsProperty);
        set => SetValue(SegmentsProperty, value);
    }

    public Brush TrackBrush
    {
        get => (Brush)GetValue(TrackBrushProperty);
        set => SetValue(TrackBrushProperty, value);
    }

    public double CornerRadius
    {
        get => (double)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    protected override void OnRender(DrawingContext context)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        context.PushClip(new RectangleGeometry(bounds, CornerRadius, CornerRadius));
        context.DrawRectangle(TrackBrush, null, bounds);

        var x = 0.0;
        foreach (var segment in Segments ?? [])
        {
            var right = Math.Min(bounds.Width, x + Math.Max(0, segment.Fraction) * bounds.Width);
            var width = right - x - Gap;
            if (width >= 1)
            {
                context.DrawRectangle(FillFor(segment.Color), null, new Rect(x, 0, width, bounds.Height));
            }

            x = right;
        }

        context.Pop();
    }

    private static Brush FillFor(string color)
    {
        if (!Fills.TryGetValue(color, out var brush))
        {
            brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            brush.Freeze();
            Fills[color] = brush;
        }

        return brush;
    }
}
