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

// Startup only needs a top-N ranking, not a retained tree of every folder on every drive.
internal static class LargestFileScanner
{
    public static List<FileSystemEntry> Scan(IReadOnlyList<string> roots, int top,
        IProgress<IReadOnlyList<FileSystemEntry>>? progress, CancellationToken ct,
        TimeSpan? reportInterval = null)
    {
        ct.ThrowIfCancellationRequested();
        if (top <= 0 || roots.Count == 0) return [];
        var ranking = new PriorityQueue<FileSystemEntry, long>();
        var rankingGate = new object();
        long minimum = -1;
        var lastReport = Stopwatch.GetTimestamp();
        var dirty = false;
        var interval = reportInterval ?? TimeSpan.FromMilliseconds(150);
        var workersPerDrive = Math.Clamp(Environment.ProcessorCount / Math.Max(1, roots.Count), 2, 8);

        // Independent drive queues prevent a large system drive from starving other
        // drives. Depth-first work keeps nearby directories together and queue memory low.
        Parallel.ForEach(roots, new ParallelOptions
        {
            CancellationToken = ct,
            MaxDegreeOfParallelism = 4
        }, root =>
        {
            using var pending = new BlockingCollection<string>(new ConcurrentStack<string>());
            pending.Add(root, ct);
            var outstanding = 1;
            Parallel.For(0, workersPerDrive, new ParallelOptions
            {
                CancellationToken = ct,
                MaxDegreeOfParallelism = workersPerDrive
            }, _ =>
            {
                using var reader = new ScanDirectoryReader();
                foreach (var path in pending.GetConsumingEnumerable(ct))
                {
                    try
                    {
                        reader.ReadEntries(path, (name, bytes, isDirectory) =>
                        {
                            if (isDirectory)
                            {
                                Interlocked.Increment(ref outstanding);
                                pending.Add(Path.Join(path, name), ct);
                                return;
                            }
                            // The cutoff only rises. Most files need no allocation or lock.
                            if (bytes <= Volatile.Read(ref minimum)) return;
                            lock (rankingGate)
                            {
                                if (ranking.Count == top && bytes <= minimum) return;
                                var entry = new FileSystemEntry
                                {
                                    Name = name.ToString(), FullPath = Path.Join(path, name), Bytes = bytes
                                };
                                if (ranking.Count == top) ranking.Dequeue();
                                ranking.Enqueue(entry, bytes);
                                if (ranking.Count == top) Volatile.Write(ref minimum, ranking.Peek().Bytes);
                                dirty = true;
                            }
                        }, ct);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
                    {
                        // A denied or disappearing directory must not stop other drives.
                    }
                    finally
                    {
                        if (Interlocked.Decrement(ref outstanding) == 0) pending.CompleteAdding();
                    }
                    if (progress is not null && Stopwatch.GetElapsedTime(Volatile.Read(ref lastReport)) >= interval)
                    {
                        lock (rankingGate)
                        {
                            if (dirty && Stopwatch.GetElapsedTime(lastReport) >= interval)
                            {
                                // Report in ranking order; Progress<T> marshals to the UI.
                                progress.Report(Snapshot(ranking));
                                dirty = false;
                                Volatile.Write(ref lastReport, Stopwatch.GetTimestamp());
                            }
                        }
                    }
                }
            });
        });
        ct.ThrowIfCancellationRequested();
        return Snapshot(ranking);
    }

    private static List<FileSystemEntry> Snapshot(PriorityQueue<FileSystemEntry, long> ranking) =>
        ranking.UnorderedItems.Select(item => item.Element).OrderByDescending(file => file.Bytes).ToList();
}
