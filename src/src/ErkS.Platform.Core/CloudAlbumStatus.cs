namespace ErkS.Platform.Core;

/// <summary>
/// What the album screen says about the shared album, in one glance: is there
/// anything to do, and whose is it?
///
/// 🔴 GREEN IS A CLAIM ABOUT THE CLOUD, SO IT MAY ONLY BE SHOWN AFTER THE CLOUD
/// ANSWERED. That is the whole reason this type exists rather than a colour
/// picked at the call site. "All merged" and "I could not ask" produce the same
/// LOCAL picture - no pending components, no newer revision recorded - and a
/// screen that renders them alike tells a person their work is shared when it
/// may only be sitting on their disk. The cloud's side is therefore never
/// inferred from the absence of evidence: it arrives as an explicit
/// <see cref="CloudProbeOutcome"/>, and anything short of
/// <see cref="CloudProbeOutcome.Answered"/> lands in
/// <see cref="CloudAlbumChangeState.Unknown"/> no matter how tidy the local
/// numbers look.
///
/// WHY THE OWN COUNT STILL SHOWS WHILE THE CLOUD IS UNKNOWN. The two halves are
/// independently knowable: what this device has not sent is a local fact and is
/// true whether or not the network answered. Hiding it behind the grey state
/// would throw away a fact we hold, so grey carries the own-side number with it
/// and says plainly that the cloud's side was not learned. Grey therefore means
/// exactly one thing - "the cloud's side is not known" - which is what keeps it
/// from ever being read as green.
///
/// WHY COLOUR IS NEVER THE ONLY CHANNEL. Every state carries a glyph and its
/// numbers, so the screen is readable without colour vision. Colour is the
/// redundant channel here, not the carrier.
/// </summary>
public enum CloudAlbumChangeState
{
    /// <summary>The project is not linked to a cloud project; there is nothing to indicate.</summary>
    NotLinked,

    /// <summary>The cloud's side was not learned - never asked, asking failed, or the server cannot answer.</summary>
    Unknown,

    /// <summary>The cloud answered and both sides hold the same work.</summary>
    Merged,

    /// <summary>This device has work the cloud has not been given.</summary>
    OwnWaiting,

    /// <summary>The cloud holds work this device has not taken in.</summary>
    OthersWaiting,

    /// <summary>Both, which is the ordinary state of a live project.</summary>
    BothWaiting,
}

/// <summary>
/// Whether the cheap cloud check actually produced an answer.
///
/// 🔴 THIS IS NOT A BOOLEAN AND MUST NOT BECOME ONE. "Not attempted" and
/// "failed" are different things to say to a person - one is "we have not looked
/// yet", the other is "we looked and could not see" - and collapsing them costs
/// the report its only honest sentence about what went wrong.
/// </summary>
public enum CloudProbeOutcome
{
    /// <summary>No check has run yet on this project - a freshly opened project, before any refresh.</summary>
    NotAttempted,

    /// <summary>A check ran and did not produce an answer: offline, refused, or the server lacks the capability.</summary>
    Failed,

    /// <summary>The cloud's side is known.</summary>
    Answered,
}

/// <summary>
/// The indicator's state and the words that go with it.
/// </summary>
public sealed record CloudAlbumStatus
{
    private CloudAlbumStatus(
        CloudAlbumChangeState state,
        int ownWaitingCount,
        int othersWaitingCount,
        int blockedCount,
        CloudProbeOutcome probe)
    {
        State = state;
        OwnWaitingCount = ownWaitingCount;
        OthersWaitingCount = othersWaitingCount;
        BlockedCount = blockedCount;
        Probe = probe;
    }

    public CloudAlbumChangeState State { get; }

    /// <summary>
    /// Components this device holds that the cloud has not been given AND THAT
    /// PRESSING THE BUTTON WOULD ACTUALLY SEND.
    ///
    /// 🔴 THE DISTINCTION THIS NUMBER EXISTS TO MAKE. A person pressed sync
    /// repeatedly against a yellow "3" and watched nothing happen, because all
    /// three components were ones this device cannot produce - a building
    /// sub-cover it cannot render, sources whose custodian is another machine.
    /// The indicator was saying "you have three things to send"; the truth was
    /// "there are three things nobody here can send". Yellow has to mean "press
    /// this and it resolves", or it teaches people that the button is broken.
    /// </summary>
    public int OwnWaitingCount { get; }

    /// <summary>
    /// Whether the cloud holds a revision this device has not taken in.
    ///
    /// This is a COUNT OF ALBUM REVISIONS BEHIND rather than of components,
    /// because the cloud publishes a canonical revision, not a per-contributor
    /// list. It is 0 whenever <see cref="Probe"/> is not
    /// <see cref="CloudProbeOutcome.Answered"/>, and that zero means "not
    /// known", never "none" - which is why nothing may read this number without
    /// first reading <see cref="State"/>.
    /// </summary>
    public int OthersWaitingCount { get; }

    /// <summary>
    /// Components waiting that THIS device cannot produce - so no amount of
    /// pressing will move them. Reported, never counted into the colour.
    ///
    /// They are not an error: another member's machine holds what is needed,
    /// and the work is waiting on them rather than on this person. Saying so is
    /// the difference between "the button does nothing" and "this one is not
    /// yours to do".
    /// </summary>
    public int BlockedCount { get; }

    public CloudProbeOutcome Probe { get; }

    /// <summary>Whether the indicator belongs on screen at all.</summary>
    public bool ShouldShow => State != CloudAlbumChangeState.NotLinked;

