namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Erk-S Sync, 2026-11-24: Twin counted 865 «POST /sync/device/challenge → 400 sync_device_unknown», two a minute,
/// from a machine whose key the server did not know - every pass asked again. The tray now waits (15 min,
/// doubling to an hour) and says so; Studio registering the key («device-registered») ends the wait at once.
/// </summary>
public sealed class AKEYTheServerDOESNOTKnowIsNOTAskedEveryPassTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public async Task AnUNKNOWNKeyWaitsAndARegistrationAsksAgainAtOnce()
    {
        using var home = new TemporaryFolder();
        using var root = new TemporaryFolder();
        using var device = new FakeDevice();
        var server = new FakeServer(Canonical, [device]) { DevicesUnknown = true };
        int told = 0;
        await using var engine = new SyncEngine(new SyncHome(home.Path), server.Client(), device, new SyncPathMap(@"D:\Cloud Work=>" + root.Path));
        engine.DeviceUnknown += () => told++;

        await engine.PassAsync(CancellationToken.None);
        await engine.PassAsync(CancellationToken.None);
        await engine.PassAsync(CancellationToken.None);

        Assert.Equal(1, server.Challenges); // one refusal, not one per pass
        Assert.Equal(1, told);
        Assert.True(engine.DeviceUnknownNow);
        Assert.Equal("device_unknown", engine.LastState.ServerStatus);
        Assert.Equal(SyncEngine.DeviceUnknownMessage, engine.LastState.ServerMessage);

        // Studio registered the key: the next pass asks at once, and the machine is known again.
        server.DevicesUnknown = false;
        engine.DeviceKeyRegistered();
        await engine.PassAsync(CancellationToken.None);

        Assert.Equal(2, server.Challenges);
        Assert.False(engine.DeviceUnknownNow);
        Assert.Equal("ok", engine.LastState.ServerStatus);
    }

    [Fact]
    public async Task TheWAITDoublesAndIsTriedAgainWhenItIsOut()
    {
        using var home = new TemporaryFolder();
        using var root = new TemporaryFolder();
        using var device = new FakeDevice();
        var server = new FakeServer(Canonical, [device]) { DevicesUnknown = true };
        await using var engine = new SyncEngine(new SyncHome(home.Path), server.Client(), device, new SyncPathMap(@"D:\Cloud Work=>" + root.Path))
        {
            DeviceUnknownFirstWait = TimeSpan.FromMilliseconds(200),
            DeviceUnknownLongestWait = TimeSpan.FromSeconds(10),
        };

        await engine.PassAsync(CancellationToken.None); // refused: wait 200 ms
        await engine.PassAsync(CancellationToken.None); // within it: not asked
        Assert.Equal(1, server.Challenges);

        await Task.Delay(400);
        await engine.PassAsync(CancellationToken.None); // out: asked, refused again - wait 400 ms
        Assert.Equal(2, server.Challenges);
        await Task.Delay(250);
        await engine.PassAsync(CancellationToken.None); // 250 < 400: still waiting (it doubled)
        Assert.Equal(2, server.Challenges);
    }
}
