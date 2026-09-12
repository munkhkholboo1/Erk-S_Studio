using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Studio's own ceiling on its raster pages, and it is a DENSITY.
///
/// 🔴 THE OWNER DECIDED THIS AND NAMED THE REASON: «харагдах байдлын хуудаснуудын
/// ачааллыг бууруулж Студиогийн өөрийнх нь хангалттай гэж үзэх хэмжээнд бариулж
/// болно. Тэгэхгүй бол харагдах байдлын нэг зураг л гэхэд 16к рендер байгаа.» They
/// render at whatever size suits them; the album decides what it carries.
///
/// 🔴 A FIXED PIXEL COUNT WOULD BE WRONG AND WOULD LOOK RIGHT. 2125 px is correct
/// for a 180 mm frame and for nothing else: on a full-width 395 mm tile it starves
/// the image, on a small tile it keeps four times what is needed. The measurement
/// that transfers between frames is millimetres per pixel.
/// </summary>
public sealed class THECEILINGIsDensityNotPixelsTests
{
    [Fact]
    public void THEBudgetMatchesTheNumbersMeasuredOnTheOwnersSheet()
    {
        // The three spans named while this was designed. If the constant moves,
        // these move with it - which is the point: they are its consequences,
        // not independent facts.
        //
        // ⚠ 395 mm is 4665.35 px exactly, so rounding UP gives 4666 - one more
        // than the 4665 quoted while this was being discussed. The quoted figure
        // was the truncation. Rounding up is the deliberate choice: a pixel short
        // of the target is a visible loss on a printed sheet and an invisible
        // saving in the file.
        Assert.Equal(2126, VisualizationRasterBudget.PixelsAcross(180d));
        Assert.Equal(3071, VisualizationRasterBudget.PixelsAcross(260d));
        Assert.Equal(4666, VisualizationRasterBudget.PixelsAcross(395d));
    }

    [Fact]
    public void THEDensityIsTHREEHUNDREDDotsPerInchAndSaysSo()
    {
        // 🔴 ONE HOME, AND THIS ASSERTS IT. The number is the OWNER'S declared
        // raster rule - «яг зөв растерийн дүрэм 300dpi» - not a setting of the
        // visualisation pages, so the budget must READ it rather than keep a copy.
        // Two constants would agree today and drift the first time one moved,
        // leaving the next raster path two authoritative-looking numbers.
        Assert.Equal(300d, AlbumRasterRule.DotsPerInch);
        Assert.Equal(
            AlbumRasterRule.MillimetresPerPixel,
            VisualizationRasterBudget.MillimetresPerPixel);
        Assert.Equal(
            AlbumRasterRule.PixelsAcross(180d),
            VisualizationRasterBudget.PixelsAcross(180d));

        // One inch of page is exactly the target many pixels - the definition,
        // asserted so a change to the arithmetic cannot pass as a change to the
        // constant.
        Assert.Equal(300, VisualizationRasterBudget.PixelsAcross(25.4d));
    }

    [Fact]
    public void A16KRenderInAFourUpTileIsCUTToTheBudget()
    {
        // The owner's actual case: a 7680-wide render on the default four-per-page
        // layout. The tile is about 192.5 mm across, so the render carries roughly
        // three times the pixels the page can show.
        VisualizationRasterPlan plan = VisualizationRasterBudget.For(
            7680,
            4875,
            Frame(192.5d, 117.5d),
            VisualizationImageFitMode.Contain);

        Assert.True(plan.Resample);
        Assert.True(
            plan.PixelWidth < 7680,
            $"the render was not reduced: {plan.PixelWidth}");

        // The aspect ratio survives - a frame-shaped resize would squash it.
        double before = 7680d / 4875d;
        double after = (double)plan.PixelWidth / plan.PixelHeight;
        Assert.True(Math.Abs(before - after) < 0.01d, $"{before} became {after}");
    }

