namespace ErkS.Platform.Core;

/// <summary>
/// Putting suggested officials into a roster WITHOUT disturbing what a person
/// wrote.
///
/// 🔴 EMPTY PLACES ARE FILLED; WRITTEN ONES ARE NEVER TOUCHED. The owner settled
/// the order asking for the feature - «Онцгой байдлын ерөнхий газар болон эрүүл
/// мэндийн яаманд хандахаар бол СОНГОДОГ байна» - the automatic sits UNDER the
/// manual choice. Overwriting a typed row inverts that, and the cost is somebody
/// losing work they cannot get back; clearing a row themselves to ask again is
/// one extra step, which is far cheaper.
///
/// 🔴 AND NO PROVENANCE FLAG. Marking rows «came from a suggestion» would add a
/// state to the schema and STILL not answer the real question - an address that
/// changes makes an old suggestion stale, and a flag cannot tell a stale one from
/// a deliberate one. «Only fill what is empty» settles it without any state at
/// all.
/// </summary>
public static class OfficialsRosterFill
{
    /// <summary>
    /// Whether this row holds anything a person put there.
    ///
    /// 🔴 AN ORGANISATION IS WHAT MAKES A ROW REAL. The editor adds blank rows
    /// when somebody presses «add», and the sheet prints a blank line to sign on
    /// - so a row with no organisation is a PLACE, not an occupant. Counting one
    /// as occupied is how a table that looks empty refuses a suggestion for
    /// having no room.
    /// </summary>
    public static bool IsEmptyPlace(ProjectApprovalEntry? entry) =>
        string.IsNullOrWhiteSpace(entry?.OrganizationName);

    /// <summary>How many of a roster's places are actually taken.</summary>
    public static int OccupiedCount(IReadOnlyList<ProjectApprovalEntry>? rows) =>
        (rows ?? []).Count(entry => !IsEmptyPlace(entry));

    /// <summary>
    /// The roster with <paramref name="additions"/> put into its empty places
    /// first, and appended only once those run out.
    /// </summary>
    /// <remarks>
    /// Filling in place rather than appending keeps the roster the length the
    /// person made it: pressing «suggest» after adding two blank rows should fill
    /// those two rows, not produce four.
    /// </remarks>
    public static IReadOnlyList<ProjectApprovalEntry> Apply(
        IReadOnlyList<ProjectApprovalEntry>? existing,
        IReadOnlyList<ProjectApprovalEntry>? additions)
    {
        var result = (existing ?? [])
            .Where(entry => entry is not null)
            .Select(entry => entry.Clone())
            .ToList();

        var pending = new Queue<ProjectApprovalEntry>(
            (additions ?? []).Where(entry => entry is not null));

        for (var index = 0; index < result.Count && pending.Count > 0; index++)
        {
            if (IsEmptyPlace(result[index]))
            {
                ProjectApprovalEntry filling = pending.Dequeue().Clone();

                // The place keeps its own identity: it is the same row on screen,
                // now with something in it.
                filling.Id = result[index].Id;
                result[index] = filling;
            }
        }

        while (pending.Count > 0)
            result.Add(pending.Dequeue().Clone());

        return result;
    }
}
