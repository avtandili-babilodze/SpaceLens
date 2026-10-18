namespace SpaceLens.Core.Services.Integration;

/// <summary>The Windows scheduled task that runs <c>SpaceLens.exe --check</c> once a day.</summary>
public interface IDailyCheckScheduler
{
    bool IsEnabled { get; }

    /// <summary>Creates (or replaces) the task for the current user. Throws <see cref="InvalidOperationException"/> when Windows refuses.</summary>
    void Enable(string executablePath);

    void Disable();
}
