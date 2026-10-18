using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Text;

namespace SpaceLens.Core.Services.Integration;

/// <summary>
/// Uses schtasks.exe with a task definition, so the check runs as the signed-in user without administrator rights,
/// catches up after the PC was off at noon, and also runs on battery.
/// </summary>
public sealed class DailyCheckScheduler : IDailyCheckScheduler
{
    public const string TaskName = "SpaceLens daily check";

    private const string TaskTemplate = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>SpaceLens checks free space once a day and lets you know when a drive is nearly full or grew quickly.</Description>
          </RegistrationInfo>
          <Triggers>
            <CalendarTrigger>
              <StartBoundary>{0}</StartBoundary>
              <Enabled>true</Enabled>
              <ScheduleByDay>
                <DaysInterval>1</DaysInterval>
              </ScheduleByDay>
            </CalendarTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <StartWhenAvailable>true</StartWhenAvailable>
            <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>
            <AllowStartOnDemand>true</AllowStartOnDemand>
            <Enabled>true</Enabled>
            <ExecutionTimeLimit>PT30M</ExecutionTimeLimit>
            <Priority>7</Priority>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{1}</Command>
              <Arguments>--check</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    public bool IsEnabled => RunSchtasks("/Query", "/TN", TaskName).ExitCode == 0;

    public void Enable(string executablePath)
    {
        var start = DateTime.Today.AddHours(12).ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        var definition = string.Format(CultureInfo.InvariantCulture, TaskTemplate, start, SecurityElement.Escape(executablePath));
        var file = Path.Join(Path.GetTempPath(), $"spacelens-task-{Guid.NewGuid():N}.xml");
        try
        {
            // schtasks reads task definitions as UTF-16.
            File.WriteAllText(file, definition, Encoding.Unicode);
            var (exitCode, output) = RunSchtasks("/Create", "/TN", TaskName, "/XML", file, "/F");
            if (exitCode != 0)
            {
                throw new InvalidOperationException("Windows didn't create the daily check: " + output.Trim());
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    public void Disable()
    {
        if (IsEnabled)
        {
            RunSchtasks("/Delete", "/TN", TaskName, "/F");
        }
    }

    private static (int ExitCode, string Output) RunSchtasks(params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Join(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Windows couldn't start schtasks.exe.");
        var error = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, error.Result + output);
    }
}
