using SpaceLens.Core.Models;

namespace SpaceLens.ViewModels;

/// <summary>A numbered cleanup step shown under an assistant answer.</summary>
public sealed record PlanStepViewModel(int Number, string Title, string Description, string SizeText, CleanupRisk Risk)
{
    public string RiskLabel => Risk == CleanupRisk.Review ? "Review" : Risk.ToLabel();
}
