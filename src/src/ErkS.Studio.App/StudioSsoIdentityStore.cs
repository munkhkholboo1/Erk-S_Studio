namespace ErkS.Studio;

/// <summary>
/// Why a read did not produce a usable record. Four situations, four answers -
/// the reader on the other side names each of them separately and so must this
/// side, because two of them mean «write the record again» and one means
/// «writing will not work either».
/// </summary>
internal enum StudioSsoIdentityReadOutcome
{
    /// <summary>A record was there and this build understands it.</summary>
    Found,

    /// <summary>Nothing is stored. A first run, or somebody cleared the entry.</summary>
    NotFound,

    /// <summary>
    /// Something IS stored and cannot be used: a shape this build does not know,
    /// or a payload that will not parse.
    ///
    /// 🔴 NOT THE SAME AS NOTHING BEING THERE, AND THE DIFFERENCE IS THE
    /// GENERATION. A newer Studio writes format 2 at generation 50; this build
    /// reads it, understands nothing, and if that counted as «no record» it
    /// would republish at generation 1. Every plugin holding 50 would then read
    /// 1 as «nothing has happened since» - a counter going backwards, which is
    /// the one thing the generation promises never to do.
    /// </summary>
    Unreadable,

    /// <summary>
    /// The store itself could not be reached. Nothing is known about what it
    /// holds, so nothing may be concluded about it - and a write is very
    /// unlikely to fare better.
    /// </summary>
    StoreUnavailable,
}

/// <summary>
/// What a read found, and the highest generation it can prove was published.
/// The floor exists so an unusable record still moves the counter forward.
/// </summary>
internal sealed record StudioSsoIdentityReadResult(
    StudioSsoIdentityReadOutcome Outcome,
    StudioSsoIdentityRecord? Record,
    long GenerationFloor);

/// <summary>
/// Where the resting identity lives, and the only place that name is written.
///
/// Windows Credential Manager, under the signed-in Windows user. The identity
/// belongs to the PERSON AT the machine, not to the machine: a plugin process
/// runs as that Windows user and can read it, and a different Windows user on
/// the same PC cannot. That also happens to agree with what the licence server
/// already believes - the device fingerprint is built from the machine and the
/// user together, so two Windows accounts on one PC are already two devices.
///
/// 🔴 A FILE WOULD HAVE BEEN EASIER AND WORSE. The per-product licence files
/// this replaces are plain JSON guarded by nothing but a folder ACL. That was
/// already thin for one product; this record is the identity for five at once,
/// so the floor is the OS-guarded store rather than a path somebody can copy.
/// </summary>
internal static class StudioSsoIdentityStore
{
    /// <summary>
    /// The credential target every Erk-S product reads. Part of the published
    /// contract - changing it is a breaking change for four other repositories,
    /// which is why it is a constant here and not a string assembled at a call
    /// site.
    /// </summary>
    public const string CredentialTarget = "Erk-S Platform/SSO/Device Identity";

    /// <summary>
    /// Shown beside the entry in the Credential Manager UI. People do look, and
    /// an unexplained entry invites deletion.
    /// </summary>
    public const string CredentialUserName = "Erk-S Studio";

    public static StudioSsoIdentityReadResult Read(ICredentialStore store)
    {
        ArgumentNullException.ThrowIfNull(store);

        StudioSsoIdentityRecord? record;
        try
        {
            record = store.Read<StudioSsoIdentityRecord>(CredentialTarget);
        }
        catch (Exception)
        {
            // The store could not be reached. Reporting this as «nothing is
            // stored» would let the caller overwrite whatever is actually there
            // - and it cannot even know whether there is anything to overwrite.
            return new StudioSsoIdentityReadResult(
                StudioSsoIdentityReadOutcome.StoreUnavailable,
                null,
                0);
        }

        if (record is null)
        {
            return new StudioSsoIdentityReadResult(
                StudioSsoIdentityReadOutcome.NotFound,
                null,
                0);
        }

        // A record from a shape this build does not know is not a record.
        // Reading it as one would apply today's meaning to yesterday's - or
        // tomorrow's - fields, which is the quiet half of every format break.
        // Out of range means «republish», not «trust it anyway» - but the
        // generation is the one field whose meaning is fixed across versions,
        // so it is kept as a floor.
        if (record.FormatVersion < StudioSsoIdentityRecord.OldestSupportedFormatVersion ||
            record.FormatVersion > StudioSsoIdentityRecord.CurrentFormatVersion)
        {
            return new StudioSsoIdentityReadResult(
                StudioSsoIdentityReadOutcome.Unreadable,
                null,
                Math.Max(0, record.Generation));
        }

        record.Device ??= new StudioSsoDeviceFingerprints();
        return new StudioSsoIdentityReadResult(
            StudioSsoIdentityReadOutcome.Found,
            record,
            Math.Max(0, record.Generation));
    }

    /// <summary>
    /// Writes the record, and says whether it managed to.
    ///
    /// The answer is returned rather than thrown because the caller is a UI
    /// refresh: it must finish either way, and a device that could not publish
    /// its identity has a real consequence to report - the plugins will refuse -
    /// rather than an exception to swallow.
    /// </summary>
    public static bool Write(ICredentialStore store, StudioSsoIdentityRecord record)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(record);
        try
        {
            store.Write(CredentialTarget, CredentialUserName, record);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
