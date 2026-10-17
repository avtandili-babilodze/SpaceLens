using System.IO;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SpaceLens.Infrastructure;
using SpaceLens.ViewModels;
using SpaceLens.Views;

namespace SpaceLens;

public partial class App : Application
{
    private ServiceProvider? services;
    private Mutex? instanceMutex;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string user = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        instanceMutex = new Mutex(true, "Local\\SpaceLens-" + user, out bool firstInstance);
        if (!firstInstance) { MessageBox.Show("SpaceLens is already running for this Windows user.", "SpaceLens"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            MessageBox.Show("SpaceLens encountered an error. " + args.Exception.Message, "SpaceLens", MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };
        services = ServiceRegistration.Build();
        var window = services.GetRequiredService<MainWindow>(); MainWindow = window;
        window.Loaded += async (_, _) =>
        {
            var vm = services.GetRequiredService<MainViewModel>(); await vm.InitializeAsync();
            int scanIndex = Array.IndexOf(e.Args, "--scan");
            if (scanIndex >= 0 && e.Args.Length > scanIndex + 1) await vm.ScanLocationAsync(e.Args[scanIndex + 1]);
            int pageIndex = Array.IndexOf(e.Args, "--page");
            if (pageIndex >= 0 && e.Args.Length > pageIndex + 1) vm.SelectedNavigation = vm.Navigation.FirstOrDefault(n => n.Key == e.Args[pageIndex + 1]) ?? vm.SelectedNavigation;
            int captureIndex = Array.IndexOf(e.Args, "--capture");
            if (captureIndex >= 0 && e.Args.Length > captureIndex + 1)
            {
                await Task.Delay(800); window.UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)window.ActualWidth, (int)window.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var file = File.Create(e.Args[captureIndex + 1]); encoder.Save(file);
            }
        };
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { services?.Dispose(); instanceMutex?.Dispose(); base.OnExit(e); }
}
