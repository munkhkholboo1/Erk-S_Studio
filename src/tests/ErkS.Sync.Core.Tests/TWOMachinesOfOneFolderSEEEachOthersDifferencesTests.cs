using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ErkS.Sync.Core.Tests;

/// <summary>
/// Two instances of SYN, one machine, one canonical path mapped to two physical folders: each
/// finds the other on the LAN, fetches its index and reports what differs - without moving a
/// byte of content.
///
/// 🔴 Task 1.1, acceptance: «хоёр төхөөрөмж нэг folderId, ижил каноник зам харуулна; ялгаатай
/// файлын жагсаалт гарна; агуулга дамжаагүй».
/// </summary>
public sealed class TWOMachinesOfOneFolderSEEEachOthersDifferencesTests
{
    private const string Canonical = @"D:\Cloud Work\Erin";

    [Fact]
    public void ACanonicalPathMapsIntoTheSecondInstancesFolder()
    {
        var map = new SyncPathMap(@"D:\Cloud Work=>C:\scratch\b");

        Assert.Equal(@"C:\scratch\b\Erin", map.Physical(Canonical));
        Assert.Equal(@"E:\Other", map.Physical(@"E:\Other"));
        Assert.Equal(Canonical, new SyncPathMap("").Physical(Canonical));
    }

    [Fact]
    public async Task EachSeesTheOthersDifferencesAndNothingIsCopied()
    {
        using var rootA = new TemporaryFolder();
        using var rootB = new TemporaryFolder();
        using var homeA = new TemporaryFolder();
        using var homeB = new TemporaryFolder();
        using var a = new FakeDevice();
        using var b = new FakeDevice();
        var server = new FakeServer([a, b]);

        string folderA = Path.Combine(rootA.Path, "Erin");
        string folderB = Path.Combine(rootB.Path, "Erin");
        rootA.File(@"Erin\same.dwg", "1");
        rootB.File(@"Erin\same.dwg", "1");
        rootA.File(@"Erin\changed.pdf", "mine");
        rootB.File(@"Erin\changed.pdf", "theirs");
        rootB.File(@"Erin\only-b.pdf", "b");
        SyncFolderMarker.Write(folderA, Identity());
        SyncFolderMarker.Write(folderB, Identity());

        await using var engineA = new SyncEngine(new SyncHome(homeA.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + rootA.Path));
        await using var engineB = new SyncEngine(new SyncHome(homeB.Path), server.Client(), b, new SyncPathMap(@"D:\Cloud Work=>" + rootB.Path));
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        Task runA = engineA.RunAsync(stop.Token);
        Task runB = engineB.RunAsync(stop.Token);

        SyncPeerState? seenByA = null;
        for (int attempt = 0; attempt < 40 && seenByA?.ExchangedAtUtc is null; attempt++)
        {
            await Task.Delay(500);
            engineA.Poke();
            engineB.Poke();
            seenByA = engineA.LastState.Folders.SingleOrDefault()?.Peers.SingleOrDefault();
        }
        stop.Cancel();
        await Task.WhenAll(runA, runB);

        SyncFolderState folder = Assert.Single(engineA.LastState.Folders);
        Assert.True(folder.PresentHere);
        Assert.Equal(Canonical, folder.CanonicalPath);
        Assert.NotNull(seenByA?.ExchangedAtUtc);
        Assert.Equal(1, seenByA!.SameCount);
        Assert.Equal(1, seenByA.DifferentCount);
        Assert.Equal(1, seenByA.OnlyThereCount);
        Assert.Equal("mine", File.ReadAllText(Path.Combine(folderA, "changed.pdf")));
        Assert.False(File.Exists(Path.Combine(folderA, "only-b.pdf")));
        Assert.NotNull(SyncStateStore.TryLoad(new SyncHome(homeA.Path)));

        // The Twin's file: the same counts, never a file name.
        string statusPath = new SyncHome(homeA.Path).StatusPath;
        SyncStatusReport? status = SyncStatusReport.TryRead(statusPath);
        Assert.NotNull(status);
        Assert.True(status!.Running);
        SyncStatusFolder reported = Assert.Single(status.Folders);
        Assert.Equal(1, reported.PeerCount);
        Assert.Equal(1, reported.DifferingFileCount);
        Assert.True(reported.PresentHere);
        string raw = File.ReadAllText(statusPath);
        Assert.DoesNotContain("changed.pdf", raw);
        Assert.DoesNotContain("only-b", raw);
        Assert.Contains("\"differingFileCount\": 1", raw);

        await engineA.DisposeAsync();
        Assert.False(SyncStatusReport.TryRead(statusPath)!.Running);
    }

