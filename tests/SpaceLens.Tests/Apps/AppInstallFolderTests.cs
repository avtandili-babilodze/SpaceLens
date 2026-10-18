using System.IO;
using SpaceLens.Core.Services.Apps;

namespace SpaceLens.Tests.Apps;

public sealed class AppInstallFolderTests
{
    private static readonly string ProgramFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

    [Fact]
    public void ARecordedInstallLocationIsUsedAsIs()
    {
        var (folder, isGuessed) = AppInstallFolder.Find(@"D:\Games\Racer", @"D:\Games\Racer\bin\racer.exe,0", null);

        Assert.Equal(@"D:\Games\Racer", folder);
        Assert.False(isGuessed);
    }

    [Fact]
    public void WithoutALocationTheFolderHoldingTheUninstallerAndProgramIsUsed()
    {
        var (folder, isGuessed) = AppInstallFolder.Find(null, @"D:\obs-studio\bin\64bit\obs64.exe", "\"D:\\obs-studio\\uninstall.exe\"");

        Assert.Equal(@"D:\obs-studio", folder);
        Assert.True(isGuessed);
    }

    [Fact]
    public void AnUninstallerInASetupSubfolderOfProgramFilesMeansTheAppsOwnFolder()
    {
        var setup = Path.Join(ProgramFiles, @"Microsoft SQL Server\160\Setup Bootstrap\SQL2022\x64\SetupARP.exe");

        var (folder, _) = AppInstallFolder.Find(null, $"\"{setup}\"", $"\"{setup}\"");

        Assert.Equal(Path.Join(ProgramFiles, "Microsoft SQL Server"), folder);
    }

    [Fact]
    public void FoldersManyProgramsShareAreNeverTheAppsFolder()
    {
        var tool = Path.Join(ProgramFiles, @"Common Files\Vendor\tool.exe");

        Assert.Equal(Path.Join(ProgramFiles, @"Common Files\Vendor"), AppInstallFolder.Find(null, tool, null).Folder);
        Assert.Equal(Path.Join(ProgramFiles, "Vendor"), AppInstallFolder.Find(ProgramFiles, Path.Join(ProgramFiles, @"Vendor\app.exe"), null).Folder);
        Assert.Null(AppInstallFolder.Find(@"C:\", null, null).Folder);
    }

    [Theory]
    [InlineData("MsiExec.exe /X{12345678-ABCD-EF00-1234-56789ABCDEF0}")]
    [InlineData("\"C:\\ProgramData\\Package Cache\\{12345678-ABCD-EF00-1234-56789ABCDEF0}\\setup.exe\" /uninstall")]
    [InlineData("C:\\Windows\\SysWOW64\\rundll32.exe dfshim.dll,ShArpMaintain app.application")]
    public void InstallerCachesAndWindowsToolsDontPointAtTheApp(string uninstallCommand)
    {
        Assert.Null(AppInstallFolder.Find(null, null, uninstallCommand).Folder);
    }

    [Fact]
    public void AppFoldersOnDifferentDrivesDontCombine()
    {
        Assert.Null(AppInstallFolder.Find(null, @"D:\App\app.exe", @"E:\App\uninstall.exe").Folder);
    }

    [Fact]
    public void AFolderOutsideTheScanIsMeasuredOnDisk()
    {
        var folder = Path.Join(Path.GetTempPath(), "SpaceLens.Tests." + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Join(folder, "bin"));
        try
        {
            File.WriteAllBytes(Path.Join(folder, "app.exe"), new byte[3000]);
            File.WriteAllBytes(Path.Join(folder, "bin", "data.pak"), new byte[7000]);

            Assert.Equal(10000, AppSize.MeasureOnDisk(folder, CancellationToken.None));
            Assert.Null(AppSize.MeasureOnDisk(Path.Join(folder, "missing"), CancellationToken.None));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
