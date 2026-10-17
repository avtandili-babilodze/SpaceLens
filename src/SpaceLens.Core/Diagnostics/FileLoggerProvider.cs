using Microsoft.Extensions.Logging;

namespace SpaceLens.Core.Diagnostics;

/// <summary>
/// Appends warnings and errors to a text file so a problem a user hit can be diagnosed afterwards.
/// The file is replaced once it passes <see cref="MaxBytes"/>; the previous one is kept as <c>.old</c>.
/// </summary>
public sealed class FileLoggerProvider(string path) : ILoggerProvider
{
    public const long MaxBytes = 1024 * 1024;

    private readonly Lock _gate = new();

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    public void Dispose()
    {
    }

    internal void Write(string entry)
    {
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                {
                    File.Move(path, path + ".old", overwrite: true);
                }

                File.AppendAllText(path, entry + Environment.NewLine);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Logging must never take the app down; a locked or read-only log file is simply skipped.
            }
        }
    }

    private sealed class FileLogger(FileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Warning;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
            {
                return;
            }

            var entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{logLevel}] {category}: {formatter(state, exception)}";
            if (exception is not null)
            {
                entry += Environment.NewLine + exception;
            }

            provider.Write(entry);
        }
    }
}
