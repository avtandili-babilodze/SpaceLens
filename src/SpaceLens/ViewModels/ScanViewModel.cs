using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Session;
using SpaceLens.Services;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>Drive selection, running a scan with progress and cancellation, and the status bar.</summary>
public sealed partial class ScanViewModel : ObservableObject
{
    private readonly IDiskScanner _scanner;
    private readonly IDriveService _driveService;
    private readonly IFolderPicker _folderPicker;
    private readonly IScanSession _session;
    private readonly IMessenger _messenger;
    private CancellationTokenSource? _activeScan;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanDriveCommand))]
    private DriveSummary? _selectedDrive;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ScanDriveCommand), nameof(ScanFolderCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private string _target = "";

    [ObservableProperty]
    private string _progressText = "";

    [ObservableProperty]
    private string _progressPath = "";

    [ObservableProperty]
    private string _statusText = "Ready";

    public ScanViewModel(
        IDiskScanner scanner,
        IDriveService driveService,
        IFolderPicker folderPicker,
        IScanSession session,
        IMessenger messenger)
    {
        _scanner = scanner;
        _driveService = driveService;
        _folderPicker = folderPicker;
        _session = session;
        _messenger = messenger;
        session.PropertyChanged += OnSessionChanged;
    }

    public ObservableCollection<DriveSummary> Drives { get; } = [];

    public bool HasScan => _session.HasScan;

    public string Summary
    {
        get
        {
            if (_session.Current is not { } scan)
            {
                return "No scan yet";
            }

            var state = scan.IsComplete ? "Scanned" : "Partial scan";
            var completed = scan.CompletedUtc.ToLocalTime();
            var duration = scan.Duration.TotalSeconds;
            var text = $"{state} {scan.RootPath}  ·  {scan.Root.FileCount:N0} files  ·  {completed:MMM d, HH:mm}  ·  {duration:0.#} s";
            if (scan.UnreadableFolderCount > 0)
            {
                text += $"  ·  {scan.UnreadableFolderCount:N0} folders couldn't be read";
            }

            return text;
        }
    }

    [RelayCommand]
    public void RefreshDrives()
    {
        var current = SelectedDrive?.RootPath;
        Drives.Clear();
        foreach (var drive in _driveService.GetDrives())
        {
            Drives.Add(drive);
        }

        var systemDrive = Path.GetPathRoot(Environment.SystemDirectory);
        SelectedDrive = Drives.FirstOrDefault(d => d.RootPath == current)
            ?? Drives.FirstOrDefault(d => string.Equals(d.RootPath, systemDrive, StringComparison.OrdinalIgnoreCase))
            ?? Drives.FirstOrDefault();
    }

    /// <summary>Scans any path (used by the scan buttons and the --scan startup option).</summary>
    public async Task ScanPathAsync(string path, CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _activeScan = cancellation;
        IsScanning = true;
        Target = path;
        ProgressText = "Starting…";
        ProgressPath = path;
        var progress = new Progress<ScanProgress>(p =>
        {
            ProgressText = $"{p.Files:N0} files  ·  {p.Folders:N0} folders  ·  {ByteSize.Format(p.Bytes)}";
            ProgressPath = p.CurrentPath;
        });

        try
        {
            var result = await _scanner.ScanAsync(path, progress, cancellation.Token);
            await _session.LoadAsync(result);
            RefreshDrives();
            StatusText = result.IsComplete
                ? $"Scanned {result.Root.FileCount:N0} files in {result.Duration.TotalSeconds:0.#} seconds."
                : "Scan stopped. Showing what was read so far.";
            if (_session.HistoryError is { } historyError)
            {
                _messenger.Send(new NotificationMessage(historyError, IsError: true));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _messenger.Send(new NotificationMessage(exception.Message, IsError: true));
            StatusText = "Scan failed.";
        }
        finally
        {
            IsScanning = false;
            _activeScan = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanScanDrive))]
    private Task ScanDriveAsync() => ScanPathAsync(SelectedDrive!.RootPath, CancellationToken.None);

    private bool CanScanDrive() => !IsScanning && SelectedDrive is not null;

    [RelayCommand(CanExecute = nameof(CanScanFolder))]
    private async Task ScanFolderAsync()
    {
        var folder = _folderPicker.PickFolder("Choose a folder to scan");
        if (folder is not null)
        {
            await ScanPathAsync(folder, CancellationToken.None);
        }
    }

    private bool CanScanFolder() => !IsScanning;

    /// <summary>Stops the running scan, however it was started; what was read so far is kept.</summary>
    [RelayCommand]
    private void Cancel() => _activeScan?.Cancel();

    private void OnSessionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IScanSession.Current))
        {
            OnPropertyChanged(nameof(HasScan));
            OnPropertyChanged(nameof(Summary));
        }
    }
}
