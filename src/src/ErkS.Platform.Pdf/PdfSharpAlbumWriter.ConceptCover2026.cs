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
        AlbumCompositionItem item,
        ConceptCoverLayout layout)
    {
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(layout.PageWidthMm);
        page.Height = XUnit.FromMillimeter(layout.PageHeightMm);
        page.Orientation = PdfSharp.PageOrientation.Landscape;

        using XGraphics gfx = XGraphics.FromPdfPage(page);
        CoverFontContexts.Add(gfx, new CoverFontContext(FontName));
        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);

        var pen = new XPen(XColors.Black, Mm(ConceptCoverSheetGrid.LineWeightMm));

        DrawConceptCover2026Frame(gfx, pen, layout);
        DrawConceptCover2026TitleTexts(gfx, request.Project, layout);
        DrawConceptCover2026UpperPair(gfx, pen, request.Project, layout);
        DrawConceptCover2026LowerPair(gfx, pen, request.Project, layout);

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
    private static double ConceptCover2026Y(ConceptCoverLayout layout, double millimetresFromBottom) =>
        Mm(layout.PageHeightMm - millimetresFromBottom);

    private static void ConceptCover2026Line(
        XGraphics gfx,
        XPen pen,
        ConceptCoverLayout layout,
        double x0Mm,
        double y0Mm,
        double x1Mm,
        double y1Mm) =>
        gfx.DrawLine(
            pen,
            Mm(x0Mm),
            ConceptCover2026Y(layout, y0Mm),
            Mm(x1Mm),
            ConceptCover2026Y(layout, y1Mm));

    private static XRect ConceptCover2026Rect(
        ConceptCoverLayout layout,
        double x0Mm,
        double bottomMm,
        double x1Mm,
        double topMm) =>
        new(Mm(x0Mm), ConceptCover2026Y(layout, topMm), Mm(x1Mm - x0Mm), Mm(topMm - bottomMm));

    private static void DrawConceptCover2026Frame(XGraphics gfx, XPen pen, ConceptCoverLayout layout)
    {
        gfx.DrawRectangle(
            pen,
            ConceptCover2026Rect(
                layout,
                layout.FrameLeftMm,
                layout.FrameBottomMm,
                layout.FrameRightMm,
                layout.FrameTopMm));
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
    /// <summary>
    /// The lines above the tables. WHICH lines those are, and where the block
    /// sits on a given sheet, are decided in
    /// <see cref="ConceptCoverTitleBlock"/> - this method only supplies the
    /// words and hands them to the renderer.
    ///
    /// That split is not tidiness. The block's offset from the centre of the
    /// free area is a number the A3 placement rests on, and it changes SIGN
    /// depending on which lines are counted as part of the block. Leaving the
    /// enumeration inline here would have left that definition implicit in a
    /// sequence of six calls.
    ///
    /// The footer is NOT part of the block: it sits below the tables and keeps
    /// its own distance from the foot of the frame, which is why it is still
    /// placed here by hand.
    /// </summary>
    private static void DrawConceptCover2026TitleTexts(
        XGraphics gfx,
        AlbumProject project,
        ConceptCoverLayout layout)
    {
        string approver = ConceptCoverApprovalResolver
            .Resolve(project.ApprovalWorkflow, project.PlanningTask)
            .ApprovedBy.FirstOrDefault()?.PersonName ?? "";

        foreach (ConceptCoverTitleLine line in ConceptCoverTitleBlock.For(layout))
        {
            string text = line.Key switch
            {
                // 🔴 The measured text of this label is bare punctuation - the
                // DXF pass recovered ":" and the approver's name but not the
                // words between. «БАТЛАВ:» is what the sheet reads, so it is
                // written here and the position is the label's own measured
                // point; if the extraction is completed and disagrees, this is
                // the line to correct.
                ConceptCoverTitleBlock.ApprovalLabel => "БАТЛАВ:",
                ConceptCoverTitleBlock.Approver => approver,
                ConceptCoverTitleBlock.SiteAddress => ProjectSiteAddress.Compose(
                    project.InitiationBasis.SiteLocation,
                    project.InitiationBasis.SiteAddress),
                // Two lines in the drawing, one project name here: the
                // placeholder was split to fit, and a real name wraps on its own.
                ConceptCoverTitleBlock.ProjectTitle => ProjectDisplayName(project),
                ConceptCoverTitleBlock.StageLine => "/ЗАГВАР ЗУРАГ/",
                _ => "",
            };

            DrawConceptCover2026Text(
                gfx,
                layout,
                text,
                centreXMm: line.CentreXMm,
                centreYMm: line.CentreYMm,
                widthMm: line.WidthMm,
                heightMm: line.CapHeightMm,
                anchorIsLeftEdge: line.AnchorIsLeftEdge);
        }

        DrawConceptCover2026Text(
            gfx,
            layout,
            ConceptCover2026Footer(project),
            // 🔴 CENTRED ON THE FRAME, NOT CARRIED OVER FROM A4. The reference
            // states this one exactly - fractionOfFrame.x = 0.5 and an offset from the
            // frame's centre of 0.0000 - which is the only placement the drawing spells
            // out rather than leaves to be measured. Written as a constant it would
            // read as a coincidence; written as centring it is the rule.
            //
            // ⚠ The height needs no change and that is a POSITIVE CONTROL, not luck:
            // the existing frame-relative rule puts this line 9.99 mm above the frame,
            // and the A3 drawing measures 10.00. An offset from the frame was the right
            // coordinate to keep.
            centreXMm: layout.TablesMiddleMm,
            centreYMm: layout.FrameBottomMm + (13.53 - ConceptCoverLayout.A4.FrameBottomMm),
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
        ConceptCoverLayout layout,
        string? text,
        double centreXMm,
        double centreYMm,
        double widthMm,
        double heightMm,
        bool anchorIsLeftEdge = false)
    {
        // 🔴 centreYMm, not a baseline. This box spans [c - h, c + h] and the
        // glyph is centred in it, so the value positions the MIDDLE of the
        // writing - which is what the arithmetic behind the title block's
        // placement depends on. It was called baselineMm until somebody had to
        // do that arithmetic.
        (double _, double topMm) = ConceptCoverTextBox.DrawBox(centreYMm, heightMm);
        DrawWrappedCoverText(
            gfx,
            text,
            new XRect(
                Mm(anchorIsLeftEdge ? centreXMm : centreXMm - widthMm / 2),
                ConceptCover2026Y(layout, topMm),
                Mm(widthMm),
                Mm(heightMm * 2)),
            heightMm,
            false,
            // A label anchored at the drawing's own insertion point is drawn FROM
            // there; centring it inside the same box would put it back where the
            // measured point was being misread as a middle.
            anchorIsLeftEdge ? XStringFormats.CenterLeft : XStringFormats.Center,
            FontName);
    }

    /// <summary>
    /// ЗӨВШИЛЦСӨН on the left, ХЯНАСАН on the right. Each side divides its own
    /// forty millimetres by its own row count, under one rule - the drawing
    /// happens to show the right-hand table with two rows in both of its
    /// variants, and two examples are not a rule.
    /// </summary>
    private static void DrawConceptCover2026UpperPair(
        XGraphics gfx,
        XPen pen,
        AlbumProject project,
        ConceptCoverLayout layout)
    {
        IReadOnlyList<ProjectApprovalEntry> concurring =
            project.ApprovalWorkflow.ConceptDesign.ConcurredBy;

        DrawConceptCover2026UpperTable(
            gfx,
            pen,
            layout,
            layout.TablesLeftMm,
            layout.TablesMiddleMm,
            "ЗӨВШИЛЦСӨН.",
            concurring,
            // ⚠ ONE, WHICH IS NOT THE DRAWING'S THREE. Left as it was: how many
            // lines this side prints when nobody has filled it is part of the
            // varying-row question the owner deferred (№24), and answering it
            // here in passing would settle it without a drawing.
            drawnRowMinimum: 1);

        // 🔴 ХЯНАСАН NOW HAS A ROSTER OF ITS OWN, AND STILL BORROWS NOBODY'S.
        // This table was drawn empty for as long as there was nowhere to store
        // its rows - the right answer then, because ЗӨВШӨӨРӨЛЦСӨН is the nearest
        // list and means something else, and a form printed with the wrong
        // parties is worse than one printed blank for signing. What was missing
        // was a list, not a fallback.
        //
        // Two rows, drawn whether or not anybody has filled them: the drawing has
        // two and the owner named two people. An unfilled row is a line to sign,
        // which is what the paper form is for.
        IReadOnlyList<ProjectApprovalEntry> reviewing =
            project.ApprovalWorkflow.ConceptDesign.ReviewedBy;

        DrawConceptCover2026UpperTable(
            gfx,
            pen,
            layout,
            layout.TablesMiddleMm,
            layout.TablesRightMm,
            "ХЯНАСАН.",
            reviewing,
            // Two, from the measured sheet: rightRowHeightsMm is [20.0, 20.0].
            drawnRowMinimum: ConceptCoverSheetGrid.MeasuredUpperRowHeightsForTwo.Count);
    }

    private static void DrawConceptCover2026UpperTable(
        XGraphics gfx,
        XPen pen,
        ConceptCoverLayout layout,
        double leftMm,
        double rightMm,
        string label,
        IReadOnlyList<ProjectApprovalEntry> rows,
        int drawnRowMinimum)
    {
        double top = layout.UpperTopMm;
        double bottom = layout.UpperBottomMm;
        double headerBottom = top - ConceptCoverSheetGrid.UpperHeaderHeightMm;
        double roleRight = leftMm + layout.UpperRoleColumnMm;
        double nameRight = roleRight + ConceptCoverSheetGrid.NameColumnMm;

        gfx.DrawRectangle(pen, ConceptCover2026Rect(layout, leftMm, bottom, rightMm, top));
        ConceptCover2026Line(gfx, pen, layout, leftMm, headerBottom, rightMm, headerBottom);
        ConceptCover2026Line(gfx, pen, layout, roleRight, bottom, roleRight, headerBottom);
        ConceptCover2026Line(gfx, pen, layout, nameRight, bottom, nameRight, headerBottom);

        DrawConceptCover2026Cell(gfx, layout, label, leftMm, headerBottom, rightMm, top, bold: true);

        // An empty table still has its rows: the sheet is signed by hand, so a
        // party with no name recorded needs a line to sign on.
        //
        // 🔴 HOW MANY LINES IS THE DRAWING'S ANSWER, NOT THE ROSTER'S. This used
        // to be Math.Max(1, ...) for both tables, so an empty ХЯНАСАН printed ONE
        // line where the measured sheet has TWO - invisible while the table had
        // no roster at all and nobody looked at how many lines it drew empty.
        int rowCount = Math.Max(Math.Max(1, drawnRowMinimum), rows.Count);
        IReadOnlyList<double> boundaries = layout.UpperRowBoundaries(rowCount);
        for (int index = 0; index < rowCount; index++)
        {
            double rowTop = boundaries[index];
            double rowBottom = boundaries[index + 1];
            if (index > 0)
                ConceptCover2026Line(gfx, pen, layout, leftMm, rowTop, rightMm, rowTop);
            if (index >= rows.Count)
                continue;

            DrawConceptCover2026Cell(
                gfx,
                layout,
                ConceptCoverApprovalResolver.DisplayPosition(rows[index]),
                leftMm,
                rowBottom,
                roleRight,
                rowTop);
            DrawConceptCover2026Cell(gfx, layout, rows[index].PersonName, roleRight, rowBottom, nameRight, rowTop);
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
        AlbumProject project,
        ConceptCoverLayout layout)
    {
        CompanyProfile company = ResolveDesignCompanyProfile(project);
        (string Role, string Name) representative = ResolveCompanyRepresentative(project);
        string clientType = ProjectClientTypes.Recognize(project.InitiationBasis.ClientType);

        DrawConceptCover2026LowerTable(
            gfx,
            pen,
            layout,
            layout.TablesLeftMm,
            layout.TablesMiddleMm,
            "ГҮЙЦЭТГЭГЧ.",
            representative.Role,
            representative.Name,
            company);

        DrawConceptCover2026LowerTable(
            gfx,
            pen,
            layout,
            layout.TablesMiddleMm,
            layout.TablesRightMm,
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
        ConceptCoverLayout layout,
        double leftMm,
        double rightMm,
        string label,
        string? role,
        string? personName,
        CompanyProfile? logoOwner)
    {
        double top = layout.LowerTopMm;
        double bottom = layout.LowerBottomMm;
        double middle = top - ConceptCoverSheetGrid.LowerRowHeightMm;
        double logoRight = leftMm + ConceptCoverSheetGrid.LogoColumnMm;
        double roleRight = logoRight + layout.LowerRoleColumnMm;
        double nameRight = roleRight + ConceptCoverSheetGrid.NameColumnMm;

        gfx.DrawRectangle(pen, ConceptCover2026Rect(layout, leftMm, bottom, rightMm, top));
        ConceptCover2026Line(gfx, pen, layout, logoRight, bottom, logoRight, top);
        ConceptCover2026Line(gfx, pen, layout, roleRight, bottom, roleRight, top);
        ConceptCover2026Line(gfx, pen, layout, nameRight, bottom, nameRight, top);

        // Starts at the logo cell's edge, not at the table's: crossing it would
        // cut the logo in half.
        ConceptCover2026Line(gfx, pen, layout, logoRight, middle, rightMm, middle);

        DrawConceptCover2026Cell(gfx, layout, label, logoRight, middle, roleRight, top, bold: true);
        DrawConceptCover2026Cell(gfx, layout, role, logoRight, bottom, roleRight, middle);
        DrawConceptCover2026Cell(gfx, layout, personName, roleRight, bottom, nameRight, middle);

        if (logoOwner is not null)
        {
            // LOGO ONLY. The other covers fall back to an Erk-S mark and the
            // words «Лого байршуул» - an instruction addressed to whoever is
            // filling the template in, not to whoever reads the finished sheet.
            // This one is signed and stamped, and "put a logo here" printed on
            // a signed document is a fault in the document.
            //
            // The absence is not hidden either: the organisation editor says a
            // logo has not been set. Same shape as the other two gaps on this
            // sheet - past six rows, and an empty ХЯНАСАН - the document is
            // left honest and the person is told plainly.
            DrawCompanyLogoOnly(
                gfx,
                logoOwner,
                ConceptCover2026Rect(layout, leftMm, bottom, logoRight, top));
        }
    }

    private static void DrawConceptCover2026Cell(
        XGraphics gfx,
        ConceptCoverLayout layout,
        string? text,
        double x0Mm,
        double bottomMm,
        double x1Mm,
        double topMm,
        bool bold = false)
    {
        XRect rect = ConceptCover2026Rect(layout, x0Mm, bottomMm, x1Mm, topMm);
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
