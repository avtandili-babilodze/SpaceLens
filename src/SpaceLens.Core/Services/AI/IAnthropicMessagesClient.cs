using System.Text.Json.Nodes;

namespace SpaceLens.Core.Services.AI;

public interface IAnthropicMessagesClient
{
    /// <summary>Sends one request to the Messages API and returns the response message object.</summary>
    Task<JsonObject> CreateMessageAsync(JsonObject body, CancellationToken cancellationToken);

    /// <summary>
    /// Sends one request as a stream and returns the same message object once it's complete.
    /// <paramref name="onText"/> receives the answer text written so far each time it grows.
    /// </summary>
    Task<JsonObject> StreamMessageAsync(JsonObject body, Action<string> onText, CancellationToken cancellationToken);
}
