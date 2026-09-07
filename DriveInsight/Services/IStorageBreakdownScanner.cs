using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DriveInsight.Services;

public interface IStorageBreakdownScanner
{
    IEnumerable<DriveInfo> GetReadyDrives();
    Task<StorageBreakdownScanResult> GetStorageBreakdownScanAsync(DriveInfo drive, int topFolders = 8,
        StorageScanMode mode = StorageScanMode.Normal, CancellationToken ct = default);
}

public sealed record StorageBreakdownScanResult(
    IReadOnlyList<StorageBreakdownItem> Items, long FileCount, int UnreadableDirectories,
    int SkippedLinks, double ElapsedSeconds);
