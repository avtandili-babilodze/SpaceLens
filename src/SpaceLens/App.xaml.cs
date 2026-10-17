using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpaceLens.Core.Configuration;
using SpaceLens.Infrastructure;
using SpaceLens.ViewModels;
using SpaceLens.Views;

namespace SpaceLens;

public partial class App : Application
{
    private ServiceProvider? _services;
    private ILogger<App>? _logger;
    private string? _logPath;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // async void is required by this override; everything it awaits handles its own errors,
        // and anything unexpected reaches OnUnhandledException on the dispatcher.
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnFatalException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        _services = new ServiceCollection()
            .AddSpaceLensApp()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        _logger = _services.GetRequiredService<ILogger<App>>();
        _logPath = _services.GetRequiredService<AppPaths>().LogPath;

        var window = _services.GetRequiredService<MainWindow>();
        var viewModel = _services.GetRequiredService<MainViewModel>();
        MainWindow = window;
        window.Show();

        await viewModel.InitializeAsync();
        await StartupOptions.Parse(e.Args).ApplyAsync(viewModel, window);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception");

        // Keep the app alive for unexpected UI errors; the user can retry what they were doing.
        var details = _logPath is null ? "" : $"\n\nDetails were saved to {_logPath}";
        MessageBox.Show(
            "Something went wrong: " + e.Exception.Message + details,
            "SpaceLens",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    /// <summary>An exception on a background thread ends the process; record it before that happens.</summary>
    private void OnFatalException(object sender, UnhandledExceptionEventArgs e) =>
        _logger?.LogCritical(e.ExceptionObject as Exception, "Unhandled exception on a background thread; SpaceLens is closing");

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "A background task failed without being awaited");
        e.SetObserved();
    }
}
