using System.Security.Cryptography;
using System.Text;

namespace ErkS.Studio;

/// <summary>
/// This device's own key pair - the thing that will make it a peer.
///
/// Today the device is identified by a FINGERPRINT: a hash of machine and user
/// facts. That is enough to recognise a machine the server already knows, and
/// not enough to be an identity between peers: a fingerprint can be copied,
/// and nothing about it can be signed. A key pair can. Introducing it now,
/// while the bot work is being done, means the peer layer inherits an identity
/// that already exists rather than replacing one that does not.
///
/// P-256, not Ed25519/X25519. Measured 2026-09-04: .NET 9's BCL offers ECDSA
/// and ECDH over the NIST curves, AES-GCM and HKDF, and no Ed25519 at all -
/// so the earlier P2P contract example naming Ed25519/X25519 was written from
/// habit rather than from what this runtime has. Adding a crypto dependency to
/// keep those names is a cost with no gain here; the design needs "sign" and
/// "agree on a shared secret", and both are present.
/// </summary>
internal sealed class StudioDeviceKeyPair
{
    /// <summary>SubjectPublicKeyInfo of the signing key. What a peer verifies against.</summary>
    public required byte[] SigningPublicKey { get; init; }

    /// <summary>SubjectPublicKeyInfo of the key-agreement key. What a project key is wrapped to.</summary>
    public required byte[] AgreementPublicKey { get; init; }

    /// <summary>PKCS#8 private keys. Never leave this machine; sealed at rest.</summary>
    public required byte[] SigningPrivateKey { get; init; }

    public required byte[] AgreementPrivateKey { get; init; }

    /// <summary>
    /// This device's peer identity: whoever can sign with the key IS this id.
    /// Base32 of SHA-256 over the signing public key, 16 characters a person
    /// can read out loud.
    ///
    /// NOT the bot id, and the difference matters. A bot id is a SEAT: the
    /// server issues it, and the assignments and career history hang off it, so
    /// it outlives any one machine. A device key id is a MACHINE. Folding them
    /// together would break a seat's history every time somebody changed
    /// laptops (decided with SRV and Master, 2026-09-04).
    ///
    /// Self-certifying is the property that matters here: a peer can check that
    /// the party it is talking to owns this id without asking a server, so a
    /// LAN works with nobody's help and a wrong address costs nothing.
    /// </summary>
    public string DeviceKeyId => DeriveKeyId(SigningPublicKey);

    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string DeriveKeyId(byte[] signingPublicKey)
    {
        ArgumentNullException.ThrowIfNull(signingPublicKey);
        byte[] digest = SHA256.HashData(signingPublicKey);
        var text = new StringBuilder(16);
        int buffer = 0;
        int bits = 0;
        foreach (byte value in digest)
        {
            buffer = (buffer << 8) | value;
            bits += 8;
            while (bits >= 5 && text.Length < 16)
            {
                bits -= 5;
                text.Append(Base32Alphabet[(buffer >> bits) & 31]);
            }
            if (text.Length == 16)
                break;
        }
        return text.ToString();
    }

    public static StudioDeviceKeyPair Create()
    {
        using ECDsa signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using ECDiffieHellman agreement = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        return new StudioDeviceKeyPair
        {
            SigningPublicKey = signing.ExportSubjectPublicKeyInfo(),
            SigningPrivateKey = signing.ExportPkcs8PrivateKey(),
            AgreementPublicKey = agreement.PublicKey.ExportSubjectPublicKeyInfo(),
            AgreementPrivateKey = agreement.ExportPkcs8PrivateKey(),
        };
    }

    public byte[] Sign(ReadOnlySpan<byte> data)
    {
        using ECDsa signing = ECDsa.Create();
        signing.ImportPkcs8PrivateKey(SigningPrivateKey, out _);
        return signing.SignData(data, HashAlgorithmName.SHA256);
    }

    public static bool Verify(byte[] signingPublicKey, ReadOnlySpan<byte> data, ReadOnlySpan<byte> signature)
    {
        ArgumentNullException.ThrowIfNull(signingPublicKey);
        try
        {
            using ECDsa verifier = ECDsa.Create();
            verifier.ImportSubjectPublicKeyInfo(signingPublicKey, out _);
            return verifier.VerifyData(data, signature, HashAlgorithmName.SHA256);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// The shared secret with another device, from which a wrapping key is
    /// derived. Used to seal a project key to a member without either side
    /// sending it - the property the escrow ring rests on.
    /// </summary>
    public byte[] DeriveSharedKey(byte[] peerAgreementPublicKey, string purpose)
    {
        ArgumentNullException.ThrowIfNull(peerAgreementPublicKey);
        using ECDiffieHellman own = ECDiffieHellman.Create();
        own.ImportPkcs8PrivateKey(AgreementPrivateKey, out _);
        using ECDiffieHellman peer = ECDiffieHellman.Create();
        peer.ImportSubjectPublicKeyInfo(peerAgreementPublicKey, out _);
        return own.DeriveKeyFromHmac(
            peer.PublicKey,
            HashAlgorithmName.SHA256,
            hmacKey: null,
            secretPrepend: null,
            secretAppend: Encoding.UTF8.GetBytes(purpose));
    }
}
