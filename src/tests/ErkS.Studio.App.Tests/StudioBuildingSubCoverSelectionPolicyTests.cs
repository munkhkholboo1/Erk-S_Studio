using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioBuildingSubCoverSelectionPolicyTests
{
    [Fact]
    public void SelectedBuildingSourceSliceAutomaticallyIncludesItsSubCover()
    {
        ProjectWorkspace project = Project();
        string sourceCode = SourceSliceCode(
            "architect@example.com",
            "school-autocad",
            "studio-building:school",
            "floor-plans");
        StudioCloudAlbumSection source = Component(
            sourceCode,
            "architect@example.com",
            "school-autocad",
            "Source");
        StudioCloudAlbumSection cover = Component(
            "generated:building-sub-cover:studio-building:school",
            "",
            "",
            "Generated");
        StudioCloudAlbumSection unrelated = Component(
            "generated:cover:concept",
            "",
            "",
            "Generated");

        StudioBuildingSubCoverSelection selection =
            StudioBuildingSubCoverSelectionPolicy.IncludeRequiredCovers(
                project,
                [unrelated, cover, source],
                [source]);

        Assert.Empty(selection.MissingRequiredCoverCodes);
        Assert.Equal(
            [cover.Code, source.Code],
            selection.Components.Select(component => component.Code));
    }

    [Fact]
    public void AssignedAutoCadSourceWithoutRenderedSubCoverIsReportedIncomplete()
    {
        ProjectWorkspace project = Project();
        project.Cloud.SharedBuildingSheetAssignments =
        [
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceOwnerEmail = "architect@example.com",
                SourceKey = "school-autocad",
                SheetId = "layout-1",
                BuildingGroupId = "school",
            },
        ];
        StudioCloudAlbumSection source = Component(
            StudioAlbumComponentIdentity.SourceCode(
                "architect@example.com",
                "school-autocad"),
            "architect@example.com",
            "school-autocad",
            "Source");

        StudioBuildingSubCoverSelection selection =
            StudioBuildingSubCoverSelectionPolicy.IncludeRequiredCovers(
                project,
                [source],
                [source]);

        Assert.Equal(
            ["generated:building-sub-cover:studio-building:school"],
            selection.MissingRequiredCoverCodes);
        Assert.Equal(source, Assert.Single(selection.Components));
    }

    [Fact]
    public void ActiveBuildingSliceDoesNotRequireCoverFromStaleAssignment()
    {
        ProjectWorkspace project = new()
        {
            BuildingGroups =
            [
                new ProjectBuildingGroup
                {
                    Id = "active-a",
                    Name = "Active A",
                    Order = 1,
                },
                new ProjectBuildingGroup
                {
                    Id = "inactive-b",
                    Name = "Inactive B",
                    Order = 2,
                },
            ],
        };
        project.Cloud.SharedBuildingSheetAssignments =
        [
            new ProjectCloudBuildingSheetAssignmentReference
            {
                SourceOwnerEmail = "architect@example.com",
                SourceKey = "shared-autocad",
                SheetId = "stale-layout",
                BuildingGroupId = "inactive-b",
            },
        ];
        StudioCloudAlbumSection source = Component(
            SourceSliceCode(
                "architect@example.com",
                "shared-autocad",
                "studio-building:active-a",
                "floor-plans"),
            "architect@example.com",
            "shared-autocad",
            "Source");
        StudioCloudAlbumSection activeCover = Component(
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix +
                "studio-building:active-a",
            "",
            "",
            "Generated");

        StudioBuildingSubCoverSelection selection =
            StudioBuildingSubCoverSelectionPolicy.IncludeRequiredCovers(
                project,
                [activeCover, source],
                [source]);

        Assert.Empty(selection.MissingRequiredCoverCodes);
        Assert.Equal(
            [activeCover.Code, source.Code],
            selection.Components.Select(component => component.Code));
    }

    private static ProjectWorkspace Project() => new()
    {
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

    private static StudioCloudAlbumSection Component(
        string code,
        string owner,
        string sourceKey,
        string kind) => new()
    {
        Code = code,
        Label = code,
        Order = 1,
        PageNumbers = [1],
        Status = "Available",
        OwnerEmail = owner,
        SourceKey = sourceKey,
        ComponentKind = kind,
    };

    private static string SourceSliceCode(
        string owner,
        string sourceKey,
        string sectionKey,
        string sequenceKey) =>
        StudioAlbumComponentIdentity.SourceCode(owner, sourceKey) +
        "|album-slice|" +
        Encode(sectionKey) +
        "." +
        Encode(sequenceKey);

    private static string Encode(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
