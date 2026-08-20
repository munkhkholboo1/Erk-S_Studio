using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Platform.Core.Tests;

public sealed class UrbanPlanningDrawingSequenceTests
{
    [Theory]
    [InlineData(MasterPlanDrawingSequence.StageType, UrbanPlanningAlbumTemplate.MasterPlanTemplateId, "Хөгжлийн ерөнхий төлөвлөгөө (ХЕТ)", 15)]
    [InlineData(PartialMasterPlanDrawingSequence.StageType, UrbanPlanningAlbumTemplate.PartialPlanTemplateId, "Хэсэгчилсэн ерөнхий төлөвлөгөө (ХЕТ)", 23)]
    public void AlbumTemplate_UsesSeparateFullNamesAndSharedHetAbbreviation(
        string stageType,
        string templateId,
        string expectedTitle,
        int expectedCompositionCount)
    {
        AlbumDefinition definition = UrbanPlanningAlbumTemplate.CreateDefinition(stageType);

        Assert.Equal(templateId, definition.TemplateId);
        Assert.Equal(expectedTitle, definition.Title);
        Assert.Equal(expectedCompositionCount, definition.Composition.Count);
        Assert.Equal("ЕТ-01", definition.Composition[0].Number);
        Assert.Equal("ИДБ-05", definition.Composition[^1].Number);
        Assert.Equal(AlbumCompositionKind.Generated, definition.Composition[0].Kind);
        Assert.Equal(AlbumCompositionKind.Generated, definition.Composition[1].Kind);
        if (stageType == PartialMasterPlanDrawingSequence.StageType)
        {
            AlbumCompositionItem siteContext = definition.Composition[2];
            Assert.Equal("site-context", siteContext.Id);
            Assert.Equal(AlbumCompositionKind.Generated, siteContext.Kind);
            Assert.Equal(AlbumGeneratedPageKind.SiteContext, siteContext.GeneratedPageKind);
            Assert.Equal(AlbumCompositionKind.SourceSlot, definition.Composition[3].Kind);
            Assert.True(definition.Composition[4].AllowMultiple);
        }
        else
        {
            Assert.Equal(AlbumCompositionKind.SourceSlot, definition.Composition[2].Kind);
            Assert.True(definition.Composition[3].AllowMultiple);
        }
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
        Assert.Equal(23, album.Definition.Composition.Count);
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
        Assert.Equal(23, album.Definition.Composition.Count);
        Assert.DoesNotContain(album.Definition.Composition,
            item => item.GeneratedPageKind is AlbumGeneratedPageKind.DesignOrganization or AlbumGeneratedPageKind.PlanningTask);
    }

    [Fact]
    public void Resolver_RestoresMissingPartialPlanSiteContextWithoutReplacingSourcePages()
    {
        var workspace = new ProjectWorkspace();
        workspace.Identity.ProjectType = ProjectTypes.UrbanPlanningProjectType.TypeId;
        workspace.Identity.StageCode = PartialMasterPlanDrawingSequence.StageType;
        workspace.Deliverables.Albums.Add(new ProjectAlbumRecord { IsPrimary = true });
        AlbumDefinition definition = UrbanPlanningAlbumTemplate.CreateDefinition(
            workspace.Identity.StageCode);
        AlbumCompositionItem siteContext = definition.Composition.Single(item =>
            item.Id == "site-context");
        definition.Composition.Remove(siteContext);
        var sourcePage = new AlbumPageDefinition { SheetKey = "source-1|sheet-1" };
        definition.Pages.Add(sourcePage);
        var album = new StudioAlbumDocument { Definition = definition };

        bool changed = ProjectAlbumTemplateResolver.Apply(workspace, album);

        Assert.True(changed);
        Assert.Same(definition, album.Definition);
        Assert.Same(sourcePage, Assert.Single(album.Definition.Pages));
        AlbumCompositionItem restored = album.Definition.Composition.Single(item =>
            item.Id == "site-context");
        Assert.Equal(AlbumCompositionKind.Generated, restored.Kind);
        Assert.Equal(AlbumGeneratedPageKind.SiteContext, restored.GeneratedPageKind);
    }

