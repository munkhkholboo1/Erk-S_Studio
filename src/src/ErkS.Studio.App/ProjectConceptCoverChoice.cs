namespace ErkS.Studio;

using System;
using System.Collections.Generic;
using System.Linq;
using ErkS.Platform.Core;

/// <summary>One entry in the concept-cover picker on the project information page.</summary>
public sealed record ProjectConceptCoverChoice(string Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>
/// Which concept-album cover a project prints.
///
/// Both stay available because that is what was asked for: «солигдож болдгоор
/// хийх хэрэгтэй» - a setting the user can change and change back, not a
/// migration. A choice that could only be made by editing the project file
/// would not meet that, which is why this list exists at all.
/// </summary>
public static class ProjectConceptCoverChoices
{
    public static IReadOnlyList<ProjectConceptCoverChoice> All { get; } =
    [
        new(AlbumConceptCoverStyles.TemplateDecides, "Одоогийн нүүр (анхдагч)"),
        new(AlbumConceptCoverStyles.Classic, "Хуучин нүүр — A3, нэг батлалтын хүснэгт"),
        new(AlbumConceptCoverStyles.Sheet2026, "2026 оны нүүр — A4, дөрвөн хүснэгт"),
        new(AlbumConceptCoverStyles.Sheet2026A3, "2026 оны нүүр — A3, дөрвөн хүснэгт"),
    ];

    public static ProjectConceptCoverChoice Resolve(string? value)
    {
        string normalized = AlbumConceptCoverStyles.Normalize(value);
        return All.First(choice => choice.Value.Equals(normalized, StringComparison.Ordinal));
    }

    /// <summary>
    /// What the picker says under itself. It names the CONSEQUENCE, because the
    /// part nobody can see from this page is which of two different drawings
    /// the album will come out with.
    /// </summary>
    public static string Explain(string? value) =>
        AlbumConceptCoverStyles.Normalize(value) switch
        {
            AlbumConceptCoverStyles.Sheet2026 =>
                "A4 хэвтээ нүүр: дээд талд ЗӨВШИЛЦСӨН, ХЯНАСАН, доод талд ГҮЙЦЭТГЭГЧ, " +
                "ЗАХИАЛАГЧ. ЗӨВШИЛЦСӨН мөрүүд төслийн жагсаалтаас ирнэ. " +
                "ХЯНАСАН хүснэгт одоогоор хоосон хэвлэгдэнэ.",
            AlbumConceptCoverStyles.Sheet2026A3 =>
                // The consequence a reader cannot see from this page is that it
                // is NOT the A4 sheet enlarged: the writing stays the size it
                // is, and the sheet gains room rather than magnification.
                "Дээрхтэй ЯГ ИЖИЛ нүүр, A3 хэвтээ дээр. Үсэг, шугам, мөрийн өндөр " +
                "томрохгүй — «Албан тушаал» багана өргөсөж, гарчгийн дээд тал " +
                "чөлөөтэй болно.",
            AlbumConceptCoverStyles.Classic =>
                "A3 нүүр: нэг батлалтын хүснэгт, БАТЛАВ ба ЗӨВШӨӨРӨЛЦСӨН мөрүүдтэй.",
            _ =>
                // Blank does NOT mean "the newest". Every album on disk predates
                // this setting, so a default that chose the 2026 sheet would
                // reprint two dozen projects as a document nobody has seen.
                "Альбом одоо хэвлэдэг нүүрээ хэвээр гаргана — сонгох хүртэл юу ч " +
                "өөрчлөгдөхгүй.",
        };
}
