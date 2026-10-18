using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace SpaceLens.Core.Services.AI;

/// <summary>
/// Rebuilds a Messages API response from its server-sent events, so a streamed answer ends up exactly like a
/// non-streamed one (thinking blocks and tool calls included). Reports the answer text as it grows.
/// </summary>
internal sealed class StreamedMessage(Action<string> onText)
{
    private readonly List<JsonObject?> _blocks = [];
    private readonly Dictionary<int, StringBuilder> _toolInputs = [];
    private readonly StringBuilder _text = new();
    private JsonObject? _message;

    /// <summary>Applies the data of one event (a JSON object with a "type").</summary>
    public void Apply(string data)
    {
        var json = JsonNode.Parse(data) as JsonObject;
        switch ((string?)json?["type"])
        {
            case "message_start":
                _message = json!["message"]?.DeepClone() as JsonObject
                    ?? throw new AiServiceException(AiErrorKind.Unexpected, "Claude's answer started without a message. Try again.");
                _blocks.Clear();
                _toolInputs.Clear();
                _text.Clear();
                onText("");
                break;
            case "content_block_start":
                StartBlock((int)json!["index"]!, json["content_block"]!.DeepClone().AsObject());
                break;
            case "content_block_delta":
                ApplyDelta((int)json!["index"]!, json["delta"]!.AsObject());
                break;
            case "content_block_stop":
                StopBlock((int)json!["index"]!);
                break;
            case "message_delta":
                if (_message is not null && json!["delta"] is JsonObject delta)
                {
                    _message["stop_reason"] = delta["stop_reason"]?.DeepClone();
                    _message["stop_sequence"] = delta["stop_sequence"]?.DeepClone();
                }

                break;
            case "error":
                // Errors after the response started (usually overload) arrive as an event instead of a status code.
                throw AnthropicMessagesClient.MapError(HttpStatusCode.OK, data);
        }
    }

    public JsonObject Build()
    {
        var message = _message
            ?? throw new AiServiceException(AiErrorKind.Unexpected, "Claude's answer ended before it started. Try again.");
        message["content"] = new JsonArray([.. _blocks.Where(b => b is not null)]);
        return message;
    }

    private void StartBlock(int index, JsonObject block)
    {
        while (_blocks.Count <= index)
        {
            _blocks.Add(null);
        }

        _blocks[index] = block;
        if ((string?)block["type"] is "tool_use" or "server_tool_use")
        {
            _toolInputs[index] = new StringBuilder();
        }
    }

    private void ApplyDelta(int index, JsonObject delta)
    {
        var block = index < _blocks.Count ? _blocks[index] : null;
        if (block is null)
        {
            return;
        }

        switch ((string?)delta["type"])
        {
            case "text_delta":
                var text = (string?)delta["text"] ?? "";
                block["text"] = (string?)block["text"] + text;
                _text.Append(text);
                onText(_text.ToString());
                break;
            case "input_json_delta":
                _toolInputs.GetValueOrDefault(index)?.Append((string?)delta["partial_json"]);
                break;
            case "thinking_delta":
                block["thinking"] = (string?)block["thinking"] + (string?)delta["thinking"];
                break;
            case "signature_delta":
                block["signature"] = (string?)block["signature"] + (string?)delta["signature"];
                break;
            case "citations_delta" when delta["citation"] is { } citation:
                if (block["citations"] is not JsonArray citations)
                {
                    citations = [];
                    block["citations"] = citations;
                }

                citations.Add(citation.DeepClone());
                break;
        }
    }

    private void StopBlock(int index)
    {
        if (index < _blocks.Count && _blocks[index] is { } block && _toolInputs.Remove(index, out var input))
        {
            block["input"] = input.Length == 0 ? new JsonObject() : JsonNode.Parse(input.ToString());
        }
    }
}
