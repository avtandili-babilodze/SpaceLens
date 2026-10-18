using System.Windows;
using Microsoft.Win32;
using SpaceLens.Core.Services.Alerts;
using SpaceLens.Core.Services.Interaction;
using SpaceLens.Views;

namespace SpaceLens.Services;

/// <summary>Native dialogs styled to match the app; owned by the main window so they stay on top of it.</summary>
public sealed class DialogService : IConfirmationService, IFolderPicker, IAlertPresenter
{
    public bool Confirm(ConfirmationRequest request)
    {
        // Installing and uninstalling run without the main window; the dialog then stands on its own.
        var owner = Application.Current.MainWindow is { IsVisible: true } window ? window : null;
        var dialog = new ConfirmDialog(request);
        if (owner is not null)
        {
            dialog.Owner = owner;
        }
        else
        {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            dialog.ShowInTaskbar = true;
        }

        return dialog.ShowDialog() == true;
    }

    public void Inform(string title, string message) =>
        Confirm(new ConfirmationRequest(title, message, "OK") { ShowCancel = false });

    public void Show(IReadOnlyList<DriveAlert> alerts) =>
        new AlertWindow(alerts, () => Application.Current.MainWindow?.Activate()).Show();

    public string? PickFolder(string title)
    {
        var picker = new OpenFolderDialog { Title = title, Multiselect = false };
        return picker.ShowDialog(Application.Current.MainWindow) == true ? picker.FolderName : null;
    }
}
