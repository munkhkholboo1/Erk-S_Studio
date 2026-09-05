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
/// party - and the seat's display name is not reachable from here for everyone:
/// listing seats needs organisation-management rights, so a plain project
/// member, and a seated machine itself, cannot resolve one. Until SRV publishes
/// a name that every reader of a source can obtain, the honest line names the
/// KIND of owner rather than inventing an identity for it.
/// </summary>
internal static class StudioSourceOwnerLabel
{
    /// <summary>Shown when a seat owns the source and its name is not reachable.</summary>
    public const string UnnamedSeat = "Байгууллагын бот суудал";

    /// <summary>Shown when the owner's kind comes from a newer server than this build.</summary>
    public const string UnreadableKind = "(эзэмшигчийн төрөл танигдсангүй)";

    /// <summary>Shown when the record names no owner at all.</summary>
    public const string Nobody = "-";

    /// <param name="seatDisplayName">
    /// The seat's own name when the caller happens to hold it - the bot-seat
    /// dialog does. Empty everywhere else, and empty is not an error.
    /// </param>
    public static string Describe(
        string? sourceOwnerKind,
        string? sourceOwnerRef,
        string? registeredBy,
        string? ownerEmail = null,
        string? seatDisplayName = null)
    {
        ProjectSourceOwner owner = ProjectSourceOwnership.Of(
            sourceOwnerKind,
            sourceOwnerRef,
            registeredBy,
            ownerEmail);

        if (owner.IsUnknownKind)
            return UnreadableKind;
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
                seatDisplayName);
}
