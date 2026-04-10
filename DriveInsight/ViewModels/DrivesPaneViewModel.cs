using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveInsight.Models;
using DriveInsight.Services;

namespace DriveInsight.ViewModels;

public partial class DrivesPaneViewModel : ViewModelBase
{
    private readonly DriveScanner _scanner = new();
    private const int NameIndentPerDepth = 24;
    private const string FolderIconPathData = "M3,7 A2,2 0 0 1 5,5 H10 L12,7 H19 A2,2 0 0 1 21,9 V18 A2,2 0 0 1 19,20 H5 A2,2 0 0 1 3,18 Z";
    private const string FileIconPathData = "M6,2 H14 L20,8 V22 H6 Z M14,2 V8 H20";
    private readonly Dictionary<string, FileSystemNode> _nodesByPath = [];

    public ObservableCollection<DriveInfo> Drives { get; } = [];
    public ObservableCollection<FileSystemNode> RootNodes { get; } = [];
    public ObservableCollection<DriveFolderRowViewModel> FolderRows { get; } = [];

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

    public string TotalCapacityText => FormatStorage(TotalCapacityBytes);
    public string UsedSpaceText => FormatStorage(UsedSpaceBytes);
    public string AvailableSpaceText => FormatStorage(AvailableSpaceBytes);
    public string UsedPercentageText => $"{UsedPercentage:0}%";

    public DrivesPaneViewModel()
    {
        foreach (var d in _scanner.GetReadyDrives())
        {
            Drives.Add(d);
        }

        SelectedDrive = Drives.FirstOrDefault();
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
                SizeText = $"{item.SizeGb} GB"
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
                SizeText = $"{item.SizeGb} GB",
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
                NameIndent = new Thickness(0, 0, 0, 0)
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
        foreach (var child in children)
        {
            var resolvedBytes = child.Bytes;
            if (child.IsFolder)
            {
                resolvedBytes = await _scanner.GetFolderSizeAsync(child.FullPath);
            }

            var childNode = new FileSystemNode
            {
                Name = child.Name,
                FullPath = child.FullPath,
                IsFolder = child.IsFolder,
                Bytes = resolvedBytes,
                SizeText = child.IsFolder
                    ? $"{resolvedBytes / 1024d / 1024d / 1024d:N2} GB"
                    : $"{resolvedBytes / 1024d / 1024d:N2} MB"
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
        if (row is null || !row.IsFolder || row.HasLoadedChildren)
        {
            return;
        }

        if (!_nodesByPath.TryGetValue(row.FullPath, out var node))
        {
            return;
        }

        row.Children.Clear();
        await EnsureChildrenLoadedAsync(node);

        var children = node.Children.Where(c => !c.IsPlaceholder).ToList();
        var totalBytes = children.Sum(c => c.Bytes);

        foreach (var child in children)
        {
            var usageRatio = totalBytes > 0 ? child.Bytes / (double)totalBytes : 0;
            var clampedRatio = Math.Clamp(usageRatio, 0.0, 1.0);

            row.Children.Add(new DriveFolderRowViewModel
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
                NameIndent = new Thickness((row.Depth + 1) * NameIndentPerDepth, 0, 0, 0),
                IsChildRow = true
            });

            if (child.IsFolder)
            {
                row.Children[^1].Children.Add(CreatePlaceholderRow(row.Depth + 2));
            }
        }

        row.HasLoadedChildren = true;
    }

    private static DriveFolderRowViewModel CreatePlaceholderRow(int depth) => new()
    {
        Name = "Loading...",
        FullPath = string.Empty,
        SizeText = string.Empty,
        UsagePercent = 0,
        UsageBrush = "#1E63FF",
        IconPathData = string.Empty,
        IconContainerSize = 0,
        IconSize = 0,
        IconBackground = "Transparent",
        IconFill = "Transparent",
        TextSize = 12,
        UsageBarHeight = 0,
        IsFolder = false,
        Depth = depth,
        NameIndent = new Thickness(0),
        IsPlaceholder = true
    };

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

    private static string FormatStorage(long bytes)
    {
        const double scale = 1024d;
        if (bytes < scale)
        {
            return $"{bytes} B";
        }

        var units = new[] { "KB", "MB", "GB", "TB", "PB" };
        var value = bytes / scale;
        var unitIndex = 0;

        while (value >= scale && unitIndex < units.Length - 1)
        {
            value /= scale;
            unitIndex++;
        }

        return $"{value:0.##} {units[unitIndex]}";
    }
}