    /// <summary>
    /// The shape shown beside the colour. Deliberately distinct per state, so
    /// the states remain separable with no colour vision at all.
    /// </summary>
    public string Glyph => State switch
    {
        CloudAlbumChangeState.NotLinked => "",
        CloudAlbumChangeState.Unknown => "?",
        CloudAlbumChangeState.Merged => "●",
        CloudAlbumChangeState.OwnWaiting => "▲",
        CloudAlbumChangeState.OthersWaiting => "▼",
        CloudAlbumChangeState.BothWaiting => "◆",
        _ => "?",
    };

    /// <summary>
    /// The numbers beside the glyph, e.g. "▲2" or "◆2/1". Empty when there is
    /// no number worth showing, so the glyph stands alone.
    /// </summary>
    public string Badge => State switch
    {
        CloudAlbumChangeState.NotLinked => "",
        CloudAlbumChangeState.OwnWaiting => Glyph + OwnWaitingCount.ToString(),
        CloudAlbumChangeState.OthersWaiting => Glyph + OthersWaitingCount.ToString(),
        CloudAlbumChangeState.BothWaiting =>
            Glyph + OwnWaitingCount.ToString() + "/" + OthersWaitingCount.ToString(),
        // Grey keeps the one number it actually knows.
        CloudAlbumChangeState.Unknown when OwnWaitingCount > 0 =>
            Glyph + " ▲" + OwnWaitingCount.ToString(),
        _ => Glyph,
    };

    /// <summary>
    /// One line for the person. The unknown state names its own cause, because
    /// "the cloud's side is not known" is not actionable until you know whether
    /// nobody has looked or looking failed.
    /// </summary>
    public string SummaryMn => State switch
    {
        CloudAlbumChangeState.NotLinked => "",
        CloudAlbumChangeState.Merged => "Үүлэн альбомтай ижил.",
        CloudAlbumChangeState.OwnWaiting =>
            $"Таны {OwnWaitingCount} хэсэг үүл рүү өгөгдөөгүй байна.",
        CloudAlbumChangeState.OthersWaiting =>
            "Үүлэнд шинэ хувилбар байна; энэ төхөөрөмж аваагүй байна.",
        CloudAlbumChangeState.BothWaiting =>
            $"Таны {OwnWaitingCount} хэсэг өгөгдөөгүй, үүлэнд шинэ хувилбар байна.",
        CloudAlbumChangeState.Unknown => UnknownSummaryMn,
        _ => "",
    } + BlockedSuffixMn;

    /// <summary>
    /// Names the components this device cannot produce, appended to whatever
    /// else the status says. Always shown when there are any, in every state -
    /// a person staring at a green cloud while a page is missing from the album
    /// needs this sentence most of all.
    /// </summary>
    private string BlockedSuffixMn =>
        BlockedCount > 0 && State != CloudAlbumChangeState.NotLinked
            ? $" Өөр {BlockedCount} хэсгийг энэ төхөөрөмж дээр бэлдэх боломжгүй " +
              "тул хүлээгдэж байна — тэдгээрийг эзэмшигч нь өөрийн компьютерээсээ илгээнэ."
            : "";

    private string UnknownSummaryMn
    {
        get
        {
            string cause = Probe == CloudProbeOutcome.NotAttempted
                ? "Үүлэн талыг хараахан шалгаагүй байна."
                : "Үүлэнтэй холбогдож чадсангүй; үүлэн талыг мэдэхгүй.";
            return OwnWaitingCount > 0
                ? cause + $" Таны {OwnWaitingCount} хэсэг өгөгдөөгүй байгаа нь мэдэгдэж байна."
                : cause;
        }
    }

    /// <summary>
    /// Builds the status from what is actually known.
    /// </summary>
    /// <param name="isLinkedToCloud">Whether this project has a cloud project behind it.</param>
    /// <param name="ownWaitingCount">Components held here and not yet given to the cloud.</param>
    /// <param name="probe">Whether the cheap cloud check produced an answer.</param>
    /// <param name="cloudIsAhead">
    /// Whether the cloud holds a revision this device has not taken in. Read
    /// ONLY when <paramref name="probe"/> answered - a caller that passes a
    /// stale value with a failed probe cannot turn the state green, by
    /// construction.
    /// </param>
    /// <param name="blockedCount">
    /// Components waiting that this device cannot produce. Reported separately
    /// and never counted into the colour - see <see cref="BlockedCount"/>.
    /// </param>
    public static CloudAlbumStatus Evaluate(
        bool isLinkedToCloud,
        int ownWaitingCount,
        CloudProbeOutcome probe,
        bool cloudIsAhead,
        int blockedCount = 0)
    {
        int own = ownWaitingCount < 0 ? 0 : ownWaitingCount;
        int blocked = blockedCount < 0 ? 0 : blockedCount;

        if (!isLinkedToCloud)
            return new CloudAlbumStatus(CloudAlbumChangeState.NotLinked, 0, 0, 0, probe);

        // 🔴 THE GATE. Everything below this line may claim something about the
        // cloud; nothing above it may. An unanswered probe cannot reach any
        // state that asserts the cloud's contents - including the green one -
        // and no arrangement of the local numbers gets past it.
        if (probe != CloudProbeOutcome.Answered)
            return new CloudAlbumStatus(CloudAlbumChangeState.Unknown, own, 0, blocked, probe);

        int others = cloudIsAhead ? 1 : 0;
        CloudAlbumChangeState state = (own > 0, cloudIsAhead) switch
        {
            (true, true) => CloudAlbumChangeState.BothWaiting,
            (true, false) => CloudAlbumChangeState.OwnWaiting,
            (false, true) => CloudAlbumChangeState.OthersWaiting,
            (false, false) => CloudAlbumChangeState.Merged,
        };

        return new CloudAlbumStatus(state, own, others, blocked, probe);
    }
}
