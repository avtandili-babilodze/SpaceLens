using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using SpaceLens.Models;
using SpaceLens.Services.AI;
using SpaceLens.Services.Security;
using SpaceLens.ViewModels;
using Xunit;

namespace SpaceLens.Tests;

public sealed class ClaudeServiceTests
{
    [Fact]
    public async Task OfficialMessagesRequestIncludesModelAndAuthentication()
    {
        string? requestBody = null;
        var handler = new StubHandler(async (request, ct) =>
        {
            Assert.Equal(ClaudeHaikuService.Endpoint, request.RequestUri!.AbsoluteUri);
            Assert.Equal("2023-06-01", request.Headers.GetValues("anthropic-version").Single());
            Assert.Equal("sk-ant-test-not-real", request.Headers.GetValues("x-api-key").Single());
            requestBody = await request.Content!.ReadAsStringAsync(ct);
            return Json("{\"content\":[{\"type\":\"text\",\"text\":\"Measured file [id:123]\"}]}");
        });
        var service = new ClaudeHaikuService(new HttpClient(handler), new MemoryCredentials());
        var reply = await service.AskAsync("What uses space?", new("{\"size\":100}", [new(123, "known", "C:\\known"), new(999, "uncited", "C:\\uncited")]), [], default);
        using var doc = JsonDocument.Parse(requestBody!); Assert.Equal("claude-haiku-5-5", doc.RootElement.GetProperty("model").GetString());
        Assert.Single(reply.References); Assert.Equal(123, reply.References[0].EntryId);
    }

    [Theory]
    [InlineData(401, "{}", "Authentication")]
    [InlineData(403, "{}", "Access denied")]
    [InlineData(429, "{}", "Rate limit")]
    [InlineData(400, "credit balance too low", "Billing")]
    [InlineData(404, "{}", "not available")]
    [InlineData(529, "{}", "temporarily unavailable")]
    public async Task ApiErrorsAreMappedWithoutEchoingSensitiveResponse(int status, string detail, string expected)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(detail + " secret-key-should-not-be-shown") }));
        var service = new ClaudeHaikuService(new HttpClient(handler), new MemoryCredentials());
        var exception = await Assert.ThrowsAsync<AiConnectionException>(() => service.TestConnectionAsync(default));
        Assert.Contains(expected, exception.Message); Assert.DoesNotContain("secret-key", exception.Message);
    }

    [Fact]
    public async Task NetworkFailureIsReportedWithoutInventingSuccess()
    {
        var handler = new StubHandler((_, _) => throw new HttpRequestException("sensitive network detail"));
        var service = new ClaudeHaikuService(new HttpClient(handler), new MemoryCredentials());
        var exception = await Assert.ThrowsAsync<AiConnectionException>(() => service.TestConnectionAsync(default));
        Assert.Contains("Network connection failed", exception.Message); Assert.DoesNotContain("sensitive", exception.Message);
    }

    [Fact]
    public async Task CancellationReachesHttpRequest()
    {
        var handler = new StubHandler(async (_, token) => { await Task.Delay(Timeout.Infinite, token); return Json("{}"); });
        var service = new ClaudeHaikuService(new HttpClient(handler), new MemoryCredentials());
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.TestConnectionAsync(cts.Token));
    }

    [Fact]
    public async Task AssistantNeverCallsClaudeWithoutExplicitMetadataConsent()
    {
        using var w = new TestWorkspace(); await w.InitializeAsync(); w.Write("data.bin", 15);
        var scan = await w.Scanner.ScanAsync(w.Content, null, default); var root = (await w.Repository.GetRootAsync(scan.Id))!;
        var fake = new RecordingAssistant(); var settings = new SettingsViewModel(new MemoryCredentials(), fake, w.Paths) { UseClaude = true, ShareMetadata = false };
        var planner = new SpaceLens.Services.Cleanup.CleanupPlanner(w.Repository);
        var vm = new AssistantViewModel(fake, new LocalStorageAssistant(w.Repository, planner), new AiContextBuilder(w.Repository), settings) { Snapshot = scan, CurrentFolder = root, Input = "What takes space?" };
        await vm.SendCommand.ExecuteAsync(null); Assert.Equal(0, fake.Calls); Assert.Contains("Metadata sharing is off", vm.Messages.Last().Text);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task OfflineConversationIsNotSentToClaudeAndRequestStartsWithUser()
    {
        var handler = new StubHandler(async (request, token) =>
        {
            string body = await request.Content!.ReadAsStringAsync(token);
            Assert.DoesNotContain("private offline question", body);
            Assert.Contains("consented question", body);
            using var document = JsonDocument.Parse(body);
            Assert.Equal("user", document.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
            return Json("{\"content\":[{\"type\":\"text\",\"text\":\"OK\"}]}");
        });
        var service = new ClaudeHaikuService(new HttpClient(handler), new MemoryCredentials());
        await service.AskAsync("current", new("{}", []),
            [new("user", "private offline question"), new("assistant", "earlier cloud response"), new ChatMessage("user", "consented question") { WasSharedWithCloud = true }], default);
    }

    [Fact]
    public async Task MalformedContentIsReportedAsAnApiError()
    {
        var handler = new StubHandler((_, _) => Task.FromResult(Json("{\"content\":{}}")));
        var service = new ClaudeHaikuService(new HttpClient(handler), new MemoryCredentials());
        var exception = await Assert.ThrowsAsync<AiConnectionException>(() => service.TestConnectionAsync(default));
        Assert.Contains("unexpected response", exception.Message);
    }
    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => send(request, token);
    }
    private sealed class MemoryCredentials : ICredentialStore
    {
        public bool HasCredential => true;
        public Task<string?> LoadAsync() => Task.FromResult<string?>("sk-ant-test-not-real");
        public Task SaveAsync(string key) => Task.CompletedTask;
        public Task RemoveAsync() => Task.CompletedTask;
    }
    private sealed class RecordingAssistant : IAiAssistantService
    {
        public int Calls { get; private set; }
        public Task TestConnectionAsync(CancellationToken token) { Calls++; return Task.CompletedTask; }
        public Task<AiReply> AskAsync(string question, AiContext context, IReadOnlyList<ChatMessage> history, CancellationToken token) { Calls++; return Task.FromResult(new AiReply("test", [])); }
    }
}
