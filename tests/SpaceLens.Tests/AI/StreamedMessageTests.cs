using System.Text.Json.Nodes;
using SpaceLens.Core.Services.AI;

namespace SpaceLens.Tests.AI;

public sealed class StreamedMessageTests
{
    [Fact]
    public void RebuildsTextAndToolCallsFromEvents()
    {
        var reported = new List<string>();
        var message = new StreamedMessage(reported.Add);

        foreach (var data in new[]
        {
            """{"type":"message_start","message":{"id":"m1","type":"message","role":"assistant","content":[],"stop_reason":null}}""",
            """{"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Let me "}}""",
            """{"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"look."}}""",
            """{"type":"content_block_stop","index":0}""",
            """{"type":"content_block_start","index":1,"content_block":{"type":"tool_use","id":"t1","name":"list_folder","input":{}}}""",
            """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"{\"path\": \"C:"}}""",
            """{"type":"content_block_delta","index":1,"delta":{"type":"input_json_delta","partial_json":"\\\\Users\"}"}}""",
            """{"type":"content_block_stop","index":1}""",
            """{"type":"message_delta","delta":{"stop_reason":"tool_use","stop_sequence":null}}""",
            """{"type":"message_stop"}"""
        })
        {
            message.Apply(data);
        }

        var result = message.Build();
        var content = result["content"]!.AsArray();
        Assert.Equal("tool_use", (string?)result["stop_reason"]);
        Assert.Equal("Let me look.", (string?)content[0]!["text"]);
        Assert.Equal(@"C:\Users", (string?)content[1]!["input"]!["path"]);
        Assert.Equal(["", "Let me ", "Let me look."], reported);
    }

    [Fact]
    public void ToolCallWithoutArgumentsGetsAnEmptyInput()
    {
        var message = new StreamedMessage(_ => { });
        message.Apply("""{"type":"message_start","message":{"type":"message","content":[]}}""");
        message.Apply("""{"type":"content_block_start","index":0,"content_block":{"type":"tool_use","id":"t1","name":"compare_with_previous_scan","input":{}}}""");
        message.Apply("""{"type":"content_block_stop","index":0}""");

        var input = message.Build()["content"]![0]!["input"];

        Assert.IsType<JsonObject>(input);
        Assert.Empty(input!.AsObject());
    }

    [Fact]
    public void ErrorEventBecomesAnOverloadError()
    {
        var message = new StreamedMessage(_ => { });

        var error = Assert.Throws<AiServiceException>(() =>
            message.Apply("""{"type":"error","error":{"type":"overloaded_error","message":"Overloaded"}}"""));

        Assert.Equal(AiErrorKind.Overloaded, error.Kind);
    }
}
