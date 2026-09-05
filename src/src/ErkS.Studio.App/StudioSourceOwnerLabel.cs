using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// How a source's owner is WRITTEN on screen.
///
/// Separate from the rule that decides who the owner is, and separate from the
/// method that builds the panel: three rules of exactly this shape were wrong
/// this week while living inside WPF assembly code, where nothing could measure
/// them.
///
/// A botId is never shown. "bot_7f3a91c4e85b4d2f" on the owner line is the same
/// defect as an email there - a machine identifier where a reader expects a
/// party. From SRV 941f472 the record carries a resolved name for BOTH kinds,
/// on every route that returns a source, so the name is finally reachable by
/// every reader rather than only by an organisation administrator.
///
/// THE FALLBACK IS ASYMMETRIC, DELIBERATELY. When no name comes back:
///
///   a PERSON falls back to their email, because an email identifies a person
///   a SEAT falls back to naming its KIND, because its reference is a machine
///   identifier that would read as somebody's name and could not be told apart
///   from one
///
/// The two are not the same question wearing different clothes, and answering
/// them with one rule would put "bot_7f3a91c4e85b4d2f" on the owner line the
/// first time a seat was deleted.
/// </summary>
internal static class StudioSourceOwnerLabel
{
    /// <summary>Shown when a seat owns the source and its name is not reachable.</summary>
    public const string UnnamedSeat = "Байгууллагын бот суудал";

    /// <summary>Shown when the owner's kind comes from a newer server than this build.</summary>
    public const string UnreadableKind = "(эзэмшигчийн төрөл танигдсангүй)";

    /// <summary>Shown when the record names no owner at all.</summary>
    public const string Nobody = "-";

    /// <param name="sourceOwnerDisplayName">
    /// The name the SERVER resolved. Empty means it could not resolve one - a
    /// deleted seat, an unregistered email - and not that the source is
    /// unowned.
    /// </param>
    /// <param name="seatDisplayName">
    /// A seat name the caller already holds, used only when the server sent
    /// none. The bot-seat dialog holds one; everywhere else this is empty, and
    /// empty is not an error.
    /// </param>
    public static string Describe(
        string? sourceOwnerKind,
        string? sourceOwnerRef,
        string? registeredBy,
        string? ownerEmail = null,
        string? sourceOwnerDisplayName = null,
        string? seatDisplayName = null)
    {
        ProjectSourceOwner owner = ProjectSourceOwnership.Of(
            sourceOwnerKind,
            sourceOwnerRef,
            registeredBy,
            ownerEmail);

        if (owner.IsUnknownKind)
            return UnreadableKind;

        string resolved = (sourceOwnerDisplayName ?? "").Trim();
        if (resolved.Length > 0)
            return resolved;

        if (owner.IsBotOwned)
        {
            string name = (seatDisplayName ?? "").Trim();
            return name.Length > 0 ? name : UnnamedSeat;
        }

        string email = owner.ControllingPersonEmail;
        return email.Length > 0 ? email : Nobody;
    }

    public static string Describe(
        ProjectCloudSourceReference? source,
        string? seatDisplayName = null) =>
        source is null
            ? Nobody
            : Describe(
                source.SourceOwnerKind,
                source.SourceOwnerRef,
                source.RegisteredBy,
                source.OwnerEmail,
                source.SourceOwnerDisplayName,
                seatDisplayName);
}
