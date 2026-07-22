using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.IO.MemoryMappedFiles;

namespace ErkS.Platform.Core.Tests;

public sealed class PdfVectorPipelineTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-vector-tests-" + Guid.NewGuid().ToString("N"));

    public PdfVectorPipelineTests()
    {
        WindowsFontResolver.Register();
        Directory.CreateDirectory(workDirectory);
    }

    [Fact]
    public void SourceAsIs_PreservesOriginalPageDimensionsAndVectorGoldenProfile()
    {
        string sourcePath = Path.Combine(workDirectory, "a3-vector.pdf");
        WriteVectorPdf(sourcePath, [(420d, 297d, "Erk-S Монгол English")]);
        SheetRecord sheet = Intake(sourcePath, 420, 297, pageCount: 1, cleanDrawing: false);
        string outputPath = BuildSingleSheetAlbum(sheet, PageFormatCatalog.SourceAsIsId, PagePlacementMode.FullPage);

        PdfVectorPageProfile reference = Assert.Single(PdfVectorQualityInspector.Inspect(sourcePath).Pages);
        PdfVectorPageProfile actual = Assert.Single(PdfVectorQualityInspector.Inspect(outputPath).Pages);

        Assert.InRange(actual.WidthMm, 419.99, 420.01);
        Assert.InRange(actual.HeightMm, 296.99, 297.01);
        Assert.Equal(reference.MediaBoxWidthMm, actual.MediaBoxWidthMm, 3);
        Assert.Equal(reference.MediaBoxHeightMm, actual.MediaBoxHeightMm, 3);
        Assert.Equal(reference.CropBoxWidthMm, actual.CropBoxWidthMm, 3);
        Assert.Equal(reference.CropBoxHeightMm, actual.CropBoxHeightMm, 3);
        Assert.True(actual.HasTextOperators);
        Assert.True(actual.HasPathPaintingOperators);
        Assert.Equal(0, actual.ImageXObjectCount);
        Assert.Equal(reference.OperatorSignature, actual.OperatorSignature);
        Assert.Equal(reference.ContentSha256, actual.ContentSha256);
        Assert.Equal(
            PdfVectorQualityInspector.Inspect(sourcePath).ToGoldenText(),
            PdfVectorQualityInspector.Inspect(outputPath).ToGoldenText());
    }

    [Fact]
    public void PreserveDrawingSpace_UsesOneToOneScaleWithoutRasterFallback()
    {
        PageRectMm drawing = BuildingArchitectureConceptPageLayout.DrawingArea;
        double drawingWidthMm = drawing.Width;
        double drawingHeightMm = drawing.Height;
        string sourcePath = Path.Combine(workDirectory, "clean-vector.pdf");
        WriteVectorPdf(sourcePath, [(drawingWidthMm, drawingHeightMm, "Clean drawing")]);
        SheetRecord sheet = Intake(
            sourcePath,
            420,
            297,
            pageCount: 1,
            cleanDrawing: true,
            contentWidthMm: drawingWidthMm,
            contentHeightMm: drawingHeightMm);
        string outputPath = BuildSingleSheetAlbum(
            sheet,
            PageFormatCatalog.ConceptA3LandscapeId,
            PagePlacementMode.PreserveDrawingSpace);

        PdfVectorPageProfile page = Assert.Single(PdfVectorQualityInspector.Inspect(outputPath).Pages);
        PdfVectorXObjectProfile form = Assert.Single(page.XObjects, item =>
            item.Kind == PdfVectorXObjectKind.Form &&
            Math.Abs(item.WidthMm - drawingWidthMm) < 0.01 &&
            Math.Abs(item.HeightMm - drawingHeightMm) < 0.01);
        Assert.NotNull(form);
        Assert.Equal(0, page.ImageXObjectCount);

        IReadOnlyList<PdfVectorOperatorProfile> matrices = page.OperatorDetails
            .Where(operation => operation.Name == "cm")
            .ToList();
        Assert.True(matrices.Any(operation =>
            operation.Name == "cm" &&
            operation.NumericOperands.Count == 6 &&
            Math.Abs(operation.NumericOperands[0] - 1) < 0.0001 &&
            Math.Abs(operation.NumericOperands[1]) < 0.0001 &&
            Math.Abs(operation.NumericOperands[2]) < 0.0001 &&
            Math.Abs(operation.NumericOperands[3] - 1) < 0.0001),
            string.Join(" | ", matrices.Select(operation =>
                string.Join(',', operation.NumericOperands.Select(value => value.ToString("0.###"))))));
    }

    [Fact]
    public void StudioOverlay_DoesNotCoverDrawingArea()
    {
        PageRectMm drawing = BuildingArchitectureConceptPageLayout.DrawingArea;
        double drawingWidthMm = drawing.Width;
        double drawingHeightMm = drawing.Height;
        string sourcePath = Path.Combine(workDirectory, "overlay-clean-vector.pdf");
        WriteVectorPdf(sourcePath, [(drawingWidthMm, drawingHeightMm, "Overlay boundary")]);
        SheetRecord sheet = Intake(
            sourcePath,
            420,
            297,
            pageCount: 1,
            cleanDrawing: true,
            contentWidthMm: drawingWidthMm,
            contentHeightMm: drawingHeightMm);
        string outputPath = BuildSingleSheetAlbum(
            sheet,
            PageFormatCatalog.ConceptA3LandscapeId,
            PagePlacementMode.PreserveDrawingSpace);

        PdfVectorPageProfile page = Assert.Single(PdfVectorQualityInspector.Inspect(outputPath).Pages);
        PdfVectorXObjectProfile sourceForm = Assert.Single(page.XObjects, item =>
            item.Kind == PdfVectorXObjectKind.Form &&
            Math.Abs(item.WidthMm - drawingWidthMm) < 0.01 &&
            Math.Abs(item.HeightMm - drawingHeightMm) < 0.01);
        Assert.NotNull(sourceForm);

        PageRectMm header = BuildingArchitectureConceptPageLayout.SheetTitleArea;
        PageRectMm titleBlock = BuildingArchitectureConceptPageLayout.TitleBlockArea;
        Assert.False(IntersectsInterior(drawing, header));
        Assert.False(IntersectsInterior(drawing, titleBlock));

        IReadOnlyList<PdfVectorOperatorProfile> matrices = page.OperatorDetails
            .Where(operation => operation.Name == "cm" && operation.NumericOperands.Count == 6)
            .ToList();
        Assert.Contains(matrices, operation =>
            Math.Abs(operation.NumericOperands[0] - 1) < 0.0001 &&
            Math.Abs(operation.NumericOperands[1]) < 0.0001 &&
            Math.Abs(operation.NumericOperands[2]) < 0.0001 &&
            Math.Abs(operation.NumericOperands[3] - 1) < 0.0001 &&
            Math.Abs(operation.NumericOperands[4] - XUnit.FromMillimeter(drawing.X).Point) < 0.01);
    }

    [Fact]
    public void ConceptElevationOverlay_RemainsVectorAndUsesElevationDrawingSpace()
    {
        PageRectMm drawing = BuildingArchitectureConceptPageLayout.ElevationDrawingArea;
        double drawingWidthMm = drawing.Width;
        double drawingHeightMm = drawing.Height;
        string sourcePath = Path.Combine(workDirectory, "elevation-clean-vector.pdf");
        WriteVectorPdf(sourcePath, [(drawingWidthMm, drawingHeightMm, "North facade")]);
        SheetRecord sheet = Intake(
            sourcePath,
            420,
            297,
            pageCount: 1,
            cleanDrawing: true,
            contentWidthMm: drawingWidthMm,
            contentHeightMm: drawingHeightMm,
            contentKind: "Elevation",
            sheetDescription: "Facade material description");
        string outputPath = BuildSingleSheetAlbum(
            sheet,
            PageFormatCatalog.ConceptA3LandscapeId,
            PagePlacementMode.PreserveDrawingSpace,
            configure: project =>
            {
                var reviewed = new ProjectApprovalEntry
                {
                    OrganizationName = "Urban authority",
                    PositionTitle = "Specialist",
                    PersonName = "H.Tuya",
                    IncludeInElevationHeader = true,
                };
                project.ApprovalWorkflow.ConceptDesign = new ConceptDesignApprovalRoster
                {
                    IsConfigured = true,
                    ApprovedBy =
                    [
                        new ProjectApprovalEntry
                        {
                            OrganizationName = "City",
                            PositionTitle = "Chief architect",
                            PersonName = "A.Dash",
                        },
                    ],
                    EndorsedBy =
                    [
                        reviewed,
                        new ProjectApprovalEntry
                        {
                            OrganizationName = "Other authority",
                            PositionTitle = "Specialist",
                            PersonName = "Not selected",
                        },
                    ],
                };
            });

        PdfVectorPageProfile page = Assert.Single(PdfVectorQualityInspector.Inspect(outputPath).Pages);
        Assert.InRange(page.WidthMm, 419.99, 420.01);
        Assert.InRange(page.HeightMm, 296.99, 297.01);
        Assert.True(page.HasTextOperators);
        Assert.True(page.HasPathPaintingOperators);
        Assert.Equal(0, page.ImageXObjectCount);
        Assert.Contains(page.XObjects, item =>
            item.Kind == PdfVectorXObjectKind.Form &&
            Math.Abs(item.WidthMm - drawingWidthMm) < 0.01 &&
            Math.Abs(item.HeightMm - drawingHeightMm) < 0.01);
    }

    [Fact]
    public void ConceptPortraitElevationOverlay_UsesPortraitPageWithoutRasterFallback()
    {
        PageFormatDefinition format = PageFormatCatalog.Resolve(
            PageFormatCatalog.ConceptElevationA3PortraitTopId);
        PageRectMm drawing = format.DrawingArea;
        string sourcePath = Path.Combine(workDirectory, "portrait-elevation-clean-vector.pdf");
        WriteVectorPdf(sourcePath, [(drawing.Width, drawing.Height, "Portrait facade")]);
        SheetRecord sheet = Intake(
            sourcePath,
            297,
            420,
            pageCount: 1,
            cleanDrawing: true,
            contentWidthMm: drawing.Width,
            contentHeightMm: drawing.Height,
            contentKind: "Elevation",
            sheetDescription: "Portrait facade",
            portrait: true);

        string outputPath = BuildSingleSheetAlbum(
            sheet,
            PageFormatCatalog.ConceptElevationA3PortraitTopId,
            PagePlacementMode.PreserveDrawingSpace);

        PdfVectorPageProfile page = Assert.Single(PdfVectorQualityInspector.Inspect(outputPath).Pages);
        Assert.InRange(page.WidthMm, 296.99, 297.01);
        Assert.InRange(page.HeightMm, 419.99, 420.01);
        Assert.True(page.HasTextOperators);
        Assert.True(page.HasPathPaintingOperators);
        Assert.Equal(0, page.ImageXObjectCount);
        Assert.Contains(page.XObjects, item =>
            item.Kind == PdfVectorXObjectKind.Form &&
            Math.Abs(item.WidthMm - drawing.Width) < 0.01 &&
            Math.Abs(item.HeightMm - drawing.Height) < 0.01);
    }

    [Fact]
    public async Task LockedPreviewFile_DoesNotBlockCanonicalAlbumBuild()
    {
        string sourcePath = Path.Combine(workDirectory, "locked-preview-source.pdf");
        WriteVectorPdf(sourcePath, [(420d, 297d, "Canonical build")]);
        SheetRecord sheet = Intake(sourcePath, 420, 297, pageCount: 1, cleanDrawing: false);
        string canonicalPath = Path.Combine(workDirectory, "canonical-album.pdf");
        BuildSingleSheetAlbum(sheet, PageFormatCatalog.SourceAsIsId, PagePlacementMode.FullPage, canonicalPath);
        var cache = new CanonicalPdfPreviewCache(Path.Combine(workDirectory, "preview-cache"));
        string previewPath = await cache.GetPreviewPathAsync(canonicalPath);

        using MemoryMappedFile previewLock = MemoryMappedFile.CreateFromFile(
            previewPath,
            FileMode.Open,
            null,
            0,
            MemoryMappedFileAccess.Read);

        string rebuilt = BuildSingleSheetAlbum(
            sheet,
            PageFormatCatalog.SourceAsIsId,
            PagePlacementMode.FullPage,
            canonicalPath);

        Assert.Equal(canonicalPath, rebuilt);
        Assert.True(File.Exists(canonicalPath));
        Assert.NotEqual(Path.GetFullPath(canonicalPath), Path.GetFullPath(previewPath));
    }

    [Fact]
    public void MissingFont_ProducesControlledFailure()
    {
        string emptyFonts = Path.Combine(workDirectory, "empty-fonts");
        Directory.CreateDirectory(emptyFonts);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            WindowsFontResolver.ValidateRequiredFonts(emptyFonts));

        Assert.Contains("Arial", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("arial.ttf", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ProjectClientTypes.Citizen, 0)]
    [InlineData(ProjectClientTypes.Organization, 1)]
    [InlineData(ProjectClientTypes.GovernmentAuthority, 1)]
    public void ConceptCover_ClientLogoFollowsClientType(string clientType, int expectedImageCount)
    {
        string logoPath = Path.Combine(workDirectory, $"client-{clientType}.png");
        File.WriteAllBytes(
            logoPath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        var project = new AlbumProject
        {
            Name = "Client logo cover",
            ProjectFolder = workDirectory,
            InitiationBasis = new ProjectInitiationBasis
            {
                ClientType = clientType,
                ClientName = "Захиалагч",
                ClientOrganizationSnapshot = new CompanyProfile
                {
                    Name = "Захиалагч",
                    LogoPath = logoPath,
                },
            },
            Company = new CompanyProfile { Name = "Design company" },
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept"),
        };
        string outputPath = Path.Combine(workDirectory, $"client-{clientType}-cover.pdf");

        new AlbumBuilder(new PdfSharpAlbumWriter()).Build(project, new SheetLibrary(), outputPath);

        PdfVectorPageProfile cover = PdfVectorQualityInspector.Inspect(outputPath).Pages[0];
        Assert.Equal(expectedImageCount, cover.ImageXObjectCount);
    }

    [Fact]
    public void SourceAsIs_MultiPageOrderAndMixedPageSizesArePreserved()
    {
        string sourcePath = Path.Combine(workDirectory, "mixed-vector.pdf");
        WriteVectorPdf(
            sourcePath,
            [
                (420d, 297d, "FIRST A3"),
                (210d, 297d, "SECOND A4"),
                (500d, 200d, "THIRD CUSTOM"),
            ]);
        SheetRecord sheet = Intake(sourcePath, 420, 297, pageCount: 3, cleanDrawing: false);
        string outputPath = BuildSingleSheetAlbum(sheet, PageFormatCatalog.SourceAsIsId, PagePlacementMode.FullPage);

        PdfVectorDocumentProfile reference = PdfVectorQualityInspector.Inspect(sourcePath);
        PdfVectorDocumentProfile actual = PdfVectorQualityInspector.Inspect(outputPath);

        Assert.Equal(3, actual.Pages.Count);
        Assert.Equal(reference.Pages.Select(page => Math.Round(page.WidthMm, 3)),
            actual.Pages.Select(page => Math.Round(page.WidthMm, 3)));
        Assert.Equal(reference.Pages.Select(page => Math.Round(page.HeightMm, 3)),
            actual.Pages.Select(page => Math.Round(page.HeightMm, 3)));
        Assert.Equal(reference.Pages.Select(page => page.ContentSha256),
            actual.Pages.Select(page => page.ContentSha256));
        Assert.Equal(reference.Pages.Select(page => page.OperatorSignature),
            actual.Pages.Select(page => page.OperatorSignature));
        Assert.All(actual.Pages, page => Assert.Equal(0, page.ImageXObjectCount));
    }

    [Fact]
    public void GoldenInspector_DetectsFullPageRasterFallback()
    {
        string vectorPath = Path.Combine(workDirectory, "vector-reference.pdf");
        string rasterPath = Path.Combine(workDirectory, "raster-fallback.pdf");
        WriteVectorPdf(vectorPath, [(420d, 297d, "Vector reference")]);
        WriteRasterFallbackPdf(rasterPath, 420, 297);

        PdfVectorPageProfile vector = Assert.Single(PdfVectorQualityInspector.Inspect(vectorPath).Pages);
        PdfVectorPageProfile raster = Assert.Single(PdfVectorQualityInspector.Inspect(rasterPath).Pages);

        Assert.Equal(0, vector.ImageXObjectCount);
        Assert.True(raster.ImageXObjectCount > 0);
        Assert.NotEqual(vector.OperatorSignature, raster.OperatorSignature);
    }

    [Fact]
    public void GoldenInspector_HandlesBlankPageWithoutResourcesOrDrawingOperators()
    {
        string path = Path.Combine(workDirectory, "blank.pdf");
        using (var document = new PdfDocument())
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(210);
            page.Height = XUnit.FromMillimeter(297);
            document.Save(path);
        }

        PdfVectorPageProfile profile = Assert.Single(PdfVectorQualityInspector.Inspect(path).Pages);

        Assert.False(profile.HasTextOperators);
        Assert.False(profile.HasPathPaintingOperators);
        Assert.Equal(0, profile.ImageXObjectCount);
        Assert.Equal(0, profile.FormXObjectCount);
        Assert.Empty(profile.XObjects);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the assertion that failed.
        }
    }

    private SheetRecord Intake(
        string pdfPath,
        double widthMm,
        double heightMm,
        int pageCount,
        bool cleanDrawing,
        double contentWidthMm = 0,
        double contentHeightMm = 0,
        string contentKind = "",
        string sheetDescription = "",
        bool portrait = false)
    {
        PageFormatSpec? format = null;
        if (cleanDrawing)
        {
            format = CreateConceptFormat(
                contentKind.Equals("Elevation", StringComparison.OrdinalIgnoreCase),
                portrait);
        }

        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                SourceId = "vector-golden-source",
                Application = SheetSourceApplication.Revit,
                DocumentTitle = "Vector golden.rvt",
            },
            Sheets =
            [
                new SheetPackageEntry
                {
                    SheetId = "vector-sheet",
                    Number = "A-01",
                    Name = "Vector golden sheet",
                    WidthMm = widthMm,
                    HeightMm = heightMm,
                    PageFormatId = format?.Id ?? "",
                    Format = format,
                    IsCleanDrawingSpace = cleanDrawing,
                    ContentWidthMm = cleanDrawing ? contentWidthMm : widthMm,
                    ContentHeightMm = cleanDrawing ? contentHeightMm : heightMm,
                    ContentKind = contentKind,
                    SheetDescription = sheetDescription,
                    PdfFileName = Path.GetFileName(pdfPath),
                    PageCount = pageCount,
                },
            ],
        };
        string manifestPath = SheetPackageWriter.Write(manifest, workDirectory, "vector-golden");
        var library = new SheetLibrary();
        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);
        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        library.Absorb(result);
        return Assert.Single(library.Snapshot());
    }

    private string BuildSingleSheetAlbum(
        SheetRecord sheet,
        string formatId,
        PagePlacementMode placementMode,
        string? outputPath = null,
        Action<AlbumProject>? configure = null)
    {
        var project = new AlbumProject { Name = "Vector golden" };
        project.Album.IncludeCover = false;
        project.Album.IncludeTableOfContents = false;
        var definition = new AlbumPageDefinition
        {
            SheetKey = sheet.Key,
            PageFormatId = formatId,
            PlacementMode = placementMode,
        };
        if (sheet.Entry.Format is not null)
        {
            PageFormatResolver.ApplySourceFormat(definition, sheet.Entry);
        }
        definition.PageFormatId = formatId;
        definition.PlacementMode = placementMode;
        project.Album.Pages.Add(definition);
        configure?.Invoke(project);
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(sheet.ManifestPath));
        outputPath ??= Path.Combine(workDirectory, Guid.NewGuid().ToString("N") + ".pdf");

        new AlbumBuilder(new PdfSharpAlbumWriter()).Build(project, library, outputPath);
        return outputPath;
    }

    private static bool IntersectsInterior(PageRectMm left, PageRectMm right) =>
        left.X < right.X + right.Width &&
        left.X + left.Width > right.X &&
        left.Y < right.Y + right.Height &&
        left.Y + left.Height > right.Y;

    private static PageFormatSpec CreateConceptFormat(bool elevation = false, bool portrait = false)
    {
        string formatId = (portrait, elevation) switch
        {
            (true, true) => PageFormatCatalog.ConceptElevationA3PortraitTopId,
            (true, false) => PageFormatCatalog.ConceptA3PortraitTopId,
            (false, true) => PageFormatCatalog.ConceptElevationA3LandscapeId,
            _ => PageFormatCatalog.ConceptA3LandscapeId,
        };
        PageFormatDefinition resolved = PageFormatCatalog.Resolve(formatId);
        PageRectMm drawing = resolved.DrawingArea;
        PageRectMm title = resolved.SheetTitleArea;
        PageRectMm corner = resolved.TitleBlockArea;
        var format = new PageFormatSpec
        {
            Id = formatId,
            Name = resolved.Name,
            Mode = "Concept",
            Code = "A3",
            Orientation = resolved.Orientation,
            BindEdge = resolved.BindEdge,
            WidthMm = resolved.WidthMm,
            HeightMm = resolved.HeightMm,
            DrawingArea = ToSpec(drawing),
            SheetTitleArea = ToSpec(title),
            TitleBlockArea = ToSpec(corner),
            Revision = elevation ? 4 : 3,
        };
        format.GeometryHash = PageFormatSpecGeometry.ComputeHash(format);
        return format;

        static PageRectSpec ToSpec(PageRectMm rect) => new()
        {
            X = rect.X,
            Y = rect.Y,
            Width = rect.Width,
            Height = rect.Height,
        };
    }

    private static void WriteVectorPdf(
        string path,
        IReadOnlyList<(double WidthMm, double HeightMm, string Label)> pages)
    {
        using var document = new PdfDocument();
        foreach ((double widthMm, double heightMm, string label) in pages)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(widthMm);
            page.Height = XUnit.FromMillimeter(heightMm);
            double crop = XUnit.FromMillimeter(2).Point;
            page.CropBox = new PdfRectangle(
                new XPoint(crop, crop),
                new XPoint(page.Width.Point - crop, page.Height.Point - crop));
            using XGraphics graphics = XGraphics.FromPdfPage(page);
            graphics.DrawLine(new XPen(XColors.Black, 0.25), 30, 40, page.Width.Point - 30, 40);
            graphics.DrawLine(new XPen(XColors.DarkBlue, 1.0), 30, 55, page.Width.Point - 30, 55);
            graphics.DrawLine(new XPen(XColors.DarkRed, 2.5), 30, 75, page.Width.Point - 30, 75);
            graphics.DrawRectangle(
                new XPen(XColors.Black, 0.5),
                new XSolidBrush(XColor.FromArgb(90, 30, 130, 90)),
                40,
                95,
                Math.Min(180, page.Width.Point / 3),
                Math.Min(120, page.Height.Point / 3));
            graphics.DrawString(
                label,
                new XFont("Arial", 16),
                XBrushes.Black,
                new XRect(35, 10, page.Width.Point - 70, 25),
                XStringFormats.Center);
        }
        document.Save(path);
    }

    private void WriteRasterFallbackPdf(string path, double widthMm, double heightMm)
    {
        string imagePath = Path.Combine(workDirectory, "fallback-pixel.png");
        File.WriteAllBytes(
            imagePath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(widthMm);
        page.Height = XUnit.FromMillimeter(heightMm);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        using XImage image = XImage.FromFile(imagePath);
        graphics.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
        document.Save(path);
    }
}
