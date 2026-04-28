using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveInsight.Models;
using DriveInsight.Services;
using DriveInsight.Utilities;

namespace DriveInsight.ViewModels;

public partial class DrivesPaneViewModel : ViewModelBase
{
    private readonly DriveScanner _scanner = new();
    private Func<Task>? _refreshDashboardAsync;
    private const string FolderIconPathData = "M3,7 A2,2 0 0 1 5,5 H10 L12,7 H19 A2,2 0 0 1 21,9 V18 A2,2 0 0 1 19,20 H5 A2,2 0 0 1 3,18 Z";
    private const string FileIconPathData = "M6,2 H14 L20,8 V22 H6 Z M14,2 V8 H20";
    private readonly Dictionary<string, FileSystemNode> _nodesByPath = [];
    private readonly HashSet<string> _expandingRows = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<DriveInfo> Drives { get; } = [];
    public ObservableCollection<FileSystemNode> RootNodes { get; } = [];
    public ObservableCollection<DriveFolderRowViewModel> FolderRows { get; } = [];
    public HierarchicalTreeDataGridSource<DriveFolderRowViewModel> FolderRowsSource { get; }

    [ObservableProperty]
    private DriveInfo? selectedDrive;

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string status = "Ready";

    private long _totalCapacityBytes;
    private long _usedSpaceBytes;
    private long _availableSpaceBytes;
    private double _usedPercentage;

    public long TotalCapacityBytes
    {
        get => _totalCapacityBytes;
        private set
        {
            if (SetProperty(ref _totalCapacityBytes, value))
            {
                OnPropertyChanged(nameof(TotalCapacityText));
            }
        }
    }

    public long UsedSpaceBytes
    {
        get => _usedSpaceBytes;
        private set
        {
            if (SetProperty(ref _usedSpaceBytes, value))
            {
                OnPropertyChanged(nameof(UsedSpaceText));
            }
        }
    }

    public long AvailableSpaceBytes
    {
        get => _availableSpaceBytes;
        private set
        {
            if (SetProperty(ref _availableSpaceBytes, value))
            {
                OnPropertyChanged(nameof(AvailableSpaceText));
            }
        }
    }

    public double UsedPercentage
    {
        get => _usedPercentage;
        private set
        {
            if (SetProperty(ref _usedPercentage, value))
            {
                OnPropertyChanged(nameof(UsedPercentageText));
            }
        }
    }

    public string TotalCapacityText => StorageFormatter.Format(TotalCapacityBytes, 2);
    public string UsedSpaceText => StorageFormatter.Format(UsedSpaceBytes, 2);
    public string AvailableSpaceText => StorageFormatter.Format(AvailableSpaceBytes, 2);
    public string UsedPercentageText => $"{UsedPercentage:0}%";

    public DrivesPaneViewModel(Func<Task>? refreshDashboardAsync = null)
    {
        _refreshDashboardAsync = refreshDashboardAsync;
        FolderRowsSource = CreateFolderRowsSource();
        RefreshAvailableDrives();
    }

    public void SetDashboardRefresh(Func<Task> refreshDashboardAsync)
    {
        _refreshDashboardAsync = refreshDashboardAsync;
    }

    [RelayCommand]
    private async Task RefreshAll()
    {
        if (_refreshDashboardAsync is null)
        {
            RefreshAvailableDrives();
            return;
        }

        await _refreshDashboardAsync();
    }

    public void RefreshAvailableDrives()
    {
        _scanner.ClearCache();
        var previouslySelectedName = SelectedDrive?.Name;
        var refreshed = _scanner.GetReadyDrives().ToList();

        Drives.Clear();
        foreach (var drive in refreshed)
        {
            Drives.Add(drive);
        }

        var nextSelection = refreshed.FirstOrDefault(d => string.Equals(d.Name, previouslySelectedName, StringComparison.OrdinalIgnoreCase))
                            ?? refreshed.FirstOrDefault();

        if (!ReferenceEquals(SelectedDrive, nextSelection))
        {
            SelectedDrive = nextSelection;
        }

        if (SelectedDrive is null)
        {
            RootNodes.Clear();
            FolderRows.Clear();
            _nodesByPath.Clear();
            Status = "No drives detected.";
        }
        else if (!string.Equals(Status, "Ready", StringComparison.Ordinal))
        {
            Status = "Ready";
        }

        RefreshDriveCapacity();
    }

