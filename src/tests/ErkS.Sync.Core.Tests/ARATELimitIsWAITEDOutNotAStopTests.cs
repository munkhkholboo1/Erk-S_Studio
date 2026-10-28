using System.Net;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-28: the 14 GB upload hit sync_rate_limited at 10:39 and then waited for the fallback
/// pass. A rate limit is waited out - the server's Retry-After, else 30 s then 60 s - and the call goes on.
/// </summary>
public sealed class ARATELimitIsWAITEDOutNotAStopTests
{
    [Fact]
    public async Task ALimitedCallWAITSThenGoesOn()
    {
        int calls = 0;
        var waits = new List<TimeSpan>();
        int answer = await SyncEngine.PatientAsync(
            _ => ++calls switch
            {
                1 => throw new SyncServerException((HttpStatusCode)429, "sync_rate_limited", "limited") { RetryAfter = TimeSpan.FromSeconds(2) },
                2 or 3 or 4 => throw new SyncServerException((HttpStatusCode)429, "sync_rate_limited", "limited"),
                _ => Task.FromResult(42),
            },
            CancellationToken.None,
            (pause, _) =>
            {
                waits.Add(pause);
                return Task.CompletedTask;
            });

        Assert.Equal(42, answer);
        Assert.Equal([2, 30, 60, 60], waits.Select(pause => pause.TotalSeconds));
    }

    [Fact]
    public async Task AnotherREFUSALIsNotWaitedFor()
    {
        await Assert.ThrowsAsync<SyncServerException>(() => SyncEngine.PatientAsync<int>(
            _ => throw new SyncServerException(HttpStatusCode.Conflict, "sync_conflict", "no"),
            CancellationToken.None,
            (_, _) => throw new InvalidOperationException("must not wait")));
    }
}

/// <summary>SRV, 2026-10-28: the server allows ~20 pieces a second per device - SYN keeps under it, shared by every caller.</summary>
public sealed class APACERSpreadsCallsTests
{
    [Fact]
    public async Task CALLSAreSpreadAtTheRATE()
    {
        var pacer = new SyncPacer(20);
        var clock = System.Diagnostics.Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, 21).Select(_ => pacer.WaitAsync(CancellationToken.None)));
        Assert.True(clock.Elapsed >= TimeSpan.FromMilliseconds(900), $"21 calls at 20/s took {clock.ElapsedMilliseconds} ms");
    }
}
