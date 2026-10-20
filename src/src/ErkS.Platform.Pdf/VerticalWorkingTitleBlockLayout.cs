using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Pdf;

/// <summary>How a corner-table cell aligns its text.</summary>
internal enum WorkingTitleBlockAlign
{
    Left,
    Center,
}

/// <summary>
/// One text cell of a corner table, in millimetres from the table's top-left
/// corner. <see cref="Wrap"/> cells may run to several lines.
/// </summary>
internal sealed record WorkingTitleBlockCell(
    double XMm,
    double YMm,
    double WidthMm,
    double HeightMm,
    string Text,
    double TextHeightMm,
    bool Bold = false,
    WorkingTitleBlockAlign Align = WorkingTitleBlockAlign.Center,
    bool Wrap = false);

/// <summary>One ruled line of a corner table, in millimetres.</summary>
internal sealed record WorkingTitleBlockLine(double X0Mm, double Y0Mm, double X1Mm, double Y1Mm);

/// <summary>
/// The vertical working-drawing corner table, 70 × 90 mm, as data: its ruled
/// lines and its text cells. The writer only scales and paints them.
/// </summary>
/// <remarks>
/// 🔴 MEASURED FROM THE OWNER'S OWN SHEET, not invented. The vector lines of
/// Grand Mark AZ 2026, sheet БА-03 - a vertical table the owner drew by hand -
/// were read with PyMuPDF on 2026-10-19:
///
///     rows     0 | 5 Үе шат | 30 (logo cell only) | 35 | 40 | 45 | 50 roles
///              66 project | 74 sheet name | 82 scale·mark·sheet | 90 ciphers·date
///     columns  25 (logo · labels · scale) | 48.7 (signature · name) | 50 (mark · sheet)
///
/// The table this replaces printed the company, the title and the number and
/// nothing else - no roles, no names, no scale, mark, ciphers or date. It looked
/// drawn and carried almost nothing a signed sheet needs.
///
/// Laid out as data so the content can be tested without rendering: the bug it
/// fixes was a table that rendered fine and said nothing.
/// </remarks>
internal static class VerticalWorkingTitleBlockLayout
{
    public const double WidthMm = WorkingDrawingPageLayout.VerticalTitleBlockWidthMm;
    public const double HeightMm = WorkingDrawingPageLayout.VerticalTitleBlockHeightMm;

    /// <summary>The logo cell, drawn by the writer from the company profile.</summary>
    public static readonly (double X, double Y, double Width, double Height) LogoCellMm = (0, 5, 25, 25);

    public static IReadOnlyList<WorkingTitleBlockLine> Lines { get; } =
    [
        // Full-width rules, top to bottom. The outer frame is the caller's.
        new(0, 5, 70, 5),
        new(0, 35, 70, 35),
        new(0, 40, 70, 40),
        new(0, 45, 70, 45),
        new(0, 50, 70, 50),
        new(0, 66, 70, 66),
        new(0, 74, 70, 74),
        new(0, 82, 70, 82),
        // The logo cell's floor; the company name sits under it.
        new(0, 30, 25, 30),
        // Logo / company / role labels against address and names.
        new(25, 5, 25, 50),
        // Signature against name, in the three role rows.
        new(48.7, 35, 48.7, 50),
        // The two bottom rows: scale · mark · sheet, cipher · cipher · date.
        new(25, 74, 25, 90),
        new(50, 74, 50, 90),
    ];

