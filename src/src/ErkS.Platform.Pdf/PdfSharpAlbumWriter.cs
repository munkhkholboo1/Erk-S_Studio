using ErkS.Platform.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Pdf;

/// <summary>
/// Composes source PDFs into Studio page instances. Source-as-is keeps the
/// original page untouched; formatted pages preserve vector content and add
/// the Studio-owned frame and title information.
/// </summary>
public sealed class PdfSharpAlbumWriter : IAlbumPdfWriter
{
    private const string FontName = BuildingArchitectureConceptPageLayout.FontFamilyName;
    private const double PointsPerMillimeter = 72.0 / 25.4;
    private const double PreserveSizeToleranceMm = 0.75;

    public AlbumBuildResult Compose(AlbumBuildRequest request, string outputPath)
    {
        WindowsFontResolver.Register();
        var warnings = new List<string>();
        using var document = new PdfDocument();
        document.Info.Title = request.Project.Album.Title;
        document.Info.Author = request.Project.Company.Name;
        var components = new Dictionary<string, AlbumBuildComponent>(StringComparer.OrdinalIgnoreCase);
        int componentOrder = 0;

        void RecordComponent(string code, string label, int firstPageIndex)
        {
            int lastPageIndex = document.PageCount;
            if (lastPageIndex <= firstPageIndex)
                return;
            if (!components.TryGetValue(code, out AlbumBuildComponent? component))
            {
                component = new AlbumBuildComponent
                {
                    Code = code,
                    Label = label,
                    Order = componentOrder++,
                };
                components.Add(code, component);
            }
            for (int page = firstPageIndex + 1; page <= lastPageIndex; page++)
            {
                if (!component.PageNumbers.Contains(page))
                    component.PageNumbers.Add(page);
            }
        }

        var generatedPages = BuildingArchitectureConceptGeneratedPagePlanner
            .Create(request.Project)
            .ToList();
        if (generatedPages.Count > 0)
        {
            foreach (var item in generatedPages)
            {
                int firstPageIndex = document.PageCount;
                DrawGeneratedPage(document, request, item);
                string documentKind = item.DocumentKind == ConceptGeneratedDocumentKind.None
                    ? item.Component.GeneratedPageKind.ToString()
                    : item.DocumentKind.ToString();
                RecordComponent(
                    $"generated:{item.Component.Id}:{documentKind}",
                    item.Title,
                    firstPageIndex);
            }
        }
        else if (request.Project.Album.IncludeCover)
        {
            int firstPageIndex = document.PageCount;
            DrawCoverPage(document, request);
            RecordComponent("generated:cover", "Нүүр хуудас", firstPageIndex);
        }

        if (request.Project.Album.IncludeTableOfContents)
        {
            int firstPageIndex = document.PageCount;
            DrawTableOfContents(document, request);
            RecordComponent("generated:table-of-contents", "Зургийн жагсаалт", firstPageIndex);
        }

        var sheetCount = 0;
        foreach (var section in request.Sections)
        {
            if (section.Kind == AlbumBuildSectionKind.Building &&
                section.Pages.Count > 0)
            {
                int firstPageIndex = document.PageCount;
                DrawBuildingSubCoverPage(document, request.Project, section.Title);
                RecordComponent(
                    $"generated:building-sub-cover:{section.Key}",
                    $"{section.Title} · Дэд нүүр хуудас",
                    firstPageIndex);
            }

            foreach (var buildPage in section.Pages)
            {
                var sheet = buildPage.Sheet;
                if (!File.Exists(sheet.PdfPath))
                {
                    throw new InvalidDataException(
                        $"Verified PDF disappeared before composition: {sheet.DisplayLabel} ({sheet.PdfPath})");
                }

                int firstPageIndex = document.PageCount;
                if (buildPage.Format.Kind == PageFormatKind.SourceAsIs)
                {
                    ImportSourceAsIs(document, sheet);
                }
                else
                {
                    ComposeFormattedPages(document, request.Project, buildPage);
                }

                string sourceIdentity = !string.IsNullOrWhiteSpace(sheet.SourceId)
                    ? sheet.SourceId
                    : sheet.SourceIdentity;
                RecordComponent(
                    "source:" + sourceIdentity,
                    string.IsNullOrWhiteSpace(section.Title) ? sheet.DisplayLabel : section.Title,
                    firstPageIndex);

                sheetCount++;
            }
        }

        int firstVisualizationNumber = BuildingArchitectureConceptAlbumSequencer.NextAutomaticNumber(
            request.Project.Album,
            request.Sections.SelectMany(section => section.Pages).Select(page => page.StudioNumber),
            generatedPages.Count);
        IReadOnlyList<VisualizationAlbumPagePlan> visualizationPages =
            string.IsNullOrWhiteSpace(request.Project.ProjectId)
                ? VisualizationPageLayoutPlanner.Create(
                    request.Project.Visualizations,
                    firstVisualizationNumber)
                : VisualizationPageLayoutPlanner.Create(
                    request.Project.Visualizations,
                    request.Project.ProjectId,
                    firstVisualizationNumber);
        foreach (VisualizationAlbumPagePlan plan in visualizationPages)
        {
            int firstPageIndex = document.PageCount;
            DrawVisualizationPage(document, request.Project, plan, warnings);
            RecordComponent("generated:visualizations", "Харагдах байдал", firstPageIndex);
        }

        if (document.PageCount == 0)
        {
            int firstPageIndex = document.PageCount;
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            RecordComponent("generated:empty", "Хоосон альбум", firstPageIndex);
        }

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var pageCount = document.PageCount;
        document.Save(outputPath);
        var result = new AlbumBuildResult
        {
            OutputPath = outputPath,
            SheetCount = sheetCount,
            PageCount = pageCount,
        };
        result.Warnings.AddRange(warnings);
        result.Components.AddRange(components.Values
            .OrderBy(item => item.Order));
        return result;
    }

    private static void ImportSourceAsIs(PdfDocument document, SheetRecord sheet)
    {
        using var source = PdfReader.Open(sheet.PdfPath, PdfDocumentOpenMode.Import);
        if (sheet.Entry.PdfPageNumber > 0)
        {
            int pageIndex = sheet.Entry.PdfPageNumber - 1;
            if (pageIndex >= source.PageCount)
            {
                throw new InvalidDataException(
                    $"Referenced PDF page {sheet.Entry.PdfPageNumber} is unavailable for {sheet.DisplayLabel}.");
            }
            document.AddPage(source.Pages[pageIndex]);
            return;
        }

        foreach (var page in source.Pages)
        {
            document.AddPage(page);
        }
    }

    private static void ComposeFormattedPages(
        PdfDocument document,
        AlbumProject project,
        AlbumBuildPage buildPage)
    {
        int sourcePageCount;
        using (var source = PdfReader.Open(buildPage.Sheet.PdfPath, PdfDocumentOpenMode.Import))
        {
            sourcePageCount = source.PageCount;
        }

        IEnumerable<int> sourcePageNumbers = buildPage.Sheet.Entry.PdfPageNumber > 0
            ? [buildPage.Sheet.Entry.PdfPageNumber]
            : Enumerable.Range(1, sourcePageCount);
        using var form = XPdfForm.FromFile(buildPage.Sheet.PdfPath);
        foreach (int sourcePageNumber in sourcePageNumbers)
        {
            if (sourcePageNumber > sourcePageCount)
            {
                throw new InvalidDataException(
                    $"Referenced PDF page {sourcePageNumber} is unavailable for {buildPage.Sheet.DisplayLabel}.");
            }
            form.PageNumber = sourcePageNumber;
            var page = document.AddPage();
            page.Width = XUnit.FromMillimeter(buildPage.Format.WidthMm);
            page.Height = XUnit.FromMillimeter(buildPage.Format.HeightMm);
            using var gfx = XGraphics.FromPdfPage(page);

            // Revit's white page can become transparent when imported as a PDF form.
            gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
            DrawSource(gfx, form, buildPage);
            DrawPageFormat(gfx, page, project, buildPage);
        }
    }

    private static void DrawSource(XGraphics gfx, XPdfForm form, AlbumBuildPage buildPage)
    {
        var format = buildPage.Format;
        var target = buildPage.Definition.PlacementMode == PagePlacementMode.FullPage
            ? new XRect(0, 0, Mm(format.WidthMm), Mm(format.HeightMm))
            : ToPoints(format.DrawingArea);

        var sourceWidth = Math.Max(1, form.PointWidth);
        var sourceHeight = Math.Max(1, form.PointHeight);
        if (buildPage.Definition.PlacementMode == PagePlacementMode.PreserveDrawingSpace)
        {
            var widthDifferenceMm = Math.Abs(target.Width - sourceWidth) / PointsPerMillimeter;
            var heightDifferenceMm = Math.Abs(target.Height - sourceHeight) / PointsPerMillimeter;
            if (widthDifferenceMm > PreserveSizeToleranceMm || heightDifferenceMm > PreserveSizeToleranceMm)
            {
                throw new InvalidDataException(
                    $"Clean drawing-space PDF is {sourceWidth / PointsPerMillimeter:0.##} x " +
                    $"{sourceHeight / PointsPerMillimeter:0.##} mm, but format '{format.Id}' requires " +
                    $"{format.DrawingArea.Width:0.##} x {format.DrawingArea.Height:0.##} mm. " +
                    "The source was not resized.");
            }

            var preserveState = gfx.Save();
            gfx.IntersectClip(target);
            gfx.DrawImage(form, target.X, target.Y, sourceWidth, sourceHeight);
            gfx.Restore(preserveState);
            return;
        }

        var scaleX = target.Width / sourceWidth;
        var scaleY = target.Height / sourceHeight;
        var scale = buildPage.Definition.PlacementMode == PagePlacementMode.FillCrop
            ? Math.Max(scaleX, scaleY)
            : Math.Min(scaleX, scaleY);
        var width = sourceWidth * scale;
        var height = sourceHeight * scale;
        var x = target.X + (target.Width - width) / 2;
        var y = target.Y + (target.Height - height) / 2;

        var state = gfx.Save();
        gfx.IntersectClip(target);
        gfx.DrawImage(form, x, y, width, height);
        gfx.Restore(state);
    }

    private static void DrawPageFormat(
        XGraphics gfx,
        PdfPage page,
        AlbumProject project,
        AlbumBuildPage buildPage)
    {
        var format = buildPage.Format;
        if (BuildingArchitectureConceptPageLayout.SupportsStudioChrome(format))
        {
            DrawConceptSheetChrome(gfx, project, buildPage);
            return;
        }

        var borderPen = new XPen(XColors.Black, 0.65);
        var finePen = new XPen(XColor.FromArgb(115, 125, 136), 0.35);
        var pageRect = new XRect(0, 0, page.Width.Point, page.Height.Point);
        var drawingRect = ToPoints(format.DrawingArea);
        var sheetTitleRect = ToPoints(format.SheetTitleArea);
        var titleRect = ToPoints(format.TitleBlockArea);
        var paperBrush = new XSolidBrush(XColor.FromArgb(254, 254, 254));

        if (sheetTitleRect.Width > 0 && sheetTitleRect.Height > 0)
        {
            gfx.DrawRectangle(paperBrush, sheetTitleRect);
        }
        if (titleRect.Width > 0 && titleRect.Height > 0)
        {
            gfx.DrawRectangle(paperBrush, titleRect);
        }

        if (format.ShowBorder)
        {
            gfx.DrawRectangle(borderPen, drawingRect);
            if (sheetTitleRect.Width > 0 && sheetTitleRect.Height > 0)
            {
                gfx.DrawRectangle(borderPen, sheetTitleRect);
            }
            gfx.DrawRectangle(borderPen, titleRect);
        }

        if (format.ShowGrid)
        {
            DrawGridMarks(gfx, drawingRect, finePen);
        }

        DrawSheetTitle(gfx, sheetTitleRect, buildPage, borderPen);
        DrawTitleBlock(gfx, titleRect, project, buildPage, borderPen, finePen);
        gfx.DrawRectangle(new XPen(XColor.FromArgb(185, 190, 196), 0.25), pageRect);
    }

