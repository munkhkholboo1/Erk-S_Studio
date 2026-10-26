using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace ErkS.Updates.Tests;

/// <summary>
/// Master, 2026-10-26 (found by CGM): a bundle installed by an administrator cannot have its live
/// files replaced by the person using it, so the no-close update (contract 5a) waited for the next
/// elevated install. The live files go to the user's overlay, and the host loads them only while the
/// signed manifest, the hash and the version still vouch for them.
/// </summary>
public sealed class ALIVEFileGOESToTheUSERSOverlayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "erks-overlay-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void TheOverlayCopyIsLoadedOnlyWhileItIsVouchedFor()
    {
        string install = Dir("install"), staging = Dir("staging"), local = Dir("local");
        Put(install, "Templates/sheet.json", "template 1");
        InstallManifest.Save(install, InstallManifest.Scan(install, "ErkS.CGM", "v0.001.1"));
        string clean = "template 2";
        var target = new UpdateManifest
        {
            ProductCode = "ErkS.CGM",
            Version = "v0.001.2",
            Files = [new UpdateFile { Path = "Templates/sheet.json", Sha256 = Sha(clean), Size = clean.Length, Apply = UpdateFile.ApplyLive }],
        };
        Directory.CreateDirectory(Path.Combine(staging, "files"));
        File.WriteAllText(DeltaDownloader.StagedPath(staging, Sha(clean)), clean);
        using RSA key = RSA.Create(2048);
        using X509Certificate2 certificate = new CertificateRequest("CN=Erk-S Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            .CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        string signed = SignedManifest.Create(target, certificate).ToJson();
        UpdatePlan plan = UpdatePlanner.Plan(InstallManifest.LoadOrScan(install, "ErkS.CGM", "?"), target);

        LiveApplyResult result = LiveOverlay.Apply("ErkS.CGM", staging, plan, signed, local, certificate.Thumbprint);

        Assert.Equal(["Templates/sheet.json"], result.Applied);
        Assert.Equal("template 1", File.ReadAllText(Path.Combine(install, "Templates", "sheet.json"))); // the install is not touched
        string loaded = LiveOverlay.Resolve(install, "ErkS.CGM", "Templates/sheet.json", local, certificate.Thumbprint);
        Assert.Equal(clean, File.ReadAllText(loaded));
        // A file the manifest does not name as live comes from the install.
        Assert.StartsWith(install, LiveOverlay.Resolve(install, "ErkS.CGM", "ErkS.CGM.dll", local, certificate.Thumbprint));

        // Changed by hand: the install's copy is used.
        File.WriteAllText(loaded, "tampered");
        Assert.StartsWith(install, LiveOverlay.Resolve(install, "ErkS.CGM", "Templates/sheet.json", local, certificate.Thumbprint));
        File.WriteAllText(loaded, clean);
        // Another signer pinned: nothing from the overlay.
        Assert.StartsWith(install, LiveOverlay.Resolve(install, "ErkS.CGM", "Templates/sheet.json", local, ManifestSignature.ErkSThumbprint));
        // The install caught up: the overlay is no longer needed.
        InstallManifest.Save(install, InstallManifest.Scan(install, "ErkS.CGM", "v0.001.2"));
        Assert.StartsWith(install, LiveOverlay.Resolve(install, "ErkS.CGM", "Templates/sheet.json", local, certificate.Thumbprint));

        LiveOverlay.Clear("ErkS.CGM", local);
        Assert.False(Directory.Exists(LiveOverlay.Root("ErkS.CGM", local)));
    }

    [Fact]
    public void VersionsCompareByTheirNumbers()
    {
        Assert.True(LiveOverlay.Compare("v0.001.105", "V0.001.106") < 0);
        Assert.True(LiveOverlay.Compare("0.002", "v0.001.999") > 0);
        Assert.Equal(0, LiveOverlay.Compare("V0.001.100", "v0.001.100"));
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

    private static string Sha(string text) => Convert.ToHexStringLower(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
}
