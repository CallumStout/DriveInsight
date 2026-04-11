using System;

namespace DriveInsight.ViewModels;

public partial class DriveCapacityCardViewModel : ViewModelBase
{
    public required string DriveName { get; init; }
    public required long UsedBytes { get; init; }
    public required long TotalBytes { get; init; }
    public required double UsedPercent { get; init; }
    public required string ProgressBrush { get; init; }

    public string UsageText => $"{FormatStorage(UsedBytes, 1)} of {FormatStorage(TotalBytes, 1)}";
    public string PercentageText => $"{UsedPercent:0}%";

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