    [Fact]
    public void ANImageALREADYBelowTheBudgetIsLEFTALONE()
    {
        // 🔴 SCALING UP INVENTS DETAIL THAT WAS NEVER PHOTOGRAPHED, and grows the
        // file to do it - the exact opposite of what this ceiling is for.
        VisualizationRasterPlan plan = VisualizationRasterBudget.For(
            800,
            600,
            Frame(395d, 245d),
            VisualizationImageFitMode.Contain);

        Assert.False(plan.Resample);
        Assert.Equal(800, plan.PixelWidth);
        Assert.Equal(600, plan.PixelHeight);
    }

    [Fact]
    public void ANImageEXACTLYAtTheBudgetIsLEFTALONE()
    {
        // 🔴 THE BOUNDARY, AND IT CAUGHT A REAL DEFECT. PixelsAcross rounds UP, so
        // an image prepared to exactly the budget is marginally DENSER than the
        // budget - and a rule that compared densities called it «resample», for
        // ever, meaning the prepared-file cache would never once report a hit and
        // every build would re-encode work it had already done. Deciding on the
        // PIXEL COUNT is exact integer arithmetic with no epsilon to tune.
        //
        // Checked from both sides: exactly enough is no work, twice as much is.
        int exact = VisualizationRasterBudget.PixelsAcross(180d);

        Assert.False(
            VisualizationRasterBudget.For(exact, exact, Frame(180d, 180d), VisualizationImageFitMode.Contain).Resample,
            "an image already at the budget was resampled for nothing");
        Assert.True(
            VisualizationRasterBudget.For(exact * 2, exact * 2, Frame(180d, 180d), VisualizationImageFitMode.Contain).Resample,
            "an image at twice the budget was left alone");
    }

    [Fact]
    public void CENTERCROPNeedsMOREPixelsThanCONTAINForTheSameFrame()
    {
        // 🔴 THE TWO MODES DISAGREE AND ONE RULE FOR BOTH WOULD LEAVE EVERY
        // CROPPED TILE SOFT. Contain fits the image inside the frame, so one
        // dimension touches the edge; CenterCrop covers the frame, so the image is
        // LARGER than the frame and its off-sheet parts still occupy pixels.
        PageRectMm frame = Frame(180d, 180d);

        VisualizationRasterPlan contain = VisualizationRasterBudget.For(
            8000, 4000, frame, VisualizationImageFitMode.Contain);
        VisualizationRasterPlan crop = VisualizationRasterBudget.For(
            8000, 4000, frame, VisualizationImageFitMode.CenterCrop);

        Assert.True(
            crop.PixelWidth > contain.PixelWidth,
            $"crop {crop.PixelWidth} should need more than contain {contain.PixelWidth}");
    }

    [Fact]
    public void CONTAINIsLimitedByWhicheverDimensionFitsFirst()
    {
        // A wide image in a square frame is limited by its width; a tall one by
        // its height. Taking the wrong one would over- or under-shoot by the
        // aspect ratio, which on a 16K render is not a small error.
        PageRectMm square = Frame(100d, 100d);
        int budget = VisualizationRasterBudget.PixelsAcross(100d);

        VisualizationRasterPlan wide = VisualizationRasterBudget.For(
            8000, 2000, square, VisualizationImageFitMode.Contain);
        Assert.Equal(budget, wide.PixelWidth);

        VisualizationRasterPlan tall = VisualizationRasterBudget.For(
            2000, 8000, square, VisualizationImageFitMode.Contain);
        Assert.Equal(budget, tall.PixelHeight);

        // 🔴 AND THE OTHER DIMENSION IS THE IMAGE'S, NOT THE FRAME'S - THIS IS
        // WHERE A SQUASH SHOWS. A mutation that set the height from the frame
        // instead of the scale survived the four-up case, because there the height
        // happens to BE the limiting dimension and the two agree exactly. On a
        // wide image in a square frame they differ by the aspect ratio, and the
        // photograph is stretched to fill a shape it never had.
        // ⚠ ASSERTED AS A RATIO, NOT AS A COMPUTED PIXEL COUNT. A first version
        // wrote «budget / 4» and was wrong by one - two independent ceilings do
        // not divide - which is the shape of restating the implementation's own
        // arithmetic in the test and getting it slightly different. The property
        // is what matters: the shape is preserved.
        Assert.Equal(4d, (double)wide.PixelWidth / wide.PixelHeight, precision: 1);
        Assert.Equal(0.25d, (double)tall.PixelWidth / tall.PixelHeight, precision: 2);

        Assert.True(
            wide.PixelHeight < budget,
            $"the height was taken from the frame, not the image: {wide.PixelHeight}");
    }

