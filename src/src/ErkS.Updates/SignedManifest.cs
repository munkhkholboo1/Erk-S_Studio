using System;
using System.IO;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErkS.Updates;

/// <summary>
/// The server's signed manifest (SRV, 2026-10-26):
/// <c>{format, body: base64(UTF-8 JSON), signature: RSA-SHA256 PKCS#1, certificate: DER base64, signerThumbprint}</c>.
/// The certificate's thumbprint is matched to the pin first, the signature is checked over the
/// body's exact bytes, and only then is the body read - nothing is re-serialised to be checked.
/// </summary>
public sealed class SignedManifest
{
    public const int CurrentFormat = 1;

    [JsonPropertyName("format")]
    public int Format { get; set; } = CurrentFormat;

    /// <summary>Base64 of the manifest's UTF-8 JSON - the bytes that were signed.</summary>
    [JsonPropertyName("body")]
    public string Body { get; set; } = "";

    /// <summary>Base64 RSA-SHA256 (PKCS#1 v1.5) signature over the body bytes.</summary>
    [JsonPropertyName("signature")]
    public string Signature { get; set; } = "";

    /// <summary>Base64 DER of the signing certificate.</summary>
    [JsonPropertyName("certificate")]
    public string Certificate { get; set; } = "";

    [JsonPropertyName("signerThumbprint")]
    public string SignerThumbprint { get; set; } = "";

    public string ToJson() => JsonSerializer.Serialize(this, UpdateManifest.Json);

    public static SignedManifest FromJson(string json) =>
        JsonSerializer.Deserialize<SignedManifest>(json, UpdateManifest.Json) ?? throw new InvalidDataException("Гарын үсэгтэй манифест хоосон байна.");

    /// <summary>Signs a manifest (publishing tools, tests) with a certificate that has its private key.</summary>
    public static SignedManifest Create(UpdateManifest manifest, X509Certificate2 certificate)
    {
        byte[] body = Encoding.UTF8.GetBytes(manifest.ToJson());
        using RSA rsa = certificate.GetRSAPrivateKey() ?? throw new InvalidOperationException("Сертификатад RSA хувийн түлхүүр алга.");
        return new SignedManifest
        {
            Body = Convert.ToBase64String(body),
            Signature = Convert.ToBase64String(rsa.SignData(body, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)),
            Certificate = Convert.ToBase64String(certificate.RawData),
            SignerThumbprint = certificate.Thumbprint,
        };
    }

    /// <summary>The manifest, only when the certificate is the pinned one and its signature holds over the body.</summary>
    public UpdateManifest Verify(string pinnedThumbprint)
    {
        if (!TryVerify(pinnedThumbprint, out byte[] body))
            throw new InvalidDataException("Шинэчлэлийн манифестын гарын үсэг таарахгүй байна — татахгүй.");
        return UpdateManifest.FromJson(Encoding.UTF8.GetString(body));
    }

    private bool TryVerify(string pinnedThumbprint, out byte[] body)
    {
        body = Array.Empty<byte>();
        try
        {
            using var certificate = new X509Certificate2(Convert.FromBase64String(Certificate));
            if (!certificate.Thumbprint.Equals(pinnedThumbprint, StringComparison.OrdinalIgnoreCase) ||
                (SignerThumbprint.Length > 0 && !SignerThumbprint.Equals(certificate.Thumbprint, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
            body = Convert.FromBase64String(Body);
            using RSA? rsa = certificate.GetRSAPublicKey();
            return rsa is not null && rsa.VerifyData(body, Convert.FromBase64String(Signature), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
