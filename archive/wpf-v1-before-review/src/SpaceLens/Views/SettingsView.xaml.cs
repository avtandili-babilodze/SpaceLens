using System.Windows;
using System.Windows.Controls;
using SpaceLens.ViewModels;

namespace SpaceLens.Views;

public partial class SettingsView : UserControl
{
    public SettingsView() => InitializeComponent();
    private async void SaveKey_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm) return;
        string value = ApiKey.Password; ApiKey.Clear();
        await vm.SaveKeyAsync(value);
    }
}
