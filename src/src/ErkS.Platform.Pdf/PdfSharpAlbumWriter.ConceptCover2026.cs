using ErkS.Platform.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Pdf;

public sealed partial class PdfSharpAlbumWriter
{
    /// <summary>
    /// The 2026 concept cover: A4 landscape, four tables in two pairs.
    ///
    /// It lives beside the older cover rather than inside it. One routine
    /// drawing two documents behind a flag is how the working and concept
    /// covers ended up five to eight millimetres apart with nothing able to see
    /// it, and this sheet is not a variation of the other one - it has a
    /// different set of blocks, a logo cell spanning two rows, and a table
    /// whose rows come from a roster.
    ///
    /// Geometry: _shared/concept-cover-sheet-contract-2026-09-06.json (measured
    /// off the user's DWG). Everything the measurement left open:
    /// _shared/concept-cover-decisions-2026-09-06.json. The two are kept apart
    /// on purpose - one is what the drawing IS, the other is what was chosen.
    /// </summary>
    private static void DrawConceptCoverSheet2026(
        PdfDocument document,
        AlbumBuildRequest request,
        AlbumCompositionItem item)
    {
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(ConceptCoverSheetGrid.PageWidthMm);
        page.Height = XUnit.FromMillimeter(ConceptCoverSheetGrid.PageHeightMm);
        page.Orientation = PdfSharp.PageOrientation.Landscape;

        using XGraphics gfx = XGraphics.FromPdfPage(page);
        CoverFontContexts.Add(gfx, new CoverFontContext(FontName));
        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);

        var pen = new XPen(XColors.Black, Mm(ConceptCoverSheetGrid.LineWeightMm));

        DrawConceptCover2026Frame(gfx, pen);
        DrawConceptCover2026TitleTexts(gfx, request.Project);
        DrawConceptCover2026UpperPair(gfx, pen, request.Project);
        DrawConceptCover2026LowerPair(gfx, pen, request.Project);

