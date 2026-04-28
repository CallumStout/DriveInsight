using System;

namespace DriveInsight.Utilities;

public static class StorageFormatter
{
    public static string Format(long bytes, int decimals = 1)
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
