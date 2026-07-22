using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core.Tests;

public sealed class SiteContextMapTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-site-context-" + Guid.NewGuid().ToString("N"));

    public SiteContextMapTests()
    {
        Directory.CreateDirectory(workDirectory);
    }

    [Fact]
    public void Normalize_ClampsExtentAndKeepsProjectOwnership()
    {
        var source = new ProjectSiteContextMap
        {
            LocationScheme = new ProjectMapViewport
            {
                ProviderId = "unknown-provider",
                CenterLatitude = 1000,
                CenterLongitude = -1000,
                Zoom = 99,
            },
        };

        source.Normalize("project-1");

        Assert.Equal("project-1", source.OwnerProjectId);
        Assert.Equal(ProjectMapProviderIds.OpenStreetMap, source.LocationScheme.ProviderId);
        Assert.Equal(85, source.LocationScheme.CenterLatitude);
        Assert.Equal(-180, source.LocationScheme.CenterLongitude);
        Assert.Equal(22, source.LocationScheme.Zoom);
        Assert.Equal(ProjectMapViewportKinds.SurroundingsOverview, source.SurroundingsOverview.Kind);
    }

    [Fact]
    public void TemplateEnsure_MigratesLegacySiteContextToStudioGeneratedPage()
    {
        AlbumDefinition definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Album");
        AlbumCompositionItem siteContext = Assert.Single(
            definition.Composition,
            item => item.Id == "site-context");
        siteContext.Kind = AlbumCompositionKind.SourceSlot;
        siteContext.GeneratedPageKind = AlbumGeneratedPageKind.None;
        siteContext.Title = "ОРЧНЫ ТОЙМ";
        siteContext.MatchNameTerms = ["ОРЧНЫ ТОЙМ"];

        bool changed = BuildingArchitectureConceptAlbumTemplate.Ensure(definition);

        Assert.True(changed);
        Assert.Equal(AlbumCompositionKind.Generated, siteContext.Kind);
        Assert.Equal(AlbumGeneratedPageKind.SiteContext, siteContext.GeneratedPageKind);
        Assert.Equal("БАЙРШЛЫН СХЕМ / ОРЧНЫ ТОЙМ", siteContext.Title);
        Assert.Empty(siteContext.MatchNameTerms);
    }

    [Fact]
    public void Sequencer_DoesNotRenderLegacySourcePageForGeneratedSiteContextSlot()
    {
        AlbumDefinition definition = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Album");
        var pages = new[]
        {
            new AlbumPageDefinition
            {
                SheetKey = "legacy-source|site-context",
                TemplateSlotId = "site-context",
            },
        };

        IReadOnlyList<ConceptAlbumSourcePage> sequence =
            BuildingArchitectureConceptAlbumSequencer.Create(
                definition,
                pages,
                new SheetLibrary(),
                []);

        Assert.Empty(sequence);
    }

    [Fact]
    public void AlbumStore_RoundTripsBothIndependentMapExtents()
    {
        string path = Path.Combine(workDirectory, "map.erksalbum");
        var project = new AlbumProject
        {
            ProjectId = "project-map",
            SiteContext = new ProjectSiteContextMap
            {
                OwnerProjectId = "project-map",
                LocationScheme = new ProjectMapViewport
                {
                    Kind = ProjectMapViewportKinds.LocationScheme,
                    ProviderId = ProjectMapProviderIds.OpenTopoMap,
                    CenterLatitude = 47.91,
                    CenterLongitude = 106.91,
                    Zoom = 16,
                    SnapshotRelativePath = "assets/site-context/location-scheme.png",
                },
                SurroundingsOverview = new ProjectMapViewport
                {
                    Kind = ProjectMapViewportKinds.SurroundingsOverview,
                    ProviderId = ProjectMapProviderIds.GoogleSatellite,
                    CenterLatitude = 47.90,
                    CenterLongitude = 106.90,
                    Zoom = 12,
                    SnapshotRelativePath = "assets/site-context/surroundings-overview.png",
                },
            },
        };

        AlbumProjectStore.Save(project, path);
        AlbumProject loaded = AlbumProjectStore.Load(path);

        Assert.Equal(ProjectMapProviderIds.OpenTopoMap, loaded.SiteContext.LocationScheme.ProviderId);
        Assert.Equal(16, loaded.SiteContext.LocationScheme.Zoom);
        Assert.Equal(ProjectMapProviderIds.GoogleSatellite, loaded.SiteContext.SurroundingsOverview.ProviderId);
        Assert.Equal(12, loaded.SiteContext.SurroundingsOverview.Zoom);
        Assert.NotEqual(
            loaded.SiteContext.LocationScheme.SnapshotRelativePath,
            loaded.SiteContext.SurroundingsOverview.SnapshotRelativePath);
    }

    [Fact]
    public void PdfBuild_IncludesStudioGeneratedSiteContextPageWithoutSnapshots()
    {
        string outputPath = Path.Combine(workDirectory, "site-context.pdf");
        var project = new AlbumProject
        {
            ProjectId = "project-map",
            Name = "Map project",
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Map album"),
            SiteContext = new ProjectSiteContextMap { OwnerProjectId = "project-map" },
            ProjectFolder = workDirectory,
        };

        AlbumBuildResult result = new AlbumBuilder(new PdfSharpAlbumWriter())
            .Build(project, new SheetLibrary(), outputPath);

        Assert.Equal(5, result.PageCount);
        Assert.Contains(result.Components, component =>
            component.Code == ProjectCloudSyncMetadata.SiteContextComponentCode);
        using var document = PdfReader.Open(outputPath, PdfDocumentOpenMode.Import);
        Assert.Equal(5, document.PageCount);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }
}
