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
                ConceptAlbum(),
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
                ConceptAlbum(),
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
                ConceptAlbum(),
                [activeCover, source],
                [source]);

        Assert.Empty(selection.MissingRequiredCoverCodes);
        Assert.Equal(
            [activeCover.Code, source.Code],
            selection.Components.Select(component => component.Code));
    }

    [Fact]
    public void AWorkingDrawingAlbumRequiresNoSubCoverItNeverDraws()
    {
        // 🔴 MEASURED ON BATNASAN, 2026-10-20: the ЕТ source was assigned to
        // «Барилга 1»; the working-drawing album is not built in building
        // sections, so no sub-cover was ever rendered - and the sync refused
        // the whole album for want of it. Only an album that draws sub-covers
        // can be missing one.
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
            StudioAlbumComponentIdentity.SourceCode("architect@example.com", "school-autocad"),
            "architect@example.com",
            "school-autocad",
            "Source");

        StudioBuildingSubCoverSelection selection =
            StudioBuildingSubCoverSelectionPolicy.IncludeRequiredCovers(
                project,
                BuildingWorkingDrawingAlbumTemplate.CreateDefinition(""),
                [source],
                [source]);

        Assert.Empty(selection.MissingRequiredCoverCodes);
        Assert.Equal(source, Assert.Single(selection.Components));
    }

    [Fact]
    public void APendingSubCoverIsOwedOnlyByAnAlbumThatDrawsThem()
    {
        // Read from source, as the WPF view's neighbours are: a pending
        // sub-cover in a working-drawing album was deferred for ever.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string view = File.ReadAllText(Path.Combine(
            directory!.FullName, "src", "src", "ErkS.Studio.App", "ShellView.AlbumComponents.cs")).Replace("\r\n", "\n");
        int start = view.IndexOf("private bool IsCurrentBuildingSubCover(string componentCode)", StringComparison.Ordinal);
        string body = view[start..view.IndexOf("\n    private ", start + 10, StringComparison.Ordinal)];

        Assert.Contains("if (!AlbumBuilder.RendersBuildingSubCovers(state.Album))", body, StringComparison.Ordinal);
    }

    private static AlbumDefinition ConceptAlbum() =>
        BuildingArchitectureConceptAlbumTemplate.CreateDefinition("");

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
