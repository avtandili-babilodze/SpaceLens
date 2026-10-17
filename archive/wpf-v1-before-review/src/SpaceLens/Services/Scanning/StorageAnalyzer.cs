using System.IO;

namespace SpaceLens.Services.Scanning;

public static class FileCategories
{
    public static string Get(string extension) => extension.ToLowerInvariant() switch
    {
        ".mp4" or ".mov" or ".mkv" or ".avi" or ".webm" => "Video",
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".heic" or ".svg" or ".raw" => "Images",
        ".mp3" or ".wav" or ".flac" or ".ogg" or ".m4a" => "Audio",
        ".zip" or ".7z" or ".rar" or ".gz" or ".tar" or ".iso" => "Archives",
        ".exe" or ".msi" or ".msix" or ".dll" or ".sys" => "Applications",
        ".pdf" or ".doc" or ".docx" or ".txt" or ".xlsx" or ".pptx" or ".csv" or ".md" => "Documents",
        ".cs" or ".xaml" or ".rs" or ".ts" or ".tsx" or ".js" or ".py" or ".go" or ".cpp" or ".json" => "Development",
        _ => "Other"
    };
    public static string Color(string category) => category switch
    {
        "Applications" => "#4D8EF7",
        "Video" => "#A18AE5",
        "Images" => "#6FB4DE",
        "Audio" => "#D89A73",
        "Archives" => "#76A6D9",
        "Documents" => "#A2B8D4",
        "Development" => "#63B5A1",
        _ => "#61718A"
    };
}
