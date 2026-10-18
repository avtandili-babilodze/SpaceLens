using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.Apps;

public interface IInstalledAppsService
{
    /// <summary>The programs Windows lists under Settings › Apps (desktop programs; Store apps aren't included).</summary>
    IReadOnlyList<InstalledApp> GetInstalledApps();
}
