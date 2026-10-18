using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Apps;

namespace SpaceLens.Tests.Support;

/// <summary>A PC with no installed apps, so tests never read the real registry.</summary>
public sealed class NoInstalledApps : IInstalledAppsService
{
    public IReadOnlyList<InstalledApp> GetInstalledApps() => [];
}
