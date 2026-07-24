using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumComponentOrderPolicyTests
{
    [Fact]
    public void Resolve_OrdersGeneralPlanBeforeEachBuildingSubCoverAndItsSources()
    {
        const string owner = "architect@example.com";
        ProjectWorkspace project = ProjectWorkspaceStore.Create("ORDER-01", "Album order");
        var buildingOne = new ProjectBuildingGroup
        {
            Id = "building-one",
            Name = "Building 1",
            Order = 1,
        };
        var buildingTwo = new ProjectBuildingGroup
        {
            Id = "building-two",
            Name = "Building 2",
            // Duplicate user-facing order values must still produce one stable
            // cover/source pair per building.
            Order = 1,
        };
        project.BuildingGroups = [buildingOne, buildingTwo];

        var generalPlan = new ProjectDesignSource
        {
            Id = "general-plan",
            Kind = DesignSourceKind.CityGen,
        };
        var buildingOneSource = new ProjectDesignSource { Id = "building-one-source" };
        var buildingTwoSource = new ProjectDesignSource { Id = "building-two-source" };
        project.Sources.AddRange([generalPlan, buildingOneSource, buildingTwoSource]);
        project.SheetBuildingAssignments[buildingOneSource.Id + "|sheet-1"] = buildingOne.Id;
        project.SheetBuildingAssignments[buildingTwoSource.Id + "|sheet-1"] = buildingTwo.Id;

        string generalPlanCode = StudioAlbumComponentIdentity.SourceCode(owner, generalPlan.Id);
        string buildingOneCode =
            StudioAlbumComponentIdentity.SourceCode(owner, buildingOneSource.Id);
        string buildingTwoCode =
            StudioAlbumComponentIdentity.SourceCode(owner, buildingTwoSource.Id);
        var registrationOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [buildingTwoCode] = 0,
            [buildingOneCode] = 1,
            [generalPlanCode] = 2,
        };

        int cover = Resolve(
            project,
            ProjectCloudSyncMetadata.CoverComponentCode,
            "",
            localOrder: 0,
            registrationOrder);
        int generalPlanOrder = Resolve(
            project,
            generalPlanCode,
            generalPlan.Id,
            localOrder: 5,
            registrationOrder);
        int buildingOneCover = Resolve(
            project,
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(buildingOne),
            "",
            localOrder: 6,
            registrationOrder);
        int buildingOneSheets = Resolve(
            project,
            buildingOneCode,
            buildingOneSource.Id,
            localOrder: 7,
            registrationOrder);
        int buildingTwoCover = Resolve(
            project,
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(buildingTwo),
            "",
            localOrder: 8,
            registrationOrder);
        int buildingTwoSheets = Resolve(
            project,
            buildingTwoCode,
            buildingTwoSource.Id,
            localOrder: 9,
            registrationOrder);

        Assert.True(cover < generalPlanOrder);
        Assert.True(generalPlanOrder < buildingOneCover);
        Assert.True(buildingOneCover < buildingOneSheets);
        Assert.True(buildingOneSheets < buildingTwoCover);
        Assert.True(buildingTwoCover < buildingTwoSheets);
    }

    [Fact]
    public void Resolve_FixedPagesDoNotDependOnPreviousManifestOrder()
    {
        var project = new ProjectWorkspace();
        var sourceOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int cover = StudioAlbumComponentOrderPolicy.Resolve(
            project,
            ProjectCloudSyncMetadata.CoverComponentCode,
            "",
            900_000,
            sourceOrder);
        int registration = StudioAlbumComponentOrderPolicy.Resolve(
            project,
            ProjectCloudSyncMetadata.CompanyRegistrationComponentCode,
            "",
            1,
            sourceOrder);
        int license = StudioAlbumComponentOrderPolicy.Resolve(
            project,
            ProjectCloudSyncMetadata.CompanyLicenseComponentCode,
            "",
            0,
            sourceOrder);
        int atd = StudioAlbumComponentOrderPolicy.Resolve(
            project,
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
            StudioAlbumComponentIdentity.AtdSourceKey,
            -50,
            sourceOrder);
        int siteContext = StudioAlbumComponentOrderPolicy.Resolve(
            project,
            ProjectCloudSyncMetadata.SiteContextComponentCode,
            "",
            -100,
            sourceOrder);

        Assert.True(cover < registration);
        Assert.True(registration < license);
        Assert.True(license < atd);
        Assert.True(atd < siteContext);
    }

    [Fact]
    public void Resolve_OwnedAtdSourceAlwaysFollowsLicenseAndPrecedesSiteContext()
    {
        const string owner = "partner@example.com";
        var project = new ProjectWorkspace();
        var sourceOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string atdCode = StudioAlbumComponentIdentity.SourceCode(
            owner,
            StudioAlbumComponentIdentity.AtdSourceKey);

        int license = StudioAlbumComponentOrderPolicy.Resolve(
            project,
            ProjectCloudSyncMetadata.CompanyLicenseComponentCode,
            "",
            10,
            sourceOrder);
        int atdWithMetadata = StudioAlbumComponentOrderPolicy.Resolve(
            project,
            atdCode,
            StudioAlbumComponentIdentity.AtdSourceKey,
            900_000,
            sourceOrder);
        int atdRecoveredFromCode = StudioAlbumComponentOrderPolicy.Resolve(
            project,
            atdCode,
            "",
            900_000,
            sourceOrder);
        int siteContext = StudioAlbumComponentOrderPolicy.Resolve(
            project,
            ProjectCloudSyncMetadata.SiteContextComponentCode,
            "",
            0,
            sourceOrder);

        Assert.True(license < atdWithMetadata);
        Assert.Equal(atdWithMetadata, atdRecoveredFromCode);
        Assert.True(atdWithMetadata < siteContext);
    }

    [Fact]
    public void Resolve_PutsPackageSubCoverBeforeItsSourceAndUnassignedSourcesAfterBuildings()
    {
        const string owner = "architect@example.com";
        ProjectWorkspace project = ProjectWorkspaceStore.Create("ORDER-02", "Package order");
        var building = new ProjectBuildingGroup
        {
            Id = "building-one",
            Name = "Building 1",
            Order = 1,
        };
        project.BuildingGroups = [building];
        var assignedSource = new ProjectDesignSource { Id = "assigned-source" };
        var unassignedSource = new ProjectDesignSource { Id = "unassigned-source" };
        project.Sources.AddRange([assignedSource, unassignedSource]);
        project.SheetBuildingAssignments[assignedSource.Id + "|sheet-1"] = building.Id;

        string assignedCode =
            StudioAlbumComponentIdentity.SourceCode(owner, assignedSource.Id);
        string unassignedCode =
            StudioAlbumComponentIdentity.SourceCode(owner, unassignedSource.Id);
        var registrationOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [unassignedCode] = 0,
            [assignedCode] = 1,
        };

        int subCover = Resolve(
            project,
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix +
            "package-building:id:" + building.Id,
            "",
            localOrder: 200000,
            registrationOrder);
        int assigned = Resolve(
            project,
            assignedCode,
            assignedSource.Id,
            localOrder: 1001,
            registrationOrder);
        int unassigned = Resolve(
            project,
            unassignedCode,
            unassignedSource.Id,
            localOrder: 1000,
            registrationOrder);

        Assert.True(subCover < assigned);
        Assert.True(assigned < unassigned);
    }

    [Fact]
    public void Resolve_UsesCloudMetadataForForeignGeneralPlanAndBuildingSources()
    {
        const string foreignOwner = "partner@example.com";
        ProjectWorkspace project = ProjectWorkspaceStore.Create("ORDER-03", "Foreign sources");
        var building = new ProjectBuildingGroup
        {
            Id = "building-one",
            Name = "Building 1",
            Order = 1,
        };
        project.BuildingGroups = [building];
        project.Cloud.SharedSources =
        [
            new ProjectCloudSourceReference
            {
                SourceKey = "foreign-general-plan",
                SourceApplication = "Erk-S CityGen for AutoCAD",
                RegisteredBy = foreignOwner,
                OwnerEmail = foreignOwner,
                Status = "Registered",
            },
            new ProjectCloudSourceReference
            {
                SourceKey = "foreign-building",
                SourceApplication = "Revit",
                RegisteredBy = foreignOwner,
                OwnerEmail = foreignOwner,
                Status = "Registered",
            },
        ];
        project.Cloud.SharedBuildingSheetAssignments =
        [
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceKey = "foreign-building",
                SheetId = "sheet-1",
                BuildingGroupId = building.Id,
            },
        ];

        string generalPlanCode = StudioAlbumComponentIdentity.SourceCode(
            foreignOwner,
            "foreign-general-plan");
        string buildingCode = StudioAlbumComponentIdentity.SourceCode(
            foreignOwner,
            "foreign-building");
        var registrationOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [buildingCode] = 0,
            [generalPlanCode] = 1,
        };

        int generalPlan = Resolve(
            project,
            generalPlanCode,
            "foreign-general-plan",
            localOrder: 700_000,
            registrationOrder);
        int subCover = Resolve(
            project,
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(building),
            "",
            localOrder: 900_000,
            registrationOrder);
        int buildingSource = Resolve(
            project,
            buildingCode,
            "foreign-building",
            localOrder: 1,
            registrationOrder);

        Assert.True(generalPlan < subCover);
        Assert.True(subCover < buildingSource);
    }

    private static int Resolve(
        ProjectWorkspace project,
        string componentCode,
        string sourceKey,
        int localOrder,
        IReadOnlyDictionary<string, int> registrationOrder) =>
        StudioAlbumComponentOrderPolicy.Resolve(
            project,
            componentCode,
            sourceKey,
            localOrder,
            registrationOrder);
}
