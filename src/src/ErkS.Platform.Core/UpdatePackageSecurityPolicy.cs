using System.Security.Cryptography;

namespace ErkS.Platform.Core;

public sealed record AuthenticodeTrustResult(
    bool IsTrusted,
    string Publisher,
    string Error,
    string SignerCertificateSha256 = "",
    uint ErrorCode = 0);

public interface IAuthenticodeTrustVerifier
{
    AuthenticodeTrustResult Verify(string path);
}

public static class UpdatePackageSecurityPolicy
{
    private const int DosHeaderLength = 64;
    private const int PeOffsetPosition = 0x3c;

    public static void ValidateTransport(Uri uri, bool isDevelopmentBuild)
    {
        ArgumentNullException.ThrowIfNull(uri);

        if (uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return;

        if (isDevelopmentBuild &&
            uri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            uri.IsLoopback)
        {
            return;
        }

        throw new InvalidOperationException(
            "Update packages must use HTTPS. Loopback HTTP is allowed only in development builds.");
    }

    public static async Task<AuthenticodeTrustResult> VerifyInstallerAsync(
        string path,
        string expectedSha256,
        string expectedPublisher,
        IAuthenticodeTrustVerifier verifier,
        IReadOnlyCollection<string>? pinnedUntrustedRootCertificateSha256 = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedPublisher);
        ArgumentNullException.ThrowIfNull(verifier);

        if (!File.Exists(path))
            throw new FileNotFoundException("The downloaded update installer was not found.", path);

        string expectedHash = NormalizeSha256(expectedSha256);
        if (expectedHash.Length != 64 || expectedHash.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("The update does not contain a valid SHA-256 checksum.");

        await using (FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            string actualHash = Convert.ToHexString(
                await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false));
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "The downloaded update SHA-256 checksum does not match. The installer will not run.");
            }
        }

        EnsurePortableExecutable(path);

        AuthenticodeTrustResult trust;
        try
        {
            trust = verifier.Verify(path);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new InvalidDataException(
                "The Authenticode signature could not be verified. The installer will not run.",
                exception);
        }

        if (!trust.IsTrusted &&
            !IsPinnedUntrustedRootSigner(
                trust,
                expectedPublisher,
                pinnedUntrustedRootCertificateSha256))
        {
            string reason = string.IsNullOrWhiteSpace(trust.Error)
                ? "The installer is unsigned or its Authenticode signature is invalid."
                : trust.Error.Trim();
            throw new InvalidDataException(reason);
        }

        if (!trust.Publisher.Trim().Equals(expectedPublisher.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"The update publisher is not trusted. Expected '{expectedPublisher}', received '{trust.Publisher}'.");
        }

        return trust.IsTrusted
            ? trust
            : trust with
            {
                IsTrusted = true,
                Error = "",
            };
    }

    public static void EnsurePortableExecutable(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 256)
            throw new InvalidDataException("The downloaded update is not a complete Windows executable.");

        Span<byte> dosHeader = stackalloc byte[DosHeaderLength];
        stream.ReadExactly(dosHeader);
        if (dosHeader[0] != (byte)'M' || dosHeader[1] != (byte)'Z')
            throw new InvalidDataException("The downloaded update is not a Windows executable.");

        int peOffset = BitConverter.ToInt32(dosHeader[PeOffsetPosition..(PeOffsetPosition + sizeof(int))]);
        if (peOffset < DosHeaderLength || peOffset > stream.Length - 6)
            throw new InvalidDataException("The downloaded update contains an invalid PE header offset.");

        stream.Position = peOffset;
        Span<byte> peHeader = stackalloc byte[6];
        stream.ReadExactly(peHeader);
        if (peHeader[0] != (byte)'P' || peHeader[1] != (byte)'E' || peHeader[2] != 0 || peHeader[3] != 0)
            throw new InvalidDataException("The downloaded update contains an invalid PE signature.");

        ushort machine = BitConverter.ToUInt16(peHeader[4..6]);
        if (machine is not 0x014c and not 0x8664 and not 0xaa64)
            throw new InvalidDataException("The downloaded update targets an unsupported Windows architecture.");
    }

    private static string NormalizeSha256(string value) => (value ?? "")
        .Trim()
        .Replace(" ", "", StringComparison.Ordinal)
        .Replace("-", "", StringComparison.Ordinal);

    private static bool IsPinnedUntrustedRootSigner(
        AuthenticodeTrustResult trust,
        string expectedPublisher,
        IReadOnlyCollection<string>? pinnedCertificateSha256)
    {
        const uint CertEUntrustedRoot = 0x800B0109;
        if (trust.ErrorCode != CertEUntrustedRoot ||
            pinnedCertificateSha256 is null ||
            pinnedCertificateSha256.Count == 0 ||
            !trust.Publisher.Trim().Equals(expectedPublisher.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string signerCertificateSha256 = NormalizeSha256(trust.SignerCertificateSha256);
        if (signerCertificateSha256.Length != 64 ||
            signerCertificateSha256.Any(character => !Uri.IsHexDigit(character)))
        {
            return false;
        }

        return pinnedCertificateSha256
            .Select(NormalizeSha256)
            .Any(pin => pin.Length == 64 &&
                pin.Equals(signerCertificateSha256, StringComparison.OrdinalIgnoreCase));
    }
}
