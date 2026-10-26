using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ErkS.Updates.Tests;

/// <summary>
/// Owner, 2026-10-26: «update бүтэн программыг шинээр татаж суулгадаг биш зөвхөн өөрчлөгдсөн
/// хэсгүүдийг нь update хийдэг болгуулаарай». Only files whose hash changed are fetched; the
/// manifest is signed; a broken transfer resumes; a failed apply puts everything back.
/// </summary>
public sealed class AnUPDATEFetchesOnlyWHATChangedTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "erks-updates-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string Dir(string name)
    {
        string path = Path.Combine(_root, name);
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Put(string directory, string relative, string text)
    {
        string path = Path.Combine(directory, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    [Fact]
    public void ThePlanHoldsOnlyChangedAndNewFilesAndDropsTheRemovedOnes()
    {
        string v1 = Dir("v1"), v2 = Dir("v2");
        Put(v1, "runtime/big.dll", new string('r', 5000));
        Put(v1, "ErkS.Studio.dll", "app 1");
        Put(v1, "old.txt", "gone next time");
        Put(v2, "runtime/big.dll", new string('r', 5000));
        Put(v2, "ErkS.Studio.dll", "app 2");
        Put(v2, "Assets/new.png", "new");

        UpdatePlan plan = UpdatePlanner.Plan(InstallManifest.Scan(v1, "ErkS.Studio", "1"), InstallManifest.Scan(v2, "ErkS.Studio", "2"), fullInstallerBytes: 100_000);

        Assert.Equal(["Assets/new.png", "ErkS.Studio.dll"], plan.Changed.Select(file => file.Path).Order());
        Assert.Equal(["old.txt"], plan.Removed);
        Assert.False(plan.UseFullInstaller);
        // Over 60% of the full installer is no delta.
        Assert.True(UpdatePlanner.Plan(new UpdateManifest(), InstallManifest.Scan(v2, "ErkS.Studio", "2"), fullInstallerBytes: 6000).UseFullInstaller);
    }

    [Fact]
    public void AManifestIsTrustedOnlyWithASignatureByThePinnedCertificate()
    {
        using RSA key = RSA.Create(2048);
        var request = new CertificateRequest("CN=Erk-S Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using X509Certificate2 certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        var manifest = new UpdateManifest { ProductCode = "ErkS.Studio", Version = "2", Files = [new UpdateFile { Path = "a.dll", Sha256 = new string('a', 64), Size = 1 }] };
        var signed = new SignedManifest { ManifestJson = manifest.ToJson() };
        signed.Signature = ManifestSignature.Sign(signed.ManifestJson, certificate);

        SignedManifest travelled = SignedManifest.FromJson(signed.ToJson());
        Assert.Equal("2", travelled.Verify(certificate.Thumbprint).Version);
        // Another certificate, or one changed byte, is refused.
        Assert.Throws<InvalidDataException>(() => travelled.Verify(ManifestSignature.ErkSThumbprint));
        travelled.ManifestJson = travelled.ManifestJson.Replace("\"2\"", "\"3\"");
        Assert.Throws<InvalidDataException>(() => travelled.Verify(certificate.Thumbprint));
    }

    [Fact]
    public async Task ABrokenTransferResumesAndABadFileIsRefused()
    {
        string v2 = Dir("v2");
        Put(v2, "ErkS.Studio.dll", new string('x', 300_000));
        UpdateManifest target = InstallManifest.Scan(v2, "ErkS.Studio", "2");
        UpdatePlan plan = UpdatePlanner.Plan(new UpdateManifest(), target);
        byte[] bytes = File.ReadAllBytes(Path.Combine(v2, "ErkS.Studio.dll"));
        var server = new FakeFiles(bytes) { FailFirstAfter = 100_000 };
        string staging = Dir("staging");
        var downloader = new DeltaDownloader(new HttpClient(server), new Uri("https://erk-s.test"), "ErkS.Studio");

        await downloader.DownloadAsync(plan, staging);

        Assert.Equal(target.Files[0].Sha256, Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(DeltaDownloader.StagedPath(staging, target.Files[0].Sha256)))));
        Assert.Contains(server.Ranges, range => range == 100_000); // it asked for the rest, not the whole
        // Wrong bytes from the server never reach staging.
        var liar = new FakeFiles(Encoding.UTF8.GetBytes(new string('y', 300_000)));
        string staging2 = Dir("staging2");
        await Assert.ThrowsAsync<InvalidDataException>(() => new DeltaDownloader(new HttpClient(liar), new Uri("https://erk-s.test"), "ErkS.Studio").DownloadAsync(plan, staging2));
        Assert.False(File.Exists(DeltaDownloader.StagedPath(staging2, target.Files[0].Sha256)));
    }

    [Fact]
    public async Task ApplyReplacesOnlyChangedFilesAndAFailurePutsEverythingBack()
    {
        string install = Dir("install"), v2 = Dir("v2");
        Put(install, "runtime/big.dll", "same");
        Put(install, "ErkS.Studio.dll", "app 1");
        Put(install, "old.txt", "old");
        Put(install, "user-notes.txt", "a person's own file");
        UpdateManifest installed = InstallManifest.LoadOrScan(install, "ErkS.Studio", "1", path => path != "user-notes.txt");
        Put(v2, "runtime/big.dll", "same");
        Put(v2, "ErkS.Studio.dll", "app 2");
        UpdateManifest target = InstallManifest.Scan(v2, "ErkS.Studio", "2");
        UpdatePlan plan = UpdatePlanner.Plan(installed, target);
        string staging = Dir("staging");
        await new DeltaDownloader(new HttpClient(new FakeFiles(File.ReadAllBytes(Path.Combine(v2, "ErkS.Studio.dll")))), new Uri("https://erk-s.test"), "ErkS.Studio") { PinnedSignerThumbprint = null }
            .DownloadAsync(plan, staging);
        DateTime untouched = File.GetLastWriteTimeUtc(Path.Combine(install, "runtime", "big.dll"));

        UpdateApplier.Apply(install, staging, plan);

        Assert.Equal("app 2", File.ReadAllText(Path.Combine(install, "ErkS.Studio.dll")));
        Assert.False(File.Exists(Path.Combine(install, "old.txt")));
        Assert.Equal(untouched, File.GetLastWriteTimeUtc(Path.Combine(install, "runtime", "big.dll")));
        Assert.Equal("a person's own file", File.ReadAllText(Path.Combine(install, "user-notes.txt")));
        Assert.Equal("2", InstallManifest.LoadOrScan(install, "ErkS.Studio", "?").Version);

        // A staged file gone bad: nothing moves, the install stays as it was.
        File.WriteAllText(DeltaDownloader.StagedPath(staging, plan.Changed[0].Sha256), "damaged");
        Assert.Throws<InvalidDataException>(() => UpdateApplier.Apply(install, staging, plan));
        Assert.Equal("app 2", File.ReadAllText(Path.Combine(install, "ErkS.Studio.dll")));
    }

    /// <summary>The file route: whole file or Range; can drop the first transfer part-way.</summary>
    private sealed class FakeFiles(byte[] content) : HttpMessageHandler
    {
        public long FailFirstAfter { get; init; }

        public List<long> Ranges { get; } = [];

        private bool _failed;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            long from = request.Headers.Range?.Ranges.First().From ?? 0;
            Ranges.Add(from);
            byte[] rest = content[(int)from..];
            HttpContent body = !_failed && FailFirstAfter > 0
                ? new StreamContent(new BreakingStream(rest, (int)FailFirstAfter))
                : new ByteArrayContent(rest);
            _failed = true;
            var response = new HttpResponseMessage(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK) { Content = body };
            if (from > 0)
                response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, content.Length - 1, content.Length);
            return Task.FromResult(response);
        }
    }

    private sealed class BreakingStream(byte[] data, int breakAt) : MemoryStream(data)
    {
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Position >= breakAt)
                throw new IOException("connection reset");
            return base.Read(buffer, offset, (int)Math.Min(count, breakAt - Position));
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            Task.FromResult(Read(buffer, offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            byte[] temp = new byte[buffer.Length];
            int read = Read(temp, 0, temp.Length);
            temp.AsSpan(0, read).CopyTo(buffer.Span);
            return ValueTask.FromResult(read);
        }
    }
}
