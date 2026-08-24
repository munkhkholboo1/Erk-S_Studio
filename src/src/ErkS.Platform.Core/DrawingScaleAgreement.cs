namespace ErkS.Platform.Core;

/// <summary>
/// Whether a drawing arrived at the scale its slot asks for.
///
/// The scale a slot prescribes is a requirement of the set - the reference
/// album says this sheet is М1:1500 - while the scale on the page is whatever
/// the drawing was actually plotted at. Nobody can catch a sheet returned at
/// the wrong scale without something to compare it against, which is why the
/// requirement lives on the slot.
///
/// The check is deliberately soft, and it is soft in three separate ways. A
/// slot that prescribes nothing asks nothing. A page that states nothing is
/// not accused. And a disagreement is reported, never refused: the person who
/// plotted the drawing knows things this does not, and a build that stops is a
/// build that gets worked around.
/// </summary>
public static class DrawingScaleAgreement
{
    /// <summary>
    /// True when there is nothing to complain about - which includes both the
    /// cases where there is nothing to compare.
    /// </summary>
    public static bool Agrees(string? requiredScale, string? actualScale)
    {
        string required = Canonical(requiredScale);
        string actual = Canonical(actualScale);
        return required.Length == 0 || actual.Length == 0 ||
            required.Equals(actual, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// What to tell the user about one sheet, or null when the two agree or
    /// when one of them said nothing.
    /// </summary>
    public static string? Describe(string? sheetNumber, string? requiredScale, string? actualScale)
    {
        if (Agrees(requiredScale, actualScale))
            return null;

        string number = (sheetNumber ?? "").Trim();
        string label = number.Length > 0 ? number : "Хуудас";
        return $"{label}: {(actualScale ?? "").Trim()} — шаардлага {(requiredScale ?? "").Trim()}";
    }

    /// <summary>
    /// The same scale written the several ways the two sides write it.
    ///
    /// A slot carries the standard's own spelling, «М1:1500», while a page's
    /// scale has already been normalised to «1:1500». Comparing them as they
    /// come would disagree on every sheet in the album, and a warning that
    /// fires on everything is read as noise and then not read at all.
    /// </summary>
    private static string Canonical(string? value)
    {
        string text = string.Concat(
            (value ?? "").Trim().Where(character => !char.IsWhiteSpace(character)));
        if (text.Length == 0)
            return "";

        // Both the Cyrillic М and the Latin M appear, because both are typed.
        if (text.StartsWith('М') || text.StartsWith('M') || text.StartsWith('м') || text.StartsWith('m'))
            text = text[1..];

        return DrawingScaleText.Normalize(text);
    }
}

/// <summary>
/// Every sheet in an album checked against the scale its slot asks for.
///
/// Reported at the end of a build rather than refused during one: the person
/// who plotted the drawing knows things this does not, and a build that stops
/// is a build that gets worked around.
/// </summary>
public static class DrawingScaleSurvey
{
    /// <summary>
    /// Sheets whose stated scale disagrees with their slot, named one by one.
    /// </summary>
    /// <remarks>
    /// Named rather than counted, because "3 sheets are at the wrong scale"
    /// leaves somebody to find which three across thirty-odd pages.
    /// </remarks>
    public static IReadOnlyList<string> Review(AlbumBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Dictionary<string, string> requiredBySlot = request.Project.Album.Composition
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .Where(item => !string.IsNullOrWhiteSpace(item.Scale))
            .ToDictionary(item => item.Id, item => item.Scale, StringComparer.OrdinalIgnoreCase);
        if (requiredBySlot.Count == 0)
            return [];

        var notices = new List<string>();
        foreach (AlbumBuildPage page in request.Sections.SelectMany(section => section.Pages))
        {
            string slotId = (page.Definition.TemplateSlotId ?? "").Trim();
            if (slotId.Length == 0 || !requiredBySlot.TryGetValue(slotId, out string? required))
                continue;

            string? notice = DrawingScaleAgreement.Describe(page.Number, required, page.ScaleText);
            if (notice is not null)
                notices.Add(notice);
        }

        return notices;
    }
}
