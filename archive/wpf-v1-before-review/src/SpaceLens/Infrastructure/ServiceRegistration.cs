using System.Net.Http;
using Microsoft.Extensions.DependencyInjection;
using SpaceLens.Repositories;
using SpaceLens.Services.AI;
using SpaceLens.Services.Cleanup;
using SpaceLens.Services.Platform;
using SpaceLens.Services.Scanning;
using SpaceLens.Services.Security;
using SpaceLens.Services.Storage;
using SpaceLens.ViewModels;
using SpaceLens.Views;

namespace SpaceLens.Infrastructure;

public static class ServiceRegistration
{
    public static ServiceProvider Build()
    {
        var services = new ServiceCollection();
        services.AddSingleton<AppPaths>();
        services.AddSingleton<IScanHistoryRepository, SqliteScanHistoryRepository>();
        services.AddSingleton<ICleanupRiskAnalyzer, CleanupRiskAnalyzer>();
        services.AddSingleton<IDiskScanner, DiskScanner>();
        services.AddSingleton<ICleanupPlanner, CleanupPlanner>();
        services.AddSingleton<IDuplicateFinder, DuplicateFinder>();
        services.AddSingleton<IWindowsPlatformService, WindowsPlatformService>();
        services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        services.AddSingleton<IStorageHistoryService, StorageHistoryService>();
        services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromSeconds(90) });
        services.AddSingleton<IAiAssistantService, ClaudeHaikuService>();
        services.AddSingleton<AiContextBuilder>(); services.AddSingleton<LocalStorageAssistant>();
        services.AddSingleton<ExplorerViewModel>(); services.AddSingleton<AssistantViewModel>();
        services.AddSingleton<SettingsViewModel>(); services.AddSingleton<CleanupViewModel>();
        services.AddSingleton<CompareViewModel>(); services.AddSingleton<DuplicatesViewModel>();
        services.AddSingleton<MainViewModel>(); services.AddSingleton<MainWindow>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
