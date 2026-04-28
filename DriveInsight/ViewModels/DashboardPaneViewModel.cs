using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DriveInsight.Commands;
using DriveInsight.Services;
using DriveInsight.Utilities;

namespace DriveInsight.ViewModels;

public partial class DashboardPaneViewModel : ViewModelBase
{
    private readonly DriveScanner _scanner = new();
    private readonly CleanupCandidateScannerService _cleanupCandidateScanner = new();
    private readonly CleanupRemovalService _cleanupRemoval = new();
    private readonly IConfirmationDialogService _confirmationDialog;
    private readonly ICleanupReviewDialogService _cleanupReviewDialog;
    private readonly Func<Task>? _afterRefresh;

    public ObservableCollection<DriveCapacityCardViewModel> DriveBreakdowns { get; } = [];
    public ObservableCollection<LargestFileViewModel> LargestFiles { get; } = [];
    public ObservableCollection<InsightCardViewModel> SmartInsights { get; } = [];
    public IAsyncRelayCommand RefreshDashboardCommand { get; }

    [ObservableProperty]
    private bool isLoadingLargestFiles;

    [ObservableProperty]
    private bool isRefreshing;

    [ObservableProperty]
    private long totalCapacityBytes;

    [ObservableProperty]
    private long totalUsedBytes;

    [ObservableProperty]
    private double totalUtilizationPercent;

