using System.IO;
using SpaceLens.Infrastructure;
using SpaceLens.Models;

namespace SpaceLens.Services.Cleanup;

public interface ICleanupRiskAnalyzer
{
    (CleanupRisk Risk, string Reason) Classify(string path, bool directory, bool reparsePoint);
}

public sealed class CleanupRiskAnalyzer(AppPaths appPaths) : ICleanupRiskAnalyzer
{
    public (CleanupRisk Risk, string Reason) Classify(string path, bool directory, bool reparsePoint)
    {
        string normalized = path.Replace('/', '\\');
        var parts = normalized.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (reparsePoint || parts.Any(p => new[] { "Windows", "Program Files", "Program Files (x86)", "ProgramData", "System Volume Information", "$Recycle.Bin", "Recovery", ".git" }.Contains(p, StringComparer.OrdinalIgnoreCase))
            || new[] { "pagefile.sys", "swapfile.sys", "hiberfil.sys", "bootmgr" }.Contains(Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            || normalized.StartsWith(appPaths.Root + "\\", StringComparison.OrdinalIgnoreCase))
            return (CleanupRisk.Protected, "System, application, repository, or linked data. Use the owning application's supported storage tools.");
        if (directory)
            return (CleanupRisk.ReviewRequired, "Folder contents may include personal or application data. Review individual files; this folder is not a safe deletion target.");
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (parts.Any(p => new[] { "AppData", "node_modules", ".venv", "venv" }.Contains(p, StringComparer.OrdinalIgnoreCase))
            || new[] { ".dll", ".sys", ".reg", ".vhd", ".vhdx", ".db", ".sqlite", ".pst", ".ost" }.Contains(extension))
            return (CleanupRisk.HighRisk, "May be required application state, a development environment, a database, or a virtual disk. Removing it can break software or lose data.");
        if (new[] { ".exe", ".msi", ".msix", ".zip", ".7z", ".rar", ".iso" }.Contains(extension))
            return (CleanupRisk.ReviewRequired, "Possible installer or archive. Confirm it is replaceable and contains no unique files before removing it.");
        return (CleanupRisk.ReviewRequired, "Purpose and backup status are unknown. Inspect this file and confirm you no longer need it. Size alone does not make a file unnecessary.");
    }
}
