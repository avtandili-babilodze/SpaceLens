using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpaceLens.Core.Configuration;
using SpaceLens.Core.DependencyInjection;
using SpaceLens.Core.Diagnostics;
using SpaceLens.Core.Services.Interaction;
using SpaceLens.Services;
using SpaceLens.ViewModels;
using SpaceLens.Views;

namespace SpaceLens.Infrastructure;

/// <summary>The composition root: Core services plus everything the WPF app adds on top.</summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddSpaceLensApp(this IServiceCollection services)
    {
        services.AddSpaceLensCore();
        services.AddLogging(logging => logging
            .SetMinimumLevel(LogLevel.Information)
            .AddDebug());
        services.AddSingleton<ILoggerProvider>(provider => new FileLoggerProvider(provider.GetRequiredService<AppPaths>().LogPath));

        // Windows integration. One dialog service answers several interfaces, so register it once.
        services.AddSingleton<DialogService>();
        services.AddSingleton<IConfirmationService>(provider => provider.GetRequiredService<DialogService>());
        services.AddSingleton<IFolderPicker>(provider => provider.GetRequiredService<DialogService>());
        services.AddSingleton<IAlertPresenter>(provider => provider.GetRequiredService<DialogService>());
        services.AddSingleton<IShellService, ShellService>();
        services.AddSingleton<IChatHistory, ChatHistoryStore>();
        services.AddSingleton<Installer>();
        services.AddSingleton<IMessenger>(WeakReferenceMessenger.Default);

        // View models: one instance each for the lifetime of the window
        services.AddSingleton<NodePresenter>();
        services.AddSingleton<ScanViewModel>();
        services.AddSingleton<NotificationViewModel>();
        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<ExplorerViewModel>();
        services.AddSingleton<DetailsViewModel>();
        services.AddSingleton<AssistantViewModel>();
        services.AddSingleton<CleanupViewModel>();
        services.AddSingleton<CompareViewModel>();
        services.AddSingleton<DuplicatesViewModel>();
        services.AddSingleton<AppsViewModel>();
        services.AddSingleton<SystemSpaceViewModel>();
        services.AddSingleton<HistoryViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
