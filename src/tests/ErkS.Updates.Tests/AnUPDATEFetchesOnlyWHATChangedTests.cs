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
        SignedManifest signed = SignedManifest.Create(manifest, certificate);

        SignedManifest travelled = SignedManifest.FromJson(signed.ToJson());
        Assert.Equal("2", travelled.Verify(certificate.Thumbprint).Version);
        // Another pinned certificate, or one changed byte of the body, is refused.
        Assert.Throws<InvalidDataException>(() => travelled.Verify(ManifestSignature.ErkSThumbprint));
        string json = Encoding.UTF8.GetString(Convert.FromBase64String(travelled.Body)).Replace("\"2\"", "\"3\"");
        travelled.Body = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
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

    /// <summary>
    /// Contract 5a, owner: «зарим жижиг шинэчлэлтүүд тухайн программыг заавал хаах албагүй байж
    /// болно». Only «live» files changed: they go in now, nothing closes; a held one waits for the
    /// next start; any «restart» file still means closing.
    /// </summary>
    [Fact]
    public async Task LiveFilesGoInWhileTheProgramRunsAndAHeldOneWaits()
    {
        string install = Dir("install"), v2 = Dir("v2");
        Put(install, "ErkS.Studio.dll", "app 1");
        Put(install, "Templates/sheet.json", "template 1");
        Put(install, "Icons/logo.svg", "icon 1");
        UpdateManifest installed = InstallManifest.LoadOrScan(install, "ErkS.Studio", "1");
        Put(v2, "ErkS.Studio.dll", "app 1");
        Put(v2, "Templates/sheet.json", "template 2");
        Put(v2, "Icons/logo.svg", "icon 2");
        UpdateManifest target = InstallManifest.Scan(v2, "ErkS.Studio", "2");
        foreach (UpdateFile file in target.Files.Where(file => !file.Path.EndsWith(".dll", StringComparison.Ordinal)))
            file.Apply = UpdateFile.ApplyLive;
        UpdatePlan plan = UpdatePlanner.Plan(installed, target);
        Assert.False(plan.NeedsRestart);
        string staging = Dir("staging");
        byte[][] bodies = plan.Changed.Select(file => File.ReadAllBytes(Path.Combine(v2, file.Path.Replace('/', Path.DirectorySeparatorChar)))).ToArray();
        await new DeltaDownloader(new HttpClient(new FakeBySha(bodies)), new Uri("https://erk-s.test"), "ErkS.Studio") { PinnedSignerThumbprint = null }
            .DownloadAsync(plan, staging);

        LiveApplyResult first;
        using (new FileStream(Path.Combine(install, "Icons", "logo.svg"), FileMode.Open, FileAccess.Read, FileShare.None))
            first = UpdateApplier.ApplyLive(install, staging, plan);

        Assert.Equal(["Templates/sheet.json"], first.Applied);
        Assert.Equal(["Icons/logo.svg"], first.Deferred);
        Assert.Equal("template 2", File.ReadAllText(Path.Combine(install, "Templates", "sheet.json")));
        Assert.Equal("1", InstallManifest.LoadOrScan(install, "ErkS.Studio", "?").Version); // not done yet
        // Next start: the held file goes in, and the version is complete.
        UpdatePlan again = UpdatePlanner.Plan(InstallManifest.LoadOrScan(install, "ErkS.Studio", "?"), target);
        Assert.Equal(["Icons/logo.svg"], again.Changed.Select(file => file.Path));
        LiveApplyResult second = UpdateApplier.ApplyLive(install, staging, again);
        Assert.Equal(["Icons/logo.svg"], second.Applied);
        Assert.Equal("icon 2", File.ReadAllText(Path.Combine(install, "Icons", "logo.svg")));
        Assert.Equal("2", InstallManifest.LoadOrScan(install, "ErkS.Studio", "?").Version);

        // A «restart» file in the plan means closing.
        target.Files.First(file => file.Path == "Templates/sheet.json").Apply = UpdateFile.ApplyRestart;
        target.Files.First(file => file.Path == "ErkS.Studio.dll").Sha256 = new string('b', 64);
        Assert.True(UpdatePlanner.Plan(installed, target).NeedsRestart);
    }

    /// <summary>Serves whichever body has the requested hash.</summary>
    private sealed class FakeBySha(byte[][] bodies) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string sha = request.RequestUri!.Segments.Last();
            byte[] body = bodies.First(item => Convert.ToHexStringLower(SHA256.HashData(item)) == sha);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
        }
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
