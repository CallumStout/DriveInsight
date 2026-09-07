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

// The chart only needs top-level totals. Retain one accumulator per root child,
// rather than building and caching a tree for every descendant directory.
internal static class TopLevelFolderScanner
{
    public static DriveScanner.TopFolderScanResult Scan(string rootPath, int top, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var timer = Stopwatch.StartNew();
        var root = new Bucket(Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath)));
        var folders = new List<Bucket>();
        using var pending = new BlockingCollection<Work>(new ConcurrentStack<Work>());
        using (var reader = new ScanDirectoryReader())
        {
            try
            {
                root.SkippedLinks = reader.ReadEntries(root.Path, (name, bytes, directory) =>
                {
                    if (directory)
                    {
                        var bucket = new Bucket(Path.Join(root.Path, name));
                        folders.Add(bucket);
                        pending.Add(new Work(bucket.Path, bucket), ct);
                    }
                    else
                    {
                        root.Bytes = AddBytes(root.Bytes, bytes);
                        root.Files++;
                    }
                }, ct);
            }
            catch (Exception ex) when (IsReadError(ex)) { root.UnreadableDirectories++; }
        }

        var outstanding = pending.Count;
        if (outstanding > 0)
        {
            ScanWorkers.Run(_ =>
            {
                using var reader = new ScanDirectoryReader();
                foreach (var work in pending.GetConsumingEnumerable(ct))
                {
                    long bytes = 0, files = 0;
                    try
                    {
                        var skipped = reader.ReadEntries(work.Path, (name, length, directory) =>
                        {
                            if (directory)
                            {
                                Interlocked.Increment(ref outstanding);
                                pending.Add(new Work(Path.Join(work.Path, name), work.Bucket), ct);
                            }
                            else
                            {
                                bytes = AddBytes(bytes, length);
                                files++;
                            }
                        }, ct);
                        Interlocked.Add(ref work.Bucket.SkippedLinks, skipped);
                    }
                    catch (Exception ex) when (IsReadError(ex))
                    {
                        Interlocked.Increment(ref work.Bucket.UnreadableDirectories);
                    }
                    finally
                    {
                        AtomicAdd(ref work.Bucket.Bytes, bytes);
                        Interlocked.Add(ref work.Bucket.Files, files);
                        if (Interlocked.Decrement(ref outstanding) == 0) pending.CompleteAdding();
                    }
                }
            }, ct);
        }

        ct.ThrowIfCancellationRequested();
        foreach (var folder in folders)
        {
            root.Bytes = AddBytes(root.Bytes, folder.Bytes);
            root.Files += folder.Files;
            root.UnreadableDirectories += folder.UnreadableDirectories;
            root.SkippedLinks += folder.SkippedLinks;
        }
        return new DriveScanner.TopFolderScanResult(folders.OrderByDescending(folder => folder.Bytes)
            .Take(Math.Max(0, top)).Select(folder => new FolderStat
            {
                Name = Path.GetFileName(folder.Path), FullPath = folder.Path, Bytes = folder.Bytes
            }).ToList(), root.Bytes, root.Files, root.UnreadableDirectories, root.SkippedLinks, timer.Elapsed.TotalSeconds);
    }

    private static bool IsReadError(Exception ex) => ex is IOException or UnauthorizedAccessException or Win32Exception;
    private static long AddBytes(long current, long bytes) => current > long.MaxValue - bytes ? long.MaxValue : current + bytes;
    private static void AtomicAdd(ref long location, long bytes)
    {
        long current;
        do { current = Volatile.Read(ref location); }
        while (Interlocked.CompareExchange(ref location, AddBytes(current, bytes), current) != current);
    }

    private readonly record struct Work(string Path, Bucket Bucket);
    private sealed class Bucket(string path)
    {
        public string Path { get; } = path;
        public long Bytes, Files;
        public int UnreadableDirectories, SkippedLinks;
    }
}
