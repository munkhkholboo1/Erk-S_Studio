namespace ErkS.Studio;

/// <summary>
/// Why the album was drawn, or why it was not.
///
/// 🔴 THE REASON IS PART OF THE DECISION, NOT A SECOND OPINION ABOUT IT. A
/// screen that worked out its own explanation would drift from the rule the
/// moment either changed, and the drift would be invisible: both sentences would
/// still sound right.
/// </summary>
internal enum AlbumRebuildReason
{
    /// <summary>A package arrived, or a sync ran. The owner's two triggers.</summary>
    OriginAlwaysDraws,

    /// <summary>The file the record names is not on disk.</summary>
    BuiltAlbumMissing,

    /// <summary>The project could not be read to fingerprint it.</summary>
    FingerprintUnreadable,

    /// <summary>Nobody recorded what this album was made of.</summary>
    FingerprintUnknown,

    /// <summary>The work moved on.</summary>
    FingerprintChanged,

    /// <summary>The one reason NOT to draw.</summary>
    NothingChanged,
}

/// <summary>What was decided and why, together.</summary>
internal sealed record AlbumRebuildDecision(bool MustDraw, AlbumRebuildReason Reason);

internal static class StudioAlbumRebuildPolicy
{
    public static bool AlwaysDraws(StudioWorkspaceOperation origin) =>
        origin is StudioWorkspaceOperation.SourceRefresh or StudioWorkspaceOperation.CloudSync;

    /// <summary>
    /// Whether to draw, and why.
    ///
    /// 🔴 «IT DID NOT REBUILD» IS A CLAIM ABOUT WORK THAT DID NOT HAPPEN, AND
    /// NOTHING CURRENTLY RECORDS IT. The product's answer to «was the album
    /// redrawn?» is a stopwatch - fast means no, slow means yes - which is a
    /// guess that gets worse on a faster machine and on a smaller album. The
    /// reason lives here, beside the decision, so it can be written down.
    /// </summary>
    public static AlbumRebuildDecision Decide(
        StudioWorkspaceOperation origin,
        string? currentFingerprint,
        string? builtFingerprint,
        bool builtAlbumIsPresent)
    {
        if (AlwaysDraws(origin))
            return new AlbumRebuildDecision(true, AlbumRebuildReason.OriginAlwaysDraws);
        if (!builtAlbumIsPresent)
            return new AlbumRebuildDecision(true, AlbumRebuildReason.BuiltAlbumMissing);

        string now = (currentFingerprint ?? "").Trim();
        string built = (builtFingerprint ?? "").Trim();

        // An unknown answer draws. Either value being empty means nobody has
        // recorded what this album was made of - an older project file, or a
        // fingerprint that could not be computed - and «I do not know» must not
        // be read as «nothing changed». That mistake shows a person an album that
        // no longer matches their work, which is worse than the delay this whole
        // rule exists to remove.
        if (now.Length == 0 || built.Length == 0)
            return new AlbumRebuildDecision(true, AlbumRebuildReason.FingerprintUnknown);

        return now.Equals(built, StringComparison.OrdinalIgnoreCase)
            ? new AlbumRebuildDecision(false, AlbumRebuildReason.NothingChanged)
            : new AlbumRebuildDecision(true, AlbumRebuildReason.FingerprintChanged);
    }

    public static bool MustDraw(
        StudioWorkspaceOperation origin,
        string? currentFingerprint,
        string? builtFingerprint,
        bool builtAlbumIsPresent) =>
        Decide(origin, currentFingerprint, builtFingerprint, builtAlbumIsPresent).MustDraw;

    /// <summary>
    /// The reason in the owner's language, for the one place they will read it:
    /// when they say «this was slow» and somebody has to answer «it drew, and
    /// here is why» without a stopwatch.
    /// </summary>
    public static string DescribeMn(AlbumRebuildReason reason) => reason switch
    {
        AlbumRebuildReason.OriginAlwaysDraws =>
            "эх үүсвэр ирсэн эсвэл синк хийгдсэн тул",
        AlbumRebuildReason.BuiltAlbumMissing =>
            "баригдсан файл олдсонгүй тул",
        AlbumRebuildReason.FingerprintUnreadable =>
            "төслийг уншиж чадаагүй тул",
        AlbumRebuildReason.FingerprintUnknown =>
            "энэ альбом юунаас бүтснийг бүртгээгүй тул",
        AlbumRebuildReason.FingerprintChanged =>
            "төсөл өөрчлөгдсөн тул",
        AlbumRebuildReason.NothingChanged =>
            "юу ч өөрчлөгдөөгүй тул",

        // 🔴 NOT A DEFAULT. A reason with no sentence would print as a blank
        // where the explanation goes - which reads as «no reason», the one thing
        // this whole record exists to rule out.
        _ => throw new ArgumentOutOfRangeException(
            nameof(reason),
            reason,
            "Энэ шалтгааны тайлбар бичигдээгүй байна."),
    };
}
