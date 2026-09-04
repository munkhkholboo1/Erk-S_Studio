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
    /// Why a project is not visible, in the words the person needs. Only ever
    /// called for a project that failed <see cref="IsVisible"/>.
    /// </summary>
    public static string ExplainRefusal(IReadOnlySet<string>? assignedProjectIds) =>
        assignedProjectIds is null
            ? "Ботын томилолт уншигдаагүй тул төслүүд харагдахгүй. " +
              "Сервертэй холбогдоод дахин оролдоно уу."
            : "Энэ төсөл энэ ботод томилогдоогүй байна.";
}
