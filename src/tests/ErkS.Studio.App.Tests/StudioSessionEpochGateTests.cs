namespace ErkS.Studio.App.Tests;

public sealed class StudioSessionEpochGateTests
{
    [Fact]
    public void StaleSameAccountTransitionCannotCommitAfterSignOutRelogin()
    {
        using var gate = new StudioSessionEpochGate();
        StudioSessionTransition accountA = gate.Begin();
        bool staleCommitted = false;

        _ = gate.Begin(); // sign out
        StudioSessionTransition sameAccountAgain = gate.Begin();

        Assert.Throws<TaskCanceledException>(() =>
            gate.Commit(accountA.Epoch, () => staleCommitted = true));
        Assert.False(staleCommitted);

        bool currentCommitted = false;
        gate.Commit(
            sameAccountAgain.Epoch,
            () => currentCommitted = true);
        Assert.True(currentCommitted);
    }

    [Fact]
    public void NewTransitionCancelsInFlightRefreshToken()
    {
        using var gate = new StudioSessionEpochGate();
        StudioSessionTransition refresh = gate.Begin();

        _ = gate.Begin();

        Assert.True(refresh.CancellationToken.IsCancellationRequested);
    }
}
