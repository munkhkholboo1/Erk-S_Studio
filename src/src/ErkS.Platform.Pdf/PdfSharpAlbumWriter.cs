using System.Runtime.CompilerServices;
using ErkS.Platform.Core;
using ErkS.Platform.Contracts;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Pdf;

/// <summary>
/// Composes source PDFs into Studio page instances. Source-as-is keeps the
/// original page untouched; formatted pages preserve vector content and add
/// the Studio-owned frame and title information.
/// </summary>
public sealed partial class PdfSharpAlbumWriter : IAlbumPdfWriter
{
    private const string FontName = BuildingArchitectureConceptPageLayout.FontFamilyName;
    private const string WorkingDrawingFontName = "ISOCPEUR MON";
    private const double PointsPerMillimeter = 72.0 / 25.4;
    private static readonly ConditionalWeakTable<XGraphics, CoverFontContext> CoverFontContexts = new();

    private sealed record CoverFontContext(string FontName);

    public AlbumBuildResult Compose(AlbumBuildRequest request, string outputPath)
    {
        WindowsFontResolver.Register();
        var warnings = new List<string>();
        using var document = new PdfDocument();
        document.Info.Title = request.Project.Album.Title;
        document.Info.Author = request.Project.Company.Name;
        document.Info.Keywords = WithCanonicalTitleBlockSignature(
            document.Info.Keywords,
            ComputeCanonicalTitleBlockSignature(request.Project));
        var components = new Dictionary<string, AlbumBuildComponent>(StringComparer.OrdinalIgnoreCase);
        int componentOrder = 0;

        void RecordComponent(
            string code,
            string label,
            int firstPageIndex,
            string sourceIdentity = "",
            string sectionKey = "",
            string sequenceKey = "",
            AlbumBuildPage? sourcePage = null)
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
                    SourceIdentity = sourceIdentity,
                    SectionKey = sectionKey,
                    SequenceKey = sequenceKey,
                };
                components.Add(code, component);
            }
            for (int page = firstPageIndex + 1; page <= lastPageIndex; page++)
            {
                if (!component.PageNumbers.Contains(page))
                    component.PageNumbers.Add(page);
                if (sourcePage is null ||
                    component.Pages.Any(item => item.PageNumber == page))
                {
                    continue;
                }

                int componentPageOffset = page - firstPageIndex - 1;
                int nativePageNumber = sourcePage.Sheet.Entry.PdfPageNumber > 0
                    ? sourcePage.Sheet.Entry.PdfPageNumber + componentPageOffset
                    : componentPageOffset + 1;
                string nativeSheetId =
                    FirstStableNativeIdentity(sourcePage.Sheet.Entry);
                component.Pages.Add(new AlbumBuildComponentPage
                {
                    PageNumber = page,
                    // The name is taken here because this is the one moment
                    // both the drawing and the physical page it landed on are
                    // in hand. A reviewer holds neither, and reads this.
                    Title = sourcePage.Title,
                    NativeSheetId = nativeSheetId,
                    NativePageNumber = nativePageNumber,
                    SortKey = StablePageSortKey(
                        sourcePage.Sheet.Entry,
                        nativeSheetId,
                        nativePageNumber),
                    SectionKey = sectionKey,
                    SequenceKey = sequenceKey,
                });
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
                if (buildPage.Format.Kind == PageFormatKind.SourceAsIs &&
                    !HasSourcePageEdits(buildPage.Definition.SourceCrop))
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
                string sectionKey = (section.Key ?? "").Trim();
                string sequenceKey = (buildPage.Definition.TemplateSlotId ?? "").Trim();
                string componentCode = "source:" + sourceIdentity;
                if (!string.IsNullOrWhiteSpace(sectionKey) ||
                    !string.IsNullOrWhiteSpace(sequenceKey))
                {
                    componentCode +=
                        $"|album-slice|{sectionKey}|{sequenceKey}";
                }
                RecordComponent(
                    componentCode,
                    string.IsNullOrWhiteSpace(section.Title) ? sheet.DisplayLabel : section.Title,
                    firstPageIndex,
                    sourceIdentity,
                    sectionKey,
                    sequenceKey,
                    buildPage);

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

    private static string FirstStableNativeIdentity(SheetPackageEntry entry)
    {
        string value = (entry.SheetId ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(value))
            return value;
        value = (entry.DrawingAssetId ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(value))
            return value;
        value = (entry.Number ?? "").Trim();
        return string.IsNullOrWhiteSpace(value)
            ? "native-page"
            : value;
    }

    private static string StablePageSortKey(
        SheetPackageEntry entry,
        string nativeSheetId,
        int nativePageNumber)
    {
        string sheetNumber = (entry.Number ?? "").Trim();
        string prefix = string.IsNullOrWhiteSpace(sheetNumber)
            ? nativeSheetId
            : sheetNumber;
        return $"{prefix}|{nativeSheetId}|{nativePageNumber:D8}";
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
            XRect sourceRect = ResolveSourceRectangle(form, buildPage.Definition.SourceCrop);
            var page = document.AddPage();
            if (buildPage.Format.Kind == PageFormatKind.SourceAsIs)
            {
                page.Width = XUnit.FromPoint(sourceRect.Width);
                page.Height = XUnit.FromPoint(sourceRect.Height);
            }
            else
            {
                page.Width = XUnit.FromMillimeter(buildPage.Format.WidthMm);
                page.Height = XUnit.FromMillimeter(buildPage.Format.HeightMm);
            }
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
        // PDF source pages now use the placement selected by Studio. This is
        // essential after a crop: FitDrawingArea supplies the Studio canvas
        // and offset records the position the user chose in its preview.
        PagePlacementMode placementMode = buildPage.Definition.PlacementMode;
        var target = format.Kind == PageFormatKind.SourceAsIs
            ? new XRect(
                0,
                0,
                ResolveSourceRectangle(form, buildPage.Definition.SourceCrop).Width,
                ResolveSourceRectangle(form, buildPage.Definition.SourceCrop).Height)
            : placementMode == PagePlacementMode.FullPage
                ? new XRect(0, 0, Mm(format.WidthMm), Mm(format.HeightMm))
                : ToPoints(format.DrawingArea);
        PdfSourcePlacement placement = CalculateSourcePlacement(
            Math.Max(1, form.PointWidth),
            Math.Max(1, form.PointHeight),
            target,
            placementMode,
            buildPage.Definition.SourceCrop,
            format.Id);

        var state = gfx.Save();
        gfx.IntersectClip(target);
        if (Math.Abs(placement.RotationDegrees) > 0.0001)
        {
            double centerX =
                placement.DestinationRectangle.X + placement.DestinationRectangle.Width / 2;
            double centerY =
                placement.DestinationRectangle.Y + placement.DestinationRectangle.Height / 2;
            gfx.TranslateTransform(centerX, centerY);
            gfx.RotateTransform(placement.RotationDegrees);
            gfx.TranslateTransform(-centerX, -centerY);
        }
        // XPdfForm does not consistently honor DrawImage's source rectangle
        // overload. Draw the full vector form through an explicit destination
        // clip instead, so a Studio crop removes the legacy page frame rather
        // than merely moving/scaling the complete source page.
        gfx.IntersectClip(placement.DestinationRectangle);
        DrawCroppedPdfForm(gfx, form, placement);
        foreach (XPoint[] polygon in placement.MaskPolygons)
        {
            gfx.DrawPolygon(XBrushes.White, polygon, XFillMode.Winding);
        }
        gfx.Restore(state);
    }

    private static void DrawCroppedPdfForm(
        XGraphics gfx,
        XPdfForm form,
        PdfSourcePlacement placement)
    {
        XRect desiredSourceDestination = CalculateCompleteSourceDestination(
            Math.Max(1, form.PointWidth),
            Math.Max(1, form.PointHeight),
            placement);
        PdfRectangle mediaBox = form.Page?.MediaBox ??
                                new PdfRectangle(
                                    new XPoint(0, 0),
                                    new XPoint(
                                        Math.Max(1, form.PointWidth),
                                        Math.Max(1, form.PointHeight)));
        XRect pdfSharpDrawRectangle = CalculatePdfSharpFormDrawRectangle(
            desiredSourceDestination,
            Math.Max(1, form.PointWidth),
            Math.Max(1, form.PointHeight),
            mediaBox.X1,
            mediaBox.Y1);
        gfx.DrawImage(form, pdfSharpDrawRectangle);
    }

    internal static XRect CalculateCompleteSourceDestination(
        double formWidth,
        double formHeight,
        PdfSourcePlacement placement)
    {
        double scaleX = placement.DestinationRectangle.Width /
                        Math.Max(1, placement.SourceRectangle.Width);
        double scaleY = placement.DestinationRectangle.Height /
                        Math.Max(1, placement.SourceRectangle.Height);
        return new XRect(
            placement.DestinationRectangle.X - placement.SourceRectangle.X * scaleX,
            placement.DestinationRectangle.Y - placement.SourceRectangle.Y * scaleY,
            Math.Max(1, formWidth) * scaleX,
            Math.Max(1, formHeight) * scaleY);
    }

    /// <summary>
    /// PDFsharp 6.2 offsets an imported form by the unscaled MediaBox origin.
    /// That is correct at scale 1, but a centered/non-zero MediaBox drifts as
    /// soon as a crop is fitted to a different size. Pre-compensating the
    /// rectangle makes the effective full-page destination equal the preview's
    /// zero-origin, top-left geometry.
    /// </summary>
    internal static XRect CalculatePdfSharpFormDrawRectangle(
        XRect desiredSourceDestination,
        double formWidth,
        double formHeight,
        double mediaBoxX1,
        double mediaBoxY1)
    {
        double scaleX = desiredSourceDestination.Width / Math.Max(1, formWidth);
        double scaleY = desiredSourceDestination.Height / Math.Max(1, formHeight);
        return new XRect(
            desiredSourceDestination.X - mediaBoxX1 * (scaleX - 1),
            desiredSourceDestination.Y + mediaBoxY1 * (scaleY - 1),
            desiredSourceDestination.Width,
            desiredSourceDestination.Height);
    }

    internal static PdfSourcePlacement CalculateSourcePlacement(
        double formWidth,
        double formHeight,
        XRect target,
        PagePlacementMode placementMode,
        SourcePageCropDefinition? crop,
        string formatId = "")
    {
        PdfSourcePagePlacementMm geometry = PdfSourcePagePlacementGeometry.Calculate(
            Math.Max(1, formWidth) / PointsPerMillimeter,
            Math.Max(1, formHeight) / PointsPerMillimeter,
            new PageRectMm
            {
                X = target.X / PointsPerMillimeter,
                Y = target.Y / PointsPerMillimeter,
                Width = target.Width / PointsPerMillimeter,
                Height = target.Height / PointsPerMillimeter,
            },
            placementMode,
            crop,
            formatId);
        XRect sourceRect = ToPoints(geometry.SourceRectangle);
        XRect destination = ToPoints(geometry.DestinationRectangle);
        IReadOnlyList<XPoint[]> masks = ResolveMaskPolygons(
            Math.Max(1, formWidth),
            Math.Max(1, formHeight),
            sourceRect,
            destination,
            crop);

        return new PdfSourcePlacement(
            sourceRect,
            destination,
            geometry.RotationDegrees,
            masks);
    }

    internal static bool HasSourcePageEdits(SourcePageCropDefinition? crop) =>
        PdfSourcePagePlacementGeometry.HasCompositionEdits(crop);

    private static IReadOnlyList<XPoint[]> ResolveMaskPolygons(
        double formWidth,
        double formHeight,
        XRect sourceRect,
        XRect destination,
        SourcePageCropDefinition? crop)
    {
        if (crop?.Masks is null || crop.Masks.Count == 0)
            return [];

        var result = new List<XPoint[]>();
        foreach (SourcePageMaskDefinition mask in crop.Masks.Where(IsUsableMask))
        {
            IReadOnlyList<SourcePagePointDefinition> normalizedPoints =
                mask.Shape == SourcePageMaskShape.Rectangle
                    ? CreateRectangleMaskPoints(mask.Points)
                    : mask.Points;
            XPoint[] points = normalizedPoints
                .Select(point =>
                {
                    double sourceX = formWidth * Math.Clamp(ResolveFinite(point.X), 0, 1);
                    double sourceY = formHeight * Math.Clamp(ResolveFinite(point.Y), 0, 1);
                    return new XPoint(
                        destination.X +
                        (sourceX - sourceRect.X) / sourceRect.Width * destination.Width,
                        destination.Y +
                        (sourceY - sourceRect.Y) / sourceRect.Height * destination.Height);
                })
                .ToArray();
            if (points.Length >= 3)
                result.Add(points);
        }
        return result;
    }

    private static IReadOnlyList<SourcePagePointDefinition> CreateRectangleMaskPoints(
        IReadOnlyList<SourcePagePointDefinition> points)
    {
        SourcePagePointDefinition first = points[0];
        SourcePagePointDefinition second = points[1];
        double left = Math.Min(first.X, second.X);
        double top = Math.Min(first.Y, second.Y);
        double right = Math.Max(first.X, second.X);
        double bottom = Math.Max(first.Y, second.Y);
        return
        [
            new SourcePagePointDefinition { X = left, Y = top },
            new SourcePagePointDefinition { X = right, Y = top },
            new SourcePagePointDefinition { X = right, Y = bottom },
            new SourcePagePointDefinition { X = left, Y = bottom },
        ];
    }

    private static bool IsUsableMask(SourcePageMaskDefinition? mask) =>
        mask is not null &&
        mask.Points is not null &&
        mask.Points.Count >= (mask.Shape == SourcePageMaskShape.Rectangle ? 2 : 3);

    private static double ResolveFinite(double? value, double fallback = 0) =>
        value is double number && !double.IsNaN(number) && !double.IsInfinity(number)
            ? number
            : fallback;

    private static XRect ResolveSourceRectangle(
        XPdfForm form,
        SourcePageCropDefinition? crop) =>
        ResolveSourceRectangle(
            Math.Max(1, form.PointWidth),
            Math.Max(1, form.PointHeight),
            crop);

    private static XRect ResolveSourceRectangle(
        double formWidth,
        double formHeight,
        SourcePageCropDefinition? crop)
    {
        formWidth = Math.Max(1, formWidth);
        formHeight = Math.Max(1, formHeight);
        if (crop is not { Enabled: true })
        {
            return new XRect(0, 0, formWidth, formHeight);
        }

        double left = Mm(Math.Max(0, crop.LeftMm));
        double top = Mm(Math.Max(0, crop.TopMm));
        double right = Mm(Math.Max(0, crop.RightMm));
        double bottom = Mm(Math.Max(0, crop.BottomMm));
        double width = formWidth - left - right;
        double height = formHeight - top - bottom;
        if (width <= 0.5 || height <= 0.5)
        {
            throw new InvalidDataException(
                "PDF source crop removes the complete page. Reduce the crop margins.");
        }

        return new XRect(left, top, width, height);
    }

    internal sealed record PdfSourcePlacement(
        XRect SourceRectangle,
        XRect DestinationRectangle,
        double RotationDegrees,
        IReadOnlyList<XPoint[]> MaskPolygons);

    private static void DrawPageFormat(
        XGraphics gfx,
        PdfPage page,
        AlbumProject project,
        AlbumBuildPage buildPage)
    {
        var format = buildPage.Format;
        if (format.Kind is PageFormatKind.SourceAsIs or PageFormatKind.Portfolio)
        {
            return;
        }

        if (BuildingArchitectureConceptPageLayout.SupportsStudioChrome(format))
        {
            DrawConceptSheetChrome(gfx, project, buildPage);
            return;
        }

        if (format.Kind == PageFormatKind.WorkingDrawing)
        {
            DrawWorkingDrawingSheetChrome(gfx, page, project, buildPage);
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

    private static void DrawWorkingDrawingSheetChrome(
        XGraphics gfx,
        PdfPage page,
        AlbumProject project,
        AlbumBuildPage buildPage)
    {
        WorkingDrawingPageRegions regions = WorkingDrawingPageLayout.Resolve(buildPage.Format);
        var borderPen = new XPen(XColors.Black, Mm(0.35));
        var finePen = new XPen(XColors.Black, Mm(0.10));
        var paperBrush = new XSolidBrush(XColor.FromArgb(254, 254, 254));
        XRect corner = ToPoints(regions.TitleBlockArea);
        XRect sheetHeader = ToPoints(regions.SheetTitleArea);

        // Text and the real standard table belong to Studio. The host-side
        // rectangles only reserve these zones and are covered here.
        gfx.DrawRectangle(paperBrush, sheetHeader);
        gfx.DrawRectangle(paperBrush, corner);
        DrawEtalonGrid(gfx, regions, borderPen, finePen);
        DrawRevitWorkingSheetHeader(gfx, sheetHeader, buildPage, borderPen);
        gfx.DrawRectangle(borderPen, corner);
        DrawRevitWorkingTitleBlock(gfx, corner, project, buildPage, borderPen, finePen);
        gfx.DrawRectangle(
            new XPen(XColor.FromArgb(185, 190, 196), 0.25),
            new XRect(0, 0, page.Width.Point, page.Height.Point));
    }

    private static void DrawRevitWorkingSheetHeader(
        XGraphics gfx,
        XRect rect,
        AlbumBuildPage buildPage,
        XPen borderPen)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        // Mirrors Revit BlueprintSheetHeader: one 9 mm strip containing the
        // sheet name and the resolved sheet scale. Sheet number stays in the
        // corner table family.
        gfx.DrawRectangle(borderPen, rect);
        string scale = string.IsNullOrWhiteSpace(buildPage.Sheet.Entry.ScaleText)
            ? "1:100"
            : buildPage.Sheet.Entry.ScaleText.Trim();
        if (!scale.StartsWith("M", StringComparison.OrdinalIgnoreCase) &&
            !scale.StartsWith("М", StringComparison.OrdinalIgnoreCase))
        {
            scale = $"M {scale}";
        }

        double padding = Mm(1.5);
        DrawWrappedCoverText(
            gfx,
            buildPage.Title,
            new XRect(rect.Left + padding, rect.Top + padding, rect.Width - padding * 2, rect.Height - padding * 2),
            2.5,
            false,
            XStringFormats.TopRight,
            WorkingDrawingFontName);
    }

    private static void DrawEtalonGrid(
        XGraphics gfx,
        WorkingDrawingPageRegions regions,
        XPen borderPen,
        XPen finePen)
    {
        XRect outer = ToPoints(regions.EtalonOuterFrame);
        XRect inner = ToPoints(regions.EtalonInnerFrame);
        gfx.DrawRectangle(borderPen, outer);
        gfx.DrawRectangle(borderPen, inner);

        for (int column = 1; column < regions.GridColumns; column++)
        {
            double x = inner.Left + inner.Width * column / regions.GridColumns;
            gfx.DrawLine(finePen, x, outer.Top, x, inner.Top);
            gfx.DrawLine(finePen, x, inner.Bottom, x, outer.Bottom);
        }
        for (int row = 1; row < regions.GridRows; row++)
        {
            double y = inner.Top + inner.Height * row / regions.GridRows;
            gfx.DrawLine(finePen, outer.Left, y, inner.Left, y);
            gfx.DrawLine(finePen, inner.Right, y, outer.Right, y);
        }

        DrawEtalonLabels(gfx, outer, inner, regions.GridColumns, regions.GridRows);
        double centerX = inner.Left + inner.Width * 0.5;
        double centerY = inner.Top + inner.Height * 0.5;
        gfx.DrawLine(borderPen, centerX, outer.Top, centerX, inner.Top);
        gfx.DrawLine(borderPen, centerX, inner.Bottom, centerX, outer.Bottom);
        gfx.DrawLine(borderPen, outer.Left, centerY, inner.Left, centerY);
        gfx.DrawLine(borderPen, inner.Right, centerY, outer.Right, centerY);
    }

    private static void DrawEtalonLabels(
        XGraphics gfx,
        XRect outer,
        XRect inner,
        int columns,
        int rows)
    {
        // The canonical printed text height is 2.5 mm throughout the working
        // drawing sheet, including etalon coordinates.
        XFont font = CreateCoverFont(2.5, false, WorkingDrawingFontName);
        double bandWidth = Math.Max(1, inner.Left - outer.Left);
        double bandHeight = Math.Max(1, inner.Top - outer.Top);
        for (int column = 0; column < columns; column++)
        {
            double width = inner.Width / columns;
            var top = new XRect(inner.Left + width * column, outer.Top, width, bandHeight);
            var bottom = new XRect(inner.Left + width * column, inner.Bottom, width, outer.Bottom - inner.Bottom);
            string label = (column + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            gfx.DrawString(label, font, XBrushes.Black, top, XStringFormats.Center);
            gfx.DrawString(label, font, XBrushes.Black, bottom, XStringFormats.Center);
        }
        for (int row = 0; row < rows; row++)
        {
            double height = inner.Height / rows;
            var left = new XRect(outer.Left, inner.Top + height * row, bandWidth, height);
            var right = new XRect(inner.Right, inner.Top + height * row, outer.Right - inner.Right, height);
            string label = GridRowLabel(row);
            gfx.DrawString(label, font, XBrushes.Black, left, XStringFormats.Center);
            gfx.DrawString(label, font, XBrushes.Black, right, XStringFormats.Center);
        }
    }

    private static string GridRowLabel(int index)
    {
        index = Math.Max(0, index);
        string label = "";
        do
        {
            label = (char)('A' + index % 26) + label;
            index = index / 26 - 1;
        }
        while (index >= 0);
        return label;
    }

    private static void DrawConceptSheetChrome(
        XGraphics gfx,
        AlbumProject project,
        AlbumBuildPage buildPage)
    {
        bool hasInformationHeader = PdfSourcePageStudioLayout.UsesInformationHeader(
            buildPage.Definition,
            buildPage.Sheet.Entry);
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

        DrawSelectedCornerTable(
            gfx,
            project,
            buildPage,
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

    /// <summary>
    /// Draws whichever corner title block the project asked for.
    ///
    /// Both are drawn here, on a page that has no reference grid: the grid
    /// belongs to the working drawing sheet, not to the block, and a project
    /// can want the larger block without wanting the grid that block usually
    /// travels with.
    /// </summary>
    private static void DrawSelectedCornerTable(
        XGraphics gfx,
        AlbumProject project,
        AlbumBuildPage buildPage,
        PageRectMm titleBlockArea,
        XPen borderPen,
        XPen finePen)
    {
        if (AlbumCornerTableStyles.Normalize(project.CornerTableStyle)
            == AlbumCornerTableStyles.WorkingDrawing)
        {
            DrawRevitWorkingTitleBlock(
                gfx,
                ToPoints(WorkingDrawingCornerArea(titleBlockArea)),
                project,
                buildPage,
                borderPen,
                finePen);
            return;
        }

        DrawConceptCornerTable(
            gfx,
            project,
            buildPage.Number,
            buildPage.ScaleText,
            titleBlockArea,
            borderPen,
            finePen);
    }

    /// <summary>
    /// The 180 x 36 mm block placed in the space the 190 x 28 one would have
    /// filled: anchored to the same bottom-right corner, so a project that
    /// switches style keeps its drawings where they were.
    /// </summary>
    private static PageRectMm WorkingDrawingCornerArea(PageRectMm conceptArea)
    {
        double width = WorkingDrawingPageLayout.HorizontalTitleBlockWidthMm;
        double height = WorkingDrawingPageLayout.HorizontalTitleBlockHeightMm;
        return new PageRectMm
        {
            X = conceptArea.X + conceptArea.Width - width,
            Y = conceptArea.Y + conceptArea.Height - height,
            Width = width,
            Height = height,
        };
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
        DrawCornerTableLines(gfx, ConceptCornerTableLines.Full(grid), borderPen, finePen);

        DrawCanonicalConceptCornerMetadata(
            gfx,
            project,
            grid,
            borderPen,
            finePen,
            clearCanonicalCells: false);

        DrawCellText(gfx, "Гарын үсэг", x3, y0, x4, y1, false, XStringFormats.Center);
        DrawCellText(gfx, "Загвар", x4, y0, x5, y1, false, XStringFormats.Center);
        DrawCellText(gfx, scaleText, x4, y1, x5, y2, false, XStringFormats.Center);
        DrawCellText(gfx, $"Хуудас-{ValueOrDash(sheetNumber)}", x4, y2, x5, y3, false, XStringFormats.Center);
        // The ENTERED sheet date decides the year when there is one. Read from
        // the clock alone, a rebuild in January reissued a December album under
        // the following year. The same expression feeds the restamp signature -
        // see CornerTableYear - so the two cannot drift apart.
        DrawCellText(gfx, $"{CornerTableYear(project)} он", x4, y3, x5, y4, false, XStringFormats.Center);
    }

    private static void DrawCanonicalConceptCornerMetadata(
        XGraphics gfx,
        AlbumProject project,
        BuildingArchitectureConceptCornerGrid grid,
        XPen borderPen,
        XPen finePen,
        bool clearCanonicalCells)
    {
        var x0 = grid.X0;
        var x1 = grid.X1;
        var x2 = grid.X2;
        var x3 = grid.X3;
        var y0 = grid.Y0;
        var y1 = grid.Y1;
        var y2 = grid.Y2;
        var y3 = grid.Y3;
        var y4 = grid.Y4;

        if (clearCanonicalCells)
        {
            gfx.DrawRectangle(
                new XSolidBrush(XColor.FromArgb(254, 254, 254)),
                Mm(x0),
                Mm(y0),
                Mm(x3 - x0),
                Mm(y4 - y0));
            // Only x0, y0 and y4 are edges of the table. x3 is an interior
            // division, and redrawing it with the border pen - which a plain
            // rectangle does - laid a heavy line down the middle of the block
            // and made it read as two tables.
            DrawCornerTableLines(
                gfx,
                ConceptCornerTableLines.Restamped(grid),
                borderPen,
                finePen);
        }

        CompanyProfile company = ResolveDesignCompanyProfile(project);
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

        DrawCellText(gfx, companyRole, x1, y1, x2, y2, false, XStringFormats.CenterLeft);
        DrawCellText(gfx, companyRepresentative.Name, x2, y1, x3, y2, false, XStringFormats.Center);

        DrawCellText(gfx, "Архитектор", x1, y2, x2, y3, false, XStringFormats.CenterLeft);
        DrawCellText(gfx, architect, x2, y2, x3, y3, false, XStringFormats.Center);

        DrawCellText(gfx, "Захиалагч", x1, y3, x2, y4, false, XStringFormats.CenterLeft);
        DrawCellText(gfx, ValueOrDash(clientName), x2, y3, x3, y4, false, XStringFormats.Center);
    }

    private static void DrawCornerTableLines(
        XGraphics gfx,
        IReadOnlyList<ConceptCornerTableSegment> lines,
        XPen borderPen,
        XPen finePen)
    {
        foreach (ConceptCornerTableSegment line in lines)
        {
            gfx.DrawLine(
                line.Heavy ? borderPen : finePen,
                Mm(line.X0),
                Mm(line.Y0),
                Mm(line.X1),
                Mm(line.Y1));
        }
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

    /// <summary>
    /// The company officer this table names, which the album labels "Захирал".
    ///
    /// It read the design representative for as long as that field held the
    /// director - one person written into two slots. Now that a chief
    /// architect can be appointed into the design representative, reading it
    /// here would print the architect's name under the word "Захирал".
    /// </summary>
    private static (string Role, string Name) ResolveCompanyRepresentative(AlbumProject project)
    {
        if (!string.IsNullOrWhiteSpace(project.Company.DirectorName))
        {
            return (
                string.IsNullOrWhiteSpace(project.Company.DirectorTitle)
                    ? "Захирал"
                    : project.Company.DirectorTitle,
                project.Company.DirectorName);
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
        DrawDesignCompanyLogo(gfx, company, rect, printedTextHeightMm);
    }

    private const string StudioFallbackLogoResourceName =
        "ErkS.Platform.Pdf.Assets.logo-erks-source.png";
    private const string StudioFallbackLogoPrompt = "\u041b\u043e\u0433\u043e \u0431\u0430\u0439\u0440\u0448\u0443\u0443\u043b";

    private static CompanyProfile ResolveDesignCompanyProfile(AlbumProject project)
    {
        CompanyProfile company = project.Company.Clone();
        company.LogoPath = ResolveAlbumAssetPath(project.ProjectFolder, company.LogoPath);
        return company;
    }

    private static void DrawDesignCompanyLogo(
        XGraphics gfx,
        CompanyProfile company,
        XRect rect,
        double? printedTextHeightMm = null)
    {
        var inner = new XRect(
            rect.X + Mm(1.5),
            rect.Y + Mm(1.5),
            Math.Max(0, rect.Width - Mm(3)),
            Math.Max(0, rect.Height - Mm(3)));
        if (TryDrawCompanyLogo(gfx, company, inner))
        {
            return;
        }

        double availableHeightMm = inner.Height / PointsPerMillimeter;
        double promptHeightMm = printedTextHeightMm is > 0
            ? Math.Clamp(printedTextHeightMm.Value, 1.8, 3.0)
            : Math.Clamp(availableHeightMm * 0.12, 1.8, 3.0);
        double promptHeight = Mm(promptHeightMm * 1.35);
        double gap = Mm(0.8);
        var logoRect = new XRect(
            inner.X,
            inner.Y,
            inner.Width,
            Math.Max(0, inner.Height - promptHeight - gap));
        if (!TryDrawStudioFallbackLogo(gfx, logoRect))
        {
            DrawFittedText(
                gfx,
                "Erk-S",
                logoRect.X,
                logoRect.Y,
                logoRect.Width,
                logoRect.Height,
                9,
                true,
                XStringFormats.Center);
        }
        DrawWrappedCoverText(
            gfx,
            StudioFallbackLogoPrompt,
            new XRect(inner.X, logoRect.Bottom + gap, inner.Width, promptHeight),
            promptHeightMm,
            false,
            XStringFormats.Center,
            WorkingDrawingFontName);
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

    private static bool TryDrawStudioFallbackLogo(XGraphics gfx, XRect rect)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return false;
        }

        try
        {
            using Stream? stream = typeof(PdfSharpAlbumWriter).Assembly
                .GetManifestResourceStream(StudioFallbackLogoResourceName);
            if (stream is null)
            {
                return false;
            }

            using XImage image = XImage.FromStream(stream);
            double containScale = Math.Min(
                rect.Width / image.PointWidth,
                rect.Height / image.PointHeight);
            double width = image.PointWidth * containScale;
            double height = image.PointHeight * containScale;
            gfx.DrawImage(
                image,
                rect.Left + (rect.Width - width) * 0.5,
                rect.Top + (rect.Height - height) * 0.5,
                width,
                height);
            return true;
        }
        catch
        {
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
        double band = Mm(5);
        var outer = new XRect(
            Math.Max(0, rect.Left - band),
            Math.Max(0, rect.Top - band),
            rect.Width + band * 2,
            rect.Height + band * 2);
        gfx.DrawRectangle(pen, outer);
        var step = Mm(50);
        for (var x = rect.Left + step; x < rect.Right; x += step)
        {
            gfx.DrawLine(pen, x, outer.Top, x, rect.Top);
            gfx.DrawLine(pen, x, rect.Bottom, x, outer.Bottom);
        }

        for (var y = rect.Top + step; y < rect.Bottom; y += step)
        {
            gfx.DrawLine(pen, outer.Left, y, rect.Left, y);
            gfx.DrawLine(pen, rect.Right, y, outer.Right, y);
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

        bool vertical = rect.Height > rect.Width;
        var firstLine = rect.Top + rect.Height * (vertical ? 0.24 : 0.34);
        var secondLine = rect.Top + rect.Height * 0.66;
        gfx.DrawLine(finePen, rect.Left, firstLine, rect.Right, firstLine);
        gfx.DrawLine(finePen, rect.Left, secondLine, rect.Right, secondLine);

        var bottomLeftWidth = rect.Width * (vertical ? 0.55 : 0.46);
        var bottomRightWidth = rect.Width * (vertical ? 0.22 : 0.28);
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

        if (!vertical && rect.Width > rect.Height * 3)
        {
            DrawFittedText(gfx, buildPage.Title, rect.Left + rect.Width * 0.52, firstLine + padding,
                rect.Width * 0.46 - padding * 2, secondLine - firstLine - padding * 2, 8, true);
        }
        else if (vertical)
        {
            DrawFittedText(
                gfx,
                buildPage.Title,
                rect.Left + padding,
                firstLine + padding,
                rect.Width - padding * 2,
                secondLine - firstLine - padding * 2,
                8,
                true,
                XStringFormats.Center);
        }
    }

    private static void DrawRevitWorkingTitleBlock(
        XGraphics gfx,
        XRect rect,
        AlbumProject project,
        AlbumBuildPage buildPage,
        XPen borderPen,
        XPen finePen)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;

        bool vertical = rect.Height > rect.Width;
        if (vertical)
        {
            DrawVerticalRevitWorkingTitleBlock(gfx, rect, project, buildPage, borderPen, finePen);
            return;
        }

        if (rect.Height <= Mm(40))
        {
            DrawCanonicalHorizontalWorkingTitleBlockV2(gfx, rect, project, buildPage, borderPen, finePen);
            return;
        }

        // Canonical Revit horizontal corner table: 180 x 55 mm.  The title is
        // a cell in this table; working drawings never receive a second title
        // strip across the top of the drawing field.
        double x60 = rect.Left + rect.Width / 3d;
        double x150 = rect.Left + rect.Width * 5d / 6d;
        double x165 = rect.Left + rect.Width * 11d / 12d;
        double y8 = rect.Top + rect.Height * 8d / 55d;
        double y18 = rect.Top + rect.Height * 18d / 55d;
        double y42 = rect.Top + rect.Height * 42d / 55d;
        double y49 = rect.Top + rect.Height * 49d / 55d;

        foreach (double y in new[] { y8, y18, y42, y49 })
            gfx.DrawLine(finePen, rect.Left, y, rect.Right, y);
        gfx.DrawLine(borderPen, x60, rect.Top, x60, rect.Bottom);
        gfx.DrawLine(borderPen, x150, rect.Top, x150, rect.Bottom);
        gfx.DrawLine(finePen, x165, y42, x165, rect.Bottom);
        gfx.DrawLine(finePen, rect.Left + rect.Width * 19d / 36d, y49, rect.Left + rect.Width * 19d / 36d, rect.Bottom);
        gfx.DrawLine(finePen, rect.Left + rect.Width * 25d / 36d, y49, rect.Left + rect.Width * 25d / 36d, rect.Bottom);

        double pad = Mm(1.5);
        DrawFittedText(gfx, CompanyDisplayName(project.Company, project.DesignOrganizationName),
            rect.Left + pad, rect.Top + pad, x60 - rect.Left - pad * 2, y18 - rect.Top - pad * 2, 7.5, true, XStringFormats.Center);
        DrawFittedText(gfx, ProjectDisplayName(project),
            x60 + pad, rect.Top + pad, x150 - x60 - pad * 2, y18 - rect.Top - pad * 2, 7.5, true, XStringFormats.Center);
        DrawFittedText(gfx, buildPage.Title,
            x60 + pad, y18 + pad, x150 - x60 - pad * 2, y42 - y18 - pad * 2, 9, true, XStringFormats.Center);
        DrawFittedText(gfx, "ҮЕ ШАТ", x150 + pad, rect.Top + pad, rect.Right - x150 - pad * 2, y8 - rect.Top - pad * 2, 5.5, false, XStringFormats.Center);
        DrawFittedText(gfx, "АЖЛЫН ЗУРАГ", x150 + pad, y8 + pad, rect.Right - x150 - pad * 2, y18 - y8 - pad * 2, 6.5, true, XStringFormats.Center);
        DrawFittedText(gfx, "МАСШТАБ", x150 + pad, y18 + pad, rect.Right - x150 - pad * 2, y42 - y18 - pad * 2, 5.5, false, XStringFormats.Center);
        DrawFittedText(gfx, buildPage.Sheet.Entry.ScaleText, x150 + pad, y18 + Mm(8), rect.Right - x150 - pad * 2, y42 - y18 - Mm(9), 7, true, XStringFormats.Center);
        DrawFittedText(gfx, project.Code, rect.Left + pad, y49 + pad, rect.Width * 19d / 36d - pad * 2, rect.Bottom - y49 - pad * 2, 5.5, false);
        DrawFittedText(gfx, buildPage.Number, rect.Left + rect.Width * 19d / 36d + pad, y49 + pad, rect.Width / 6d - pad * 2, rect.Bottom - y49 - pad * 2, 7, true, XStringFormats.Center);
        DrawFittedText(gfx, string.IsNullOrWhiteSpace(buildPage.Sheet.Entry.Revision) ? "R0" : buildPage.Sheet.Entry.Revision,
            x165 + pad, y42 + pad, rect.Right - x165 - pad * 2, rect.Bottom - y42 - pad * 2, 6, false, XStringFormats.Center);
    }

    private static void DrawCanonicalHorizontalWorkingTitleBlock(
        XGraphics gfx,
        XRect rect,
        AlbumProject project,
        AlbumBuildPage buildPage,
        XPen borderPen,
        XPen finePen)
    {
        double X(double mm) => rect.Left + Mm(mm);
        double Y(double mm) => rect.Top + Mm(mm);
        double pad = Mm(0.8);
        double[] xs = [25, 45, 75, 105, 125, 145, 165];
        double[] ys = [8, 15, 22, 29];

        gfx.DrawLine(borderPen, X(25), rect.Top, X(25), rect.Bottom);
        gfx.DrawLine(finePen, X(25), Y(8), rect.Right, Y(8));
        foreach (double y in ys.Skip(1))
            gfx.DrawLine(finePen, X(25), Y(y), rect.Right, Y(y));
        foreach (double x in xs.Skip(1))
            gfx.DrawLine(finePen, X(x), Y(8), X(x), rect.Bottom);

        DrawFittedText(gfx, "R standard", rect.Left + pad, rect.Top + pad, Mm(25) - pad * 2, rect.Height - pad * 2, 8, true, XStringFormats.Center);
        DrawFittedText(gfx, buildPage.Title, X(25) + pad, rect.Top + pad, rect.Right - X(25) - pad * 2, Mm(8) - pad * 2, 6.5, true);
        DrawFittedText(gfx, "Архитектор", X(25) + pad, Y(8) + pad, Mm(20) - pad * 2, Mm(7) - pad * 2, 5.2, false);
        DrawFittedText(gfx, "Гүйцэтгэсэн", X(25) + pad, Y(15) + pad, Mm(20) - pad * 2, Mm(7) - pad * 2, 5.2, false);
        DrawFittedText(gfx, "Шалгасан", X(25) + pad, Y(22) + pad, Mm(20) - pad * 2, Mm(7) - pad * 2, 5.2, false);
        DrawFittedText(gfx, ProjectDisplayName(project), X(45) + pad, Y(8) + pad, Mm(60) - pad * 2, Mm(21) - pad * 2, 6, true, XStringFormats.Center);
        DrawFittedText(gfx, "ЕГ шифр:", X(105) + pad, Y(8) + pad, Mm(20) - pad * 2, Mm(7) - pad * 2, 4.8, false);
        DrawFittedText(gfx, "Масштаб:", X(145) + pad, Y(8) + pad, Mm(20) - pad * 2, Mm(7) - pad * 2, 4.8, false);
        DrawFittedText(gfx, "Огноо:", X(165) + pad, Y(8) + pad, rect.Right - X(165) - pad * 2, Mm(7) - pad * 2, 4.8, false);
        DrawFittedText(gfx, "ТГ шифр:", X(105) + pad, Y(15) + pad, Mm(20) - pad * 2, Mm(7) - pad * 2, 4.8, false);
        // The values beside the two cipher labels and the date. They were drawn
        // as labels with nothing next to them: three cells that looked like a
        // rendering fault and were in fact three fields nobody had. Empty stays
        // empty - a cipher is issued outside Studio and a date from the clock
        // would change on every rebuild.
        DrawFittedText(gfx, project.GeneralDesignCipher, X(125) + pad, Y(8) + pad, Mm(20) - pad * 2, Mm(7) - pad * 2, 5, false);
        DrawFittedText(gfx, project.TechnicalDesignCipher, X(125) + pad, Y(22) + pad, Mm(20) - pad * 2, Mm(7) - pad * 2, 5, false);
        DrawFittedText(
            gfx,
            project.SheetDateUtc is { } sheetDate ? sheetDate.ToLocalTime().ToString("yyyy-MM-dd") : "",
            X(165) + pad,
            Y(15) + pad,
            rect.Right - X(165) - pad * 2,
            Mm(7) - pad * 2,
            5,
            false);
        DrawFittedText(gfx, "Зургийн марк:", X(125) + pad, Y(15) + pad, Mm(20) - pad * 2, Mm(7) - pad * 2, 4.8, false);
        DrawFittedText(gfx, "Хуудас:", X(145) + pad, Y(15) + pad, Mm(20) - pad * 2, Mm(7) - pad * 2, 4.8, false);
        DrawFittedText(gfx, buildPage.Sheet.Entry.ScaleText, X(145) + pad, Y(22) + pad, Mm(20) - pad * 2, Mm(7) - pad * 2, 5.5, true, XStringFormats.Center);
        DrawFittedText(gfx, project.Code, X(105) + pad, Y(29) + pad, Mm(40) - pad * 2, rect.Bottom - Y(29) - pad * 2, 5, false);
        DrawFittedText(gfx, buildPage.Sheet.Entry.Discipline, X(145) + pad, Y(29) + pad, Mm(20) - pad * 2, rect.Bottom - Y(29) - pad * 2, 5.2, true, XStringFormats.Center);
        DrawFittedText(gfx, buildPage.Number, X(165) + pad, Y(29) + pad, rect.Right - X(165) - pad * 2, rect.Bottom - Y(29) - pad * 2, 5.5, true, XStringFormats.Center);
    }

    private static void DrawCanonicalHorizontalWorkingTitleBlockV2(
        XGraphics gfx,
        XRect rect,
        AlbumProject project,
        AlbumBuildPage buildPage,
        XPen borderPen,
        XPen finePen)
    {
        double X(double mm) => rect.Left + Mm(mm);
        double Y(double mm) => rect.Top + Mm(mm);
        double pad = Mm(0.65);
        double standardTextSize = Mm(CoverFontEmSizeMm(2.5));
        double[] columns = [27, 48, 74, 101, 127, 153];

        gfx.DrawLine(borderPen, X(27), rect.Top, X(27), rect.Bottom);
        gfx.DrawLine(finePen, X(27), Y(11), rect.Right, Y(11));
        gfx.DrawLine(finePen, X(27), Y(17), rect.Right, Y(17));
        // Signature area has three rows; the metadata area on the right has
        // exactly two rows in the canonical horizontal family.
        gfx.DrawLine(finePen, X(27), Y(23), X(101), Y(23));
        gfx.DrawLine(finePen, X(27), Y(30), X(101), Y(30));
        gfx.DrawLine(finePen, X(101), Y(26.5), rect.Right, Y(26.5));
        foreach (double x in columns.Skip(1))
            gfx.DrawLine(finePen, X(x), Y(17), X(x), rect.Bottom);

        var logoCell = new XRect(rect.Left, Y(2), Mm(27), Mm(27));
        gfx.DrawRectangle(finePen, logoCell);
        CompanyProfile company = ResolveDesignCompanyProfile(project);
        DrawDesignCompanyLogo(gfx, company, logoCell);
        DrawWrappedCoverText(gfx, ProjectDisplayName(project),
            new XRect(X(27) + pad, rect.Top + pad, rect.Right - X(27) - pad * 2, Mm(5.5) - pad),
            2.5, false, XStringFormats.Center, WorkingDrawingFontName);
        DrawWrappedCoverText(gfx, project.InitiationBasis.SiteAddress,
            new XRect(X(27) + pad, Y(5.5), rect.Right - X(27) - pad * 2, Mm(5.5) - pad),
            2.5, false, XStringFormats.Center, WorkingDrawingFontName);
        DrawWrappedCoverText(gfx, buildPage.Title,
            new XRect(X(27) + pad, Y(11) + pad, rect.Right - X(27) - pad * 2, Mm(6) - pad * 2),
            2.5, false, XStringFormats.CenterLeft, WorkingDrawingFontName);

        DrawFittedText(gfx, "\u0410\u0440\u0445\u0438\u0442\u0435\u043a\u0442\u043e\u0440", X(27) + pad, Y(17) + pad, Mm(21) - pad * 2, Mm(6) - pad * 2, standardTextSize, false, fontName: WorkingDrawingFontName);
        DrawFittedText(gfx, "\u0413\u04af\u0439\u0446\u044d\u0442\u0433\u044d\u0441\u044d\u043d", X(27) + pad, Y(23) + pad, Mm(21) - pad * 2, Mm(7) - pad * 2, standardTextSize, false, fontName: WorkingDrawingFontName);
        DrawFittedText(gfx, "\u0428\u0430\u043b\u0433\u0430\u0441\u0430\u043d", X(27) + pad, Y(30) + pad, Mm(21) - pad * 2, rect.Bottom - Y(30) - pad * 2, standardTextSize, false, fontName: WorkingDrawingFontName);
        IReadOnlyList<string> names = ResolveCanonicalHorizontalWorkingTitleBlockNames(
            project,
            buildPage.Definition.RoleAssignments);
        for (int row = 0; row < names.Count; row++)
        {
            double top = row == 0 ? 17 : row == 1 ? 23 : 30;
            double bottom = row == 0 ? 23 : row == 1 ? 30 : 36;
            DrawFittedText(gfx, names[row], X(48) + pad, Y(top) + pad,
                Mm(26) - pad * 2, Mm(bottom - top) - pad * 2, standardTextSize, false, XStringFormats.Center, WorkingDrawingFontName);
        }

        double metadataFont = standardTextSize;
        // The project CODE is Studio's own number (STUDIO-20260722-1906) and
        // not an official cipher; printing it here said a code had been issued
        // when none had. The cipher is entered, and an unfilled cell keeps its
        // label so it reads as a form field rather than a rendering fault.
        DrawFittedText(gfx, LabelledCell("\u0415\u0413 \u0448\u0438\u0444\u0440", project.GeneralDesignCipher), X(101) + pad, Y(17) + pad, Mm(26) - pad * 2, Mm(9.5) - pad * 2, metadataFont, false, fontName: WorkingDrawingFontName);
        DrawFittedText(gfx, $"\u041c\u0430\u0441\u0448\u0442\u0430\u0431: {buildPage.ScaleText}", X(127) + pad, Y(17) + pad, Mm(26) - pad * 2, Mm(9.5) - pad * 2, metadataFont, false, fontName: WorkingDrawingFontName);
        // Taken from the CLOCK, this moved every time the album was rebuilt -
        // a document date that meant "whenever this was last regenerated".
        // The sheet date is entered and stored for exactly that reason.
        DrawFittedText(gfx, LabelledCell("\u041e\u0433\u043d\u043e\u043e", SheetDateText(project)), X(153) + pad, Y(17) + pad, rect.Right - X(153) - pad * 2, Mm(9.5) - pad * 2, metadataFont, false, fontName: WorkingDrawingFontName);
        DrawFittedText(gfx, LabelledCell("\u0422\u0413 \u0448\u0438\u0444\u0440", project.TechnicalDesignCipher), X(101) + pad, Y(26.5) + pad, Mm(26) - pad * 2, rect.Bottom - Y(26.5) - pad * 2, metadataFont, false, fontName: WorkingDrawingFontName);
        DrawFittedText(gfx, $"\u0417\u0443\u0440\u0433\u0438\u0439\u043d \u043c\u0430\u0440\u043a: {buildPage.Sheet.Entry.Discipline}", X(127) + pad, Y(26.5) + pad, Mm(26) - pad * 2, rect.Bottom - Y(26.5) - pad * 2, metadataFont, false, fontName: WorkingDrawingFontName);
        DrawFittedText(gfx, $"\u0425\u0443\u0443\u0434\u0430\u0441: {buildPage.Number}", X(153) + pad, Y(26.5) + pad, rect.Right - X(153) - pad * 2, rect.Bottom - Y(26.5) - pad * 2, metadataFont, false, fontName: WorkingDrawingFontName);
    }

    /// <summary>
    /// A corner-table cell that is a LABELLED FIELD: the label always prints,
    /// the value joins it when there is one.
    ///
    /// Dropping the label with the value would leave a blank rectangle that
    /// reads as a rendering fault - which is how three of these cells were
    /// reported. Inventing a value to fill it is the other way to be wrong, and
    /// the more expensive one on a document somebody signs.
    /// </summary>
    internal static string LabelledCell(string label, string? value)
    {
        string text = (value ?? "").Trim();
        return text.Length == 0 ? label + ":" : label + ": " + text;
    }

    /// <summary>
    /// The year the corner table prints: the entered sheet date's year when
    /// there is one, otherwise the current year.
    ///
    /// Unlike the labelled ЕГ/ТГ cells, this one is a bare value in a ruled
    /// grid, where an empty cell reads as a broken table rather than as an
    /// unfilled field - so the clock stays as the fallback here and the entered
    /// date simply wins over it.
    ///
    /// USED IN TWO PLACES ON PURPOSE. The restamp signature hashes this year to
    /// decide whether a built album still matches the project; if the signature
    /// read the clock while the table read the sheet date, changing the date
    /// would redraw the table without changing the signature, and the album
    /// would be left stamped with the old year and reported as current.
    /// </summary>
    internal static int CornerTableYear(AlbumProject project) =>
        project.SheetDateUtc?.ToLocalTime().Year ?? DateTime.Now.Year;

    /// <summary>
    /// The sheet date as ENTERED, never as measured now. Empty when it has not
    /// been entered: a date is a fact about the document, and a rebuild is not
    /// an event that changes it.
    /// </summary>
    internal static string SheetDateText(AlbumProject project) =>
        project.SheetDateUtc is { } sheetDate
            ? sheetDate.ToLocalTime().ToString("yyyy.MM.dd")
            : "";

    internal static IReadOnlyList<string> ResolveCanonicalHorizontalWorkingTitleBlockNames(
        AlbumProject project,
        IEnumerable<AlbumPageRoleAssignment>? roleAssignments = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        return
        [
            AlbumPageRoleAssignmentResolver.ResolveDocumentName(
                roleAssignments,
                AlbumPageRoleCodes.Architect,
                project.Participants) ?? ResolveArchitect(project),
            AlbumPageRoleAssignmentResolver.ResolveDocumentName(
                roleAssignments,
                AlbumPageRoleCodes.PreparedBy,
                project.Participants) ?? ResolveWorkingDrawingSigner(
                    project.Company.Signers,
                    "Гүйцэтгэсэн",
                    "Боловсруулсан",
                    "Prepared",
                    "Drawn"),
            AlbumPageRoleAssignmentResolver.ResolveDocumentName(
                roleAssignments,
                AlbumPageRoleCodes.CheckedBy,
                project.Participants) ?? ResolveWorkingDrawingSigner(
                    project.Company.Signers,
                    "Шалгасан",
                    "Хянасан",
                    "Checked",
                    "Reviewed"),
        ];
    }

    private static string ResolveWorkingDrawingSigner(
        IEnumerable<CompanySigner>? signers,
        params string[] roleMarkers)
    {
        CompanySigner? signer = (signers ?? Array.Empty<CompanySigner>())
            .FirstOrDefault(candidate => roleMarkers.Any(marker =>
                candidate.Role?.Contains(marker, StringComparison.OrdinalIgnoreCase) == true));
        return signer?.FullName?.Trim() ?? "";
    }

    private static void DrawVerticalRevitWorkingTitleBlock(
        XGraphics gfx,
        XRect rect,
        AlbumProject project,
        AlbumBuildPage buildPage,
        XPen borderPen,
        XPen finePen)
    {
        double y60 = rect.Top + rect.Height / 3d;
        double y150 = rect.Top + rect.Height * 5d / 6d;
        double x8 = rect.Left + rect.Width * 8d / 55d;
        double x47 = rect.Right - rect.Width * 8d / 55d;
        gfx.DrawLine(borderPen, rect.Left, y60, rect.Right, y60);
        gfx.DrawLine(borderPen, rect.Left, y150, rect.Right, y150);
        gfx.DrawLine(finePen, x8, rect.Top, x8, rect.Bottom);
        gfx.DrawLine(finePen, x47, rect.Top, x47, rect.Bottom);
        double pad = Mm(1.5);
        DrawFittedText(gfx, CompanyDisplayName(project.Company, project.DesignOrganizationName), x8 + pad, rect.Top + pad, x47 - x8 - pad * 2, y60 - rect.Top - pad * 2, 7, true, XStringFormats.Center);
        DrawFittedText(gfx, buildPage.Title, x8 + pad, y60 + pad, x47 - x8 - pad * 2, y150 - y60 - pad * 2, 8, true, XStringFormats.Center);
        DrawFittedText(gfx, buildPage.Number, x8 + pad, y150 + pad, x47 - x8 - pad * 2, rect.Bottom - y150 - pad * 2, 8, true, XStringFormats.Center);
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
        XStringFormat? format = null,
        string fontName = FontName)
    {
        if (string.IsNullOrWhiteSpace(text) || width <= 2 || height <= 2)
        {
            return;
        }

        var size = preferredSize;
        XFont font;
        do
        {
            font = new XFont(fontName, size, bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);
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
                if (UsesGeneratedWorkingDrawingFormat(request.Project.Album))
                {
                    DrawAlbumFormatCoverPage(document, request, plan);
                }
                else if (AlbumCoverStyle.UsesApprovalCover(request.Project.Album.TemplateId))
                {
                    DrawConceptCoverPage(document, request, plan.Component);
                }
                else if (request.Project.Album.TemplateId.Equals(
                             BuildingWorkingDrawingAlbumTemplate.TemplateId,
                             StringComparison.OrdinalIgnoreCase))
                {
                    DrawWorkingDrawingCoverPage(document, request, plan.Component);
                }
                else
                {
                    DrawCoverPage(document, request);
                }
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
            case AlbumGeneratedPageKind.None:
                // Non-building templates use a Studio-owned drawing list and
                // explanatory-notes front-matter page after the cover.
                if (UsesGeneratedWorkingDrawingFormat(request.Project.Album) ||
                    request.Project.Album.TemplateId.Equals(
                        BuildingWorkingDrawingAlbumTemplate.TemplateId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    DrawWorkingDrawingTableOfContents(document, request, plan.Component);
                }
                else
                {
                    DrawTableOfContents(document, request);
                }
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

    private static PdfPage AddGeneratedPage(PdfDocument document, AlbumDefinition album)
    {
        if (!UsesGeneratedWorkingDrawingFormat(album))
        {
            return AddA3LandscapePage(document);
        }

        PageFormatDefinition format = WorkingDrawingAlbumFormatFactory.Resolve(album);
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(format.WidthMm);
        page.Height = XUnit.FromMillimeter(format.HeightMm);
        return page;
    }

    private static bool UsesGeneratedWorkingDrawingFormat(AlbumDefinition album) =>
        PageFormatCatalog.IsUsable(album.GeneratedPageFormat) &&
        album.GeneratedPageFormat!.Kind == PageFormatKind.WorkingDrawing;

    private static void DrawBuildingSubCoverPage(
        PdfDocument document,
        AlbumProject project,
        string buildingName)
    {
        PdfPage page = AddA3LandscapePage(document);
        using var gfx = XGraphics.FromPdfPage(page);
        var border = new XPen(XColors.Black, Mm(0.25));
        CompanyProfile company = ResolveDesignCompanyProfile(project);
        string companyName = CompanyLegalDisplayName(company, project.DesignOrganizationName);

        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        gfx.DrawRectangle(border, ToPoints(BuildingArchitectureConceptPageLayout.Frame));

        DrawDesignCompanyLogo(
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



    private static void DrawCanonicalA3ApprovalCoverPage(
        PdfDocument document,
        AlbumBuildRequest request,
        AlbumCompositionItem item,
        bool drawWorkingDrawingEtalon = false)
    {
        var page = AddA3LandscapePage(document);
        using var gfx = XGraphics.FromPdfPage(page);
        CoverFontContexts.Add(
            gfx,
            new CoverFontContext(drawWorkingDrawingEtalon ? WorkingDrawingFontName : FontName));
        var border = new XPen(XColors.Black, Mm(0.25));
        var fine = new XPen(XColors.Black, Mm(0.10));
        // The skin picks the geometry, once, here. One routine draws two covers
        // that answer to different authorities: the working-drawing cover
        // reproduces what Revit drew - PFR measured its eight vertical rules off
        // a real exported sheet, and every one agrees with the contract to
        // within 0.02 mm - while the concept album's cover is Studio's own and
        // the user confirmed on 2026-09-06 that its different column split is
        // deliberate, not a mistake.
        CoverApprovalTableGrid grid = CoverApprovalTableGrid.For(drawWorkingDrawingEtalon);
        CompanyProfile company = ResolveDesignCompanyProfile(request.Project);
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
        string clientType = ProjectClientTypes.Recognize(initiationBasis.ClientType);
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
        var reviewRows = BuildCoverReviewRows(gfx, grid, approvalSnapshot.EndorsedBy);
        var processedColumn = BuildCoverProcessedColumn(
            gfx,
            grid,
            companyRole,
            companyRepresentative.Name,
            clientRole,
            clientRepresentativeName);
        var reviewTableBottomMm = reviewRows.Count == 0 ? 93.86 : reviewRows[^1].BottomMm;
        var tableBottomMm = Math.Min(reviewTableBottomMm, processedColumn.BottomMm);

        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        if (drawWorkingDrawingEtalon)
        {
            WorkingDrawingPageRegions coverRegions = WorkingDrawingPageLayout.Resolve(
                PageFormatCatalog.DefaultWorkingDrawing);
            DrawEtalonGrid(gfx, coverRegions, border, fine);
        }
        else
        {
            gfx.DrawRectangle(border, ToPoints(BuildingArchitectureConceptPageLayout.Frame));
        }

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
        string coverTypeTitle = AlbumCoverStyle.Resolve(
            request.Project.Album.TemplateId,
            drawWorkingDrawingEtalon);
        DrawCoverText(
            gfx,
            coverTypeTitle,
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
        DrawSketchCoverApprovalTable(gfx, grid, border, fine, reviewRows, processedColumn, tableBottomMm);

        DrawCoverCellText(gfx, "Албан тушаал", grid.TableLeft, 153.86, grid.ReviewRoleRight, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Нэр", grid.ReviewRoleRight, 153.86, grid.ReviewNameRight, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Гарын үсэг", grid.ReviewNameRight, 153.86, grid.ReviewRight, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Албан тушаал", grid.CompanyLogoRight, 153.86, grid.CompanyRoleRight, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Нэр", grid.CompanyRoleRight, 153.86, grid.CompanyNameRight, 161.86, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Гарын үсэг", grid.CompanyNameRight, 153.86, grid.TableRight, 161.86, bodyTextHeightMm, false, XStringFormats.Center);

        foreach (var row in reviewRows)
        {
            DrawCoverCellText(gfx, ConceptCoverApprovalResolver.DisplayPosition(row.Entry), grid.TableLeft, row.BottomMm, grid.ReviewRoleRight, row.TopMm, bodyTextHeightMm, false, XStringFormats.CenterLeft, 2.0);
            DrawCoverCellText(gfx, row.Entry.PersonName, grid.ReviewRoleRight, row.BottomMm, grid.ReviewNameRight, row.TopMm, bodyTextHeightMm, false, XStringFormats.Center);
        }

        DrawCoverCellText(gfx, BuildingArchitectureConceptPageLayout.CoverProcessedTopSectionTitle, grid.ReviewRight, processedColumn.TopHeaderBottomMm, grid.CompanyLogoRight, BuildingArchitectureConceptPageLayout.CoverTableTopMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCompanyLogoOrMark(gfx, company, CoverRect(grid.ReviewRight, processedColumn.TopDataBottomMm, grid.CompanyLogoRight, processedColumn.TopHeaderBottomMm), bodyTextHeightMm);
        DrawCoverCellText(gfx, companyRole, grid.CompanyLogoRight, processedColumn.TopDataBottomMm, grid.CompanyRoleRight, processedColumn.TopHeaderBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, companyRepresentative.Name, grid.CompanyRoleRight, processedColumn.TopDataBottomMm, grid.CompanyNameRight, processedColumn.TopHeaderBottomMm, bodyTextHeightMm, false, XStringFormats.Center);

        DrawCoverCellText(gfx, BuildingArchitectureConceptPageLayout.CoverProcessedBottomSectionTitle, grid.ReviewRight, processedColumn.BottomHeaderBottomMm, grid.CompanyLogoRight, processedColumn.TopDataBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Албан тушаал", grid.CompanyLogoRight, processedColumn.BottomHeaderBottomMm, grid.CompanyRoleRight, processedColumn.TopDataBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Нэр", grid.CompanyRoleRight, processedColumn.BottomHeaderBottomMm, grid.CompanyNameRight, processedColumn.TopDataBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, "Гарын үсэг", grid.CompanyNameRight, processedColumn.BottomHeaderBottomMm, grid.TableRight, processedColumn.TopDataBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        if (ProjectClientTypes.UsesLogo(clientType))
        {
            DrawCompanyLogoOnly(
                gfx,
                clientOrganization,
                CoverRect(
                    grid.ReviewRight,
                    tableBottomMm,
                    grid.CompanyLogoRight,
                    processedColumn.BottomHeaderBottomMm));
        }
        DrawCoverCellText(gfx, clientRole, grid.CompanyLogoRight, tableBottomMm, grid.CompanyRoleRight, processedColumn.BottomHeaderBottomMm, bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverCellText(gfx, ValueOrDash(clientRepresentativeName), grid.CompanyRoleRight, tableBottomMm, grid.CompanyNameRight, processedColumn.BottomHeaderBottomMm, bodyTextHeightMm, false, XStringFormats.Center);

        // The DESIGN ORGANISATION's registered city, not the project's location -
        // decided in corner-table-space-contract-2026-09-06.json. It was a
        // constant, so every cover this program has ever produced said
        // Ulaanbaatar, including one issued by a company registered anywhere
        // else. Empty when the organisation has not recorded one: PFR removed
        // the identical default on their side (a9541d1) rather than inventing a
        // city, and the two covers have to say the same thing.
        DrawCoverText(gfx, company.RegisteredCity, CoverCenteredRect(210.0, 26.125, 200.0, 12.0), bodyTextHeightMm, false, XStringFormats.Center);
        DrawCoverText(gfx, $"{DateTime.Now:yyyy} он", CoverCenteredRect(210.0, 15.625, 90.0, 12.0), bodyTextHeightMm, false, XStringFormats.Center);
    }

    private static void DrawConceptCoverPage(
        PdfDocument document,
        AlbumBuildRequest request,
        AlbumCompositionItem item)
    {
        DrawCanonicalA3ApprovalCoverPage(
            document,
            request,
            item,
            drawWorkingDrawingEtalon: false);
    }

    private static void DrawWorkingDrawingCoverPage(
        PdfDocument document,
        AlbumBuildRequest request,
        AlbumCompositionItem item)
    {
        // The working cover has its own semantic identity and title. It shares
        // only the approval-table geometry with the concept cover and uses the
        // same etalon-grid generator as every working-drawing sheet.
        DrawCanonicalA3ApprovalCoverPage(
            document,
            request,
            item,
            drawWorkingDrawingEtalon: true);
    }

    private static void DrawAlbumFormatCoverPage(
        PdfDocument document,
        AlbumBuildRequest request,
        ConceptGeneratedPagePlan plan)
    {
        PageFormatDefinition format = WorkingDrawingAlbumFormatFactory.Resolve(
            request.Project.Album);
        PdfPage page = AddGeneratedPage(document, request.Project.Album);
        using XGraphics gfx = XGraphics.FromPdfPage(page);
        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        WorkingDrawingPageRegions regions = WorkingDrawingPageLayout.Resolve(format);
        WorkingDrawingGeneratedPageChrome chrome =
            WorkingDrawingGeneratedPageChromePolicy.Resolve(
                AlbumGeneratedPageKind.Cover);
        var borderPen = new XPen(XColors.Black, Mm(0.35));
        var finePen = new XPen(XColors.Black, Mm(0.10));
        DrawEtalonGrid(gfx, regions, borderPen, finePen);
        if (chrome.ShowSheetHeader || chrome.ShowTitleBlock)
        {
            AlbumBuildPage generatedPage = CreateWorkingGeneratedPage(
                format,
                plan.Number,
                plan.Title,
                plan.Component.RoleAssignments);
            if (chrome.ShowSheetHeader)
            {
                DrawRevitWorkingSheetHeader(
                    gfx,
                    ToPoints(regions.SheetTitleArea),
                    generatedPage,
                    borderPen);
            }
            if (chrome.ShowTitleBlock)
            {
                XRect corner = ToPoints(regions.TitleBlockArea);
                gfx.DrawRectangle(borderPen, corner);
                DrawRevitWorkingTitleBlock(
                    gfx,
                    corner,
                    request.Project,
                    generatedPage,
                    borderPen,
                    finePen);
            }
        }
        gfx.DrawRectangle(
            new XPen(XColor.FromArgb(185, 190, 196), 0.25),
            new XRect(0, 0, page.Width.Point, page.Height.Point));

        double left = Mm(regions.EtalonInnerFrame.X + 18d);
        double right = Mm(
            regions.EtalonInnerFrame.X + regions.EtalonInnerFrame.Width - 18d);
        double top = Mm(regions.EtalonInnerFrame.Y + 18d);
        double bottom = Mm(
            regions.EtalonInnerFrame.Y + regions.EtalonInnerFrame.Height - 18d);
        double width = Math.Max(Mm(40d), right - left);
        double height = Math.Max(Mm(70d), bottom - top);
        double centerY = top + height * 0.5;

        CompanyProfile company = ResolveDesignCompanyProfile(request.Project);
        DrawDesignCompanyLogo(
            gfx,
            company,
            new XRect(left + width * 0.4, top + height * 0.03, width * 0.2, height * 0.16));
        DrawFittedText(
            gfx,
            CompanyLegalDisplayName(company, request.Project.DesignOrganizationName),
            left,
            top + height * 0.20,
            width,
            height * 0.08,
            15,
            true,
            XStringFormats.Center);
        DrawFittedText(
            gfx,
            ValueOrDash(request.Project.InitiationBasis.SiteAddress),
            left,
            top + height * 0.33,
            width,
            height * 0.07,
            12,
            false,
            XStringFormats.Center);
        DrawFittedText(
            gfx,
            ProjectDisplayName(request.Project),
            left,
            centerY - height * 0.08,
            width,
            height * 0.12,
            25,
            true,
            XStringFormats.Center);
        DrawFittedText(
            gfx,
            request.Project.Album.Title,
            left,
            centerY + height * 0.06,
            width,
            height * 0.10,
            18,
            true,
            XStringFormats.Center);
        DrawFittedText(
            gfx,
            $"{DateTime.Now:yyyy} ОН",
            left,
            bottom - height * 0.08,
            width,
            height * 0.06,
            11,
            false,
            XStringFormats.Center);
    }

    private static void DrawSketchCoverApprovalTable(
        XGraphics gfx,
        CoverApprovalTableGrid grid,
        XPen border,
        XPen fine,
        IReadOnlyList<CoverReviewRow> reviewRows,
        CoverProcessedColumn processedColumn,
        double tableBottomMm)
    {
        double x0 = grid.TableLeft;
        var y0 = tableBottomMm;
        double x1 = grid.TableRight;
        const double y1 = BuildingArchitectureConceptPageLayout.CoverTableTopMm;
        double rightX0 = grid.ReviewRight;
        const double headerY0 = BuildingArchitectureConceptPageLayout.CoverColumnHeaderBottomMm;

        DrawCoverLine(gfx, border, x0, y0, x1, y0);
        DrawCoverLine(gfx, border, x0, y1, x1, y1);
        DrawCoverLine(gfx, border, x0, y0, x0, y1);
        DrawCoverLine(gfx, border, x1, y0, x1, y1);
        DrawCoverLine(gfx, border, x0, headerY0, x1, headerY0);
        DrawCoverLine(gfx, border, rightX0, y0, rightX0, y1);

        DrawCoverLine(gfx, fine, grid.ReviewRoleRight, y0, grid.ReviewRoleRight, y1);
        DrawCoverLine(gfx, fine, grid.ReviewNameRight, y0, grid.ReviewNameRight, y1);
        DrawCoverLine(gfx, fine, grid.CompanyLogoRight, processedColumn.TopDataBottomMm, grid.CompanyLogoRight, y1);
        DrawCoverLine(gfx, fine, grid.CompanyRoleRight, processedColumn.TopDataBottomMm, grid.CompanyRoleRight, y1);
        DrawCoverLine(gfx, fine, grid.CompanyNameRight, processedColumn.TopDataBottomMm, grid.CompanyNameRight, y1);
        DrawCoverLine(gfx, fine, grid.CompanyLogoRight, y0, grid.CompanyLogoRight, processedColumn.TopDataBottomMm);
        DrawCoverLine(gfx, fine, grid.CompanyRoleRight, y0, grid.CompanyRoleRight, processedColumn.TopDataBottomMm);
        DrawCoverLine(gfx, fine, grid.CompanyNameRight, y0, grid.CompanyNameRight, processedColumn.TopDataBottomMm);

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
        double horizontalInsetMm = 1.2,
        string? fontName = null)
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
            format,
            fontName);
    }

    private static void DrawCoverText(
        XGraphics gfx,
        string? text,
        XRect rect,
        double printedTextHeightMm,
        bool bold,
        XStringFormat format,
        string? fontName = null) =>
        DrawWrappedCoverText(gfx, text, rect, printedTextHeightMm, bold, format, fontName);

    private static void DrawWrappedCoverText(
        XGraphics gfx,
        string? text,
        XRect rect,
        double printedTextHeightMm,
        bool bold,
        XStringFormat format,
        string? fontName = null)
    {
        if (string.IsNullOrWhiteSpace(text) || rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        string resolvedFontName = fontName ??
            (CoverFontContexts.TryGetValue(gfx, out CoverFontContext? context)
                ? context.FontName
                : FontName);
        (XFont font, double fittedTextHeightMm) = CreateCoverFontToFitLongestWord(
            gfx,
            text,
            rect.Width,
            printedTextHeightMm,
            bold,
            resolvedFontName);
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
        CreateCoverFont(printedTextHeightMm, bold, FontName);

    private static XFont CreateCoverFont(double printedTextHeightMm, bool bold, string fontName) =>
        new(
            fontName,
            Mm(CoverFontEmSizeMm(printedTextHeightMm)),
            bold ? XFontStyleEx.Bold : XFontStyleEx.Regular);

    private static (XFont Font, double PrintedTextHeightMm) CreateCoverFontToFitLongestWord(
        XGraphics gfx,
        string text,
        double maxWidth,
        double printedTextHeightMm,
        bool bold,
        string fontName = FontName)
    {
        XFont font = CreateCoverFont(printedTextHeightMm, bold, fontName);
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
        return (CreateCoverFont(fittedTextHeightMm, bold, fontName), fittedTextHeightMm);
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
        CoverApprovalTableGrid grid,
        IReadOnlyList<ProjectApprovalEntry> approvals)
    {
        const double rowsTopMm = BuildingArchitectureConceptPageLayout.CoverColumnHeaderBottomMm;
        const double baseRowsHeightMm = BuildingArchitectureConceptPageLayout.CoverReviewRowsBaseHeightMm;
        double roleTextWidthMm = grid.ReviewRoleRight - grid.TableLeft - 2.4;
        double nameTextWidthMm = grid.ReviewNameRight - grid.ReviewRoleRight - 2.4;
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
        CoverApprovalTableGrid grid,
        string companyRole,
        string companyRepresentativeName,
        string clientTypeLabel,
        string clientName)
    {
        const double topHeaderBottomMm = BuildingArchitectureConceptPageLayout.CoverColumnHeaderBottomMm;
        const double titleHeightMm = BuildingArchitectureConceptPageLayout.CoverSectionHeaderHeightMm;
        const double baseClientDataHeightMm = BuildingArchitectureConceptPageLayout.CoverClientDataBaseHeightMm;
        const double baseCompanyDataHeightMm = BuildingArchitectureConceptPageLayout.CoverCompanyDataBaseHeightMm;
        double roleTextWidthMm = grid.CompanyRoleRight - grid.CompanyLogoRight - 2.4;
        double nameTextWidthMm = grid.CompanyNameRight - grid.CompanyRoleRight - 2.4;
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
        var page = AddGeneratedPage(document, request.Project.Album);
        using var gfx = XGraphics.FromPdfPage(page);
        DrawGeneratedPageChrome(
            gfx,
            page,
            request.Project,
            plan.Title,
            plan.Number,
            plan.Component.RoleAssignments);
        DrawGeneratedDocumentContent(gfx, request.Project, plan);
    }

    private static void DrawPlanningTaskPage(
        PdfDocument document,
        AlbumBuildRequest request,
        ConceptGeneratedPagePlan plan)
    {
        var page = AddGeneratedPage(document, request.Project.Album);
        using var gfx = XGraphics.FromPdfPage(page);
        DrawGeneratedPageChrome(
            gfx,
            page,
            request.Project,
            plan.Title,
            plan.Number,
            plan.Component.RoleAssignments);

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
        PdfPage page = AddGeneratedPage(document, request.Project.Album);
        using XGraphics gfx = XGraphics.FromPdfPage(page);
        DrawGeneratedPageChrome(
            gfx,
            page,
            request.Project,
            plan.Title,
            plan.Number,
            plan.Component.RoleAssignments);

        (PageRectMm locationPanel,
            PageRectMm locationMap,
            PageRectMm overviewPanel,
            PageRectMm overviewMap) = ResolveSiteContextRegions(request.Project.Album);

        DrawSiteContextMapPanel(
            gfx,
            request.Project,
            request.Project.SiteContext.LocationScheme,
            locationPanel,
            locationMap,
            "БАЙРШЛЫН СХЕМ");
        DrawSiteContextMapPanel(
            gfx,
            request.Project,
            request.Project.SiteContext.SurroundingsOverview,
            overviewPanel,
            overviewMap,
            "ОРЧНЫ ТОЙМ");
    }

    private static (
        PageRectMm LocationPanel,
        PageRectMm LocationMap,
        PageRectMm OverviewPanel,
        PageRectMm OverviewMap) ResolveSiteContextRegions(AlbumDefinition album)
    {
        if (!UsesGeneratedWorkingDrawingFormat(album))
        {
            return (
                BuildingArchitectureConceptPageLayout.SiteContextLocationPanel,
                BuildingArchitectureConceptPageLayout.SiteContextLocationMapArea,
                BuildingArchitectureConceptPageLayout.SiteContextOverviewPanel,
                BuildingArchitectureConceptPageLayout.SiteContextOverviewMapArea);
        }

        PageFormatDefinition format = WorkingDrawingAlbumFormatFactory.Resolve(album);
        WorkingDrawingPageRegions regions = WorkingDrawingPageLayout.Resolve(format);
        const double inset = 4d;
        const double gap = 6d;
        const double headerHeight = 12d;
        double left = regions.EtalonInnerFrame.X + inset;
        double top = regions.SheetTitleArea.Y + regions.SheetTitleArea.Height + 5d;
        double right = regions.EtalonInnerFrame.X + regions.EtalonInnerFrame.Width - inset;
        double bottom = regions.TitleBlockArea.Y - 5d;
        double panelWidth = Math.Max(1d, (right - left - gap) / 2d);
        double panelHeight = Math.Max(headerHeight + 1d, bottom - top);
        var locationPanel = new PageRectMm
        {
            X = left,
            Y = top,
            Width = panelWidth,
            Height = panelHeight,
        };
        var overviewPanel = new PageRectMm
        {
            X = left + panelWidth + gap,
            Y = top,
            Width = panelWidth,
            Height = panelHeight,
        };
        return (
            locationPanel,
            CreateMapArea(locationPanel, headerHeight),
            overviewPanel,
            CreateMapArea(overviewPanel, headerHeight));
    }

    private static PageRectMm CreateMapArea(PageRectMm panel, double headerHeight) => new()
    {
        X = panel.X,
        Y = panel.Y + headerHeight,
        Width = panel.Width,
        Height = Math.Max(1d, panel.Height - headerHeight),
    };

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
        string number,
        IEnumerable<AlbumPageRoleAssignment>? roleAssignments = null)
    {
        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);
        if (UsesGeneratedWorkingDrawingFormat(project.Album))
        {
            PageFormatDefinition format = WorkingDrawingAlbumFormatFactory.Resolve(project.Album);
            AlbumBuildPage generatedPage = CreateWorkingGeneratedPage(
                format,
                number,
                title,
                roleAssignments);
            DrawWorkingDrawingSheetChrome(gfx, page, project, generatedPage);
            return;
        }

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
        PdfPage page = AddGeneratedPage(document, project.Album);
        using XGraphics gfx = XGraphics.FromPdfPage(page);
        AlbumCompositionItem? component = project.Album.Composition.FirstOrDefault(item =>
            item.Id.Equals("visualizations", StringComparison.OrdinalIgnoreCase));
        DrawGeneratedPageChrome(
            gfx,
            page,
            project,
            plan.Title,
            plan.Number,
            component?.RoleAssignments);
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

        CompanyProfile company = ResolveDesignCompanyProfile(request.Project);
        var subtitleFont = new XFont(FontName, 15);
        var labelFont = new XFont(FontName, 11);
        var mutedBrush = new XSolidBrush(XColor.FromArgb(96, 108, 122));
        var width = page.Width.Point;
        var height = page.Height.Point;

        gfx.DrawRectangle(XBrushes.White, 0, 0, width, height);

        DrawDesignCompanyLogo(
            gfx,
            company,
            new XRect(width * 0.38, height * 0.055, width * 0.24, height * 0.08));

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

    private static void DrawWorkingDrawingTableOfContents(
        PdfDocument document,
        AlbumBuildRequest request,
        AlbumCompositionItem item)
    {
        PageFormatDefinition format = UsesGeneratedWorkingDrawingFormat(request.Project.Album)
            ? WorkingDrawingAlbumFormatFactory.Resolve(request.Project.Album)
            : PageFormatCatalog.DefaultWorkingDrawing;
        PdfPage page = UsesGeneratedWorkingDrawingFormat(request.Project.Album)
            ? AddGeneratedPage(document, request.Project.Album)
            : AddA3LandscapePage(document);
        using var gfx = XGraphics.FromPdfPage(page);
        gfx.DrawRectangle(XBrushes.White, 0, 0, page.Width.Point, page.Height.Point);

        AlbumBuildPage generatedPage = CreateWorkingGeneratedPage(
            format,
            item.Number,
            "ЗУРГИЙН ЖАГСААЛТ, ТАЙЛБАР БИЧИГ",
            item.RoleAssignments);
        DrawWorkingDrawingSheetChrome(gfx, page, request.Project, generatedPage);

        WorkingDrawingPageRegions regions = WorkingDrawingPageLayout.Resolve(format);
        double left = Mm(regions.EtalonInnerFrame.X + 4);
        double right = Mm(regions.EtalonInnerFrame.X + regions.EtalonInnerFrame.Width - 4);
        double y = Mm(regions.SheetTitleArea.Y + regions.SheetTitleArea.Height + 5);
        double rowHeight = Mm(7);
        double numberWidth = Mm(18);
        double sheetNumberWidth = Mm(30);
        double applicationWidth = Mm(34);
        XFont font = CreateCoverFont(2.5, false, WorkingDrawingFontName);
        XFont boldFont = CreateCoverFont(2.5, true, WorkingDrawingFontName);
        var linePen = new XPen(XColors.Black, Mm(0.10));

        void DrawRow(string index, string number, string title, string application, bool bold)
        {
            XFont rowFont = bold ? boldFont : font;
            double x1 = left + numberWidth;
            double x2 = x1 + sheetNumberWidth;
            double x3 = right - applicationWidth;
            gfx.DrawLine(linePen, left, y + rowHeight, right, y + rowHeight);
            gfx.DrawLine(linePen, left, y, left, y + rowHeight);
            gfx.DrawLine(linePen, x1, y, x1, y + rowHeight);
            gfx.DrawLine(linePen, x2, y, x2, y + rowHeight);
            gfx.DrawLine(linePen, x3, y, x3, y + rowHeight);
            gfx.DrawLine(linePen, right, y, right, y + rowHeight);
            gfx.DrawString(index, rowFont, XBrushes.Black, new XRect(left, y, numberWidth, rowHeight), XStringFormats.Center);
            gfx.DrawString(number, rowFont, XBrushes.Black, new XRect(x1, y, sheetNumberWidth, rowHeight), XStringFormats.Center);
            gfx.DrawString(title, rowFont, XBrushes.Black, new XRect(x2 + Mm(1), y, x3 - x2 - Mm(2), rowHeight), XStringFormats.CenterLeft);
            gfx.DrawString(application, rowFont, XBrushes.Black, new XRect(x3, y, applicationWidth, rowHeight), XStringFormats.Center);
            y += rowHeight;
        }

        gfx.DrawLine(linePen, left, y, right, y);
        DrawRow("Д/д", "Дугаар", "Хуудсны нэр", "Эх үүсвэр", true);
        int index = 1;
        foreach (AlbumBuildPage buildPage in request.Sections.SelectMany(section => section.Pages))
        {
            DrawRow(
                index.ToString(System.Globalization.CultureInfo.InvariantCulture),
                buildPage.Number,
                buildPage.Title,
                buildPage.Sheet.Source.Application.ToString(),
                false);
            index++;
        }
    }

    private static AlbumBuildPage CreateWorkingGeneratedPage(
        PageFormatDefinition format,
        string number,
        string title,
        IEnumerable<AlbumPageRoleAssignment>? roleAssignments = null)
    {
        var entry = new SheetPackageEntry
        {
            SheetId = $"studio-generated-{number}",
            Number = number,
            Name = title,
            Discipline = "ЕХ",
            ScaleText = "-",
            WidthMm = format.WidthMm,
            HeightMm = format.HeightMm,
            PageFormatId = format.Id,
            IsCleanDrawingSpace = true,
        };
        var source = new SheetPackageSource
        {
            Application = SheetSourceApplication.Manual,
            DocumentTitle = "Erk-S Studio",
        };
        var sheet = new SheetRecord
        {
            Key = entry.SheetId,
            SourceId = "studio-generated",
            SourceIdentity = "studio-generated",
            Entry = entry,
            Source = source,
            PackageId = Guid.Empty,
            ManifestPath = "",
            PdfPath = "",
            SourceSheetIndex = 0,
            ExportedAtUtc = DateTimeOffset.UtcNow,
            IsVerified = true,
        };
        return new AlbumBuildPage
        {
            Sheet = sheet,
            Definition = new AlbumPageDefinition
            {
                NumberOverride = number,
                TitleOverride = title,
                PageFormatId = format.Id,
                PlacementMode = PagePlacementMode.PreserveDrawingSpace,
                RoleAssignments = (roleAssignments ?? [])
                    .Select(assignment => assignment.Clone())
                    .ToList(),
            },
            Format = format,
            StudioNumber = number,
        };
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
