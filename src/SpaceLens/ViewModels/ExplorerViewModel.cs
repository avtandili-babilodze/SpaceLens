using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Cleanup;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Session;
using SpaceLens.Core.Services.Settings;
using SpaceLens.Services;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>
/// The explorer: navigation, breadcrumbs, search and actions on items. The folder tree and the item table
/// are separate view models (<see cref="Tree"/> and <see cref="Table"/>); a folder can also be shown as a map.
/// </summary>
public sealed partial class ExplorerViewModel : ObservableObject
{
    private const int MaxSearchResults = 1_000;
    private const int MaxLargestFiles = 500;
    private static readonly TimeSpan SearchDelay = TimeSpan.FromMilliseconds(250);

    private readonly IScanSession _session;
    private readonly IShellService _shell;
    private readonly ICleanupExecutor _executor;
    private readonly IFolderPicker _folderPicker;
    private readonly ISettingsStore _settings;
    private readonly IMessenger _messenger;
    private CancellationTokenSource? _searchCancellation;
    private bool _suppressSearch;
    private string? _resultsCaption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedNode), nameof(CanGoUp))]
    private FolderNode? _currentFolder;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsFolderMode), nameof(ListTitle), nameof(IsMapVisible))]
    private ExplorerMode _mode;

    /// <summary>Show the current folder as a map of tiles instead of a list.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMapVisible), nameof(ShowList), nameof(FooterHint))]
    private bool _showMap;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _listCaption = "";

    public ExplorerViewModel(
        IScanSession session,
        NodePresenter presenter,
        IShellService shell,
        ICleanupExecutor executor,
        IFolderPicker folderPicker,
        ISettingsStore settings,
        IMessenger messenger)
    {
        _session = session;
        _shell = shell;
        _executor = executor;
        _folderPicker = folderPicker;
        _settings = settings;
        _messenger = messenger;
        Tree = new FolderTreeViewModel(presenter);
        Table = new ItemTableViewModel(presenter);
        Table.RowsChanged += (_, _) => UpdateCaption();
        Table.PropertyChanged += OnTableChanged;

        session.ScanLoaded += (_, _) => OnScanLoaded();
        session.ContentsChanged += (_, _) => OnContentsChanged();
    }

    public FolderTreeViewModel Tree { get; }

    public ItemTableViewModel Table { get; }

    public ObservableCollection<Breadcrumb> Breadcrumbs { get; } = [];

    /// <summary>What the details panel and the assistant should focus on.</summary>
    public StorageNode? SelectedNode => Table.Selected?.Node ?? CurrentFolder;

    public bool IsFolderMode => Mode == ExplorerMode.Folder;

    /// <summary>The map only shows a folder; search results and largest files stay a list.</summary>
    public bool IsMapVisible => ShowMap && IsFolderMode;

    /// <summary>The other half of the list/map toggle.</summary>
    public bool ShowList
    {
        get => !ShowMap;
        set => ShowMap = !value;
    }

    public string FooterHint => ShowMap
        ? "Tile colors show file types  ·  click to select, double-click to open"
        : "Sizes are logical file sizes  ·  double-click a folder to open it";

    public bool CanGoUp => CurrentFolder?.Parent is not null;

    public bool HasScan => _session.HasScan;

    public string ListTitle => Mode switch
    {
        ExplorerMode.Search => "Search results",
        ExplorerMode.LargestFiles => "Largest files",
        _ => ""
    };

    [RelayCommand]
    public void NavigateTo(StorageNode? node)
    {
        var folder = node as FolderNode ?? node?.Parent;
        if (folder is null || _session.Current is null)
        {
            return;
        }

        if (folder.IsLink)
        {
            _messenger.Send(new NotificationMessage("Links and junctions aren't followed. Open the folder they point to instead."));
            return;
        }

        var needsListing = Mode != ExplorerMode.Folder || !ReferenceEquals(folder, CurrentFolder);
        if (Mode != ExplorerMode.Folder)
        {
            ClearSearchText();
            Mode = ExplorerMode.Folder;
        }

        if (needsListing)
        {
            CurrentFolder = folder;
            UpdateBreadcrumbs(folder);
            Tree.Select(folder);
            ShowFolderContents(folder);
        }

        Table.Select(ReferenceEquals(node, folder) ? null : node);
    }

    [RelayCommand]
    public async Task ShowLargestFilesAsync(FolderNode? folder)
    {
        folder ??= CurrentFolder;
        if (folder is null || _session.Current is not { } scan)
        {
            return;
        }

        ClearSearchText();
        CurrentFolder = folder;
        UpdateBreadcrumbs(folder);
        Mode = ExplorerMode.LargestFiles;
        IsBusy = true;
        try
        {
            var files = await _session.ReadAsync(_ => StorageAnalyzer.LargestFiles(folder, MaxLargestFiles));
            _resultsCaption = $"The {files.Count:N0} largest files in {NodePresenter.GetDisplayName(folder, scan.Drive)}";
            Table.Show(files, scan.Drive, parentSize: null);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Open(StorageItemViewModel? item)
    {
        if (item is null)
        {
            return;
        }

        if (item.IsFolder || Mode != ExplorerMode.Folder)
        {
            NavigateTo(item.Node);
            return;
        }

        Reveal(item);
    }

    [RelayCommand]
    private void GoUp() => NavigateTo(CurrentFolder?.Parent);

    [RelayCommand]
    private void ExitResults() => NavigateTo(CurrentFolder ?? _session.Current?.Root);

    [RelayCommand]
    private void Reveal(StorageItemViewModel? item)
    {
        var path = item?.FullPath ?? CurrentFolder?.FullPath;
        if (path is not null)
        {
            RunShell(() => _shell.RevealInExplorer(path));
        }
    }

    [RelayCommand]
    private void CopyPath(StorageItemViewModel? item)
    {
        var path = item?.FullPath ?? CurrentFolder?.FullPath;
        if (path is not null)
        {
            if (RunShell(() => _shell.CopyToClipboard(path)))
            {
                _messenger.Send(new NotificationMessage("Path copied."));
            }
        }
    }

    [RelayCommand]
    private void AskAbout(StorageItemViewModel? item)
    {
        var node = item?.Node ?? SelectedNode;
        if (node is null)
        {
            return;
        }

        var question = node.IsFolder
            ? $"What is \"{node.Name}\" and what's taking space in it?"
            : $"What is \"{node.Name}\" and can I safely delete it?";
        _messenger.Send(new AskAssistantMessage(question, node));
    }

    /// <summary>Recycles the one item a context menu or row button was used on.</summary>
    [RelayCommand]
    private Task RecycleAsync(StorageItemViewModel? item)
    {
        var target = item?.Node ?? Table.Selected?.Node;
        return target is null ? Task.CompletedTask : RecycleItemsAsync([target]);
    }

    /// <summary>Recycles every ticked row (the selection bar's button).</summary>
    [RelayCommand]
    private Task RecycleCheckedAsync() => RecycleItemsAsync(Table.CheckedNodes);

    /// <summary>Moves the one item a context menu was used on to a folder on another drive.</summary>
    [RelayCommand]
    private Task MoveAsync(StorageItemViewModel? item)
    {
        var target = item?.Node ?? Table.Selected?.Node;
        return target is null ? Task.CompletedTask : MoveItemsAsync([target]);
    }

    /// <summary>Moves every ticked row (the selection bar's button).</summary>
    [RelayCommand]
    private Task MoveCheckedAsync() => MoveItemsAsync(Table.CheckedNodes);

    /// <summary>Adds an item to the never-touch list, so it's never suggested for cleanup again.</summary>
    [RelayCommand]
    private void NeverClean(StorageItemViewModel? item)
    {
        var node = item?.Node ?? SelectedNode;
        if (node is null)
        {
            return;
        }

        var excluded = _settings.Current.ExcludedFolders;
        if (!excluded.Any(path => PathRules.IsSameOrInside(node.FullPath, path)))
        {
            _settings.Save(_settings.Current with { ExcludedFolders = [.. excluded, node.FullPath] });
        }

        _messenger.Send(new NotificationMessage(
            $"SpaceLens won't suggest \"{node.Name}\" for cleanup anymore. Manage the list in Settings › Never-touch folders."));
    }

    /// <summary>A click on the map: items of this folder are selected; anything deeper opens its folder with it selected.</summary>
    [RelayCommand]
    private void SelectOnMap(StorageNode? node)
    {
        if (node?.Parent is not { } parent)
        {
            return;
        }

        if (!ReferenceEquals(parent, CurrentFolder))
        {
            NavigateTo(parent);
        }

        Table.Select(node);
    }

    /// <summary>A double-click on the map: folders open, files are shown in File Explorer.</summary>
    [RelayCommand]
    private void OpenOnMap(StorageNode? node)
    {
        if (node is FolderNode)
        {
            NavigateTo(node);
        }
        else if (node is not null)
        {
            RunShell(() => _shell.RevealInExplorer(node.FullPath));
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!_suppressSearch)
        {
            _ = SearchAsync(value);
        }
    }

    private async Task RecycleItemsAsync(IReadOnlyList<StorageNode> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var outcome = await _executor.RecycleAsync(targets);
        if (!outcome.WasCancelled || outcome.RecycledCount > 0)
        {
            _messenger.Send(new NotificationMessage(outcome.Describe(), IsError: outcome.FailedCount > 0));
        }
    }

    private async Task MoveItemsAsync(IReadOnlyList<StorageNode> targets)
    {
        if (targets.Count == 0)
        {
            return;
        }

        var destination = _folderPicker.PickFolder("Choose a folder on another drive to move the items to");
        if (destination is null)
        {
            return;
        }

        try
        {
            var outcome = await _executor.MoveAsync(targets, destination);
            if (!outcome.WasCancelled || outcome.MovedCount > 0)
            {
                _messenger.Send(new NotificationMessage(outcome.Describe(), IsError: outcome.FailedCount > 0));
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            _messenger.Send(new NotificationMessage("The items couldn't be moved: " + exception.Message, IsError: true));
        }
    }

    /// <summary>Lists the current view again, for example after cleanup risks changed.</summary>
    public void RefreshListing()
    {
        switch (Mode)
        {
            case ExplorerMode.Folder when CurrentFolder is { } folder:
                var selected = Table.Selected?.Node;
                ShowFolderContents(folder);
                Table.Select(selected);
                break;
            case ExplorerMode.LargestFiles:
                _ = ShowLargestFilesAsync(CurrentFolder);
                break;
            case ExplorerMode.Search:
                _ = SearchAsync(SearchText);
                break;
        }
    }

    private async Task SearchAsync(string text)
    {
        _searchCancellation?.Cancel();
        _searchCancellation = new CancellationTokenSource();
        var token = _searchCancellation.Token;

        try
        {
            await Task.Delay(SearchDelay, token);
            if (_session.Current is not { } scan)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                if (Mode == ExplorerMode.Search && CurrentFolder is { } folder)
                {
                    Mode = ExplorerMode.Folder;
                    ShowFolderContents(folder);
                }

                return;
            }

            IsBusy = true;
            var result = await _session.ReadAsync(s => StorageAnalyzer.Search(s.Root, text, MaxSearchResults, token), token);

            // The user may have navigated or typed again while the search ran; never show stale results.
            token.ThrowIfCancellationRequested();
            Mode = ExplorerMode.Search;
            _resultsCaption = DescribeSearch(text.Trim(), result);
            Table.Show(result.Items, scan.Drive, parentSize: null);
        }
        catch (OperationCanceledException)
        {
            // A newer keystroke or a navigation replaced this search.
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsBusy = false;
            }
        }
    }

    private static string DescribeSearch(string text, SearchResult result)
    {
        if (result.IsTruncated)
        {
            return $"{result.TotalMatches:N0} matches for \"{text}\" · showing the {result.Items.Count:N0} largest";
        }

        var matches = result.TotalMatches == 1 ? "match" : "matches";
        return $"{result.TotalMatches:N0} {matches} for \"{text}\"";
    }

    private void OnScanLoaded()
    {
        var scan = _session.Current!;
        Tree.Load(scan);
        CurrentFolder = null;
        Mode = ExplorerMode.Folder;
        ClearSearchText();
        OnPropertyChanged(nameof(HasScan));
        NavigateTo(scan.Root);
    }

    private void OnContentsChanged()
    {
        Tree.Refresh();
        var folder = CurrentFolder;
        while (folder is not null && !IsAttached(folder))
        {
            folder = folder.Parent;
        }

        switch (Mode)
        {
            case ExplorerMode.Search:
                Table.Retain(IsAttached);
                break;
            case ExplorerMode.LargestFiles:
                _ = ShowLargestFilesAsync(folder);
                break;
            default:
                CurrentFolder = null;
                NavigateTo(folder ?? _session.Current?.Root);
                break;
        }
    }

    private void OnTableChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ItemTableViewModel.Selected))
        {
            OnPropertyChanged(nameof(SelectedNode));
        }
    }

    private void ShowFolderContents(FolderNode folder)
    {
        _resultsCaption = null;
        Table.Show(folder.Children, _session.Current?.Drive, folder.Size);
    }

    private void UpdateCaption()
    {
        if (Mode != ExplorerMode.Folder && _resultsCaption is not null)
        {
            ListCaption = _resultsCaption;
            return;
        }

        var items = Table.MatchingCount == 1 ? "item" : "items";
        var count = $"{Table.MatchingCount:N0} {items}";
        ListCaption = Table.IsTruncated ? $"{count} · showing the {Table.Rows.Count:N0} largest" : count;
    }

    private void UpdateBreadcrumbs(FolderNode folder)
    {
        var drive = _session.Current?.Drive;
        Breadcrumbs.Clear();
        var chain = new List<FolderNode>();
        for (var node = folder; node is not null; node = node.Parent)
        {
            chain.Insert(0, node);
        }

        foreach (var node in chain)
        {
            Breadcrumbs.Add(new Breadcrumb(NodePresenter.GetDisplayName(node, drive), node));
        }
    }

    private void ClearSearchText()
    {
        _searchCancellation?.Cancel();
        IsBusy = false;
        _suppressSearch = true;
        SearchText = "";
        _suppressSearch = false;
    }

    /// <summary>False once an item (or one of its folders) was removed from the scan.</summary>
    private bool IsAttached(StorageNode node)
    {
        for (var current = node; current.Parent is { } parent; current = parent)
        {
            var stillListed = current switch
            {
                FolderNode folder => parent.Folders.Contains(folder),
                FileNode file => parent.Files.Contains(file),
                _ => false
            };
            if (!stillListed)
            {
                return false;
            }
        }

        return true;
    }

    private bool RunShell(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (InvalidOperationException exception)
        {
            _messenger.Send(new NotificationMessage(exception.Message, IsError: true));
            return false;
        }
    }
}
