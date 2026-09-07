using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DriveInsight.Services;

internal static class ScanWorkers
{
    public static int Count { get; } = Math.Clamp(Environment.ProcessorCount - 2, 2, 6);
    private static readonly SemaphoreSlim Slots = new(Count, Count);

    // Directory reads are synchronous OS calls. Keep them on a bounded set of
    // dedicated, lower-priority threads rather than starving the shared thread pool.
    // This method itself is called only by background scan coordinators.
    public static void Run(Action<int> work, CancellationToken ct)
    {
        using var failure = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var workers = Enumerable.Range(0, Count).Select(index => Task.Factory.StartNew(() =>
        {
            Slots.Wait(ct);
            try
            {
                Thread.CurrentThread.Priority = ThreadPriority.BelowNormal;
                work(index);
            }
            finally { Slots.Release(); }
        }, ct, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        // Drain every worker before callers dispose their native buffers and queues.
        Task.WhenAll(workers).GetAwaiter().GetResult();
    }
}
