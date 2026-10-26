using System.Net;
using System.Security.Cryptography;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Two machines of one folder exchange their indexes over mutual TLS; a key the server does
/// not list for the folder gets nothing, whatever it says about itself.
///
/// 🔴 Task 1.1: only the index travels (path, size, hash) - and only between devices attached
/// to the folder. The test runs both ends in one process with throwaway keys, the way the
/// task asks two instances to be tried on one machine.
/// </summary>
public sealed class ONLYAMachineTheServerLISTSGetsTheIndexTests
{
    [Fact]
    public async Task AnATTACHEDPeerReceivesTheIndex()
    {
        using var folder = new TemporaryFolder();
        folder.File("plan.dwg", "a");
        string index = SyncIndexStore.Serialize(SyncIndexBuilder.Build(folder.Path, "f-1"));
        using Device server = new();
        using Device client = new();
        var attached = new HashSet<string> { server.KeyId, client.KeyId };

        await using var serving = new SyncLanExchange(server.Identity, _ => attached, id => id == "f-1" ? index : null);
        await using var asking = new SyncLanExchange(client.Identity, _ => attached, _ => null);

        SyncIndex received = await asking.FetchIndexAsync(
            new IPEndPoint(IPAddress.Loopback, serving.Port),
            server.KeyId,
            "f-1",
            Timeout());

        Assert.Equal(["plan.dwg"], received.Entries.Select(entry => entry.RelativePath));
    }

    [Fact]
    public async Task AnUNLISTEDKeyIsRefusedAfterTheHandshake()
    {
        using Device server = new();
        using Device stranger = new();
        var attached = new HashSet<string> { server.KeyId };

        await using var serving = new SyncLanExchange(server.Identity, _ => attached, _ => "{}");
        await using var asking = new SyncLanExchange(stranger.Identity, _ => new HashSet<string> { server.KeyId }, _ => null);

        SyncLanException refusal = await Assert.ThrowsAsync<SyncLanException>(() => asking.FetchIndexAsync(
            new IPEndPoint(IPAddress.Loopback, serving.Port),
            server.KeyId,
            "f-1",
            Timeout()));
        Assert.Equal("sync_lan_peer_not_attached", refusal.Code);
    }

    [Fact]
    public async Task AServerWithTheWRONGKeyIsNotBelieved()
    {
        using Device server = new();
        using Device client = new();
        using Device expected = new();
        var attached = new HashSet<string> { server.KeyId, client.KeyId, expected.KeyId };

        await using var serving = new SyncLanExchange(server.Identity, _ => attached, _ => "{}");
        await using var asking = new SyncLanExchange(client.Identity, _ => attached, _ => null);

        SyncLanException refusal = await Assert.ThrowsAsync<SyncLanException>(() => asking.FetchIndexAsync(
            new IPEndPoint(IPAddress.Loopback, serving.Port),
            expected.KeyId,
            "f-1",
            Timeout()));
        Assert.Equal("sync_lan_peer_not_attached", refusal.Code);
    }

    /// <summary>
    /// Erk-S Sync, 2026-10-26: LAN worked one way - the laptop reached the desktop, the desktop not
    /// the laptop (its firewall). The connection the laptop opened is offered back: the desktop asks
    /// on it for the laptop's index as on its own.
    /// </summary>
    [Fact]
    public async Task AConnectionOfferedBackCarriesTheOtherWay()
    {
        using Device desktop = new();
        using Device laptop = new();
        var attached = new HashSet<string> { desktop.KeyId, laptop.KeyId };
        const string laptopIndex = "{\"folderId\":\"f-1\",\"entries\":[{\"relativePath\":\"laptop.dwg\",\"size\":1,\"lastWriteUtc\":\"2026-10-26T00:00:00Z\",\"kind\":0,\"sha256\":\"aa\",\"chunkSha256\":[\"aa\"]}]}";
        await using var atDesktop = new SyncLanExchange(desktop.Identity, _ => attached, _ => "{\"folderId\":\"f-1\",\"entries\":[]}");
        await using var atLaptop = new SyncLanExchange(laptop.Identity, _ => attached, id => id == "f-1" ? laptopIndex : null);
        var offered = new TaskCompletionSource();
        atDesktop.ReverseOffered += () => offered.TrySetResult();

        await using SyncLanSession session = await atLaptop.OpenAsync(new IPEndPoint(IPAddress.Loopback, atDesktop.Port), desktop.KeyId, "f-1", Timeout());
        await session.FetchIndexAsync(Timeout());
        Assert.True(atDesktop.ReachedUsLately(laptop.KeyId));
        Task serving = atLaptop.OfferBackAsync(session, desktop.KeyId, Timeout());
        await offered.Task.WaitAsync(Timeout());

        SyncLanSession back = Assert.IsType<SyncLanSession>(atDesktop.TakeReverse(laptop.KeyId, "f-1"));
        SyncIndex received = await back.FetchIndexAsync(Timeout());
        atDesktop.Release(laptop.KeyId, "f-1");
        await serving.WaitAsync(Timeout());

        Assert.Equal(["laptop.dwg"], received.Entries.Select(entry => entry.RelativePath));
    }

    [Fact]
    public void TheSessionSignatureCoversTheDomainTheNonceAndTheKey()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] nonce = RandomNumberGenerator.GetBytes(32);

        byte[] signature = SyncDeviceKey.SignSession(key, nonce);

        byte[] message = [
            .. "erks-sync-device-session"u8.ToArray(),
            .. nonce,
            .. SHA256.HashData(key.ExportSubjectPublicKeyInfo()),
        ];
        Assert.Equal(64, signature.Length);
        Assert.True(key.VerifyData(message, signature, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    private static CancellationToken Timeout() => new CancellationTokenSource(TimeSpan.FromSeconds(20)).Token;

    private sealed class Device : IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public Device()
        {
            KeyId = SyncDeviceKeyIds.Of(_key.ExportSubjectPublicKeyInfo());
            Identity = new SyncLanIdentity(KeyId, SyncDeviceKey.CreateTlsCertificate(_key));
        }

        public string KeyId { get; }

        public SyncLanIdentity Identity { get; }

        public void Dispose()
        {
            Identity.Certificate.Dispose();
            _key.Dispose();
        }
    }
}
