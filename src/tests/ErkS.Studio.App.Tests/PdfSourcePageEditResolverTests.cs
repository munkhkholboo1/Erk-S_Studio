using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class PdfSourcePageEditResolverTests
{
    [Fact]
    public void Resolve_NoSelection_DisablesButton()
    {
        PdfSourcePageEditResolution result = PdfSourcePageEditResolver.Resolve(
            PdfSource(),
            [],
            []);

        Assert.Equal(PdfSourcePageEditState.NoSelection, result.State);
        Assert.False(result.IsButtonEnabled);
        Assert.Null(result.Sheet);
        Assert.Null(result.Page);
    }

    [Fact]
    public void Resolve_MultipleSelection_DisablesButton()
    {
        SheetRecord first = Sheet("pdf-source|page-1", "page-1");
        SheetRecord second = Sheet("pdf-source|page-2", "page-2");

        PdfSourcePageEditResolution result = PdfSourcePageEditResolver.Resolve(
            PdfSource(),
            [first, second],
            []);

        Assert.Equal(PdfSourcePageEditState.MultipleSelection, result.State);
        Assert.False(result.IsButtonEnabled);
        Assert.Null(result.Sheet);
        Assert.Null(result.Page);
    }

    [Fact]
    public void Resolve_NonPdfProjectSource_DisablesButton()
    {
        SheetRecord sheet = Sheet("pdf-source|page-1", "page-1");
        var source = new ProjectDesignSource
        {
            Id = "pdf-source",
            Kind = DesignSourceKind.Folder,
        };

        PdfSourcePageEditResolution result = PdfSourcePageEditResolver.Resolve(
            source,
            [sheet],
            []);

        Assert.Equal(PdfSourcePageEditState.NotPdf, result.State);
        Assert.False(result.IsButtonEnabled);
        Assert.Same(sheet, result.Sheet);
        Assert.Null(result.Page);
    }

    [Fact]
    public void Resolve_NonPdfSheet_DisablesButton()
    {
        SheetRecord sheet = Sheet(
            "pdf-source|page-1",
            "page-1",
            SheetSourceApplication.Revit);

        PdfSourcePageEditResolution result = PdfSourcePageEditResolver.Resolve(
            PdfSource(),
            [sheet],
            []);

        Assert.Equal(PdfSourcePageEditState.NotPdf, result.State);
        Assert.False(result.IsButtonEnabled);
        Assert.Same(sheet, result.Sheet);
        Assert.Null(result.Page);
    }

    [Fact]
    public void Resolve_InactiveSheet_WinsBeforeAlbumPageLookupAndKeepsButtonEnabled()
    {
        SheetRecord sheet = Sheet("pdf-source|page-1", "page-1");
        ProjectDesignSource source = PdfSource();
        source.SetSheetActive(sheet.Entry.SheetId, active: false);
        var stalePage = new AlbumPageDefinition { SheetKey = sheet.Key };

        PdfSourcePageEditResolution result = PdfSourcePageEditResolver.Resolve(
            source,
            [sheet],
            [stalePage]);

        Assert.Equal(PdfSourcePageEditState.Inactive, result.State);
        Assert.True(result.IsButtonEnabled);
        Assert.Same(sheet, result.Sheet);
        Assert.Null(result.Page);
    }

    [Fact]
    public void Resolve_ExactSheetKey_ReturnsReadyTarget()
    {
        SheetRecord sheet = Sheet("pdf-source|page-1", "page-1");
        var unrelatedPage = new AlbumPageDefinition
        {
            SheetKey = "pdf-source|other-page",
        };
        var matchingPage = new AlbumPageDefinition { SheetKey = sheet.Key };

        PdfSourcePageEditResolution result = PdfSourcePageEditResolver.Resolve(
            PdfSource(),
            [sheet],
            [unrelatedPage, matchingPage]);

        Assert.Equal(PdfSourcePageEditState.Ready, result.State);
        Assert.True(result.IsButtonEnabled);
        Assert.Same(sheet, result.Sheet);
        Assert.Same(matchingPage, result.Page);
    }

    [Fact]
    public void Resolve_SheetKeyComparison_IsOrdinal()
    {
        SheetRecord sheet = Sheet("pdf-source|page-1", "page-1");
        var differentlyCasedPage = new AlbumPageDefinition
        {
            SheetKey = "PDF-SOURCE|PAGE-1",
        };

        PdfSourcePageEditResolution result = PdfSourcePageEditResolver.Resolve(
            PdfSource(),
            [sheet],
            [differentlyCasedPage]);

        Assert.Equal(PdfSourcePageEditState.AlbumPageMissing, result.State);
        Assert.True(result.IsButtonEnabled);
        Assert.Same(sheet, result.Sheet);
        Assert.Null(result.Page);
    }

    [Fact]
    public void Resolve_ActiveSheetWithoutAlbumPage_KeepsButtonEnabled()
    {
        SheetRecord sheet = Sheet("pdf-source|page-1", "page-1");

        PdfSourcePageEditResolution result = PdfSourcePageEditResolver.Resolve(
            PdfSource(),
            [sheet],
            []);

        Assert.Equal(PdfSourcePageEditState.AlbumPageMissing, result.State);
        Assert.True(result.IsButtonEnabled);
        Assert.Same(sheet, result.Sheet);
        Assert.Null(result.Page);
    }

    [Fact]
    public void Resolve_DoesNotMapAlbumPageBySheetId()
    {
        SheetRecord sheet = Sheet("pdf-source|page-1", "page-1");
        var sheetIdPage = new AlbumPageDefinition
        {
            SheetKey = sheet.Entry.SheetId,
        };

        PdfSourcePageEditResolution result = PdfSourcePageEditResolver.Resolve(
            PdfSource(),
            [sheet],
            [sheetIdPage]);

        Assert.Equal(PdfSourcePageEditState.AlbumPageMissing, result.State);
        Assert.True(result.IsButtonEnabled);
        Assert.Null(result.Page);
    }

    private static ProjectDesignSource PdfSource() => new()
    {
        Id = "pdf-source",
        Kind = DesignSourceKind.Pdf,
    };

    private static SheetRecord Sheet(
        string key,
        string sheetId,
        SheetSourceApplication application = SheetSourceApplication.Pdf) => new()
    {
        Key = key,
        SourceId = "pdf-source",
        SourceIdentity = "pdf-source",
        Entry = new SheetPackageEntry
        {
            SheetId = sheetId,
            Number = sheetId,
            Name = sheetId,
        },
        Source = new SheetPackageSource
        {
            SourceId = "pdf-source",
            Application = application,
        },
        PackageId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ManifestPath = "manifest.json",
        PdfPath = "source.pdf",
        SourceSheetIndex = 0,
        ExportedAtUtc = DateTimeOffset.UnixEpoch,
        IsVerified = true,
    };
}
