using ErkS.Platform.Core;
using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumSectionGroupingTests
{
    [Fact]
    public void PartialGeneralPlan_WithoutLinkedPages_DoesNotExposeCompositionSlotsAsPages()
    {
        var album = UrbanPlanningAlbumTemplate.CreateDefinition(
            PartialMasterPlanDrawingSequence.StageType);

        IReadOnlyList<StudioAlbumSectionGroup> groups =
            StudioAlbumSectionGrouping.ResolvePopulatedSourceSlots(album, []);

        Assert.Empty(groups);
    }

    [Fact]
    public void PartialGeneralPlan_OnlyExposesPopulatedSlotsInCompositionOrder()
    {
        var album = UrbanPlanningAlbumTemplate.CreateDefinition(
            PartialMasterPlanDrawingSequence.StageType);
        AlbumCompositionItem etSlot = album.Composition.First(item =>
            item.Kind == AlbumCompositionKind.SourceSlot &&
            item.Number.StartsWith("ЕТ-", StringComparison.Ordinal));
        AlbumCompositionItem engineeringSlot = album.Composition.First(item =>
            item.Kind == AlbumCompositionKind.SourceSlot &&
            item.Number.StartsWith("ИДБ-", StringComparison.Ordinal));

        IReadOnlyList<StudioAlbumSectionGroup> groups =
            StudioAlbumSectionGrouping.ResolvePopulatedSourceSlots(
                album,
                [engineeringSlot.Id, etSlot.Id]);

        Assert.Collection(
            groups,
            group =>
            {
                Assert.Contains("Ерөнхий төлөвлөгөө", group.Title);
                Assert.Equal([etSlot], group.Components);
            },
            group =>
            {
                Assert.Contains("Инженерийн дэд бүтэц", group.Title);
                Assert.Equal([engineeringSlot], group.Components);
            });
    }
}
