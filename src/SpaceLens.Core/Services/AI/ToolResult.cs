using SpaceLens.Core.Models;

namespace SpaceLens.Core.Services.AI;

public sealed record ToolResult(string Content, bool IsError = false, CleanupPlan? Plan = null);
