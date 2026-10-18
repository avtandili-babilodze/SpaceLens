using System.IO;
using SpaceLens.Core.Services.Apps;

namespace SpaceLens.Tests.Apps;

public sealed class UninstallCommandLineTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\App\\uninstall.exe\" /S", @"C:\Program Files\App\uninstall.exe", "/S")]
    [InlineData(@"C:\Program Files\App\unins000.exe", @"C:\Program Files\App\unins000.exe", "")]
    [InlineData(@"C:\Program Files (x86)\Tool\Uninstall.exe --remove now", @"C:\Program Files (x86)\Tool\Uninstall.exe", "--remove now")]
    [InlineData("MsiExec.exe /I{12345678-ABCD-EF00-1234-56789ABCDEF0}", "MsiExec.exe", "/X{12345678-ABCD-EF00-1234-56789ABCDEF0}")]
    [InlineData("MsiExec.exe /X{12345678-ABCD-EF00-1234-56789ABCDEF0}", "MsiExec.exe", "/X{12345678-ABCD-EF00-1234-56789ABCDEF0}")]
    public void SplitsProgramAndArguments(string commandLine, string fileName, string arguments)
    {
        var (actualFile, actualArguments) = UninstallCommandLine.Split(commandLine);

        Assert.Equal(fileName, actualFile);
        Assert.Equal(arguments, actualArguments);
    }

    [Fact]
    public void ExpandsEnvironmentVariables()
    {
        var (fileName, _) = UninstallCommandLine.Split(@"%SystemRoot%\uninstall.exe /quiet");

        Assert.Equal(Path.Join(Environment.GetEnvironmentVariable("SystemRoot"), "uninstall.exe"), fileName);
    }
}
