using SpaceLens.Core.Services.Alerts;

namespace SpaceLens.Services;

public interface IAlertPresenter
{
    /// <summary>Shows the daily check's card in the corner of the screen.</summary>
    void Show(IReadOnlyList<DriveAlert> alerts);
}
