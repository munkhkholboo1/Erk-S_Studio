namespace ErkS.Studio;

/// <summary>
/// When the album is drawn again, and when it is left alone.
///
/// 🔴 THE PRODUCT REDREW AN ALBUM THAT HAD NOT CHANGED, UNTIL IT WAS UNUSABLE. A
/// timer fired 1.5 seconds after any activity and rebuilt everything; opening the
/// album page rebuilt it again; and the finished album already on disk was never
/// consulted. On the owner's project - 26 renders - that was ten minutes of CPU
/// and 9.7 GB of memory to produce a file that already existed. They wrote the
/// rule themselves, twice:
///
///   «студио руу илгээхэд альбумаа шинэчлэнэ. синк хийхэд шинэчлэнэ. ингээд л
///    болоошт»
///   «ингэтлээ гацаад байвал ХЭН Ч ХЭРЭГЛЭХГҮЙ»
///
/// The second sentence is why this is not a performance improvement: it is a
/// condition of the product being usable at all.
/// </summary>
internal static class StudioAlbumRebuildPolicy
{
    /// <summary>
    /// Whether this operation is one of the two the owner named, which draw the
    /// album without asking anything else.
    ///
    /// 🔴 DERIVED FROM THE OPERATION, NOT FROM A LIST OF CALL SITES. Twenty-six
    /// places call the album update; enumerating the ones allowed to build would
    /// be wrong the first time somebody added the twenty-seventh. The two triggers
    /// are the two the owner named - a package arriving from a plugin, and a sync
    /// - and both are already values of <see cref="StudioWorkspaceOperation"/>.
    /// </summary>
    public static bool AlwaysDraws(StudioWorkspaceOperation origin) =>
        origin is StudioWorkspaceOperation.SourceRefresh or StudioWorkspaceOperation.CloudSync;

    /// <summary>
    /// Whether the album has to be drawn at all.
    /// </summary>
    /// <param name="origin">What asked for the album.</param>
    /// <param name="currentFingerprint">What the album would be drawn from now.</param>
    /// <param name="builtFingerprint">What the album on disk was drawn from.</param>
    /// <param name="builtAlbumIsPresent">
    /// Whether the file that fingerprint describes is still there. A record
    /// pointing at a PDF somebody deleted is not an album, and treating it as one
    /// would leave a person with an empty screen and no way to refill it.
    /// </param>
    public static bool MustDraw(
        StudioWorkspaceOperation origin,
        string? currentFingerprint,
        string? builtFingerprint,
        bool builtAlbumIsPresent)
    {
        if (AlwaysDraws(origin))
            return true;
        if (!builtAlbumIsPresent)
            return true;

        string now = (currentFingerprint ?? "").Trim();
        string built = (builtFingerprint ?? "").Trim();

        // An unknown answer draws. Either value being empty means nobody has
        // recorded what this album was made of - an older project file, or a
        // fingerprint that could not be computed - and «I do not know» must not
        // be read as «nothing changed». That mistake shows a person an album that
        // no longer matches their work, which is worse than the delay this whole
        // rule exists to remove.
        if (now.Length == 0 || built.Length == 0)
            return true;

        return !now.Equals(built, StringComparison.OrdinalIgnoreCase);
    }
}