    private static void DrawConceptSheetChrome(
        XGraphics gfx,
        AlbumProject project,
        AlbumBuildPage buildPage)
    {
        bool hasInformationHeader = BuildingArchitectureConceptPageLayout.UsesInformationHeader(
            buildPage.Sheet.Entry.ContentKind,
            buildPage.Sheet.Entry.Name,
            buildPage.Definition.TemplateSlotId);
        BuildingArchitectureConceptPageRegions regions =
            BuildingArchitectureConceptPageLayout.ResolveRegions(
                buildPage.Format,
                hasInformationHeader);
        var borderPen = new XPen(XColors.Black, Mm(0.35));
        var finePen = new XPen(XColors.Black, Mm(0.10));
        var frame = ToPoints(regions.Frame);
        var header = ToPoints(regions.SheetTitleArea);
        var corner = ToPoints(regions.TitleBlockArea);
        var paperBrush = new XSolidBrush(XColor.FromArgb(254, 254, 254));

        // These areas belong to Studio and must cover any authoring-application
        // annotation that may still be present during the migration period.
        // Near-white deliberately forces a PDF color operator after an imported
        // form restores its graphics state; otherwise some renderers reuse black.
        gfx.DrawRectangle(paperBrush, header);
        gfx.DrawRectangle(paperBrush, corner);
        if (hasInformationHeader)
        {
            gfx.DrawRectangle(
                paperBrush,
                ToPoints(regions.InformationArea));
        }
        gfx.DrawRectangle(borderPen, frame);
        gfx.DrawLine(
            borderPen,
            Mm(regions.SheetTitleArea.X),
            Mm(regions.SheetTitleArea.Y + regions.SheetTitleArea.Height),
            Mm(regions.SheetTitleArea.X + regions.SheetTitleArea.Width),
            Mm(regions.SheetTitleArea.Y + regions.SheetTitleArea.Height));

        if (hasInformationHeader)
        {
            DrawConceptElevationHeader(gfx, project, buildPage, regions, borderPen);
        }

        DrawFittedText(
            gfx,
            buildPage.Title,
            header.Left + Mm(3),
            header.Top + Mm(0.8),
            header.Width - Mm(6),
            header.Height - Mm(1.6),
            8.5,
            false,
            XStringFormats.CenterRight);

        DrawConceptCornerTable(
            gfx,
            project,
            buildPage.Number,
            buildPage.Sheet.Entry.ScaleText,
            regions.TitleBlockArea,
            borderPen,
            finePen);
        double stampWidth = Math.Min(79, Math.Max(0, regions.Frame.Width - 2));
        DrawFittedText(
            gfx,
            "Sheet generated by Erk-S Platform",
            Mm(regions.Frame.X + regions.Frame.Width - stampWidth - 1),
            Mm(regions.Frame.Y + regions.Frame.Height + 0.4),
            Mm(stampWidth),
            Mm(3.6),
            3.6,
            false,
            XStringFormats.CenterRight);
    }

    private static void DrawConceptSheetChrome(
        XGraphics gfx,
        AlbumProject project,
        string title,
        string number)
    {
        PageFormatDefinition format = PageFormatCatalog.Resolve(
            PageFormatCatalog.ConceptA3LandscapeId);
        BuildingArchitectureConceptPageRegions regions =
            BuildingArchitectureConceptPageLayout.ResolveRegions(
                format,
                includeInformationHeader: false);
        var borderPen = new XPen(XColors.Black, Mm(0.35));
        var finePen = new XPen(XColors.Black, Mm(0.10));
        var frame = ToPoints(regions.Frame);
        var header = ToPoints(regions.SheetTitleArea);
        var corner = ToPoints(regions.TitleBlockArea);
        var paperBrush = new XSolidBrush(XColor.FromArgb(254, 254, 254));

        gfx.DrawRectangle(paperBrush, header);
        gfx.DrawRectangle(paperBrush, corner);
        gfx.DrawRectangle(borderPen, frame);
        gfx.DrawLine(
            borderPen,
            Mm(regions.SheetTitleArea.X),
            Mm(regions.SheetTitleArea.Y + regions.SheetTitleArea.Height),
            Mm(regions.SheetTitleArea.X + regions.SheetTitleArea.Width),
            Mm(regions.SheetTitleArea.Y + regions.SheetTitleArea.Height));
        DrawFittedText(
            gfx,
            title,
            header.Left + Mm(3),
            header.Top + Mm(0.8),
            header.Width - Mm(6),
            header.Height - Mm(1.6),
            8.5,
            false,
            XStringFormats.CenterRight);
        DrawConceptCornerTable(
            gfx,
            project,
            number,
            "",
            regions.TitleBlockArea,
            borderPen,
            finePen);
        DrawFittedText(
            gfx,
            "Sheet generated by Erk-S Platform",
            Mm(335),
            Mm(292.4),
            Mm(79),
            Mm(3.6),
            3.6,
            false,
            XStringFormats.CenterRight);
    }

    private static void DrawConceptElevationHeader(
        XGraphics gfx,
        AlbumProject project,
        AlbumBuildPage buildPage,
        BuildingArchitectureConceptPageRegions regions,
        XPen borderPen)
    {
        PageRectMm info = regions.InformationArea;
        double x0 = info.X;
        double xRole = regions.ApprovalRoleArea.X + regions.ApprovalRoleArea.Width;
        double xApproval = regions.ApprovalNameArea.X + regions.ApprovalNameArea.Width;
        double x1 = info.X + info.Width;
        double y0 = info.Y;
        double y1 = info.Y + info.Height;
        double titleBottom = regions.SheetTitleArea.Y + regions.SheetTitleArea.Height;

        gfx.DrawLine(borderPen, Mm(x0), Mm(y1), Mm(x1), Mm(y1));
        gfx.DrawLine(borderPen, Mm(xApproval), Mm(y0), Mm(xApproval), Mm(y1));
        gfx.DrawLine(borderPen, Mm(x0), Mm(titleBottom), Mm(x1), Mm(titleBottom));

        ConceptElevationHeaderSnapshot roster = ConceptElevationHeaderResolver.Resolve(
            project.ApprovalWorkflow,
            project.PlanningTask);
        DrawElevationRoster(gfx, roster, x0, xRole, xApproval, y0, y1);

        const double paddingMm = 3.0;
        const double headingHeightMm = 5.0;
        DrawElevationHeaderLabel(
            gfx,
            "ТАЙЛБАР",
            new XRect(
                Mm(xApproval + paddingMm),
                Mm(y0 + 1.8),
                Mm(x1 - xApproval - paddingMm * 2),
                Mm(headingHeightMm)));
        string description = buildPage.Definition.ElevationDescriptionOverride
            ?? buildPage.Sheet.Entry.SheetDescription;
        DrawTopAlignedFittedText(
            gfx,
            description,
            new XRect(
                Mm(xApproval + paddingMm),
                Mm(y0 + headingHeightMm + 2.0),
                Mm(x1 - xApproval - paddingMm * 2),
                Mm(info.Height - headingHeightMm - 4.0)),
            BuildingArchitectureConceptPageLayout.CornerTextHeightMm,
            1.5,
            bold: false);
    }

    private static void DrawElevationRoster(
        XGraphics gfx,
        ConceptElevationHeaderSnapshot roster,
        double x0,
        double xRole,
        double xApproval,
        double y0,
        double y1)
    {
        IReadOnlyList<ProjectApprovalEntry> approved = roster.ApprovedBy;
        IReadOnlyList<ProjectApprovalEntry> reviewed = roster.ReviewedBy;
        const double paddingMm = 3.0;
        const double headingHeightMm = 4.5;
        const double gapMm = 1.0;
        int rowCount = Math.Max(1, approved.Count) + reviewed.Count;
        double rowsHeight = Math.Max(
            0,
            y1 - y0 - paddingMm * 2 - headingHeightMm * 2 - gapMm);
        double rowHeight = rowCount == 0 ? 0 : rowsHeight / rowCount;
        double y = y0 + paddingMm;

        DrawElevationHeaderLabel(
            gfx,
            "БАТЛАВ:",
            new XRect(Mm(x0 + paddingMm), Mm(y), Mm(xRole - x0 - paddingMm * 2), Mm(headingHeightMm)));
        y += headingHeightMm;
        foreach (ProjectApprovalEntry entry in approved)
        {
            DrawElevationRosterRow(gfx, entry, x0, xRole, xApproval, y, y + rowHeight, paddingMm);
            y += rowHeight;
        }

        y += gapMm;
        DrawElevationHeaderLabel(
            gfx,
            "ХЯНАВ:",
            new XRect(Mm(x0 + paddingMm), Mm(y), Mm(xRole - x0 - paddingMm * 2), Mm(headingHeightMm)));
        y += headingHeightMm;
        foreach (ProjectApprovalEntry entry in reviewed)
        {
            DrawElevationRosterRow(gfx, entry, x0, xRole, xApproval, y, y + rowHeight, paddingMm);
            y += rowHeight;
        }
    }

    private static void DrawElevationRosterRow(
        XGraphics gfx,
        ProjectApprovalEntry entry,
        double x0,
        double xRole,
        double xApproval,
        double y0,
        double y1,
        double paddingMm)
    {
        DrawFittedCornerText(
            gfx,
            ConceptCoverApprovalResolver.DisplayPosition(entry).ToUpperInvariant(),
            new XRect(
                Mm(x0 + paddingMm),
                Mm(y0 + 0.2),
                Mm(xRole - x0 - paddingMm * 2),
                Mm(Math.Max(0.1, y1 - y0 - 0.4))),
            false,
            XStringFormats.CenterLeft);
        DrawFittedCornerText(
            gfx,
            entry.PersonName.ToUpperInvariant(),
            new XRect(
                Mm(xRole + 1.0),
                Mm(y0 + 0.2),
                Mm(xApproval - xRole - 2.0),
                Mm(Math.Max(0.1, y1 - y0 - 0.4))),
            false,
            XStringFormats.Center);
    }

    private static void DrawElevationHeaderLabel(XGraphics gfx, string text, XRect rect)
    {
        XFont font = CreateCornerFont(
            BuildingArchitectureConceptPageLayout.CornerTextHeightMm,
            bold: true);
        gfx.DrawString(text, font, XBrushes.Black, rect, XStringFormats.CenterLeft);
    }

    private static void DrawTopAlignedFittedText(
        XGraphics gfx,
        string? text,
        XRect rect,
        double maximumPrintedHeightMm,
        double minimumPrintedHeightMm,
        bool bold)
    {
        if (string.IsNullOrWhiteSpace(text) || rect.Width <= 0 || rect.Height <= 0)
            return;

        double printedHeightMm = maximumPrintedHeightMm;
        XFont font;
        IReadOnlyList<string> lines;
        double lineHeight;
        while (true)
        {
            font = CreateCornerFont(printedHeightMm, bold);
            lines = WrapCoverText(gfx, text.Trim(), font, rect.Width);
            lineHeight = Mm(printedHeightMm * BuildingArchitectureConceptPageLayout.CornerLineHeightFactor);
            bool fits = lines.All(line => gfx.MeasureString(line, font).Width <= rect.Width + 0.01) &&
                lines.Count * lineHeight <= rect.Height + 0.01;
            if (fits || printedHeightMm <= minimumPrintedHeightMm)
                break;
            printedHeightMm = Math.Max(minimumPrintedHeightMm, printedHeightMm - 0.1);
        }

        double y = rect.Y;
        foreach (string line in lines)
        {
            gfx.DrawString(
                line,
                font,
                XBrushes.Black,
                new XRect(rect.X, y, rect.Width, lineHeight),
                XStringFormats.CenterLeft);
            y += lineHeight;
            if (y > rect.Bottom + 0.01)
                break;
        }
    }

