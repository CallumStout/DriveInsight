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

    public IEnumerable<DriveInfo> GetReadyDrives() => DriveInfo.GetDrives().Where(d => d.IsReady);

    public async Task<List<FolderStat>> GetTopFoldersAsync(string rootPath, int top = 20, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var root = new DirectoryInfo(rootPath);
            var topDirectories = new List<DirectoryInfo>();

            try
            {
                foreach (var dir in root.EnumerateDirectories())
                {
                    ct.ThrowIfCancellationRequested();
                    if (IsReparsePoint(dir))
                    {
                        continue;
                    }

                    topDirectories.Add(dir);
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
                result.Add(new FolderStat
                {
                    Name = dir.Name,
                    FullPath = dir.FullName,
                    Bytes = size
                });
            });

            return result
                .OrderByDescending(x => x.Bytes)
                .Take(top)
                .ToList();
        }, ct);
    }

    public async Task<List<FileSystemEntry>> GetImmediateChildrenAsync(string folderPath, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var result = new List<FileSystemEntry>();
            var dir = new DirectoryInfo(folderPath);

            try
            {
                foreach (var childDir in dir.EnumerateDirectories())
                {
                    ct.ThrowIfCancellationRequested();
                    if (IsReparsePoint(childDir))
                    {
                        continue;
                    }

                    result.Add(new FileSystemEntry
                    {
                        Name = childDir.Name,
                        FullPath = childDir.FullName,
                        IsFolder = true
                    });
                }
            }
            catch { }

            try
            {
                foreach (var childFile in dir.EnumerateFiles())
                {
                    ct.ThrowIfCancellationRequested();
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
            var topFiles = new PriorityQueue<FileSystemEntry, long>();

            foreach (var drive in drives)
            {
                ct.ThrowIfCancellationRequested();
                if (!drive.IsReady)
                {
                    continue;
                }

                var rootPath = drive.RootDirectory.FullName;
                var pending = new Stack<DirectoryInfo>();
                pending.Push(new DirectoryInfo(rootPath));

                while (pending.Count > 0)
                {
                    ct.ThrowIfCancellationRequested();
                    var current = pending.Pop();

                    try
                    {
                        foreach (var file in current.EnumerateFiles())
                        {
                            ct.ThrowIfCancellationRequested();

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

                    try
                    {
                        foreach (var childDir in current.EnumerateDirectories())
                        {
                            ct.ThrowIfCancellationRequested();
                            if (IsReparsePoint(childDir))
                            {
                                continue;
                            }

                            pending.Push(childDir);
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return topFiles.UnorderedItems
                .Select(item => item.Element)
                .OrderByDescending(item => item.Bytes)
                .ToList();
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
        if (IsReparsePoint(dir))
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
                foreach (var file in current.EnumerateFiles())
                {
                    ct.ThrowIfCancellationRequested();
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

            try
            {
                foreach (var childDir in current.EnumerateDirectories())
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        if ((childDir.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                    }

                    pending.Push(childDir);
                }
            }
            catch
            {
            }
        }

        return total;
    }

    private static bool IsReparsePoint(DirectoryInfo dir)
    {
        try
        {
            return (dir.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return false;
        }
    }
}
