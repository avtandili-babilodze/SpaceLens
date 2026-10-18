namespace SpaceLens.Core.Services.Interaction;

/// <summary>What a confirmation dialog shows: a question, details such as the items affected, and the button texts.</summary>
public sealed record ConfirmationRequest(
    string Title,
    string Message,
    string ConfirmText,
    IReadOnlyList<string>? Details = null,
    string? Footnote = null,
    bool IsDestructive = false)
{
    public bool ShowCancel { get; init; } = true;

    public string CancelText { get; init; } = "Cancel";
}
