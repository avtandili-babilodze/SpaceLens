using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Services.Session;
using SpaceLens.Core.Services.Settings;
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
    private readonly IScanSession _session;
    private IReadOnlyList<string> _excludedFolders;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CurrentPage), nameof(IsWorkspacePage), nameof(ShowSummaryCards), nameof(ShowDetails))]
    private NavigationItem _selectedNavigation;

    [ObservableProperty]
    private bool _isCompact;

    public MainViewModel(
        IMessenger messenger,
        IScanSession session,
        ISettingsStore settingsStore,
        ScanViewModel scan,
        NotificationViewModel notifications,
        DashboardViewModel dashboard,
        ExplorerViewModel explorer,
        DetailsViewModel details,
        AssistantViewModel assistant,
        CleanupViewModel cleanup,
        CompareViewModel compare,
        DuplicatesViewModel duplicates,
        AppsViewModel apps,
        SystemSpaceViewModel systemSpace,
        HistoryViewModel history,
        SettingsViewModel settings)
    {
        _messenger = messenger;
        _session = session;
        _excludedFolders = settingsStore.Current.ExcludedFolders;
        Scan = scan;
        Notifications = notifications;
        Dashboard = dashboard;
        Explorer = explorer;
        Details = details;
        Assistant = assistant;
        Cleanup = cleanup;
        Compare = compare;
        Duplicates = duplicates;
        Apps = apps;
        SystemSpace = systemSpace;
        History = history;
        Settings = settings;

        Navigation =
        [
            new(AppPage.Overview, "Overview", "\uE80F"),
            new(AppPage.Folders, "Folders", "\uE8B7"),
            new(AppPage.Cleanup, "Cleanup", "\uEA99"),
            new(AppPage.Apps, "Apps", "\uECAA"),
            new(AppPage.SystemSpace, "System space", "\uE770"),
            new(AppPage.Duplicates, "Duplicates", "\uE8C8"),
            new(AppPage.Compare, "Compare Scans", "\uE9D2"),
            new(AppPage.History, "History", "\uE81C"),
            new(AppPage.Settings, "Settings", "\uE713")
        ];
        _selectedNavigation = Navigation[0];

        settingsStore.Changed += (_, _) => OnSettingsChanged(settingsStore.Current.ExcludedFolders);
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

    public AppsViewModel Apps { get; }

    public SystemSpaceViewModel SystemSpace { get; }

    public HistoryViewModel History { get; }

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

    /// <summary>Apps and System space measure when first opened, not at startup.</summary>
    partial void OnSelectedNavigationChanged(NavigationItem value)
    {
        switch (value.Page)
        {
            case AppPage.Apps:
                _ = Apps.EnsureLoadedAsync();
                break;
            case AppPage.SystemSpace:
                _ = SystemSpace.EnsureLoadedAsync();
                break;
        }
    }

    private void ShowWorkspace()
    {
        if (!IsWorkspacePage)
        {
            Navigate(AppPage.Folders);
        }
    }

    /// <summary>A changed never-touch list changes cleanup risks: re-analyze the scan and relist what the explorer shows.</summary>
    private void OnSettingsChanged(IReadOnlyList<string> excludedFolders)
    {
        if (excludedFolders.SequenceEqual(_excludedFolders, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _excludedFolders = excludedFolders;
        Explorer.RefreshListing();
        _ = _session.RefreshCleanupAsync();
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
