using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using DriveInsight.Services;

namespace DriveInsight.ViewModels;

public partial class DashboardPaneViewModel : ViewModelBase
{
    private readonly DriveScanner _scanner = new();

    public ObservableCollection<DriveCapacityCardViewModel> DriveBreakdowns { get; } = [];
    public ObservableCollection<LargestFileViewModel> LargestFiles { get; } = [];
    public ObservableCollection<InsightCardViewModel> SmartInsights { get; } = [];

    [ObservableProperty]
    private bool isLoadingLargestFiles;

    [ObservableProperty]
    private long totalCapacityBytes;

    [ObservableProperty]
    private long totalUsedBytes;

    [ObservableProperty]
    private double totalUtilizationPercent;

    public string TotalCapacityText => FormatStorage(TotalCapacityBytes, 1);
    public string TotalUsedText => FormatStorage(TotalUsedBytes, 1);
    public string TotalUtilizationText => $"{TotalUtilizationPercent:0}%";

    public DashboardPaneViewModel()
    {
        LoadDriveCapacity();
        _ = LoadBiggestFilesAndInsightsAsync();
    }

    private void LoadDriveCapacity()
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

    private async Task LoadBiggestFilesAndInsightsAsync()
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

    private async Task BuildSmartInsightsAsync()
    {
        SmartInsights.Clear();

        var mostConstrainedDrive = DriveBreakdowns
            .OrderByDescending(d => d.UsedPercent)
            .FirstOrDefault();

        if (mostConstrainedDrive is not null && mostConstrainedDrive.UsedPercent >= 85d)
        {
            SmartInsights.Add(new InsightCardViewModel
            {
                Kind = InsightKind.Critical,
                Title = $"Drive {mostConstrainedDrive.DriveName} Critical",
                Message = $"Storage is over {mostConstrainedDrive.UsedPercent:0}% full. System performance may be impacted soon.",
            });
        }

        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var windowsOldPath = Path.Combine(systemRoot, "Windows.old");
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
                        Message = $"Windows.old is taking {FormatStorage(windowsOldSize, 1)}. This can be safely removed.",
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
            });
        }

        if (SmartInsights.Count == 0)
        {
            SmartInsights.Add(new InsightCardViewModel
            {
                Kind = InsightKind.Tip,
                Title = "No Critical Alerts",
                Message = "Your drives look healthy. Run a scan to surface optimization opportunities.",
            });
        }
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

    private static string FormatStorage(long bytes, int decimals)
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

        return $"{value.ToString($"F{Math.Max(0, decimals)}")} {units[unitIndex]}";
    }
}
