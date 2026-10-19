using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Pdf;

/// <summary>
/// The horizontal working-drawing corner table, 170 × 34 mm, as data: its
/// ruled lines and its text cells. The writer only scales and paints them.
/// </summary>
/// <remarks>
/// 🔴 MEASURED FROM THE OWNER'S OWN SHEETS. The vector lines of Eco Garden AZ
/// (EG BA 05-10-24.pdf) read with PyMuPDF on 2026-10-19:
///
///     columns  25 (logo · labels) | 45 (name) | 70 (signature) | 95 | 120 | 145
///     rows     9 project | 15 sheet title | 21, 27 role rows (left, to 95)
///              24 metadata row (right, from 95) | 27 logo floor (0 → 25)
///
/// Eco Garden's table is 33 high; PFR's TitleBlockHorizontal.rfa and the
/// owner say 34, and the extra millimetre goes to the bottom rows.
///
/// The table this replaces was laid out on 180 × 36, a size taken from a
/// contract whose numbers matched no family anyone places.
/// </remarks>
internal static class HorizontalWorkingTitleBlockLayout
{
    public const double WidthMm = WorkingDrawingPageLayout.HorizontalTitleBlockWidthMm;
    public const double HeightMm = WorkingDrawingPageLayout.HorizontalTitleBlockHeightMm;

    private const double LeftRow1 = 21;
    private const double LeftRow2 = 27;
    private const double RightRow = 24.5;

    /// <summary>The logo cell, drawn by the writer from the company profile.</summary>
    public static readonly (double X, double Y, double Width, double Height) LogoCellMm = (0, 0, 25, 27);

    public static IReadOnlyList<WorkingTitleBlockLine> Lines { get; } =
    [
        // The outer frame is the caller's.
        new(25, 0, 25, HeightMm),
        new(25, 9, WidthMm, 9),
        new(25, 15, WidthMm, 15),
        new(25, LeftRow1, 95, LeftRow1),
        new(0, LeftRow2, 95, LeftRow2),
        new(95, RightRow, WidthMm, RightRow),
        new(45, 15, 45, HeightMm),
        new(70, 15, 70, HeightMm),
        new(95, 15, 95, HeightMm),
        new(120, 15, 120, HeightMm),
        new(145, 15, 145, HeightMm),
    ];

    public static IReadOnlyList<WorkingTitleBlockCell> Cells(
        AlbumProject project,
        string? sheetTitle,
        string? sheetNumber,
        string? mark,
        string? scale,
        IEnumerable<AlbumPageRoleAssignment>? roleAssignments,
        IEnumerable<SheetRoleSignature>? sourceRoles)
    {
        ArgumentNullException.ThrowIfNull(project);
        CompanyProfile company = project.Company ?? new CompanyProfile();
        IReadOnlyList<string> names =
            PdfSharpAlbumWriter.ResolveCanonicalHorizontalWorkingTitleBlockNames(
                project,
                roleAssignments,
                sourceRoles);
        string location = ProjectSiteAddress.Compose(
            project.InitiationBasis.SiteLocation,
            project.InitiationBasis.SiteAddress);

        return
        [
            new(0, LeftRow2, 25, HeightMm - LeftRow2,
                PdfSharpAlbumWriter.CompanyLegalDisplayNameFor(company, project.DesignOrganizationName), 1.8, Wrap: true),

            new(25, 0, 145, 4.5, PdfSharpAlbumWriter.ProjectDisplayNameFor(project), 2.5),
            new(25, 4.5, 145, 4.5, location == "-" ? "" : location, 2.5),
            new(25, 9, 145, 6, (sheetTitle ?? "").Trim(), 2.5, Align: WorkingTitleBlockAlign.Left),

            new(25, 15, 20, LeftRow1 - 15, "Архитектор", 2.0, Align: WorkingTitleBlockAlign.Left),
            new(25, LeftRow1, 20, LeftRow2 - LeftRow1, "Гүйцэтгэсэн", 2.0, Align: WorkingTitleBlockAlign.Left),
            new(25, LeftRow2, 20, HeightMm - LeftRow2, "Шалгасан", 2.0, Align: WorkingTitleBlockAlign.Left),
            new(45, 15, 25, LeftRow1 - 15, names[0], 2.0),
            new(45, LeftRow1, 25, LeftRow2 - LeftRow1, names[1], 2.0),
            new(45, LeftRow2, 25, HeightMm - LeftRow2, names[2], 2.0),

            // Labels always print: an unfilled cipher reads as a form field,
            // not as a rendering fault.
            new(95, 15, 25, RightRow - 15,
                PdfSharpAlbumWriter.LabelledCell("ЕГ шифр", project.GeneralDesignCipher), 2.0,
                Align: WorkingTitleBlockAlign.Left, Wrap: true),
            new(120, 15, 25, RightRow - 15, $"Масштаб: {(scale ?? "").Trim()}", 2.0,
                Align: WorkingTitleBlockAlign.Left, Wrap: true),
            new(145, 15, 25, RightRow - 15,
                PdfSharpAlbumWriter.LabelledCell("Огноо", PdfSharpAlbumWriter.SheetDateText(project)), 2.0,
                Align: WorkingTitleBlockAlign.Left, Wrap: true),
            new(95, RightRow, 25, HeightMm - RightRow,
                PdfSharpAlbumWriter.LabelledCell("ТГ шифр", project.TechnicalDesignCipher), 2.0,
                Align: WorkingTitleBlockAlign.Left, Wrap: true),
            new(120, RightRow, 25, HeightMm - RightRow, $"Зургийн марк: {(mark ?? "").Trim()}", 2.0,
                Align: WorkingTitleBlockAlign.Left, Wrap: true),
            new(145, RightRow, 25, HeightMm - RightRow, $"Хуудас: {(sheetNumber ?? "").Trim()}", 2.0,
                Align: WorkingTitleBlockAlign.Left, Wrap: true),
        ];
    }
}
