using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DriveInsight.Models;

namespace DriveInsight.Services;

public sealed class DriveScanner : IStorageBreakdownScanner
{
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private ScanCache _cache = new();

    public IEnumerable<DriveInfo> GetReadyDrives() => DriveInfo.GetDrives().Where(d => d.IsReady);

    // Replacing the cache prevents an older in-flight scan from repopulating a refreshed cache.
    public void ClearCache() => Interlocked.Exchange(ref _cache, new ScanCache());

    public Task<List<FolderStat>> GetTopFoldersAsync(string rootPath, int top = 20, CancellationToken ct = default) =>
        GetTopFoldersAsync(rootPath, top, StorageScanMode.Normal, ct);

    public async Task<List<FolderStat>> GetTopFoldersAsync(
        string rootPath, int top, StorageScanMode mode, CancellationToken ct = default) =>
        (await GetTopFolderScanAsync(rootPath, top, mode, ct)).TopFolders;

    public async Task<TopFolderScanResult> GetTopFolderScanAsync(
        string rootPath, int top = 20, StorageScanMode mode = StorageScanMode.Normal, CancellationToken ct = default)
    {
        var timer = Stopwatch.StartNew();
        await _scanGate.WaitAsync(ct);
        try
        {
            var cache = Volatile.Read(ref _cache).For(mode);
            var path = Normalize(rootPath);
            await Task.Run(() => EnsureScanned([path], cache, ct), ct);
            var root = cache[path];
            var folders = root.Children.Select(child => new FolderStat
            {
                Name = Path.GetFileName(child),
                FullPath = child,
                Bytes = cache[child].Bytes
            }).OrderByDescending(folder => folder.Bytes).Take(Math.Max(0, top)).ToList();
            return new TopFolderScanResult(folders, root.Bytes, root.Files, root.UnreadableDirectories,
                root.SkippedLinks, timer.Elapsed.TotalSeconds);
        }
        finally { _scanGate.Release(); }
    }

