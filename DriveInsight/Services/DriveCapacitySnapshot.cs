using System;
using System.IO;

namespace DriveInsight.Services;

internal sealed record DriveCapacitySnapshot(DriveInfo Drive, long Total, long Used, long Available)
{
    // Call only on a worker: readiness and capacity can block on a busy/offline device.
    public static DriveCapacitySnapshot Read(DriveInfo drive)
    {
        try
        {
            if (drive.IsReady)
                return new(drive, drive.TotalSize, Math.Max(0, drive.TotalSize - drive.TotalFreeSpace), drive.AvailableFreeSpace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        return new(drive, 0, 0, 0);
    }
}
