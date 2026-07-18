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

        var generatedPages = request.Project.Album.Composition
            .Where(item => item.Kind == AlbumCompositionKind.Generated)
            .OrderBy(item => item.Order)
            .ToList();
        if (generatedPages.Count > 0)
        {
            foreach (var item in generatedPages)
            {
                DrawGeneratedPage(document, request, item);
            }
        }
        else if (request.Project.Album.IncludeCover)
        {
            DrawCoverPage(document, request);
        }

        if (request.Project.Album.IncludeTableOfContents)
        {
            DrawTableOfContents(document, request);
        }

        var sheetCount = 0;
        foreach (var section in request.Sections)
        {
            foreach (var buildPage in section.Pages)
            {
                var sheet = buildPage.Sheet;
                if (!File.Exists(sheet.PdfPath))
                {
                    warnings.Add($"Missing PDF skipped: {sheet.DisplayLabel} ({sheet.PdfPath})");
                    continue;
                }

                try
                {
                    if (buildPage.Format.Kind == PageFormatKind.SourceAsIs)
                    {
                        ImportSourceAsIs(document, sheet.PdfPath);
                    }
                    else
                    {
                        ComposeFormattedPages(document, request.Project, buildPage);
                    }

                    sheetCount++;
                }
                catch (Exception exception)
                {
                    warnings.Add($"Sheet failed to compose: {sheet.DisplayLabel}: {exception.Message}");
                }
            }
        }

        if (document.PageCount == 0)
        {
            var page = document.AddPage();
            page.Size = PdfSharp.PageSize.A4;
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
        return result;
    }

    private static void ImportSourceAsIs(PdfDocument document, string pdfPath)
    {
        using var source = PdfReader.Open(pdfPath, PdfDocumentOpenMode.Import);
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

        using var form = XPdfForm.FromFile(buildPage.Sheet.PdfPath);
        for (var sourcePageNumber = 1; sourcePageNumber <= sourcePageCount; sourcePageNumber++)
        {
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
        if (BuildingArchitectureConceptPageLayout.IsCanonical(format))
        {
            DrawConceptSheetChrome(gfx, project, buildPage.Title, buildPage.Number);
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
        string sheetTitle,
        string sheetNumber)
    {
        var borderPen = new XPen(XColors.Black, Mm(0.35));
        var finePen = new XPen(XColors.Black, Mm(0.10));
        var frame = ToPoints(BuildingArchitectureConceptPageLayout.Frame);
        var header = ToPoints(BuildingArchitectureConceptPageLayout.SheetTitleArea);
        var corner = ToPoints(BuildingArchitectureConceptPageLayout.TitleBlockArea);
        var paperBrush = new XSolidBrush(XColor.FromArgb(254, 254, 254));

        // These areas belong to Studio and must cover any authoring-application
        // annotation that may still be present during the migration period.
        // Near-white deliberately forces a PDF color operator after an imported
        // form restores its graphics state; otherwise some renderers reuse black.
        gfx.DrawRectangle(paperBrush, header);
        gfx.DrawRectangle(paperBrush, corner);
        gfx.DrawRectangle(borderPen, frame);
        gfx.DrawLine(
            borderPen,
            Mm(BuildingArchitectureConceptPageLayout.FrameLeftMm),
            Mm(BuildingArchitectureConceptPageLayout.SheetHeaderBottomMm),
            Mm(BuildingArchitectureConceptPageLayout.FrameRightMm),
            Mm(BuildingArchitectureConceptPageLayout.SheetHeaderBottomMm));

        DrawFittedText(
            gfx,
            sheetTitle,
            header.Left + Mm(3),
            header.Top + Mm(0.8),
            header.Width - Mm(6),
            header.Height - Mm(1.6),
            8.5,
            false,
            XStringFormats.CenterRight);

        DrawConceptCornerTable(gfx, project, sheetNumber, borderPen, finePen);
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

    private static void DrawConceptCornerTable(
        XGraphics gfx,
        AlbumProject project,
        string sheetNumber,
        XPen borderPen,
        XPen finePen)
    {
        var x0 = BuildingArchitectureConceptPageLayout.CornerX0Mm;
        var x1 = BuildingArchitectureConceptPageLayout.CornerX1Mm;
        var x2 = BuildingArchitectureConceptPageLayout.CornerX2Mm;
        var x3 = BuildingArchitectureConceptPageLayout.CornerX3Mm;
        var x4 = BuildingArchitectureConceptPageLayout.CornerX4Mm;
        var x5 = BuildingArchitectureConceptPageLayout.CornerX5Mm;
        var y0 = BuildingArchitectureConceptPageLayout.CornerY0Mm;
        var y1 = BuildingArchitectureConceptPageLayout.CornerY1Mm;
        var y2 = BuildingArchitectureConceptPageLayout.CornerY2Mm;
        var y3 = BuildingArchitectureConceptPageLayout.CornerY3Mm;
        var y4 = BuildingArchitectureConceptPageLayout.CornerY4Mm;

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
        var clientName = string.IsNullOrWhiteSpace(project.InitiationBasis.ClientName)
            ? project.ClientName
            : project.InitiationBasis.ClientName;
        var companyRole = string.IsNullOrWhiteSpace(companyName)
            ? companyRepresentative.Role
            : $"\"{companyName}\" {companyRepresentative.Role}".Trim();

        DrawCompanyLogoOrMark(gfx, company, TopLeftRect(x0, y0, x1, y4));
        DrawCellText(gfx, project.Name, x1, y0, x2, y1, 6.8, false, XStringFormats.CenterLeft);
        DrawCellText(gfx, "Нэр", x2, y0, x3, y1, 6.7, false, XStringFormats.Center);
        DrawCellText(gfx, "Гарын үсэг", x3, y0, x4, y1, 6.7, false, XStringFormats.Center);
        DrawCellText(gfx, "Загвар", x4, y0, x5, y1, 6.7, false, XStringFormats.Center);

        DrawCellText(gfx, companyRole, x1, y1, x2, y2, 6.3, false, XStringFormats.CenterLeft);
        DrawCellText(gfx, companyRepresentative.Name, x2, y1, x3, y2, 6.3, false, XStringFormats.Center);

        DrawCellText(gfx, "Архитектор", x1, y2, x2, y3, 6.5, false, XStringFormats.CenterLeft);
        DrawCellText(gfx, architect, x2, y2, x3, y3, 6.3, false, XStringFormats.Center);
        DrawCellText(gfx, $"Хуудас-{ValueOrDash(sheetNumber)}", x4, y2, x5, y3, 6.0, false, XStringFormats.Center);

        DrawCellText(gfx, "Захиалагч", x1, y3, x2, y4, 6.5, false, XStringFormats.CenterLeft);
        DrawCellText(gfx, ValueOrDash(clientName), x2, y3, x3, y4, 6.3, false, XStringFormats.Center);
        DrawCellText(gfx, $"{DateTime.Now:yyyy} он", x4, y3, x5, y4, 6.0, false, XStringFormats.Center);
    }

    private static void DrawCellText(
        XGraphics gfx,
        string? text,
        double x0Mm,
        double y0Mm,
        double x1Mm,
        double y1Mm,
        double fontSize,
        bool bold,
        XStringFormat format)
    {
        var paddingMm = format == XStringFormats.CenterLeft ? 1.8 : 0.8;
        DrawFittedText(
            gfx,
            text,
            Mm(x0Mm + paddingMm),
            Mm(y0Mm + 0.6),
            Mm(x1Mm - x0Mm - paddingMm * 2),
            Mm(y1Mm - y0Mm - 1.2),
            fontSize,
            bold,
            format);
    }

    private static (string Role, string Name) ResolveCompanyRepresentative(AlbumProject project)
    {
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
        return project.Participants
                   .Where(candidate => candidate.Role.Contains("architect", StringComparison.OrdinalIgnoreCase))
                   .OrderBy(candidate => candidate.Role.Contains("Major", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                   .Select(candidate => candidate.FullName)
                   .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
               ?? "";
    }

    private static string CompanyDisplayName(CompanyProfile company, string fallback = "")
    {
        if (!string.IsNullOrWhiteSpace(company.DisplayName))
            return company.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(company.Name))
            return company.Name.Trim();
        return fallback.Trim();
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
        var projectName = string.IsNullOrWhiteSpace(project.Name) ? project.Code : project.Name;
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
        AlbumCompositionItem item)
    {
        switch (item.GeneratedPageKind)
        {
            case AlbumGeneratedPageKind.Cover:
                DrawConceptCoverPage(document, request, item);
                break;
            case AlbumGeneratedPageKind.DesignOrganization:
                DrawDesignOrganizationPage(document, request, item);
                break;
            case AlbumGeneratedPageKind.PlanningTask:
                DrawPlanningTaskPage(document, request, item);
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

    private const double CoverTableLeftMm = 68.275;
    private const double CoverReviewRoleRightMm = 131.275;
    private const double CoverReviewNameRightMm = 171.275;
    private const double CoverReviewRightMm = 196.275;
    private const double CoverCompanyRoleLeftMm = 226.275;
    private const double CoverCompanyRoleRightMm = 284.975;
    private const double CoverCompanyNameRightMm = 326.725;
    private const double CoverTableRightMm = 351.725;

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
        var authorityMembers = request.Project.PlanningTask.AuthorityMembers;
        var approved = authorityMembers.FirstOrDefault(member => HasRole(member, "Chief Architect"));
        var approvals = authorityMembers
            .Where(member => !ReferenceEquals(member, approved))
            .Where(member => member.Roles.Count > 0 || !string.IsNullOrWhiteSpace(member.FullName))
            .Take(8)
            .ToList();
        var reviewRowCount = Math.Clamp(approvals.Count, 1, 8);
        var companyRepresentative = ResolveCompanyRepresentative(request.Project);
        var companyName = CompanyDisplayName(company, request.Project.DesignOrganizationName);
        var companyRole = string.IsNullOrWhiteSpace(companyName)
            ? companyRepresentative.Role
            : $"\"{companyName}\" {companyRepresentative.Role}".Trim();
        var clientName = string.IsNullOrWhiteSpace(request.Project.InitiationBasis.ClientName)
            ? request.Project.ClientName
            : request.Project.InitiationBasis.ClientName;
        const double bodyTextHeightMm = BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm;
        const double projectNameTextHeightMm = BuildingArchitectureConceptPageLayout.CoverProjectNameTextHeightMm;
        var approvedRole = (approved is null ? "Ерөнхий архитектор" : DisplayRoles(approved)).ToUpperInvariant();
        var approvedName = (approved?.FullName ?? "").ToUpperInvariant();
        var approvedRoleHeightMm = MeasureCoverTextHeightMm(gfx, approvedRole, 120.0, bodyTextHeightMm);
        var approvedNameHeightMm = MeasureCoverTextHeightMm(gfx, approvedName, 75.0, bodyTextHeightMm);
        var approvedRowHeightMm = Math.Max(8.0, Math.Max(approvedRoleHeightMm, approvedNameHeightMm) + 1.2);
        const double approvedRowTopMm = 262.205;
        var reviewRows = BuildCoverReviewRows(gfx, approvals, reviewRowCount);
        var companyColumn = BuildCoverCompanyColumn(
            gfx,
            companyRole,
            companyRepresentative.Name,
            clientName);
        var reviewTableBottomMm = reviewRows.Count == 0 ? 93.86 : reviewRows[^1].BottomMm;
        var tableBottomMm = Math.Min(reviewTableBottomMm, companyColumn.BottomMm);

        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        gfx.DrawRectangle(border, ToPoints(BuildingArchitectureConceptPageLayout.Frame));

        DrawCoverText(gfx, "БАТЛАВ:", CoverCenteredRect(210.0, 281.205, 50.0, 8.0), bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverText(
            gfx,
            approvedRole,
            CoverRect(105.8, approvedRowTopMm - approvedRowHeightMm, 225.8, approvedRowTopMm),
            bodyTextHeightMm,
            false,
            XStringFormats.CenterLeft);
        DrawCoverText(
            gfx,
            approvedName,
            CoverRect(277.4, approvedRowTopMm - approvedRowHeightMm, 352.4, approvedRowTopMm),
            bodyTextHeightMm,
            false,
            XStringFormats.CenterLeft);

        DrawCoverText(
            gfx,
            ValueOrDash(request.Project.InitiationBasis.SiteAddress),
            CoverCenteredRect(210.0, 220.510, 180.0, 8.0),
            bodyTextHeightMm,
            false,
            XStringFormats.Center);
        DrawCoverText(
            gfx,
            request.Project.Name,
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
        DrawSketchCoverApprovalTable(gfx, border, fine, reviewRows, companyColumn, tableBottomMm);

        DrawCoverCellText(gfx, "Албан тушаал", CoverTableLeftMm, 153.86, CoverReviewRoleRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Нэр", CoverReviewRoleRightMm, 153.86, CoverReviewNameRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Гарын үсэг", CoverReviewNameRightMm, 153.86, CoverReviewRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Албан тушаал", CoverCompanyRoleLeftMm, 153.86, CoverCompanyRoleRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Нэр", CoverCompanyRoleRightMm, 153.86, CoverCompanyNameRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Гарын үсэг", CoverCompanyNameRightMm, 153.86, CoverTableRightMm, 161.86, bodyTextHeightMm, false, XStringFormats.Center);

        foreach (var row in reviewRows)
        {
            DrawCoverCellText(gfx, row.Member is null ? "" : DisplayRoles(row.Member), CoverTableLeftMm, row.BottomMm, CoverReviewRoleRightMm, row.TopMm, bodyTextHeightMm, false, XStringFormats.CenterLeft, 2.0);
            DrawCoverCellText(gfx, row.Member?.FullName ?? "", CoverReviewRoleRightMm, row.BottomMm, CoverReviewNameRightMm, row.TopMm, bodyTextHeightMm, false, XStringFormats.Center);
        }

        DrawCoverCellText(gfx, "Захиалагч", 196.275, companyColumn.ClientTitleBottomMm, 226.275, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Иргэн", CoverCompanyRoleLeftMm, companyColumn.ClientDataBottomMm, CoverCompanyRoleRightMm, companyColumn.ClientTitleBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, ValueOrDash(clientName), CoverCompanyRoleRightMm, companyColumn.ClientDataBottomMm, CoverCompanyNameRightMm, companyColumn.ClientTitleBottomMm, bodyTextHeightMm, false, XStringFormats.Center);

        DrawCoverCellText(gfx, "Гүйцэтгэсэн", 196.275, companyColumn.CompanyTitleBottomMm, 351.725, companyColumn.ClientDataBottomMm, bodyTextHeightMm, false, XStringFormats.CenterLeft, 5.7);
        DrawCompanyLogoOrMark(gfx, company, CoverRect(196.275, tableBottomMm, 226.275, companyColumn.CompanyTitleBottomMm), bodyTextHeightMm);
        DrawCoverCellText(gfx, "Албан тушаал", CoverCompanyRoleLeftMm, companyColumn.CompanyHeaderBottomMm, CoverCompanyRoleRightMm, companyColumn.CompanyTitleBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Нэр", CoverCompanyRoleRightMm, companyColumn.CompanyHeaderBottomMm, CoverCompanyNameRightMm, companyColumn.CompanyTitleBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Гарын үсэг", CoverCompanyNameRightMm, companyColumn.CompanyHeaderBottomMm, CoverTableRightMm, companyColumn.CompanyTitleBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, companyRole, CoverCompanyRoleLeftMm, tableBottomMm, CoverCompanyRoleRightMm, companyColumn.CompanyHeaderBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, companyRepresentative.Name, CoverCompanyRoleRightMm, tableBottomMm, CoverCompanyNameRightMm, companyColumn.CompanyHeaderBottomMm, bodyTextHeightMm, false, XStringFormats.Center);

        DrawCoverText(gfx, "Улаанбаатар хот", CoverCenteredRect(210.0, 26.125, 200.0, 12.0), bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverText(gfx, $"{DateTime.Now:yyyy} он", CoverCenteredRect(210.0, 15.625, 90.0, 12.0), bodyTextHeightMm, false, XStringFormats.Center);
    }

    private static void DrawSketchCoverApprovalTable(
        XGraphics gfx,
        XPen border,
        XPen fine,
        IReadOnlyList<CoverReviewRow> reviewRows,
        CoverCompanyColumn companyColumn,
        double tableBottomMm)
    {
        const double x0 = CoverTableLeftMm;
        var y0 = tableBottomMm;
        const double x1 = CoverTableRightMm;
        const double y1 = 161.86;
        const double rightX0 = CoverReviewRightMm;
        const double headerY0 = 153.86;

        DrawCoverLine(gfx, border, x0, y0, x1, y0);
        DrawCoverLine(gfx, border, x0, y1, x1, y1);
        DrawCoverLine(gfx, border, x0, y0, x0, y1);
        DrawCoverLine(gfx, border, x1, y0, x1, y1);
        DrawCoverLine(gfx, border, x0, headerY0, x1, headerY0);
        DrawCoverLine(gfx, border, rightX0, y0, rightX0, y1);

        DrawCoverLine(gfx, fine, CoverReviewRoleRightMm, y0, CoverReviewRoleRightMm, y1);
        DrawCoverLine(gfx, fine, CoverReviewNameRightMm, y0, CoverReviewNameRightMm, y1);
        DrawCoverLine(gfx, fine, CoverCompanyRoleLeftMm, companyColumn.ClientDataBottomMm, CoverCompanyRoleLeftMm, y1);
        DrawCoverLine(gfx, fine, CoverCompanyRoleRightMm, companyColumn.ClientDataBottomMm, CoverCompanyRoleRightMm, y1);
        DrawCoverLine(gfx, fine, CoverCompanyNameRightMm, companyColumn.ClientDataBottomMm, CoverCompanyNameRightMm, y1);
        DrawCoverLine(gfx, fine, CoverCompanyRoleLeftMm, y0, CoverCompanyRoleLeftMm, companyColumn.CompanyTitleBottomMm);
        DrawCoverLine(gfx, fine, CoverCompanyRoleRightMm, y0, CoverCompanyRoleRightMm, companyColumn.CompanyTitleBottomMm);
        DrawCoverLine(gfx, fine, CoverCompanyNameRightMm, y0, CoverCompanyNameRightMm, companyColumn.CompanyTitleBottomMm);

        DrawCoverLine(gfx, fine, rightX0, companyColumn.ClientDataBottomMm, x1, companyColumn.ClientDataBottomMm);
        DrawCoverLine(gfx, fine, rightX0, companyColumn.CompanyTitleBottomMm, x1, companyColumn.CompanyTitleBottomMm);
        DrawCoverLine(gfx, fine, CoverCompanyRoleLeftMm, companyColumn.CompanyHeaderBottomMm, x1, companyColumn.CompanyHeaderBottomMm);

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

    private static IReadOnlyList<CoverReviewRow> BuildCoverReviewRows(
        XGraphics gfx,
        IReadOnlyList<ProjectMember> approvals,
        int rowCount)
    {
        const double rowsTopMm = 153.86;
        const double baseRowsHeightMm = 60.0;
        const double roleTextWidthMm = CoverReviewRoleRightMm - CoverTableLeftMm - 2.4;
        const double nameTextWidthMm = CoverReviewNameRightMm - CoverReviewRoleRightMm - 2.4;
        const double cellVerticalPaddingMm = 1.2;
        const double bodyTextHeightMm = BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm;
        var baseRowHeightMm = baseRowsHeightMm / Math.Max(1, rowCount);
        var rows = new List<CoverReviewRow>(rowCount);
        var topMm = rowsTopMm;

        for (var index = 0; index < rowCount; index++)
        {
            var member = index < approvals.Count ? approvals[index] : null;
            var roleHeightMm = MeasureCoverTextHeightMm(
                gfx,
                member is null ? "" : DisplayRoles(member),
                roleTextWidthMm,
                bodyTextHeightMm);
            var nameHeightMm = MeasureCoverTextHeightMm(
                gfx,
                member?.FullName,
                nameTextWidthMm,
                bodyTextHeightMm);
            var requiredHeightMm = Math.Max(roleHeightMm, nameHeightMm) + cellVerticalPaddingMm;
            var rowHeightMm = Math.Max(baseRowHeightMm, requiredHeightMm);
            var bottomMm = topMm - rowHeightMm;
            rows.Add(new CoverReviewRow(member, bottomMm, topMm));
            topMm = bottomMm;
        }

        return rows;
    }

    private static CoverCompanyColumn BuildCoverCompanyColumn(
        XGraphics gfx,
        string companyRole,
        string companyRepresentativeName,
        string clientName)
    {
        const double clientTitleBottomMm = 153.86;
        const double titleHeightMm = 8.0;
        const double headerHeightMm = 8.0;
        const double baseClientDataHeightMm = 16.0;
        const double baseCompanyDataHeightMm = 20.0;
        const double roleTextWidthMm = CoverCompanyRoleRightMm - CoverCompanyRoleLeftMm - 2.4;
        const double nameTextWidthMm = CoverCompanyNameRightMm - CoverCompanyRoleRightMm - 2.4;
        const double cellVerticalPaddingMm = 1.2;
        const double bodyTextHeightMm = BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm;

        var clientRoleHeightMm = MeasureCoverTextHeightMm(
            gfx,
            "Иргэн",
            roleTextWidthMm,
            bodyTextHeightMm);
        var clientNameHeightMm = MeasureCoverTextHeightMm(
            gfx,
            clientName,
            nameTextWidthMm,
            bodyTextHeightMm);
        var clientDataHeightMm = Math.Max(
            baseClientDataHeightMm,
            Math.Max(clientRoleHeightMm, clientNameHeightMm) + cellVerticalPaddingMm);
        var clientDataBottomMm = clientTitleBottomMm - clientDataHeightMm;
        var companyTitleBottomMm = clientDataBottomMm - titleHeightMm;
        var companyHeaderBottomMm = companyTitleBottomMm - headerHeightMm;

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
        var companyDataHeightMm = Math.Max(
            baseCompanyDataHeightMm,
            Math.Max(companyRoleHeightMm, companyNameHeightMm) + cellVerticalPaddingMm);
        var bottomMm = companyHeaderBottomMm - companyDataHeightMm;
        return new CoverCompanyColumn(
            clientTitleBottomMm,
            clientDataBottomMm,
            companyTitleBottomMm,
            companyHeaderBottomMm,
            bottomMm);
    }

    private static XRect CoverCenteredRect(double centerXMm, double centerYMm, double widthMm, double heightMm) =>
        ToPoints(BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(centerXMm, centerYMm, widthMm, heightMm));

    private static XRect CoverRect(double x0Mm, double y0Mm, double x1Mm, double y1Mm) =>
        ToPoints(BuildingArchitectureConceptPageLayout.FromBottomLeft(x0Mm, y0Mm, x1Mm, y1Mm));

    private sealed record CoverReviewRow(ProjectMember? Member, double BottomMm, double TopMm);

    private sealed record CoverCompanyColumn(
        double ClientTitleBottomMm,
        double ClientDataBottomMm,
        double CompanyTitleBottomMm,
        double CompanyHeaderBottomMm,
        double BottomMm);

    private static bool HasRole(ProjectMember member, string role) =>
        member.Roles.Any(candidate => candidate.Contains(role, StringComparison.OrdinalIgnoreCase));

    private static string DisplayRoles(ProjectMember member) =>
        string.Join(", ", member.Roles.Select(DisplayRole).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string DisplayRole(string role)
    {
        if (role.Contains("Chief Architect", StringComparison.OrdinalIgnoreCase))
        {
            return "Ерөнхий архитектор";
        }
        if (role.Contains("Department Head", StringComparison.OrdinalIgnoreCase))
        {
            return "Хэлтсийн дарга";
        }
        if (role.Contains("Authority Specialist", StringComparison.OrdinalIgnoreCase))
        {
            return "Хот байгуулалтын мэргэжилтэн";
        }
        return role;
    }

    private static void DrawDesignOrganizationPage(
        PdfDocument document,
        AlbumBuildRequest request,
        AlbumCompositionItem item)
    {
        var page = AddA3LandscapePage(document);
        using var gfx = XGraphics.FromPdfPage(page);
        DrawGeneratedPageChrome(gfx, page, request.Project, item);

        var border = new XPen(XColors.Black, 0.55);
        var fine = new XPen(XColor.FromArgb(155, 163, 173), 0.4);
        var muted = new XSolidBrush(XColor.FromArgb(92, 101, 112));
        var company = request.Project.Company;

        var identityRect = new XRect(Mm(20), Mm(28), Mm(120), Mm(225));
        gfx.DrawRectangle(border, identityRect);
        DrawCompanyIdentity(gfx, company, new XRect(Mm(28), Mm(39), Mm(104), Mm(48)), 15);
        gfx.DrawLine(fine, Mm(28), Mm(95), Mm(132), Mm(95));
        DrawInfoRow(gfx, "БАЙГУУЛЛАГА", ValueOrDash(company.Name), 28, 103, 104, 18);
        DrawInfoRow(gfx, "ТОВЧ НЭР", ValueOrDash(company.ShortName), 28, 123, 104, 13);
        DrawInfoRow(gfx, "РЕГИСТР", ValueOrDash(company.RegistrationNumber), 28, 138, 104, 13);
        DrawInfoRow(gfx, "ХОТ / ДҮҮРЭГ", ValueOrDash(company.RegisteredCity), 28, 153, 104, 13);
        DrawInfoRow(gfx, "ХАЯГ", ValueOrDash(company.Address), 28, 168, 104, 18);
        DrawInfoRow(gfx, "УТАС", ValueOrDash(CompanyPhoneText(company)), 28, 188, 104, 15);
        DrawInfoRow(gfx, "И-МЭЙЛ", ValueOrDash(company.Email), 28, 205, 104, 13);
        DrawInfoRow(gfx, "ВЭБ", ValueOrDash(company.WebSite), 28, 220, 104, 13);
        DrawInfoRow(gfx, "ЛИЦЕНЗ", ValueOrDash(CompanyLicenseText(company)), 28, 235, 104, 14);

        var tableX = 147d;
        var tableY = 28d;
        var tableWidth = 263d;
        var headerHeight = 15d;
        gfx.DrawRectangle(border, Mm(tableX), Mm(tableY), Mm(tableWidth), Mm(225));
        gfx.DrawRectangle(new XSolidBrush(XColor.FromArgb(239, 242, 246)), Mm(tableX), Mm(tableY), Mm(tableWidth), Mm(headerHeight));
        DrawFittedText(gfx, "ТӨСӨЛД ОРОЛЦОГЧИД", Mm(tableX + 5), Mm(tableY), Mm(tableWidth - 10), Mm(headerHeight), 11, true, XStringFormats.CenterLeft);

        var roleWidth = 88d;
        var nameWidth = 105d;
        var emailWidth = tableWidth - roleWidth - nameWidth;
        var rowY = tableY + headerHeight;
        DrawTableRow(gfx, border, muted, tableX, rowY, roleWidth, nameWidth, emailWidth,
            "ҮҮРЭГ", "НЭР", "И-МЭЙЛ", true);
        rowY += 13;

        var people = company.Signers
            .Select(signer => new ProjectParticipant { Role = signer.Role, FullName = signer.FullName })
            .Concat(request.Project.Participants)
            .Where(person => !string.IsNullOrWhiteSpace(person.FullName))
            .DistinctBy(person => $"{person.Role}|{person.FullName}|{person.Email}", StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
        foreach (var person in people)
        {
            DrawTableRow(gfx, border, muted, tableX, rowY, roleWidth, nameWidth, emailWidth,
                ValueOrDash(person.Role), ValueOrDash(person.FullName), ValueOrDash(person.Email), false);
            rowY += 13;
        }

        if (people.Count == 0)
        {
            DrawTableRow(gfx, border, muted, tableX, rowY, roleWidth, nameWidth, emailWidth,
                "-", "Мэдээлэл бүрдээгүй", "-", false);
        }
    }

    private static void DrawPlanningTaskPage(
        PdfDocument document,
        AlbumBuildRequest request,
        AlbumCompositionItem item)
    {
        var page = AddA3LandscapePage(document);
        using var gfx = XGraphics.FromPdfPage(page);
        DrawGeneratedPageChrome(gfx, page, request.Project, item);

        var border = new XPen(XColors.Black, 0.55);
        var muted = new XSolidBrush(XColor.FromArgb(92, 101, 112));
        var basis = request.Project.InitiationBasis;
        var task = request.Project.PlanningTask;

        gfx.DrawRectangle(border, Mm(20), Mm(28), Mm(185), Mm(135));
        DrawInfoRow(gfx, "АТД ОЛГОСОН БАЙГУУЛЛАГА", ValueOrDash(task.IssuingAuthorityName), 25, 35, 175, 19);
        DrawInfoRow(gfx, "АТД ДУГААР", ValueOrDash(task.AtdNumber), 25, 56, 175, 17);
        DrawInfoRow(gfx, "ОЛГОСОН ОГНОО", FormatDate(task.IssuedAtUtc), 25, 75, 175, 17);
        DrawInfoRow(gfx, "ТӨЛӨВ", ValueOrDash(task.Status), 25, 94, 175, 17);
        DrawInfoRow(gfx, "ЗАХИАЛАГЧ", ValueOrDash(basis.ClientName), 25, 113, 175, 17);
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

    private static void DrawGeneratedPageChrome(
        XGraphics gfx,
        PdfPage page,
        AlbumProject project,
        AlbumCompositionItem item)
    {
        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        DrawConceptSheetChrome(gfx, project, item.Title, item.Number);
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

        if (!string.IsNullOrWhiteSpace(request.Project.Name))
        {
            gfx.DrawString(request.Project.Name, subtitleFont, XBrushes.Black,
                new XRect(40, height * 0.38 + 52, width - 80, 26), XStringFormats.Center);
        }

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
