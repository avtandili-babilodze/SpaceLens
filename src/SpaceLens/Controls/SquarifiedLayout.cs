using System.Windows;

namespace SpaceLens.Controls;

/// <summary>
/// Squarified treemap layout (Bruls, Huizing and van Wijk): fills a rectangle with one rectangle per weight, each
/// with an area proportional to its weight and as close to square as the order allows. Weights must be sorted
/// largest first; zero weights get empty rectangles.
/// </summary>
public static class SquarifiedLayout
{
    public static Rect[] Arrange(IReadOnlyList<double> weights, Rect bounds)
    {
        var result = new Rect[weights.Count];
        var total = weights.Sum();
        if (total <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return result;
        }

        var scale = bounds.Width * bounds.Height / total;
        var areas = weights.Select(w => Math.Max(0, w) * scale).ToArray();
        var remaining = bounds;
        var start = 0;
        while (start < areas.Length && remaining.Width > 0 && remaining.Height > 0)
        {
            // Grow the row while that keeps its worst aspect ratio from getting worse.
            var side = Math.Min(remaining.Width, remaining.Height);
            var end = start + 1;
            var rowSum = areas[start];
            while (end < areas.Length
                && Worst(areas, start, end + 1, rowSum + areas[end], side) <= Worst(areas, start, end, rowSum, side))
            {
                rowSum += areas[end];
                end++;
            }

            remaining = PlaceRow(areas, start, end, rowSum, remaining, result);
            start = end;
        }

        return result;
    }

    /// <summary>The largest aspect ratio in a row laid along a side of the given length.</summary>
    private static double Worst(double[] areas, int start, int end, double sum, double side)
    {
        if (sum <= 0)
        {
            return double.MaxValue;
        }

        var largest = 0.0;
        var smallest = double.MaxValue;
        for (var i = start; i < end; i++)
        {
            largest = Math.Max(largest, areas[i]);
            smallest = Math.Min(smallest, areas[i]);
        }

        var sideSquared = side * side;
        var sumSquared = sum * sum;
        return smallest <= 0
            ? double.MaxValue
            : Math.Max(sideSquared * largest / sumSquared, sumSquared / (sideSquared * smallest));
    }

    /// <summary>Lays a row along the shorter side of <paramref name="bounds"/> and returns the space left over.</summary>
    private static Rect PlaceRow(double[] areas, int start, int end, double sum, Rect bounds, Rect[] result)
    {
        if (bounds.Width >= bounds.Height)
        {
            // A column on the left.
            var width = Math.Min(bounds.Width, sum / bounds.Height);
            var y = bounds.Top;
            for (var i = start; i < end; i++)
            {
                var height = sum > 0 ? areas[i] / sum * bounds.Height : 0;
                result[i] = new Rect(bounds.Left, y, width, height);
                y += height;
            }

            return new Rect(bounds.Left + width, bounds.Top, Math.Max(0, bounds.Width - width), bounds.Height);
        }

        // A row along the top.
        var rowHeight = Math.Min(bounds.Height, sum / bounds.Width);
        var x = bounds.Left;
        for (var i = start; i < end; i++)
        {
            var width = sum > 0 ? areas[i] / sum * bounds.Width : 0;
            result[i] = new Rect(x, bounds.Top, width, rowHeight);
            x += width;
        }

        return new Rect(bounds.Left, bounds.Top + rowHeight, bounds.Width, Math.Max(0, bounds.Height - rowHeight));
    }
}
