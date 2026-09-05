using System.Security.Cryptography;
using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The client half of the bot-session contract agreed with SRV on 2026-09-05.
/// The payload is nonce ‖ SHA256(SPKI) - no e-mail, no public key on the wire -
/// and both halves were compared byte for byte before either was called.
/// </summary>
public sealed class StudioBotSessionSignatureTests
{
    private static readonly byte[] Nonce =
        Convert.FromHexString("000102030405060708090A0B0C0D0E0F101112131415161718191A1B1C1D1E1F");

    [Fact]
    public void ThePayloadIsExactlyTheNonceThenTheKeyDigest()
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] spki = key.ExportSubjectPublicKeyInfo();

        byte[] payload = StudioDeviceKeyStore.BotSessionPayload(Nonce, spki);

        Assert.Equal(64, payload.Length);
        Assert.Equal(Nonce, payload[..32]);
        Assert.Equal(SHA256.HashData(spki), payload[32..]);
    }

    [Fact]
    public void NoEMailIsInIt_WhichIsWhatMakesItShorterThanTheRegistration()
    {
        // A seated device does not act for a person. Registration signs
        // nonce ‖ digest ‖ e-mail precisely so it cannot be replayed against
        // another account; a bot session has no account to name, and naming one
        // would have the machine announce a credential it no longer holds.
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] spki = key.ExportSubjectPublicKeyInfo();

        byte[] session = StudioDeviceKeyStore.BotSessionPayload(Nonce, spki);
        byte[] registration = [.. Nonce, .. SHA256.HashData(spki), .. Encoding.UTF8.GetBytes("owner@erk-s.local")];

        Assert.True(session.Length < registration.Length);
        Assert.Equal(session, registration[..session.Length]);
    }

    [Fact]
    public void ASessionSignatureDoesNotVerifyAsARegistrationOne()
    {
        // The two payloads share a prefix, so this is worth pinning: a captured
        // session signature must not be usable where a registration is expected,
        // in either direction.
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] spki = key.ExportSubjectPublicKeyInfo();
        byte[] signature = key.SignData(
            StudioDeviceKeyStore.BotSessionPayload(Nonce, spki),
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        byte[] registration = [.. Nonce, .. SHA256.HashData(spki), .. Encoding.UTF8.GetBytes("owner@erk-s.local")];
        Assert.False(key.VerifyData(
            registration,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void TheSignatureIsP1363AndVerifiesTheWayTheServerWillRecomputeIt()
    {
        byte[]? signature = StudioDeviceKeyStore.SignBotSession(Nonce);
        Assert.NotNull(signature);

        // P-256 in IEEE P1363 is r‖s, 64 bytes. DER would be variable-length and
        // the server refuses it by name.
        Assert.Equal(64, signature!.Length);

        // Verified from the public key alone, against a payload rebuilt rather
        // than reused - which is all the server will have.
        byte[] publicKey = StudioDeviceKeyStore.PublicKey();
        using ECDsa verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(publicKey, out _);
        Assert.True(verifier.VerifyData(
            StudioDeviceKeyStore.BotSessionPayload(Nonce, publicKey),
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void TheFingerprintTheServerLooksTheKeyUpByIsTheDigestOfTheSameBytes()
    {
        // deviceFingerprint on the wire is SHA256(SPKI) as uppercase hex - the
        // value the registration recorded - computed from the key itself, never
        // read back from a local marker file. A seated machine has no account to
        // key a marker by, and a self-written field proves nothing anyway.
        byte[] publicKey = StudioDeviceKeyStore.PublicKey();

        Assert.Equal(
            Convert.ToHexString(SHA256.HashData(publicKey)),
            StudioDeviceKeyStore.Fingerprint());
    }
}
