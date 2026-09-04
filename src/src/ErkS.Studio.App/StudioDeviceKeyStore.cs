using System.Security.Cryptography;

namespace ErkS.Studio;

/// <summary>
/// This machine's device key: a named, persisted, NON-EXPORTABLE ECDSA P-256
/// key in the Windows key store of the signed-in user.
///
/// Why a key and not the fingerprint we have: today's fingerprint is a hash of
/// machine and user facts, and every input is read by the client and sent by
/// the client. The server has never verified a device - it has accepted a
/// claim. Anyone can send any 64-hex string and be that device. Under SSO that
/// gets worse rather than better, because a forged record moves three products
/// at once instead of one.
///
/// A non-exportable key changes the claim into a proof: the private half never
/// leaves the store, so signing with it is something only this machine can do.
///
/// MEASURED on this platform (2026-09-04), not assumed:
///   - the key is created with ExportPolicy = None and a private-key export
///     attempt is refused by the provider;
///   - it persists as a file under %APPDATA%\Microsoft\Crypto\Keys and a second
///     process reads back the same public key;
///   - a .NET Framework host under the same Windows user opens it and signs -
///     which is the AutoCAD/Revit plugin case, and the reason the boundary is
///     "one Windows user = one device".
/// NOT measured: survival across a reboot. Persisted storage survives one by
/// construction and the file is on disk, but nobody has restarted this machine
/// to see it.
/// </summary>
internal static class StudioDeviceKeyStore
{
    /// <summary>
    /// Stable name. An unnamed CNG key dies with the process, which would look
    /// exactly like "the key did not survive a reboot" - the failure this
    /// design would find hardest to tell apart from a real one.
    /// </summary>
    private const string KeyName = "ErkS.Studio.DeviceIdentity.v1";

    private static readonly object Gate = new();

    public static bool Exists()
    {
        try
        {
            return CngKey.Exists(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// Opens this machine's key, creating it on first use. The caller gets an
    /// ECDsa it must dispose.
    /// </summary>
    public static ECDsa Open()
    {
        lock (Gate)
        {
            if (!Exists())
            {
                var parameters = new CngKeyCreationParameters
                {
                    Provider = CngProvider.MicrosoftSoftwareKeyStorageProvider,
                    KeyCreationOptions = CngKeyCreationOptions.None,
                    ExportPolicy = CngExportPolicies.None,
                };
                using CngKey created = CngKey.Create(CngAlgorithm.ECDsaP256, KeyName, parameters);
                return new ECDsaCng(created);
            }

            CngKey opened = CngKey.Open(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
            return new ECDsaCng(opened);
        }
    }

    public static byte[] PublicKey()
    {
        using ECDsa key = Open();
        return key.ExportSubjectPublicKeyInfo();
    }

    /// <summary>
    /// The device fingerprint derived from the key: SHA-256 over the public key
    /// as 64 uppercase hex - the SAME SHAPE the trait-based fingerprint has
    /// today. Nothing on the wire changes; only where the value comes from.
    /// </summary>
    public static string Fingerprint() => FingerprintOf(PublicKey());

    public static string FingerprintOf(byte[] subjectPublicKeyInfo)
    {
        ArgumentNullException.ThrowIfNull(subjectPublicKeyInfo);
        return Convert.ToHexString(SHA256.HashData(subjectPublicKeyInfo));
    }

    /// <summary>
    /// Signs the registration challenge.
    ///
    /// The input is a binary concatenation with no separators, exactly as the
    /// server recomputes it:
    ///   nonce || SHA256(SPKI) || UTF8(normalised e-mail)
    /// The e-mail is inside the signature so a captured registration cannot be
    /// replayed against another account.
    ///
    /// IEEE P1363 (r||s, 64 bytes for P-256), not DER - stated by the contract
    /// and enforced by a server test.
    /// </summary>
    public static byte[] SignRegistration(byte[] nonce, string normalisedEmail)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        using ECDsa key = Open();
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        byte[] digest = SHA256.HashData(publicKey);
        byte[] email = System.Text.Encoding.UTF8.GetBytes(normalisedEmail ?? "");

        byte[] payload = new byte[nonce.Length + digest.Length + email.Length];
        nonce.CopyTo(payload, 0);
        digest.CopyTo(payload, nonce.Length);
        email.CopyTo(payload, nonce.Length + digest.Length);

        return key.SignData(
            payload,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    /// <summary>
    /// Which (fingerprint, account) pairs this machine has already registered.
    ///
    /// Keyed by account as well as key, because one Windows user can sign in
    /// with two accounts and the server records both against the same key -
    /// that is a confirmed, allowed case, not a conflict. Losing this file
    /// costs two round trips and nothing else: registration is idempotent.
    /// </summary>
    private static string MarkerPath => System.IO.Path.Combine(
        StudioAccountService.AccountDataRoot,
        "device-key-registrations.json");

    public static bool IsRegistered(string fingerprint, string accountEmail)
    {
        try
        {
            if (!System.IO.File.Exists(MarkerPath))
                return false;
            HashSet<string>? marks = System.Text.Json.JsonSerializer
                .Deserialize<HashSet<string>>(System.IO.File.ReadAllText(MarkerPath));
            return marks?.Contains(MarkerKey(fingerprint, accountEmail)) == true;
        }
        catch (Exception exception) when (
            exception is System.IO.IOException
                or System.Text.Json.JsonException
                or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void MarkRegistered(string fingerprint, string accountEmail)
    {
        try
        {
            HashSet<string> marks = [];
            if (System.IO.File.Exists(MarkerPath))
            {
                marks = System.Text.Json.JsonSerializer
                    .Deserialize<HashSet<string>>(System.IO.File.ReadAllText(MarkerPath)) ?? [];
            }
            marks.Add(MarkerKey(fingerprint, accountEmail));
            System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MarkerPath)!);
            System.IO.File.WriteAllText(
                MarkerPath,
                System.Text.Json.JsonSerializer.Serialize(marks));
        }
        catch (Exception exception) when (
            exception is System.IO.IOException or UnauthorizedAccessException)
        {
            // Not being able to remember costs a repeat registration, which the
            // server accepts idempotently. Nothing to report.
        }
    }

    private static string MarkerKey(string fingerprint, string accountEmail) =>
        (fingerprint ?? "").Trim().ToUpperInvariant() + "|" +
        (accountEmail ?? "").Trim().ToLowerInvariant();

    /// <summary>
    /// Removes the key. Only for tests and for a device that is being reset -
    /// deleting it makes this machine a different device to the server, which
    /// is correct when the machine really is being handed on, and destructive
    /// when it is not.
    /// </summary>
    public static void Delete()
    {
        lock (Gate)
        {
            if (!Exists())
                return;
            using CngKey key = CngKey.Open(KeyName, CngProvider.MicrosoftSoftwareKeyStorageProvider);
            key.Delete();
        }
    }
}
