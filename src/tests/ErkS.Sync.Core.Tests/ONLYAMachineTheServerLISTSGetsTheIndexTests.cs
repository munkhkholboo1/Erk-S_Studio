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

    /// <summary>
    /// Erk-S Sync, 2026-10-27: the desktop never heard the laptop's multicast, though the laptop reached
    /// the desktop over TCP. The connection itself says where the laptop listens; the desktop finds it from then on.
    /// </summary>
    [Fact]
    public async Task AMachineThatREACHEDUsIsFOUNDWithoutItsMulticast()
    {
        using Device desktop = new();
        using Device laptop = new();
        var attached = new HashSet<string> { desktop.KeyId, laptop.KeyId };
        await using var atDesktop = new SyncLanExchange(desktop.Identity, _ => attached, _ => "{\"folderId\":\"f-1\",\"entries\":[]}");
        await using var atLaptop = new SyncLanExchange(laptop.Identity, _ => attached, _ => null);
        var reached = new TaskCompletionSource<(string Id, IPEndPoint At, string Folder)>();
        atDesktop.PeerReached += (id, at, folder) => reached.TrySetResult((id, at, folder));

        await atLaptop.FetchIndexAsync(new IPEndPoint(IPAddress.Loopback, atDesktop.Port), desktop.KeyId, "f-1", Timeout());
        (string id, IPEndPoint at, string folderId) = await reached.Task.WaitAsync(Timeout());

        Assert.Equal(laptop.KeyId, id);
        Assert.Equal(new IPEndPoint(IPAddress.Loopback, atLaptop.Port), at);
        Assert.Equal("f-1", folderId);
        using var discovery = new SyncLanDiscovery(desktop.KeyId);
        discovery.Learn(id, at, folderId);
        Assert.Equal(at, discovery.Find(laptop.KeyId)?.Endpoint);
        Assert.Contains("f-1", discovery.Find(laptop.KeyId)!.FolderIds);
    }

    /// <summary>Erk-S Sync, 2026-10-27: pieces one by one ran the LAN at a tenth of the Wi-Fi - now several are asked at once on one connection.</summary>
    [Fact]
    public async Task PiecesAskedTOGETHERComeBackEachToItsCALLER()
    {
        using Device server = new();
        using Device client = new();
        var attached = new HashSet<string> { server.KeyId, client.KeyId };
        await using var serving = new SyncLanExchange(server.Identity, _ => attached, _ => "{}",
            (_, path, _, chunk) => path == "gone.dwg" ? null : Enumerable.Repeat((byte)chunk, 100_000 + chunk).ToArray());
        await using var asking = new SyncLanExchange(client.Identity, _ => attached, _ => null);
        await using SyncLanSession session = await asking.OpenAsync(new IPEndPoint(IPAddress.Loopback, serving.Port), server.KeyId, "f-1", Timeout());

        Task<byte[]>[] pieces = Enumerable.Range(0, 8).Select(chunk => session.FetchChunkAsync("plan.dwg", "aa", chunk, Timeout())).ToArray();
        Task<byte[]> missing = session.FetchChunkAsync("gone.dwg", "aa", 0, Timeout());
        Task<byte[]> after = session.FetchChunkAsync("plan.dwg", "aa", 9, Timeout());

        for (int chunk = 0; chunk < 8; chunk++)
        {
            byte[] piece = await pieces[chunk];
            Assert.Equal(100_000 + chunk, piece.Length);
            Assert.All(piece, value => Assert.Equal((byte)chunk, value));
        }
        Assert.Equal("sync_lan_no_chunk", (await Assert.ThrowsAsync<SyncLanException>(() => missing)).Code);
        Assert.Equal(100_009, (await after).Length); // a refused piece leaves the connection in step
    }

    /// <summary>Erk-S Sync, 2026-10-27: one TCP stream used a quarter of the Wi-Fi - pieces go round several connections, the file still arrives whole.</summary>
    [Fact]
    public async Task PiecesOverSEVERALConnectionsMakeOneWHOLEFile()
    {
        using Device server = new();
        using Device client = new();
        using var here = new TemporaryFolder();
        var attached = new HashSet<string> { server.KeyId, client.KeyId };
        byte[] content = new byte[SyncIndex.ChunkSize * 3 + 1234];
        new Random(7).NextBytes(content);
        byte[] PieceOf(int chunk) => content.AsSpan(chunk * SyncIndex.ChunkSize, Math.Min(SyncIndex.ChunkSize, content.Length - chunk * SyncIndex.ChunkSize)).ToArray();
        var pieces = Enumerable.Range(0, 4).Select(chunk => Convert.ToHexStringLower(SHA256.HashData(PieceOf(chunk)))).ToList();
        var theirs = new SyncIndexEntry("model.rvt", content.Length, DateTime.UtcNow, SyncFileKinds.Of("model.rvt"), Convert.ToHexStringLower(SHA256.HashData(content)), pieces);
        await using var serving = new SyncLanExchange(server.Identity, _ => attached, _ => "{}", (_, _, _, chunk) => PieceOf(chunk));
        await using var asking = new SyncLanExchange(client.Identity, _ => attached, _ => null);
        var endpoint = new IPEndPoint(IPAddress.Loopback, serving.Port);
        var lanes = new List<SyncLanSession>();
        for (int lane = 0; lane < 3; lane++)
            lanes.Add(await asking.OpenAsync(endpoint, server.KeyId, "f-1", Timeout()));
        var timings = new SyncApplyTimings();

        SyncApplyResult result = await SyncFileAssembler.ApplyAsync(
            here.Path,
            new SyncTransferAction(SyncTransferKind.Fetch, theirs, null),
            new SyncIndex { FolderId = "f-1", Entries = [] },
            (chunk, token) => lanes[chunk % lanes.Count].FetchChunkAsync("model.rvt", theirs.Sha256, chunk, token),
            DateTimeOffset.UtcNow,
            Timeout(),
            timings: timings);
        foreach (SyncLanSession lane in lanes)
            await lane.DisposeAsync();

        Assert.Equal(SyncApplyOutcome.Applied, result.Outcome);
        Assert.Equal(content, await File.ReadAllBytesAsync(Path.Combine(here.Path, "model.rvt")));
        Assert.True(timings.HashTicks > 0 && timings.DiskTicks > 0);
    }

    [Fact]
    public void OnlyPRIVATEAddressesAreToldOrTRIED()
    {
        Assert.True(SyncLanAddresses.IsPrivate(IPAddress.Parse("192.168.1.20")));
        Assert.True(SyncLanAddresses.IsPrivate(IPAddress.Parse("10.0.0.5")));
        Assert.True(SyncLanAddresses.IsPrivate(IPAddress.Parse("172.20.3.4")));
        Assert.False(SyncLanAddresses.IsPrivate(IPAddress.Parse("172.32.0.1")));
        Assert.False(SyncLanAddresses.IsPrivate(IPAddress.Parse("8.8.8.8")));
        Assert.False(SyncLanAddresses.IsPrivate(IPAddress.Parse("169.254.1.1")));
        Assert.False(SyncLanAddresses.IsPrivate(IPAddress.Loopback));

        IReadOnlyList<IPEndPoint> parsed = SyncLanAddresses.Parse(["192.168.1.20:50128", "8.8.8.8:50128", "nonsense", "10.0.0.5:0"]);

        Assert.Equal([new IPEndPoint(IPAddress.Parse("192.168.1.20"), 50128)], parsed);
        Assert.All(SyncLanAddresses.Own(50128), text => Assert.True(SyncLanAddresses.IsPrivate(IPEndPoint.Parse(text).Address)));
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
