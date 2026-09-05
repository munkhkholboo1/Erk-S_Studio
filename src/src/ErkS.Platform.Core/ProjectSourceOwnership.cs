namespace ErkS.Platform.Core;

/// <summary>
/// The KIND of party that owns a source stream, as the server states it.
///
/// Three states, not two, and the third is the point. SRV publishes
/// sourceOwnerKind as "Bot", "Person" or empty, where EMPTY means the row was
/// written before 2026-09-05 and the question had not been asked yet - the
/// server resolves those from registeredBy and never rewrites them. So absent
/// is person-owned, by decision, on both sides.
///
/// A value that is neither empty nor one of the two known words is a different
/// thing entirely: a kind from a newer server that this build has never heard
/// of. Answering that with "person" is the fallback this codebase keeps paying
/// for - it turns "I do not know" into a confident wrong answer, and here the
/// wrong answer hands a seat's work to whichever person the row happens to
/// name. Unknown stays unknown and is refused.
/// </summary>
public static class ProjectSourceOwnerKinds
{
    /// <summary>An organisation's bot seat. The owner is the SEAT, not whoever fills it.</summary>
    public const string Bot = "Bot";

    /// <summary>A person, named by account email.</summary>
    public const string Person = "Person";

    /// <summary>A kind this build does not recognise. Never treated as either of the above.</summary>
    public const string Unknown = "Unknown";

    /// <summary>
    /// Reads the wire value. Empty is Person by SRV's stated rule; an
    /// unrecognised word is Unknown, never Person.
    /// </summary>
    public static string Recognize(string? value)
    {
        string text = (value ?? "").Trim();
        if (text.Length == 0)
            return Person;
        if (text.Equals(Bot, StringComparison.OrdinalIgnoreCase))
            return Bot;
        return text.Equals(Person, StringComparison.OrdinalIgnoreCase)
            ? Person
            : Unknown;
    }
}

/// <summary>
/// Who owns one source stream: a kind and a reference, kept apart.
///
/// Two fields rather than one for the reason SRV gave when they chose the same
/// shape: a single field meaning different things depending on its shape is a
/// field every reader has to guess about, and "bot_7f3a..." sitting in an email
/// column is exactly that.
/// </summary>
/// <param name="Kind">One of <see cref="ProjectSourceOwnerKinds"/>.</param>
/// <param name="Reference">A botId when Bot, an account email when Person, empty when neither is known.</param>
public sealed record ProjectSourceOwner(string Kind, string Reference)
{
    /// <summary>Nothing is known about this source at all.</summary>
    public static ProjectSourceOwner None { get; } =
        new(ProjectSourceOwnerKinds.Person, "");

    public bool IsBotOwned => Kind.Equals(ProjectSourceOwnerKinds.Bot, StringComparison.Ordinal);

    public bool IsPersonOwned => Kind.Equals(ProjectSourceOwnerKinds.Person, StringComparison.Ordinal);

    /// <summary>A kind this build cannot interpret. Callers must refuse rather than guess.</summary>
    public bool IsUnknownKind => Kind.Equals(ProjectSourceOwnerKinds.Unknown, StringComparison.Ordinal);

    /// <summary>
    /// The person who controls this source, or empty when no person does.
    ///
    /// THE RULE THIS PROPERTY EXISTS FOR: a bot-owned source resolves to empty
    /// and NEVER to an email - not through registeredBy, not through a locally
    /// stored owner, not through any fallback. A seat's work does not become
    /// somebody's personal work because a person-shaped field was reachable.
    /// </summary>
    public string ControllingPersonEmail =>
        IsPersonOwned ? Reference : "";
}

/// <summary>
/// The one place that answers "who owns this source".
///
/// It exists because the answer used to be assembled at seven call sites out of
/// raw fields - registeredBy, then custodianEmail, then ownerEmail, then the
/// email stored in the local file - each site walking the chain slightly
/// differently. That chain was correct only while every source belonged to a
/// person. From 2026-09-05 a seat can own one, and SRV empties registeredBy and
/// custodianEmail on those rows deliberately, so that every chain of that shape
/// now ends at "" - which those sites read as "nobody owns it" and let through.
/// </summary>
public static class ProjectSourceOwnership
{
    public static ProjectSourceOwner Of(ProjectCloudSourceReference? source) =>
        source is null
            ? ProjectSourceOwner.None
            : Of(
                source.SourceOwnerKind,
                source.SourceOwnerRef,
                source.RegisteredBy,
                source.OwnerEmail);

    /// <summary>
    /// The same rule asked of raw values, so the wire DTO and the project
    /// mirror cannot answer it differently. Two records of one source
    /// disagreeing about its owner is the shape that hides defects longest.
    /// </summary>
    public static ProjectSourceOwner Of(
        string? sourceOwnerKind,
        string? sourceOwnerRef,
        string? registeredBy,
        string? ownerEmail)
    {
        string kind = ProjectSourceOwnerKinds.Recognize(sourceOwnerKind);
        if (kind.Equals(ProjectSourceOwnerKinds.Unknown, StringComparison.Ordinal))
            return new ProjectSourceOwner(kind, (sourceOwnerRef ?? "").Trim());

        if (kind.Equals(ProjectSourceOwnerKinds.Bot, StringComparison.Ordinal))
            return new ProjectSourceOwner(kind, (sourceOwnerRef ?? "").Trim());

        // Person. The explicit reference wins; a row written before the field
        // existed is resolved from registeredBy, which is what the server does
        // with the same row and must not diverge from.
        string reference = NormalizeEmail(sourceOwnerRef);
        if (reference.Length == 0)
            reference = NormalizeEmail(registeredBy);
        if (reference.Length == 0)
            reference = NormalizeEmail(ownerEmail);
        return new ProjectSourceOwner(kind, reference);
    }

    /// <summary>
    /// Who may operate the source right now: custody when a participant holds
    /// it, otherwise the owner.
    ///
    /// Custody is a PERSONAL fact - it names a project participant - and SRV
    /// left AssignSourceCustodian personal on purpose, so a bot-owned row
    /// carries no custodian and custody must not be invented for one. Asking
    /// this of a bot-owned source yields empty, and the caller is expected to
    /// have asked <see cref="Of"/> first.
    /// </summary>
    public static string ControllingPersonEmail(ProjectCloudSourceReference? source)
    {
        if (source is null)
            return "";
        ProjectSourceOwner owner = Of(source);
        if (!owner.IsPersonOwned)
            return "";
        string custodian = NormalizeEmail(source.CustodianEmail);
        return custodian.Length > 0 ? custodian : owner.ControllingPersonEmail;
    }

    /// <summary>
    /// A stable key for "the same owner", safe to group by.
    ///
    /// Grouping by registeredBy alone collapsed every bot-owned row into one
    /// bucket once that field went empty, and the grouping kept a single
    /// survivor per bucket - so two seats owning the same source key lost one
    /// of the two, with nothing said.
    /// </summary>
    public static string OwnerKey(ProjectCloudSourceReference? source) =>
        KeyOf(Of(source));

    /// <summary>The same key from raw values, for records that are not the project mirror.</summary>
    public static string OwnerKey(
        string? sourceOwnerKind,
        string? sourceOwnerRef,
        string? registeredBy,
        string? ownerEmail) =>
        KeyOf(Of(sourceOwnerKind, sourceOwnerRef, registeredBy, ownerEmail));

    private static string KeyOf(ProjectSourceOwner owner) =>
        owner.Kind + ":" + owner.Reference.ToLowerInvariant();

    private static string NormalizeEmail(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
