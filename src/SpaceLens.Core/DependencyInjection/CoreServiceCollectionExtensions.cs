using Microsoft.Extensions.DependencyInjection;
using SpaceLens.Core.Configuration;
using SpaceLens.Core.Repositories;
using SpaceLens.Core.Services.AI;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Duplicates;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Security;
using SpaceLens.Core.Services.Session;
using SpaceLens.Core.Services.Settings;
using SpaceLens.Core.Services.Storage;

namespace SpaceLens.Core.DependencyInjection;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers every Core service. The host must also register an
    /// <see cref="Services.Interaction.IConfirmationService"/> and logging.
    /// </summary>
    public static IServiceCollection AddSpaceLensCore(this IServiceCollection services)
    {
        // Environment
        services.AddSingleton<AppPaths>();
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(provider => KnownLocations.ForCurrentUser(provider.GetRequiredService<AppPaths>().Root));

        // Scanning
        services.AddSingleton<IDriveService, DriveService>();
        services.AddSingleton<IDiskScanner, DiskScanner>();
        services.AddSingleton<IDuplicateFinder, DuplicateFinder>();

        // Cleanup
        services.AddSingleton<CleanupCatalog>();
        services.AddSingleton<ICleanupRiskAnalyzer, CleanupRiskAnalyzer>();
        services.AddSingleton<ICleanupAnalyzer, CleanupAnalyzer>();
        services.AddSingleton<ICleanupPlanner, CleanupPlanner>();
        services.AddSingleton<IRecycleBinService, RecycleBinService>();
        services.AddSingleton<ICleanupExecutor, CleanupExecutor>();

        // History and the current scan
        services.AddSingleton<IScanHistoryRepository, SqliteScanHistoryRepository>();
        services.AddSingleton<IStorageHistoryService, StorageHistoryService>();
        services.AddSingleton<IScanSession, ScanSession>();

        // Settings and credentials
        services.AddSingleton<ICredentialStore, WindowsCredentialStore>();
        services.AddSingleton<ISettingsStore, JsonSettingsStore>();

        // Claude (the client applies its own per-request timeout)
        services.AddHttpClient(AnthropicMessagesClient.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan);
        services.AddSingleton<IAnthropicMessagesClient, AnthropicMessagesClient>();
        services.AddSingleton<AiContextBuilder>();
        services.AddSingleton<IStorageToolbox, StorageToolbox>();
        services.AddSingleton<IAiAssistantService, ClaudeHaikuService>();
        services.AddSingleton<ILocalStorageAssistant, LocalStorageAssistant>();

        return services;
    }
}
