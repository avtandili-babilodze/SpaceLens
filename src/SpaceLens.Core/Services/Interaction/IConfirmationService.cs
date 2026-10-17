namespace SpaceLens.Core.Services.Interaction;

/// <summary>
/// Asks the user before something irreversible happens. Core services depend on this abstraction;
/// the WPF app supplies the dialog.
/// </summary>
public interface IConfirmationService
{
    /// <summary>Shows a modal confirmation; true only when the user explicitly confirms.</summary>
    bool Confirm(ConfirmationRequest request);

    /// <summary>Shows a message the user acknowledges.</summary>
    void Inform(string title, string message);
}
