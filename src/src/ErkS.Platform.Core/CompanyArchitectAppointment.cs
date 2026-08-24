namespace ErkS.Platform.Core;

/// <summary>
/// What a company profile should hold for its chief architect after somebody
/// edits it.
/// </summary>
/// <param name="Title">The architect's job title.</param>
/// <param name="Name">The architect's name.</param>
/// <param name="Known">
/// Whether this device can now claim to know who the architect is - see
/// <see cref="CompanyProfile.DesignRepresentativeKnown"/>.
/// </param>
public readonly record struct CompanyArchitectAppointment(
    string Title,
    string Name,
    bool Known);

/// <summary>
/// Turns what someone typed into the chief-architect boxes into an answer, and
/// refuses to invent one.
///
/// The difficulty is that "empty" carries two different meanings. On a profile
/// the server has told us about, empty means nobody is appointed, and saving
/// it should clear whatever the server holds. On a profile that predates the
/// split, empty means this device was never told - and saving that as an
/// appointment of nobody would erase an architect somebody appointed on the
/// website.
///
/// Nothing here compares the architect's name to the director's. The residue
/// left by the old mirroring does make them equal, but so does a small company
/// where one person is both, and deleting that person's appointment because
/// the shape looked like residue would be the same kind of guess this whole
/// change exists to remove.
/// </summary>
public static class CompanyArchitectEditorPolicy
{
    public static CompanyArchitectAppointment Decide(
        bool known,
        string? typedTitle,
        string? typedName)
    {
        string title = (typedTitle ?? "").Trim();
        string name = (typedName ?? "").Trim();

        // Already known: the boxes are the whole truth, blanks included.
        if (known)
            return new CompanyArchitectAppointment(title, name, Known: true);

        // Not known, and nothing typed: still not known. The stored fields are
        // left exactly as they are so that the next sync can replace them.
        if (title.Length == 0 && name.Length == 0)
            return new CompanyArchitectAppointment("", "", Known: false);

        // Somebody typed a name: that is an appointment, and the first thing
        // this device actually knows about the architect.
        return new CompanyArchitectAppointment(title, name, Known: true);
    }

    /// <summary>
    /// What to show under the chief-architect boxes.
    /// </summary>
    /// <remarks>
    /// This says out loud that the album does not read this field, because it
    /// does not: the architect the album prints comes from whoever the project
    /// team appointed, and these boxes are the organisation's own record, kept
    /// with the cloud and the website. Two different things now share the
    /// words "ерөнхий архитектор", and letting the reader assume they are one
    /// would be a promise the software does not keep.
    /// </remarks>
    public static string Explain(bool known, string? storedName) =>
        known
            ? (string.IsNullOrWhiteSpace(storedName)
                ? "Байгууллагад ерөнхий архитектор томилогдоогүй."
                : "Байгууллагын бүртгэлийн ерөнхий архитектор. Альбомын архитекторын мөр "
                  + "төслийн багаас томилогддог тул энд бичсэн нэр альбомд автоматаар орохгүй.")
            : "Ерөнхий архитекторыг энэ төхөөрөмж хараахан мэдэхгүй байна. "
              + "Үүлтэй нэг синк хийвэл серверийн утга ирнэ; эсвэл нэрийг нь энд бичиж томилно уу.";
}
