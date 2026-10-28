using System.Net;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-10-28: the 14 GB upload hit sync_rate_limited at 10:39 and then waited for the fallback
/// pass. A rate limit is waited out - the server's Retry-After, else 5 s doubling to 30 s - and the call goes on.
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
        Assert.Equal([2, 5, 10, 20], waits.Select(pause => pause.TotalSeconds));
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