    public Task<List<FileSystemEntry>> GetImmediateChildrenAsync(
        string folderPath, StorageScanMode mode = StorageScanMode.Normal, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var path = Normalize(folderPath);
            var result = new List<FileSystemEntry>();
            ScanDirectoryReader.Read(path, (name, bytes, isDirectory) => result.Add(new FileSystemEntry
            {
                Name = name.ToString(),
                FullPath = Path.Join(path, name),
                IsFolder = isDirectory,
                Bytes = isDirectory ? 0 : bytes
            }), ct);
            return result.OrderByDescending(x => x.IsFolder).ThenBy(x => x.Name).ToList();
        }, ct);

    public async Task<long> GetFolderSizeAsync(string folderPath, CancellationToken ct = default) =>
        (await GetFolderSizesAsync([folderPath], ct))[folderPath];

    public Task<Dictionary<string, long>> GetFolderSizesAsync(IEnumerable<string> folderPaths, CancellationToken ct = default) =>
        GetFolderSizesAsync(folderPaths, StorageScanMode.Normal, ct);

    public async Task<Dictionary<string, long>> GetFolderSizesAsync(
        IEnumerable<string> folderPaths, StorageScanMode mode, CancellationToken ct = default)
    {
        var paths = folderPaths.Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        await _scanGate.WaitAsync(ct);
        try
        {
            var cache = Volatile.Read(ref _cache).For(mode);
            return await Task.Run(() =>
            {
                var normalized = paths.Select(Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                EnsureScanned(normalized, cache, ct);
                return paths.ToDictionary(path => path, path => cache[Normalize(path)].Bytes, StringComparer.OrdinalIgnoreCase);
            }, ct);
        }
        finally { _scanGate.Release(); }
    }

    public async Task<List<FileSystemEntry>> GetTopFilesAcrossDrivesAsync(
        IEnumerable<DriveInfo> drives, int top = 5, CancellationToken ct = default,
        IProgress<IReadOnlyList<FileSystemEntry>>? progress = null)
    {
        var paths = drives.Where(drive =>
        {
            try { return drive.IsReady; }
            catch (IOException) { return false; }
        }).Select(drive => Normalize(drive.RootDirectory.FullName)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return await GetTopFilesAsync(paths, top, ct, progress);
    }

    internal Task<List<FileSystemEntry>> GetTopFilesAsync(
        IReadOnlyList<string> paths, int top, CancellationToken ct = default,
        IProgress<IReadOnlyList<FileSystemEntry>>? progress = null) =>
        Task.Run(() => LargestFileScanner.Scan(paths.Select(Normalize)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), top, progress, ct), ct);

    public async Task<List<StorageBreakdownItem>> GetStorageBreakdownAsync(
        DriveInfo drive, int topFolders = 8, StorageScanMode mode = StorageScanMode.Normal, CancellationToken ct = default)
        => (await GetStorageBreakdownScanAsync(drive, topFolders, mode, ct)).Items.ToList();

    public Task<StorageBreakdownScanResult> GetStorageBreakdownScanAsync(
        DriveInfo drive, int topFolders = 8, StorageScanMode mode = StorageScanMode.Normal, CancellationToken ct = default)
        => Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();
            if (!drive.IsReady) throw new IOException("Drive is not ready.");
            var rootPath = drive.RootDirectory.FullName;
            var scan = TopLevelFolderScanner.Scan(rootPath, topFolders, ct);
            var usedBytes = Math.Max(0, drive.TotalSize - drive.TotalFreeSpace);
            return BuildStorageBreakdown(rootPath, scan, usedBytes);
        }, ct);

    internal static StorageBreakdownScanResult BuildStorageBreakdown(
        string rootPath, TopFolderScanResult scan, long usedBytes)
    {
        var topBytes = scan.TopFolders.Aggregate(0L, (bytes, folder) => AddBytes(bytes, folder.Bytes));
        var result = scan.TopFolders.Where(folder => folder.Bytes > 0).Select(folder => new StorageBreakdownItem
        {
            Name = folder.Name, FullPath = folder.FullPath, Bytes = folder.Bytes
        }).ToList();
        if (scan.RootBytes > topBytes)
            result.Add(new StorageBreakdownItem { Name = "Other scanned files", FullPath = rootPath, Bytes = scan.RootBytes - topBytes });
        if (usedBytes > scan.RootBytes)
            result.Add(new StorageBreakdownItem { Name = "System / Protected", FullPath = rootPath, Bytes = usedBytes - scan.RootBytes });
        return new StorageBreakdownScanResult(result.OrderByDescending(item => item.Bytes).ToArray(),
            scan.FileCount, scan.UnreadableDirectories, scan.SkippedLinks, scan.ElapsedSeconds);
    }

    private static void EnsureScanned(string[] paths, ConcurrentDictionary<string, FolderSnapshot> cache, CancellationToken ct)
    {
        var missing = paths.Where(path => !cache.ContainsKey(path)).ToArray();
        // A parent scan supplies all requested descendants, including overlapping inputs.
        var missingSet = missing.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var roots = missing.Where(path =>
        {
            for (var parent = Path.GetDirectoryName(path); parent is not null; parent = Path.GetDirectoryName(parent))
                if (missingSet.Contains(parent)) return false;
            return true;
        }).ToArray();
        if (roots.Length > 0) ScanDirectories(roots, cache, ct);
        ct.ThrowIfCancellationRequested();
        // An excluded link or inaccessible ancestor may not have enumerated a requested path.
        foreach (var path in paths)
            if (!cache.ContainsKey(path)) ScanDirectories([path], cache, ct);
    }

    private static void ScanDirectories(
        IReadOnlyList<string> paths, ConcurrentDictionary<string, FolderSnapshot> cache, CancellationToken ct)
    {
        if (paths.Count == 0) return;
        using var pending = new BlockingCollection<DirectoryWork>();
        var completed = new ConcurrentBag<DirectoryWork>();
        var outstanding = paths.Count;
        foreach (var path in paths) pending.Add(new DirectoryWork(path, null), ct);

        ScanWorkers.Run(_ =>
        {
            using var reader = new ScanDirectoryReader();
            foreach (var work in pending.GetConsumingEnumerable(ct))
            {
                try
                {
                    var skipped = reader.ReadEntries(work.Path, (name, bytes, isDirectory) =>
                    {
                        if (isDirectory)
                        {
                            var childPath = Path.Join(work.Path, name);
                            work.Children.Add(childPath);
                            Interlocked.Increment(ref work.Remaining);
                            Interlocked.Increment(ref outstanding);
                            pending.Add(new DirectoryWork(childPath, work), ct);
                        }
                        else
                        {
                            // This worker owns direct totals; merge with child totals only at completion.
                            work.DirectBytes = AddBytes(work.DirectBytes, bytes);
                            work.DirectFiles++;
                        }
                    }, ct);
                    Interlocked.Add(ref work.SkippedLinks, skipped);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
                {
                    // Preserve partial results and expose incomplete coverage to the UI.
                    Interlocked.Increment(ref work.UnreadableDirectories);
                }
                finally
                {
                    AtomicAddBytes(ref work.Bytes, work.DirectBytes);
                    Interlocked.Add(ref work.Files, work.DirectFiles);
                    CompleteDirectory(work, completed);
                    if (Interlocked.Decrement(ref outstanding) == 0) pending.CompleteAdding();
                }
            }
        }, ct);

        // Cancellation never publishes partial folder totals as a reusable scan.
        ct.ThrowIfCancellationRequested();
        foreach (var work in completed)
            cache[work.Path] = new FolderSnapshot(work.Bytes, work.Files, work.UnreadableDirectories,
                work.SkippedLinks, work.Children.ToArray());
    }

    private static void CompleteDirectory(DirectoryWork work, ConcurrentBag<DirectoryWork> completed)
    {
        // Iterative postorder aggregation avoids rescanning and handles very deep trees.
        while (Interlocked.Decrement(ref work.Remaining) == 0)
        {
            completed.Add(work);
            if (work.Parent is not { } parent) return;
            AtomicAddBytes(ref parent.Bytes, work.Bytes);
            Interlocked.Add(ref parent.Files, work.Files);
            Interlocked.Add(ref parent.UnreadableDirectories, work.UnreadableDirectories);
            Interlocked.Add(ref parent.SkippedLinks, work.SkippedLinks);
            work = parent;
        }
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    private static long AddBytes(long current, long bytes) => current > long.MaxValue - bytes ? long.MaxValue : current + bytes;
    private static void AtomicAddBytes(ref long location, long bytes)
    {
        long current;
        do { current = Volatile.Read(ref location); }
        while (Interlocked.CompareExchange(ref location, AddBytes(current, bytes), current) != current);
    }

    public sealed record TopFolderScanResult(List<FolderStat> TopFolders, long RootBytes,
        long FileCount = 0, int UnreadableDirectories = 0, int SkippedLinks = 0, double ElapsedSeconds = 0);

    private sealed record FolderSnapshot(long Bytes, long Files, int UnreadableDirectories, int SkippedLinks, string[] Children);
    private sealed class ScanCache
    {
        private readonly ConcurrentDictionary<string, FolderSnapshot> _normal = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, FolderSnapshot> _deep = new(StringComparer.OrdinalIgnoreCase);
        public ConcurrentDictionary<string, FolderSnapshot> For(StorageScanMode mode) => mode == StorageScanMode.Deep ? _deep : _normal;
    }
    private sealed class DirectoryWork(string path, DirectoryWork? parent)
    {
        public string Path { get; } = path;
        public DirectoryWork? Parent { get; } = parent;
        public List<string> Children { get; } = [];
        public int Remaining = 1;
        public long DirectBytes, DirectFiles, Bytes, Files;
        public int UnreadableDirectories, SkippedLinks;
    }
}
