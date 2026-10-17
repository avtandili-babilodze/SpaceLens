namespace SpaceLens.Core.Models;

/// <summary>A cleanup risk together with the reason the user sees.</summary>
public sealed record RiskAssessment(CleanupRisk Risk, string Reason)
{
    public string Label => Risk.ToLabel();

    public bool AllowsRemoval => Risk is CleanupRisk.Low or CleanupRisk.Review;
}
