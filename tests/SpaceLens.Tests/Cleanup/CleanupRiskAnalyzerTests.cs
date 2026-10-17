using System.IO;
using SpaceLens.Core.Models;
using SpaceLens.Tests.Support;

namespace SpaceLens.Tests.Cleanup;

public sealed class CleanupRiskAnalyzerTests
{
    private readonly FolderNode _root = TestData.TypicalDrive();

    [Theory]
    [InlineData(@"C:\Windows", CleanupRisk.Protected)]
    [InlineData(@"C:\Windows\System32\kernel32.dll", CleanupRisk.Protected)]
    [InlineData(@"C:\Program Files\App\app.exe", CleanupRisk.Protected)]
    [InlineData(@"C:\pagefile.sys", CleanupRisk.Protected)]
    [InlineData(@"C:\Windows.old", CleanupRisk.Protected)]
    [InlineData(@"C:\Users", CleanupRisk.High)]
    [InlineData(@"C:\Users\alex", CleanupRisk.High)]
    [InlineData(@"C:\Users\alex\AppData", CleanupRisk.High)]
    [InlineData(@"C:\Users\alex\AppData\Roaming", CleanupRisk.High)]
    [InlineData(@"C:\Users\alex\AppData\Roaming\Game\saves.db", CleanupRisk.High)]
    [InlineData(@"C:\Users\alex\AppData\Local\Temp", CleanupRisk.High)]
    [InlineData(@"C:\Users\alex\Documents", CleanupRisk.High)]
    [InlineData(@"C:\Users\alex\Documents\vm\disk.vhdx", CleanupRisk.High)]
    [InlineData(@"C:\Users\alex\AppData\Local\Temp\old.tmp", CleanupRisk.Low)]
    [InlineData(@"C:\Users\alex\AppData\Local\Temp\fresh.tmp", CleanupRisk.Review)]
    [InlineData(@"C:\Users\alex\AppData\Local\Google\Chrome\User Data\Default\Cache", CleanupRisk.Low)]
    [InlineData(@"C:\Users\alex\AppData\Local\npm-cache", CleanupRisk.Low)]
    [InlineData(@"C:\Users\alex\AppData\Local\CrashDumps\app.dmp", CleanupRisk.Low)]
    [InlineData(@"C:\Users\alex\Downloads\setup-old.exe", CleanupRisk.Low)]
    [InlineData(@"C:\Users\alex\Downloads\setup-new.msi", CleanupRisk.Review)]
    [InlineData(@"C:\Users\alex\Downloads\photos.zip", CleanupRisk.Review)]
    [InlineData(@"C:\Users\alex\Videos\holiday.mp4", CleanupRisk.Review)]
    [InlineData(@"C:\Users\alex\Documents\thesis.docx", CleanupRisk.Review)]
    [InlineData(@"C:\Users\alex\code\web\node_modules", CleanupRisk.Review)]
    public void Assess_ClassifiesTypicalLocations(string path, CleanupRisk expected)
    {
        var node = _root.Find(path) ?? throw new InvalidOperationException($"{path} is not in the test tree.");

        var assessment = TestData.RiskAnalyzer.Assess(node);

        Assert.Equal(expected, assessment.Risk);
        Assert.False(string.IsNullOrWhiteSpace(assessment.Reason));
    }

    [Theory]
    [InlineData(@"C:\Users\alex\.local\bin\tool.exe", CleanupRisk.High)]
    [InlineData(@"C:\Users\alex\.vscode\extensions\ext\bundle.js", CleanupRisk.High)]
    [InlineData(@"C:\Users\alex\.nuget\packages\pkg\1.0\pkg.nupkg", CleanupRisk.Low)]
    [InlineData(@"C:\Users\alex\.cache\models\weights.bin", CleanupRisk.Review)]
    [InlineData(@"C:\Users\alex\Documents\portable\app.exe", CleanupRisk.High)]
    public void Assess_TreatsHiddenToolFoldersAndProgramFilesAsAppData(string path, CleanupRisk expected)
    {
        var file = TestData.File(_root, path, 10 * ByteSize.MB);

        Assert.Equal(expected, TestData.RiskAnalyzer.Assess(file).Risk);
    }

    [Fact]
    public void Assess_ProtectsLinksAndTheDriveItself()
    {
        var link = TestData.Folder(_root, @"C:\Users\alex").AddFolder("OneDrive Link", TestData.LongAgo, isLink: true);

        Assert.Equal(CleanupRisk.Protected, TestData.RiskAnalyzer.Assess(link).Risk);
        Assert.Equal(CleanupRisk.Protected, TestData.RiskAnalyzer.Assess(_root).Risk);
    }

    [Fact]
    public void Assess_NeverCallsWholeSystemOrPersonalFoldersSafe()
    {
        var mustNotBeRemovable = new[]
        {
            @"C:\Windows", @"C:\Program Files", @"C:\Users\alex\AppData", @"C:\Users\alex\AppData\Local",
            @"C:\Users\alex\Documents", @"C:\Users\alex\Downloads", @"C:\Users\alex\Videos"
        };

        foreach (var path in mustNotBeRemovable)
        {
            var node = TestData.Folder(_root, path);
            Assert.False(TestData.RiskAnalyzer.Assess(node).AllowsRemoval, path);
        }
    }

    [Fact]
    public void Assess_TreatsBuildOutputNextToAProjectAsRebuildable()
    {
        TestData.File(_root, @"C:\Users\alex\code\api\Api.csproj", 2 * ByteSize.KB);
        var bin = TestData.Folder(_root, @"C:\Users\alex\code\api\bin");
        var unrelatedBin = TestData.Folder(_root, @"C:\Users\alex\Documents\bin");

        Assert.Contains("rebuild", TestData.RiskAnalyzer.Assess(bin).Reason, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rebuild", TestData.RiskAnalyzer.Assess(unrelatedBin).Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(@"D:\Program Files\Tool\tool.dll", CleanupRisk.Protected)]
    [InlineData(@"D:\Windows\notes.txt", CleanupRisk.Protected)]
    [InlineData(@"E:\ProgramData\Vendor\state.dat", CleanupRisk.Protected)]
    [InlineData(@"C:\Users\sam\Documents\report.docx", CleanupRisk.High)]
    [InlineData(@"C:\Users\alex\Downloads\tools\setup.exe", CleanupRisk.High)]
    public void Assess_ProtectsSystemFoldersOnEveryDriveAndOtherPeoplesFiles(string path, CleanupRisk expected)
    {
        var drive = FolderNode.CreateRoot(Path.GetPathRoot(path)!, TestData.LongAgo);
        var file = TestData.File(drive, path, 10 * ByteSize.MB);

        Assert.Equal(expected, TestData.RiskAnalyzer.Assess(file).Risk);
    }
}
