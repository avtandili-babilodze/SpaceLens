using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Services.AI;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Security;
using SpaceLens.Core.Services.Session;
using SpaceLens.Core.Services.Settings;
using SpaceLens.Tests.Support;
using SpaceLens.ViewModels;

namespace SpaceLens.Tests.ViewModels;

public sealed class AssistantViewModelTests
{
    // Any request reaching this handler fails the test: these scenarios must stay on the PC.
    private readonly StubHttpHandler _api = new((_, _) => throw new InvalidOperationException("Nothing should be sent to Claude."));

    [Fact]
    public async Task AsksForConsentBeforeSharingAnythingWithClaude()
    {
        var settings = new MemorySettingsStore();
        settings.Save(settings.Current with { ShareScanDetails = false });
        var assistant = await CreateAsync(new MemoryCredentialStore(), settings);

        await assistant.SendCommand.ExecuteAsync("Why is my drive full?");

        Assert.True(assistant.NeedsConsent);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task AnsweringOfflineAfterDecliningSendsNothing()
    {
        var settings = new MemorySettingsStore();
        settings.Save(settings.Current with { ShareScanDetails = false });
        var assistant = await CreateAsync(new MemoryCredentialStore(), settings);
        await assistant.SendCommand.ExecuteAsync("Free up 3 GB");

        await assistant.AnswerOfflineCommand.ExecuteAsync(null);

        Assert.False(assistant.NeedsConsent);
        Assert.False(settings.Current.ShareScanDetails);
        Assert.Contains(assistant.Messages, m => m.Source == AnswerSource.Offline && m.HasSteps);
        Assert.Empty(_api.Requests);
    }

    [Fact]
    public async Task AnswersOfflineWithoutAnApiKey()
    {
        var assistant = await CreateAsync(new MemoryCredentialStore(key: null), new MemorySettingsStore());

        await assistant.SendCommand.ExecuteAsync("What can I clean without breaking anything?");

        Assert.Contains(assistant.Messages, m => m.Source == AnswerSource.Offline);
        Assert.Equal("Offline answers", assistant.ModeLabel);
        Assert.Empty(_api.Requests);
    }

    private async Task<AssistantViewModel> CreateAsync(ICredentialStore credentials, ISettingsStore settings)
    {
        IScanSession session = await TestServices.LoadedSessionAsync();
        var claude = TestServices.Claude(session, _api, credentials, settings);
        var local = new LocalStorageAssistant(session, TestData.RiskAnalyzer, new CleanupPlanner(), new NoInstalledApps(), TestData.Clock);
        var executor = TestServices.Executor(session, new FakeRecycleBin(), new ScriptedDialogs(answer: true));
        return new AssistantViewModel(claude, local, credentials, settings, session, executor, new NoChatHistory(), new StrongReferenceMessenger());
    }
}
