using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.AI;

/// <summary>Answers common storage questions from the scan alone, with no network access.</summary>
public interface ILocalStorageAssistant
{
    Task<AiReply> AskAsync(string question, StorageNode? focus, CancellationToken cancellationToken);
}
