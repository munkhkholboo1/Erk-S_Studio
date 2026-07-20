using System.Security.Cryptography;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class UpdatePackageSecurityTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "erks-update-security-" + Guid.NewGuid().ToString("N"));

    public UpdatePackageSecurityTests() => Directory.CreateDirectory(root);

    [Fact]
    public async Task ValidSignedInstaller_IsAccepted()
    {
        string path = WritePortableExecutable("valid.exe");
        string hash = Sha256(path);
        var verifier = new StubVerifier(new AuthenticodeTrustResult(true, "Erk-S LLC", ""));

        AuthenticodeTrustResult result = await UpdatePackageSecurityPolicy.VerifyInstallerAsync(
            path,
            hash,
            "Erk-S LLC",
            verifier);

        Assert.True(result.IsTrusted);
        Assert.Equal("Erk-S LLC", result.Publisher);
    }

    [Theory]
    [InlineData("The installer is unsigned.")]
    [InlineData("The Authenticode signature is invalid.")]
    public async Task UnsignedOrInvalidSignature_IsRejected(string trustError)
    {
        string path = WritePortableExecutable("untrusted.exe");
        var verifier = new StubVerifier(new AuthenticodeTrustResult(false, "", trustError));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                Sha256(path),
                "Erk-S LLC",
                verifier));

        Assert.Contains(trustError, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PinnedSignerWithOnlyUntrustedRoot_IsAccepted()
    {
        const string certificateSha256 = "A8A0A7C1435FC0E63A39CB3D101D9A532E1736D83FCBB65246DCA5B485636D8A";
        string path = WritePortableExecutable("pinned-private-root.exe");
        var verifier = new StubVerifier(new AuthenticodeTrustResult(
            false,
            "Erk-S LLC",
            "certificate chain terminated in an untrusted root",
            certificateSha256,
            0x800B0109u));

        AuthenticodeTrustResult result = await UpdatePackageSecurityPolicy.VerifyInstallerAsync(
            path,
            Sha256(path),
            "Erk-S LLC",
            verifier,
            [certificateSha256]);

        Assert.True(result.IsTrusted);
        Assert.Equal(certificateSha256, result.SignerCertificateSha256);
    }

    [Fact]
    public async Task UntrustedRootWithUnknownSignerPin_IsRejected()
    {
        string path = WritePortableExecutable("unknown-private-root.exe");
        var verifier = new StubVerifier(new AuthenticodeTrustResult(
            false,
            "Erk-S LLC",
            "certificate chain terminated in an untrusted root",
            new string('A', 64),
            0x800B0109u));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                Sha256(path),
                "Erk-S LLC",
                verifier,
                [new string('B', 64)]));
    }

    [Fact]
    public async Task PinnedSignerDoesNotOverrideBadDigestTrustFailure()
    {
        const string certificateSha256 = "A8A0A7C1435FC0E63A39CB3D101D9A532E1736D83FCBB65246DCA5B485636D8A";
        string path = WritePortableExecutable("bad-authenticode-digest.exe");
        var verifier = new StubVerifier(new AuthenticodeTrustResult(
            false,
            "Erk-S LLC",
            "The digital signature of the object did not verify.",
            certificateSha256,
            0x80096010u));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                Sha256(path),
                "Erk-S LLC",
                verifier,
                [certificateSha256]));
    }

    [Fact]
    public async Task WrongPublisher_IsRejected()
    {
        string path = WritePortableExecutable("wrong-publisher.exe");
        var verifier = new StubVerifier(new AuthenticodeTrustResult(true, "Another Publisher", ""));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                Sha256(path),
                "Erk-S LLC",
                verifier));

        Assert.Contains("publisher", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ShaMismatch_IsRejectedBeforeSignatureTrust()
    {
        string path = WritePortableExecutable("hash-mismatch.exe");
        var verifier = new StubVerifier(new AuthenticodeTrustResult(true, "Erk-S LLC", ""));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                new string('0', 64),
                "Erk-S LLC",
                verifier));

        Assert.Equal(0, verifier.CallCount);
    }

    [Fact]
    public async Task NonExecutableFile_IsRejectedBeforeSignatureTrust()
    {
        string path = Path.Combine(root, "not-an-exe.bin");
        await File.WriteAllTextAsync(path, "not a PE file");
        var verifier = new StubVerifier(new AuthenticodeTrustResult(true, "Erk-S LLC", ""));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                Sha256(path),
                "Erk-S LLC",
                verifier));

        Assert.Equal(0, verifier.CallCount);
    }

    [Fact]
    public void ProductionHttpDownload_IsRejected()
    {
        Assert.Throws<InvalidOperationException>(() =>
            UpdatePackageSecurityPolicy.ValidateTransport(
                new Uri("http://127.0.0.1:5055/update.exe"),
                isDevelopmentBuild: false));
        Assert.Throws<InvalidOperationException>(() =>
            UpdatePackageSecurityPolicy.ValidateTransport(
                new Uri("http://updates.example.com/update.exe"),
                isDevelopmentBuild: true));
    }

    [Fact]
    public void LoopbackHttpDevelopmentDownload_IsAllowed()
    {
        UpdatePackageSecurityPolicy.ValidateTransport(
            new Uri("http://127.0.0.1:5055/update.exe"),
            isDevelopmentBuild: true);
        UpdatePackageSecurityPolicy.ValidateTransport(
            new Uri("https://erk-s.mn/update.exe"),
            isDevelopmentBuild: false);
    }

    [Theory]
    [InlineData((ushort)0x014c)]
    [InlineData((ushort)0x8664)]
    [InlineData((ushort)0xaa64)]
    public async Task SupportedWindowsArchitectures_AreAccepted(ushort machine)
    {
        string path = WritePortableExecutable($"machine-{machine:x4}.exe", machine);

        AuthenticodeTrustResult result = await UpdatePackageSecurityPolicy.VerifyInstallerAsync(
            path,
            Sha256(path),
            " Erk-S LLC ",
            new StubVerifier(new AuthenticodeTrustResult(true, "erK-s llc", "")));

        Assert.True(result.IsTrusted);
    }

    [Theory]
    [InlineData("bad-m.exe", 0, (byte)'N')]
    [InlineData("bad-z.exe", 1, (byte)'Y')]
    [InlineData("bad-p.exe", 0x80, (byte)'Q')]
    [InlineData("bad-e.exe", 0x81, (byte)'F')]
    [InlineData("bad-pe-null-1.exe", 0x82, (byte)1)]
    [InlineData("bad-pe-null-2.exe", 0x83, (byte)1)]
    public async Task InvalidPortableExecutableSignatures_AreRejected(
        string name,
        int index,
        byte value)
    {
        string path = WritePortableExecutable(name, mutate: bytes => bytes[index] = value);
        var verifier = new StubVerifier(new AuthenticodeTrustResult(true, "Erk-S LLC", ""));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                Sha256(path),
                "Erk-S LLC",
                verifier));

        Assert.Equal(0, verifier.CallCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1020)]
    public async Task InvalidPeHeaderOffsets_AreRejected(int peOffset)
    {
        string path = WritePortableExecutable(
            $"bad-offset-{peOffset}.exe",
            mutate: bytes => BitConverter.GetBytes(peOffset).CopyTo(bytes, 0x3c));
        var verifier = new StubVerifier(new AuthenticodeTrustResult(true, "Erk-S LLC", ""));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                Sha256(path),
                "Erk-S LLC",
                verifier));

        Assert.Equal(0, verifier.CallCount);
    }

    [Fact]
    public async Task UnsupportedPeArchitecture_IsRejected()
    {
        string path = WritePortableExecutable("unsupported-machine.exe", machine: 0x01c4);
        var verifier = new StubVerifier(new AuthenticodeTrustResult(true, "Erk-S LLC", ""));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                Sha256(path),
                "Erk-S LLC",
                verifier));

        Assert.Equal(0, verifier.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-sha256")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    public async Task InvalidExpectedHash_IsRejected(string expectedHash)
    {
        string path = WritePortableExecutable("invalid-expected-hash.exe");
        var verifier = new StubVerifier(new AuthenticodeTrustResult(true, "Erk-S LLC", ""));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                expectedHash,
                "Erk-S LLC",
                verifier));

        Assert.Equal(0, verifier.CallCount);
    }

    [Fact]
    public async Task EmptyTrustError_UsesControlledUnsignedMessage()
    {
        string path = WritePortableExecutable("empty-trust-error.exe");

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                Sha256(path),
                "Erk-S LLC",
                new StubVerifier(new AuthenticodeTrustResult(false, "", "  "))));

        Assert.Contains("unsigned", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TrustVerifierFailure_IsWrappedAsControlledDataError()
    {
        string path = WritePortableExecutable("verifier-failure.exe");

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                Sha256(path),
                "Erk-S LLC",
                new ThrowingVerifier(new InvalidOperationException("trust provider unavailable"))));

        Assert.IsType<InvalidOperationException>(error.InnerException);
    }

    [Fact]
    public async Task TrustVerifierCancellation_IsNotHidden()
    {
        string path = WritePortableExecutable("verifier-cancelled.exe");

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            UpdatePackageSecurityPolicy.VerifyInstallerAsync(
                path,
                Sha256(path),
                "Erk-S LLC",
                new ThrowingVerifier(new OperationCanceledException())));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }

    private string WritePortableExecutable(
        string name,
        ushort machine = 0x8664,
        Action<byte[]>? mutate = null)
    {
        string path = Path.Combine(root, name);
        byte[] bytes = new byte[1024];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        BitConverter.GetBytes(0x80).CopyTo(bytes, 0x3c);
        bytes[0x80] = (byte)'P';
        bytes[0x81] = (byte)'E';
        bytes[0x82] = 0;
        bytes[0x83] = 0;
        BitConverter.GetBytes(machine).CopyTo(bytes, 0x84);
        mutate?.Invoke(bytes);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static string Sha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private sealed class StubVerifier(AuthenticodeTrustResult result) : IAuthenticodeTrustVerifier
    {
        public int CallCount { get; private set; }

        public AuthenticodeTrustResult Verify(string path)
        {
            CallCount++;
            return result;
        }
    }

    private sealed class ThrowingVerifier(Exception exception) : IAuthenticodeTrustVerifier
    {
        public AuthenticodeTrustResult Verify(string path) => throw exception;
    }
}