    [RelayCommand]
    private async Task ScanSelectedDrive()
    {
        if (SelectedDrive is null)
        {
            return;
        }

        IsBusy = true;
        _scanner.ClearCache();
        Status = $"Scanning {SelectedDrive.Name}...";
        RootNodes.Clear();
        FolderRows.Clear();
        _nodesByPath.Clear();

        var top = await _scanner.GetTopFoldersAsync(SelectedDrive.RootDirectory.FullName);
        var totalBytes = 0L;
        foreach (var item in top)
        {
            totalBytes += item.Bytes;
        }

        foreach (var item in top)
        {
            var folderNode = new FileSystemNode
            {
                Name = item.Name,
                FullPath = item.FullPath,
                IsFolder = true,
                Bytes = item.Bytes,
                SizeText = StorageFormatter.Format(item.Bytes, 2)
            };

            folderNode.Children.Add(CreatePlaceholderNode());
            RootNodes.Add(folderNode);
            _nodesByPath[folderNode.FullPath] = folderNode;

            var usageRatio = totalBytes > 0 ? item.Bytes / (double)totalBytes : 0;
            var clampedRatio = Math.Clamp(usageRatio, 0.0, 1.0);
            FolderRows.Add(new DriveFolderRowViewModel
            {
                Name = item.Name,
                FullPath = item.FullPath,
                SizeText = StorageFormatter.Format(item.Bytes, 2),
                UsagePercent = clampedRatio * 100.0,
                UsageBrush = clampedRatio >= 0.3 ? "#C75000" : "#1E63FF",
                IconPathData = FolderIconPathData,
                IconContainerSize = 30,
                IconSize = 16,
                IconBackground = "#EEF4FF",
                IconFill = "#1E63FF",
                TextSize = 13,
                UsageBarHeight = 8,
                IsFolder = true,
                Depth = 0,
                NameIndent = new Thickness(0, 0, 0, 0),
                RowOffsetX = 0
            });
            FolderRows[^1].Children.Add(CreatePlaceholderRow(1));
        }

        Status = $"Done. {FolderRows.Count} folders loaded.";
        IsBusy = false;
    }

    partial void OnSelectedDriveChanged(DriveInfo? value)
    {
        RefreshDriveCapacity();
    }

    public async Task EnsureChildrenLoadedAsync(FileSystemNode? node)
    {
        if (node is null || !node.IsFolder || node.HasLoadedChildren)
        {
            return;
        }

        Status = $"Loading {node.Name}...";
        node.Children.Clear();

        var children = await _scanner.GetImmediateChildrenAsync(node.FullPath);
        var folderPaths = children
            .Where(child => child.IsFolder)
            .Select(child => child.FullPath)
            .ToList();

        var folderSizes = folderPaths.Count > 0
            ? await _scanner.GetFolderSizesAsync(folderPaths)
            : new Dictionary<string, long>();

        foreach (var child in children)
        {
            var resolvedBytes = child.Bytes;
            if (child.IsFolder)
            {
                resolvedBytes = folderSizes.TryGetValue(child.FullPath, out var folderBytes) ? folderBytes : 0L;
            }

            var childNode = new FileSystemNode
            {
                Name = child.Name,
                FullPath = child.FullPath,
                IsFolder = child.IsFolder,
                Bytes = resolvedBytes,
                SizeText = StorageFormatter.Format(resolvedBytes, 2)
            };

            if (childNode.IsFolder)
            {
                childNode.Children.Add(CreatePlaceholderNode());
            }

            node.Children.Add(childNode);
            _nodesByPath[childNode.FullPath] = childNode;
        }

        node.HasLoadedChildren = true;
        Status = $"Loaded {node.Children.Count} items in {node.Name}.";
    }

    private static FileSystemNode CreatePlaceholderNode() => new()
    {
        Name = "Loading...",
        IsPlaceholder = true
    };

