namespace ErkS.Studio;

/// <summary>
/// What the server said about its rules - INCLUDING the case where it said
/// nothing.
///
/// 🔴 A LIST CANNOT CARRY THIS ANSWER, AND IT USED TO TRY. GetServerRulesAsync
/// returned an empty list both when the server stated that it has no rules and
/// when the request never got one - a refusal, a dropped network, a body this
/// build cannot parse. Two different worlds arriving as one value:
///
///     «I have no rules for you»        →  []
///     «I could not tell you my rules»  →  []
///
/// On a platform whose standing principle is «keep the rules on the server»,
/// that collapse is the one way to switch the principle off silently: the
/// client falls back to its own defaults and nobody - not the person, not
/// whoever helps them - can tell which of the two happened, or that anything
/// happened at all.
///
/// 🔴 TODAY'S ONLY CONSUMER HANDLES IT CORRECTLY, WHICH IS WHY THE BEHAVIOUR IS
/// NOT CHANGED HERE. It keeps its default and asks again on the next visit, and
/// its comment names both causes. The hazard is the NEXT consumer, and the
/// difference between the two kinds of rule is the whole reason this type
/// exists:
///
///     a rule that DISPLAYS   may fall back to a default - a window is a window
///     a rule that RESTRICTS  may NOT: «no rule» would read as «allowed», and a
///                            limit the server could not state becomes a limit
///                            that is not enforced
///
/// So the rules cannot be reached without first saying which world this is.
/// <see cref="Stated"/> throws when the server did not answer - loudly, on the
/// first run, rather than quietly permitting whatever the missing rule forbade.
/// </summary>
internal sealed class StudioServerRuleAnswer
{
    private readonly IReadOnlyList<StudioServerRule> rules;

    private StudioServerRuleAnswer(
        bool serverAnswered,
        IReadOnlyList<StudioServerRule> rules,
        string whyNot)
    {
        ServerAnswered = serverAnswered;
        this.rules = rules;
        WhyNot = whyNot;
    }

    /// <summary>
    /// Whether the server answered at all. Must be asked before the rules can
    /// be read.
    /// </summary>
    public bool ServerAnswered { get; }

    /// <summary>
    /// Why there is no answer, for the person and for the record. Empty when
    /// the server answered.
    /// </summary>
    public string WhyNot { get; }

    /// <summary>
    /// The rules the server stated - named StatedRules rather than Stated because
    /// a one-word name collides with StudioCompanionServerAnswer.Stated, and a
    /// rule that scans source for its own readers then finds the wrong file. A
    /// name a search cannot tell apart is a name that hides its callers.
    ///
    /// MAY BE EMPTY - that is an answer, and a different one from silence.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The server did not answer. Reading rules out of silence is the defect
    /// this type exists to make impossible, so it is refused rather than
    /// answered with an empty list.
    /// </exception>
    public IReadOnlyList<StudioServerRule> StatedRules => ServerAnswered
        ? rules
        : throw new InvalidOperationException(
            "Сервер дүрмээ хэлээгүй тул дүрэм уншиж болохгүй: " + WhyNot);

    /// <summary>The server answered. The list may be empty and that is its answer.</summary>
    public static StudioServerRuleAnswer FromServer(IReadOnlyList<StudioServerRule>? stated) =>
        new(true, stated ?? [], "");

    /// <summary>
    /// The server did not answer, and this is why.
    ///
    /// The reason travels with the answer because it is what the person is
    /// shown and what the boundary record keeps - a silence nobody can explain
    /// is the thing this whole mechanism was built after.
    /// </summary>
    public static StudioServerRuleAnswer Unanswered(string why) =>
        new(false, [], (why ?? "").Trim());
}
