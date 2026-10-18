namespace SpaceLens.ViewModels;

/// <summary>A chat message as written to disk between sessions.</summary>
public sealed record SavedChatMessage(
    ChatRole Role,
    string Text,
    AnswerSource Source,
    long GoalBytes,
    IReadOnlyList<PlanStepViewModel>? Steps);
