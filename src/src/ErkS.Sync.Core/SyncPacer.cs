namespace ErkS.Sync.Core;

/// <summary>
/// At most <c>perSecond</c> calls a second, spread evenly, whoever calls (every folder and partner share one):
/// SYN stays under the server's per-device limits instead of being refused and waiting minutes.
/// </summary>
public sealed class SyncPacer(double perSecond)
{
    private readonly object _gate = new();
    private readonly TimeSpan _gap = perSecond > 0 ? TimeSpan.FromSeconds(1 / perSecond) : TimeSpan.Zero;
    private DateTimeOffset _next = DateTimeOffset.MinValue;

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        if (_gap == TimeSpan.Zero)
            return;
        TimeSpan wait;
        lock (_gate)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            DateTimeOffset at = _next > now ? _next : now;
            _next = at + _gap;
            wait = at - now;
        }
        if (wait > TimeSpan.Zero)
            await Task.Delay(wait, cancellationToken);
    }
}
