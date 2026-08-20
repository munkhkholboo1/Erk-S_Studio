using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectDesignSourceClassificationTests
{
    [Fact]
    public void RevitSourcesDefaultToBuildingPurpose()
    {
        Assert.Equal(
            ProjectDesignSourcePurpose.Building,
            ProjectDesignSourceClassification.DefaultPurpose(DesignSourceKind.Revit));
    }

    [Fact]
    public void AutoCadSourcesHaveNoAssumedPurpose()
    {
        // AutoCAD delivers both a building.s sheets and the project.s general plan, so the
        // package.s own content decides. Assuming a building put every general-plan DWG into
        // a building group.
        Assert.Equal(
            ProjectDesignSourcePurpose.Unspecified,
            ProjectDesignSourceClassification.DefaultPurpose(DesignSourceKind.AutoCad));
    }

    [Fact]
    public void PackageBuildingIdentityMapsAutoCadSheetsToCanonicalStudioGroup()
    {
        var source = new ProjectDesignSource
        {
            Id = "autocad-source",
            Kind = DesignSourceKind.AutoCad,
        };
        var project = new ProjectWorkspace
        {
            Sources = [source],
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "school",
                    Name = "Сургууль",
                    Order = 3,
                },
            ],
        };
        var packageSource = new SheetPackageSource
        {
            SourceId = source.Id,
            Application = SheetSourceApplication.AutoCad,
        };
        var entry = new SheetPackageEntry
        {
            SheetId = "layout-1",
            BuildingId = "school",
            BuildingName = "Сургууль",
        };

        bool changed =
            ProjectDesignSourceClassification.ApplyPackageBuildingGroupAssignments(
                project,
                source,
                packageSource,
                [entry]);

        Assert.True(changed);
        Assert.Equal(
            "school",
            project.SheetBuildingAssignments[
                SheetRecord.MakeKey(packageSource, entry, source.Id)]);
    }

    [Fact]
    public void PackageMetadata_DetectsGeneralPlanWithoutOverridingExplicitPurpose()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("SRC-001", "Source classification");
        var source = new ProjectDesignSource
        {
            Id = "autocad-general-plan",
            Kind = DesignSourceKind.AutoCad,
        };
        project.Sources.Add(source);
        var manifest = new SheetPackageManifest
        {
            PackageScope = SheetPackageScope.FullSnapshot,
            Source = new SheetPackageSource
            {
                SourceId = source.Id,
                Application = SheetSourceApplication.AutoCad,
            },
            Sheets =
            [
                new SheetPackageEntry
                {
                    SheetId = "GP-01",
                    ContentKind = "Ерөнхий төлөвлөгөө",
                },
            ],
        };

        ProjectCloudSyncMetadata.RecordPackage(project, source, manifest, "abc123");

        Assert.Equal(
            ProjectDesignSourcePurpose.GeneralPlan,
            ProjectDesignSourceClassification.DetectedPurpose(source));
        Assert.Equal(
            ProjectDesignSourcePurpose.GeneralPlan,
            ProjectDesignSourceClassification.EffectivePurpose(source));

        ProjectDesignSourceClassification.SetExplicitPurpose(
            source,
            ProjectDesignSourcePurpose.Building,
            "building-a");

        Assert.Equal(
            ProjectDesignSourcePurpose.Building,
            ProjectDesignSourceClassification.EffectivePurpose(source));
    }

    [Fact]
    public void FullSnapshot_DetectsBuildingAndDeltaDoesNotDowngradeGeneralPlan()
    {
        var buildingSource = new ProjectDesignSource { Kind = DesignSourceKind.Revit };
        ProjectDesignSourceClassification.RecordDetectedPurpose(
            buildingSource,
            new SheetPackageManifest
            {
                PackageScope = SheetPackageScope.FullSnapshot,
                Source = new SheetPackageSource { Application = SheetSourceApplication.Revit },
                Sheets = [new SheetPackageEntry { SheetId = "A-01", ContentKind = "Давхрын байгуулалт" }],
            });

        Assert.Equal(
            ProjectDesignSourcePurpose.Building,
            ProjectDesignSourceClassification.DetectedPurpose(buildingSource));

        var generalPlanSource = new ProjectDesignSource { Kind = DesignSourceKind.AutoCad };
        ProjectDesignSourceClassification.RecordDetectedPurpose(
            generalPlanSource,
            new SheetPackageManifest
            {
                PackageScope = SheetPackageScope.FullSnapshot,
                Source = new SheetPackageSource { Application = SheetSourceApplication.AutoCad },
                Sheets = [new SheetPackageEntry { SheetId = "GP-01", ContentKind = "General plan" }],
            });
        ProjectDesignSourceClassification.RecordDetectedPurpose(
            generalPlanSource,
            new SheetPackageManifest
            {
                PackageScope = SheetPackageScope.Delta,
                Source = new SheetPackageSource { Application = SheetSourceApplication.AutoCad },
                Sheets = [new SheetPackageEntry { SheetId = "A-01", ContentKind = "Огтлол" }],
            });

        Assert.Equal(
            ProjectDesignSourcePurpose.GeneralPlan,
            ProjectDesignSourceClassification.DetectedPurpose(generalPlanSource));
    }

    [Fact]
    public void BuildingSource_DefaultAssignmentPreservesManualSheetAssignment()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("SRC-002", "Building assignment");
        project.BuildingGroups =
        [
            new ProjectBuildingGroup { Id = "building-a", Name = "A блок", Order = 1 },
            new ProjectBuildingGroup { Id = "building-b", Name = "B блок", Order = 2 },
        ];
        project.SheetBuildingAssignments["sheet-manual"] = "building-b";
        var source = new ProjectDesignSource { Kind = DesignSourceKind.AutoCad };
        ProjectDesignSourceClassification.SetExplicitPurpose(
            source,
            ProjectDesignSourcePurpose.Building,
            "building-a");

        bool changed =
            ProjectDesignSourceClassification.ApplyDefaultBuildingGroupAssignments(
                project,
                source,
                ["sheet-auto", "sheet-manual"]);

        Assert.True(changed);
        Assert.Equal("building-a", project.SheetBuildingAssignments["sheet-auto"]);
        Assert.Equal("building-b", project.SheetBuildingAssignments["sheet-manual"]);
    }

    [Fact]
    public void GeneralPlanOwner_CanEditSiteContextBeforeBoundaryExists()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = AddOwnedSource(
            project,
            "general-plan-source",
            ProjectDesignSourcePurpose.GeneralPlan);

        ProjectSiteContextEditAuthority owner =
            ProjectSiteContextEditingPolicy.Resolve(project, "planner@erks.local");
        ProjectSiteContextEditAuthority collaborator =
            ProjectSiteContextEditingPolicy.Resolve(project, "architect@erks.local");

        Assert.True(owner.CanEdit);
        Assert.Equal(source.Id, owner.SourceId);
        Assert.False(collaborator.CanEdit);
    }

    [Fact]
    public void BuildingSource_DoesNotGrantSiteContextEditPermission()
    {
        ProjectWorkspace project = CloudProject();
        _ = AddOwnedSource(
            project,
            "building-source",
            ProjectDesignSourcePurpose.Building);

        ProjectSiteContextEditAuthority authority =
            ProjectSiteContextEditingPolicy.Resolve(project, "planner@erks.local");

        Assert.False(authority.CanEdit);
        Assert.Contains("Ерөнхий төлөвлөгөө", authority.Message);
    }

    [Fact]
    public void PackageDetectedGeneralPlan_GrantsOwnerPermissionBeforeBoundaryExists()
    {
        ProjectWorkspace project = CloudProject();
        ProjectDesignSource source = AddOwnedSource(
            project,
            "detected-general-plan",
            ProjectDesignSourcePurpose.Unspecified);
        ProjectDesignSourceClassification.RecordDetectedPurpose(
            source,
            new SheetPackageManifest
            {
                PackageScope = SheetPackageScope.FullSnapshot,
                Source = new SheetPackageSource { Application = SheetSourceApplication.AutoCad },
                Sheets =
                [
                    new SheetPackageEntry
                    {
                        SheetId = "GP-01",
                        ContentKind = "Ерөнхий төлөвлөгөө",
                    },
                ],
            });

        ProjectSiteContextEditAuthority authority =
            ProjectSiteContextEditingPolicy.Resolve(project, "planner@erks.local");

        Assert.True(authority.CanEdit);
        Assert.Equal(source.Id, authority.SourceId);
    }

    private static ProjectWorkspace CloudProject()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("SRC-CLOUD", "Cloud source");
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "cloud-project";
        return project;
    }

    private static ProjectDesignSource AddOwnedSource(
        ProjectWorkspace project,
        string sourceId,
        ProjectDesignSourcePurpose purpose)
    {
        var source = new ProjectDesignSource
        {
            Id = sourceId,
            Kind = DesignSourceKind.AutoCad,
        };
        ProjectDesignSourceClassification.SetExplicitPurpose(
            source,
            purpose,
            purpose == ProjectDesignSourcePurpose.Building ? "building-a" : "");
        ProjectCloudSyncMetadata.BindCloudOwner(source, "planner@erks.local");
        project.Sources.Add(source);
        return source;
    }

    [Theory]
    [InlineData("ЕТ")]
    [InlineData("ет")]
    public void AutoCadGeneralPlanDrawingMarkIsDetectedAsGeneralPlan(string discipline)
    {
        // AutoCAD sends the drawing mark as the discipline, and the general-plan album marks
        // its general-plan sheets ЕТ, which spells out neither phrase detection looked for.
        var source = new ProjectDesignSource
        {
            Id = "autocad-general-plan",
            Kind = DesignSourceKind.AutoCad,
        };

        ProjectDesignSourceClassification.RecordDetectedPurpose(
            source,
            GeneralPlanManifest(discipline: discipline, contentKind: "traffic-scheme"));

        Assert.Equal(
            ProjectDesignSourcePurpose.GeneralPlan,
            ProjectDesignSourceClassification.EffectivePurpose(source));
    }

    [Fact]
    public void HyphenatedGeneralPlanSlotIdIsDetectedAsGeneralPlan()
    {
        // Content kinds arrive as template slot ids, hyphenated.
        var source = new ProjectDesignSource
        {
            Id = "autocad-zoning",
            Kind = DesignSourceKind.AutoCad,
        };

        ProjectDesignSourceClassification.RecordDetectedPurpose(
            source,
            GeneralPlanManifest(discipline: "AR", contentKind: "general-plan-zoning"));

        Assert.Equal(
            ProjectDesignSourcePurpose.GeneralPlan,
            ProjectDesignSourceClassification.EffectivePurpose(source));
    }

    [Fact]
    public void OrdinaryBuildingSheetsStayABuilding()
    {
        var source = new ProjectDesignSource
        {
            Id = "autocad-building",
            Kind = DesignSourceKind.AutoCad,
        };

        ProjectDesignSourceClassification.RecordDetectedPurpose(
            source,
            GeneralPlanManifest(discipline: "AR", contentKind: "floor-plan"));

        Assert.Equal(
            ProjectDesignSourcePurpose.Building,
            ProjectDesignSourceClassification.EffectivePurpose(source));
    }


    [Fact]
    public void PackageNamingAnUnknownBuilding_CreatesThatBuildingGroup()
    {
        // AutoCAD delivers a building this project has never listed. Before, the sheets were
        // left in no building at all because only an existing group could be matched.
        var source = new ProjectDesignSource
        {
            Id = "autocad-source",
            Kind = DesignSourceKind.AutoCad,
        };
        var project = new ProjectWorkspace { Sources = [source] };
        var packageSource = new SheetPackageSource
        {
            SourceId = source.Id,
            Application = SheetSourceApplication.AutoCad,
        };
        var entry = new SheetPackageEntry
        {
            SheetId = "AR-01",
            BuildingId = "kindergarten",
            BuildingName = "Цэцэрлэг",
        };

        bool changed =
            ProjectDesignSourceClassification.ApplyPackageBuildingGroupAssignments(
                project,
                source,
                packageSource,
                [entry],
                allowNewBuildingGroups: true);

        Assert.True(changed);
        ProjectBuildingGroup created = Assert.Single(project.BuildingGroups);
        Assert.Equal("Цэцэрлэг", created.Name);
        // The declared id is kept so the next package resolves to this same group.
        Assert.Equal("kindergarten", created.Id);
        Assert.Equal(
            created.Id,
            project.SheetBuildingAssignments[
                SheetRecord.MakeKey(packageSource, entry, source.Id)]);

        Assert.False(
            ProjectDesignSourceClassification.ApplyPackageBuildingGroupAssignments(
                project,
                source,
                packageSource,
                [entry],
                allowNewBuildingGroups: true));
        Assert.Single(project.BuildingGroups);
    }

    [Fact]
    public void AlbumThatDoesNotComposeBuildings_NeverGainsABuildingGroup()
    {
        // An urban-planning album draws no building sub-cover, so a group created here would
        // demand a cover that never exists and block the project's sync.
        var source = new ProjectDesignSource
        {
            Id = "autocad-source",
            Kind = DesignSourceKind.AutoCad,
        };
        var project = new ProjectWorkspace { Sources = [source] };
        var packageSource = new SheetPackageSource
        {
            SourceId = source.Id,
            Application = SheetSourceApplication.AutoCad,
        };

        bool changed =
            ProjectDesignSourceClassification.ApplyPackageBuildingGroupAssignments(
                project,
                source,
                packageSource,
                [
                    new SheetPackageEntry
                    {
                        SheetId = "IDB-01",
                        BuildingName = "Цэцэрлэг",
                    },
                ]);

        Assert.False(changed);
        Assert.Empty(project.BuildingGroups);
        Assert.Empty(project.SheetBuildingAssignments);
    }

    [Fact]
    public void SheetsWithoutABuildingIdentity_DoNotInventAGroup()
    {
        var source = new ProjectDesignSource
        {
            Id = "autocad-source",
            Kind = DesignSourceKind.AutoCad,
        };
        var project = new ProjectWorkspace { Sources = [source] };
        var packageSource = new SheetPackageSource
        {
            SourceId = source.Id,
            Application = SheetSourceApplication.AutoCad,
        };

        bool changed =
            ProjectDesignSourceClassification.ApplyPackageBuildingGroupAssignments(
                project,
                source,
                packageSource,
                [new SheetPackageEntry { SheetId = "AR-01" }]);

        Assert.False(changed);
        Assert.Empty(project.BuildingGroups);
        Assert.Empty(project.SheetBuildingAssignments);
    }

    [Fact]
    public void DetectedBuildingSourceWithoutAGroup_GetsOneNamedAfterItsDrawing()
    {
        // Nobody picks a building group for an AutoCAD source, because it is only recognised
        // as a building once its package is read.
        var source = new ProjectDesignSource
        {
            Id = "autocad-building",
            Kind = DesignSourceKind.AutoCad,
            Name = "AutoCAD - Layout",
            NativeDocumentTitle = "Сургууль.dwg",
        };
        var project = new ProjectWorkspace { Sources = [source] };
        ProjectDesignSourceClassification.RecordDetectedPurpose(
            source,
            GeneralPlanManifest(discipline: "AR", contentKind: "floor-plan"));

        Assert.True(
            ProjectDesignSourceClassification.EnsureBuildingGroupForSource(project, source));

        ProjectBuildingGroup created = Assert.Single(project.BuildingGroups);
        Assert.Equal("Сургууль", created.Name);
        Assert.Equal(
            created.Id,
            ProjectDesignSourceClassification.BuildingGroupId(source));

        // Reading the package again must not add a second group beside it.
        Assert.False(
            ProjectDesignSourceClassification.EnsureBuildingGroupForSource(project, source));
        Assert.Single(project.BuildingGroups);
    }

    [Fact]
    public void GeneralPlanSource_NeverGetsABuildingGroup()
    {
        var source = new ProjectDesignSource
        {
            Id = "autocad-general-plan",
            Kind = DesignSourceKind.AutoCad,
            NativeDocumentTitle = "ЕТ.dwg",
        };
        var project = new ProjectWorkspace { Sources = [source] };
        ProjectDesignSourceClassification.RecordDetectedPurpose(
            source,
            GeneralPlanManifest(discipline: "ЕТ", contentKind: "general-plan-zoning"));

        Assert.False(
            ProjectDesignSourceClassification.EnsureBuildingGroupForSource(project, source));
        Assert.Empty(project.BuildingGroups);
    }

    [Fact]
    public void EngineeringInfrastructureMarkIsNotTheGeneralPlan()
    {
        // ИДБ is Инженерийн дэд бүтэц, a discipline of the same album. This purpose makes a
        // source the owner of the project's general plan, Project Land and location scheme,
        // which an engineering-infrastructure source is not.
        var source = new ProjectDesignSource
        {
            Id = "autocad-idb",
            Kind = DesignSourceKind.AutoCad,
        };

        ProjectDesignSourceClassification.RecordDetectedPurpose(
            source,
            GeneralPlanManifest(discipline: "ИДБ", contentKind: "heating-supply"));

        Assert.NotEqual(
            ProjectDesignSourcePurpose.GeneralPlan,
            ProjectDesignSourceClassification.EffectivePurpose(source));
    }
    private static SheetPackageManifest GeneralPlanManifest(
        string discipline,
        string contentKind) => new()
    {
        PackageScope = SheetPackageScope.FullSnapshot,
        Sheets =
        [
            new SheetPackageEntry
            {
                SheetId = "sheet-1",
                Discipline = discipline,
                ContentKind = contentKind,
            },
        ],
    };
}
