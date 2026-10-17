using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels.Messages;

/// <summary>Ask the assistant a question, optionally about a specific item.</summary>
public sealed record AskAssistantMessage(string Question, StorageNode? Focus = null);
