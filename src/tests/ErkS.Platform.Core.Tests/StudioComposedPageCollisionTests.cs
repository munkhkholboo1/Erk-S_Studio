using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The last way a producer's sheet could still land beside the page Studio
/// draws.
/// </summary>
/// <remarks>
/// Both producers now filter these out by exact title, which is the right test
/// for them to use - the looser ones would have swallowed unrelated sheets. It
/// leaves hand-typed titles open, and only the receiving side can close that.
///
/// The inputs here are therefore the near misses rather than the exact hits: a
/// test built on the canonical title would pass whether or not the comparison
/// tolerated anything.
/// </remarks>
public sealed class StudioComposedPageCollisionTests
{
    private static AlbumDefinition WorkingDrawingAlbum() =>
        BuildingWorkingDrawingAlbumTemplate.CreateDefinition(
            BuildingWorkingDrawingAlbumTemplate.DefaultTitle);

    [Fact]
    public void TheCoverRevitSendsIsRecognised()
    {
        AlbumCompositionItem? slot = StudioComposedPageCollision.Find(
            WorkingDrawingAlbum(),
            new SheetPackageEntry { SheetId = "s1", Number = "00", Name = "НҮҮР ХУУДАС" });

        Assert.Equal("cover", slot?.Id);
    }

    [Theory]
    [InlineData("ЗУРГИЙН ЖАГСААЛТ ТАЙЛБАР БИЧИГ")]   // the comma dropped
    [InlineData("Зургийн жагсаалт, тайлбар бичиг")]   // typed in lower case
    [InlineData("  ЗУРГИЙН  ЖАГСААЛТ,  ТАЙЛБАР  БИЧИГ  ")] // spacing
    public void ATitleTypedByHandIsStillRecognised(string name)
    {
        AlbumCompositionItem? slot = StudioComposedPageCollision.Find(
            WorkingDrawingAlbum(),
            new SheetPackageEntry { SheetId = "s2", Number = "01", Name = name });

        Assert.Equal("drawing-list-and-notes", slot?.Id);
    }

    [Fact]
    public void AProducerNamingTheSlotOutrightIsRecognisedWhateverTheTitleSays()
    {
        AlbumCompositionItem? slot = StudioComposedPageCollision.Find(
            WorkingDrawingAlbum(),
            new SheetPackageEntry
            {
                SheetId = "s3",
                Number = "ЕХ-07",
                Name = "Огт өөр нэр",
                TemplateSlotId = "cover",
            });

        Assert.Equal("cover", slot?.Id);
    }

    [Theory]
    [InlineData("НҮҮР ТАЛ")]                     // an elevation, not a cover
    [InlineData("ЕРӨНХИЙ ХЭСЭГ")]                // shares the cover's category
    [InlineData("НҮҮР ХУУДАСНЫ ЗУРАГ")]          // contains the title, is not it
    [InlineData("1 давхрын байгуулалт")]
    public void OrdinaryDrawingsAreLeftAlone(string name)
    {
        AlbumCompositionItem? slot = StudioComposedPageCollision.Find(
            WorkingDrawingAlbum(),
            new SheetPackageEntry { SheetId = "s4", Number = "АР-01", Name = name });

        Assert.Null(slot);
    }

    [Fact]
    public void AnAlbumThatComposesNothingCollidesWithNothing()
    {
        var album = new AlbumDefinition
        {
            Composition =
            [
                new AlbumCompositionItem
                {
                    Id = "sheets",
                    Title = "НҮҮР ХУУДАС",
                    Kind = AlbumCompositionKind.SourceSlot,
                },
            ],
        };

        // The title matches, but the slot is one sheets arrive into rather than
        // one Studio draws. Only Studio-composed slots can be duplicated.
        Assert.Null(StudioComposedPageCollision.Find(
            album,
            new SheetPackageEntry { SheetId = "s5", Name = "НҮҮР ХУУДАС" }));
    }

    [Fact]
    public void TheMessageNamesTheSheetAndSaysWhereItWent()
    {
        AlbumDefinition album = WorkingDrawingAlbum();
        var entry = new SheetPackageEntry { SheetId = "s6", Number = "00", Name = "НҮҮР ХУУДАС" };

        string message = StudioComposedPageCollision.Describe(
            entry,
            StudioComposedPageCollision.Find(album, entry)!);

        Assert.Contains("00 НҮҮР ХУУДАС", message);
        Assert.Contains("альбомд орсонгүй", message);
        Assert.Contains("Эх үүсвэрийн жагсаалтад хэвээр", message);
    }
}
