using System;

namespace DriveInsight.ViewModels;

public partial class LargestFileViewModel : ViewModelBase
{
    public required string Name { get; init; }
    public required string FilePath { get; init; }
    public required long SizeBytes { get; init; }
    public required string Category { get; init; }

    public string SizeText => FormatStorage(SizeBytes, 1);

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
