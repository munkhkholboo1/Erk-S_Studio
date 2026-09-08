using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace ErkS.Studio;

/// <summary>
/// What this device is working as, left where the other products can read it.
///
/// THE DECISION BEHIND IT, in the user's words: «PFA PFR CGA CGM бүгдэд
/// бүртгэлээрээ нэвтрэх функц байхгүй болно. зөвхөн студиод нэвтэрснээр лиценз
/// таньдаг болох ёстой.» So Studio is the only place anybody signs in, and the
/// plugins stop being clients of the licence server's sign-in and become readers
/// of this record.
///
/// 🔴 THE RECORD IS A STATE, NOT A PERMISSION. It says who this device is
/// working as and nothing about what that identity may do. The entitlement is
/// resolved by the server from <see cref="HandoffToken"/> - flags it computes,
/// not a tier a client interprets. That separation is the whole reason the
/// current design exists: three plugins each deciding what a licence type grants
/// is how two of them came to disagree about the same licence, and moving that
/// interpretation into Studio would be the same defect with fewer copies.
///
/// WHY <see cref="State"/> IS ALWAYS READABLE. A plugin has no sign-in of its
/// own to fall back to any more, so «it did not work» has to name which of four
/// different situations it was: Studio was never installed (no record at all),
/// nobody is signed in, the machine is a bot seat whose PIN has not been
/// entered, or the identity lapsed because Studio has not been opened. Sealing
/// the whole record while locked would collapse the middle two into «missing»,
/// and a person would go looking for a sign-in dialog that no longer exists.
/// So the STATE is in the clear and only the identity behind it is withheld.
/// </summary>
internal sealed class StudioSsoIdentityRecord
{
    /// <summary>
    /// The shape this record is written in.
    ///
    /// 🔴 READERS ACCEPT N AND N-1, AND THAT IS NOT A COURTESY HERE. Studio has
    /// paid for breaking it once already: when the device fingerprint moved to
    /// its common form, everything bound under the old value was refused without
    /// a word and a project simply looked as though it had nothing new. Under
    /// SSO the same mistake does not skip an upload - with no plugin sign-in
    /// left to fall back on, it stops four products from starting.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>The oldest shape a reader on this side still accepts.</summary>
    public const int OldestSupportedFormatVersion = 1;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    /// <summary>
    /// Bumped whenever the identity changes: sign-in, sign-out, entering or
    /// leaving bot state, switching accounts, the PIN opening a seat.
    ///
    /// It exists so a plugin that cached an entitlement can tell that the ground
    /// moved under it. Without it a long export finishes under a licence that
    /// stopped applying halfway through and nothing reports it.
    ///
    /// Bumped on CHANGE, never on republish. Studio rewrites this record every
    /// time the account UI refreshes - a counter that moved each time would tell
    /// every plugin to re-resolve because somebody opened a menu.
    /// </summary>
    public long Generation { get; set; }

    public string State { get; set; } = StudioSsoIdentityState.SignedOut;

    /// <summary>Person or Bot. Empty while <see cref="State"/> is SignedOut.</summary>
    public string IdentityKind { get; set; } = "";

    public string AccountEmail { get; set; } = "";

    public string BotId { get; set; } = "";

    public string OrganizationId { get; set; } = "";

    public StudioSsoDeviceFingerprints Device { get; set; } = new();

    /// <summary>
    /// What a plugin presents to the server to have its entitlement resolved.
    ///
    /// 🔴 ABSENT IS NOT EMPTY. The property is omitted from the JSON entirely
    /// when Studio holds no token, so a reader cannot mistake "" for a token
    /// that happens to be blank. A reader that finds no token has an identity
    /// and no way to prove it, which is a different situation from having no
    /// identity - and it gets its own refusal.
    ///
    /// NOT the account's own credential. A plugin must never hold something that
    /// could re-authenticate the person somewhere else; this is device-bound and
    /// product-scoped, minted and renewed by Studio, and useless if copied.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? HandoffToken { get; set; }

    public DateTimeOffset IssuedAtUtc { get; set; }

    /// <summary>
    /// When this record stops being usable if Studio is never opened again.
    ///
    /// Studio renews it while it runs. A lapse is not «unlicensed» - it is
    /// «open Studio once», and the refusal has to say the second thing.
    /// </summary>
    public DateTimeOffset ExpiresAtUtc { get; set; }

