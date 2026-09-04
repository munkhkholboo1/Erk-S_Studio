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
    public static bool IsVisible(
        bool seatedAsBot,
        IReadOnlySet<string>? assignedProjectIds,
        string? projectId)
    {
        if (!seatedAsBot)
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
        bool seatedAsBot,
        IReadOnlySet<string>? assignedProjectIds,
        bool hasFile,
        string? fileIdentity,
        string? rowProjectId) =>
        IsVisible(
            seatedAsBot,
            assignedProjectIds,
            hasFile ? fileIdentity : rowProjectId);

    /// <summary>
    /// Why a project is not visible, in the words the person needs. Only ever
    /// called for a project that failed <see cref="IsVisible"/>.
    /// </summary>
    public static string ExplainRefusal(IReadOnlySet<string>? assignedProjectIds) =>
        assignedProjectIds is null
            ? "Ботын томилолт уншигдаагүй тул төслүүд харагдахгүй. " +
              "Сервертэй холбогдоод дахин оролдоно уу."
            : "Энэ төсөл энэ ботод томилогдоогүй байна.";
}