    public static IReadOnlyList<WorkingTitleBlockCell> Cells(
        AlbumProject project,
        string? sheetTitle,
        string? sheetNumber,
        string? mark,
        string? scale,
        IEnumerable<AlbumPageRoleAssignment>? roleAssignments,
        IEnumerable<SheetRoleSignature>? sourceRoles,
        DateTimeOffset? sourceDate = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        CompanyProfile company = project.Company ?? new CompanyProfile();
        string markText = (mark ?? "").Trim();
        IReadOnlyList<string> names =
            PdfSharpAlbumWriter.ResolveCanonicalHorizontalWorkingTitleBlockNames(
                project,
                roleAssignments,
                sourceRoles);

        string address = (company.Address ?? "").Trim();
        string phone = PdfSharpAlbumWriter.CompanyPhoneTextFor(company).Trim();
        string companyBlock = string.Join(
            "\n",
            new[]
            {
                address.Length == 0 ? "" : "Хаяг: " + address,
                phone.Length == 0 ? "" : "Утас " + phone.Replace("\n", ", "),
            }.Where(line => line.Length > 0));
        string location = ProjectSiteAddress.Compose(
            project.InitiationBasis.SiteLocation,
            project.InitiationBasis.SiteAddress);
        string projectBlock = string.Join(
            "\n",
            new[] { location, PdfSharpAlbumWriter.ProjectDisplayNameFor(project) }
                .Select(value => (value ?? "").Trim())
                .Where(value => value.Length > 0 && value != "-"));

        return
        [
            new(0, 0, 25, 5, "Үе шат:", 2.0, Align: WorkingTitleBlockAlign.Left),
            new(25, 0, 45, 5, markText.Length == 0 ? "АЖЛЫН ЗУРАГ" : $"АЖЛЫН ЗУРАГ /{markText}/", 2.5, Bold: true),
            new(0, 30, 25, 5, PdfSharpAlbumWriter.CompanyLegalDisplayNameFor(company, project.DesignOrganizationName), 1.8),
            new(25, 5, 45, 30, companyBlock, 2.0, Wrap: true),

            new(0, 35, 25, 5, "Архитектор", 2.0, Align: WorkingTitleBlockAlign.Left),
            new(0, 40, 25, 5, "Гүйцэтгэсэн", 2.0, Align: WorkingTitleBlockAlign.Left),
            new(0, 45, 25, 5, "Шалгасан", 2.0, Align: WorkingTitleBlockAlign.Left),
            new(48.7, 35, 21.3, 5, names[0], 2.0, Align: WorkingTitleBlockAlign.Left),
            new(48.7, 40, 21.3, 5, names[1], 2.0, Align: WorkingTitleBlockAlign.Left),
            new(48.7, 45, 21.3, 5, names[2], 2.0, Align: WorkingTitleBlockAlign.Left),

            new(0, 50, 70, 16, projectBlock, 2.5, Wrap: true),
            new(0, 66, 70, 8, (sheetTitle ?? "").Trim(), 3.0, Bold: true, Wrap: true),

            new(0, 74, 25, 4, "Масштаб:", 1.8, Align: WorkingTitleBlockAlign.Left),
            new(0, 78, 25, 4, PdfSharpAlbumWriter.ScaleCellText(scale), 2.5),
            new(25, 74, 25, 4, "Зургийн марк:", 1.8, Align: WorkingTitleBlockAlign.Left),
            new(25, 78, 25, 4, markText, 2.5, Bold: true),
            new(50, 74, 20, 4, "Хуудас", 1.8, Align: WorkingTitleBlockAlign.Left),
            new(50, 78, 20, 4, (sheetNumber ?? "").Trim(), 2.5, Bold: true),

            // Labels always print: an unfilled cipher reads as a form field,
            // not as a rendering fault - the horizontal table's rule.
            new(0, 82, 25, 4, "Е.Г.Шифр:", 1.8, Align: WorkingTitleBlockAlign.Left),
            new(0, 86, 25, 4, (project.GeneralDesignCipher ?? "").Trim(), 2.2),
            new(25, 82, 25, 4, "Т.Г.Шифр:", 1.8, Align: WorkingTitleBlockAlign.Left),
            new(25, 86, 25, 4, (project.TechnicalDesignCipher ?? "").Trim(), 2.2),
            new(50, 82, 20, 4, "Огноо", 1.8, Align: WorkingTitleBlockAlign.Left),
            new(50, 86, 20, 4, PdfSharpAlbumWriter.SheetDateText(project, sourceDate), 2.2),
        ];
    }
}
