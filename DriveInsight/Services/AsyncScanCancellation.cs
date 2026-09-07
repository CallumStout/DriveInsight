using System;
using System.Threading;
using System.Threading.Tasks;

namespace DriveInsight.Services;

// CancelAsync marks the token immediately but runs potentially blocking cancellation
// callbacks away from the caller (in particular, a navigation event on the UI thread).
internal sealed class AsyncScanCancellation : IAsyncDisposable
{
    private readonly CancellationTokenSource _source = new();
    private Task _callbacks = Task.CompletedTask;
    public CancellationToken Token => _source.Token;
    public void Cancel() => _callbacks = _source.CancelAsync();
    public async ValueTask DisposeAsync()
    {
        try { await _callbacks.ConfigureAwait(false); }
        finally { _source.Dispose(); }
    }
}