    /// <summary>
    /// A tripwire over the whole record, keyed to this device.
    ///
    /// 🔴 STATED HONESTLY BECAUSE THE OPPOSITE IS TEMPTING: this catches hand
    /// editing and catches the record being copied to another machine. It is
    /// NOT authentication. The key is derivable on the device by anything that
    /// can already read the record, so code running as this Windows user could
    /// forge it - which is exactly as true of the per-product licence files this
    /// replaces. What actually proves the identity is the server resolving
    /// <see cref="HandoffToken"/>; this only stops a record from travelling.
    /// </summary>
    public string StateSignature { get; set; } = "";

    /// <summary>
    /// The fields the signature covers, in a fixed order, so both sides compute
    /// the same string. Everything that decides what a reader does is in here;
    /// the signature itself is not, for the obvious reason.
    /// </summary>
    public string CanonicalForm() => string.Join(
        "\n",
        FormatVersion.ToString(CultureInfo.InvariantCulture),
        Generation.ToString(CultureInfo.InvariantCulture),
        State,
        IdentityKind,
        AccountEmail,
        BotId,
        OrganizationId,
        Device.CanonicalFingerprint,
        Device.LegacyFingerprint,
        HandoffToken ?? "",
        IssuedAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        ExpiresAtUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    /// <summary>
    /// The parts that make this a DIFFERENT identity rather than the same one
    /// written again. Compared to decide whether the generation moves.
    ///
    /// Deliberately excludes the timestamps, the token and the signature: a
    /// renewal is not an identity change, and treating it as one would tell
    /// every plugin to re-resolve on a timer.
    /// </summary>
    public string IdentityForm() => string.Join(
        "\n",
        State,
        IdentityKind,
        AccountEmail,
        BotId,
        OrganizationId,
        Device.CanonicalFingerprint,
        Device.LegacyFingerprint);
}

internal sealed class StudioSsoDeviceFingerprints
{
    /// <summary>
    /// One machine, TWO valid values, and both are carried because a reader
    /// holding only one proves nothing about a record stored under the other.
    /// The server already holds this rule for activations; repeating it here is
    /// what keeps a machine's records from being stranded the day the canonical
    /// form moves again.
    /// </summary>
    public string CanonicalFingerprint { get; set; } = "";

    public string LegacyFingerprint { get; set; } = "";
}

internal static class StudioSsoIdentityState
{
    /// <summary>Somebody is signed in, or an unlocked seat is in force.</summary>
    public const string Active = "Active";

    /// <summary>
    /// This machine holds an organisation's seat and the PIN has not been
    /// entered, so it is not acting as the seat at all yet. Named rather than
    /// absent: it is a state the person at the machine can fix in four
    /// keystrokes, and a missing record would send them looking for an install.
    /// </summary>
    public const string BotLocked = "BotLocked";

    /// <summary>
    /// Studio is here and nobody is signed in.
    ///
    /// 🔴 WRITTEN RATHER THAN DELETED, AND THAT IS THE POINT. «No record» and
    /// «nobody signed in» are different situations with different answers -
    /// install Studio, versus sign in to the Studio you already have. If signing
    /// out deleted the record the two would be indistinguishable and one of the
    /// two messages would be wrong every time.
    /// </summary>
    public const string SignedOut = "SignedOut";

    public static bool IsKnown(string? value) =>
        value == Active || value == BotLocked || value == SignedOut;
}

internal static class StudioSsoIdentityKind
{
    public const string Person = "Person";
    public const string Bot = "Bot";
}

/// <summary>
/// The tripwire over the record. Kept in one place so the value Studio writes
/// and the value a reader recomputes cannot drift apart by being written twice.
/// </summary>
internal static class StudioSsoIdentitySignature
{
    /// <summary>
    /// Published, because every product has to derive the same key. A salt is
    /// not a secret here and pretending otherwise would be the kind of
    /// self-flattering claim this record is careful not to make.
    /// </summary>
    private const string KeySalt = "Erk-S Platform SSO identity v1";

    public static string Compute(StudioSsoIdentityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        using var hmac = new HMACSHA256(DeriveKey(record.Device.CanonicalFingerprint));
        return Convert.ToBase64String(
            hmac.ComputeHash(Encoding.UTF8.GetBytes(record.CanonicalForm())));
    }

    public static bool Verify(StudioSsoIdentityRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.StateSignature))
            return false;

        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromBase64String(Compute(record)),
                Convert.FromBase64String(record.StateSignature));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static byte[] DeriveKey(string canonicalFingerprint) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(KeySalt + "|" + (canonicalFingerprint ?? "")));
}
