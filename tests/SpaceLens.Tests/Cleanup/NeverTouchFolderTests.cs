using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Tests.Support;

namespace SpaceLens.Tests.Cleanup;

public sealed class NeverTouchFolderTests
{
    private const string Cache = @"C:\Users\alex\AppData\Local\Google\Chrome\User Data\Default\Cache";

    [Fact]
    public void ItemsInsideANeverTouchFolderAreProtected()
    {
        var root = TestData.TypicalDrive();
        var settings = new MemorySettingsStore();
        settings.Save(settings.Current with { ExcludedFolders = [@"C:\Users\alex\AppData\Local\Google"] });
        var analyzer = new CleanupRiskAnalyzer(TestData.Catalog, TestData.Clock, settings);

        var assessment = analyzer.Assess(root.Find(Cache)!);

        Assert.Equal(CleanupRisk.Protected, assessment.Risk);
        Assert.Contains("Never-touch", assessment.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void ANeverTouchFolderLeavesTheCleanupGroups()
    {
        var settings = new MemorySettingsStore();
        var analyzer = new CleanupRiskAnalyzer(TestData.Catalog, TestData.Clock, settings);
        var cleanup = new CleanupAnalyzer(TestData.Catalog, analyzer, new FakeRecycleBin(), TestData.Clock);
        var scan = TestData.Scan(TestData.TypicalDrive());
        Assert.Contains(cleanup.Analyze(scan).Opportunities.SelectMany(o => o.Items), i => i.FullPath == Cache);

        settings.Save(settings.Current with { ExcludedFolders = [Cache] });

        Assert.DoesNotContain(cleanup.Analyze(scan).Opportunities.SelectMany(o => o.Items), i => i.FullPath.StartsWith(Cache, StringComparison.OrdinalIgnoreCase));
    }
}