    private static void DrawConceptCornerTable(
        XGraphics gfx,
        AlbumProject project,
        string sheetNumber,
        string scaleText,
        PageRectMm titleBlockArea,
        XPen borderPen,
        XPen finePen)
    {
        BuildingArchitectureConceptCornerGrid grid =
            BuildingArchitectureConceptPageLayout.ResolveCornerGrid(titleBlockArea);
        var x0 = grid.X0;
        var x1 = grid.X1;
        var x2 = grid.X2;
        var x3 = grid.X3;
        var x4 = grid.X4;
        var x5 = grid.X5;
        var y0 = grid.Y0;
        var y1 = grid.Y1;
        var y2 = grid.Y2;
        var y3 = grid.Y3;
        var y4 = grid.Y4;

        gfx.DrawRectangle(
            new XSolidBrush(XColor.FromArgb(254, 254, 254)),
            Mm(x0),
            Mm(y0),
            Mm(x5 - x0),
            Mm(y4 - y0));
        gfx.DrawRectangle(borderPen, Mm(x0), Mm(y0), Mm(x5 - x0), Mm(y4 - y0));
        foreach (var x in new[] { x1, x2, x3, x4 })
        {
            gfx.DrawLine(finePen, Mm(x), Mm(y0), Mm(x), Mm(y4));
        }
        foreach (var y in new[] { y1, y2, y3 })
        {
            gfx.DrawLine(finePen, Mm(x1), Mm(y), Mm(x5), Mm(y));
        }

        var company = project.Company;
        var companyName = CompanyDisplayName(company, project.DesignOrganizationName);
        var companyRepresentative = ResolveCompanyRepresentative(project);
        var architect = ResolveArchitect(project);
        var clientName = ProjectClientTypes.ResolveCoverPersonName(
            project.InitiationBasis.ClientType,
            project.InitiationBasis.ClientName,
            project.InitiationBasis.ClientRepresentativeName,
            project.ClientName);
        var companyRole = string.IsNullOrWhiteSpace(companyName)
            ? companyRepresentative.Role
            : $"\"{companyName}\" {companyRepresentative.Role}".Trim();

        DrawCompanyLogoOrMark(gfx, company, TopLeftRect(x0, y0, x1, y4));
        DrawCellText(gfx, ProjectDisplayName(project), x1, y0, x2, y1, false, XStringFormats.CenterLeft);
        DrawCellText(gfx, "Нэр", x2, y0, x3, y1, false, XStringFormats.Center);
        DrawCellText(gfx, "Гарын үсэг", x3, y0, x4, y1, false, XStringFormats.Center);
        DrawCellText(gfx, "Загвар", x4, y0, x5, y1, false, XStringFormats.Center);

        DrawCellText(gfx, companyRole, x1, y1, x2, y2, false, XStringFormats.CenterLeft);
        DrawCellText(gfx, companyRepresentative.Name, x2, y1, x3, y2, false, XStringFormats.Center);
        DrawCellText(gfx, scaleText, x4, y1, x5, y2, false, XStringFormats.Center);

        DrawCellText(gfx, "Архитектор", x1, y2, x2, y3, false, XStringFormats.CenterLeft);
        DrawCellText(gfx, architect, x2, y2, x3, y3, false, XStringFormats.Center);
        DrawCellText(gfx, $"Хуудас-{ValueOrDash(sheetNumber)}", x4, y2, x5, y3, false, XStringFormats.Center);

        DrawCellText(gfx, "Захиалагч", x1, y3, x2, y4, false, XStringFormats.CenterLeft);
        DrawCellText(gfx, ValueOrDash(clientName), x2, y3, x3, y4, false, XStringFormats.Center);
        DrawCellText(gfx, $"{DateTime.Now:yyyy} он", x4, y3, x5, y4, false, XStringFormats.Center);
    }

    private static void DrawCellText(
        XGraphics gfx,
        string? text,
        double x0Mm,
        double y0Mm,
        double x1Mm,
        double y1Mm,
        bool bold,
        XStringFormat format)
    {
        var horizontalPaddingMm = format == XStringFormats.CenterLeft ? 1.2 : 0.6;
        DrawFittedCornerText(
            gfx,
            text,
            new XRect(
                Mm(x0Mm + horizontalPaddingMm),
                Mm(y0Mm + 0.4),
                Mm(x1Mm - x0Mm - horizontalPaddingMm * 2),
                Mm(y1Mm - y0Mm - 0.8)),
            bold,
            format);
    }

    private static void DrawFittedCornerText(
        XGraphics gfx,
        string? text,
        XRect rect,
        bool bold,
        XStringFormat format)
    {
        if (string.IsNullOrWhiteSpace(text) || rect.Width <= 0 || rect.Height <= 0)
            return;

        string value = text.Trim();
        double printedHeightMm = BuildingArchitectureConceptPageLayout.CornerTextHeightMm;
        XFont font;
        IReadOnlyList<string> lines;
        double lineHeight;
        while (true)
        {
            font = CreateCornerFont(printedHeightMm, bold);
            lines = WrapCoverText(gfx, value, font, rect.Width);
            lineHeight = Mm(printedHeightMm * BuildingArchitectureConceptPageLayout.CornerLineHeightFactor);
            bool widthFits = lines.All(line => gfx.MeasureString(line, font).Width <= rect.Width + 0.01);
            bool heightFits = lines.Count * lineHeight <= rect.Height + 0.01;
            if ((widthFits && heightFits) ||
                printedHeightMm <= BuildingArchitectureConceptPageLayout.CornerMinimumTextHeightMm)
            {
                break;
            }

            printedHeightMm = Math.Max(
                BuildingArchitectureConceptPageLayout.CornerMinimumTextHeightMm,
                printedHeightMm - 0.1);
        }

        double totalHeight = lines.Count * lineHeight;
        double y = rect.Y + Math.Max(0, (rect.Height - totalHeight) * 0.5);
        foreach (string line in lines)
        {
            gfx.DrawString(
                line,
                font,
                XBrushes.Black,
                new XRect(rect.X, y, rect.Width, lineHeight),
                format);
            y += lineHeight;
        }
    }

    private static XFont CreateCornerFont(double printedTextHeightMm, bool bold) =>
        new(
            FontName,
            Mm(printedTextHeightMm / BuildingArchitectureConceptPageLayout.ArialCapHeightRatio),
            bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    private static (string Role, string Name) ResolveCompanyRepresentative(AlbumProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.Company.DesignRepresentativeName))
        {
            return (
                string.IsNullOrWhiteSpace(project.Company.DesignRepresentativeTitle)
                    ? "Захирал"
                    : project.Company.DesignRepresentativeTitle,
                project.Company.DesignRepresentativeName);
        }

        var signer = project.Company.Signers.FirstOrDefault(candidate =>
                         candidate.Role.Contains("захирал", StringComparison.OrdinalIgnoreCase))
                     ?? project.Company.Signers.FirstOrDefault();
        if (signer is not null)
        {
            return (
                string.IsNullOrWhiteSpace(signer.Role) ? "Захирал" : signer.Role,
                signer.FullName);
        }

