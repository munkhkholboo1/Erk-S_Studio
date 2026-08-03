using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Platform.Core.Tests;

public sealed class UrbanPlanningDrawingSequenceTests
{
    [Theory]
    [InlineData(MasterPlanDrawingSequence.StageType, UrbanPlanningAlbumTemplate.MasterPlanTemplateId, "Хөгжлийн ерөнхий төлөвлөгөө (ХЕТ)")]
    [InlineData(PartialMasterPlanDrawingSequence.StageType, UrbanPlanningAlbumTemplate.PartialPlanTemplateId, "Хэсэгчилсэн ерөнхий төлөвлөгөө (ХЕТ)")]
    public void AlbumTemplate_UsesSeparateFullNamesAndSharedHetAbbreviation(
        string stageType,
        string templateId,
        string expectedTitle)
    {
        AlbumDefinition definition = UrbanPlanningAlbumTemplate.CreateDefinition(stageType);

        Assert.Equal(templateId, definition.TemplateId);
        Assert.Equal(expectedTitle, definition.Title);
        Assert.Equal(15, definition.Composition.Count);
        Assert.Equal("ЕТ-01", definition.Composition[0].Number);
        Assert.Equal("ИДБ-05", definition.Composition[^1].Number);
        Assert.Equal(AlbumCompositionKind.Generated, definition.Composition[0].Kind);
        Assert.Equal(AlbumCompositionKind.Generated, definition.Composition[1].Kind);
        Assert.Equal(AlbumCompositionKind.SourceSlot, definition.Composition[2].Kind);
        Assert.True(definition.Composition[3].AllowMultiple);
    }

    [Fact]
    public void Resolver_MigratesExistingDrawingListToStudioGeneratedWithoutReplacingAlbum()
    {
        var workspace = new ProjectWorkspace();
        workspace.Identity.ProjectType = ProjectTypes.UrbanPlanningProjectType.TypeId;
        workspace.Identity.StageCode = PartialMasterPlanDrawingSequence.StageType;
        workspace.Deliverables.Albums.Add(new ProjectAlbumRecord { IsPrimary = true });
        AlbumDefinition definition = UrbanPlanningAlbumTemplate.CreateDefinition(workspace.Identity.StageCode);
        AlbumCompositionItem drawingList = definition.Composition.Single(item => item.Id == "drawing-list-and-notes");
        drawingList.Kind = AlbumCompositionKind.SourceSlot;
        definition.Pages.Add(new AlbumPageDefinition());
        var album = new StudioAlbumDocument { Definition = definition };

        bool changed = ProjectAlbumTemplateResolver.Apply(workspace, album);

        Assert.True(changed);
        Assert.Same(definition, album.Definition);
        Assert.Equal(AlbumCompositionKind.Generated, drawingList.Kind);
        Assert.Single(album.Definition.Pages);
    }

    [Fact]
    public void Resolver_ReplacesEmptyLegacyAlbumWhenClassificationChangesToHet()
    {
        var workspace = new ProjectWorkspace();
        workspace.Deliverables.Albums.Add(new ProjectAlbumRecord { IsPrimary = true });
        workspace.Identity.ProjectType = ProjectTypes.UrbanPlanningProjectType.TypeId;
        workspace.Identity.StageCode = PartialMasterPlanDrawingSequence.StageType;
        var album = new StudioAlbumDocument
        {
            Definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Хуучин бүрдэл"),
        };

        bool changed = ProjectAlbumTemplateResolver.Apply(workspace, album);

        Assert.True(changed);
        Assert.Equal(UrbanPlanningAlbumTemplate.PartialPlanTemplateId, album.Definition.TemplateId);
        Assert.Equal("Хэсэгчилсэн ерөнхий төлөвлөгөө (ХЕТ)", album.Definition.Title);
        Assert.Equal(15, album.Definition.Composition.Count);
    }

