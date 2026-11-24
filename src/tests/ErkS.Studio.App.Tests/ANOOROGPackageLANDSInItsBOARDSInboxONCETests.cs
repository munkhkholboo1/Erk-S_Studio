using System.IO;
using System.Security.Cryptography;
using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Ноорог агент, 2026-11-19 (the owner approved «Ноорог → Studio холболт»): a project's Noorog
/// packages come from the server into the board's source inbox - PDFs first, the manifest last,
/// every file checked - and a package already brought is never fetched again.
/// </summary>
public sealed class ANOOROGPackageLANDSInItsBOARDSInboxONCETests : IDisposable
{
    private const string SourceId = "0123456789abcdef0123456789abcdef";

    private readonly string folder = Path.Combine(Path.GetTempPath(), "erks-noorog-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    private string Inbox => Path.Combine(folder, "sources", "Ноорог — Агуулах", "deliveries");

    [Fact]
    public async Task APACKAGEIsBroughtWholeManifestLastAndOnlyOnce()
    {
        var client = new FakeClient();
        client.Add("pkg-aaaaaaaa11", ("001.pdf", "%PDF-1 one"), ("002.pdf", "%PDF-1 two"), ("board.erks-sheets.json", "{}"));

        NoorogFetchReport first = await NoorogDeliveryIntake.FetchAsync(client, "p1", _ => NoorogSourceSlot.Inbox(Inbox));

        Assert.Equal(1, first.Fetched);
        Assert.Empty(first.Failures);
        string target = Path.Combine(Inbox, "20261119-101500-pkgaaaaa");
        Assert.Equal("%PDF-1 one", File.ReadAllText(Path.Combine(target, "001.pdf")));
        Assert.True(File.Exists(Path.Combine(target, "board.erks-sheets.json")));
        Assert.Empty(Directory.EnumerateFiles(target, "*.download"));
        Assert.Equal("board.erks-sheets.json", client.Asked[^1]); // the watched file comes last
        Assert.Contains("pkg-aaaaaaaa11", File.ReadAllText(Path.Combine(Path.GetDirectoryName(Inbox)!, NoorogDeliveryIntake.LedgerFileName)));

        // Even with its folder gone (the ledger remembers), it is not fetched again.
        Directory.Delete(target, recursive: true);
        client.Asked.Clear();
        NoorogFetchReport second = await NoorogDeliveryIntake.FetchAsync(client, "p1", _ => NoorogSourceSlot.Inbox(Inbox));
        Assert.Equal(0, second.Fetched);
        Assert.Equal(1, second.AlreadyHere);
        Assert.Empty(client.Asked);
    }

    [Fact]
    public async Task AFILEWhoseHashDiffersLeavesNoManifestAndNoPartsBehind()
    {
        var client = new FakeClient();
        client.Add("pkg-bbbbbbbb", ("001.pdf", "%PDF-1 one"), ("board.erks-sheets.json", "{}"));
        client.Corrupt = "001.pdf";

        NoorogFetchReport report = await NoorogDeliveryIntake.FetchAsync(client, "p1", _ => NoorogSourceSlot.Inbox(Inbox));

        Assert.Equal(0, report.Fetched);
        Assert.Single(report.Failures);
        Assert.Empty(Directory.EnumerateFiles(Inbox, "*", SearchOption.AllDirectories));

        client.Corrupt = null; // the next attempt brings it
        Assert.Equal(1, (await NoorogDeliveryIntake.FetchAsync(client, "p1", _ => NoorogSourceSlot.Inbox(Inbox))).Fetched);
    }

    [Fact]
    public async Task ABOARDBoundToAnotherDeviceIsLeftToIt()
    {
        var client = new FakeClient();
        client.Add("pkg-cccccccc", ("001.pdf", "x"), ("board.erks-sheets.json", "{}"));

        NoorogFetchReport report = await NoorogDeliveryIntake.FetchAsync(client, "p1", _ => NoorogSourceSlot.Elsewhere("өөр төхөөрөмж"));

        Assert.Equal(1, report.Elsewhere);
        Assert.Empty(client.Asked);
    }

    [Theory]
    [InlineData("..\\evil.pdf", SourceId, "board.erks-sheets.json")]
    [InlineData("sub/001.pdf", SourceId, "board.erks-sheets.json")]
    [InlineData("001.exe", SourceId, "board.erks-sheets.json")]
    [InlineData("001.pdf", "not-a-source-id", "board.erks-sheets.json")]
    [InlineData("001.pdf", SourceId, "missing.erks-sheets.json")]
    public void ALISTEDPackageThatCouldWriteElsewhereIsRefused(string pdfName, string sourceId, string manifest)
    {
        var delivery = new NoorogDelivery(
            "pkg-1", "board-1", "Агуулах", sourceId, DateTimeOffset.UtcNow, manifest,
            [new NoorogDeliveryFile(pdfName, new string('a', 64), 1), new NoorogDeliveryFile("board.erks-sheets.json", new string('b', 64), 1)]);

        Assert.NotNull(NoorogDeliveryIntake.Problem(delivery));
    }

    [Fact]
    public void ABOARDBecomesASourceNamedForItWithThePackagesSourceId()
    {
        var delivery = new NoorogDelivery("pkg-1", "brd_1", " Агуулах ", SourceId.ToUpperInvariant(), DateTimeOffset.UtcNow, "b.erks-sheets.json", []);

        ProjectDesignSource source = NoorogDeliveryIntake.NewSource(delivery);

        Assert.Equal(SourceId, source.Id);
        Assert.Equal("Ноорог — Агуулах", source.Name);
        Assert.Equal(DesignSourceKind.Folder, source.Kind);
        Assert.Equal("brd_1", source.Metadata[NoorogDeliveryIntake.BoardIdMetadataKey]);
    }

    [Fact]
    public void SYNCSNoorogLineNamesTheProject()
    {
        Assert.Equal("p7", StudioReleaseListener.ProjectIn("{\"event\":\"noorog-delivery\",\"projectId\":\"p7\"}", "noorog-delivery"));
        Assert.Null(StudioReleaseListener.ProjectIn("{\"event\":\"presence\",\"projectId\":\"p7\"}", "noorog-delivery"));
    }

    /// <summary>Erk-S Sync, 2026-11-24: the tray asks Studio to register this machine's key with a «device-unknown» line.</summary>
    [Fact]
    public void SYNCSDeviceUnknownLineIsHeard()
    {
        Assert.True(StudioReleaseListener.IsEvent("{\"event\":\"device-unknown\"}", "device-unknown"));
        Assert.False(StudioReleaseListener.IsEvent("{\"event\":\"presence\",\"projectId\":\"p7\"}", "device-unknown"));
        Assert.False(StudioReleaseListener.IsEvent("not json", "device-unknown"));
    }

    private sealed class FakeClient : INoorogDeliveryClient
    {
        private readonly List<NoorogDelivery> deliveries = [];
        private readonly Dictionary<string, byte[]> bytes = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Asked { get; } = [];

        public string? Corrupt { get; set; }

        public void Add(string packageId, params (string Name, string Text)[] files)
        {
            var listed = new List<NoorogDeliveryFile>();
            foreach ((string name, string text) in files)
            {
                byte[] data = Encoding.UTF8.GetBytes(text);
                bytes[packageId + "/" + name] = data;
                listed.Add(new NoorogDeliveryFile(name, Convert.ToHexStringLower(SHA256.HashData(data)), data.Length));
            }
            deliveries.Add(new NoorogDelivery(
                packageId, "brd_1", "Агуулах", SourceId,
                new DateTimeOffset(2026, 11, 19, 10, 15, 0, TimeSpan.Zero),
                files.Last().Name, listed));
        }

        public Task<IReadOnlyList<NoorogDelivery>> ListNoorogDeliveriesAsync(string projectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<NoorogDelivery>>(deliveries);

        public async Task CopyNoorogDeliveryFileAsync(string projectId, string packageId, string name, Stream destination, long maxBytes, CancellationToken cancellationToken = default)
        {
            Asked.Add(name);
            byte[] data = bytes[packageId + "/" + name];
            if (name == Corrupt)
                data = [.. data.Reverse()];
            await destination.WriteAsync(data, cancellationToken);
        }
    }
}
