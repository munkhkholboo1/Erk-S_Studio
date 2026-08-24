namespace ErkS.Platform.Core;

/// <summary>
/// What one person has contributed to a project, in sources.
/// </summary>
/// <param name="Count">How many sources they registered.</param>
/// <param name="SheetCount">How many sheets those sources carry in total.</param>
/// <param name="Names">
/// The sources themselves, in a readable order, for a tooltip or a list.
/// </param>
public sealed record ProjectMemberSourceSummary(
    int Count,
    int SheetCount,
    IReadOnlyList<string> Names)
{
    public static readonly ProjectMemberSourceSummary None =
        new(0, 0, []);

    public bool Any => Count > 0;
}

/// <summary>
/// Which sources on a project belong to which person.
///
/// Only sources registered through the cloud can answer this: they carry the
/// email of whoever registered them. A source added purely on one device has
/// no person recorded at all - only an organisation - so it cannot be
/// attributed here, and pretending otherwise would put someone's name on work
/// the data does not say is theirs.
/// </summary>
public static class ProjectMemberSources
{
    public static ProjectMemberSourceSummary For(
        IEnumerable<ProjectCloudSourceReference>? sharedSources,
        string? memberEmail)
    {
        string email = Normalize(memberEmail);
        if (email.Length == 0)
            return ProjectMemberSourceSummary.None;

        List<ProjectCloudSourceReference> mine =
        [
            .. (sharedSources ?? [])
                .Where(source => source is not null)
                .Where(source => Normalize(source.RegisteredBy).Equals(email, StringComparison.Ordinal)),
        ];

        if (mine.Count == 0)
            return ProjectMemberSourceSummary.None;

        return new ProjectMemberSourceSummary(
            mine.Count,
            mine.Sum(source => Math.Max(0, source.SheetCount)),
            [.. mine.Select(Describe).Order(StringComparer.CurrentCulture)]);
    }

    /// <summary>
    /// A name a person would recognise, preferring what they called the
    /// document over the key the machine matches on.
    /// </summary>
    private static string Describe(ProjectCloudSourceReference source)
    {
        if (!string.IsNullOrWhiteSpace(source.SourceDocumentReference))
            return source.SourceDocumentReference.Trim();
        if (!string.IsNullOrWhiteSpace(source.SourceKey))
            return source.SourceKey.Trim();
        return string.IsNullOrWhiteSpace(source.SourceApplication)
            ? "Нэргүй эх үүсвэр"
            : source.SourceApplication.Trim();
    }

    private static string Normalize(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