    [Fact]
    public void MasterPlanInitialSequence_ContainsEtThenIdbInRequiredOrder()
    {
        var sequence = new MasterPlanDrawingSequence();

        Assert.Equal(Enumerable.Range(1, 15), sequence.Drawings.Select(item => item.Order));
        Assert.Equal(10, sequence.Drawings.Count(item => item.Mark == UrbanPlanningDrawingMarks.GeneralPlan));
        Assert.Equal(5, sequence.Drawings.Count(item => item.Mark == UrbanPlanningDrawingMarks.EngineeringInfrastructure));
        Assert.Equal(Enumerable.Range(1, 10), sequence.Drawings.Where(item => item.Mark == UrbanPlanningDrawingMarks.GeneralPlan).Select(item => item.MarkOrder));
        Assert.Equal(Enumerable.Range(1, 5), sequence.Drawings.Where(item => item.Mark == UrbanPlanningDrawingMarks.EngineeringInfrastructure).Select(item => item.MarkOrder));
        Assert.Equal("Нүүр хуудас", sequence.Drawings[0].Title);
        Assert.Equal("Инженерийн бэлтгэл арга хэмжээ", sequence.Drawings[^1].Title);
    }

    [Fact]
    public void MasterPlanInitialSequence_MarksMapDrawingsAsNomenclatureReady()
    {
        var sequence = new MasterPlanDrawingSequence();

        Assert.All(sequence.Drawings.Skip(3), item =>
        {
            Assert.True(item.UsesNomenclatureGrid);
            Assert.True(item.AllowMultiplePages);
        });
        Assert.All(sequence.Drawings.Take(3), item => Assert.False(item.UsesNomenclatureGrid));
    }

    [Fact]
    public void PartialPlanSequence_CoversBd3010321CoreDrawingsAndOptionalRiskPlans()
    {
        var sequence = new PartialMasterPlanDrawingSequence();

        Assert.Equal(Enumerable.Range(1, 22), sequence.Drawings.Select(item => item.Order));
        Assert.Equal(17, sequence.Drawings.Count(item => item.Mark == UrbanPlanningDrawingMarks.GeneralPlan));
        Assert.Equal(5, sequence.Drawings.Count(item => item.Mark == UrbanPlanningDrawingMarks.EngineeringInfrastructure));
        Assert.Equal(20, sequence.Drawings.Count(item => item.Required));
        Assert.Equal(
            ["waste-management", "disaster-management"],
            sequence.Drawings.Where(item => !item.Required).Select(item => item.Id));
        Assert.Equal(
            [
                "development-context",
                "existing-condition",
                "demographic-economic-analysis",
                "street-road-transport",
                "pedestrian-movement",
                "red-lines",
                "general-plan-zoning",
                "development-projection",
                "social-service-accessibility",
                "green-infrastructure",
                "grading",
                "first-phase-land-management",
                "technical-economic-indicators",
                "integrated-engineering-networks",
                "water-and-sewer",
                "heating-supply",
                "power-supply",
                "communications-and-signaling",
            ],
            sequence.Drawings
                .Where(item => item.Id is not "cover" and not "drawing-list-and-notes" && item.Required)
                .Select(item => item.Id));
    }

    [Fact]
    public void PartialPlanSequence_OnlyMarksMapBasedDrawingsAsNomenclatureReady()
    {
        var sequence = new PartialMasterPlanDrawingSequence();
        string[] mapDrawingIds =
        [
            "existing-condition",
            "street-road-transport",
            "pedestrian-movement",
            "red-lines",
            "general-plan-zoning",
            "social-service-accessibility",
            "green-infrastructure",
            "waste-management",
            "grading",
            "disaster-management",
            "first-phase-land-management",
            "integrated-engineering-networks",
            "water-and-sewer",
            "heating-supply",
            "power-supply",
            "communications-and-signaling",
        ];

        Assert.All(sequence.Drawings.Where(item => mapDrawingIds.Contains(item.Id)), item =>
        {
            Assert.True(item.UsesNomenclatureGrid);
            Assert.True(item.AllowMultiplePages);
        });
        Assert.All(sequence.Drawings.Where(item => !mapDrawingIds.Contains(item.Id)), item =>
            Assert.False(item.UsesNomenclatureGrid));
        Assert.True(sequence.Drawings.Single(item => item.Id == "development-projection").AllowMultiplePages);
    }

