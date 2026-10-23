using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace ErkS.Sync.Core;

/// <summary>
/// This machine's device key, as SYN uses it: Studio's own non-exportable ECDSA P-256 key
/// in the signed-in Windows user's key store.
///
/// 🔴 SYN HAS NO LOGIN OF ITS OWN (plan §4.8). The same Windows user's other processes open
/// this key and sign with it - measured 2026-09-04 - so ErkS.Sync.exe proves which machine it
/// is without a password and without a token Studio would have to hand over.
///
/// ⚠ IT NEVER CREATES THE KEY. Studio creates and registers it; a SYN that minted one would
/// present a fingerprint the server has never seen.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SyncDeviceKey
{
    /// <summary>The persisted CNG key name. Studio's StudioDeviceKeyStore opens the same one.</summary>
    public const string KeyName = "ErkS.Studio.DeviceIdentity.v1";

    /// <summary>Signature domain of the sync device session (SRV contract sync-folder, deviceSession).</summary>
    public const string SessionDomain = "erks-sync-device-session";

    /// <summary>This machine's server device key id, or null when Studio has not created the key yet.</summary>
    public static string? TryDeviceKeyId()
    {
        using ECDsa? key = TryOpen();
        return key is null ? null : SyncDeviceKeyIds.Of(key.ExportSubjectPublicKeyInfo());
    }

    public static ECDsa? TryOpen()
    {
        try
        {
            if (!CngKey.Exists(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider))
                return null;
            return new ECDsaCng(CngKey.Open(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider));
        }
        catch (CryptographicException)
        {
            return null;
        }
    }


    /// <summary>ECDSA-SHA256, IEEE P1363, over UTF8(domain) || nonce || SHA256(SPKI).</summary>
    public static byte[] SignSession(ECDsa key, byte[] nonce)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(nonce);
        byte[] spkiHash = SHA256.HashData(key.ExportSubjectPublicKeyInfo());
        byte[] domain = Encoding.UTF8.GetBytes(SessionDomain);
        byte[] message = new byte[domain.Length + nonce.Length + spkiHash.Length];
        domain.CopyTo(message, 0);
        nonce.CopyTo(message, domain.Length);
        spkiHash.CopyTo(message, domain.Length + nonce.Length);
        return key.SignData(message, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>
    /// A short-lived self-signed certificate over the device key, for mutual TLS between
    /// members' machines. Its only meaning is its public key: a peer accepts it when that key
    /// is one the server lists for the folder - the name and dates prove nothing.
    /// </summary>
    public static X509Certificate2 CreateTlsCertificate(ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var request = new CertificateRequest("CN=Erk-S Sync device", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(
            [new Oid("1.3.6.1.5.5.7.3.1"), new Oid("1.3.6.1.5.5.7.3.2")],
            critical: false));
        DateTimeOffset now = DateTimeOffset.UtcNow;
        X509Certificate2 certificate = request.CreateSelfSigned(now.AddMinutes(-5), now.AddDays(2));
        // SChannel cannot present an ephemeral key; the device key is persisted and is used as
        // it is. Only an in-memory key (tests) takes the PKCS#12 round trip.
        if (key is ECDsaCng { Key.IsEphemeral: false })
            return certificate;
        using (certificate)
            return X509CertificateLoader.LoadPkcs12(certificate.Export(X509ContentType.Pkcs12), null);
    }
}