    [Fact]
    public void OnlyTheRealInstanceWritesTheTwinsStatusFile()
    {
        string? saved = Environment.GetEnvironmentVariable("ERKS_SYNC_HOME");
        try
        {
            Environment.SetEnvironmentVariable("ERKS_SYNC_HOME", null);
            Assert.EndsWith(@"\Erk-S Platform\Sync\status.json", SyncHome.Default().StatusPath);
            Environment.SetEnvironmentVariable("ERKS_SYNC_HOME", @"C:\scratch\second");
            Assert.Equal(@"C:\scratch\second\status.json", SyncHome.Default().StatusPath);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ERKS_SYNC_HOME", saved);
        }
    }

    [Fact]
    public async Task AFolderNOTOnThisDiskIsReportedNotIndexed()
    {
        using var root = new TemporaryFolder();
        using var home = new TemporaryFolder();
        using var a = new FakeDevice();
        var server = new FakeServer([a]);
        await using var engine = new SyncEngine(new SyncHome(home.Path), server.Client(), a, new SyncPathMap(@"D:\Cloud Work=>" + root.Path));

        await engine.PassAsync(CancellationToken.None);

        SyncFolderState folder = Assert.Single(engine.LastState.Folders);
        Assert.False(folder.PresentHere);
        Assert.Equal("ok", engine.LastState.ServerStatus);
    }

    private static SyncFolderIdentity Identity() => new()
    {
        FolderId = "f-1",
        ProjectCode = "ERIN-2026",
        ProjectId = "p-1",
        CanonicalPath = Canonical,
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };

    private sealed class FakeDevice : ISyncDevice, IDisposable
    {
        private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        public FakeDevice()
        {
            DeviceKeyId = SyncDeviceKeyIds.Of(_key.ExportSubjectPublicKeyInfo());
            TlsCertificate = SyncDeviceKey.CreateTlsCertificate(_key);
        }

        public string DeviceKeyId { get; }

        public X509Certificate2 TlsCertificate { get; }

        public byte[] SignSession(byte[] nonce) => SyncDeviceKey.SignSession(_key, nonce);

        public void Dispose()
        {
            TlsCertificate.Dispose();
            _key.Dispose();
        }
    }

    /// <summary>The SRV device routes, answering the way the contract says.</summary>
    private sealed class FakeServer(IReadOnlyList<FakeDevice> devices) : HttpMessageHandler
    {
        public SyncServerClient Client() => new(new HttpClient(this, disposeHandler: false) { BaseAddress = new Uri("https://erk-s.test") });

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            HttpResponseMessage response = path switch
            {
                "/api/cloud-era/v1/sync/device/challenge" => Json(new { nonce = "AAEC", expiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(2) }),
                "/api/cloud-era/v1/sync/device/session" => Json(new { accessToken = "t", accessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(12), deviceKeyId = "x" }),
                "/api/cloud-era/v1/sync/device/heartbeat" => new HttpResponseMessage(HttpStatusCode.NoContent),
                "/api/cloud-era/v1/sync/device/folders" => Json(new
                {
                    folders = new[]
                    {
                        new
                        {
                            folder = new SyncServerFolder("f-1", "p-1", "ERIN-2026", "Эрин", Canonical, "owner@example.mn", "", DateTimeOffset.UtcNow),
                            devices = devices.Select(device => new SyncServerDevice(device.DeviceKeyId, "", "member@example.mn", "", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).ToArray(),
                        },
                    },
                }),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound),
            };
            return Task.FromResult(response);
        }

        private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK) { Content = JsonContent.Create(body) };
    }
}
