using System.Text.Json.Nodes;

namespace SpaceLens.Core.Services.AI;

public interface IStorageToolbox
{
    /// <summary>Tool definitions for the Messages API <c>tools</c> parameter (a fresh copy each call).</summary>
    JsonArray CreateDefinitions();

    /// <summary>A short status line for the UI, e.g. "Looking inside Downloads".</summary>
    string Describe(string toolName, JsonObject input);

    Task<ToolResult> ExecuteAsync(string toolName, JsonObject input, CancellationToken cancellationToken);
}