        var administrator = project.Participants.FirstOrDefault(candidate =>
            candidate.Role.Contains("Admin", StringComparison.OrdinalIgnoreCase));
        return (administrator is null ? "Захирал" : "Зураг төслийн байгууллагын админ", administrator?.FullName ?? "");
    }

    private static string ResolveArchitect(AlbumProject project)
    {
        return AppointedArchitectResolver.ForDocument(project.Participants);
    }

    private static string CompanyDisplayName(CompanyProfile company, string fallback = "")
    {
        if (!string.IsNullOrWhiteSpace(company.DisplayName))
            return company.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(company.Name))
            return company.Name.Trim();
        return fallback.Trim();
    }

    private static string ProjectDisplayName(AlbumProject project) =>
        ValueOrDash(!string.IsNullOrWhiteSpace(project.Name) ? project.Name : project.Code);

    private static string CompanyLegalDisplayName(CompanyProfile company, string fallback = "")
    {
        string name = !string.IsNullOrWhiteSpace(company.Name)
            ? company.Name.Trim()
            : CompanyDisplayName(company, fallback);
        string legalForm = company.LegalForm?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(legalForm) ||
            name.Contains(legalForm, StringComparison.OrdinalIgnoreCase))
        {
            return name;
        }

        return string.IsNullOrWhiteSpace(name)
            ? legalForm
            : $"{name} {legalForm}";
    }

    private static string CompanyPhoneText(CompanyProfile company)
    {
        var phoneNumbers = (company.PhoneNumbers ?? [])
            .Select(value => (value ?? "").Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return phoneNumbers.Count == 0 ? company.Phone : string.Join("\n", phoneNumbers);
    }

    private static string CompanyLicenseText(CompanyProfile company)
    {
        return string.Join(" · ", new[] { company.LicenseScope, company.LicenseNumber }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim()));
    }

    private static void DrawCompanyLogoOrMark(
        XGraphics gfx,
        CompanyProfile company,
        XRect rect,
        double? printedTextHeightMm = null)
    {
        var inner = new XRect(rect.X + Mm(1.5), rect.Y + Mm(1.5), rect.Width - Mm(3), rect.Height - Mm(3));
        if (TryDrawCompanyLogo(gfx, company, inner))
        {
            return;
        }

        var mark = string.IsNullOrWhiteSpace(company.ShortName)
            ? CompanyDisplayName(company)
            : company.ShortName;
        if (printedTextHeightMm is double textHeightMm)
        {
            DrawWrappedCoverText(gfx, ValueOrDash(mark), inner, textHeightMm, true, XStringFormats.Center);
            return;
        }

        DrawFittedText(gfx, ValueOrDash(mark), inner.X, inner.Y, inner.Width, inner.Height, 8, true, XStringFormats.Center);
    }

    private static void DrawCompanyLogoOnly(
        XGraphics gfx,
        CompanyProfile company,
        XRect rect)
    {
        var inner = new XRect(
            rect.X + Mm(1.5),
            rect.Y + Mm(1.5),
            Math.Max(0, rect.Width - Mm(3)),
            Math.Max(0, rect.Height - Mm(3)));
        _ = TryDrawCompanyLogo(gfx, company, inner);
    }

    private static string ResolveAlbumAssetPath(string? projectFolder, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(projectFolder))
            return path;
        try
        {
            string root = Path.GetFullPath(projectFolder);
            string candidate = Path.GetFullPath(Path.Combine(root, path));
            return ProjectWorkspacePaths.IsInside(root, candidate) ? candidate : "";
        }
        catch
        {
            return "";
        }
    }

    private static bool TryDrawCompanyLogo(XGraphics gfx, CompanyProfile company, XRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0 || string.IsNullOrWhiteSpace(company.LogoPath) || !File.Exists(company.LogoPath))
        {
            return false;
        }

        try
        {
            company.Normalize();
            using var image = XImage.FromFile(company.LogoPath);
            double containScale = Math.Min(rect.Width / image.PointWidth, rect.Height / image.PointHeight);
            double width = image.PointWidth * containScale * company.LogoScale;
            double height = image.PointHeight * containScale * company.LogoScale;
            double x = rect.Left + (rect.Width - width) * 0.5 + company.LogoOffsetX * rect.Width * 0.5;
            double y = rect.Top + (rect.Height - height) * 0.5 + company.LogoOffsetY * rect.Height * 0.5;
            var state = gfx.Save();
            try
            {
                gfx.IntersectClip(rect);
                gfx.DrawImage(image, x, y, width, height);
            }
            finally
            {
                gfx.Restore(state);
            }
            return true;
        }
        catch
        {
            // An optional logo must never prevent the album from building.
            return false;
        }
    }

    private static XRect TopLeftRect(double x0Mm, double y0Mm, double x1Mm, double y1Mm) =>
        new(Mm(x0Mm), Mm(y0Mm), Mm(x1Mm - x0Mm), Mm(y1Mm - y0Mm));

    private static void DrawSheetTitle(
        XGraphics gfx,
        XRect rect,
        AlbumBuildPage buildPage,
        XPen borderPen)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var numberWidth = Math.Min(rect.Width * 0.28, Mm(48));
        gfx.DrawLine(borderPen, rect.Right - numberWidth, rect.Top, rect.Right - numberWidth, rect.Bottom);
        var padding = Math.Max(2, Math.Min(Mm(2), rect.Height * 0.12));
        DrawFittedText(
            gfx,
            buildPage.Title,
            rect.Left + padding,
            rect.Top + padding,
            rect.Width - numberWidth - padding * 2,
            rect.Height - padding * 2,
            9,
            true);
        DrawFittedText(
            gfx,
            buildPage.Number,
            rect.Right - numberWidth + padding,
            rect.Top + padding,
            numberWidth - padding * 2,
            rect.Height - padding * 2,
            10,
            true,
            XStringFormats.Center);
    }

    private static void DrawGridMarks(XGraphics gfx, XRect rect, XPen pen)
    {
        var step = Mm(50);
        for (var x = rect.Left + step; x < rect.Right; x += step)
        {
            gfx.DrawLine(pen, x, rect.Top, x, rect.Top + Mm(2.5));
            gfx.DrawLine(pen, x, rect.Bottom - Mm(2.5), x, rect.Bottom);
        }

        for (var y = rect.Top + step; y < rect.Bottom; y += step)
        {
            gfx.DrawLine(pen, rect.Left, y, rect.Left + Mm(2.5), y);
            gfx.DrawLine(pen, rect.Right - Mm(2.5), y, rect.Right, y);
        }
    }

    private static void DrawTitleBlock(
        XGraphics gfx,
        XRect rect,
        AlbumProject project,
        AlbumBuildPage buildPage,
        XPen borderPen,
        XPen finePen)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        var firstLine = rect.Top + rect.Height * 0.34;
        var secondLine = rect.Top + rect.Height * 0.66;
        gfx.DrawLine(finePen, rect.Left, firstLine, rect.Right, firstLine);
        gfx.DrawLine(finePen, rect.Left, secondLine, rect.Right, secondLine);

        var bottomLeftWidth = rect.Width * 0.46;
        var bottomRightWidth = rect.Width * 0.28;
        gfx.DrawLine(borderPen, rect.Left + bottomLeftWidth, secondLine, rect.Left + bottomLeftWidth, rect.Bottom);
        gfx.DrawLine(borderPen, rect.Right - bottomRightWidth, secondLine, rect.Right - bottomRightWidth, rect.Bottom);

        var padding = Math.Max(2, Math.Min(Mm(2), rect.Height * 0.08));
        var projectName = ProjectDisplayName(project);
        var companyName = CompanyDisplayName(project.Company, project.DesignOrganizationName);

        DrawFittedText(gfx, companyName, rect.Left + padding, rect.Top + padding,
            rect.Width - padding * 2, firstLine - rect.Top - padding * 2, 9, true);
        DrawFittedText(gfx, projectName, rect.Left + padding, firstLine + padding,
            rect.Width - padding * 2, secondLine - firstLine - padding * 2, 8, false);
        DrawFittedText(gfx, project.Code, rect.Left + padding, secondLine + padding,
            bottomLeftWidth - padding * 2, rect.Bottom - secondLine - padding * 2, 7, false);
        DrawFittedText(gfx, buildPage.Number, rect.Left + bottomLeftWidth + padding, secondLine + padding,
            rect.Width - bottomLeftWidth - bottomRightWidth - padding * 2,
            rect.Bottom - secondLine - padding * 2, 9, true);
        DrawFittedText(gfx,
            string.IsNullOrWhiteSpace(buildPage.Sheet.Entry.Revision) ? "R0" : buildPage.Sheet.Entry.Revision,
            rect.Right - bottomRightWidth + padding,
            secondLine + padding,
            bottomRightWidth - padding * 2,
            rect.Bottom - secondLine - padding * 2,
            7,
            false);

        if (rect.Width > rect.Height * 3)
        {
            DrawFittedText(gfx, buildPage.Title, rect.Left + rect.Width * 0.52, firstLine + padding,
                rect.Width * 0.46 - padding * 2, secondLine - firstLine - padding * 2, 8, true);
        }
    }

    private static void DrawFittedText(
        XGraphics gfx,
        string? text,
        double x,
        double y,
        double width,
        double height,
        double preferredSize,
        bool bold,
        XStringFormat? format = null)
    {
        if (string.IsNullOrWhiteSpace(text) || width <= 2 || height <= 2)
        {
            return;
        }

        var size = preferredSize;
        XFont font;
        do
        {
            font = new XFont(FontName, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
            if (gfx.MeasureString(text, font).Width <= width || size <= 5.5)
            {
                break;
            }

            size -= 0.5;
        }
        while (true);

        gfx.DrawString(
            text,
            font,
            XBrushes.Black,
            new XRect(x, y, width, height),
            format ?? XStringFormats.CenterLeft);
    }

    private static XRect ToPoints(PageRectMm rect) =>
        new(Mm(rect.X), Mm(rect.Y), Mm(rect.Width), Mm(rect.Height));

    private static double Mm(double value) => value * PointsPerMillimeter;

    private static void DrawGeneratedPage(
        PdfDocument document,
        AlbumBuildRequest request,
        ConceptGeneratedPagePlan plan)
    {
        switch (plan.Component.GeneratedPageKind)
        {
            case AlbumGeneratedPageKind.Cover:
                DrawConceptCoverPage(document, request, plan.Component);
                break;
            case AlbumGeneratedPageKind.DesignOrganization:
                DrawDesignOrganizationPage(document, request, plan);
                break;
            case AlbumGeneratedPageKind.PlanningTask:
                DrawPlanningTaskPage(document, request, plan);
                break;
            case AlbumGeneratedPageKind.SiteContext:
                DrawSiteContextPage(document, request, plan);
                break;
        }
    }

    private static PdfPage AddA3LandscapePage(PdfDocument document)
    {
        var page = document.AddPage();
        page.Width = XUnit.FromMillimeter(420);
        page.Height = XUnit.FromMillimeter(297);
        return page;
    }

    private static void DrawBuildingSubCoverPage(
        PdfDocument document,
        AlbumProject project,
        string buildingName)
    {
        PdfPage page = AddA3LandscapePage(document);
        using var gfx = XGraphics.FromPdfPage(page);
        var border = new XPen(XColors.Black, Mm(0.25));
        CompanyProfile company = project.Company.Clone();
        company.LogoPath = ResolveAlbumAssetPath(project.ProjectFolder, company.LogoPath);
        string companyName = CompanyLegalDisplayName(company, project.DesignOrganizationName);

        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        gfx.DrawRectangle(border, ToPoints(BuildingArchitectureConceptPageLayout.Frame));

        DrawCompanyLogoOnly(
            gfx,
            company,
            CoverCenteredRect(210.0, 245.0, 58.0, 42.0));
        DrawCoverText(
            gfx,
            companyName,
            CoverCenteredRect(210.0, 216.0, 220.0, 10.0),
            2.5,
            true,
            XStringFormats.Center);
        DrawCoverText(
            gfx,
            project.InitiationBasis.SiteAddress,
            CoverCenteredRect(210.0, 183.0, 250.0, 10.0),
            2.5,
            false,
            XStringFormats.Center);
        DrawCoverText(
            gfx,
            ProjectDisplayName(project),
            CoverCenteredRect(210.0, 164.0, 270.0, 16.0),
            4.0,
            false,
            XStringFormats.Center);
        DrawCoverText(
            gfx,
            "/ БАРИЛГЫН ЗУРАГ /",
            CoverCenteredRect(210.0, 143.0, 130.0, 8.0),
            2.5,
            false,
            XStringFormats.Center);
        DrawCoverText(
            gfx,
            buildingName,
            CoverCenteredRect(210.0, 116.0, 285.0, 22.0),
            6.0,
            true,
            XStringFormats.Center);
        DrawCoverText(
            gfx,
            $"{DateTime.Now:yyyy} ОН",
            CoverCenteredRect(210.0, 18.0, 90.0, 10.0),
            2.5,
            false,
            XStringFormats.Center);
    }

    private const double CoverTableLeftMm = BuildingArchitectureConceptPageLayout.CoverTableLeftMm;
    private const double CoverReviewRoleRightMm = BuildingArchitectureConceptPageLayout.CoverReviewRoleRightMm;
    private const double CoverReviewNameRightMm = BuildingArchitectureConceptPageLayout.CoverReviewNameRightMm;
    private const double CoverReviewRightMm = BuildingArchitectureConceptPageLayout.CoverProcessedLeftMm;
    private const double CoverCompanyRoleLeftMm = BuildingArchitectureConceptPageLayout.CoverProcessedLogoRightMm;
    private const double CoverCompanyRoleRightMm = BuildingArchitectureConceptPageLayout.CoverProcessedRoleRightMm;
    private const double CoverCompanyNameRightMm = BuildingArchitectureConceptPageLayout.CoverProcessedNameRightMm;
    private const double CoverTableRightMm = BuildingArchitectureConceptPageLayout.CoverTableRightMm;

    private static void DrawConceptCoverPage(
        PdfDocument document,
        AlbumBuildRequest request,
        AlbumCompositionItem item)
    {
        var page = AddA3LandscapePage(document);
        using var gfx = XGraphics.FromPdfPage(page);
        var border = new XPen(XColors.Black, Mm(0.25));
        var fine = new XPen(XColors.Black, Mm(0.10));
        var company = request.Project.Company;
        ConceptCoverApprovalSnapshot approvalSnapshot = ConceptCoverApprovalResolver.Resolve(
            request.Project.ApprovalWorkflow,
            request.Project.PlanningTask);
        var companyRepresentative = ResolveCompanyRepresentative(request.Project);
        var companyName = CompanyDisplayName(company, request.Project.DesignOrganizationName);
        var companyRole = string.IsNullOrWhiteSpace(companyName)
            ? companyRepresentative.Role
            : $"\"{companyName}\" {companyRepresentative.Role}".Trim();
        ProjectInitiationBasis initiationBasis = request.Project.InitiationBasis;
        var canonicalClientName = string.IsNullOrWhiteSpace(initiationBasis.ClientName)
            ? request.Project.ClientName
            : initiationBasis.ClientName;
        string clientType = ProjectClientTypes.Normalize(initiationBasis.ClientType);
        string clientRole = ProjectClientTypes.ResolveCoverRole(
            clientType,
            canonicalClientName,
            initiationBasis.ClientRepresentativePosition);
        string clientRepresentativeName = ProjectClientTypes.ResolveCoverPersonName(
            clientType,
            canonicalClientName,
            initiationBasis.ClientRepresentativeName,
            request.Project.ClientName);
        CompanyProfile clientOrganization = (initiationBasis.ClientOrganizationSnapshot ?? new CompanyProfile()).Clone();
        clientOrganization.Name = canonicalClientName;
        clientOrganization.DisplayName = canonicalClientName;
        clientOrganization.LogoPath = ResolveAlbumAssetPath(
            request.Project.ProjectFolder,
            clientOrganization.LogoPath);
        const double bodyTextHeightMm = BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm;
        const double projectNameTextHeightMm = BuildingArchitectureConceptPageLayout.CoverProjectNameTextHeightMm;
        IReadOnlyList<CoverApprovedRow> approvedRows = BuildCoverApprovedRows(gfx, approvalSnapshot.ApprovedBy);
        var reviewRows = BuildCoverReviewRows(gfx, approvalSnapshot.EndorsedBy);
        var processedColumn = BuildCoverProcessedColumn(
            gfx,
            companyRole,
            companyRepresentative.Name,
            clientRole,
            clientRepresentativeName);
        var reviewTableBottomMm = reviewRows.Count == 0 ? 93.86 : reviewRows[^1].BottomMm;
        var tableBottomMm = Math.Min(reviewTableBottomMm, processedColumn.BottomMm);

        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        gfx.DrawRectangle(border, ToPoints(BuildingArchitectureConceptPageLayout.Frame));

        DrawCoverText(gfx, "БАТЛАВ:", CoverCenteredRect(210.0, 281.205, 50.0, 8.0), bodyTextHeightMm, false, XStringFormats.Center);
        foreach (CoverApprovedRow row in approvedRows)
        {
            DrawCoverText(
                gfx,
                ConceptCoverApprovalResolver.DisplayPosition(row.Entry).ToUpperInvariant(),
                CoverRect(105.8, row.BottomMm, 225.8, row.TopMm),
                bodyTextHeightMm,
                false,
                XStringFormats.CenterLeft);
            DrawCoverText(
                gfx,
                row.Entry.PersonName.ToUpperInvariant(),
                CoverRect(277.4, row.BottomMm, 352.4, row.TopMm),
                bodyTextHeightMm,
                false,
                XStringFormats.CenterLeft);
        }

        DrawCoverText(
            gfx,
            ValueOrDash(request.Project.InitiationBasis.SiteAddress),
            CoverCenteredRect(210.0, 220.510, 180.0, 8.0),
            bodyTextHeightMm,
            false,
            XStringFormats.Center);
        DrawCoverText(
            gfx,
            ProjectDisplayName(request.Project),
            CoverCenteredRect(210.0, 207.510, 220.0, 12.0),
            projectNameTextHeightMm,
            false,
            XStringFormats.Center);
        DrawCoverText(
            gfx,
            "/ЗАГВАР ЗУРАГ/",
            CoverCenteredRect(210.0, 186.760, 110.0, 8.0),
            bodyTextHeightMm,
            false,
            XStringFormats.Center);

        DrawCoverText(
            gfx,
            "ЗӨВШӨӨРӨЛЦСӨН:",
            CoverRect(68.275, 162.36, 196.275, 168.86),
            bodyTextHeightMm,
            false,
            XStringFormats.CenterLeft);
        DrawCoverText(
            gfx,
            "БОЛОВСРУУЛСАН:",
            CoverRect(196.275, 162.36, 351.725, 168.86),
            bodyTextHeightMm,
            false,
            XStringFormats.CenterLeft);
        DrawSketchCoverApprovalTable(gfx, border, fine, reviewRows, processedColumn, tableBottomMm);

        DrawCoverCellText(gfx, "Албан тушаал", CoverTableLeftMm, 153.86, CoverReviewRoleRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Нэр", CoverReviewRoleRightMm, 153.86, CoverReviewNameRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Гарын үсэг", CoverReviewNameRightMm, 153.86, CoverReviewRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Албан тушаал", CoverCompanyRoleLeftMm, 153.86, CoverCompanyRoleRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Нэр", CoverCompanyRoleRightMm, 153.86, CoverCompanyNameRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Гарын үсэг", CoverCompanyNameRightMm, 153.86, CoverTableRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);

        foreach (var row in reviewRows)
        {
            DrawCoverCellText(gfx, ConceptCoverApprovalResolver.DisplayPosition(row.Entry), CoverTableLeftMm, row.BottomMm, CoverReviewRoleRightMm, row.TopMm, bodyTextHeightMm, false, XStringFormats.CenterLeft, 2.0);
            DrawCoverCellText(gfx, row.Entry.PersonName, CoverReviewRoleRightMm, row.BottomMm, CoverReviewNameRightMm, row.TopMm, bodyTextHeightMm, false, XStringFormats.Center);
        }

        DrawCoverCellText(gfx, BuildingArchitectureConceptPageLayout.CoverProcessedTopSectionTitle, CoverReviewRightMm, processedColumn.TopHeaderBottomMm, CoverCompanyRoleLeftMm, BuildingArchitectureConceptPageLayout.CoverTableTopMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCompanyLogoOrMark(gfx, company, CoverRect(CoverReviewRightMm, processedColumn.TopDataBottomMm, CoverCompanyRoleLeftMm, processedColumn.TopHeaderBottomMm), bodyTextHeightMm);
        DrawCoverCellText(gfx, companyRole, CoverCompanyRoleLeftMm, processedColumn.TopDataBottomMm, CoverCompanyRoleRightMm, processedColumn.TopHeaderBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, companyRepresentative.Name, CoverCompanyRoleRightMm, processedColumn.TopDataBottomMm, CoverCompanyNameRightMm, processedColumn.TopHeaderBottomMm, bodyTextHeightMm, false, XStringFormats.Center);

        DrawCoverCellText(gfx, BuildingArchitectureConceptPageLayout.CoverProcessedBottomSectionTitle, CoverReviewRightMm, processedColumn.BottomHeaderBottomMm, CoverCompanyRoleLeftMm, processedColumn.TopDataBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Албан тушаал", CoverCompanyRoleLeftMm, processedColumn.BottomHeaderBottomMm, CoverCompanyRoleRightMm, processedColumn.TopDataBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Нэр", CoverCompanyRoleRightMm, processedColumn.BottomHeaderBottomMm, CoverCompanyNameRightMm, processedColumn.TopDataBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Гарын үсэг", CoverCompanyNameRightMm, processedColumn.BottomHeaderBottomMm, CoverTableRightMm, processedColumn.TopDataBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        if (ProjectClientTypes.UsesLogo(clientType))
        {
            DrawCompanyLogoOnly(
                gfx,
                clientOrganization,
                CoverRect(
                    CoverReviewRightMm,
                    tableBottomMm,
                    CoverCompanyRoleLeftMm,
                    processedColumn.BottomHeaderBottomMm));
        }
        DrawCoverCellText(gfx, clientRole, CoverCompanyRoleLeftMm, tableBottomMm, CoverCompanyRoleRightMm, processedColumn.BottomHeaderBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, ValueOrDash(clientRepresentativeName), CoverCompanyRoleRightMm, tableBottomMm, CoverCompanyNameRightMm, processedColumn.BottomHeaderBottomMm, bodyTextHeightMm, false, XStringFormats.Center);

        DrawCoverText(gfx, "Улаанбаатар хот", CoverCenteredRect(210.0, 26.125, 200.0, 12.0), bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverText(gfx, $"{DateTime.Now:yyyy} он", CoverCenteredRect(210.0, 15.625, 90.0, 12.0), bodyTextHeightMm, false, XStringFormats.Center);
    }

    private static void DrawSketchCoverApprovalTable(
        XGraphics gfx,
        XPen border,
        XPen fine,
        IReadOnlyList<CoverReviewRow> reviewRows,
        CoverProcessedColumn processedColumn,
        double tableBottomMm)
    {
        const double x0 = CoverTableLeftMm;
        var y0 = tableBottomMm;
        const double x1 = CoverTableRightMm;
        const double y1 = BuildingArchitectureConceptPageLayout.CoverTableTopMm;
        const double rightX0 = CoverReviewRightMm;
        const double headerY0 = BuildingArchitectureConceptPageLayout.CoverColumnHeaderBottomMm;

        DrawCoverLine(gfx, border, x0, y0, x1, y0);
        DrawCoverLine(gfx, border, x0, y1, x1, y1);
        DrawCoverLine(gfx, border, x0, y0, x0, y1);
        DrawCoverLine(gfx, border, x1, y0, x1, y1);
        DrawCoverLine(gfx, border, x0, headerY0, x1, headerY0);
        DrawCoverLine(gfx, border, rightX0, y0, rightX0, y1);

        DrawCoverLine(gfx, fine, CoverReviewRoleRightMm, y0, CoverReviewRoleRightMm, y1);
        DrawCoverLine(gfx, fine, CoverReviewNameRightMm, y0, CoverReviewNameRightMm, y1);
        DrawCoverLine(gfx, fine, CoverCompanyRoleLeftMm, processedColumn.TopDataBottomMm, CoverCompanyRoleLeftMm, y1);
        DrawCoverLine(gfx, fine, CoverCompanyRoleRightMm, processedColumn.TopDataBottomMm, CoverCompanyRoleRightMm, y1);
        DrawCoverLine(gfx, fine, CoverCompanyNameRightMm, processedColumn.TopDataBottomMm, CoverCompanyNameRightMm, y1);
        DrawCoverLine(gfx, fine, CoverCompanyRoleLeftMm, y0, CoverCompanyRoleLeftMm, processedColumn.TopDataBottomMm);
        DrawCoverLine(gfx, fine, CoverCompanyRoleRightMm, y0, CoverCompanyRoleRightMm, processedColumn.TopDataBottomMm);
        DrawCoverLine(gfx, fine, CoverCompanyNameRightMm, y0, CoverCompanyNameRightMm, processedColumn.TopDataBottomMm);

        DrawCoverLine(gfx, fine, rightX0, processedColumn.TopDataBottomMm, x1, processedColumn.TopDataBottomMm);
        DrawCoverLine(gfx, fine, rightX0, processedColumn.BottomHeaderBottomMm, x1, processedColumn.BottomHeaderBottomMm);

        for (var index = 0; index < reviewRows.Count - 1; index++)
        {
            DrawCoverLine(gfx, fine, x0, reviewRows[index].BottomMm, rightX0, reviewRows[index].BottomMm);
        }
    }

    private static void DrawCoverLine(
        XGraphics gfx,
        XPen pen,
        double x0Mm,
        double y0Mm,
        double x1Mm,
        double y1Mm)
    {
        gfx.DrawLine(
            pen,
            Mm(x0Mm),
            Mm(BuildingArchitectureConceptPageLayout.PageHeightMm - y0Mm),
            Mm(x1Mm),
            Mm(BuildingArchitectureConceptPageLayout.PageHeightMm - y1Mm));
    }

    private static void DrawCoverCellText(
        XGraphics gfx,
        string? text,
        double x0Mm,
        double y0Mm,
        double x1Mm,
        double y1Mm,
        double printedTextHeightMm,
        bool bold,
        XStringFormat format,
        double horizontalInsetMm = 1.2)
    {
        var rect = CoverRect(x0Mm, y0Mm, x1Mm, y1Mm);
        DrawWrappedCoverText(
            gfx,
            text,
            new XRect(
                rect.X + Mm(horizontalInsetMm),
                rect.Y + Mm(0.6),
                rect.Width - Mm(horizontalInsetMm * 2),
                rect.Height - Mm(1.2)),
            printedTextHeightMm,
            bold,
            format);
    }

    private static void DrawCoverText(
        XGraphics gfx,
        string? text,
        XRect rect,
        double printedTextHeightMm,
        bool bold,
        XStringFormat format) =>
        DrawWrappedCoverText(gfx, text, rect, printedTextHeightMm, bold, format);

    private static void DrawWrappedCoverText(
        XGraphics gfx,
        string? text,
        XRect rect,
        double printedTextHeightMm,
        bool bold,
        XStringFormat format)
    {
        if (string.IsNullOrWhiteSpace(text) || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        (XFont font, double fittedTextHeightMm) = CreateCoverFontToFitLongestWord(
            gfx,
            text,
            rect.Width,
            printedTextHeightMm,
            bold);
        var lines = WrapCoverText(gfx, text, font, rect.Width);
        var lineHeight = Mm(CoverLineHeightMm(fittedTextHeightMm));
        var totalHeight = lines.Count * lineHeight;
        var y = rect.Y + Math.Max(0, (rect.Height - totalHeight) * 0.5);
        foreach (var line in lines)
        {
            gfx.DrawString(
                line,
                font,
                XBrushes.Black,
                new XRect(rect.X, y, rect.Width, lineHeight),
                format);
            y += lineHeight;
        }
    }

    private static XFont CreateCoverFont(double printedTextHeightMm, bool bold) =>
        new(
            FontName,
            Mm(CoverFontEmSizeMm(printedTextHeightMm)),
            bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    private static (XFont Font, double PrintedTextHeightMm) CreateCoverFontToFitLongestWord(
        XGraphics gfx,
        string text,
        double maxWidth,
        double printedTextHeightMm,
        bool bold)
    {
        XFont font = CreateCoverFont(printedTextHeightMm, bold);
        string longestWord = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split(new[] { ' ', '\t', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .OrderByDescending(word => gfx.MeasureString(word, font).Width)
            .FirstOrDefault() ?? "";
        if (longestWord.Length == 0)
        {
            return (font, printedTextHeightMm);
        }

        double measuredWidth = gfx.MeasureString(longestWord, font).Width;
        if (measuredWidth <= maxWidth)
        {
            return (font, printedTextHeightMm);
        }

        double fittedTextHeightMm = Math.Max(
            1.5,
            printedTextHeightMm * maxWidth / measuredWidth * 0.98);
        return (CreateCoverFont(fittedTextHeightMm, bold), fittedTextHeightMm);
    }

    private static double CoverFontEmSizeMm(double printedTextHeightMm) =>
        printedTextHeightMm / BuildingArchitectureConceptPageLayout.ArialCapHeightRatio;

    private static double CoverLineHeightMm(double printedTextHeightMm) =>
        printedTextHeightMm * BuildingArchitectureConceptPageLayout.CoverLineHeightFactor;

    private static IReadOnlyList<string> WrapCoverText(
        XGraphics gfx,
        string text,
        XFont font,
        double maxWidth)
    {
        var lines = new List<string>();
        var paragraphs = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');

        foreach (var paragraph in paragraphs)
        {
            var words = paragraph.Split(
                new[] { ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (words.Length == 0)
            {
                lines.Add("");
                continue;
            }

            var current = "";
            foreach (var word in words)
            {
                var candidate = current.Length == 0 ? word : $"{current} {word}";
                if (gfx.MeasureString(candidate, font).Width <= maxWidth)
                {
                    current = candidate;
                    continue;
                }

                if (current.Length > 0)
                {
                    lines.Add(current);
                    current = "";
                }

                if (gfx.MeasureString(word, font).Width <= maxWidth)
                {
                    current = word;
                    continue;
                }

                // Names and formal titles must remain whole; the drawing font is
                // fitted to the longest token before wrapping.
                current = word;
            }

            if (current.Length > 0)
            {
                lines.Add(current);
            }
        }

        return lines;
    }

    private static double MeasureCoverTextHeightMm(
        XGraphics gfx,
        string? text,
        double widthMm,
        double printedTextHeightMm)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        (XFont font, double fittedTextHeightMm) = CreateCoverFontToFitLongestWord(
            gfx,
            text,
            Mm(widthMm),
            printedTextHeightMm,
            false);
        var lineCount = WrapCoverText(gfx, text, font, Mm(widthMm)).Count;
        return lineCount * CoverLineHeightMm(fittedTextHeightMm);
    }

    private static IReadOnlyList<CoverApprovedRow> BuildCoverApprovedRows(
        XGraphics gfx,
        IReadOnlyList<ProjectApprovalEntry> approvals)
    {
        const double rowsTopMm = 262.205;
        const double roleTextWidthMm = 120.0;
        const double nameTextWidthMm = 75.0;
        const double cellVerticalPaddingMm = 1.2;
        const double bodyTextHeightMm = BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm;
        var rows = new List<CoverApprovedRow>(approvals.Count);
        var topMm = rowsTopMm;

        foreach (ProjectApprovalEntry entry in approvals)
        {
            string role = ConceptCoverApprovalResolver.DisplayPosition(entry).ToUpperInvariant();
            string name = entry.PersonName.ToUpperInvariant();
            double roleHeightMm = MeasureCoverTextHeightMm(
                gfx,
                role,
                roleTextWidthMm,
                bodyTextHeightMm);
            double nameHeightMm = MeasureCoverTextHeightMm(
                gfx,
                name,
                nameTextWidthMm,
                bodyTextHeightMm);
            double rowHeightMm = Math.Max(
                8.0,
                Math.Max(roleHeightMm, nameHeightMm) + cellVerticalPaddingMm);
            double bottomMm = topMm - rowHeightMm;
            rows.Add(new CoverApprovedRow(entry, bottomMm, topMm));
            topMm = bottomMm;
        }

        return rows;
    }

    private static IReadOnlyList<CoverReviewRow> BuildCoverReviewRows(
        XGraphics gfx,
        IReadOnlyList<ProjectApprovalEntry> approvals)
    {
        const double rowsTopMm = BuildingArchitectureConceptPageLayout.CoverColumnHeaderBottomMm;
        const double baseRowsHeightMm = BuildingArchitectureConceptPageLayout.CoverReviewRowsBaseHeightMm;
        const double roleTextWidthMm = CoverReviewRoleRightMm - CoverTableLeftMm - 2.4;
        const double nameTextWidthMm = CoverReviewNameRightMm - CoverReviewRoleRightMm - 2.4;
        const double cellVerticalPaddingMm = 1.2;
        const double bodyTextHeightMm = BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm;
        var baseRowHeightMm = baseRowsHeightMm / Math.Max(1, approvals.Count);
        var rows = new List<CoverReviewRow>(approvals.Count);
        var topMm = rowsTopMm;

        foreach (ProjectApprovalEntry entry in approvals)
        {
            var roleHeightMm = MeasureCoverTextHeightMm(
                gfx,
                ConceptCoverApprovalResolver.DisplayPosition(entry),
                roleTextWidthMm,
                bodyTextHeightMm);
            var nameHeightMm = MeasureCoverTextHeightMm(
                gfx,
                entry.PersonName,
                nameTextWidthMm,
                bodyTextHeightMm);
            var requiredHeightMm = Math.Max(roleHeightMm, nameHeightMm) + cellVerticalPaddingMm;
            var rowHeightMm = Math.Max(baseRowHeightMm, requiredHeightMm);
            var bottomMm = topMm - rowHeightMm;
            rows.Add(new CoverReviewRow(entry, bottomMm, topMm));
            topMm = bottomMm;
        }

        return rows;
    }

    private static CoverProcessedColumn BuildCoverProcessedColumn(
        XGraphics gfx,
        string companyRole,
        string companyRepresentativeName,
        string clientTypeLabel,
        string clientName)
    {
        const double topHeaderBottomMm = BuildingArchitectureConceptPageLayout.CoverColumnHeaderBottomMm;
        const double titleHeightMm = BuildingArchitectureConceptPageLayout.CoverSectionHeaderHeightMm;
        const double baseClientDataHeightMm = BuildingArchitectureConceptPageLayout.CoverClientDataBaseHeightMm;
        const double baseCompanyDataHeightMm = BuildingArchitectureConceptPageLayout.CoverCompanyDataBaseHeightMm;
        const double roleTextWidthMm = CoverCompanyRoleRightMm - CoverCompanyRoleLeftMm - 2.4;
        const double nameTextWidthMm = CoverCompanyNameRightMm - CoverCompanyRoleRightMm - 2.4;
        const double cellVerticalPaddingMm = 1.2;
        const double bodyTextHeightMm = BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm;

        var clientRoleHeightMm = MeasureCoverTextHeightMm(
            gfx,
            clientTypeLabel,
            roleTextWidthMm,
            bodyTextHeightMm);
        var clientNameHeightMm = MeasureCoverTextHeightMm(
            gfx,
            clientName,
            nameTextWidthMm,
            bodyTextHeightMm);
        var companyRoleHeightMm = MeasureCoverTextHeightMm(
            gfx,
            companyRole,
            roleTextWidthMm,
            bodyTextHeightMm);
        var companyNameHeightMm = MeasureCoverTextHeightMm(
            gfx,
            companyRepresentativeName,
            nameTextWidthMm,
            bodyTextHeightMm);
        var sharedDataHeightMm = Math.Max(
            Math.Max(baseClientDataHeightMm, baseCompanyDataHeightMm),
            Math.Max(
                Math.Max(clientRoleHeightMm, clientNameHeightMm),
                Math.Max(companyRoleHeightMm, companyNameHeightMm)) + cellVerticalPaddingMm);
        var topDataBottomMm = topHeaderBottomMm - sharedDataHeightMm;
        var bottomHeaderBottomMm = topDataBottomMm - titleHeightMm;
        var bottomMm = bottomHeaderBottomMm - sharedDataHeightMm;
        return new CoverProcessedColumn(
            topHeaderBottomMm,
            topDataBottomMm,
            bottomHeaderBottomMm,
            bottomMm);
    }

    private static XRect CoverCenteredRect(double centerXMm, double centerYMm, double widthMm, double heightMm) =>
        ToPoints(BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(centerXMm, centerYMm, widthMm, heightMm));

    private static XRect CoverRect(double x0Mm, double y0Mm, double x1Mm, double y1Mm) =>
        ToPoints(BuildingArchitectureConceptPageLayout.FromBottomLeft(x0Mm, y0Mm, x1Mm, y1Mm));

    private sealed record CoverApprovedRow(ProjectApprovalEntry Entry, double BottomMm, double TopMm);

    private sealed record CoverReviewRow(ProjectApprovalEntry Entry, double BottomMm, double TopMm);

    private sealed record CoverProcessedColumn(
        double TopHeaderBottomMm,
        double TopDataBottomMm,
        double BottomHeaderBottomMm,
        double BottomMm);

    private static void DrawDesignOrganizationPage(
        PdfDocument document,
        AlbumBuildRequest request,
        ConceptGeneratedPagePlan plan)
    {
        var page = AddA3LandscapePage(document);
        using var gfx = XGraphics.FromPdfPage(page);
        DrawGeneratedPageChrome(gfx, page, request.Project, plan.Title, plan.Number);
        DrawGeneratedDocumentContent(gfx, request.Project, plan);
    }

    private static void DrawPlanningTaskPage(
        PdfDocument document,
        AlbumBuildRequest request,
        ConceptGeneratedPagePlan plan)
    {
        var page = AddA3LandscapePage(document);
        using var gfx = XGraphics.FromPdfPage(page);
        DrawGeneratedPageChrome(gfx, page, request.Project, plan.Title, plan.Number);

        if (plan.DocumentPages.Count > 0)
        {
            DrawGeneratedDocumentContent(gfx, request.Project, plan);
            return;
        }

        var border = new XPen(XColors.Black, 0.55);
        var muted = new XSolidBrush(XColor.FromArgb(92, 101, 112));
        var basis = request.Project.InitiationBasis;
        var task = request.Project.PlanningTask;

        gfx.DrawRectangle(border, Mm(20), Mm(28), Mm(185), Mm(135));
        DrawInfoRow(gfx, "АТД ОЛГОСОН БАЙГУУЛЛАГА", ValueOrDash(task.IssuingAuthorityName), 25, 35, 175, 19);
        DrawInfoRow(gfx, "АТД ДУГААР", ValueOrDash(task.AtdNumber), 25, 56, 175, 17);
        DrawInfoRow(gfx, "ОЛГОСОН ОГНОО", FormatDate(task.IssuedAtUtc), 25, 75, 175, 17);
        DrawInfoRow(gfx, "ТӨЛӨВ", ValueOrDash(task.Status), 25, 94, 175, 17);
        DrawInfoRow(
            gfx,
            "ЗАХИАЛАГЧ",
            ValueOrDash(ProjectClientTypes.ResolveCoverPersonName(
                basis.ClientType,
                basis.ClientName,
                basis.ClientRepresentativeName,
                request.Project.ClientName)),
            25,
            113,
            175,
            17);
        DrawInfoRow(gfx, "ТӨСЛИЙН БАЙРШИЛ", ValueOrDash(basis.SiteAddress), 25, 132, 175, 24);

        gfx.DrawRectangle(border, Mm(212), Mm(28), Mm(198), Mm(135));
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(239, 242, 246)), Mm(212), Mm(28), Mm(198), Mm(15));
        DrawFittedText(gfx, "ЗӨВШӨӨРӨЛЦӨХ, БАТЛАХ ЭРХТЭЙ ОРОЛЦОГЧИД", Mm(217), Mm(28), Mm(188), Mm(15), 10, true, XStringFormats.CenterLeft);
        var authorityY = 43d;
        foreach (var member in task.AuthorityMembers.Take(7))
        {
            var roles = string.Join(", ", member.Roles);
            gfx.DrawRectangle(border, Mm(212), Mm(authorityY), Mm(198), Mm(16));
            gfx.DrawLine(border, Mm(290), Mm(authorityY), Mm(290), Mm(authorityY + 16));
            DrawFittedText(gfx, ValueOrDash(roles), Mm(216), Mm(authorityY + 2), Mm(70), Mm(12), 8, false);
            DrawFittedText(gfx, ValueOrDash(member.FullName), Mm(294), Mm(authorityY + 2), Mm(112), Mm(12), 9, true);
            authorityY += 16;
        }

        gfx.DrawRectangle(border, Mm(20), Mm(176), Mm(390), Mm(77));
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(239, 242, 246)), Mm(20), Mm(176), Mm(390), Mm(14));
        DrawFittedText(gfx, "ТӨЛӨВЛӨЛТИЙН ДААЛГАВРЫН ТОВЧ МЭДЭЭЛЭЛ, ШААРДЛАГА", Mm(25), Mm(176), Mm(380), Mm(14), 10, true, XStringFormats.CenterLeft);
        var summary = string.IsNullOrWhiteSpace(task.Summary) ? basis.Summary : task.Summary;
        var requirements = task.Requirements.Count == 0
            ? ""
            : string.Join("\n", task.Requirements.Select(requirement => $"- {requirement}"));
        DrawWrappedText(
            gfx,
            string.Join("\n", new[] { summary, requirements }.Where(value => !string.IsNullOrWhiteSpace(value))),
            new XFont(FontName, 9),
            XBrushes.Black,
            new XRect(Mm(27), Mm(196), Mm(376), Mm(49)),
            Mm(5));
    }

    private static void DrawSiteContextPage(
        PdfDocument document,
        AlbumBuildRequest request,
        ConceptGeneratedPagePlan plan)
    {
        PdfPage page = AddA3LandscapePage(document);
        using XGraphics gfx = XGraphics.FromPdfPage(page);
        DrawGeneratedPageChrome(gfx, page, request.Project, plan.Title, plan.Number);

        DrawSiteContextMapPanel(
            gfx,
            request.Project,
            request.Project.SiteContext.LocationScheme,
            BuildingArchitectureConceptPageLayout.SiteContextLocationPanel,
            BuildingArchitectureConceptPageLayout.SiteContextLocationMapArea,
            "БАЙРШЛЫН СХЕМ");
        DrawSiteContextMapPanel(
            gfx,
            request.Project,
            request.Project.SiteContext.SurroundingsOverview,
            BuildingArchitectureConceptPageLayout.SiteContextOverviewPanel,
            BuildingArchitectureConceptPageLayout.SiteContextOverviewMapArea,
            "ОРЧНЫ ТОЙМ");
    }

    private static void DrawSiteContextMapPanel(
        XGraphics gfx,
        AlbumProject project,
        ProjectMapViewport viewport,
        PageRectMm panelMm,
        PageRectMm mapAreaMm,
        string title)
    {
        XRect panel = ToPoints(panelMm);
        XRect mapArea = ToPoints(mapAreaMm);
        var border = new XPen(XColors.Black, Mm(0.15));
        var muted = new XSolidBrush(XColor.FromArgb(92, 101, 112));
        gfx.DrawRectangle(XBrushes.White, panel);
        gfx.DrawRectangle(border, panel);
        gfx.DrawLine(border, panel.Left, mapArea.Top, panel.Right, mapArea.Top);
        DrawFittedText(
            gfx,
            title,
            panel.Left + Mm(4),
            panel.Top,
            panel.Width - Mm(8),
            mapArea.Top - panel.Top,
            10,
            true,
            XStringFormats.Center);

        string? snapshotPath = ResolveDocumentPath(project, viewport.SnapshotRelativePath);
        if (snapshotPath is not null)
        {
            try
            {
                using XImage image = XImage.FromFile(snapshotPath);
                DrawContainedImage(gfx, image, mapArea);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                DrawSiteContextPlaceholder(gfx, viewport, mapArea, muted, "Газрын зургийг уншиж чадсангүй");
            }
        }
        else
        {
            DrawSiteContextPlaceholder(gfx, viewport, mapArea, muted, "Газрын зураг тохируулаагүй");
        }

        string attribution = string.IsNullOrWhiteSpace(viewport.Attribution)
            ? SiteContextProviderLabel(viewport.ProviderId)
            : viewport.Attribution;
        DrawFittedText(
            gfx,
            attribution,
            mapArea.Left + Mm(2),
            mapArea.Bottom - Mm(5),
            mapArea.Width - Mm(4),
            Mm(4),
            5.5,
            false,
            XStringFormats.CenterRight);
    }

    private static void DrawSiteContextPlaceholder(
        XGraphics gfx,
        ProjectMapViewport viewport,
        XRect mapArea,
        XBrush muted,
        string message)
    {
        gfx.DrawString(
            message,
            new XFont(FontName, 10),
            muted,
            new XRect(mapArea.X + Mm(8), mapArea.Y, mapArea.Width - Mm(16), mapArea.Height),
            XStringFormats.Center);
        gfx.DrawString(
            $"{viewport.CenterLatitude:0.000000}, {viewport.CenterLongitude:0.000000} · z{viewport.Zoom:0.#}",
            new XFont(FontName, 6.5),
            muted,
            new XRect(mapArea.X + Mm(8), mapArea.Bottom - Mm(12), mapArea.Width - Mm(16), Mm(5)),
            XStringFormats.Center);
    }

    private static string SiteContextProviderLabel(string providerId) => providerId switch
    {
        ProjectMapProviderIds.OpenStreetMap => "© OpenStreetMap contributors",
        ProjectMapProviderIds.OpenTopoMap => "© OpenStreetMap contributors · OpenTopoMap",
        ProjectMapProviderIds.GoogleRoad or ProjectMapProviderIds.GoogleSatellite => "Google Maps",
        ProjectMapProviderIds.AzureRoad or ProjectMapProviderIds.AzureAerial => "Microsoft Azure Maps",
        _ => providerId,
    };

    private static void DrawGeneratedPageChrome(
        XGraphics gfx,
        PdfPage page,
        AlbumProject project,
        string title,
        string number)
    {
        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        DrawConceptSheetChrome(gfx, project, title, number);
    }

    private static void DrawGeneratedDocumentContent(
        XGraphics gfx,
        AlbumProject project,
        ConceptGeneratedPagePlan plan)
    {
        var muted = new XSolidBrush(XColor.FromArgb(92, 101, 112));
        DrawFittedText(
            gfx,
            plan.DocumentLabel,
            Mm(20),
            Mm(17),
            Mm(320),
            Mm(9),
            10.5,
            true,
            XStringFormats.CenterLeft);
        if (plan.BatchCount > 1)
        {
            DrawFittedText(
                gfx,
                $"{plan.BatchNumber}/{plan.BatchCount}",
                Mm(350),
                Mm(17),
                Mm(60),
                Mm(9),
                8.5,
                false,
                XStringFormats.CenterRight);
        }

        if (plan.DocumentPages.Count == 0)
        {
            DrawGeneratedDocumentPlaceholder(gfx, project, plan, muted);
            return;
        }

        var content = new XRect(Mm(20), Mm(29), Mm(390), Mm(226));
        IReadOnlyList<XRect> tiles = CreateDocumentTileLayout(content, plan.DocumentPages.Count);
        for (int index = 0; index < plan.DocumentPages.Count; index++)
        {
            DrawDocumentTile(gfx, project, plan.DocumentPages[index], tiles[index], muted);
        }
    }

    private static void DrawGeneratedDocumentPlaceholder(
        XGraphics gfx,
        AlbumProject project,
        ConceptGeneratedPagePlan plan,
        XBrush muted)
    {
        var border = new XPen(XColor.FromArgb(176, 183, 192), Mm(0.15));
        var rect = new XRect(Mm(55), Mm(54), Mm(320), Mm(164));
        gfx.DrawRectangle(border, rect);
        string primary = plan.DocumentKind == ConceptGeneratedDocumentKind.ApprovedPlanningTask
            ? ValueOrDash(project.PlanningTask.IssuingAuthorityName)
            : ValueOrDash(CompanyDisplayName(project.Company, project.DesignOrganizationName));
        string secondary = plan.DocumentKind == ConceptGeneratedDocumentKind.ApprovedPlanningTask
            ? $"АТД {ValueOrDash(project.PlanningTask.AtdNumber)}"
            : $"Регистр {ValueOrDash(project.Company.RegistrationNumber)}";
        DrawFittedText(gfx, primary, rect.X + Mm(20), rect.Y + Mm(45), rect.Width - Mm(40), Mm(28), 15, true, XStringFormats.Center);
        DrawFittedText(gfx, secondary, rect.X + Mm(20), rect.Y + Mm(78), rect.Width - Mm(40), Mm(18), 10, false, XStringFormats.Center);
        gfx.DrawString(
            "Хуулбар оруулаагүй",
            new XFont(FontName, 8.5),
            muted,
            new XRect(rect.X + Mm(20), rect.Y + Mm(112), rect.Width - Mm(40), Mm(14)),
            XStringFormats.Center);
    }

    private static IReadOnlyList<XRect> CreateDocumentTileLayout(XRect area, int count)
    {
        const double gapMm = 7;
        double gap = Mm(gapMm);
        if (count <= 1)
            return [area];
        if (count == 2)
        {
            double width = (area.Width - gap) * 0.5;
            return
            [
                new XRect(area.X, area.Y, width, area.Height),
                new XRect(area.X + width + gap, area.Y, width, area.Height),
            ];
        }
        if (count == 3)
        {
            double width = (area.Width - gap) * 0.5;
            double rightHeight = (area.Height - gap) * 0.5;
            return
            [
                new XRect(area.X, area.Y, width, area.Height),
                new XRect(area.X + width + gap, area.Y, width, rightHeight),
                new XRect(area.X + width + gap, area.Y + rightHeight + gap, width, rightHeight),
            ];
        }

        double tileWidth = (area.Width - gap) * 0.5;
        double tileHeight = (area.Height - gap) * 0.5;
        return
        [
            new XRect(area.X, area.Y, tileWidth, tileHeight),
            new XRect(area.X + tileWidth + gap, area.Y, tileWidth, tileHeight),
            new XRect(area.X, area.Y + tileHeight + gap, tileWidth, tileHeight),
            new XRect(area.X + tileWidth + gap, area.Y + tileHeight + gap, tileWidth, tileHeight),
        ];
    }

    private static void DrawDocumentTile(
        XGraphics gfx,
        AlbumProject project,
        ConceptGeneratedDocumentPage documentPage,
        XRect tile,
        XBrush muted)
    {
        var frame = new XPen(XColor.FromArgb(166, 174, 184), Mm(0.12));
        gfx.DrawRectangle(XBrushes.White, tile);
        gfx.DrawRectangle(frame, tile);
        string? path = ResolveDocumentPath(project, documentPage.Document.RelativePath);
        if (path is null)
        {
            DrawFittedText(gfx, "Файл олдсонгүй", tile.X + Mm(8), tile.Y, tile.Width - Mm(16), tile.Height, 10, true, XStringFormats.Center);
            return;
        }

        var imageArea = new XRect(
            tile.X + Mm(3),
            tile.Y + Mm(3),
            tile.Width - Mm(6),
            tile.Height - Mm(9));
        try
        {
            if (Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                using var form = XPdfForm.FromFile(path);
                form.PageNumber = documentPage.SourcePageNumber;
                DrawContainedImage(gfx, form, imageArea);
            }
            else
            {
                using var image = XImage.FromFile(path);
                DrawContainedImage(gfx, image, imageArea);
            }

            string pageLabel = documentPage.Document.PageCount > 1
                ? $"{documentPage.SourcePageNumber}/{documentPage.Document.PageCount}"
                : Path.GetFileName(documentPage.Document.OriginalFileName);
            gfx.DrawString(
                pageLabel,
                new XFont(FontName, 6.5),
                muted,
                new XRect(tile.X + Mm(3), tile.Bottom - Mm(6), tile.Width - Mm(6), Mm(4)),
                XStringFormats.CenterRight);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            DrawFittedText(gfx, "Баримтыг уншиж чадсангүй", tile.X + Mm(8), tile.Y, tile.Width - Mm(16), tile.Height, 9, true, XStringFormats.Center);
        }
    }

    private static void DrawContainedImage(XGraphics gfx, XImage image, XRect target)
    {
        double sourceWidth = Math.Max(1, image.PointWidth);
        double sourceHeight = Math.Max(1, image.PointHeight);
        double scale = Math.Min(target.Width / sourceWidth, target.Height / sourceHeight);
        double width = sourceWidth * scale;
        double height = sourceHeight * scale;
        double x = target.X + (target.Width - width) * 0.5;
        double y = target.Y + (target.Height - height) * 0.5;
        var state = gfx.Save();
        try
        {
            gfx.IntersectClip(target);
            gfx.DrawImage(image, x, y, width, height);
        }
        finally
        {
            gfx.Restore(state);
        }
    }

    private static void DrawVisualizationPage(
        PdfDocument document,
        AlbumProject project,
        VisualizationAlbumPagePlan plan,
        ICollection<string> warnings)
    {
        PdfPage page = AddA3LandscapePage(document);
        using XGraphics gfx = XGraphics.FromPdfPage(page);
        DrawGeneratedPageChrome(gfx, page, project, plan.Title, plan.Number);
        var tilePen = new XPen(XColor.FromArgb(200, 204, 210), Mm(0.10));

        foreach (VisualizationImageTilePlan tile in plan.Tiles)
        {
            XRect frame = ToPoints(tile.Frame);
            gfx.DrawRectangle(XBrushes.White, frame);
            string? path = ResolveDocumentPath(project, tile.Image.RelativePath);
            if (path is null)
            {
                warnings.Add($"Visualization image was not found: {tile.Image.OriginalFileName}");
                DrawFittedText(
                    gfx,
                    "Зураг олдсонгүй",
                    frame.X + Mm(5),
                    frame.Y,
                    frame.Width - Mm(10),
                    frame.Height,
                    9,
                    false,
                    XStringFormats.Center);
                gfx.DrawRectangle(tilePen, frame);
                continue;
            }

            try
            {
                using XImage image = XImage.FromFile(path);
                if (tile.FitMode == VisualizationImageFitMode.CenterCrop)
                {
                    DrawCroppedVisualizationImage(
                        gfx,
                        image,
                        frame,
                        tile.Image.FocalPointX,
                        tile.Image.FocalPointY);
                }
                else
                {
                    DrawContainedImage(gfx, image, frame);
                }
                gfx.DrawRectangle(tilePen, frame);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                warnings.Add($"Visualization image could not be read: {tile.Image.OriginalFileName}");
                DrawFittedText(
                    gfx,
                    "Зургийг уншиж чадсангүй",
                    frame.X + Mm(5),
                    frame.Y,
                    frame.Width - Mm(10),
                    frame.Height,
                    9,
                    false,
                    XStringFormats.Center);
                gfx.DrawRectangle(tilePen, frame);
            }
        }
    }

    private static void DrawCroppedVisualizationImage(
        XGraphics gfx,
        XImage image,
        XRect target,
        double focalPointX,
        double focalPointY)
    {
        double sourceWidth = Math.Max(1d, image.PointWidth);
        double sourceHeight = Math.Max(1d, image.PointHeight);
        double sourceRatio = sourceWidth / sourceHeight;
        double targetRatio = target.Width / target.Height;
        double cropWidth = sourceWidth;
        double cropHeight = sourceHeight;

        if (sourceRatio > targetRatio)
            cropWidth = sourceHeight * targetRatio;
        else
            cropHeight = sourceWidth / targetRatio;

        double focusX = Math.Clamp(focalPointX, 0d, 1d) * sourceWidth;
        double focusY = Math.Clamp(focalPointY, 0d, 1d) * sourceHeight;
        double cropX = Math.Clamp(focusX - cropWidth * 0.5d, 0d, sourceWidth - cropWidth);
        double cropY = Math.Clamp(focusY - cropHeight * 0.5d, 0d, sourceHeight - cropHeight);
        gfx.DrawImage(
            image,
            target,
            new XRect(cropX, cropY, cropWidth, cropHeight),
            XGraphicsUnit.Point);
    }

    private static string? ResolveDocumentPath(AlbumProject project, string relativeOrAbsolutePath)
    {
        if (string.IsNullOrWhiteSpace(relativeOrAbsolutePath))
            return null;
        string path = Path.IsPathRooted(relativeOrAbsolutePath)
            ? Path.GetFullPath(relativeOrAbsolutePath)
            : string.IsNullOrWhiteSpace(project.ProjectFolder)
                ? Path.GetFullPath(relativeOrAbsolutePath)
                : Path.GetFullPath(Path.Combine(project.ProjectFolder, relativeOrAbsolutePath));
        return File.Exists(path) ? path : null;
    }

    private static void DrawCompanyIdentity(
        XGraphics gfx,
        CompanyProfile company,
        XRect rect,
        double preferredNameSize)
    {
        var maxLogoWidth = Math.Min(rect.Width * 0.28, Mm(42));
        var logoRect = new XRect(rect.Left, rect.Top, maxLogoWidth, rect.Height);
        if (TryDrawCompanyLogo(gfx, company, logoRect))
        {
            DrawFittedText(gfx, ValueOrDash(CompanyDisplayName(company)), logoRect.Right + Mm(6), rect.Top,
                rect.Right - logoRect.Right - Mm(6), rect.Height, preferredNameSize, true, XStringFormats.CenterLeft);
            return;
        }

        DrawFittedText(gfx, ValueOrDash(CompanyDisplayName(company)), rect.X, rect.Y, rect.Width, rect.Height,
            preferredNameSize, true, XStringFormats.Center);
    }

    private static void DrawCoverValue(
        XGraphics gfx,
        string label,
        string value,
        double xMm,
        double yMm,
        double widthMm,
        XBrush mutedBrush)
    {
        gfx.DrawString(label, new XFont(FontName, 8), mutedBrush, new XRect(Mm(xMm), Mm(yMm), Mm(widthMm), Mm(7)), XStringFormats.Center);
        DrawFittedText(gfx, value, Mm(xMm), Mm(yMm + 9), Mm(widthMm), Mm(16), 11, true, XStringFormats.Center);
    }

    private static void DrawInfoRow(
        XGraphics gfx,
        string label,
        string value,
        double xMm,
        double yMm,
        double widthMm,
        double heightMm)
    {
        var labelWidth = Math.Min(48, widthMm * 0.34);
        var border = new XPen(XColor.FromArgb(155, 163, 173), 0.4);
        gfx.DrawRectangle(border, Mm(xMm), Mm(yMm), Mm(widthMm), Mm(heightMm));
        gfx.DrawLine(border, Mm(xMm + labelWidth), Mm(yMm), Mm(xMm + labelWidth), Mm(yMm + heightMm));
        DrawFittedText(gfx, label, Mm(xMm + 2), Mm(yMm + 1), Mm(labelWidth - 4), Mm(heightMm - 2), 7, true);
        DrawFittedText(gfx, value, Mm(xMm + labelWidth + 3), Mm(yMm + 1), Mm(widthMm - labelWidth - 6), Mm(heightMm - 2), 8.5, false);
    }

    private static void DrawTableRow(
        XGraphics gfx,
        XPen border,
        XBrush mutedBrush,
        double xMm,
        double yMm,
        double roleWidthMm,
        double nameWidthMm,
        double emailWidthMm,
        string role,
        string name,
        string email,
        bool isHeader)
    {
        const double rowHeight = 13;
        var totalWidth = roleWidthMm + nameWidthMm + emailWidthMm;
        gfx.DrawRectangle(border, Mm(xMm), Mm(yMm), Mm(totalWidth), Mm(rowHeight));
        gfx.DrawLine(border, Mm(xMm + roleWidthMm), Mm(yMm), Mm(xMm + roleWidthMm), Mm(yMm + rowHeight));
        gfx.DrawLine(border, Mm(xMm + roleWidthMm + nameWidthMm), Mm(yMm), Mm(xMm + roleWidthMm + nameWidthMm), Mm(yMm + rowHeight));
        var brush = isHeader ? mutedBrush : XBrushes.Black;
        var size = isHeader ? 7.5 : 8.5;
        DrawFittedText(gfx, role, Mm(xMm + 3), Mm(yMm + 1), Mm(roleWidthMm - 6), Mm(rowHeight - 2), size, isHeader);
        DrawFittedText(gfx, name, Mm(xMm + roleWidthMm + 3), Mm(yMm + 1), Mm(nameWidthMm - 6), Mm(rowHeight - 2), size, isHeader);
        DrawFittedText(gfx, email, Mm(xMm + roleWidthMm + nameWidthMm + 3), Mm(yMm + 1), Mm(emailWidthMm - 6), Mm(rowHeight - 2), size, isHeader);
    }

    private static void DrawWrappedText(
        XGraphics gfx,
        string? text,
        XFont font,
        XBrush brush,
        XRect rect,
        double lineHeight)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            DrawFittedText(gfx, "Мэдээлэл бүрдээгүй", rect.X, rect.Y, rect.Width, rect.Height, 9, false);
            return;
        }

        var y = rect.Top;
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = string.IsNullOrEmpty(line) ? word : $"{line} {word}";
                if (gfx.MeasureString(candidate, font).Width <= rect.Width)
                {
                    line = candidate;
                    continue;
                }

                if (!string.IsNullOrEmpty(line) && y + lineHeight <= rect.Bottom)
                {
                    gfx.DrawString(line, font, brush, new XPoint(rect.Left, y + font.Size));
                    y += lineHeight;
                }
                line = word;
            }

            if (!string.IsNullOrEmpty(line) && y + lineHeight <= rect.Bottom)
            {
                gfx.DrawString(line, font, brush, new XPoint(rect.Left, y + font.Size));
                y += lineHeight;
            }
            y += lineHeight * 0.35;
            if (y >= rect.Bottom)
            {
                break;
            }
        }
    }

    private static string FormatDate(DateTimeOffset? value) =>
        value.HasValue ? value.Value.ToString("yyyy-MM-dd") : "-";

    private static string ValueOrDash(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();

    private static string FirstNonEmpty(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary)
            ? primary.Trim()
            : fallback?.Trim() ?? "";

    private static void DrawCoverPage(PdfDocument document, AlbumBuildRequest request)
    {
        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        using var gfx = XGraphics.FromPdfPage(page);

        var company = request.Project.Company;
        var subtitleFont = new XFont(FontName, 15);
        var labelFont = new XFont(FontName, 11);
        var mutedBrush = new XSolidBrush(XColor.FromArgb(96, 108, 122));
        var width = page.Width.Point;
        var height = page.Height.Point;

        gfx.DrawRectangle(XBrushes.White, 0, 0, width, height);

        var companyName = CompanyDisplayName(company);
        if (!string.IsNullOrWhiteSpace(companyName))
        {
            gfx.DrawString(companyName, subtitleFont, XBrushes.Black,
                new XRect(0, height * 0.14, width, 24), XStringFormats.Center);
        }

        DrawFittedText(
            gfx,
            request.Project.Album.Title,
            40,
            height * 0.38,
            width - 80,
            44,
            30,
            true,
            XStringFormats.Center);

        gfx.DrawString(ProjectDisplayName(request.Project), subtitleFont, XBrushes.Black,
            new XRect(40, height * 0.38 + 52, width - 80, 26), XStringFormats.Center);

        if (!string.IsNullOrWhiteSpace(request.Project.Code))
        {
            gfx.DrawString(request.Project.Code, labelFont, mutedBrush,
                new XRect(0, height * 0.38 + 84, width, 18), XStringFormats.Center);
        }

        gfx.DrawString(DateTime.Now.ToString("yyyy-MM-dd"), labelFont, mutedBrush,
            new XRect(0, height - 70, width, 16), XStringFormats.Center);
    }

    private static void DrawTableOfContents(PdfDocument document, AlbumBuildRequest request)
    {
        var sectionFont = new XFont(FontName, 12, XFontStyleEx.Bold);
        var rowFont = new XFont(FontName, 10);
        var headerFont = new XFont(FontName, 16, XFontStyleEx.Bold);
        var mutedBrush = new XSolidBrush(XColor.FromArgb(96, 108, 122));

        var page = document.AddPage();
        page.Size = PdfSharp.PageSize.A4;
        var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        var y = 60.0;
        gfx.DrawString("Гарчиг", headerFont, XBrushes.Black, new XPoint(50, y));
        y += 28;

        void EnsureRoom()
        {
            if (y <= page.Height.Point - 60)
            {
                return;
            }

            gfx.Dispose();
            page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
            gfx = XGraphics.FromPdfPage(page);
            gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
            y = 60.0;
        }

        var index = 1;
        foreach (var section in request.Sections)
        {
            if (!string.IsNullOrWhiteSpace(section.Title))
            {
                EnsureRoom();
                y += 8;
                gfx.DrawString(section.Title, sectionFont, XBrushes.Black, new XPoint(50, y));
                y += 20;
            }

            foreach (var buildPage in section.Pages)
            {
                EnsureRoom();
                gfx.DrawString(index.ToString(), rowFont, mutedBrush,
                    new XRect(50, y - 10, 24, 14), XStringFormats.TopLeft);
                gfx.DrawString(buildPage.Number, rowFont, XBrushes.Black,
                    new XRect(80, y - 10, 80, 14), XStringFormats.TopLeft);
                gfx.DrawString(buildPage.Title, rowFont, XBrushes.Black,
                    new XRect(170, y - 10, page.Width.Point - 240, 14), XStringFormats.TopLeft);
                gfx.DrawString(buildPage.Sheet.Source.Application.ToString(), rowFont, mutedBrush,
                    new XRect(page.Width.Point - 110, y - 10, 60, 14), XStringFormats.TopLeft);
                y += 17;
                index++;
            }
        }

        gfx.Dispose();
    }
}
