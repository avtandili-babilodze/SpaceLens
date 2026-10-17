using System.ComponentModel;
using System.Windows;
using SpaceLens.ViewModels;

namespace SpaceLens.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Maximize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.IsScanning)
        {
            e.Cancel = true; vm.ScanCommand.Cancel();
            if (vm.ScanCommand.ExecutionTask is { } scan) await scan;
            Close(); return;
        }
        vm.Assistant.SendCommand.Cancel(); vm.Settings.TestConnectionCommand.Cancel(); vm.Duplicates.FindCommand.Cancel();
    }
}
