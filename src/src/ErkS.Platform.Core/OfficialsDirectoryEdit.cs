namespace ErkS.Platform.Core;

/// <summary>
/// Changing one district's officials inside the whole directory.
///
/// 🔴 THE EDITOR SHOWS ONE DISTRICT AND SAVES ALL OF THEM. That is where a list
/// of thirty-six rows quietly becomes a list of four: the screen holds the rows
/// it is showing, the save writes what the screen holds, and every other
/// district is gone with nothing having failed. The rule is separated from the
/// screen and tested here for exactly that reason - a rule that lives inside a
/// window's assembly is a rule nobody can check.
/// </summary>
public static class OfficialsDirectoryEdit
{
    /// <summary>
    /// The whole directory with <paramref name="unitCode"/>'s rows replaced by
    /// <paramref name="rows"/>, and every other unit untouched.
    /// </summary>
    /// <remarks>
    /// Order is preserved for the units that are not being edited, and the
    /// edited unit keeps its place: a save that reordered the file would make
    /// every save look like a change to anything comparing the two.
    /// </remarks>
    public static IReadOnlyList<OfficialsDirectoryEntry> ReplaceUnit(
        IReadOnlyList<OfficialsDirectoryEntry>? all,
        string? unitCode,
        IReadOnlyList<OfficialsDirectoryEntry>? rows)
    {
        string unit = (unitCode ?? "").Trim();

        // 🔴 NO UNIT, NO WRITE. Replacing «the rows of nowhere» would append the
        // screen's rows under an empty code - unreachable by any lookup - or, if
        // it matched on empty, delete every row that has no code. Both are silent.
        if (unit.Length == 0)
            return Copy(all);

        var result = new List<OfficialsDirectoryEntry>();
        var written = false;

        foreach (OfficialsDirectoryEntry entry in all ?? [])
        {
            if (entry is null)
                continue;

            if (!string.Equals((entry.UnitCode ?? "").Trim(), unit, StringComparison.Ordinal))
            {
                result.Add(entry.Clone());
                continue;
            }

            // The edited unit's new rows land where its old rows began, so the
            // file's order does not churn on every save.
            if (!written)
            {
                result.AddRange(Stamped(rows, unit));
                written = true;
            }
        }

        if (!written)
            result.AddRange(Stamped(rows, unit));

        return result;
    }

    /// <summary>
    /// The distinct unit codes the directory holds, in the order they first
    /// appear - what a chooser offers as «districts already filled in».
    /// </summary>
    public static IReadOnlyList<string> UnitsWithOfficials(
        IReadOnlyList<OfficialsDirectoryEntry>? all)
    {
        var seen = new List<string>();
        foreach (OfficialsDirectoryEntry entry in all ?? [])
        {
            string unit = (entry?.UnitCode ?? "").Trim();
            if (unit.Length > 0 && !seen.Contains(unit, StringComparer.Ordinal))
                seen.Add(unit);
        }

        return seen;
    }

    /// <summary>
    /// One unit's rows, copied out for editing.
    ///
    /// COPIES, so that abandoning an edit leaves the directory as it was. A
    /// screen handed the live objects would save half its changes by accident
    /// the moment anything else read the list.
    /// </summary>
    public static IReadOnlyList<OfficialsDirectoryEntry> RowsOf(
        IReadOnlyList<OfficialsDirectoryEntry>? all,
        string? unitCode)
    {
        string unit = (unitCode ?? "").Trim();
        if (unit.Length == 0)
            return [];

        return (all ?? [])
            .Where(entry =>
                entry is not null &&
                string.Equals((entry.UnitCode ?? "").Trim(), unit, StringComparison.Ordinal))
            .Select(entry => entry.Clone())
            .ToList();
    }

    /// <summary>
    /// The rows as they will be stored: copied, normalised, and stamped with the
    /// unit being edited.
    ///
    /// 🔴 THE CODE COMES FROM THE CHOSEN DISTRICT, NEVER FROM THE ROW. A screen
    /// that let a row carry its own code would let somebody file an official
    /// under a district they are not looking at - and see nothing, because the
    /// row would simply vanish from the list they are editing.
    /// </summary>
    private static IEnumerable<OfficialsDirectoryEntry> Stamped(
        IReadOnlyList<OfficialsDirectoryEntry>? rows,
        string unit)
    {
        foreach (OfficialsDirectoryEntry row in rows ?? [])
        {
            if (row is null)
                continue;

            OfficialsDirectoryEntry copy = row.Clone();
            copy.UnitCode = unit;
            copy.Normalize();

            // A row with no organisation is a row somebody started and left; it
            // would be refused by the reader anyway, and refusing it HERE means
            // the count on screen after a save matches what was kept.
            if (copy.OrganizationName.Length > 0)
                yield return copy;
        }
    }

    private static IReadOnlyList<OfficialsDirectoryEntry> Copy(
        IReadOnlyList<OfficialsDirectoryEntry>? all) =>
        (all ?? [])
            .Where(entry => entry is not null)
            .Select(entry => entry.Clone())
            .ToList();
}
