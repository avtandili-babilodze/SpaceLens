using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using SpaceLens.Core.Models;
using SpaceLens.Core.Services.Scanning;
using SpaceLens.Core.Services.Session;
using SpaceLens.Services;
using SpaceLens.ViewModels.Messages;

namespace SpaceLens.ViewModels;

/// <summary>The right-hand panel: what the selected item is, how its space splits up, and its largest files.</summary>
public sealed partial class DetailsViewModel(IScanSession session, NodePresenter presenter, IShellService shell, IMessenger messenger)
    : ObservableObject
{
    private const int LargestItemCount = 5;
    private const int TypeSegments = 4;

    private int _version;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNode), nameof(IsFolder))]
    private StorageNode? _node;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _path = "";

    [ObservableProperty]
    private string _sizeText = "";

    [ObservableProperty]
    private string _itemsText = "";

    [ObservableProperty]
    private NodeIcon _icon;

    [ObservableProperty]
    private RiskAssessment? _risk;

    [ObservableProperty]
    private string? _note;

    [ObservableProperty]
    private IReadOnlyList<BreakdownSegment> _segments = [];

    [ObservableProperty]
    private IReadOnlyList<StorageItemViewModel> _largestItems = [];

    [ObservableProperty]
    private BreakdownMode _breakdownMode = BreakdownMode.Contents;

    public bool HasNode => Node is not null;

    public bool IsFolder => Node is FolderNode;

    public async Task ShowAsync(StorageNode? node)
    {
        var version = ++_version;
        Node = node;
        if (node is null || session.Current is not { } scan)
        {
            Segments = [];
            LargestItems = [];
            return;
        }

        var assessment = presenter.Assess(node);
        Name = NodePresenter.GetDisplayName(node, scan.Drive);
        Path = node.FullPath;
        Icon = presenter.GetIcon(node);
        Risk = assessment;
        SizeText = ByteSize.Format(node.Size);
        ItemsText = node is FolderNode folder
            ? $"{folder.FileCount + folder.FolderCount:N0} items"
            : FileTypeClassifier.GetTypeName(node);
        Note = DescribeCaveats(node, scan);

        if (node is not FolderNode target)
        {
            Segments = [];
            LargestItems = [];
            return;
        }

        var mode = BreakdownMode;
        var unaccounted = target.IsRoot ? scan.UnaccountedBytes : 0;
        try
        {
            var (segments, largest) = await session.ReadAsync(_ =>
            {
                var breakdown = mode == BreakdownMode.Contents
                    ? StorageAnalyzer.ChildBreakdown(target, top: 3, unaccounted)
                    : MergeSmallTypes(StorageAnalyzer.TypeBreakdown(target));
                return (breakdown, StorageAnalyzer.LargestFiles(target, LargestItemCount));
            });

            // Selection may have moved on while the breakdown was computed.
            if (version != _version)
            {
                return;
            }

            Segments = segments;
            LargestItems = largest.Select(f => new StorageItemViewModel(f, presenter)).ToList();
        }
        catch (InvalidOperationException)
        {
            // The scan was replaced while this was loading; the next selection refreshes the panel.
        }
    }

    [RelayCommand]
    private Task SetBreakdownModeAsync(BreakdownMode mode)
    {
        BreakdownMode = mode;
        return ShowAsync(Node);
    }

    [RelayCommand]
    private void Reveal()
    {
        if (Node is null)
        {
            return;
        }

        var path = Node.FullPath;
        RunShell(() => shell.RevealInExplorer(path));
    }

    [RelayCommand]
    private void CopyPath()
    {
        if (Node is null)
        {
            return;
        }

        var path = Node.FullPath;
        if (RunShell(() => shell.CopyToClipboard(path)))
        {
            messenger.Send(new NotificationMessage("Path copied."));
        }
    }

    [RelayCommand]
    private void Ask()
    {
        if (Node is not null)
        {
            messenger.Send(new AskAssistantMessage($"What is taking up space in \"{Name}\", and what could I clean there?", Node));
        }
    }

    [RelayCommand]
    private void Open(StorageNode? node)
    {
        if (node is not null)
        {
            messenger.Send(new ShowInExplorerMessage(node));
        }
    }

    [RelayCommand]
    private void OpenItem(StorageItemViewModel? item) => Open(item?.Node);

    [RelayCommand]
    private void SeeAllLargest()
    {
        if (Node is FolderNode folder)
        {
            messenger.Send(new ShowLargestFilesMessage(folder));
        }
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
            messenger.Send(new NotificationMessage(exception.Message, IsError: true));
            return false;
        }
    }

    private static string? DescribeCaveats(StorageNode node, ScanResult scan)
    {
        if (node is FolderNode { IsUnreadable: true })
        {
            return "Windows denied access to this folder, so its contents aren't counted.";
        }

        if (node is FolderNode { IsLink: true })
        {
            return "This is a link. Its contents are counted where the target folder lives.";
        }

        if (node is FileNode { IsCloudOnly: true } cloudFile)
        {
            return $"Online-only file ({ByteSize.Format(cloudFile.Length)} in the cloud). It uses almost no space on this PC.";
        }

        if (!scan.IsComplete && ReferenceEquals(node, scan.Root))
        {
            return "The scan was stopped early, so these totals are partial.";
        }

        if (!ReferenceEquals(node, scan.Root) || !scan.IsDriveRoot || scan.Drive is null)
        {
            return null;
        }

        if (scan.UnaccountedBytes > 0)
        {
            return $"Windows reports {ByteSize.Format(scan.UnaccountedBytes)} more in use than the scan could measure: "
                + "system files, folders you can't read, and file-system overhead.";
        }

        return scan.Root.Size > scan.Drive.UsedBytes
            ? "The scan adds up to a little more than Windows reports as used. Files with several names (hard links, "
                + "common in Windows\\WinSxS) are counted once per name, and compressed files at their full size."
            : null;
    }

    private static List<BreakdownSegment> MergeSmallTypes(List<BreakdownSegment> segments)
    {
        if (segments.Count <= TypeSegments + 1)
        {
            return segments;
        }

        var rest = segments.Skip(TypeSegments).ToList();
        var merged = new BreakdownSegment("Other", rest.Sum(s => s.Bytes), rest.Sum(s => s.Fraction), StorageAnalyzer.OtherColor);
        return [.. segments.Take(TypeSegments), merged];
    }
}
