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
    private ElevatedDeepScanSession? _deepScanSession;
    private const string FolderIconPathData = "M3,7 A2,2 0 0 1 5,5 H10 L12,7 H19 A2,2 0 0 1 21,9 V18 A2,2 0 0 1 19,20 H5 A2,2 0 0 1 3,18 Z";
    private const string FileIconPathData = "M6,2 H14 L20,8 V22 H6 Z M14,2 V8 H20";
    private const string OtherScannedSpaceName = "Other scanned files";
    private const string NormalUnscannedSpaceName = "System / protected / unscanned";
    private const string DeepUnattributedSpaceName = "Unattributed system space";
    private readonly Dictionary<string, FileSystemNode> _nodesByPath = [];
    private readonly HashSet<string> _expandingRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _topLevelScannedPaths = new(StringComparer.OrdinalIgnoreCase);

    public ObservableCollection<DriveInfo> Drives { get; } = [];
    public ObservableCollection<FileSystemNode> RootNodes { get; } = [];
    public ObservableCollection<DriveFolderRowViewModel> FolderRows { get; } = [];
    public HierarchicalTreeDataGridSource<DriveFolderRowViewModel> FolderRowsSource { get; }
    public IAsyncRelayCommand? ExportCsvCommand { get; set; }

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
        _ = StopDeepScanSessionAsync();
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

    [RelayCommand(CanExecute = nameof(CanScanSelectedDrive))]
    private async Task ScanSelectedDrive()
    {
        if (SelectedDrive is null)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await StopDeepScanSessionAsync();
            _scanner.ClearCache();
            Status = $"Scanning {SelectedDrive.Name}...";

            var scan = await _scanner.GetTopFolderScanAsync(SelectedDrive.RootDirectory.FullName);
            PopulateFolderRows(scan.TopFolders, StorageScanMode.Normal, scan.RootBytes);

            Status = BuildScanCompleteStatus();
        }
        catch
        {
            Status = $"Could not scan {SelectedDrive.Name}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanScanSelectedDrive))]
    private async Task DeepScanSelectedDrive()
    {
        if (SelectedDrive is null)
        {
            return;
        }

        var processPath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(processPath))
        {
            Status = "Could not start deep scan.";
            return;
        }

        var driveName = SelectedDrive.Name;
        IsBusy = true;
        Status = $"Requesting administrator access for {driveName}";

        try
        {
            if (_deepScanSession is not null)
            {
                await _deepScanSession.DisposeAsync();
                _deepScanSession = null;
            }

            _deepScanSession = await ElevatedDeepScanSession.StartAsync(processPath);
            if (_deepScanSession is null)
            {
                Status = "Deep scan was cancelled.";
                return;
            }

            Status = $"Deep scanning {driveName}...";
            var scan = await _deepScanSession.ScanDriveAsync(driveName);
            PopulateFolderRows(scan.TopFolders, StorageScanMode.Deep, scan.RootBytes);
            Status = BuildScanCompleteStatus("Deep scan done");
        }
        catch (System.ComponentModel.Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223)
        {
            Status = "Deep scan was cancelled.";
        }
        catch
        {
            Status = $"Could not deep scan {driveName}.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PopulateFolderRows(
        IReadOnlyCollection<FolderStat> top,
        StorageScanMode mode,
        long scannedBytes)
    {
        RootNodes.Clear();
        FolderRows.Clear();
        _nodesByPath.Clear();
        _topLevelScannedPaths.Clear();

        var topBytes = top.Sum(item => item.Bytes);
        scannedBytes = Math.Max(scannedBytes, topBytes);
        var totalBytes = Math.Max(UsedSpaceBytes, scannedBytes);

        foreach (var item in top)
        {
            _topLevelScannedPaths.Add(item.FullPath);

            var folderNode = new FileSystemNode
            {
                Name = item.Name,
                FullPath = item.FullPath,
                IsFolder = true,
                Bytes = item.Bytes,
                SizeText = StorageFormatter.Format(item.Bytes, 2),
                ScanMode = mode
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
                SizeBytes = item.Bytes,
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
                RowOffsetX = 0,
                ScanMode = mode
            });
            FolderRows[^1].Children.Add(CreatePlaceholderRow(1));
        }

        var otherScannedBytes = Math.Max(0, scannedBytes - topBytes);
        if (otherScannedBytes > 0)
        {
            AddSyntheticUsageRow(
                OtherScannedSpaceName,
                SelectedDrive?.RootDirectory.FullName ?? string.Empty,
                otherScannedBytes,
                totalBytes,
                mode,
                "#1E63FF",
                "#EEF4FF",
                "#1E63FF",
                SyntheticDriveRowKind.OtherScannedFiles,
                true);
        }

        var unscannedBytes = Math.Max(0, UsedSpaceBytes - scannedBytes);
        if (unscannedBytes > 0)
        {
            AddSyntheticUsageRow(
                GetUnscannedSpaceName(mode),
                SelectedDrive?.RootDirectory.FullName ?? string.Empty,
                unscannedBytes,
                totalBytes,
                mode,
                "#C75000",
                "#FFF3E8",
                "#C75000",
                SyntheticDriveRowKind.UnattributedSpace,
                true);
        }
    }

    private void AddSyntheticUsageRow(
        string name,
        string fullPath,
        long bytes,
        long totalBytes,
        StorageScanMode mode,
        string usageBrush,
        string iconBackground,
        string iconFill,
        SyntheticDriveRowKind syntheticKind,
        bool expandable)
    {
        var usageRatio = totalBytes > 0 ? bytes / (double)totalBytes : 0;
        var clampedRatio = Math.Clamp(usageRatio, 0.0, 1.0);

        var row = new DriveFolderRowViewModel
        {
            Name = name,
            FullPath = fullPath,
            SizeText = StorageFormatter.Format(bytes, 2),
            SizeBytes = bytes,
            UsagePercent = clampedRatio * 100.0,
            UsageBrush = usageBrush,
            IconPathData = FileIconPathData,
            IconContainerSize = 30,
            IconSize = 16,
            IconBackground = iconBackground,
            IconFill = iconFill,
            TextSize = 13,
            UsageBarHeight = 8,
            IsFolder = expandable,
            Depth = 0,
            NameIndent = new Thickness(0, 0, 0, 0),
            RowOffsetX = 0,
            ScanMode = mode,
            SyntheticKind = syntheticKind
        };

        if (expandable)
        {
            row.Children.Add(CreatePlaceholderRow(1));
        }

        FolderRows.Add(row);
    }

    private static string GetUnscannedSpaceName(StorageScanMode mode)
    {
        return mode == StorageScanMode.Deep
            ? DeepUnattributedSpaceName
            : NormalUnscannedSpaceName;
    }

    private string BuildScanCompleteStatus(string prefix = "Done")
    {
        var folderCount = FolderRows.Count(row => row.IsFolder);
        var unscannedRows = FolderRows
            .Where(row =>
                string.Equals(row.Name, NormalUnscannedSpaceName, StringComparison.Ordinal) ||
                string.Equals(row.Name, DeepUnattributedSpaceName, StringComparison.Ordinal))
            .ToList();
        var unscannedBytes = unscannedRows
            .Sum(row => row.SizeBytes);

        if (unscannedBytes <= 0)
        {
            return $"{prefix}. {folderCount} folders.";
        }

        var hasDeepUnattributedRow = unscannedRows.Any(row =>
            string.Equals(row.Name, DeepUnattributedSpaceName, StringComparison.Ordinal));

        var description = hasDeepUnattributedRow
            ? "unattributed"
            : "unscanned";

        return $"{prefix}. {folderCount} folders. {StorageFormatter.Format(unscannedBytes, 2)} {description}.";
    }

    private async Task StopDeepScanSessionAsync()
    {
        var session = _deepScanSession;
        if (session is null)
        {
            return;
        }

        _deepScanSession = null;
        try
        {
            await session.DisposeAsync();
        }
        catch
        {
        }
    }

    private bool CanScanSelectedDrive()
    {
        return !IsBusy && SelectedDrive is not null;
    }

    partial void OnSelectedDriveChanged(DriveInfo? value)
    {
        RefreshDriveCapacity();
        ScanSelectedDriveCommand.NotifyCanExecuteChanged();
        DeepScanSelectedDriveCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        ScanSelectedDriveCommand.NotifyCanExecuteChanged();
        DeepScanSelectedDriveCommand.NotifyCanExecuteChanged();
    }

    public async Task EnsureChildrenLoadedAsync(FileSystemNode? node)
    {
        if (node is null || !node.IsFolder || node.HasLoadedChildren)
        {
            return;
        }

        Status = $"Loading {node.Name}...";
        node.Children.Clear();

        var children = node.ScanMode == StorageScanMode.Deep
            ? await GetDeepChildrenAsync(node.FullPath)
            : await GetNormalChildrenAsync(node.FullPath);

        foreach (var child in children)
        {
            var childNode = new FileSystemNode
            {
                Name = child.Name,
                FullPath = child.FullPath,
                IsFolder = child.IsFolder,
                Bytes = child.Bytes,
                SizeText = StorageFormatter.Format(child.Bytes, 2),
                ScanMode = node.ScanMode
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

    private async Task<List<FileSystemEntry>> GetNormalChildrenAsync(string folderPath)
    {
        var children = await _scanner.GetImmediateChildrenAsync(folderPath);
        var folderPaths = children
            .Where(child => child.IsFolder)
            .Select(child => child.FullPath)
            .ToList();

        if (folderPaths.Count == 0)
        {
            return children;
        }

        var folderSizes = await _scanner.GetFolderSizesAsync(folderPaths);
        return children
            .Select(child => child.IsFolder && folderSizes.TryGetValue(child.FullPath, out var folderBytes)
                ? new FileSystemEntry
                {
                    Name = child.Name,
                    FullPath = child.FullPath,
                    IsFolder = child.IsFolder,
                    Bytes = folderBytes
                }
                : child)
            .ToList();
    }

    private async Task<List<FileSystemEntry>> GetDeepChildrenAsync(string folderPath)
    {
        try
        {
            if (_deepScanSession is null)
            {
                Status = "Run Deep Scan again before expanding protected folders.";
                return [];
            }

            return await _deepScanSession.LoadChildrenAsync(folderPath);
        }
        catch
        {
            Status = $"Could not deep expand {Path.GetFileName(folderPath)}.";
            return [];
        }
    }

    private static FileSystemNode CreatePlaceholderNode() => new()
    {
        Name = "Loading...",
        IsPlaceholder = true
    };

    public async Task ExpandFolderRowAsync(DriveFolderRowViewModel? row)
    {
        if (row is null || !row.IsFolder || row.HasLoadedChildren || _expandingRows.Contains(GetExpansionKey(row)))
        {
            return;
        }

        if (row.SyntheticKind != SyntheticDriveRowKind.None)
        {
            await ExpandSyntheticRowAsync(row);
            return;
        }

        if (!_nodesByPath.TryGetValue(row.FullPath, out var node))
        {
            return;
        }

        var expansionKey = GetExpansionKey(row);
        _expandingRows.Add(expansionKey);
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
                    SizeBytes = child.Bytes,
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
                    IsChildRow = true,
                    ScanMode = child.ScanMode
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
            _expandingRows.Remove(expansionKey);
        }
    }

    private async Task ExpandSyntheticRowAsync(DriveFolderRowViewModel row)
    {
        var expansionKey = GetExpansionKey(row);
        _expandingRows.Add(expansionKey);
        try
        {
            var childRows = row.SyntheticKind switch
            {
                SyntheticDriveRowKind.OtherScannedFiles => await BuildOtherScannedRowsAsync(row),
                SyntheticDriveRowKind.UnattributedSpace => BuildUnattributedRows(row),
                _ => []
            };

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                row.Children.Clear();
                foreach (var childRow in childRows)
                {
                    row.Children.Add(childRow);
                }

                row.HasLoadedChildren = true;
            });
        }
        finally
        {
            _expandingRows.Remove(expansionKey);
        }
    }

    private async Task<List<DriveFolderRowViewModel>> BuildOtherScannedRowsAsync(DriveFolderRowViewModel row)
    {
        var rootPath = SelectedDrive?.RootDirectory.FullName;
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return [];
        }

        var children = row.ScanMode == StorageScanMode.Deep
            ? await GetDeepChildrenAsync(rootPath)
            : await GetNormalChildrenAsync(rootPath);

        var otherChildren = children
            .Where(child => !_topLevelScannedPaths.Contains(child.FullPath))
            .OrderByDescending(child => child.Bytes)
            .ToList();

        return CreateChildRows(otherChildren, row, row.SizeBytes);
    }

    private static List<DriveFolderRowViewModel> BuildUnattributedRows(DriveFolderRowViewModel row)
    {
        return
        [
            new DriveFolderRowViewModel
            {
                Name = "Not exposed as enumerable files",
                FullPath = row.FullPath,
                SizeText = row.SizeText,
                SizeBytes = row.SizeBytes,
                UsagePercent = 100,
                UsageBrush = row.UsageBrush,
                IconPathData = FileIconPathData,
                IconContainerSize = 20,
                IconSize = 12,
                IconBackground = "#FFF3E8",
                IconFill = "#C75000",
                TextSize = 12,
                UsageBarHeight = 6,
                IsFolder = false,
                Depth = row.Depth + 1,
                NameIndent = new Thickness(0),
                RowOffsetX = 0,
                IsChildRow = true,
                ScanMode = row.ScanMode
            }
        ];
    }

    private static List<DriveFolderRowViewModel> CreateChildRows(
        IReadOnlyCollection<FileSystemEntry> children,
        DriveFolderRowViewModel parent,
        long totalBytes)
    {
        var rows = new List<DriveFolderRowViewModel>(children.Count);
        foreach (var child in children)
        {
            var usageRatio = totalBytes > 0 ? child.Bytes / (double)totalBytes : 0;
            var clampedRatio = Math.Clamp(usageRatio, 0.0, 1.0);

            var childRow = new DriveFolderRowViewModel
            {
                Name = child.Name,
                FullPath = child.FullPath,
                SizeText = StorageFormatter.Format(child.Bytes, 2),
                SizeBytes = child.Bytes,
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
                Depth = parent.Depth + 1,
                NameIndent = new Thickness(0),
                RowOffsetX = 0,
                IsChildRow = true,
                ScanMode = parent.ScanMode
            };

            if (child.IsFolder)
            {
                childRow.Children.Add(CreatePlaceholderRow(parent.Depth + 2));
            }

            rows.Add(childRow);
        }

        return rows;
    }

    private static string GetExpansionKey(DriveFolderRowViewModel row)
    {
        return row.SyntheticKind == SyntheticDriveRowKind.None
            ? row.FullPath
            : $"{row.SyntheticKind}:{row.FullPath}";
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
        SizeBytes = 0,
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
                new GridLength(2, GridUnitType.Star),
                new TextColumnOptions<DriveFolderRowViewModel>
                {
                    CompareAscending = CompareSizeAscending,
                    CompareDescending = CompareSizeDescending
                }));

        source.Columns.Add(
            new TextColumn<DriveFolderRowViewModel, string>(
                "USAGE SHARE",
                row => $"{row.UsagePercent:0.##}%",
                new GridLength(2, GridUnitType.Star),
                new TextColumnOptions<DriveFolderRowViewModel>
                {
                    CompareAscending = CompareUsageAscending,
                    CompareDescending = CompareUsageDescending
                }));

        return source;
    }

    private static int CompareSizeAscending(DriveFolderRowViewModel? left, DriveFolderRowViewModel? right)
    {
        return Nullable.Compare(left?.SizeBytes, right?.SizeBytes);
    }

    private static int CompareSizeDescending(DriveFolderRowViewModel? left, DriveFolderRowViewModel? right)
    {
        return Nullable.Compare(right?.SizeBytes, left?.SizeBytes);
    }

    private static int CompareUsageAscending(DriveFolderRowViewModel? left, DriveFolderRowViewModel? right)
    {
        return Nullable.Compare(left?.UsagePercent, right?.UsagePercent);
    }

    private static int CompareUsageDescending(DriveFolderRowViewModel? left, DriveFolderRowViewModel? right)
    {
        return Nullable.Compare(right?.UsagePercent, left?.UsagePercent);
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
