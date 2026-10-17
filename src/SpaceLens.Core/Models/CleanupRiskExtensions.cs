namespace SpaceLens.Core.Models;

public static class CleanupRiskExtensions
{
    public static string ToLabel(this CleanupRisk risk) => risk switch
    {
        CleanupRisk.Low => "Low Risk",
        CleanupRisk.Review => "Review Required",
        CleanupRisk.High => "High Risk",
        _ => "Protected"
    };
}
