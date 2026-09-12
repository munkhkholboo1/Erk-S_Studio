namespace ErkS.Studio;

/// <summary>
/// What a machine in bot state may see and open.
///
/// The decree this exists for: "Зөвхөн томилогдсон төсөл дээр үүргийнхээ
/// дагуу л оролцоно. Бусад төсөл харагдахгүй." A seated machine had been
/// showing the owner's whole catalogue, which is the opposite of that.
///
/// UNKNOWN MEANS HIDE. If the assignment list has not been read - the server
/// was unreachable, the seat has not resumed yet, the token is not in hand -
/// the answer is "nothing", never "everything". Showing all of them while the
/// list is missing is exactly the failure being fixed, and it is the one a
/// reader is most tempted to write, because "no filter yet" looks like "no
/// filter needed".
/// </summary>
internal static class StudioBotProjectVisibility
{
    /// <summary>
    /// Whether <paramref name="projectId"/> may be seen or opened.
    ///
    /// <paramref name="assignedProjectIds"/> is null when the assignment list
    /// is unknown, and empty when the seat is genuinely assigned nothing. Both
    /// answer false - they differ only in what the user is told.
    /// </summary>
    /// <param name="actingAsBot">
    /// The BOT is the one acting - not merely «this machine holds a seat».
    ///
    /// 🔴 THE PARAMETER USED TO BE CALLED seatedAsBot AND THE NAME WAS THE
    /// DEFECT. A machine keeps its seat when the owner signs in on it, so callers
    /// passing «seated» hid the owner's whole catalogue from the owner. The rule
    /// was right; it was being asked the wrong question.
    /// </param>
    public static bool IsVisible(
        bool actingAsBot,
        IReadOnlySet<string>? assignedProjectIds,
        string? projectId)
    {
        if (!actingAsBot)
            return true;
        if (assignedProjectIds is null)
            return false;
        if (string.IsNullOrWhiteSpace(projectId))
            return false;
        return assignedProjectIds.Contains(projectId.Trim());
    }

    /// <summary>
    /// Whether a seat may OPEN a project, given what the route about to open it
    /// knows.
    ///
    /// Hiding a row is not a boundary - a project can be reached from the home
    /// page, a recent card, a file dialog, or the cloud list - so every route
    /// asks this before it opens anything.
    ///
    /// <paramref name="hasFile"/> says whether there is a project file on this
    /// disk to read. When there is, IT is the authority and the row that led
    /// here is ignored: a row came from a list, and a list is not evidence.
    /// That includes a file whose identity could not be read - it yields no
    /// identity and is refused, because falling back to the row there would
    /// open an unreadable file on the strength of a list entry.
    ///
    /// When there is no file the row's server id is all there is, which is the
    /// cloud-only case: nothing has been mirrored yet. That case is why this
    /// method exists. The gate had been written on the local-file route alone,
    /// and the cloud route branches away one line earlier - so a project never
    /// assigned to the seat opened in full, album and all.
    /// </summary>
    public static bool MayOpen(
        bool actingAsBot,
        IReadOnlySet<string>? assignedProjectIds,
        bool hasFile,
        string? fileIdentity,
        string? rowProjectId) =>
        IsVisible(
            actingAsBot,
            assignedProjectIds,
            hasFile ? fileIdentity : rowProjectId);

    /// <summary>
    /// The assignment list has not been read at all. Not the same as «assigned
    /// nothing», and the two must never share a sentence.
    /// </summary>
    public const string AssignmentsUnreadMn =
        "Ботын томилолт уншигдаагүй тул төслүүд харагдахгүй. " +
        "Сервертэй холбогдоод дахин оролдоно уу.";

    /// <summary>
    /// «This seat has no projects», as a noun phrase.
    ///
    /// 🔴 ONE HOME, BECAUSE TWO PLACES SAY IT. The status line under the shell
    /// has been saying it correctly all along while the empty project list said
    /// «Cloud ERA бүртгэлээр нэвтэрнэ үү» - advice that is false on a seat, which
    /// signed in with its PIN and would change nothing by following it. Two
    /// spellings of one fact is how they come to disagree; the seat's own words
    /// were already right, so they are the ones that are shared.
    /// </summary>
    public const string NoAssignedProjectsMn = "томилогдсон төсөл алга";

    /// <summary>
    /// Why a project is not visible, in the words the person needs. Only ever
    /// called for a project that failed <see cref="IsVisible"/>.
    /// </summary>
    public static string ExplainRefusal(IReadOnlySet<string>? assignedProjectIds) =>
        assignedProjectIds is null
            ? AssignmentsUnreadMn
            : "Энэ төсөл энэ суудалд томилогдоогүй байна.";

    /// <summary>
    /// Why the project LIST is empty on a seat - a title and a sentence.
    ///
    /// 🔴 THE EMPTY LIST HAD ONE SENTENCE FOR THREE DIFFERENT REASONS: «not
    /// signed in», «a seat with no assignment» and «an owner with no projects». A
    /// seat is signed in - by its PIN - so «нэвтэрнэ үү» sent the reader to a door
    /// that would change nothing, or out of bot state altogether.
    ///
    /// 🔴 UNREAD AND ASSIGNED-NOTHING STAY APART. One is «ask the server again»,
    /// the other is «ask the licence holder for an assignment». Folding them would
    /// tell somebody to check their network about a decision nobody has made yet.
    /// </summary>
    public static (string Title, string Message) ExplainEmptyList(
        IReadOnlySet<string>? assignedProjectIds) =>
        assignedProjectIds is null
            ? ("Ботын томилолт уншигдаагүй", AssignmentsUnreadMn)
            : (Capitalised(NoAssignedProjectsMn),
               "Энэ суудалд төсөл томилогдмогц энд харагдана. " +
               "Томилолтыг лиценз эзэмшигч хийнэ.");

    /// <summary>
    /// The shared phrase as a heading. Derived rather than typed a second time -
    /// a title that merely LOOKS like the phrase is the divergence this avoids.
    /// </summary>
    internal static string Capitalised(string phrase) =>
        phrase.Length == 0 ? phrase : char.ToUpperInvariant(phrase[0]) + phrase[1..];
}
