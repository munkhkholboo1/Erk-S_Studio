using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
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
    public void WindowsFontResolver_LoadsWorkingDrawingFontBesideRendererAssembly()
    {
        var resolver = new WindowsFontResolver();

        byte[]? font = resolver.GetFont("isocpeur mon#");

        Assert.NotNull(font);
        Assert.True(font.Length > 1000);
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

    [Theory]
    [InlineData(SheetPrintColorMode.Original)]
    [InlineData(SheetPrintColorMode.BlackAndWhite)]
    [InlineData(SheetPrintColorMode.Grayscale)]
    public void PrintColorModeMetadata_DoesNotRecolorBakedVectorPdf(
        SheetPrintColorMode printColorMode)
    {
        string sourcePath = Path.Combine(
            workDirectory,
            $"baked-{printColorMode}.pdf");
        WriteVectorPdf(sourcePath, [(420d, 297d, printColorMode.ToString())]);
        SheetRecord sheet = Intake(
            sourcePath,
            420,
            297,
            pageCount: 1,
            cleanDrawing: false,
            printColorMode: printColorMode);

        string outputPath = BuildSingleSheetAlbum(
            sheet,
            PageFormatCatalog.SourceAsIsId,
            PagePlacementMode.FullPage);

        PdfVectorPageProfile source =
            Assert.Single(PdfVectorQualityInspector.Inspect(sourcePath).Pages);
        PdfVectorPageProfile output =
            Assert.Single(PdfVectorQualityInspector.Inspect(outputPath).Pages);
        Assert.Equal(printColorMode, sheet.Entry.PrintColorMode);
        Assert.Equal(source.OperatorSignature, output.OperatorSignature);
        Assert.Equal(source.ContentSha256, output.ContentSha256);
        Assert.Equal(0, output.ImageXObjectCount);
    }

    [Fact]
    public void SourceCrop_ProducesCroppedVectorPageWithoutRasterFallback()
    {
        string sourcePath = Path.Combine(workDirectory, "legacy-title-block.pdf");
        WriteVectorPdf(
            sourcePath,
            [(420d, 297d, "Legacy project title block")],
            applyCropBox: false);
        SheetRecord sheet = Intake(sourcePath, 420, 297, pageCount: 1, cleanDrawing: false);
        string outputPath = BuildSingleSheetAlbum(
            sheet,
            PageFormatCatalog.SourceAsIsId,
            PagePlacementMode.FullPage,
            configure: project =>
            {
                project.Album.Pages.Single().SourceCrop = new SourcePageCropDefinition
                {
                    Enabled = true,
                    LeftMm = 15,
                    TopMm = 5,
                    RightMm = 35,
                    BottomMm = 20,
                };
            });

        PdfVectorPageProfile page = Assert.Single(PdfVectorQualityInspector.Inspect(outputPath).Pages);

        Assert.InRange(page.WidthMm, 369.99, 370.01);
        Assert.InRange(page.HeightMm, 271.99, 272.01);
        Assert.True(page.HasPathPaintingOperators);
        Assert.Equal(0, page.ImageXObjectCount);
        Assert.Contains(page.XObjects, item => item.Kind == PdfVectorXObjectKind.Form);
        Assert.True(
            page.Operators.Count(operation => operation is "W" or "W*") >= 2,
            "A cropped PDF form must have both target-area and crop-region clips.");
    }

    [Theory]
    [InlineData(SheetPrintColorMode.Original)]
    [InlineData(SheetPrintColorMode.BlackAndWhite)]
    [InlineData(SheetPrintColorMode.Grayscale)]
    public void PrintColorModeMetadata_CroppedFormPreservesBakedColorOperators(
        SheetPrintColorMode printColorMode)
    {
        string sourcePath = Path.Combine(
            workDirectory,
            $"cropped-color-{printColorMode}.pdf");
        WriteVectorPdf(sourcePath, [(420d, 297d, printColorMode.ToString())]);
        SheetRecord sheet = Intake(
            sourcePath,
            420,
            297,
            pageCount: 1,
            cleanDrawing: false,
            printColorMode: printColorMode);
        string outputPath = BuildSingleSheetAlbum(
            sheet,
            PageFormatCatalog.SourceAsIsId,
            PagePlacementMode.FullPage,
            configure: project =>
            {
                project.Album.Pages.Single().SourceCrop = new SourcePageCropDefinition
                {
                    Enabled = true,
                    LeftMm = 10,
                    TopMm = 10,
                    RightMm = 10,
                    BottomMm = 10,
                };
            });

        using PdfDocument source = PdfReader.Open(sourcePath, PdfDocumentOpenMode.Import);
        using PdfDocument output = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        CSequence sourceContent = ContentReader.ReadContent(source.Pages[0]);
        PdfDictionary form = FindSingleFormXObject(output.Pages[0]);
        CSequence formContent = ContentReader.ReadContent(form.Stream!.UnfilteredValue);
        IReadOnlyList<string> sourceColors = ColorOperatorSignature(sourceContent);
        IReadOnlyList<string> formColors = ColorOperatorSignature(formContent);

        Assert.Equal(printColorMode, sheet.Entry.PrintColorMode);
        Assert.NotEmpty(sourceColors);
        Assert.Equal(sourceColors, formColors);
        Assert.Equal(
            0,
            Assert.Single(PdfVectorQualityInspector.Inspect(outputPath).Pages).ImageXObjectCount);
    }

    [Fact]
    public void SourceCrop_CenteredMediaBoxEmitsThePreviewPlacementMatrix()
    {
        string sourcePath = Path.Combine(workDirectory, "centered-media-box.pdf");
        WriteVectorPdf(
            sourcePath,
            [(420d, 297d, "Centered MediaBox")],
            applyCropBox: false);
        using (PdfDocument source = PdfSharp.Pdf.IO.PdfReader.Open(
                   sourcePath,
                   PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify))
        {
            PdfPage page = source.Pages[0];
            double width = page.MediaBox.Width;
            double height = page.MediaBox.Height;
            page.MediaBox = new PdfRectangle(
                new XPoint(-width / 2, -height / 2),
                new XPoint(width / 2, height / 2));
            source.Save(sourcePath);
        }

        SheetRecord sheet = Intake(sourcePath, 420, 297, pageCount: 1, cleanDrawing: false);
        PageFormatDefinition format = PdfSourcePageFormatFactory.Create(
            "A3",
            "LANDSCAPE",
            "LEFT");
        var crop = new SourcePageCropDefinition
        {
            Enabled = true,
            LeftMm = 94.74,
            TopMm = 25.68,
            RightMm = 90.64,
            BottomMm = 67.94,
            OffsetXmm = -55.78,
            OffsetYmm = 0,
            ScalePercent = 100,
        };
        string outputPath = BuildSingleSheetAlbum(
            sheet,
            format.Id,
            PagePlacementMode.FitDrawingArea,
            configure: project =>
            {
                AlbumPageDefinition page = project.Album.Pages.Single();
                page.PageFormatSnapshot = format;
                page.FollowSourceFormat = false;
                page.SourceCrop = crop.DeepClone();
            });
        PdfSourcePagePlacementMm placement = PdfSourcePagePlacementGeometry.Calculate(
            420,
            297,
            format.DrawingArea,
            PagePlacementMode.FitDrawingArea,
            crop,
            format.Id);
        double scale =
            placement.CompleteSourceDestination.Width / 420;
        PdfVectorPageProfile output =
            Assert.Single(PdfVectorQualityInspector.Inspect(outputPath).Pages);
        PdfVectorOperatorProfile formMatrix = Assert.Single(
            output.OperatorDetails,
            operation =>
                operation.Name == "cm" &&
                operation.NumericOperands.Count == 6 &&
                Math.Abs(operation.NumericOperands[0] - scale) < 0.001 &&
                Math.Abs(operation.NumericOperands[3] - scale) < 0.001);
        double mediaBoxX1 = XUnit.FromMillimeter(-210).Point;
        double mediaBoxY1 = XUnit.FromMillimeter(-148.5).Point;
        double expectedX =
            XUnit.FromMillimeter(placement.CompleteSourceDestination.X).Point -
            mediaBoxX1 * scale;
        double expectedY =
            XUnit.FromMillimeter(
                format.HeightMm -
                placement.CompleteSourceDestination.Y -
                placement.CompleteSourceDestination.Height).Point -
            mediaBoxY1 * scale;

        Assert.Equal(expectedX, formMatrix.NumericOperands[4], 3);
        Assert.Equal(expectedY, formMatrix.NumericOperands[5], 3);
    }

    [Fact]
    public void PdfSourceFormat_CreatesStandardAndCustomStudioPageGeometry()
    {
        PageFormatDefinition a2Landscape = PdfSourcePageFormatFactory.Create(
            "A2",
            "LANDSCAPE",
            "LEFT");
        PageFormatDefinition customPortrait = PdfSourcePageFormatFactory.Create(
            PdfSourcePageFormatFactory.CustomCode,
            "PORTRAIT",
            "RIGHT",
            customWidthMm: 360,
            customHeightMm: 510);

        Assert.Equal(PageFormatKind.Concept, a2Landscape.Kind);
        Assert.Equal(594, a2Landscape.WidthMm);
        Assert.Equal(420, a2Landscape.HeightMm);
        Assert.Equal("LEFT", a2Landscape.BindEdge);
        Assert.True(a2Landscape.DrawingArea.Width > 0);
        Assert.True(a2Landscape.TitleBlockArea.Width > 0);

        Assert.Equal(PageFormatKind.Concept, customPortrait.Kind);
        Assert.Equal(360, customPortrait.WidthMm);
        Assert.Equal(510, customPortrait.HeightMm);
        Assert.Equal("RIGHT", customPortrait.BindEdge);
        Assert.True(customPortrait.DrawingArea.Width > 0);
        Assert.True(customPortrait.DrawingArea.Height > 0);
    }

    [Fact]
    public void PdfSourceMasksAndTransform_StayVectorOnConfiguredStudioPage()
    {
        string sourcePath = Path.Combine(workDirectory, "legacy-composed-sheet.pdf");
        WriteVectorPdf(
            sourcePath,
            [(430d, 305d, "Legacy frame and title block")],
            applyCropBox: false);
        SheetRecord sheet = Intake(sourcePath, 430, 305, pageCount: 1, cleanDrawing: false);
        PageFormatDefinition format = PdfSourcePageFormatFactory.Create(
            "A3",
            "LANDSCAPE",
            "LEFT");
        string outputPath = BuildSingleSheetAlbum(
            sheet,
            format.Id,
            PagePlacementMode.FitDrawingArea,
            configure: project =>
            {
                AlbumPageDefinition page = project.Album.Pages.Single();
                page.PageFormatSnapshot = format;
                page.FollowSourceFormat = false;
                page.SourceCrop = new SourcePageCropDefinition
                {
                    Enabled = true,
                    LeftMm = 8,
                    TopMm = 6,
                    RightMm = 12,
                    BottomMm = 18,
                    OffsetXmm = 2,
                    OffsetYmm = -1,
                    ScalePercent = 96,
                    RotationDegrees = 1.5,
                    Masks =
                    [
                        new SourcePageMaskDefinition
                        {
                            Shape = SourcePageMaskShape.Rectangle,
                            Points =
                            [
                                new SourcePagePointDefinition { X = 0.72, Y = 0.82 },
                                new SourcePagePointDefinition { X = 0.98, Y = 0.98 },
                            ],
                        },
                        new SourcePageMaskDefinition
                        {
                            Shape = SourcePageMaskShape.Polygon,
                            Points =
                            [
                                new SourcePagePointDefinition { X = 0.02, Y = 0.02 },
                                new SourcePagePointDefinition { X = 0.30, Y = 0.02 },
                                new SourcePagePointDefinition { X = 0.24, Y = 0.11 },
                                new SourcePagePointDefinition { X = 0.02, Y = 0.09 },
                            ],
                        },
                    ],
                };
            });

        PdfVectorPageProfile pageProfile =
            Assert.Single(PdfVectorQualityInspector.Inspect(outputPath).Pages);

        Assert.InRange(pageProfile.WidthMm, 419.99, 420.01);
        Assert.InRange(pageProfile.HeightMm, 296.99, 297.01);
        Assert.Equal(0, pageProfile.ImageXObjectCount);
        Assert.Contains(pageProfile.XObjects, item => item.Kind == PdfVectorXObjectKind.Form);
        Assert.True(pageProfile.HasPathPaintingOperators);
    }

    [Fact]
    public void PdfSourcePlacement_AppliesCropTransformAndIndependentMasks()
    {
        const double pointsPerMillimeter = 72d / 25.4d;
        var crop = new SourcePageCropDefinition
        {
            Enabled = true,
            LeftMm = 10,
            TopMm = 5,
            RightMm = 20,
            BottomMm = 15,
            OffsetXmm = 2,
            OffsetYmm = -3,
            ScalePercent = 50,
            RotationDegrees = 12,
            Masks =
            [
                new SourcePageMaskDefinition
                {
                    Shape = SourcePageMaskShape.Rectangle,
                    Points =
                    [
                        new SourcePagePointDefinition { X = 0.25, Y = 0.25 },
                        new SourcePagePointDefinition { X = 0.5, Y = 0.5 },
                    ],
                },
                new SourcePageMaskDefinition
                {
                    Shape = SourcePageMaskShape.Polygon,
                    Points =
                    [
                        new SourcePagePointDefinition { X = 0.6, Y = 0.1 },
                        new SourcePagePointDefinition { X = 0.8, Y = 0.2 },
                        new SourcePagePointDefinition { X = 0.7, Y = 0.4 },
                    ],
                },
            ],
        };
        var target = new XRect(0, 0, 420, 297);

        PdfSharpAlbumWriter.PdfSourcePlacement placement =
            PdfSharpAlbumWriter.CalculateSourcePlacement(
                430,
                305,
                target,
                PagePlacementMode.FitDrawingArea,
                crop,
                "test");

        Assert.Equal(10 * pointsPerMillimeter, placement.SourceRectangle.X, 6);
        Assert.Equal(5 * pointsPerMillimeter, placement.SourceRectangle.Y, 6);
        Assert.Equal(
            430 - 30 * pointsPerMillimeter,
            placement.SourceRectangle.Width,
            6);
        Assert.Equal(
            305 - 20 * pointsPerMillimeter,
            placement.SourceRectangle.Height,
            6);
        Assert.Equal(12, placement.RotationDegrees);
        Assert.Equal(2, placement.MaskPolygons.Count);
        Assert.Equal(4, placement.MaskPolygons[0].Length);
        Assert.Equal(3, placement.MaskPolygons[1].Length);

        double fitScale = Math.Min(
            target.Width / placement.SourceRectangle.Width,
            target.Height / placement.SourceRectangle.Height);
        Assert.Equal(
            placement.SourceRectangle.Width * fitScale * 0.5,
            placement.DestinationRectangle.Width,
            6);
        Assert.Equal(
            placement.SourceRectangle.Height * fitScale * 0.5,
            placement.DestinationRectangle.Height,
            6);
        Assert.Equal(
            (target.Width - placement.DestinationRectangle.Width) / 2 +
            2 * pointsPerMillimeter,
            placement.DestinationRectangle.X,
            6);
        Assert.Equal(
            (target.Height - placement.DestinationRectangle.Height) / 2 -
            3 * pointsPerMillimeter,
            placement.DestinationRectangle.Y,
            6);
    }

    [Fact]
    public void PreservePhysicalSize_KeepsCroppedPdfAtOneToOneAndIgnoresLegacyScalePercent()
    {
        const double pointsPerMillimeter = 72d / 25.4d;
        var crop = new SourcePageCropDefinition
        {
            Enabled = true,
            LeftMm = 10,
            TopMm = 5,
            RightMm = 20,
            BottomMm = 15,
            OffsetXmm = 2,
            OffsetYmm = -3,
            ScalePercent = 50,
        };
        var target = new XRect(
            0,
            0,
            420 * pointsPerMillimeter,
            297 * pointsPerMillimeter);

        PdfSharpAlbumWriter.PdfSourcePlacement placement =
            PdfSharpAlbumWriter.CalculateSourcePlacement(
                300 * pointsPerMillimeter,
                200 * pointsPerMillimeter,
                target,
                PagePlacementMode.PreservePhysicalSize,
                crop,
                "pdf-a3");

        Assert.Equal(270 * pointsPerMillimeter, placement.SourceRectangle.Width, 6);
        Assert.Equal(180 * pointsPerMillimeter, placement.SourceRectangle.Height, 6);
        Assert.Equal(placement.SourceRectangle.Width, placement.DestinationRectangle.Width, 6);
        Assert.Equal(placement.SourceRectangle.Height, placement.DestinationRectangle.Height, 6);
        Assert.Equal(
            (target.Width - placement.SourceRectangle.Width) / 2 +
            2 * pointsPerMillimeter,
            placement.DestinationRectangle.X,
            6);
        Assert.Equal(
            (target.Height - placement.SourceRectangle.Height) / 2 -
            3 * pointsPerMillimeter,
            placement.DestinationRectangle.Y,
            6);
    }

    [Fact]
    public void CroppedPdfForm_MapsOnlyTheSelectedSourceRegionIntoItsDestination()
    {
        const double pointsPerMillimeter = 72d / 25.4d;
        var crop = new SourcePageCropDefinition
        {
            Enabled = true,
            LeftMm = 15,
            TopMm = 5,
            RightMm = 35,
            BottomMm = 20,
        };
        var destination = new XRect(30, 40, 740, 544);
        PdfSharpAlbumWriter.PdfSourcePlacement placement =
            PdfSharpAlbumWriter.CalculateSourcePlacement(
                420 * pointsPerMillimeter,
                297 * pointsPerMillimeter,
                destination,
                PagePlacementMode.FullPage,
                crop);

        XRect completeSourceDestination =
            PdfSharpAlbumWriter.CalculateCompleteSourceDestination(
                420 * pointsPerMillimeter,
                297 * pointsPerMillimeter,
                placement);
        double scaleX = placement.DestinationRectangle.Width /
                        placement.SourceRectangle.Width;
        double scaleY = placement.DestinationRectangle.Height /
                        placement.SourceRectangle.Height;

        Assert.Equal(
            placement.DestinationRectangle.X,
            completeSourceDestination.X + placement.SourceRectangle.X * scaleX,
            6);
        Assert.Equal(
            placement.DestinationRectangle.Y,
            completeSourceDestination.Y + placement.SourceRectangle.Y * scaleY,
            6);
        Assert.True(completeSourceDestination.X < placement.DestinationRectangle.X);
        Assert.True(completeSourceDestination.Y < placement.DestinationRectangle.Y);
        Assert.True(
            completeSourceDestination.Width > placement.DestinationRectangle.Width);
        Assert.True(
            completeSourceDestination.Height > placement.DestinationRectangle.Height);
    }

    [Fact]
    public void CroppedPdfForm_CompensatesCenteredMediaBoxWhenScaled()
    {
        var desired = new XRect(-302.7, -42.0, 1476.2, 1043.8);
        const double formWidth = 1190.52;
        const double formHeight = 841.8;
        const double mediaBoxX1 = -595.26;
        const double mediaBoxY1 = -420.9;

        XRect drawRectangle =
            PdfSharpAlbumWriter.CalculatePdfSharpFormDrawRectangle(
                desired,
                formWidth,
                formHeight,
                mediaBoxX1,
                mediaBoxY1);
        double scaleX = drawRectangle.Width / formWidth;
        double scaleY = drawRectangle.Height / formHeight;

        // PDFsharp applies the MediaBox origin before emitting the form matrix.
        // XGraphics uses a top-left Y axis while PDF forms use bottom-left Y,
        // so the Y compensation has the opposite sign from X.
        double effectiveX =
            drawRectangle.X - mediaBoxX1 + mediaBoxX1 * scaleX;
        double effectiveY =
            drawRectangle.Y + mediaBoxY1 - mediaBoxY1 * scaleY;
        Assert.Equal(desired.X, effectiveX, 6);
        Assert.Equal(desired.Y, effectiveY, 6);
        Assert.NotEqual(desired.X, drawRectangle.X);
        Assert.NotEqual(desired.Y, drawRectangle.Y);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("100", "1:100")]
    [InlineData(" 1 : 500 ", "1:500")]
    [InlineData("1 / 1000", "1:1000")]
    [InlineData("NTS", "NTS")]
    public void DrawingScaleText_NormalizesTitleBlockMetadataWithoutGeometryMeaning(
        string? input,
        string expected)
    {
        Assert.Equal(expected, DrawingScaleText.Normalize(input));
    }

    [Fact]
    public void DrawingScaleText_OverrideCanInheritBlankOrReplaceSourceMetadata()
    {
        var entry = new SheetPackageEntry { ScaleText = "1:100" };
        var page = new AlbumPageDefinition();

        Assert.Equal("1:100", DrawingScaleText.Resolve(page, entry));

        page.ScaleTextOverride = "";
        Assert.Equal("", DrawingScaleText.Resolve(page, entry));

        page.ScaleTextOverride = "500";
        Assert.Equal("1:500", DrawingScaleText.Resolve(page, entry));
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
    public void ConceptPortraitFullSheetMigration_UsesStudioChromeWithoutRasterFallback()
    {
        string sourcePath = Path.Combine(workDirectory, "portrait-elevation-full-sheet.pdf");
        WriteVectorPdf(sourcePath, [(297d, 420d, "Legacy Revit title block")]);
        SheetRecord sheet = Intake(
            sourcePath,
            297,
            420,
            pageCount: 1,
            cleanDrawing: false,
            contentKind: "Elevation",
            sheetDescription: "Portrait facade",
            portrait: true,
            includeFormatForFullSheet: true);
        var project = new AlbumProject
        {
            Name = "Portrait migration",
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept"),
        };
        project.Album.Pages.Add(new AlbumPageDefinition
        {
            SheetKey = sheet.Key,
            TemplateSlotId = "elevations",
            PageFormatId = PageFormatCatalog.SourceAsIsId,
            PlacementMode = PagePlacementMode.FullPage,
            FollowSourceFormat = true,
        });
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(sheet.ManifestPath));

        AlbumBuildRequest request = AlbumBuilder.CreateRequest(project, library);
        AlbumBuildPage buildPage = Assert.Single(request.Sections.SelectMany(section => section.Pages));
        Assert.Equal(PageFormatCatalog.ConceptElevationA3PortraitTopId, buildPage.Format.Id);
        Assert.Equal(PagePlacementMode.FullPage, buildPage.Definition.PlacementMode);

        string outputPath = Path.Combine(workDirectory, "portrait-elevation-migrated.pdf");
        new AlbumBuilder(new PdfSharpAlbumWriter()).Build(project, library, outputPath);

        PdfVectorPageProfile page = Assert.Single(
            PdfVectorQualityInspector.Inspect(outputPath).Pages,
            candidate =>
                Math.Abs(candidate.WidthMm - 297) < 0.01 &&
                Math.Abs(candidate.HeightMm - 420) < 0.01);
        Assert.InRange(page.WidthMm, 296.99, 297.01);
        Assert.InRange(page.HeightMm, 419.99, 420.01);
        Assert.True(page.HasTextOperators);
        Assert.True(page.HasPathPaintingOperators);
        Assert.Equal(0, page.ImageXObjectCount);
        Assert.Contains(page.XObjects, item =>
            item.Kind == PdfVectorXObjectKind.Form &&
            Math.Abs(item.WidthMm - 297) < 0.01 &&
            Math.Abs(item.HeightMm - 420) < 0.01);
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
    public void WorkingDrawingTitleBlock_UsesProjectCompanyLogo()
    {
        string logoPath = Path.Combine(workDirectory, "working-company-logo.png");
        File.WriteAllBytes(
            logoPath,
            Convert.FromBase64String(
                "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII="));
        string sourcePath = Path.Combine(workDirectory, "working-title-block-source.pdf");
        WriteVectorPdf(sourcePath, [(390d, 277d, "Working drawing")]);
        SheetRecord sheet = Intake(sourcePath, 390, 277, pageCount: 1, cleanDrawing: false);

        string outputPath = BuildSingleSheetAlbum(
            sheet,
            PageFormatCatalog.WorkingDrawingA3LandscapeId,
            PagePlacementMode.PreserveDrawingSpace,
            configure: project =>
            {
                project.ProjectFolder = workDirectory;
                project.Name = "Project name";
                project.InitiationBasis.SiteAddress = "Project address";
                project.Company = new CompanyProfile { Name = "Company", LogoPath = logoPath };
            });

        PdfVectorPageProfile page = Assert.Single(PdfVectorQualityInspector.Inspect(outputPath).Pages);
        Assert.Equal(1, page.ImageXObjectCount);
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
        bool portrait = false,
        bool includeFormatForFullSheet = false,
        SheetPrintColorMode printColorMode = SheetPrintColorMode.Original)
    {
        PageFormatSpec? format = null;
        if (cleanDrawing || includeFormatForFullSheet)
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
                    PrintColorMode = printColorMode,
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

    private static PdfDictionary FindSingleFormXObject(PdfPage page)
    {
        PdfDictionary resources = page.Elements.GetDictionary("/Resources")
            ?? throw new InvalidDataException("Output page has no resources.");
        PdfDictionary xObjects = resources.Elements.GetDictionary("/XObject")
            ?? throw new InvalidDataException("Output page has no XObjects.");
        List<PdfDictionary> forms = [];
        foreach (string key in xObjects.Elements.Keys)
        {
            PdfItem? item = xObjects.Elements[key];
            if (item is PdfReference reference)
                item = reference.Value;
            if (item is PdfDictionary dictionary &&
                dictionary.Elements.GetName("/Subtype").Equals(
                    "/Form",
                    StringComparison.Ordinal))
            {
                forms.Add(dictionary);
            }
        }

        return Assert.Single(forms);
    }

    private static IReadOnlyList<string> ColorOperatorSignature(CSequence sequence) =>
        EnumerateOperators(sequence)
            .Where(operation => operation.Name is
                "G" or "g" or "RG" or "rg" or "K" or "k" or "SC" or "sc" or "SCN" or "scn")
            .Select(operation =>
                operation.Name + ":" + string.Join(
                    ",",
                    operation.Operands.Select(operand => operand switch
                    {
                        CInteger integer => integer.Value.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        CReal real => real.Value.ToString(
                            "R",
                            System.Globalization.CultureInfo.InvariantCulture),
                        _ => operand.ToString(),
                    })))
            .ToList();

    private static IEnumerable<COperator> EnumerateOperators(CSequence sequence)
    {
        foreach (CObject item in sequence)
        {
            if (item is COperator operation)
            {
                yield return operation;
            }
            else if (item is CSequence nested)
            {
                foreach (COperator nestedOperation in EnumerateOperators(nested))
                    yield return nestedOperation;
            }
        }
    }

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
        IReadOnlyList<(double WidthMm, double HeightMm, string Label)> pages,
        bool applyCropBox = true)
    {
        using var document = new PdfDocument();
        foreach ((double widthMm, double heightMm, string label) in pages)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(widthMm);
            page.Height = XUnit.FromMillimeter(heightMm);
            if (applyCropBox)
            {
                double crop = XUnit.FromMillimeter(2).Point;
                page.CropBox = new PdfRectangle(
                    new XPoint(crop, crop),
                    new XPoint(page.Width.Point - crop, page.Height.Point - crop));
            }
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
