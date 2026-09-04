using System.Security.Cryptography;
using System.Text;

namespace ErkS.Studio;

/// <summary>
/// Seals a seated device's bot credential under its PIN.
///
/// Verification is LOCAL by design: there is no server endpoint that answers
/// "is this PIN right", because that would be a bot authenticating against the
/// server. Instead the PIN derives a key, and the right PIN is the one that
/// opens the sealed blob - a wrong one fails to decrypt and proves itself
/// wrong without anybody being asked.
///
/// The bot id is authenticated alongside the payload, so a record edited to
/// name another seat fails to open rather than handing over the wrong seat's
/// credential.
/// </summary>
internal static class StudioBotPinVault
{
    private const int SaltSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int KeySize = 32;

    /// <summary>
    /// Deliberately high for four digits: the whole key space is 10,000, so the
    /// only thing standing between a copied file and the credential is how long
    /// each guess takes. A local unlock happens once a day; a guessing run
    /// happens ten thousand times.
    /// </summary>
    private const int Iterations = 600_000;

    public sealed record SealedBotCredential(byte[] Blob);

    public static bool IsWellFormedPin(string? pin) =>
        pin is { Length: 4 } && pin.All(char.IsAsciiDigit);

    /// <summary>
    /// Seals <paramref name="credential"/> so that only <paramref name="pin"/>
    /// reopens it on this machine, for this seat.
    /// </summary>
    public static SealedBotCredential Seal(string botId, string pin, string credential)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(botId);
        if (!IsWellFormedPin(pin))
            throw new ArgumentException("ПИН нь яг 4 оронтой тоо байна.", nameof(pin));
        ArgumentNullException.ThrowIfNull(credential);

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] key = DeriveKey(pin, salt);
        byte[] plaintext = Encoding.UTF8.GetBytes(credential);
        byte[] cipher = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintext, cipher, tag, AssociatedData(botId));
        }
        CryptographicOperations.ZeroMemory(key);
        CryptographicOperations.ZeroMemory(plaintext);

        byte[] blob = new byte[SaltSize + NonceSize + TagSize + cipher.Length];
        salt.CopyTo(blob, 0);
        nonce.CopyTo(blob, SaltSize);
        tag.CopyTo(blob, SaltSize + NonceSize);
        cipher.CopyTo(blob, SaltSize + NonceSize + TagSize);
        return new SealedBotCredential(blob);
    }

    /// <summary>
    /// Returns the credential, or null when the PIN is wrong, the seat does not
    /// match, or the blob has been tampered with. One answer for all three on
    /// purpose: telling them apart would say which part of a guess was right.
    /// </summary>
    public static string? TryOpen(string botId, string pin, SealedBotCredential sealedCredential)
    {
        ArgumentNullException.ThrowIfNull(sealedCredential);
        if (string.IsNullOrWhiteSpace(botId) ||
            !IsWellFormedPin(pin) ||
            sealedCredential.Blob.Length <= SaltSize + NonceSize + TagSize)
        {
            return null;
        }

        ReadOnlySpan<byte> blob = sealedCredential.Blob;
        byte[] key = DeriveKey(pin, blob[..SaltSize].ToArray());
        byte[] cipher = blob[(SaltSize + NonceSize + TagSize)..].ToArray();
        byte[] plaintext = new byte[cipher.Length];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(
                blob.Slice(SaltSize, NonceSize),
                cipher,
                blob.Slice(SaltSize + NonceSize, TagSize),
                plaintext,
                AssociatedData(botId));
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException)
        {
            return null;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private static byte[] AssociatedData(string botId) =>
        Encoding.UTF8.GetBytes("erks.bot.seat:" + botId.Trim().ToLowerInvariant());

    private static byte[] DeriveKey(string pin, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(pin),
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
}