        _ = item;
    }

    /// <summary>
    /// Y on this sheet is measured from the BOTTOM, like the contract and like
    /// the DWG.
    ///
    /// 🔴 THE SHARED COVER HELPERS CANNOT BE REUSED, and the reason is a trap
    /// rather than a limitation: DrawCoverLine and CoverRect flip against
    /// BuildingArchitectureConceptPageLayout.PageHeightMm, which is a CONSTANT
    /// 297. They read as page-relative and are A3-only. A second A4 sheet
    /// written by somebody who reaches for them will land 87 mm off the page,
    /// and the drawing will look empty rather than wrong.
    ///
    /// Making the page height a parameter of those helpers is a separate change
    /// - every existing caller passes A3 today and would have to be checked -
    /// so this is a note rather than a fix.
    /// </summary>
    private static double ConceptCover2026Y(double millimetresFromBottom) =>
        Mm(ConceptCoverSheetGrid.PageHeightMm - millimetresFromBottom);

    private static void ConceptCover2026Line(
        XGraphics gfx,
        XPen pen,
        double x0Mm,
        double y0Mm,
        double x1Mm,
        double y1Mm) =>
        gfx.DrawLine(pen, Mm(x0Mm), ConceptCover2026Y(y0Mm), Mm(x1Mm), ConceptCover2026Y(y1Mm));

    private static XRect ConceptCover2026Rect(double x0Mm, double bottomMm, double x1Mm, double topMm) =>
        new(Mm(x0Mm), ConceptCover2026Y(topMm), Mm(x1Mm - x0Mm), Mm(topMm - bottomMm));

    private static void DrawConceptCover2026Frame(XGraphics gfx, XPen pen)
    {
        gfx.DrawRectangle(
            pen,
            ConceptCover2026Rect(
                ConceptCoverSheetGrid.FrameLeftMm,
                ConceptCoverSheetGrid.FrameBottomMm,
                ConceptCoverSheetGrid.FrameLeftMm + ConceptCoverSheetGrid.FrameWidthMm,
                ConceptCoverSheetGrid.FrameBottomMm + ConceptCoverSheetGrid.FrameHeightMm));
    }

    /// <summary>
    /// The lines above the tables. Positions are the drawing's; the words are
    /// the project's.
    ///
    /// The four instruction notes, the ҮЛГЭРЧИЛСЭН ЗАГВАР watermark and the red
    /// pointer circles are NOT drawn - they are the template telling its reader
    /// what to fill in. The red project-title placeholder IS drawn, because it
    /// is a field rather than an instruction: colour does not decide this, and
    /// sorting by colour would have got both of them wrong.
    /// </summary>
    private static void DrawConceptCover2026TitleTexts(XGraphics gfx, AlbumProject project)
    {
        const double bodyTextMm = 2.475;

        // 🔴 The measured text of this label is bare punctuation - the DXF pass
        // recovered ":" and the approver's name but not the words in between.
        // «БАТЛАВ:» is what the sheet reads, so it is written here and the
        // position is the label's own measured point; if the extraction is
        // completed and disagrees, this is the line to correct.
        DrawConceptCover2026Text(
            gfx,
            "БАТЛАВ:",
            centreXMm: 159.75,
            baselineMm: 188.49,
            widthMm: 60.0,
            heightMm: bodyTextMm);
        DrawConceptCover2026Text(
            gfx,
            ConceptCoverApprovalResolver
                .Resolve(project.ApprovalWorkflow, project.PlanningTask)
                .ApprovedBy.FirstOrDefault()?.PersonName ?? "",
            centreXMm: 184.65,
            baselineMm: 178.97,
            widthMm: 90.0,
            heightMm: bodyTextMm);

        DrawConceptCover2026Text(
            gfx,
            project.InitiationBasis.SiteAddress,
            centreXMm: 141.47,
            baselineMm: 149.18,
            widthMm: 200.0,
            heightMm: bodyTextMm);

        // Two lines in the drawing, one project name here: the placeholder was
        // split to fit, and a real name wraps on its own.
        DrawConceptCover2026Text(
            gfx,
            ProjectDisplayName(project),
            centreXMm: 148.5,
            baselineMm: 130.0,
            widthMm: 230.0,
            heightMm: 8.0);

        DrawConceptCover2026Text(
            gfx,
            "/ЗАГВАР ЗУРАГ/",
            centreXMm: 141.51,
            baselineMm: 116.88,
            widthMm: 90.0,
            heightMm: bodyTextMm);

        DrawConceptCover2026Text(
            gfx,
            ConceptCover2026Footer(project),
            centreXMm: 148.5,
            baselineMm: 13.53,
            widthMm: 200.0,
            heightMm: 2.829);
    }

    /// <summary>
    /// «УЛААНБААТАР ХОТ 2026 ОН» in the drawing - the organisation's registered
    /// city and the sheet's own year here. Neither is a constant: the same
    /// hard-coded city was removed from the other two covers on 2026-09-06.
    /// </summary>
    internal static string ConceptCover2026Footer(AlbumProject project)
    {
        string city = (ResolveDesignCompanyProfile(project).RegisteredCity ?? "").Trim();
        string year = CornerTableYear(project).ToString(System.Globalization.CultureInfo.InvariantCulture);
        return city.Length == 0 ? $"{year} ОН" : $"{city} {year} ОН";
    }

    private static void DrawConceptCover2026Text(
        XGraphics gfx,
        string? text,
        double centreXMm,
        double baselineMm,
        double widthMm,
        double heightMm) =>
        DrawWrappedCoverText(
            gfx,
            text,
            new XRect(
                Mm(centreXMm - widthMm / 2),
                ConceptCover2026Y(baselineMm + heightMm),
                Mm(widthMm),
                Mm(heightMm * 2)),
            heightMm,
            false,
            XStringFormats.Center,
            FontName);

    /// <summary>
    /// ЗӨВШИЛЦСӨН on the left, ХЯНАСАН on the right. Each side divides its own
    /// forty millimetres by its own row count, under one rule - the drawing
    /// happens to show the right-hand table with two rows in both of its
    /// variants, and two examples are not a rule.
    /// </summary>
    private static void DrawConceptCover2026UpperPair(
        XGraphics gfx,
        XPen pen,
        AlbumProject project)
    {
        IReadOnlyList<ProjectApprovalEntry> concurring =
            project.ApprovalWorkflow.ConceptDesign.ConcurredBy;

        DrawConceptCover2026UpperTable(
            gfx,
            pen,
            ConceptCoverSheetGrid.TablesLeftMm,
            ConceptCoverSheetGrid.TablesMiddleMm,
            "ЗӨВШИЛЦСӨН.",
            concurring);

        // 🔴 ХЯНАСАН has no roster on this side yet. The table is drawn empty
        // rather than filled from a neighbouring list: ЗӨВШӨӨРӨЛЦСӨН is the
        // nearest thing and means something else, and a form printed with the
        // wrong parties is worse than one printed blank for signing.
        DrawConceptCover2026UpperTable(
            gfx,
            pen,
            ConceptCoverSheetGrid.TablesMiddleMm,
            ConceptCoverSheetGrid.TablesRightMm,
            "ХЯНАСАН.",
            []);
    }

    private static void DrawConceptCover2026UpperTable(
        XGraphics gfx,
        XPen pen,
        double leftMm,
        double rightMm,
        string label,
        IReadOnlyList<ProjectApprovalEntry> rows)
    {
        double top = ConceptCoverSheetGrid.UpperTopMm;
        double bottom = ConceptCoverSheetGrid.UpperBottomMm;
        double headerBottom = top - ConceptCoverSheetGrid.UpperHeaderHeightMm;
        double roleRight = leftMm + ConceptCoverSheetGrid.UpperRoleColumnMm;
        double nameRight = roleRight + ConceptCoverSheetGrid.NameColumnMm;

        gfx.DrawRectangle(pen, ConceptCover2026Rect(leftMm, bottom, rightMm, top));
        ConceptCover2026Line(gfx, pen, leftMm, headerBottom, rightMm, headerBottom);
        ConceptCover2026Line(gfx, pen, roleRight, bottom, roleRight, headerBottom);
        ConceptCover2026Line(gfx, pen, nameRight, bottom, nameRight, headerBottom);

        DrawConceptCover2026Cell(gfx, label, leftMm, headerBottom, rightMm, top, bold: true);

        // An empty table still has its rows: the sheet is signed by hand, so a
        // party with no name recorded needs a line to sign on.
        int rowCount = Math.Max(1, rows.Count);
        IReadOnlyList<double> boundaries = ConceptCoverSheetGrid.UpperRowBoundaries(rowCount);
        for (int index = 0; index < rowCount; index++)
        {
            double rowTop = boundaries[index];
            double rowBottom = boundaries[index + 1];
            if (index > 0)
                ConceptCover2026Line(gfx, pen, leftMm, rowTop, rightMm, rowTop);
            if (index >= rows.Count)
                continue;

            DrawConceptCover2026Cell(
                gfx,
                ConceptCoverApprovalResolver.DisplayPosition(rows[index]),
                leftMm,
                rowBottom,
                roleRight,
                rowTop);
            DrawConceptCover2026Cell(gfx, rows[index].PersonName, roleRight, rowBottom, nameRight, rowTop);
        }
    }

    /// <summary>
    /// ГҮЙЦЭТГЭГЧ and ЗАХИАЛАГЧ. Two rows of eight millimetres, and a logo cell
    /// that is ONE cell sixteen millimetres tall - the divider between the rows
    /// stops at its edge instead of crossing it.
    /// </summary>
    private static void DrawConceptCover2026LowerPair(
        XGraphics gfx,
        XPen pen,
        AlbumProject project)
    {
        CompanyProfile company = ResolveDesignCompanyProfile(project);
        (string Role, string Name) representative = ResolveCompanyRepresentative(project);
        string clientType = ProjectClientTypes.Recognize(project.InitiationBasis.ClientType);

        DrawConceptCover2026LowerTable(
            gfx,
            pen,
            ConceptCoverSheetGrid.TablesLeftMm,
            ConceptCoverSheetGrid.TablesMiddleMm,
            "ГҮЙЦЭТГЭГЧ.",
            representative.Role,
            representative.Name,
            company);

        DrawConceptCover2026LowerTable(
            gfx,
            pen,
            ConceptCoverSheetGrid.TablesMiddleMm,
            ConceptCoverSheetGrid.TablesRightMm,
            "ЗАХИАЛАГЧ.",
            ProjectClientTypes.ResolveCoverRole(
                clientType,
                project.InitiationBasis.ClientName,
                project.InitiationBasis.ClientRepresentativePosition),
            ProjectClientTypes.ResolveCoverPersonName(
                clientType,
                project.InitiationBasis.ClientName,
                project.InitiationBasis.ClientRepresentativeName,
                project.ClientName),
            ProjectClientTypes.UsesLogo(clientType)
                ? project.InitiationBasis.ClientOrganizationSnapshot
                : null);
    }

    private static void DrawConceptCover2026LowerTable(
        XGraphics gfx,
        XPen pen,
        double leftMm,
        double rightMm,
        string label,
        string? role,
        string? personName,
        CompanyProfile? logoOwner)
    {
        double top = ConceptCoverSheetGrid.LowerTopMm;
        double bottom = ConceptCoverSheetGrid.LowerBottomMm;
        double middle = top - ConceptCoverSheetGrid.LowerRowHeightMm;
        double logoRight = leftMm + ConceptCoverSheetGrid.LogoColumnMm;
        double roleRight = logoRight + ConceptCoverSheetGrid.LowerRoleColumnMm;
        double nameRight = roleRight + ConceptCoverSheetGrid.NameColumnMm;

        gfx.DrawRectangle(pen, ConceptCover2026Rect(leftMm, bottom, rightMm, top));
        ConceptCover2026Line(gfx, pen, logoRight, bottom, logoRight, top);
        ConceptCover2026Line(gfx, pen, roleRight, bottom, roleRight, top);
        ConceptCover2026Line(gfx, pen, nameRight, bottom, nameRight, top);

        // Starts at the logo cell's edge, not at the table's: crossing it would
        // cut the logo in half.
        ConceptCover2026Line(gfx, pen, logoRight, middle, rightMm, middle);

        DrawConceptCover2026Cell(gfx, label, logoRight, middle, roleRight, top, bold: true);
        DrawConceptCover2026Cell(gfx, role, logoRight, bottom, roleRight, middle);
        DrawConceptCover2026Cell(gfx, personName, roleRight, bottom, nameRight, middle);

        if (logoOwner is not null)
        {
            DrawCompanyLogoOrMark(
                gfx,
                logoOwner,
                ConceptCover2026Rect(leftMm, bottom, logoRight, top),
                2.0);
        }
    }

    private static void DrawConceptCover2026Cell(
        XGraphics gfx,
        string? text,
        double x0Mm,
        double bottomMm,
        double x1Mm,
        double topMm,
        bool bold = false)
    {
        XRect rect = ConceptCover2026Rect(x0Mm, bottomMm, x1Mm, topMm);
        DrawWrappedCoverText(
            gfx,
            text,
            new XRect(
                rect.X + Mm(1.2),
                rect.Y + Mm(0.6),
                rect.Width - Mm(2.4),
                rect.Height - Mm(1.2)),
            2.0,
            bold,
            XStringFormats.Center,
            FontName);
    }
}
