namespace ErkS.Studio;

/// <summary>Which identity a Studio session is acting as.</summary>
public enum StudioSessionKind
{
    /// <summary>A person signed in with their own account.</summary>
    Personal,

    /// <summary>A machine acting as an organisation's bot seat.</summary>
    BotSeat,
}

/// <summary>
/// What a session may do in a project, given the session's KIND.
///
/// THE RULE, decided by the user and fixed by the platform: a bot session gets
/// the seat's assignment and nothing else; a personal session gets the person's
/// own participation and nothing else. No union, no maximum, no fallback.
///
/// "If the seat does not grant it, fall back to the person's own rights" is the
/// sentence this class exists to make impossible. It sounds helpful and it is a
/// hole: the whole point of a seat is that the machine acts with the seat's
/// authority, so a person who is a company admin in their own right must NOT
/// get admin powers on a machine that was handed to them with a draughtsman's
/// seat. The two sources are mutually exclusive, not ranked.
///
/// A consequence worth stating because it looks like a bug: on a seated machine
/// somebody may be able to do LESS than they could on the web with their own
/// account. That difference is the design working, not something to soften with
/// a fallback.
///
/// A SEAT SCOPE IS A CLAIM, NOT A GUARANTEE. The server publishes what a seat
/// may do; whether every endpoint behind it honours a seat rather than a person
/// is a separate question, and in September 2026 it did not - the flags said
/// "you may write sources" while the routes still asked what the PERSON could
/// do. So a granted scope can still be refused, and the refusal is the server's
/// to explain. Do not compensate here by widening what a seat is given.
///
/// Kept out of the shell deliberately. Three rules of exactly this shape were
/// wrong this week while living inside methods that build WPF controls, where
/// nothing could measure them.
/// </summary>
public static class StudioEffectiveAuthority
{
    /// <summary>
    /// Every scope the server is known to issue for a project, from its own
    /// scope builder. Listed so that a scope which is neither allowed nor
    /// excluded below fails a test rather than quietly reaching a seat.
    /// </summary>
    public static IReadOnlyList<string> KnownProjectScopes { get; } =
    [
        "project.read",
        "project.delete",
        "project.leave",
        "team.manage",
        "project.metadata.write",
        "concept.write",
        "source.write",
        "album.create",
        "album.submit",
        "approval.act",
    ];

    /// <summary>
    /// What a SEAT may hold. An allow-list, not a deny-list, and the direction
    /// matters: a deny-list lets a scope added later flow to seats by default -
    /// failing open - while a forgotten entry here fails closed, which is the
    /// side to be wrong on.
    ///
    /// The seat's reach is the PROJECT it is assigned to. Deleting or leaving a
    /// project are not things a machine does on anyone's behalf: leaving is a
    /// person's own act, and deleting is the owner's. They are excluded here as
    /// well as at the server, so neither side alone is load-bearing.
    /// </summary>
    public static IReadOnlySet<string> SeatAllowedScopes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "project.read",
            "team.manage",
            "project.metadata.write",
            "concept.write",
            "source.write",
            "album.create",
            "album.submit",
            "approval.act",
        };

    /// <summary>Scopes that must never reach a seat, named rather than implied.</summary>
    public static IReadOnlySet<string> SeatExcludedScopes { get; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "project.delete",
            "project.leave",
        };

    /// <summary>
    /// The scopes in force for one project.
    ///
    /// A null source means "not known yet" and yields nothing - never the other
    /// source. Unknown must not be answered with somebody else's rights.
    /// </summary>
    public static IReadOnlySet<string> ScopesFor(
        StudioSessionKind sessionKind,
        IReadOnlyCollection<string>? personalScopes,
        IReadOnlyCollection<string>? seatScopes)
    {
        IReadOnlyCollection<string>? chosen = sessionKind == StudioSessionKind.BotSeat
            ? seatScopes
            : personalScopes;
        if (chosen is null)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        IEnumerable<string> usable = chosen
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim());
        if (sessionKind == StudioSessionKind.BotSeat)
            usable = usable.Where(SeatAllowedScopes.Contains);

        return new HashSet<string>(usable, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether one scope is in force. Same rule, asked one at a time.</summary>
    public static bool Allows(
        StudioSessionKind sessionKind,
        IReadOnlyCollection<string>? personalScopes,
        IReadOnlyCollection<string>? seatScopes,
        string scope) =>
        !string.IsNullOrWhiteSpace(scope) &&
        ScopesFor(sessionKind, personalScopes, seatScopes).Contains(scope.Trim());
}
