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
    private static readonly int SizeScanParallelism = Math.Clamp(Environment.ProcessorCount / 2, 2, 6);
    private static readonly int DriveScanParallelism = Math.Clamp(Environment.ProcessorCount / 4, 1, 3);

    public IEnumerable<DriveInfo> GetReadyDrives() => DriveInfo.GetDrives().Where(d => d.IsReady);

    public void ClearCache() => _folderSizeCache.Clear();

    public async Task<List<FolderStat>> GetTopFoldersAsync(string rootPath, int top = 20, CancellationToken ct = default)
    {
        var result = await ScanTopFoldersAsync(rootPath, top, ct);
        return result.TopFolders;
    }

    public async Task<List<FileSystemEntry>> GetImmediateChildrenAsync(string folderPath, CancellationToken ct = default)
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
                        if (SystemPathExclusions.ShouldExcludeDirectory(childDir))
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

                    if (child is not FileInfo childFile || SystemPathExclusions.ShouldExcludeFile(childFile))
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
            catch { }

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
        return await Task.Run(() =>
        {
            var paths = folderPaths
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var result = new ConcurrentDictionary<string, long>(StringComparer.OrdinalIgnoreCase);
            Parallel.ForEach(paths, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = SizeScanParallelism
            }, path =>
            {
                var size = GetFolderSize(path, ct);
                result[path] = size;
            });

            return result.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
        }, ct);
    }

    public async Task<List<FileSystemEntry>> GetTopFilesAcrossDrivesAsync(IEnumerable<DriveInfo> drives, int top = 5, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var maxItems = Math.Max(1, top);
            var candidates = new ConcurrentBag<FileSystemEntry>();
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

            Parallel.ForEach(readyDrives, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = DriveScanParallelism
            }, drive =>
            {
                foreach (var file in GetTopFilesOnDrive(drive, maxItems, ct))
                {
                    candidates.Add(file);
                }
            });

            return candidates
                .OrderByDescending(item => item.Bytes)
                .Take(maxItems)
                .ToList();
        }, ct);
    }

    private static List<FileSystemEntry> GetTopFilesOnDrive(DriveInfo drive, int maxItems, CancellationToken ct)
    {
        var topFiles = new PriorityQueue<FileSystemEntry, long>();
        var rootPath = drive.RootDirectory.FullName;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(rootPath));

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();

            try
            {
                foreach (var item in current.EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if (item is DirectoryInfo childDir)
                    {
                        if (SystemPathExclusions.ShouldExcludeDirectory(childDir))
                        {
                            continue;
                        }

                        pending.Push(childDir);
                        continue;
                    }

                    if (item is not FileInfo file)
                    {
                        continue;
                    }

                    if (SystemPathExclusions.ShouldExcludeFile(file))
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

                    var entry = new FileSystemEntry
                    {
                        Name = file.Name,
                        FullPath = file.FullName,
                        Bytes = size,
                        IsFolder = false
                    };

                    if (topFiles.Count < maxItems)
                    {
                        topFiles.Enqueue(entry, size);
                        continue;
                    }

                    topFiles.TryPeek(out _, out var smallestSize);
                    if (size <= smallestSize)
                    {
                        continue;
                    }

                    topFiles.Dequeue();
                    topFiles.Enqueue(entry, size);
                }
            }
            catch
            {
            }
        }

        return topFiles.UnorderedItems
            .Select(item => item.Element)
            .OrderByDescending(item => item.Bytes)
            .ToList();
    }

    public async Task<List<StorageBreakdownItem>> GetStorageBreakdownAsync(DriveInfo drive, int topFolders = 8, CancellationToken ct = default)
    {
        if (!drive.IsReady)
        {
            return [];
        }

        var rootPath = drive.RootDirectory.FullName;
        var scan = await ScanTopFoldersAsync(rootPath, topFolders, ct);
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

    private async Task<TopFolderScanResult> ScanTopFoldersAsync(string rootPath, int top, CancellationToken ct)
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
                        if (SystemPathExclusions.ShouldExcludeDirectory(dir))
                        {
                            continue;
                        }

                        topDirectories.Add(dir);
                        continue;
                    }

                    if (item is not FileInfo file || SystemPathExclusions.ShouldExcludeFile(file))
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
            catch
            {
            }

            var result = new ConcurrentBag<FolderStat>();
            Parallel.ForEach(topDirectories, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = SizeScanParallelism
            }, dir =>
            {
                var size = GetFolderSize(dir.FullName, ct);
                Interlocked.Add(ref rootBytes, size);
                result.Add(new FolderStat
                {
                    Name = dir.Name,
                    FullPath = dir.FullName,
                    Bytes = size
                });
            });

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
        ct.ThrowIfCancellationRequested();
        if (_folderSizeCache.TryGetValue(folderPath, out var cached))
        {
            return cached;
        }

        var dir = new DirectoryInfo(folderPath);
        if (SystemPathExclusions.ShouldExcludeDirectory(dir))
        {
            _folderSizeCache[folderPath] = 0;
            return 0;
        }

        var size = SafeDirSize(dir, ct);
        _folderSizeCache[folderPath] = size;
        return size;
    }

    private static long SafeDirSize(DirectoryInfo dir, CancellationToken ct)
    {
        long total = 0;
        var pending = new Stack<DirectoryInfo>();
        pending.Push(dir);

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var current = pending.Pop();

            try
            {
                foreach (var item in current.EnumerateFileSystemInfos())
                {
                    ct.ThrowIfCancellationRequested();
                    if (item is DirectoryInfo childDir)
                    {
                        try
                        {
                            if (SystemPathExclusions.ShouldExcludeDirectory(childDir))
                            {
                                continue;
                            }
                        }
                        catch
                        {
                        }

                        pending.Push(childDir);
                        continue;
                    }

                    if (item is not FileInfo file)
                    {
                        continue;
                    }

                    if (SystemPathExclusions.ShouldExcludeFile(file))
                    {
                        continue;
                    }

                    try
                    {
                        total = checked(total + file.Length);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        return total;
    }

    private sealed record TopFolderScanResult(List<FolderStat> TopFolders, long RootBytes);
}
