using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SpaceLens.ViewModels;

namespace SpaceLens.Views;

/// <summary>PasswordBox.Password can't be bound, so the key is handed to the view model here and cleared immediately.</summary>
public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();

    private void OnSaveKey(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel viewModel)
        {
            return;
        }

        var key = ApiKeyBox.Password;
        ApiKeyBox.Clear();
        viewModel.SaveKeyCommand.Execute(key);
    }

    private void OnApiKeyKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OnSaveKey(sender, e);
        }
    }
}
