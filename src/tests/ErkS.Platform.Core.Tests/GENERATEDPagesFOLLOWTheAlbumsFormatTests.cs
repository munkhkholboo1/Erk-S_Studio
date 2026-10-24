using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// 🔴 Owner, 2026-10-24 (BN album, through the CGA session): «альбумныхаа форматыг дагах ёстой.
/// нүүр хуудас мөн адил A2 дээр хийгдэх ёстой». The building sheets were Erk-S A2 (582 × 420)
/// and Studio drew its own cover at A3 (420 × 297) because nobody had chosen a format for the
/// pages it generates. Unchosen, that format now follows the album's sheets; and a sheet whose
/// format differs from the album's is named in the review.
/// </summary>
public sealed class GENERATEDPagesFOLLOWTheAlbumsFormatTests
{
    [Theory]
    [InlineData(582, 420, 1, 2, "LANDSCAPE")]
    [InlineData(420, 297, 1, 1, "LANDSCAPE")]
    [InlineData(828, 297, 2, 1, "LANDSCAPE")]
    [InlineData(420, 582, 1, 2, "PORTRAIT")]
    public void ASheetSizeIsReadAsItsModuleGrid(double width, double height, int columns, int rows, string orientation)
    {
        PageFormatDefinition? format = WorkingDrawingAlbumFormatFactory.FromSheetSize(width, height);

        Assert.NotNull(format);
        Assert.Equal(WorkingDrawingAlbumFormatFactory.Create(columns, rows, orientation).GeometryHash, format!.GeometryHash);
    }

    [Fact]
    public void ASizeOffTheGridIsNotGuessed() =>
        Assert.Null(WorkingDrawingAlbumFormatFactory.FromSheetSize(500, 350));

    [Fact]
    public void TheCommonestSheetFormatIsTheAlbumsWhenNoneWasChosen()
    {
        var album = new AlbumDefinition();
        album.InferredPageFormat = WorkingDrawingAlbumFormatFactory.Dominant([(582, 420), (582, 420), (420, 297)]);

        PageFormatDefinition resolved = WorkingDrawingAlbumFormatFactory.Resolve(album);

        Assert.Equal(582, resolved.WidthMm, 0.5);
        Assert.Equal(420, resolved.HeightMm, 0.5);
    }

    [Fact]
    public void AnA2AlbumDrawsItsGeneratedPagesOnA2EvenUnchosen()
    {
        // BN, 2026-10-24: every sheet 582 × 420, nothing chosen - the cover came out 420 × 297.
        var album = new AlbumDefinition { InferredPageFormat = WorkingDrawingAlbumFormatFactory.Create(1, 2) };

        Assert.True(DrawingListPagination.UsesWorkingDrawingFormat(album));
    }

    [Fact]
    public void AnA3AlbumKeepsItsA3Pages() =>
        Assert.False(DrawingListPagination.UsesWorkingDrawingFormat(
            new AlbumDefinition { InferredPageFormat = WorkingDrawingAlbumFormatFactory.Create(1, 1) }));

    [Fact]
    public void AFormatAPersonChoseStillWins()
    {
        var album = new AlbumDefinition
        {
            GeneratedPageFormat = WorkingDrawingAlbumFormatFactory.Create(1, 1),
            InferredPageFormat = WorkingDrawingAlbumFormatFactory.Create(1, 2),
        };

        Assert.Equal(420, WorkingDrawingAlbumFormatFactory.Resolve(album).WidthMm, 0.5);
    }
}