    public async Task ExpandFolderRowAsync(DriveFolderRowViewModel? row)
    {
        if (row is null || !row.IsFolder || row.HasLoadedChildren || _expandingRows.Contains(row.FullPath))
        {
            return;
        }

        if (!_nodesByPath.TryGetValue(row.FullPath, out var node))
        {
            return;
        }

        _expandingRows.Add(row.FullPath);
        try
        {
            await EnsureChildrenLoadedAsync(node);

            var children = node.Children.Where(c => !c.IsPlaceholder).ToList();
            var totalBytes = children.Sum(c => c.Bytes);
            var newRows = new List<DriveFolderRowViewModel>(children.Count);

            foreach (var child in children)
            {
                var usageRatio = totalBytes > 0 ? child.Bytes / (double)totalBytes : 0;
                var clampedRatio = Math.Clamp(usageRatio, 0.0, 1.0);

                var childRow = new DriveFolderRowViewModel
                {
                    Name = child.Name,
                    FullPath = child.FullPath,
                    SizeText = child.SizeText,
                    UsagePercent = clampedRatio * 100.0,
                    UsageBrush = clampedRatio >= 0.3 ? "#C75000" : "#1E63FF",
                    IconPathData = child.IsFolder ? FolderIconPathData : FileIconPathData,
                    IconContainerSize = 20,
                    IconSize = 12,
                    IconBackground = "#F1F5FB",
                    IconFill = "#5C6D86",
                    TextSize = 12,
                    UsageBarHeight = 6,
                    IsFolder = child.IsFolder,
                    Depth = row.Depth + 1,
                    NameIndent = new Thickness(0),
                    RowOffsetX = 0,
                    IsChildRow = true
                };

                if (child.IsFolder)
                {
                    childRow.Children.Add(CreatePlaceholderRow(row.Depth + 2));
                }

                newRows.Add(childRow);
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                row.Children.Clear();
                foreach (var childRow in newRows)
                {
                    row.Children.Add(childRow);
                }

                row.HasLoadedChildren = true;
            });
        }
        finally
        {
            _expandingRows.Remove(row.FullPath);
        }
    }

    private void RefreshDriveCapacity()
    {
        if (SelectedDrive is null || !SelectedDrive.IsReady)
        {
            TotalCapacityBytes = 0;
            UsedSpaceBytes = 0;
            AvailableSpaceBytes = 0;
            UsedPercentage = 0;
            return;
        }

        var total = SelectedDrive.TotalSize;
        var available = SelectedDrive.AvailableFreeSpace;
        var used = Math.Max(0, total - available);
        var ratio = total > 0 ? used / (double)total : 0d;

        TotalCapacityBytes = total;
        UsedSpaceBytes = used;
        AvailableSpaceBytes = available;
        UsedPercentage = Math.Clamp(ratio * 100d, 0d, 100d);
    }

    private static DriveFolderRowViewModel CreatePlaceholderRow(int depth) => new()
    {
        Name = "Loading...",
        FullPath = string.Empty,
        SizeText = string.Empty,
        UsagePercent = 0,
        UsageBrush = "#1E63FF",
        IconPathData = FolderIconPathData,
        IconContainerSize = 0,
        IconSize = 0,
        IconBackground = "Transparent",
        IconFill = "Transparent",
        TextSize = 12,
        UsageBarHeight = 0,
        IsFolder = false,
        Depth = depth,
        NameIndent = new Thickness(0),
        RowOffsetX = 0,
        IsPlaceholder = true
    };

    private HierarchicalTreeDataGridSource<DriveFolderRowViewModel> CreateFolderRowsSource()
    {
        var source = new HierarchicalTreeDataGridSource<DriveFolderRowViewModel>(FolderRows);

        source.Columns.Add(
            new HierarchicalExpanderColumn<DriveFolderRowViewModel>(
                new TextColumn<DriveFolderRowViewModel, string>(
                    "FOLDER NAME",
                    row => row.Name,
                    new GridLength(3, GridUnitType.Star),
                    new TextColumnOptions<DriveFolderRowViewModel>
                    {
                        TextTrimming = TextTrimming.CharacterEllipsis
                    }),
                row => GetRowChildren(row),
                row => row.IsFolder,
                row => row.IsExpanded));

        source.Columns.Add(
            new TextColumn<DriveFolderRowViewModel, string>(
                "LOCATION",
                row => row.FullPath,
                new GridLength(4, GridUnitType.Star),
                new TextColumnOptions<DriveFolderRowViewModel>
                {
                    TextTrimming = TextTrimming.CharacterEllipsis
                }));

        source.Columns.Add(
            new TextColumn<DriveFolderRowViewModel, string>(
                "SIZE",
                row => row.SizeText,
                new GridLength(2, GridUnitType.Star)));

        source.Columns.Add(
            new TextColumn<DriveFolderRowViewModel, string>(
                "USAGE SHARE",
                row => $"{row.UsagePercent:0.##}%",
                new GridLength(2, GridUnitType.Star)));

        return source;
    }

    private IEnumerable<DriveFolderRowViewModel> GetRowChildren(DriveFolderRowViewModel row)
    {
        if (row.IsFolder && !row.HasLoadedChildren)
        {
            _ = ExpandFolderRowAsync(row);
        }

        return row.Children;
    }
}
