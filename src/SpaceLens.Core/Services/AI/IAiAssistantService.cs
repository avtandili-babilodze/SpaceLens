using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.AI;

public interface IAiAssistantService
{
    /// <summary>Makes a real, minimal API request to check the saved key, billing and model access.</summary>
    Task TestConnectionAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Answers a question about the current scan. <paramref name="activity"/> receives short status lines
    /// while Claude looks things up, and <paramref name="answer"/> the answer text written so far as it streams in
    /// (it can start over when Claude looks something up first). The conversation is unchanged if the request fails.
    /// </summary>
    Task<AiReply> AskAsync(
        AiConversation conversation,
        string question,
        StorageNode? focus,
        IProgress<string>? activity,
        IProgress<string>? answer,
        CancellationToken cancellationToken);
}
