namespace ErkS.Studio;

using System;
using System.Collections.Generic;
using System.Linq;
using ErkS.Platform.Core;

/// <summary>
/// One entry in the corner-table picker on the project information page.
/// </summary>
/// <param name="Value">
/// The stored value, which AutoCAD and Revit read from the project file.
/// </param>
/// <param name="Label">What the user reads.</param>
public sealed record ProjectCornerTableChoice(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// What a project can choose for its corner title block.
///
/// The list is small on purpose. These are the two blocks that exist, drawn by
/// two different routines, and a project uses one of them throughout - a set
/// where the block changes partway reads as two documents bound together.
///
/// The measurements are in the labels because that is how the people using
/// this talk about them: a title block is identified by its size on the page.
/// </summary>
public static class ProjectCornerTableChoices
{
    public static IReadOnlyList<ProjectCornerTableChoice> All { get; } =
    [
        new(AlbumCornerTableStyles.TemplateDecides, "Загварын дагуу (анхдагч)"),
        new(AlbumCornerTableStyles.Concept, "Загвар зургийн хүснэгт — 190×28 мм"),
        new(AlbumCornerTableStyles.WorkingDrawing, "Ажлын зургийн хүснэгт — 180×36 мм"),
    ];

    public static ProjectCornerTableChoice Resolve(string? value)
    {
        string normalized = AlbumCornerTableStyles.Normalize(value);
        return All.First(choice =>
            choice.Value.Equals(normalized, StringComparison.Ordinal));
    }

    /// <summary>
    /// What the picker says under itself. It names the consequence rather than
    /// repeating the choice, because the part nobody can see from the page is
    /// that AutoCAD and Revit follow this too.
    /// </summary>
    public static string Explain(string? value) =>
        AlbumCornerTableStyles.Normalize(value) switch
        {
            AlbumCornerTableStyles.Concept =>
                "Хуудас 190×28 мм хүснэгттэй гарна. AutoCAD, Revit тал ч үүнийг дагана. " +
                NewSheetsOnly,
            AlbumCornerTableStyles.WorkingDrawing =>
                "Хуудас 180×36 мм хүснэгттэй гарна. Эталон тор нэмэгдэхгүй. " +
                "AutoCAD, Revit тал ч үүнийг дагана. " + NewSheetsOnly,
            _ =>
                "Альбомын загвар өөрөө шийднэ — одоо байгаа төслүүдийн харагдац " +
                "өөрчлөгдөхгүй.",
        };

    /// <summary>
    /// AutoCAD freezes the choice into a sheet when the sheet is created, so
    /// that a frame already drawn in a DWG cannot change under the person who
    /// drew it. That is the right behaviour and it has a cost: switching the
    /// style leaves every existing sheet exactly as it was.
    ///
    /// Without saying so, someone changes the setting, sees nothing move, and
    /// concludes it does not work.
    /// </summary>
    private const string NewSheetsOnly =
        "Энэ сонголт зөвхөн шинээр үүсгэх хуудсанд үйлчилнэ; байгаа хуудас хэв маягаа хадгална.";
}
