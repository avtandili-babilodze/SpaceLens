using SpaceLens.Models;

namespace SpaceLens.Services.AI;

public interface IAiAssistantService
{
    Task TestConnectionAsync(CancellationToken cancellationToken);
    Task<AiReply> AskAsync(string question, AiContext context, IReadOnlyList<ChatMessage> history, CancellationToken cancellationToken);
}