    [Fact]
    public void THEPLANNERSOwnFramesAreWhatThisIsMeasuredAgainst()
    {
        // 🔴 THE FRAME COMES FROM THE PRODUCT, NOT FROM THIS TEST. A budget
        // computed against invented millimetres would agree with itself forever
        // while disagreeing with the sheet - my own fixture is not the product.
        var source = new ProjectVisualizationSource();
        for (var index = 0; index < VisualizationPageLayoutPlanner.DefaultImagesPerPage; index++)
        {
            source.Images.Add(new ProjectVisualizationImage
            {
                Id = "img" + index,
                PixelWidth = 7680,
                PixelHeight = 4875,
                RelativePath = "v" + index + ".png",
            });
        }

        IReadOnlyList<VisualizationAlbumPagePlan> pages =
            VisualizationPageLayoutPlanner.Create(source, firstPageNumber: 1);

        VisualizationAlbumPagePlan page = Assert.Single(pages);
        Assert.Equal(
            VisualizationPageLayoutPlanner.DefaultImagesPerPage,
            page.Tiles.Count);

        foreach (VisualizationImageTilePlan tile in page.Tiles)
        {
            VisualizationRasterPlan plan = VisualizationRasterBudget.For(
                tile.Image.PixelWidth,
                tile.Image.PixelHeight,
                tile.Frame,
                tile.FitMode);

            Assert.True(plan.Resample, "a 16K render in the product's own tile was left alone");
            Assert.True(
                plan.PixelWidth < tile.Image.PixelWidth,
                $"tile {tile.Frame.Width:0.#} mm kept {plan.PixelWidth} of {tile.Image.PixelWidth}");
        }
    }

    [Fact]
    public void ANUNMEASUREDImageIsLEFTALONERatherThanGuessedAt()
    {
        // A zero means the record was never filled in. Inventing a size here
        // would write a number nobody measured into a signed sheet.
        foreach ((int w, int h) in new[] { (0, 100), (100, 0), (0, 0), (-1, 5) })
        {
            VisualizationRasterPlan plan = VisualizationRasterBudget.For(
                w, h, Frame(180d, 180d), VisualizationImageFitMode.Contain);
            Assert.False(plan.Resample, $"{w}x{h} was resampled");
            Assert.True(plan.PixelWidth >= 1 && plan.PixelHeight >= 1);
        }

        // A frame nobody measured is the same state from the other side.
        Assert.False(
            VisualizationRasterBudget.For(7680, 4875, Frame(0d, 0d), VisualizationImageFitMode.Contain).Resample);
    }

    [Fact]
    public void APLANNeverAsksForZeroPixels()
    {
        // A tiny frame still needs one pixel. Zero would be a file nothing can
        // open, produced silently from a layout nobody thought was extreme.
        VisualizationRasterPlan plan = VisualizationRasterBudget.For(
            7680, 4875, Frame(0.01d, 0.01d), VisualizationImageFitMode.Contain);

        Assert.True(plan.PixelWidth >= 1);
        Assert.True(plan.PixelHeight >= 1);
        Assert.Equal(1, VisualizationRasterBudget.PixelsAcross(0d));
        Assert.Equal(1, VisualizationRasterBudget.PixelsAcross(-5d));
    }

    private static PageRectMm Frame(double widthMm, double heightMm) => new()
    {
        X = 0d,
        Y = 0d,
        Width = widthMm,
        Height = heightMm,
    };
}
