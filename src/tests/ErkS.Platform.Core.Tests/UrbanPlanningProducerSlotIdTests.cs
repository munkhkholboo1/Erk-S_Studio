using ErkS.Platform.Core;
using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The producer names the slot each sheet belongs in. Studio used to work the
/// answer out again from the sheet number and title instead.
/// </summary>
/// <remarks>
/// Across three real AutoCAD packages - 63 sheets - the guess agreed with the
/// producer on every one. That is why the tests here deliberately break the
/// agreement: while the number and the title still point at the right slot,
/// reading the id and guessing it are indistinguishable, and a test built on
/// such data would pass whichever one the code did.
///
/// The user renumbering a drawing, or retitling it, is the ordinary event that
/// separates them.
/// </remarks>
public sealed class UrbanPlanningProducerSlotIdTests
{
    [Fact]
    public void ARenumberedSheetStillLandsInTheSlotItsProducerNamed()
    {
        AlbumDefinition album =
            UrbanPlanningAlbumTemplate.CreateDefinition(PartialMasterPlanDrawingSequence.StageType);

        AlbumCompositionItem? slot = UrbanPlanningAlbumTemplate.FindSourceSlot(
            album,
            number: "ЕТ-99",
            contentKind: "",
            name: "",
            templateSlotId: "pedestrian-movement");

        Assert.NotNull(slot);
        Assert.Equal("pedestrian-movement", slot!.Id);
    }

    [Fact]
    public void ARetitledSheetStillLandsThere()
    {
        AlbumDefinition album =
            UrbanPlanningAlbumTemplate.CreateDefinition(PartialMasterPlanDrawingSequence.StageType);

        AlbumCompositionItem? slot = UrbanPlanningAlbumTemplate.FindSourceSlot(
            album,
            number: "",
            contentKind: "",
            name: "Зам талбайн шинэ нэр",
            templateSlotId: "grading");

        Assert.NotNull(slot);
        Assert.Equal("grading", slot!.Id);
    }

    [Fact]
    public void TheProducerWinsWhenTheCluesPointSomewhereElse()
    {
        // The number says ЕТ-13 (waste-management) and the producer says
        // grading. Before the id was read, this sheet went where the number
        // pointed - silently, and only on the day someone had renumbered.
        AlbumDefinition album =
            UrbanPlanningAlbumTemplate.CreateDefinition(PartialMasterPlanDrawingSequence.StageType);

        AlbumCompositionItem? slot = UrbanPlanningAlbumTemplate.FindSourceSlot(
            album,
            number: "ЕТ-13",
            contentKind: "",
            name: "",
            templateSlotId: "grading");

        Assert.NotNull(slot);
        Assert.Equal("grading", slot!.Id);
    }

    [Fact]
    public void WithoutTheIdTheCluesStillDoTheWork()
    {
        // Every building package and everything Revit sends leaves this empty,
        // so the older route has to keep working rather than becoming a branch
        // nothing exercises.
        AlbumDefinition album =
            UrbanPlanningAlbumTemplate.CreateDefinition(PartialMasterPlanDrawingSequence.StageType);

        AlbumCompositionItem? slot = UrbanPlanningAlbumTemplate.FindSourceSlot(
            album,
            number: "ЕТ-13",
            contentKind: "",
            name: "",
            templateSlotId: "");

        Assert.NotNull(slot);
        Assert.Equal("waste-management", slot!.Id);
    }

    [Fact]
    public void AnIdThisAlbumDoesNotHaveFallsThroughRatherThanSwallowingTheSheet()
    {
        // Nomenclature sheets carry ids no composition declares. They must not
        // be dropped on the way past, and they must not match a slot either -
        // they belong to no slot, which is a different thing from being lost.
        AlbumDefinition album =
            UrbanPlanningAlbumTemplate.CreateDefinition(PartialMasterPlanDrawingSequence.StageType);

        AlbumCompositionItem? byNumber = UrbanPlanningAlbumTemplate.FindSourceSlot(
            album,
            number: "ЕТ-14",
            contentKind: "",
            name: "",
            templateSlotId: "nomenclature:104-53");
        Assert.Equal("grading", byNumber?.Id);

        AlbumCompositionItem? nothing = UrbanPlanningAlbumTemplate.FindSourceSlot(
            album,
            number: "НМ-01",
            contentKind: "nomenclature:104-53",
            name: "Номенклатур 104-53",
            templateSlotId: "nomenclature:104-53");
        Assert.Null(nothing);
    }
}
