using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.AI;

public interface IAiAssistantService
{
    /// <summary>Makes a real, minimal API request to check the saved key, billing and model access.</summary>
    Task TestConnectionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Answers a question about the current scan. <paramref name="activity"/> receives short status lines
    /// while Claude looks things up. The conversation is unchanged if the request fails.
    /// </summary>
    Task<AiReply> AskAsync(
        AiConversation conversation,
        string question,
        StorageNode? focus,
        IProgress<string>? activity,
        CancellationToken cancellationToken);
}
