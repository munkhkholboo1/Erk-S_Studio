namespace ErkS.Studio;

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

    public static StudioSsoIdentityRecord? Read(ICredentialStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        try
        {
            StudioSsoIdentityRecord? record =
                store.Read<StudioSsoIdentityRecord>(CredentialTarget);
            if (record is null)
                return null;

            // A record from a shape this build does not know is not a record.
            // Reading it as one would apply today's meaning to yesterday's - or
            // tomorrow's - fields, which is the quiet half of every format
            // break. Out of range means "republish", not "trust it anyway".
            if (record.FormatVersion < StudioSsoIdentityRecord.OldestSupportedFormatVersion ||
                record.FormatVersion > StudioSsoIdentityRecord.CurrentFormatVersion)
            {
                return null;
            }

            record.Device ??= new StudioSsoDeviceFingerprints();
            return record;
        }
        catch (Exception)
        {
            // A store that cannot be read is the same situation as an empty one
            // for every decision above this: the record gets rebuilt. Letting it
            // escape would take down the account UI, which is the one screen a
            // person needs when their identity is in doubt.
            return null;
        }
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
