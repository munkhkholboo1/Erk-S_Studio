using System.Security.Cryptography;
using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// These touch the real Windows key store, because that is the thing under
/// test: the design rests on a private key the provider refuses to export, and
/// a mock would assert only that the mock was written to agree.
/// </summary>
public sealed class StudioDeviceKeyStoreTests
{
    [Fact]
    public void TheKeyPersistsAndItsPrivateHalfCannotBeExported()
    {
        using ECDsa first = StudioDeviceKeyStore.Open();
        byte[] publicKey = first.ExportSubjectPublicKeyInfo();

        // Opened again - a named, persisted key, not one that dies with the
        // handle. An ephemeral key would look exactly like a key that failed
        // to survive a restart.
        using ECDsa second = StudioDeviceKeyStore.Open();
        Assert.Equal(publicKey, second.ExportSubjectPublicKeyInfo());

        // The property the whole design rests on.
        var cng = (ECDsaCng)second;
        Assert.Equal(CngExportPolicies.None, cng.Key.ExportPolicy);
        Assert.Throws<CryptographicException>(() =>
            cng.Key.Export(CngKeyBlobFormat.EccPrivateBlob));
    }

    [Fact]
    public void TheFingerprintHasTheSameShapeAsTheOneItReplaces()
    {
        // 64 uppercase hex - identical to the trait-based fingerprint, so
        // nothing on the wire changes and no DTO moves. Only the origin of the
        // value does.
        string fingerprint = StudioDeviceKeyStore.Fingerprint();

        Assert.Equal(64, fingerprint.Length);
        Assert.All(fingerprint, c => Assert.True(char.IsAsciiHexDigitUpper(c)));
        Assert.Equal(fingerprint, StudioDeviceKeyStore.FingerprintOf(StudioDeviceKeyStore.PublicKey()));
    }

    [Fact]
    public void TheRegistrationSignatureIsP1363OverNonceKeyAndEmail()
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        const string email = "owner@erk-s.local";

        byte[] signature = StudioDeviceKeyStore.SignRegistration(nonce, email);

        // P-256 in IEEE P1363 is exactly r||s, 64 bytes. A DER signature would
        // be variable-length and the server refuses it by name.
        Assert.Equal(64, signature.Length);

        // Verify the way the server does: recompute the payload rather than
        // trusting the one that was signed.
        byte[] publicKey = StudioDeviceKeyStore.PublicKey();
        byte[] digest = SHA256.HashData(publicKey);
        byte[] emailBytes = Encoding.UTF8.GetBytes(email);
        byte[] payload = [.. nonce, .. digest, .. emailBytes];

        using ECDsa verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(publicKey, out _);
        Assert.True(verifier.VerifyData(
            payload,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void ASignatureForOneAccountDoesNotVerifyForAnother()
    {
        // The e-mail is inside the signed payload precisely so a captured
        // registration cannot be replayed against a different account.
        byte[] nonce = RandomNumberGenerator.GetBytes(32);
        byte[] signature = StudioDeviceKeyStore.SignRegistration(nonce, "owner@erk-s.local");

        byte[] publicKey = StudioDeviceKeyStore.PublicKey();
        byte[] payload =
        [
            .. nonce,
            .. SHA256.HashData(publicKey),
            .. Encoding.UTF8.GetBytes("someone-else@erk-s.local"),
        ];

        using ECDsa verifier = ECDsa.Create();
        verifier.ImportSubjectPublicKeyInfo(publicKey, out _);
        Assert.False(verifier.VerifyData(
            payload,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void UntilAKeyIsRegisteredNothingAboutTheSentIdentityChanges()
    {
        StudioDeviceIdentity.ForgetRegisteredKeyFingerprint();
        StudioDeviceFingerprints traits = StudioDeviceIdentity.TraitBasedFingerprints;

        Assert.Equal(traits.Canonical, StudioDeviceIdentity.Fingerprints.Canonical);
        Assert.Equal(traits.Legacy, StudioDeviceIdentity.Fingerprints.Legacy);

        try
        {
            StudioDeviceIdentity.UseRegisteredKeyFingerprint("ABC123");

            // The key becomes canonical and the TRAIT-canonical value moves
            // into the legacy slot - not the older product salt. Sending the
            // oldest form here would strand every record the server holds
            // under the trait-canonical one.
            Assert.Equal("ABC123", StudioDeviceIdentity.Fingerprints.Canonical);
            Assert.Equal(traits.Canonical, StudioDeviceIdentity.Fingerprints.Legacy);
            Assert.NotEqual(traits.Legacy, StudioDeviceIdentity.Fingerprints.Legacy);
        }
        finally
        {
            StudioDeviceIdentity.ForgetRegisteredKeyFingerprint();
        }
    }
}
