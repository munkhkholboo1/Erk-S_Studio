namespace ErkS.Studio;

internal sealed record StudioSessionTransition(
    long Epoch,
    CancellationToken CancellationToken);

/// <summary>
/// Serializes account-context transitions and makes the epoch check plus
/// session commit one atomic operation.
/// </summary>
internal sealed class StudioSessionEpochGate : IDisposable
{
    private readonly object gate = new();
    private CancellationTokenSource cancellation = new();
    private long epoch;

    public long Epoch => Interlocked.Read(ref epoch);

    public StudioSessionTransition Begin()
    {
        lock (gate)
        {
            cancellation.Cancel();
            cancellation.Dispose();
            cancellation = new CancellationTokenSource();
            long next = Interlocked.Increment(ref epoch);
            return new StudioSessionTransition(
                next,
                cancellation.Token);
        }
    }

    public StudioSessionTransition Capture()
    {
        lock (gate)
        {
            return new StudioSessionTransition(
                Epoch,
                cancellation.Token);
        }
    }

    public bool IsCurrent(long expectedEpoch) =>
        Epoch == expectedEpoch;

    public void Require(long expectedEpoch)
    {
        if (!IsCurrent(expectedEpoch))
        {
            throw new TaskCanceledException(
                "Studio account context changed while the operation was running.");
        }
    }

    public void Commit(long expectedEpoch, Action commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        lock (gate)
        {
            Require(expectedEpoch);
            commit();
            Require(expectedEpoch);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            cancellation.Cancel();
            cancellation.Dispose();
        }
    }
}
