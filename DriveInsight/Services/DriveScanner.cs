using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DriveInsight.Models;

namespace DriveInsight.Services;

public sealed class DriveScanner
{
    private readonly ConcurrentDictionary<string, long> _folderSizeCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly int SizeScanParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);
    private static readonly int FileScanParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 8);

    public IEnumerable<DriveInfo> GetReadyDrives() => DriveInfo.GetDrives().Where(d => d.IsReady);

    public void ClearCache() => _folderSizeCache.Clear();

    public async Task<List<FolderStat>> GetTopFoldersAsync(string rootPath, int top = 20, CancellationToken ct = default)
    {
        return await GetTopFoldersAsync(rootPath, top, StorageScanMode.Normal, ct);
    }

    public async Task<List<FolderStat>> GetTopFoldersAsync(
        string rootPath,
        int top,
        StorageScanMode mode,
        CancellationToken ct = default)
    {
        var result = await ScanTopFoldersAsync(rootPath, top, mode, ct);
        return result.TopFolders;
    }

    public async Task<List<FileSystemEntry>> GetImmediateChildrenAsync(
        string folderPath,
        StorageScanMode mode = StorageScanMode.Normal,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var result = new List<FileSystemEntry>();
            var dir = new DirectoryInfo(folderPath);

            try
            {
                foreach (var child in dir.EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if (child is DirectoryInfo childDir)
                    {
                        if (SystemPathExclusions.ShouldExcludeDirectory(childDir, mode))
                        {
                            continue;
                        }

                        result.Add(new FileSystemEntry
                        {
                            Name = childDir.Name,
                            FullPath = childDir.FullName,
                            IsFolder = true
                        });

                        continue;
                    }

                    if (child is not FileInfo childFile || SystemPathExclusions.ShouldExcludeFile(childFile, mode))
                    {
                        continue;
                    }

                    long size = 0;
                    try { size = childFile.Length; } catch { }

                    result.Add(new FileSystemEntry
                    {
                        Name = childFile.Name,
                        FullPath = childFile.FullName,
                        Bytes = size,
                        IsFolder = false
                    });
                }
            }
            catch when (!ct.IsCancellationRequested) { }

            return result
                .OrderByDescending(x => x.IsFolder)
                .ThenBy(x => x.Name)
                .ToList();
        }, ct);
    }

    public async Task<long> GetFolderSizeAsync(string folderPath, CancellationToken ct = default)
    {
        return await Task.Run(() => GetFolderSize(folderPath, ct), ct);
    }

    public async Task<Dictionary<string, long>> GetFolderSizesAsync(IEnumerable<string> folderPaths, CancellationToken ct = default)
    {
        return await GetFolderSizesAsync(folderPaths, StorageScanMode.Normal, ct);
    }

    public async Task<Dictionary<string, long>> GetFolderSizesAsync(
        IEnumerable<string> folderPaths,
        StorageScanMode mode,
        CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var paths = folderPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return GetFolderSizes(paths, mode, ct);
        }, ct);
    }

    public async Task<List<FileSystemEntry>> GetTopFilesAcrossDrivesAsync(IEnumerable<DriveInfo> drives, int top = 5, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var maxItems = Math.Max(1, top);
            var readyDrives = drives.Where(drive =>
            {
                try
                {
                    return drive.IsReady;
                }
                catch
                {
                    return false;
                }
            }).ToList();

            return GetTopFiles(readyDrives, maxItems, ct)
                .OrderByDescending(item => item.Bytes)
                .Take(maxItems)
                .ToList();
        }, ct);
    }

    private static List<FileSystemEntry> GetTopFiles(IReadOnlyList<DriveInfo> drives, int maxItems, CancellationToken ct)
    {
        using var pending = new BlockingCollection<DirectoryInfo>();
        var workerResults = new ConcurrentBag<List<FileSystemEntry>>();
        var outstandingDirectories = 0;

        foreach (var drive in drives)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref outstandingDirectories);
            pending.Add(drive.RootDirectory, ct);
        }

        if (outstandingDirectories == 0)
        {
            pending.CompleteAdding();
            return [];
        }

        Parallel.For(0, FileScanParallelism, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = FileScanParallelism
        }, _ =>
        {
            var localTopFiles = new PriorityQueue<FileSystemEntry, long>();

            foreach (var current in pending.GetConsumingEnumerable(ct))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    AddTopFilesFromDirectory(
                        current,
                        pending,
                        localTopFiles,
                        maxItems,
                        ref outstandingDirectories,
                        ct);
                }
                finally
                {
                    if (Interlocked.Decrement(ref outstandingDirectories) == 0)
                    {
                        pending.CompleteAdding();
                    }
                }
            }

            workerResults.Add(TopFilesToList(localTopFiles));
        });

        return workerResults
            .SelectMany(files => files)
            .OrderByDescending(item => item.Bytes)
            .Take(maxItems)
            .ToList();
    }

    private static void AddTopFilesFromDirectory(
        DirectoryInfo directory,
        BlockingCollection<DirectoryInfo> pending,
        PriorityQueue<FileSystemEntry, long> topFiles,
        int maxItems,
        ref int outstandingDirectories,
        CancellationToken ct)
    {
        try
        {
            foreach (var item in directory.EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();
                if (item is DirectoryInfo childDir)
                {
                    if (SystemPathExclusions.ShouldExcludeDirectory(childDir))
                    {
                        continue;
                    }

                    Interlocked.Increment(ref outstandingDirectories);
                    pending.Add(childDir, ct);
                    continue;
                }

                if (item is not FileInfo file || SystemPathExclusions.ShouldExcludeFile(file))
                {
                    continue;
                }

                long size;
                try
                {
                    size = file.Length;
                }
                catch
                {
                    continue;
                }

                AddTopFile(topFiles, new FileSystemEntry
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    Bytes = size,
                    IsFolder = false
                }, maxItems);
            }
        }
        catch when (!ct.IsCancellationRequested)
        {
        }
    }

    private static void AddTopFile(
        PriorityQueue<FileSystemEntry, long> topFiles,
        FileSystemEntry entry,
        int maxItems)
    {
        if (topFiles.Count < maxItems)
        {
            topFiles.Enqueue(entry, entry.Bytes);
            return;
        }

        topFiles.TryPeek(out _, out var smallestSize);
        if (entry.Bytes <= smallestSize)
        {
            return;
        }

        topFiles.Dequeue();
        topFiles.Enqueue(entry, entry.Bytes);
    }

    private static List<FileSystemEntry> TopFilesToList(PriorityQueue<FileSystemEntry, long> topFiles)
    {
        return topFiles.UnorderedItems
            .Select(item => item.Element)
            .OrderByDescending(item => item.Bytes)
            .ToList();
    }

    public async Task<List<StorageBreakdownItem>> GetStorageBreakdownAsync(
        DriveInfo drive,
        int topFolders = 8,
        StorageScanMode mode = StorageScanMode.Normal,
        CancellationToken ct = default)
    {
        if (!drive.IsReady)
        {
            return [];
        }

        var rootPath = drive.RootDirectory.FullName;
        var scan = await ScanTopFoldersAsync(rootPath, topFolders, mode, ct);
        var top = scan.TopFolders;
        var topBytes = top.Sum(folder => folder.Bytes);
        var rootBytes = scan.RootBytes;
        var usedBytes = Math.Max(0, drive.TotalSize - drive.AvailableFreeSpace);

        var result = top
            .Where(folder => folder.Bytes > 0)
            .Select(folder => new StorageBreakdownItem
            {
                Name = folder.Name,
                FullPath = folder.FullPath,
                Bytes = folder.Bytes
            })
            .ToList();

        var otherScannedBytes = Math.Max(0, rootBytes - topBytes);
        if (otherScannedBytes > 0)
        {
            result.Add(new StorageBreakdownItem
            {
                Name = "Other scanned files",
                FullPath = rootPath,
                Bytes = otherScannedBytes
            });
        }

        var protectedBytes = Math.Max(0, usedBytes - rootBytes);
        if (protectedBytes > 0)
        {
            result.Add(new StorageBreakdownItem
            {
                Name = "System / Protected",
                FullPath = rootPath,
                Bytes = protectedBytes
            });
        }

        return result
            .OrderByDescending(item => item.Bytes)
            .ToList();
    }

    private async Task<TopFolderScanResult> ScanTopFoldersAsync(
        string rootPath,
        int top,
        StorageScanMode mode,
        CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            var root = new DirectoryInfo(rootPath);
            var topDirectories = new List<DirectoryInfo>();
            long rootBytes = 0;

            try
            {
                foreach (var item in root.EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if (item is DirectoryInfo dir)
                    {
                        if (SystemPathExclusions.ShouldExcludeDirectory(dir, mode))
                        {
                            continue;
                        }

                        topDirectories.Add(dir);
                        continue;
                    }

                    if (item is not FileInfo file || SystemPathExclusions.ShouldExcludeFile(file, mode))
                    {
                        continue;
                    }

                    try
                    {
                        rootBytes = checked(rootBytes + file.Length);
                    }
                    catch
                    {
                    }
                }
            }
            catch when (!ct.IsCancellationRequested)
            {
            }

            var topDirectorySizes = GetFolderSizes(
                topDirectories.Select(directory => directory.FullName).ToList(),
                mode,
                ct);
            var result = new List<FolderStat>(topDirectories.Count);
            foreach (var dir in topDirectories)
            {
                var size = topDirectorySizes.TryGetValue(dir.FullName, out var bytes) ? bytes : 0L;
                rootBytes = AddBytes(rootBytes, size);
                result.Add(new FolderStat
                {
                    Name = dir.Name,
                    FullPath = dir.FullName,
                    Bytes = size
                });
            }

            _folderSizeCache[rootPath] = rootBytes;
            return new TopFolderScanResult(
                result
                    .OrderByDescending(x => x.Bytes)
                    .Take(Math.Max(0, top))
                    .ToList(),
                rootBytes);
        }, ct);
    }

    private long GetFolderSize(string folderPath, CancellationToken ct)
    {
        var sizes = GetFolderSizes([folderPath], StorageScanMode.Normal, ct);
        return sizes.TryGetValue(folderPath, out var size) ? size : 0;
    }

    private Dictionary<string, long> GetFolderSizes(
        IReadOnlyList<string> folderPaths,
        StorageScanMode mode,
        CancellationToken ct)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var scanRoots = new List<FolderSizeAccumulator>();

        foreach (var folderPath in folderPaths)
        {
            ct.ThrowIfCancellationRequested();
            if (_folderSizeCache.TryGetValue(folderPath, out var cached))
            {
                result[folderPath] = cached;
                continue;
            }

            var dir = new DirectoryInfo(folderPath);
            if (SystemPathExclusions.ShouldExcludeDirectory(dir, mode))
            {
                _folderSizeCache[folderPath] = 0;
                result[folderPath] = 0;
                continue;
            }

            scanRoots.Add(new FolderSizeAccumulator(dir.FullName));
        }

        if (scanRoots.Count > 0)
        {
            ScanDirectories(scanRoots.Select(root => new DirectoryScanWork(
                new DirectoryInfo(root.FullPath),
                root)), mode, ct);

            foreach (var root in scanRoots)
            {
                _folderSizeCache[root.FullPath] = root.Bytes;
                result[root.FullPath] = root.Bytes;
            }
        }

        return result;
    }

    private static void ScanDirectories(
        IEnumerable<DirectoryScanWork> roots,
        StorageScanMode mode,
        CancellationToken ct)
    {
        using var pending = new BlockingCollection<DirectoryScanWork>();
        var outstandingDirectories = 0;

        foreach (var root in roots)
        {
            ct.ThrowIfCancellationRequested();
            Interlocked.Increment(ref outstandingDirectories);
            pending.Add(root, ct);
        }

        if (outstandingDirectories == 0)
        {
            pending.CompleteAdding();
            return;
        }

        Parallel.For(0, SizeScanParallelism, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = SizeScanParallelism
        }, _ =>
        {
            foreach (var work in pending.GetConsumingEnumerable(ct))
            {
                ct.ThrowIfCancellationRequested();

                try
                {
                    ScanDirectory(work, pending, ref outstandingDirectories, mode, ct);
                }
                finally
                {
                    if (Interlocked.Decrement(ref outstandingDirectories) == 0)
                    {
                        pending.CompleteAdding();
                    }
                }
            }
        });
    }

    private static void ScanDirectory(
        DirectoryScanWork work,
        BlockingCollection<DirectoryScanWork> pending,
        ref int outstandingDirectories,
        StorageScanMode mode,
        CancellationToken ct)
    {
        long directoryBytes = 0;

        try
        {
            foreach (var item in work.Directory.EnumerateFileSystemInfos())
            {
                ct.ThrowIfCancellationRequested();
                if (item is DirectoryInfo childDir)
                {
                    try
                    {
                        if (SystemPathExclusions.ShouldExcludeDirectory(childDir, mode))
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    Interlocked.Increment(ref outstandingDirectories);
                    pending.Add(new DirectoryScanWork(childDir, work.Accumulator), ct);
                    continue;
                }

                if (item is not FileInfo file || SystemPathExclusions.ShouldExcludeFile(file, mode))
                {
                    continue;
                }

                try
                {
                    directoryBytes = AddBytes(directoryBytes, file.Length);
                }
                catch
                {
                }
            }
        }
        catch when (!ct.IsCancellationRequested)
        {
        }

        if (directoryBytes > 0)
        {
            Interlocked.Add(ref work.Accumulator.Bytes, directoryBytes);
        }
    }

    private static long AddBytes(long current, long bytes)
    {
        try
        {
            return checked(current + bytes);
        }
        catch
        {
            return long.MaxValue;
        }
    }

    private sealed record TopFolderScanResult(List<FolderStat> TopFolders, long RootBytes);

    private sealed record DirectoryScanWork(DirectoryInfo Directory, FolderSizeAccumulator Accumulator);

    private sealed class FolderSizeAccumulator(string fullPath)
    {
        public string FullPath { get; } = fullPath;

        public long Bytes;
    }
}
