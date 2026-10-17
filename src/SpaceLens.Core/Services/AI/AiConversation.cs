using System.Text.Json.Nodes;

namespace SpaceLens.Core.Services.AI;

/// <summary>
/// The raw Messages API transcript of one chat session. It is append-only: assistant turns (including thinking
/// blocks) are sent back exactly as received. A failed request is rolled back to its checkpoint.
/// </summary>
public sealed class AiConversation
{
    private readonly List<JsonObject> _messages = [];

    /// <summary>Questions asked in this conversation.</summary>
    public int QuestionCount { get; private set; }

    public int Checkpoint() => _messages.Count;

    public void RollbackTo(int checkpoint)
    {
        if (checkpoint < _messages.Count)
        {
            _messages.RemoveRange(checkpoint, _messages.Count - checkpoint);
        }
    }

    public void AddQuestion(string text)
    {
        _messages.Add(Message("user", new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text })));
        QuestionCount++;
    }

    public void AddToolResults(JsonArray results) => _messages.Add(Message("user", results));

    public void AddAssistantTurn(JsonArray content) => _messages.Add(Message("assistant", content));

    /// <summary>A copy of the transcript for a request body (JSON nodes can only belong to one parent).</summary>
    public JsonArray ToRequestMessages() => new([.. _messages.Select(m => m.DeepClone())]);

    private static JsonObject Message(string role, JsonArray content) => new() { ["role"] = role, ["content"] = content };
}
