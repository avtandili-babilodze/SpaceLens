using System.Text;
using System.Text.Json.Nodes;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Settings;

namespace SpaceLens.Core.Services.AI;

/// <summary>
/// Claude Haiku 5.5 through the Messages API, with a tool-use loop over <see cref="IStorageToolbox"/>.
/// Each question carries a compact scan summary; Claude fetches anything else it needs with tools.
/// Answers stream in when the caller wants to show them as they're written.
/// </summary>
public sealed class ClaudeHaikuService(
    IAnthropicMessagesClient client,
    AiContextBuilder contextBuilder,
    IStorageToolbox toolbox,
    ISettingsStore settings) : IAiAssistantService
{
    private const int MaxToolRounds = 8;
    private const int MaxOutputTokens = 4096;

    public async Task TestConnectionAsync(CancellationToken cancellationToken)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Current.Model,
            ["max_tokens"] = 64,
            ["output_config"] = new JsonObject { ["effort"] = "low" },
            ["messages"] = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = AssistantPrompts.ConnectionTest })
        };

        var response = await client.CreateMessageAsync(body, cancellationToken);
        if (response["type"]?.GetValue<string>() != "message")
        {
            throw new AiServiceException(AiErrorKind.Unexpected, "Anthropic answered, but not with a message. Try again.");
        }
    }

    public async Task<AiReply> AskAsync(
        AiConversation conversation,
        string question,
        StorageNode? focus,
        IProgress<string>? activity,
        IProgress<string>? answer,
        CancellationToken cancellationToken)
    {
        var context = await contextBuilder.BuildAsync(focus, cancellationToken);
        var checkpoint = conversation.Checkpoint();
        conversation.AddQuestion($"<scan_context>\n{context}\n</scan_context>\n\n{question}");

        try
        {
            CleanupPlan? plan = null;
            for (var round = 0; round < MaxToolRounds; round++)
            {
                var request = BuildRequest(conversation);
                var response = answer is null
                    ? await client.CreateMessageAsync(request, cancellationToken)
                    : await client.StreamMessageAsync(request, answer.Report, cancellationToken);
                var content = response["content"] as JsonArray
                    ?? throw new AiServiceException(AiErrorKind.Unexpected, "Claude's answer was missing its content. Try again.");

                // The assistant turn goes back verbatim, thinking blocks included, as the API requires.
                conversation.AddAssistantTurn((JsonArray)content.DeepClone());

                switch (response["stop_reason"]?.GetValue<string>())
                {
                    case "tool_use":
                        var results = new JsonArray();
                        foreach (var call in content.OfType<JsonObject>().Where(IsToolCall))
                        {
                            var result = await RunToolAsync(call, activity, cancellationToken);
                            plan = result.Plan ?? plan;
                            results.Add(new JsonObject
                            {
                                ["type"] = "tool_result",
                                ["tool_use_id"] = (string?)call["id"],
                                ["content"] = result.Content,
                                ["is_error"] = result.IsError
                            });
                        }

                        // All results of one turn go back together in a single user message.
                        conversation.AddToolResults(results);
                        continue;

                    case "pause_turn":
                        continue;

                    case "refusal":
                        throw new AiServiceException(AiErrorKind.Refused, "Claude declined to answer that. Try rephrasing your question.");

                    case var stopReason:
                        var text = ExtractText(content);
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            throw new AiServiceException(AiErrorKind.Unexpected, "Claude returned an empty answer. Try again.");
                        }

                        // A turn cut off mid tool call would leave a tool_use without a result, which the API
                        // rejects on every later request; keep the answer but drop this exchange from the history.
                        if (HasToolCall(content))
                        {
                            conversation.RollbackTo(checkpoint);
                        }

                        if (stopReason is "max_tokens" or "model_context_window_exceeded")
                        {
                            text += "\n\n(The answer was cut short.)";
                        }

                        return new AiReply(text.Trim(), plan);
                }
            }

            const string TooManySteps = "Claude needed too many steps for that question. Try something more specific.";
            throw new AiServiceException(AiErrorKind.Unexpected, TooManySteps);
        }
        catch
        {
            conversation.RollbackTo(checkpoint);
            throw;
        }
    }

    private async Task<ToolResult> RunToolAsync(JsonObject call, IProgress<string>? activity, CancellationToken cancellationToken)
    {
        var name = (string?)call["name"] ?? "";
        var input = call["input"] as JsonObject ?? new JsonObject();
        activity?.Report(toolbox.Describe(name, input));
        return await toolbox.ExecuteAsync(name, input, cancellationToken);
    }

    private JsonObject BuildRequest(AiConversation conversation) => new()
    {
        ["model"] = settings.Current.Model,
        ["max_tokens"] = MaxOutputTokens,
        ["system"] = new JsonArray(new JsonObject
        {
            ["type"] = "text",
            ["text"] = AssistantPrompts.System,
            ["cache_control"] = new JsonObject { ["type"] = "ephemeral" }
        }),
        ["tools"] = toolbox.CreateDefinitions(),
        ["output_config"] = new JsonObject { ["effort"] = "medium" },
        ["messages"] = conversation.ToRequestMessages()
    };

    private static bool IsToolCall(JsonObject block) => (string?)block["type"] == "tool_use";

    private static bool HasToolCall(JsonArray content) => content.OfType<JsonObject>().Any(IsToolCall);

    private static string ExtractText(JsonArray content)
    {
        var text = new StringBuilder();
        foreach (var block in content.OfType<JsonObject>().Where(b => (string?)b["type"] == "text"))
        {
            text.Append((string?)block["text"]);
        }

        return text.ToString();
    }
}
