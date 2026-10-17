using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Tests.Support;

namespace SpaceLens.Tests.Cleanup;

public sealed class CleanupPlanningTests
{
    private static CleanupAnalysis AnalyzeTypicalDrive(long recycleBinBytes = 0)
    {
        var analyzer = TestServices.CleanupAnalyzer(new FakeRecycleBin(recycleBinBytes));
        return analyzer.Analyze(TestData.Scan(TestData.TypicalDrive()));
    }

    private static CleanupOpportunity Opportunity(CleanupAnalysis analysis, CleanupKind kind) =>
        analysis.Opportunities.Single(o => o.Kind == kind);

    [Fact]
    public void Analyze_FindsEachKindOfLeftover()
    {
        var analysis = AnalyzeTypicalDrive();

        // User Temp and Windows\Temp both count; fresh.tmp is too recent to touch.
        Assert.Equal(["old.tmp", "setup.log"], Opportunity(analysis, CleanupKind.TempFiles).Items.Select(i => i.Name));
        Assert.Equal(["Cache"], Opportunity(analysis, CleanupKind.BrowserCache).Items.Select(i => i.Name));
        Assert.Equal(["npm-cache"], Opportunity(analysis, CleanupKind.AppCache).Items.Select(i => i.Name));
        Assert.Equal(["CrashDumps"], Opportunity(analysis, CleanupKind.CrashReports).Items.Select(i => i.Name));
        Assert.Equal(["setup-old.exe"], Opportunity(analysis, CleanupKind.OldInstallers).Items.Select(i => i.Name));
        Assert.Equal(["photos.zip"], Opportunity(analysis, CleanupKind.DownloadedArchives).Items.Select(i => i.Name));
        Assert.Equal(["holiday.mp4"], Opportunity(analysis, CleanupKind.LargeVideos).Items.Select(i => i.Name));
        Assert.Equal(["node_modules"], Opportunity(analysis, CleanupKind.DevArtifacts).Items.Select(i => i.Name));
        Assert.Equal(CleanupAction.OpenStorageSettings, Opportunity(analysis, CleanupKind.WindowsLeftovers).Action);
    }

    [Fact]
    public void Analyze_NeverOffersProtectedOrHighRiskItemsForRemoval()
    {
        var analysis = AnalyzeTypicalDrive();

        var offered = analysis.Opportunities
            .Where(o => o.Action == CleanupAction.RecycleItems)
            .SelectMany(o => o.Items)
            .ToList();

        Assert.All(offered, item => Assert.True(TestData.RiskAnalyzer.Assess(item).AllowsRemoval, item.FullPath));
        Assert.DoesNotContain(offered, item => item.Name is "disk.vhdx" or "saves.db" or "fresh.tmp" or "setup-new.msi" or "pagefile.sys");
    }

    [Fact]
    public void Analyze_IncludesTheRecycleBinOnlyForDriveScans()
    {
        var analysis = AnalyzeTypicalDrive(recycleBinBytes: 2 * ByteSize.GB);

        var bin = Opportunity(analysis, CleanupKind.RecycleBin);
        Assert.Equal(CleanupAction.EmptyRecycleBin, bin.Action);
        Assert.Equal(2 * ByteSize.GB, bin.Bytes);
    }

    [Fact]
    public void Plan_UsesLowRiskGroupsBeforeAnythingThatNeedsReview()
    {
        var analysis = AnalyzeTypicalDrive();
        var planner = new CleanupPlanner();

        var plan = planner.CreatePlan(analysis, 1 * ByteSize.GB);

        Assert.True(plan.MeetsGoal);
        Assert.All(plan.Steps, step => Assert.Equal(CleanupRisk.Low, step.Opportunity.Risk));
    }

    [Fact]
    public void Plan_TakesOnlyAsManyReviewItemsAsTheGoalNeeds()
    {
        var analysis = AnalyzeTypicalDrive();
        var lowRisk = analysis.LowRiskBytes;

        var plan = new CleanupPlanner().CreatePlan(analysis, lowRisk + 100 * ByteSize.MB);

        Assert.True(plan.MeetsGoal);
        var reviewSteps = plan.Steps.Where(s => s.Opportunity.Risk == CleanupRisk.Review).ToList();
        Assert.Single(reviewSteps);
        Assert.Single(reviewSteps[0].Items);
    }

    [Fact]
    public void Plan_SaysHowFarShortItFallsInsteadOfAddingRiskyItems()
    {
        var analysis = AnalyzeTypicalDrive();

        var plan = new CleanupPlanner().CreatePlan(analysis, 500 * ByteSize.GB);

        Assert.False(plan.MeetsGoal);
        Assert.Contains("short of", plan.Summary);
        Assert.DoesNotContain(plan.Steps, s => s.Opportunity.Risk is CleanupRisk.High or CleanupRisk.Protected);
    }

    [Theory]
    [InlineData("25 GB", 25 * ByteSize.GB)]
    [InlineData("I need to free 30gb", 30 * ByteSize.GB)]
    [InlineData("free up 1.5 TB please", (long)(1.5 * ByteSize.TB))]
    [InlineData("500 MB", 500 * ByteSize.MB)]
    [InlineData("2,5 GiB", (long)(2.5 * ByteSize.GB))]
    [InlineData("20", 20 * ByteSize.GB)]
    public void GoalParser_ReadsAmountsFromText(string text, long expected)
    {
        Assert.True(StorageGoalParser.TryParse(text, out var bytes));
        Assert.Equal(expected, bytes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("free some space")]
    [InlineData("-5 GB")]
    [InlineData("0 GB")]
    [InlineData("Why is Windows11_23H2 so big")]
    public void GoalParser_RejectsTextWithoutAPositiveAmount(string text) =>
        Assert.False(StorageGoalParser.TryParse(text, out _));

    [Fact]
    public void Analyze_OnlyCountsInstallersSavedDirectlyInDownloads()
    {
        var root = TestData.TypicalDrive();
        TestData.File(root, @"C:\Users\alex\Downloads\tools\portable\setup-tool.exe", 300 * ByteSize.MB);

        var analysis = TestServices.CleanupAnalyzer().Analyze(TestData.Scan(root));

        Assert.Equal(["setup-old.exe"], Opportunity(analysis, CleanupKind.OldInstallers).Items.Select(i => i.Name));
    }
}
