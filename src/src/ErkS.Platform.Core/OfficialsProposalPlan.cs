namespace ErkS.Platform.Core;

/// <summary>
/// Why a proposed official did not reach the roster.
/// </summary>
public enum OfficialsProposalSkip
{
    /// <summary>It did reach it.</summary>
    None,

    /// <summary>The roster already carries this organisation.</summary>
    AlreadyPresent,

    /// <summary>The table is full; this row has nowhere to print.</summary>
    NoRoomLeft,
}

/// <summary>
/// One proposed official and what became of it.
/// </summary>
public sealed record OfficialsProposalOutcome(
    OfficialsDirectoryEntry Entry,
    OfficialsBlock Block,
    OfficialsProposalSkip Skip)
{
    public bool WasAdded => Skip == OfficialsProposalSkip.None;
}

/// <summary>
/// What pressing «suggest from the address» will actually do, worked out BEFORE
/// anything is added.
///
/// 🔴 THE CAP AND THE PROPOSAL MEET HERE, AND THAT MEETING IS WHERE ROWS GET
/// LOST. ХЯНАСАН holds exactly two; a project that already has two and an
/// address that offers one more is an ordinary situation, and the wrong answers
/// are both silent: adding a third puts a row where the sheet cannot print it,
/// and dropping it without a word leaves somebody believing the address had
/// nothing to offer. So every proposed row comes back with a REASON, including
/// the ones that arrived.
///
/// Kept out of the screen deliberately - a rule inside a window's assembly is a
/// rule nobody can check, and this chain has already paid that bill twice.
/// </summary>
public static class OfficialsProposalPlan
{
    /// <param name="proposed">What the address offered.</param>
    /// <param name="concurredBy">The ЗӨВШИЛЦСӨН rows the project already has.</param>
    /// <param name="reviewedBy">The ХЯНАСАН rows the project already has.</param>
    public static IReadOnlyList<OfficialsProposalOutcome> For(
        IReadOnlyList<OfficialsProposalRow>? proposed,
        IReadOnlyList<ProjectApprovalEntry>? concurredBy,
        IReadOnlyList<ProjectApprovalEntry>? reviewedBy)
    {
        var outcomes = new List<OfficialsProposalOutcome>();
        if (proposed is null || proposed.Count == 0)
            return outcomes;

        // 🔴 CAPACITY IS COUNTED AS ROWS ARE ADDED, NOT ONCE AT THE START. Two
        // urban-planning officials offered to an empty ХЯНАСАН both fit; offered
        // to one that already holds one, only the first does. A single check
        // against the starting count would admit both and overfill the table.
        //
        // 🔴 AND ONLY ONE TABLE HAS A COUNT AT ALL. A first version computed room
        // for ЗӨВШИЛЦСӨН too and never consulted it - a value written and read by
        // nobody, which a mutation walked straight through: flipping it to
        // «capped» changed nothing, because nothing looked. The asymmetry is the
        // rule here, so it is written as an asymmetry rather than as a parameter
        // that quietly means nothing on one side.
        //
        // 🔴 OCCUPIED PLACES, NOT ROWS. The editor adds blank rows when somebody
        // presses «add», and counting those as occupants makes a table that looks
        // EMPTY on screen refuse a suggestion for having no room - an answer
        // nobody could make sense of while staring at two blank lines.
        int reviewedRoom = Math.Max(
            0,
            ProjectApprovalRosterLimits.MaxReviewedBy - OfficialsRosterFill.OccupiedCount(reviewedBy));

        foreach (OfficialsProposalRow row in proposed)
        {
            if (row is null)
                continue;

            if (row.AlreadyPresent)
            {
                outcomes.Add(new OfficialsProposalOutcome(
                    row.Entry,
                    row.Block,
                    OfficialsProposalSkip.AlreadyPresent));
                continue;
            }

            // 🔴 ONLY ХЯНАСАН REFUSES. ЗӨВШИЛЦСӨН divides its height by whatever
            // it holds, so a further party is cramped rather than impossible -
            // and refusing there would leave a project that genuinely has seven
            // unable to produce a cover at all. The two tables differ because
            // their DRAWINGS differ, which is the same reason the editor's own
            // ceiling differs between them.
            if (RefusesBeyondMaximum(row.Block))
            {
                if (reviewedRoom <= 0)
                {
                    outcomes.Add(new OfficialsProposalOutcome(
                        row.Entry,
                        row.Block,
                        OfficialsProposalSkip.NoRoomLeft));
                    continue;
                }

                reviewedRoom--;
            }

            outcomes.Add(new OfficialsProposalOutcome(
                row.Entry,
                row.Block,
                OfficialsProposalSkip.None));
        }

        return outcomes;
    }

    /// <summary>
    /// The sentence the person reads afterwards.
    ///
    /// 🔴 IT NAMES WHAT DID NOT HAPPEN, NOT JUST WHAT DID. «Two added» beside a
    /// third row that silently went nowhere is the shape of every defect this
    /// chain has produced; a count of additions alone cannot tell an address that
    /// offered two from one that offered three.
    /// </summary>
    public static string DescribeMn(IReadOnlyList<OfficialsProposalOutcome>? outcomes)
    {
        IReadOnlyList<OfficialsProposalOutcome> rows = outcomes ?? [];
        if (rows.Count == 0)
            return "Энэ хаягт бүртгэгдсэн албан тушаалтан олдсонгүй.";

        int added = rows.Count(row => row.WasAdded);
        int present = rows.Count(row => row.Skip == OfficialsProposalSkip.AlreadyPresent);
        int noRoom = rows.Count(row => row.Skip == OfficialsProposalSkip.NoRoomLeft);

        var parts = new List<string>();
        if (added > 0)
            parts.Add($"{added} мөр нэмэгдлээ");
        if (present > 0)
            parts.Add($"{present} нь жагсаалтад аль хэдийн байна");
        if (noRoom > 0)
            parts.Add($"{noRoom} мөр багтсангүй — ХЯНАСАН хүснэгт хоёр байртай");

        return string.Join(", ", parts) + ".";
    }

    /// <summary>
    /// Whether a table refuses a further row, or merely gets crowded.
    ///
    /// 🔴 THE ANSWER COMES FROM THE DRAWING, AND THE TWO TABLES DIFFER.
    /// ЗӨВШИЛЦСӨН divides a fixed height by however many rows it holds, so a
    /// further party is cramped and possible; ХЯНАСАН prints at the measured two
    /// rows, so a third has nowhere to go. This mirrors the editor's own ceiling,
    /// and the pair is asserted together - if they ever disagree, a row offered
    /// by the address would be refused on one screen and accepted on the other.
    /// </summary>
    public static bool RefusesBeyondMaximum(OfficialsBlock block) => block switch
    {
        OfficialsBlock.ReviewedBy => true,
        OfficialsBlock.ConcurredBy => false,
        _ => throw new ArgumentOutOfRangeException(
            nameof(block),
            block,
            "Энэ хүснэгт дүүрэхэд юу болохыг шийдээгүй байна."),
    };
}