    [Fact]
    public void Resolver_ReplacesConceptContaminationAndPreservesReceivedSourcePages()
    {
        var workspace = new ProjectWorkspace();
        workspace.Identity.ProjectType = ProjectTypes.UrbanPlanningProjectType.TypeId;
        workspace.Identity.StageCode = PartialMasterPlanDrawingSequence.StageType;
        workspace.Deliverables.Albums.Add(new ProjectAlbumRecord { IsPrimary = true });
        workspace.Sources.Add(new ProjectDesignSource { Id = "source-1" });
        var sourcePage = new AlbumPageDefinition { SheetKey = "source-1|sheet-1" };
        var album = new StudioAlbumDocument
        {
            Definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("wrong"),
        };
        album.Definition.Pages.Add(sourcePage);

        bool changed = ProjectAlbumTemplateResolver.Apply(workspace, album);

        Assert.True(changed);
        Assert.Equal(UrbanPlanningAlbumTemplate.PartialPlanTemplateId, album.Definition.TemplateId);
        Assert.Same(sourcePage, Assert.Single(album.Definition.Pages));
        Assert.DoesNotContain(album.Definition.Composition,
            item => item.GeneratedPageKind is AlbumGeneratedPageKind.DesignOrganization or AlbumGeneratedPageKind.PlanningTask);
    }

    [Fact]
    public void AlbumStore_DoesNotInjectConceptTemplateIntoUrbanPlanningAlbum()
    {
        AlbumDefinition definition = UrbanPlanningAlbumTemplate.CreateDefinition(
            PartialMasterPlanDrawingSequence.StageType);
        var album = new StudioAlbumDocument
        {
            PackageType = ProjectTypes.UrbanPlanningProjectType.TypeId,
            StageCode = PartialMasterPlanDrawingSequence.StageType,
            Definition = definition,
        };

        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".erksalbum");
        try
        {
            StudioAlbumDocumentStore.Save(album, path);
            album = StudioAlbumDocumentStore.Load(path);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }

        Assert.Equal(UrbanPlanningAlbumTemplate.PartialPlanTemplateId, album.Definition.TemplateId);
        Assert.Equal(15, album.Definition.Composition.Count);
        Assert.DoesNotContain(album.Definition.Composition,
            item => item.GeneratedPageKind is AlbumGeneratedPageKind.DesignOrganization or AlbumGeneratedPageKind.PlanningTask);
    }

    [Theory]
    [InlineData(typeof(MasterPlanDrawingSequence))]
    [InlineData(typeof(PartialMasterPlanDrawingSequence))]
    public void InitialSequence_ContainsEtThenIdbInRequiredOrder(Type sequenceType)
    {
        var sequence = (IUrbanPlanningDrawingSequence)Activator.CreateInstance(sequenceType)!;

        Assert.Equal(Enumerable.Range(1, 15), sequence.Drawings.Select(item => item.Order));
        Assert.Equal(10, sequence.Drawings.Count(item => item.Mark == UrbanPlanningDrawingMarks.GeneralPlan));
        Assert.Equal(5, sequence.Drawings.Count(item => item.Mark == UrbanPlanningDrawingMarks.EngineeringInfrastructure));
        Assert.Equal(Enumerable.Range(1, 10), sequence.Drawings.Where(item => item.Mark == UrbanPlanningDrawingMarks.GeneralPlan).Select(item => item.MarkOrder));
        Assert.Equal(Enumerable.Range(1, 5), sequence.Drawings.Where(item => item.Mark == UrbanPlanningDrawingMarks.EngineeringInfrastructure).Select(item => item.MarkOrder));
        Assert.Equal("Нүүр хуудас", sequence.Drawings[0].Title);
        Assert.Equal("Инженерийн бэлтгэл арга хэмжээ", sequence.Drawings[^1].Title);
    }

    [Theory]
    [InlineData(typeof(MasterPlanDrawingSequence))]
    [InlineData(typeof(PartialMasterPlanDrawingSequence))]
    public void InitialSequence_MarksMapDrawingsAsNomenclatureReady(Type sequenceType)
    {
        var sequence = (IUrbanPlanningDrawingSequence)Activator.CreateInstance(sequenceType)!;

        Assert.All(sequence.Drawings.Skip(3), item =>
        {
            Assert.True(item.UsesNomenclatureGrid);
            Assert.True(item.AllowMultiplePages);
        });
        Assert.All(sequence.Drawings.Take(3), item => Assert.False(item.UsesNomenclatureGrid));
    }
}