    [Theory]
    [InlineData("ET 09", "", "", "general-plan-zoning")]
    [InlineData("IDB-02", "", "", "water-and-sewer")]
    [InlineData("", "pedestrian-movement", "", "pedestrian-movement")]
    [InlineData("", "", "Явган хүний замын хөдөлгөөний схем", "pedestrian-movement")]
    [InlineData("IDB-05", "engineering-preparation", "", "grading")]
    public void PartialPlanTemplate_FindsSourceSlotFromNormalizedProducerMetadata(
        string number,
        string contentKind,
        string name,
        string expectedSlotId)
    {
        AlbumDefinition definition = UrbanPlanningAlbumTemplate.CreateDefinition(
            PartialMasterPlanDrawingSequence.StageType);

        AlbumCompositionItem? slot = UrbanPlanningAlbumTemplate.FindSourceSlot(
            definition,
            number,
            contentKind,
            name);

        Assert.NotNull(slot);
        Assert.Equal(expectedSlotId, slot.Id);
    }

    [Fact]
    public void MasterPlanTemplate_KeepsGradingAndEngineeringPreparationDistinct()
    {
        AlbumDefinition definition = UrbanPlanningAlbumTemplate.CreateDefinition(
            MasterPlanDrawingSequence.StageType);

        AlbumCompositionItem? slot = UrbanPlanningAlbumTemplate.FindSourceSlot(
            definition,
            "",
            "engineering-preparation",
            "");

        Assert.NotNull(slot);
        Assert.Equal("engineering-preparation", slot.Id);

        var page = new AlbumPageDefinition { TemplateSlotId = "engineering-preparation" };
        UrbanPlanningAlbumTemplate.MigrateLegacyPages(definition, [page]);

        Assert.Equal("engineering-preparation", page.TemplateSlotId);
    }

    [Fact]
    public void Resolver_MigratesPartialPlanV1WithoutLosingSourcePages()
    {
        var workspace = new ProjectWorkspace();
        workspace.Identity.ProjectType = ProjectTypes.UrbanPlanningProjectType.TypeId;
        workspace.Identity.StageCode = PartialMasterPlanDrawingSequence.StageType;
        workspace.Deliverables.Albums.Add(new ProjectAlbumRecord { IsPrimary = true });
        var legacyEngineeringPreparation = new AlbumPageDefinition
        {
            SheetKey = "source-1|engineering-preparation",
            TemplateSlotId = "engineering-preparation",
        };
        var legacyWastePlan = new AlbumPageDefinition
        {
            SheetKey = "source-1|waste-management",
            TemplateSlotId = "waste-management",
        };
        var album = new StudioAlbumDocument
        {
            Definition = new AlbumDefinition
            {
                Title = "Хэсэгчилсэн ерөнхий төлөвлөгөө (ХЕТ)",
                TemplateId = UrbanPlanningAlbumTemplate.LegacyPartialPlanTemplateId,
                Pages = [legacyEngineeringPreparation, legacyWastePlan],
            },
        };

        bool changed = ProjectAlbumTemplateResolver.Apply(workspace, album);

        Assert.True(changed);
        Assert.Equal(UrbanPlanningAlbumTemplate.PartialPlanTemplateId, album.Definition.TemplateId);
        Assert.Same(legacyEngineeringPreparation, album.Definition.Pages[0]);
        Assert.Same(legacyWastePlan, album.Definition.Pages[1]);
        Assert.Equal("grading", legacyEngineeringPreparation.TemplateSlotId);
        Assert.Equal("waste-management", legacyWastePlan.TemplateSlotId);
        Assert.All(album.Definition.Pages, page => Assert.NotNull(page.SectionId));
    }
}
