using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioBuildingCompositionSyncTests
{
    [Fact]
    public void ApplyCanonicalPreservesPendingLocalCompositionButCachesCloudUnion()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "local-building",
                    Name = "Локал барилга",
                    Order = 1,
                },
            ],
        };
        var canonical = new StudioCloudBuildingComposition
        {
            Version = 4,
            Groups =
            [
                new StudioCloudBuildingGroup
                {
                    Id = "cloud-building",
                    Name = "Cloud барилга",
                    Order = 2,
                },
            ],
            SheetAssignments =
            [
                new StudioCloudBuildingSheetAssignment
                {
                    SourceKey = "remote-revit",
                    SheetId = "sheet-7",
                    BuildingGroupId = "cloud-building",
                },
            ],
        };

        bool changed = StudioBuildingCompositionSync.ApplyCanonical(
            project,
            new SheetLibrary(),
            canonical,
            preserveLocalEdits: true);

        Assert.True(changed);
        Assert.Equal("local-building", Assert.Single(project.BuildingGroups).Id);
        Assert.Equal(4, project.Cloud.SharedBuildingCompositionVersion);
        Assert.Equal("cloud-building", Assert.Single(project.Cloud.SharedBuildingGroups).Id);
        ProjectCloudBuildingSheetAssignmentReference shared =
            Assert.Single(project.Cloud.SharedBuildingSheetAssignments);
        Assert.Equal("remote-revit", shared.SourceKey);
        Assert.Equal("sheet-7", shared.SheetId);
    }

    [Fact]
    public void CreateUpdateKeepsForeignAssignmentsAndDropsStaleLocalAssignments()
    {
        var project = new ProjectWorkspace
        {
            Sources =
            [
                new ProjectDesignSource
                {
                    Id = "local-autocad",
                    Kind = DesignSourceKind.AutoCad,
                },
            ],
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "building-1",
                    Name = "Барилга 1",
                    Order = 1,
                },
            ],
        };
        project.Cloud.SharedBuildingSheetAssignments =
        [
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceKey = "remote-revit",
                SheetId = "section-1",
                BuildingGroupId = "building-1",
            },
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceKey = "local-autocad",
                SheetId = "deleted-plan",
                BuildingGroupId = "building-1",
            },
        ];

        StudioCloudBuildingCompositionUpdateRequest update =
            StudioBuildingCompositionSync.CreateUpdate(
                project,
                new SheetLibrary());

        StudioCloudBuildingSheetAssignment assignment =
            Assert.Single(update.SheetAssignments);
        Assert.Equal("remote-revit", assignment.SourceKey);
        Assert.Equal("section-1", assignment.SheetId);
        Assert.Equal("building-1", assignment.BuildingGroupId);
    }

    [Fact]
    public void ApplyCanonicalReplacesLocalCompositionAfterSuccessfulSync()
    {
        var project = new ProjectWorkspace
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "old-building",
                    Name = "Хуучин",
                    Order = 1,
                },
            ],
            SheetBuildingAssignments = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase)
            {
                ["local|sheet-1"] = "old-building",
            },
        };
        var canonical = new StudioCloudBuildingComposition
        {
            Version = 2,
            Groups =
            [
                new StudioCloudBuildingGroup
                {
                    Id = "building-2",
                    Name = "Барилга 2",
                    Order = 2,
                },
            ],
        };

        _ = StudioBuildingCompositionSync.ApplyCanonical(
            project,
            new SheetLibrary(),
            canonical,
            preserveLocalEdits: false);

        Assert.Equal("building-2", Assert.Single(project.BuildingGroups).Id);
        Assert.Empty(project.SheetBuildingAssignments);
    }
}
