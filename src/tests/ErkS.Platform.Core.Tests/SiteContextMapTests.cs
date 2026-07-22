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
    public void Normalize_KeepsCoverageZoomSeparateFromCaptureDetailZoom()
    {
        var legacy = new ProjectMapViewport { Zoom = 12, DetailZoom = 0 };
        var detailed = new ProjectMapViewport { Zoom = 12, DetailZoom = 14 };
        var invalidLowerDetail = new ProjectMapViewport { Zoom = 16, DetailZoom = 10 };

        legacy.Normalize(ProjectMapViewportKinds.LocationScheme);
        detailed.Normalize(ProjectMapViewportKinds.LocationScheme);
        invalidLowerDetail.Normalize(ProjectMapViewportKinds.LocationScheme);

        Assert.Equal(12, legacy.Zoom);
        Assert.Equal(12, legacy.DetailZoom);
        Assert.Equal(12, detailed.Zoom);
        Assert.Equal(14, detailed.DetailZoom);
        Assert.Equal(16, invalidLowerDetail.Zoom);
        Assert.Equal(16, invalidLowerDetail.DetailZoom);
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
                    DetailZoom = 17,
                    SnapshotRelativePath = "assets/site-context/location-scheme.png",
                },
                SurroundingsOverview = new ProjectMapViewport
                {
                    Kind = ProjectMapViewportKinds.SurroundingsOverview,
                    ProviderId = ProjectMapProviderIds.GoogleSatellite,
                    CenterLatitude = 47.90,
                    CenterLongitude = 106.90,
                    Zoom = 12,
                    DetailZoom = 14,
                    SnapshotRelativePath = "assets/site-context/surroundings-overview.png",
                },
                PlanFeatures = new ProjectSitePlanFeatures
                {
                    SourceId = "citygen-source",
                    SourceManifestSha256 = new string('a', 64),
                    Roads =
                    [
                        new ProjectSiteRoadOverlay
                        {
                            Id = "road-1",
                            Name = "Main road",
                            Path =
                            [
                                new ProjectGeoCoordinate { Longitude = 106.90, Latitude = 47.90 },
                                new ProjectGeoCoordinate { Longitude = 106.91, Latitude = 47.91 },
                            ],
                        },
                    ],
                    Buildings =
                    [
                        new ProjectSiteBuildingOverlay
                        {
                            Id = "building-1",
                            Number = "B-01",
                            Name = "Office",
                            Ring =
                            [
                                new ProjectGeoCoordinate { Longitude = 106.90, Latitude = 47.90 },
                                new ProjectGeoCoordinate { Longitude = 106.91, Latitude = 47.90 },
                                new ProjectGeoCoordinate { Longitude = 106.91, Latitude = 47.91 },
                                new ProjectGeoCoordinate { Longitude = 106.90, Latitude = 47.90 },
                            ],
                        },
                    ],
                },
            },
        };

        AlbumProjectStore.Save(project, path);
        AlbumProject loaded = AlbumProjectStore.Load(path);

        Assert.Equal(ProjectMapProviderIds.OpenTopoMap, loaded.SiteContext.LocationScheme.ProviderId);
        Assert.Equal(16, loaded.SiteContext.LocationScheme.Zoom);
        Assert.Equal(17, loaded.SiteContext.LocationScheme.DetailZoom);
        Assert.Equal(ProjectMapProviderIds.GoogleSatellite, loaded.SiteContext.SurroundingsOverview.ProviderId);
        Assert.Equal(12, loaded.SiteContext.SurroundingsOverview.Zoom);
        Assert.Equal(14, loaded.SiteContext.SurroundingsOverview.DetailZoom);
        Assert.NotEqual(
            loaded.SiteContext.LocationScheme.SnapshotRelativePath,
            loaded.SiteContext.SurroundingsOverview.SnapshotRelativePath);
        Assert.Equal("Main road", Assert.Single(loaded.SiteContext.PlanFeatures.Roads).Name);
        Assert.Equal("B-01", Assert.Single(loaded.SiteContext.PlanFeatures.Buildings).Number);
    }

    [Fact]
    public void WorkspaceRecovery_RestoresOrphanedSiteContextSnapshots()
    {
        string projectFolder = Path.Combine(workDirectory, "map-project");
        string projectPath = Path.Combine(projectFolder, ProjectWorkspace.DefaultFileName);
        string assetFolder = Path.Combine(projectFolder, "assets", "site-context");
        Directory.CreateDirectory(assetFolder);
        byte[] onePixelPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
        File.WriteAllBytes(Path.Combine(assetFolder, "location-scheme.png"), onePixelPng);
        File.WriteAllBytes(Path.Combine(assetFolder, "surroundings-overview.png"), onePixelPng);

        ProjectWorkspace project = ProjectWorkspaceStore.Create("MAP-001", "Map project");
        ProjectWorkspaceStore.Save(project, projectPath);

        bool recovered = ProjectWorkspaceStore.RecoverSiteContextSnapshots(project, projectPath);
        ProjectWorkspaceStore.Save(project, projectPath);
        ProjectWorkspace loaded = ProjectWorkspaceStore.Load(projectPath);

        Assert.True(recovered);
        Assert.Equal(
            "assets/site-context/location-scheme.png",
            loaded.SiteContext.LocationScheme.SnapshotRelativePath);
        Assert.Equal(
            "assets/site-context/surroundings-overview.png",
            loaded.SiteContext.SurroundingsOverview.SnapshotRelativePath);
        Assert.Equal(1, loaded.SiteContext.LocationScheme.SnapshotPixelWidth);
        Assert.Equal(1, loaded.SiteContext.LocationScheme.SnapshotPixelHeight);
        Assert.NotEmpty(loaded.SiteContext.LocationScheme.SnapshotSha256);
        Assert.NotNull(loaded.SiteContext.UpdatedAtUtc);
    }

    [Fact]
    public void CityGenProjectSiteReconciler_ImportsOnlyLinkedSidecarAndSkipsUnchangedContent()
    {
        string drawingPath = Path.Combine(workDirectory, "general-plan.dwg");
        string sidecarPath = Path.Combine(workDirectory, "general-plan.erks-citygen-site.json");
        File.WriteAllText(drawingPath, "test drawing placeholder");
        WriteCityGenSidecar(sidecarPath, areaSquareMeters: 12_345, eastLongitude: 106.92);

        ProjectWorkspace project = ProjectWorkspaceStore.Create("MAP-002", "CityGen map project");
        project.Sources.Add(new ProjectDesignSource
        {
            Id = "citygen-source",
            Kind = DesignSourceKind.CityGen,
            Name = "CityGen - General plan",
            NativeDocumentTitle = "general-plan.dwg",
            NativeDocumentPath = drawingPath,
        });

        CityGenProjectSiteReconciliationResult imported =
            CityGenProjectSiteReconciler.Reconcile(project);
        CityGenProjectSiteReconciliationResult unchanged =
            CityGenProjectSiteReconciler.Reconcile(project);

        Assert.True(imported.Changed);
        Assert.True(imported.Imported);
        Assert.Equal("citygen-source", imported.SourceId);
        Assert.Equal("citygen-source", project.SiteContext.Boundary.SourceId);
        Assert.Equal("general-plan.dwg", project.SiteContext.Boundary.SourceDocumentName);
        Assert.Equal(32648, project.SiteContext.Boundary.SourceEpsg);
        Assert.Equal(12_345, project.SiteContext.Boundary.AreaSquareMeters);
        Assert.True(project.SiteContext.Boundary.HasGeometry);
        Assert.Empty(project.SiteContext.PlanFeatures.Roads);
        Assert.Empty(project.SiteContext.PlanFeatures.Buildings);
        Assert.Equal("citygen-source", project.SiteContext.PlanFeatures.SourceId);
        Assert.Equal(
            project.SiteContext.Boundary.SourceManifestSha256,
            project.SiteContext.PlanFeatures.SourceManifestSha256);
        Assert.InRange(project.SiteContext.LocationScheme.CenterLongitude, 106.91, 106.93);
        Assert.False(unchanged.Changed);
        Assert.False(unchanged.Imported);

        ProjectWorkspace unrelated = ProjectWorkspaceStore.Create("MAP-003", "Unrelated project");
        CityGenProjectSiteReconciliationResult unrelatedResult =
            CityGenProjectSiteReconciler.Reconcile(unrelated);

        Assert.False(unrelatedResult.Changed);
        Assert.False(unrelated.SiteContext.Boundary.HasGeometry);
        Assert.False(unrelated.SiteContext.PlanFeatures.HasGeometry);
    }

    [Fact]
    public void CityGenProjectSiteReconciler_ImportsAndReplacesLinkedPlanFeatures()
    {
        string drawingPath = Path.Combine(workDirectory, "planned-site.dwg");
        string sidecarPath = Path.Combine(workDirectory, "planned-site.erks-citygen-site.json");
        File.WriteAllText(drawingPath, "test drawing placeholder");
        WriteCityGenSidecar(
            sidecarPath,
            areaSquareMeters: 20_000,
            eastLongitude: 106.94,
            includePlanFeatures: true);

        ProjectWorkspace project = ProjectWorkspaceStore.Create("MAP-PLAN", "CityGen plan overlay");
        project.Sources.Add(new ProjectDesignSource
        {
            Id = "planned-citygen-source",
            Kind = DesignSourceKind.CityGen,
            NativeDocumentPath = drawingPath,
        });

        CityGenProjectSiteReconciliationResult imported =
            CityGenProjectSiteReconciler.Reconcile(project);

        Assert.True(imported.Changed);
        ProjectSiteRoadOverlay road = Assert.Single(project.SiteContext.PlanFeatures.Roads);
        Assert.Equal("ROAD-01", road.Id);
        Assert.Equal("Main axis", road.Name);
        Assert.Equal(3, road.Path.Count);
        ProjectSiteBuildingOverlay building =
            Assert.Single(project.SiteContext.PlanFeatures.Buildings);
        Assert.Equal("BUILDING-01", building.Id);
        Assert.Equal("B-01", building.Number);
        Assert.Equal("Office", building.Name);
        Assert.True(building.HasGeometry);
        Assert.Equal(
            project.SiteContext.Boundary.SourceManifestSha256,
            project.SiteContext.PlanFeatures.SourceManifestSha256);

        WriteCityGenSidecar(
            sidecarPath,
            areaSquareMeters: 20_001,
            eastLongitude: 106.94,
            includePlanFeatures: false);
        CityGenProjectSiteReconciliationResult removed =
            CityGenProjectSiteReconciler.Reconcile(project);

        Assert.True(removed.Changed);
        Assert.Empty(project.SiteContext.PlanFeatures.Roads);
        Assert.Empty(project.SiteContext.PlanFeatures.Buildings);
        Assert.Equal("planned-citygen-source", project.SiteContext.PlanFeatures.SourceId);
    }

    [Fact]
    public void CityGenProjectSiteReconciler_ReimportsChangedSidecarByContentHash()
    {
        string drawingPath = Path.Combine(workDirectory, "updated-plan.dwg");
        string sidecarPath = Path.Combine(workDirectory, "updated-plan.erks-citygen-site.json");
        File.WriteAllText(drawingPath, "test drawing placeholder");
        WriteCityGenSidecar(sidecarPath, areaSquareMeters: 10_000, eastLongitude: 106.90);

        ProjectWorkspace project = ProjectWorkspaceStore.Create("MAP-004", "Updated CityGen project");
        project.Sources.Add(new ProjectDesignSource
        {
            Id = "updated-citygen-source",
            Kind = DesignSourceKind.AutoCad,
            NativeDocumentPath = drawingPath,
        });

        CityGenProjectSiteReconciler.Reconcile(project);
        string originalHash = project.SiteContext.Boundary.SourceManifestSha256;
        WriteCityGenSidecar(sidecarPath, areaSquareMeters: 11_500, eastLongitude: 107.00);

        CityGenProjectSiteReconciliationResult updated =
            CityGenProjectSiteReconciler.Reconcile(project);

        Assert.True(updated.Changed);
        Assert.True(updated.Imported);
        Assert.Equal(11_500, project.SiteContext.Boundary.AreaSquareMeters);
        Assert.NotEqual(originalHash, project.SiteContext.Boundary.SourceManifestSha256);
        Assert.InRange(project.SiteContext.LocationScheme.CenterLongitude, 106.99, 107.01);
    }

    [Fact]
    public void CityGenProjectSiteReconciler_PreservesStudioMapCompositionWhenGeometryChanges()
    {
        string drawingPath = Path.Combine(workDirectory, "persistent-map.dwg");
        string sidecarPath = Path.Combine(workDirectory, "persistent-map.erks-citygen-site.json");
        File.WriteAllText(drawingPath, "test drawing placeholder");
        WriteCityGenSidecar(sidecarPath, areaSquareMeters: 10_000, eastLongitude: 106.90);

        ProjectWorkspace project = ProjectWorkspaceStore.Create("MAP-PERSIST", "Persistent map composition");
        project.Sources.Add(new ProjectDesignSource
        {
            Id = "persistent-citygen-source",
            Kind = DesignSourceKind.CityGen,
            NativeDocumentPath = drawingPath,
        });
        CityGenProjectSiteReconciler.Reconcile(project);

        DateTimeOffset savedAt = DateTimeOffset.Parse("2026-07-22T13:00:00Z");
        project.SiteContext.LocationScheme = new ProjectMapViewport
        {
            Kind = ProjectMapViewportKinds.LocationScheme,
            ProviderId = ProjectMapProviderIds.OpenTopoMap,
            CenterLatitude = 47.92,
            CenterLongitude = 106.93,
            Zoom = 14,
            DetailZoom = 16,
            Bearing = 7,
            SnapshotRelativePath = "assets/site-context/location-scheme.png",
            SnapshotSha256 = "location-hash",
            SnapshotPixelWidth = 2400,
            SnapshotPixelHeight = 1600,
            Attribution = "Saved location map",
            UpdatedAtUtc = savedAt,
        };
        project.SiteContext.SurroundingsOverview = new ProjectMapViewport
        {
            Kind = ProjectMapViewportKinds.SurroundingsOverview,
            ProviderId = ProjectMapProviderIds.OpenStreetMap,
            CenterLatitude = 47.91,
            CenterLongitude = 106.92,
            Zoom = 11,
            DetailZoom = 12,
            SnapshotRelativePath = "assets/site-context/surroundings-overview.png",
            SnapshotSha256 = "overview-hash",
            SnapshotPixelWidth = 2400,
            SnapshotPixelHeight = 1600,
            Attribution = "Saved surroundings map",
            UpdatedAtUtc = savedAt,
        };

        ProjectMapViewport savedLocation = project.SiteContext.LocationScheme.Clone();
        ProjectMapViewport savedOverview = project.SiteContext.SurroundingsOverview.Clone();
        WriteCityGenSidecar(
            sidecarPath,
            areaSquareMeters: 11_500,
            eastLongitude: 107.00,
            includePlanFeatures: true);

        CityGenProjectSiteReconciliationResult updated =
            CityGenProjectSiteReconciler.Reconcile(project);

        Assert.True(updated.Changed);
        Assert.Equal(11_500, project.SiteContext.Boundary.AreaSquareMeters);
        Assert.Single(project.SiteContext.PlanFeatures.Roads);
        Assert.Single(project.SiteContext.PlanFeatures.Buildings);
        AssertViewportCompositionEqual(savedLocation, project.SiteContext.LocationScheme);
        AssertViewportCompositionEqual(savedOverview, project.SiteContext.SurroundingsOverview);
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

    private static void WriteCityGenSidecar(
        string path,
        double areaSquareMeters,
        double eastLongitude,
        bool includePlanFeatures = false)
    {
        string planFeatures = includePlanFeatures
            ? $$"""
              ,
              "planFeatures": {
                "roads": [
                  {
                    "id": "ROAD-01",
                    "name": "Main axis",
                    "geometry": {
                      "type": "LineString",
                      "coordinates": [
                        [{{eastLongitude - 0.009}}, 47.901],
                        [{{eastLongitude - 0.005}}, 47.905],
                        [{{eastLongitude - 0.001}}, 47.909]
                      ]
                    }
                  }
                ],
                "buildings": [
                  {
                    "id": "BUILDING-01",
                    "number": "B-01",
                    "name": "Office",
                    "buildingType": "Office",
                    "geometry": {
                      "type": "Polygon",
                      "coordinates": [[
                        [{{eastLongitude - 0.008}}, 47.902],
                        [{{eastLongitude - 0.006}}, 47.902],
                        [{{eastLongitude - 0.006}}, 47.904],
                        [{{eastLongitude - 0.008}}, 47.904],
                        [{{eastLongitude - 0.008}}, 47.902]
                      ]]
                    }
                  }
                ]
              }
              """
            : "";
        string json = $$"""
        {
          "schema": "erks.citygen.project-site",
          "schemaVersion": 1,
          "sourceDocument": {
            "name": "general-plan.dwg",
            "polylineHandle": "A12",
            "layerName": "PROJECT-LAND"
          },
          "sourceCrs": {
            "authority": "EPSG",
            "epsg": 32648,
            "name": "UTM84-48N",
            "coordinateOrder": "Easting,Northing",
            "unit": "metre"
          },
          "geometry": {
            "type": "Polygon",
            "coordinates": [[
              [{{eastLongitude - 0.01}}, 47.90],
              [{{eastLongitude}}, 47.90],
              [{{eastLongitude}}, 47.91],
              [{{eastLongitude - 0.01}}, 47.91],
              [{{eastLongitude - 0.01}}, 47.90]
            ]]
          },
          "areaSquareMeters": {{areaSquareMeters}},
          "coordinateMode": "direct-utm",
          "updatedAtUtc": "2026-07-22T12:00:00Z"{{planFeatures}}
        }
        """;
        File.WriteAllText(path, json);
    }

    private static void AssertViewportCompositionEqual(
        ProjectMapViewport expected,
        ProjectMapViewport actual)
    {
        Assert.Equal(expected.ProviderId, actual.ProviderId);
        Assert.Equal(expected.CenterLatitude, actual.CenterLatitude);
        Assert.Equal(expected.CenterLongitude, actual.CenterLongitude);
        Assert.Equal(expected.Zoom, actual.Zoom);
        Assert.Equal(expected.DetailZoom, actual.DetailZoom);
        Assert.Equal(expected.Bearing, actual.Bearing);
        Assert.Equal(expected.SnapshotRelativePath, actual.SnapshotRelativePath);
        Assert.Equal(expected.SnapshotSha256, actual.SnapshotSha256);
        Assert.Equal(expected.SnapshotPixelWidth, actual.SnapshotPixelWidth);
        Assert.Equal(expected.SnapshotPixelHeight, actual.SnapshotPixelHeight);
        Assert.Equal(expected.Attribution, actual.Attribution);
        Assert.Equal(expected.UpdatedAtUtc, actual.UpdatedAtUtc);
    }
}
