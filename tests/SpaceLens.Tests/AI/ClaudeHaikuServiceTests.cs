using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.AI;
using SpaceLens.Core.Services.Session;
using SpaceLens.Tests.Support;

namespace SpaceLens.Tests.AI;

public sealed class ClaudeHaikuServiceTests
{
    private static async Task<(ClaudeHaikuService Service, StubHttpHandler Handler, ScanSession Session)> CreateAsync(
        params Func<HttpRequestMessage, string, HttpResponseMessage>[] responses)
    {
        var session = await TestServices.LoadedSessionAsync();
        var handler = new StubHttpHandler(responses);
        return (TestServices.Claude(session, handler), handler, session);
    }

    private static Func<HttpRequestMessage, string, HttpResponseMessage> Reply(string json) =>
        (_, _) => StubHttpHandler.Json(HttpStatusCode.OK, json);

    [Fact]
    public async Task RunsToolsAndSendsTheAssistantTurnBackUnchanged()
    {
        var (service, handler, _) = await CreateAsync(
            Reply("""
                {"type":"message","stop_reason":"tool_use","content":[
                  {"type":"thinking","thinking":"","signature":"sig-123"},
                  {"type":"tool_use","id":"toolu_1","name":"list_folder","input":{"path":"C:\\Users\\alex\\Downloads"}}]}
                """),
            Reply("""{"type":"message","stop_reason":"end_turn","content":[{"type":"text","text":"Old installers in [[C:\\Users\\alex\\Downloads]] are safe to review."}]}"""));
        var conversation = new AiConversation();
        var activity = new List<string>();

        var progress = new SyncProgress(activity.Add);
        var reply = await service.AskAsync(conversation, "What is in Downloads?", null, progress, CancellationToken.None);

        Assert.Contains("Old installers", reply.Text);
        Assert.Equal(["Looking inside Downloads"], activity);
        Assert.Equal(2, handler.Requests.Count);

        var second = JsonNode.Parse(handler.Requests[1].Body)!;
        var messages = second["messages"]!.AsArray();
        Assert.Equal(3, messages.Count);
        Assert.Equal("sig-123", messages[1]!["content"]![0]!["signature"]!.GetValue<string>());
        var toolResult = messages[2]!["content"]![0]!;
        Assert.Equal("tool_result", toolResult["type"]!.GetValue<string>());
        Assert.Equal("toolu_1", toolResult["tool_use_id"]!.GetValue<string>());
        Assert.Contains("setup-old.exe", toolResult["content"]!.GetValue<string>());
        Assert.False(toolResult["is_error"]!.GetValue<bool>());
    }

    [Fact]
    public async Task UsesClaudeHaiku55WithoutSamplingParameters()
    {
        var (service, handler, _) = await CreateAsync(
            Reply("""{"type":"message","stop_reason":"end_turn","content":[{"type":"text","text":"Hi"}]}"""));

        await service.AskAsync(new AiConversation(), "Why is my drive full?", null, null, CancellationToken.None);

        var body = JsonNode.Parse(handler.Requests[0].Body)!.AsObject();
        Assert.Equal("claude-haiku-5-5", body["model"]!.GetValue<string>());
        Assert.False(body.ContainsKey("temperature"));
        Assert.NotEmpty(body["tools"]!.AsArray());
        var question = body["messages"]![0]!["content"]![0]!["text"]!.GetValue<string>();
        Assert.Contains("<scan_context>", question);
        Assert.Contains("Why is my drive full?", question);
    }

    [Fact]
    public async Task ReturnsThePlanClaudeComputedWithTheTool()
    {
        var (service, _, _) = await CreateAsync(
            Reply("""{"type":"message","stop_reason":"tool_use","content":[{"type":"tool_use","id":"t1","name":"plan_cleanup","input":{"goal_gb":2}}]}"""),
            Reply("""{"type":"message","stop_reason":"end_turn","content":[{"type":"text","text":"Here is a plan."}]}"""));

        var reply = await service.AskAsync(new AiConversation(), "Free 2 GB", null, null, CancellationToken.None);

        Assert.NotNull(reply.Plan);
        Assert.Equal(2 * ByteSize.GB, reply.Plan!.GoalBytes);
        Assert.True(reply.Plan.MeetsGoal);
    }

    [Fact]
    public async Task UnknownPathsComeBackAsToolErrors()
    {
        var (service, handler, _) = await CreateAsync(
            Reply("""{"type":"message","stop_reason":"tool_use","content":[{"type":"tool_use","id":"t1","name":"list_folder","input":{"path":"Z:\\Nope"}}]}"""),
            Reply("""{"type":"message","stop_reason":"end_turn","content":[{"type":"text","text":"I couldn't find it."}]}"""));

        await service.AskAsync(new AiConversation(), "List Z:", null, null, CancellationToken.None);

        var toolResult = JsonNode.Parse(handler.Requests[1].Body)!["messages"]![2]!["content"]![0]!;
        Assert.True(toolResult["is_error"]!.GetValue<bool>());
    }

    [Fact]
    public async Task RefusalFailsAndLeavesTheConversationAsItWas()
    {
        var (service, _, _) = await CreateAsync(Reply("""{"type":"message","stop_reason":"refusal","content":[]}"""));
        var conversation = new AiConversation();

        var error = await Assert.ThrowsAsync<AiServiceException>(() =>
            service.AskAsync(conversation, "Something", null, null, CancellationToken.None));

        Assert.Equal(AiErrorKind.Refused, error.Kind);
        Assert.Equal(0, conversation.Checkpoint());
    }

    [Fact]
    public async Task ContextStaysSmallForBigScans()
    {
        var root = TestData.TypicalDrive();
        for (var i = 0; i < 20_000; i++)
        {
            TestData.File(root, $@"C:\Users\alex\Documents\many\f{i % 200}\file{i}.txt", 1000 + i);
        }

        var session = await TestServices.LoadedSessionAsync(root);

        var builder = new AiContextBuilder(session, TestData.RiskAnalyzer);
        var context = await builder.BuildAsync(root.Find(@"C:\Users\alex\Documents"), CancellationToken.None);

        Assert.True(context.Length < 16_000, $"Context was {context.Length} characters.");
        // Paths appear JSON-escaped (one backslash becomes two).
        Assert.Contains(@"C:\\Users\\alex\\Documents", context);
        Assert.DoesNotContain("file19999.txt", context);
    }

    private sealed class SyncProgress(Action<string> report) : IProgress<string>
    {
        public void Report(string value) => report(value);
    }

    [Fact]
    public async Task AnswerCutOffMidToolCallIsKeptButLeftOutOfTheConversation()
    {
        var (service, _, _) = await CreateAsync(Reply("""
            {"type":"message","stop_reason":"max_tokens","content":[
              {"type":"text","text":"Most of the space is in Downloads."},
              {"type":"tool_use","id":"toolu_9","name":"list_folder","input":{}}]}
            """));
        var conversation = new AiConversation();

        var reply = await service.AskAsync(conversation, "Why is my drive full?", null, null, CancellationToken.None);

        Assert.StartsWith("Most of the space is in Downloads.", reply.Text);
        Assert.Contains("cut short", reply.Text);

        // A tool_use without its result would make every later request fail.
        Assert.Equal(0, conversation.Checkpoint());
    }
}