    public string TotalCapacityText => StorageFormatter.Format(TotalCapacityBytes);
    public string TotalUsedText => StorageFormatter.Format(TotalUsedBytes);
    public string TotalUtilizationText => $"{TotalUtilizationPercent:0}%";
    private static string WindowsOldPath => Path.Combine(Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\", "Windows.old");

    public DashboardPaneViewModel(
        Func<Task>? afterRefresh = null,
        IConfirmationDialogService? confirmationDialog = null,
        ICleanupReviewDialogService? cleanupReviewDialog = null)
    {
        var nullDialog = new NullDialogService();
        _afterRefresh = afterRefresh;
        _confirmationDialog = confirmationDialog ?? nullDialog;
        _cleanupReviewDialog = cleanupReviewDialog ?? nullDialog;
        RefreshDashboardCommand = RefreshCommand.Create(this);
        _ = RefreshDashboardCommand.ExecuteAsync(null);
    }

    partial void OnIsRefreshingChanged(bool value)
    {
        RefreshDashboardCommand.NotifyCanExecuteChanged();
        foreach (var insight in SmartInsights)
        {
            insight.ActionCommand?.NotifyCanExecuteChanged();
        }
    }

    internal void LoadDriveCapacity()
    {
        var drives = _scanner.GetReadyDrives().ToList();
        var totalCapacity = 0L;
        var totalUsed = 0L;

        DriveBreakdowns.Clear();

        foreach (var drive in drives)
        {
            var total = drive.TotalSize;
            var available = drive.AvailableFreeSpace;
            var used = Math.Max(0, total - available);
            var usedPercent = total > 0 ? used / (double)total * 100d : 0d;

            totalCapacity += total;
            totalUsed += used;

            DriveBreakdowns.Add(new DriveCapacityCardViewModel
            {
                DriveName = drive.Name.TrimEnd('\\'),
                UsedBytes = used,
                TotalBytes = total,
                UsedPercent = Math.Clamp(usedPercent, 0d, 100d),
                ProgressBrush = usedPercent >= 90d ? "#C93A2F" : usedPercent >= 75d ? "#D1791A" : "#1E63FF"
            });
        }

        TotalCapacityBytes = totalCapacity;
        TotalUsedBytes = totalUsed;
        TotalUtilizationPercent = totalCapacity > 0 ? Math.Clamp(totalUsed / (double)totalCapacity * 100d, 0d, 100d) : 0d;

        OnPropertyChanged(nameof(TotalCapacityText));
        OnPropertyChanged(nameof(TotalUsedText));
        OnPropertyChanged(nameof(TotalUtilizationText));
    }

    internal async Task LoadBiggestFilesAndInsightsAsync()
    {
        try
        {
            IsLoadingLargestFiles = true;
            var drives = _scanner.GetReadyDrives().ToList();
            var topFiles = await _scanner.GetTopFilesAcrossDrivesAsync(drives, 5);

            LargestFiles.Clear();
            foreach (var file in topFiles)
            {
                LargestFiles.Add(new LargestFileViewModel
                {
                    Name = file.Name,
                    FilePath = file.FullPath,
                    SizeBytes = file.Bytes,
                    Category = ResolveCategory(file.Name)
                });
            }
        }
        catch
        {
            LargestFiles.Clear();
        }
        finally
        {
            IsLoadingLargestFiles = false;
            await BuildSmartInsightsAsync();
        }
    }

    internal Task RefreshLinkedPanesAsync() => _afterRefresh?.Invoke() ?? Task.CompletedTask;

    private async Task BuildSmartInsightsAsync()
    {
        SmartInsights.Clear();

        var mostConstrainedDrive = DriveBreakdowns
            .OrderByDescending(d => d.UsedPercent)
            .FirstOrDefault();

        if (mostConstrainedDrive is not null && mostConstrainedDrive.UsedPercent >= 50d)
        {
            SmartInsights.Add(new InsightCardViewModel
            {
                Kind = InsightKind.Critical,
                Title = $"Drive {mostConstrainedDrive.DriveName} Critical",
                Message = $"Storage is over {mostConstrainedDrive.UsedPercent:0}% full. System performance may be impacted soon.",
                ActionText = "Review cleanup",
                ActionCommand = new AsyncRelayCommand(
                    () => ReviewConstrainedDriveCleanupAsync(mostConstrainedDrive.DriveName),
                    () => !IsRefreshing)
            });
        }

        var windowsOldPath = WindowsOldPath;
        if (Directory.Exists(windowsOldPath))
        {
            try
            {
                var windowsOldSize = await _scanner.GetFolderSizeAsync(windowsOldPath);
                if (windowsOldSize > 0)
                {
                    SmartInsights.Add(new InsightCardViewModel
                    {
                        Kind = InsightKind.Warning,
                        Title = "Redundant OS Files",
                        Message = $"Windows.old is taking {StorageFormatter.Format(windowsOldSize)}. This can be safely removed.",
                        ActionText = "Remove",
                        ActionCommand = new AsyncRelayCommand(RemoveWindowsOldAsync, () => !IsRefreshing)
                    });
                }
            }
            catch
            {
            }
        }

        var largestFile = LargestFiles.OrderByDescending(file => file.SizeBytes).FirstOrDefault();
        if (largestFile is not null)
        {
            SmartInsights.Add(new InsightCardViewModel
            {
                Kind = InsightKind.Tip,
                Title = "Optimization Tip",
                Message = $"Archive or move {largestFile.Name} to recover up to {largestFile.SizeText} quickly.",
                ActionText = "Open location",
                ActionCommand = new AsyncRelayCommand(
                    () => OpenFileLocationAsync(largestFile.FilePath),
                    () => !IsRefreshing)
            });
        }

        if (SmartInsights.Count == 0)
        {
            SmartInsights.Add(new InsightCardViewModel
            {
                Kind = InsightKind.Tip,
                Title = "No Critical Alerts",
                Message = "Your drives look healthy. Run a scan to surface optimization opportunities.",
                ActionText = "N/A"
            });
        }
    }

    private async Task RemoveWindowsOldAsync()
    {
        if (IsRefreshing)
        {
            return;
        }

        var shouldRefresh = false;
        var confirmed = await _confirmationDialog.ConfirmAsync(
            "Remove Windows.old?",
            $"DriveInsight will permanently remove the files in {WindowsOldPath}. This may require administrator permission.",
            "Remove",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        try
        {
            IsRefreshing = true;
            shouldRefresh = true;
            await _cleanupRemoval.DeletePathAsync(WindowsOldPath);
        }
        catch
        {
        }
        finally
        {
            if (shouldRefresh)
            {
                await RefreshAfterCleanupAsync();
            }

            IsRefreshing = false;
        }
    }

    private async Task ReviewConstrainedDriveCleanupAsync(string driveName)
    {
        if (IsRefreshing)
        {
            return;
        }

        var shouldRefresh = false;

        try
        {
            IsRefreshing = true;
            var candidates = await _cleanupCandidateScanner.ScanAsync(driveName);
            IsRefreshing = false;

            var selectedCandidates = await _cleanupReviewDialog.ReviewAsync(driveName, candidates);
            if (selectedCandidates.Count == 0)
            {
                return;
            }

            IsRefreshing = true;
            shouldRefresh = true;
            await _cleanupRemoval.DeleteAsync(selectedCandidates);
        }
        catch
        {
        }
        finally
        {
            if (shouldRefresh)
            {
                await RefreshAfterCleanupAsync();
            }

            IsRefreshing = false;
        }
    }

    private async Task RefreshAfterCleanupAsync()
    {
        _scanner.ClearCache();
        LoadDriveCapacity();
        await LoadBiggestFilesAndInsightsAsync();
        await RefreshLinkedPanesAsync();
    }

    private static Task OpenFileLocationAsync(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = $"/select,\"{filePath}\"",
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

    private static string ResolveCategory(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".mov" or ".mp4" or ".mkv" or ".avi" => "VIDEO",
            ".zip" or ".rar" or ".7z" or ".iso" => "ARCHIVE",
            ".sql" or ".bak" or ".db" => "DATA",
            ".exe" or ".msi" or ".pkg" => "INSTALLER",
            ".psd" or ".blend" or ".obj" => "MEDIA",
            ".log" or ".txt" or ".json" or ".xml" => "TEXT",
            _ => "FILE"
        };
    }

}
