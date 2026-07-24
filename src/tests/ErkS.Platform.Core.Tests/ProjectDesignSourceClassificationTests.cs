using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

public sealed class ProjectDesignSourceClassificationTests
{
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
}
