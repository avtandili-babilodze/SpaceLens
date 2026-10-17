using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>One message in the assistant panel: a question, an answer (with an optional plan), a notice or an error.</summary>
public sealed class ChatMessageViewModel
{
    public ChatMessageViewModel(ChatRole role, string text, AnswerSource source = AnswerSource.None, CleanupPlan? plan = null)
    {
        Role = role;
        Text = text;
        Source = source;
        GoalBytes = plan?.GoalBytes ?? 0;
        Steps = plan?.Steps
            .Select((step, index) => new PlanStepViewModel(
                index + 1,
                step.Opportunity.Title,
                step.Opportunity.Description,
                "~" + ByteSize.Format(step.Bytes),
                step.Opportunity.Risk))
            .ToList() ?? [];
    }

    public ChatRole Role { get; }

    public string Text { get; }

    public AnswerSource Source { get; }

    public IReadOnlyList<PlanStepViewModel> Steps { get; }

    public bool HasSteps => Steps.Count > 0;

    /// <summary>The goal of the attached plan, used to open it on the Cleanup page.</summary>
    public long GoalBytes { get; }

    public string SourceLabel => Source switch
    {
        AnswerSource.Claude => "Claude Haiku 5.5",
        AnswerSource.Offline => "Offline answer",
        _ => ""
    };
}
