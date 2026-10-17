using System.Windows;
using Microsoft.Win32;
using SpaceLens.Core.Services.Interaction;
using SpaceLens.Views;

namespace SpaceLens.Services;

/// <summary>Native dialogs styled to match the app; owned by the main window so they stay on top of it.</summary>
public sealed class DialogService : IConfirmationService, IFolderPicker
{
    public bool Confirm(ConfirmationRequest request)
    {
        var dialog = new ConfirmDialog(request) { Owner = Application.Current.MainWindow };
        return dialog.ShowDialog() == true;
    }

    public void Inform(string title, string message) =>
        Confirm(new ConfirmationRequest(title, message, "OK") { ShowCancel = false });

    public string? PickFolder()
    {
        var picker = new OpenFolderDialog { Title = "Choose a folder to scan", Multiselect = false };
        return picker.ShowDialog(Application.Current.MainWindow) == true ? picker.FolderName : null;
    }
}
