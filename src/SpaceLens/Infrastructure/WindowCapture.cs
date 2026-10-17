using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpaceLens.Infrastructure;

/// <summary>Renders a window to a PNG at its real DPI (used by <c>--capture</c> for screenshots and UI checks).</summary>
public static class WindowCapture
{
    public static void SaveAsPng(Window window, string path)
    {
        window.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(window);
        var bitmap = new RenderTargetBitmap(
            (int)(window.ActualWidth * dpi.DpiScaleX),
            (int)(window.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX,
            dpi.PixelsPerInchY,
            PixelFormats.Pbgra32);
        bitmap.Render(window);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
