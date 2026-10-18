using CommunityToolkit.Mvvm.ComponentModel;
using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>
/// One message in the assistant panel: a question, an answer (with an optional plan), a notice or an error.
/// An answer's text grows while it streams in.
/// </summary>
public sealed partial class ChatMessageViewModel : ObservableObject
{
    [ObservableProperty]
    private string _text;

    /// <summary>The plan was carried out, or the scan it was made from was replaced.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanClean))]
    private bool _isPlanClosed;

    public ChatMessageViewModel(ChatRole role, string text, AnswerSource source = AnswerSource.None, CleanupPlan? plan = null)
    {
        Role = role;
        _text = text;
        Source = source;
        Plan = plan;
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

    /// <summary>Restores a message saved by an earlier session. Its plan can be reopened, but not run, since the scan is gone.</summary>
    public ChatMessageViewModel(SavedChatMessage saved)
    {
        Role = saved.Role;
        _text = saved.Text;
        Source = saved.Source;
        GoalBytes = saved.GoalBytes;
        Steps = saved.Steps ?? [];
    }

    public ChatRole Role { get; }

    public AnswerSource Source { get; }

    /// <summary>The plan behind the steps, while the scan it was made from is still loaded.</summary>
    public CleanupPlan? Plan { get; }

    public IReadOnlyList<PlanStepViewModel> Steps { get; }

    public bool HasSteps => Steps.Count > 0;

    /// <summary>The goal of the attached plan, used to open it on the Cleanup page.</summary>
    public long GoalBytes { get; }

    /// <summary>The plan has items SpaceLens can move to the Recycle Bin right from the chat.</summary>
    public bool CanClean => !IsPlanClosed && Plan is { } plan && plan.Steps.Any(s => s.Opportunity.Action != CleanupAction.OpenStorageSettings);

    public string SourceLabel => Source switch
    {
        AnswerSource.Claude => "Claude Haiku 5.5",
        AnswerSource.Offline => "Offline answer",
        _ => ""
    };

    public SavedChatMessage ToSaved() => new(Role, Text, Source, GoalBytes, Steps.Count > 0 ? Steps : null);
}
