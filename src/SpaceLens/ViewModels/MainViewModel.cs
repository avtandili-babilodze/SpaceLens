using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>
/// The window: navigation between pages and the wiring between panels. Each page and panel is its own view model;
/// this class only composes them.
/// </summary>
public sealed partial class MainViewModel : ObservableObject,
    IRecipient<ShowInExplorerMessage>,
    IRecipient<ShowLargestFilesMessage>,
    IRecipient<NavigateMessage>
{
    private static readonly string[] QuestionStarters =
        ["why", "what", "how", "which", "can", "should", "is", "where", "help", "free", "find"];

    private readonly IMessenger _messenger;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage), nameof(IsWorkspacePage), nameof(ShowSummaryCards), nameof(ShowDetails))]
    private NavigationItem _selectedNavigation;

    [ObservableProperty]
    private bool _isCompact;

    public MainViewModel(
        IMessenger messenger,
        ScanViewModel scan,
        NotificationViewModel notifications,
        DashboardViewModel dashboard,
        ExplorerViewModel explorer,
        DetailsViewModel details,
        AssistantViewModel assistant,
        CleanupViewModel cleanup,
        CompareViewModel compare,
        DuplicatesViewModel duplicates,
        SettingsViewModel settings)
    {
        _messenger = messenger;
        Scan = scan;
        Notifications = notifications;
        Dashboard = dashboard;
        Explorer = explorer;
        Details = details;
        Assistant = assistant;
        Cleanup = cleanup;
        Compare = compare;
        Duplicates = duplicates;
        Settings = settings;

        Navigation =
        [
            new(AppPage.Overview, "Overview", ""),
            new(AppPage.Folders, "Folders", ""),
            new(AppPage.Cleanup, "Cleanup", ""),
            new(AppPage.Compare, "Compare Scans", ""),
            new(AppPage.Duplicates, "Duplicates", ""),
            new(AppPage.Settings, "Settings", "")
        ];
        _selectedNavigation = Navigation[0];

        scan.PropertyChanged += OnScanChanged;
        explorer.PropertyChanged += OnExplorerChanged;
        messenger.RegisterAll(this);
    }

    public ScanViewModel Scan { get; }

    public NotificationViewModel Notifications { get; }

    public DashboardViewModel Dashboard { get; }

    public ExplorerViewModel Explorer { get; }

    public DetailsViewModel Details { get; }

    public AssistantViewModel Assistant { get; }

    public CleanupViewModel Cleanup { get; }

    public CompareViewModel Compare { get; }

    public DuplicatesViewModel Duplicates { get; }

    public SettingsViewModel Settings { get; }

    public IReadOnlyList<NavigationItem> Navigation { get; }

    public AppPage CurrentPage => SelectedNavigation.Page;

    /// <summary>Overview and Folders share the explorer, the details panel and the assistant.</summary>
    public bool IsWorkspacePage => CurrentPage is AppPage.Overview or AppPage.Folders;

    public bool ShowSummaryCards => CurrentPage == AppPage.Overview;

    public bool ShowDetails => IsWorkspacePage;

    public async Task InitializeAsync()
    {
        Scan.RefreshDrives();
        await Compare.InitializeAsync();
    }

    public void Receive(ShowInExplorerMessage message)
    {
        ShowWorkspace();
        Explorer.NavigateTo(message.Node);
    }

    public void Receive(ShowLargestFilesMessage message)
    {
        ShowWorkspace();
        _ = Explorer.ShowLargestFilesAsync(message.Folder);
    }

    public void Receive(NavigateMessage message) => Navigate(message.Page);

    /// <summary>True for text that reads like a question rather than a file name.</summary>
    internal static bool LooksLikeQuestion(string text)
    {
        var firstWord = text.Split(' ', 2)[0].ToLowerInvariant();
        return text.EndsWith('?') || (text.Contains(' ') && QuestionStarters.Contains(firstWord));
    }

    [RelayCommand]
    private void Navigate(AppPage page) => SelectedNavigation = Navigation.First(n => n.Page == page);

    /// <summary>
    /// Typing in the header box filters the explorer as you go; Enter hands the text to the assistant.
    /// A bare name becomes a question about it.
    /// </summary>
    [RelayCommand]
    private void SubmitSearch()
    {
        var text = Explorer.SearchText.Trim();
        if (text.Length < 2)
        {
            return;
        }

        var question = LooksLikeQuestion(text)
            ? text
            : $"Find \"{text}\" in my scan and tell me what it is and whether it's safe to clean.";
        Explorer.SearchText = "";
        _messenger.Send(new AskAssistantMessage(question, Explorer.SelectedNode));
    }

    private void ShowWorkspace()
    {
        if (!IsWorkspacePage)
        {
            Navigate(AppPage.Folders);
        }
    }

    private void OnScanChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanViewModel.SelectedDrive))
        {
            Dashboard.Drive = Scan.SelectedDrive;
        }
    }

    private void OnExplorerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExplorerViewModel.SelectedNode))
        {
            var node = Explorer.SelectedNode;
            _ = Details.ShowAsync(node);
            Assistant.Focus = node;
        }
    }
}
