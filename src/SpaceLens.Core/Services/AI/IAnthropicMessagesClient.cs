using System.Text.Json.Nodes;

namespace SpaceLens.Core.Services.AI;

public interface IAnthropicMessagesClient
{
    /// <summary>Sends one request to the Messages API and returns the response message object.</summary>
    Task<JsonObject> CreateMessageAsync(JsonObject body, CancellationToken cancellationToken);
}
