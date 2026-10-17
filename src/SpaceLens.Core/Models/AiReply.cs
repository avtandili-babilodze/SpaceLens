namespace SpaceLens.Core.Models;

/// <summary>An answer from the assistant, with the structured plan it computed (if any).</summary>
public sealed record AiReply(string Text, CleanupPlan? Plan = null);
