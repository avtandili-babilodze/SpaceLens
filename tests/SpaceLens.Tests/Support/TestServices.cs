using System.Net.Http;
using Microsoft.Extensions.Logging.Abstractions;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.AI;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Interaction;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Security;
using SpaceLens.Core.Services.Session;
using SpaceLens.Core.Services.Settings;
using SpaceLens.Core.Services.Storage;
using SpaceLens.ViewModels;

namespace SpaceLens.Tests.Support;

/// <summary>Builds the real services with test doubles and silent loggers, so tests only state what matters to them.</summary>
public static class TestServices
{
    public static DiskScanner Scanner() => new(new DriveService(), NullLogger<DiskScanner>.Instance);

    public static CleanupAnalyzer CleanupAnalyzer(IRecycleBinService? recycleBin = null) =>
        new(TestData.Catalog, TestData.RiskAnalyzer, recycleBin ?? new FakeRecycleBin(), TestData.Clock);

    public static ScanSession Session(IStorageHistoryService? history = null) =>
        new(CleanupAnalyzer(), history ?? new NoHistory(), NullLogger<ScanSession>.Instance);

    public static async Task<ScanSession> LoadedSessionAsync(FolderNode? root = null)
    {
        var session = Session();
        await session.LoadAsync(TestData.Scan(root ?? TestData.TypicalDrive()));
        return session;
    }

    public static CleanupExecutor Executor(IScanSession session, IRecycleBinService recycleBin, IConfirmationService confirmation) =>
        new(TestData.RiskAnalyzer, recycleBin, confirmation, session, NullLogger<CleanupExecutor>.Instance);

    public static NodePresenter Presenter { get; } = new(TestData.Locations, TestData.RiskAnalyzer);

    public static ClaudeHaikuService Claude(
        IScanSession session,
        HttpMessageHandler handler,
        ICredentialStore? credentials = null,
        ISettingsStore? settings = null)
    {
        var client = AnthropicClient(handler, credentials);
        var toolbox = new StorageToolbox(session, TestData.RiskAnalyzer, new CleanupPlanner());
        var context = new AiContextBuilder(session, TestData.RiskAnalyzer);
        return new ClaudeHaikuService(client, context, toolbox, settings ?? new MemorySettingsStore());
    }

    public static AnthropicMessagesClient AnthropicClient(HttpMessageHandler handler, ICredentialStore? credentials = null) =>
        new(new StubHttpClientFactory(handler), credentials ?? new MemoryCredentialStore(), NullLogger<AnthropicMessagesClient>.Instance);
}
