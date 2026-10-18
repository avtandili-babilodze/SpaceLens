using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SpaceLens.Infrastructure;

/// <summary>Loads the icon an app shows in Settings › Apps from its "path,index" DisplayIcon value.</summary>
public static class AppIconLoader
{
    /// <summary>The icon, or null when the file is gone or has no icon. Call on the UI thread.</summary>
    public static ImageSource? Load(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return null;
        }

        var path = Environment.ExpandEnvironmentVariables(iconPath.Trim()).Trim('"');
        var index = 0;
        var comma = path.LastIndexOf(',');
        if (comma > 0 && int.TryParse(path[(comma + 1)..], out var parsed))
        {
            index = parsed;
            path = path[..comma].Trim().Trim('"');
        }

        if (!System.IO.File.Exists(path))
        {
            return null;
        }

        var large = new IntPtr[1];
        if (ExtractIconExW(path, index, large, null, 1) == 0 || large[0] == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var image = Imaging.CreateBitmapSourceFromHIcon(large[0], Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            image.Freeze();
            return image;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            DestroyIcon(large[0]);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern uint ExtractIconExW(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);
}
