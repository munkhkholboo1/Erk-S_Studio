using System;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ErkS.Updates;

/// <summary>
/// A manifest's signature: a detached CMS (PKCS#7) over the UTF-8 bytes of the manifest JSON, made
/// with the Erk-S certificate. Checking it proves the bytes and pins the signer by thumbprint -
/// the certificate is the platform's own, so its chain is not asked of Windows.
/// </summary>
public static class ManifestSignature
{
    /// <summary>The Erk-S code-signing certificate every product's updates are pinned to.</summary>
    public const string ErkSThumbprint = "52B2508A0CC5DABBE706B949DE21AD038031AD1C";

    /// <summary>Signs with a certificate that has its private key (the publisher's store).</summary>
    public static string Sign(string manifestJson, X509Certificate2 certificate)
    {
        var content = new ContentInfo(Encoding.UTF8.GetBytes(manifestJson));
        var cms = new SignedCms(content, detached: true);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, certificate)
        {
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        cms.ComputeSignature(signer, silent: true);
        return Convert.ToBase64String(cms.Encode());
    }

    /// <summary>Signs with the certificate of that thumbprint from CurrentUser\My or LocalMachine\My.</summary>
    public static string Sign(string manifestJson, string thumbprint)
    {
        foreach (StoreLocation location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.My, location);
            store.Open(OpenFlags.ReadOnly);
            X509Certificate2Collection found = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            if (found.Count > 0 && found[0].HasPrivateKey)
                return Sign(manifestJson, found[0]);
        }
        throw new InvalidOperationException("Гарын үсгийн сертификат олдсонгүй: " + thumbprint);
    }

    public static bool Verify(string manifestJson, string signatureBase64, string pinnedThumbprint)
    {
        if (string.IsNullOrEmpty(manifestJson) || string.IsNullOrEmpty(signatureBase64))
            return false;
        try
        {
            var cms = new SignedCms(new ContentInfo(Encoding.UTF8.GetBytes(manifestJson)), detached: true);
            cms.Decode(Convert.FromBase64String(signatureBase64));
            cms.CheckSignature(verifySignatureOnly: true);
            foreach (SignerInfo signer in cms.SignerInfos)
            {
                if (signer.Certificate is { } certificate &&
                    certificate.Thumbprint.Equals(pinnedThumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException)
        {
            return false;
        }
    }
}
