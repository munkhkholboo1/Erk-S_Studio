using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumPreviewPageMapTests
{
    [Fact]
    public void CanonicalSourcePage_UsesManifestPagesInsteadOfLocalAlbumOffset()
    {
        const string owner = "architect@example.com";
        const string sourceKey = "autocad-school";
        ProjectWorkspace workspace = ProjectWorkspaceStore.Create(
            "PREVIEW-001",
            "Canonical preview");
        var source = new ProjectDesignSource { Id = "local-source" };
        workspace.Sources.Add(source);
        ProjectCloudSyncMetadata.BindToCloudSource(workspace, source, sourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(source, owner);

        AlbumBuildPage first = BuildPage(source.Id, "sheet-1", 1);
        AlbumBuildPage second = BuildPage(source.Id, "sheet-2", 2);
        AlbumBuildRequest request = Request(
            new AlbumBuildSection
            {
                Key = "studio-building:school",
                Title = "Сургууль",
                Kind = AlbumBuildSectionKind.Building,
                Pages = [first, second],
            });
        string componentCode = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "studio-building:school",
            "");
        ProjectCloudAlbumComponentReference[] manifest =
        [
            new()
            {
                Code = ProjectCloudSyncMetadata.CoverComponentCode,
                PageNumbers = [1],
            },
            new()
            {
                Code = ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix +
                    "studio-building:school",
                PageNumbers = [12],
            },
            new()
            {
                Code = componentCode,
                PageNumbers = [13, 14],
                OwnerEmail = owner,
                SourceKey = sourceKey,
                ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
            },
        ];

        int? page = StudioAlbumPreviewPageMap.ResolveCanonicalSourcePage(
            workspace,
            request,
            second.Definition.Id,
            manifest);

        Assert.Equal(14, page);
    }

    [Fact]
    public void CanonicalSourcePage_FailsClosedWhenManifestDoesNotContainTargetOffset()
    {
        const string owner = "architect@example.com";
        const string sourceKey = "autocad-school";
        ProjectWorkspace workspace = ProjectWorkspaceStore.Create(
            "PREVIEW-002",
            "Incomplete canonical preview");
        var source = new ProjectDesignSource { Id = "local-source" };
        workspace.Sources.Add(source);
        ProjectCloudSyncMetadata.BindToCloudSource(workspace, source, sourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(source, owner);

        AlbumBuildPage first = BuildPage(source.Id, "sheet-1", 1);
        AlbumBuildPage second = BuildPage(source.Id, "sheet-2", 2);
        AlbumBuildRequest request = Request(
            new AlbumBuildSection
            {
                Key = "studio-building:school",
                Title = "Сургууль",
                Kind = AlbumBuildSectionKind.Building,
                Pages = [first, second],
            });
        string componentCode = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "studio-building:school",
            "");

        int? page = StudioAlbumPreviewPageMap.ResolveCanonicalSourcePage(
            workspace,
            request,
            second.Definition.Id,
            [
                new ProjectCloudAlbumComponentReference
                {
                    Code = componentCode,
                    PageNumbers = [13],
                },
            ]);

        Assert.Null(page);
    }

    [Fact]
    public void LocalSourcePage_CountsWriterInsertedBuildingSubCover()
    {
        AlbumBuildPage generalPlan = BuildPage("general", "master-plan", 1);
        AlbumBuildPage school = BuildPage("school", "floor-plan", 2);
        AlbumBuildRequest request = Request(
            new AlbumBuildSection
            {
                Key = "fixed:general-plan",
                Title = "Ерөнхий төлөвлөгөө",
                Pages = [generalPlan],
            },
            new AlbumBuildSection
            {
                Key = "studio-building:school",
                Title = "Сургууль",
                Kind = AlbumBuildSectionKind.Building,
                Pages = [school],
            });

        int? page = StudioAlbumPreviewPageMap.ResolveLocalSourcePage(
            request,
            school.Definition.Id,
            leadingPageCount: 7,
            _ => true);

        Assert.Equal(10, page);
    }

    [Fact]
    public void LocalVisualizationPage_CountsEveryBuildingSubCover()
    {
        AlbumBuildRequest request = Request(
            new AlbumBuildSection
            {
                Key = "studio-building:apartment",
                Title = "Орон сууц",
                Kind = AlbumBuildSectionKind.Building,
                Pages =
                [
                    BuildPage("apartment", "plan", 1),
                    BuildPage("apartment", "section", 2),
                ],
            },
            new AlbumBuildSection
            {
                Key = "studio-building:school",
                Title = "Сургууль",
                Kind = AlbumBuildSectionKind.Building,
                Pages = [BuildPage("school", "plan", 3)],
            });

        int? page = StudioAlbumPreviewPageMap.ResolveLocalVisualizationPage(
            request,
            visualizationPageIndex: 0,
            leadingPageCount: 7,
            _ => true);

        Assert.Equal(13, page);
    }

    [Fact]
    public void CanonicalGeneratedPage_ResolvesOwnedAtdSourceAlias()
    {
        int? page = StudioAlbumPreviewPageMap.ResolveCanonicalGeneratedPage(
            [
                new ProjectCloudAlbumComponentReference
                {
                    Code = StudioAlbumComponentIdentity.SourceCode(
                        "admin@example.com",
                        StudioAlbumComponentIdentity.AtdSourceKey),
                    SourceKey = StudioAlbumComponentIdentity.AtdSourceKey,
                    OwnerEmail = "admin@example.com",
                    PageNumbers = [5, 6],
                },
            ],
            ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
            zeroBasedPageOffset: 1);

        Assert.Equal(6, page);
    }

    [Theory]
    [InlineData(@"C:\projects\albums\cloud\canonical.pdf", true)]
    [InlineData(@"C:\projects\albums\cloud-local\canonical.pdf", true)]
    [InlineData(@"C:\projects\albums\cloud-local\canonical\R63.pdf", true)]
    [InlineData(@"C:\projects\albums\building-concept.pdf", false)]
    public void UsesSharedManifest_IsRestrictedToCanonicalCacheFolders(
        string path,
        bool expected)
    {
        Assert.Equal(expected, StudioAlbumPreviewPageMap.UsesSharedManifest(path));
    }

    private static AlbumBuildRequest Request(params AlbumBuildSection[] sections) =>
        new()
        {
            Project = new AlbumProject
            {
                Album =
                {
                    IncludeCover = false,
                    IncludeTableOfContents = false,
                },
            },
            Sections = sections,
        };

    private static AlbumBuildPage BuildPage(
        string sourceId,
        string sheetId,
        int sourceSheetIndex)
    {
        var definition = new AlbumPageDefinition
        {
            SheetKey = $"{sourceId}|{sheetId}",
        };
        return new AlbumBuildPage
        {
            Definition = definition,
            Format = PageFormatCatalog.Resolve(PageFormatCatalog.SourceAsIsId),
            Sheet = new SheetRecord
            {
                Key = definition.SheetKey,
                SourceId = sourceId,
                SourceIdentity = sourceId,
                Entry = new SheetPackageEntry
                {
                    SheetId = sheetId,
                    PageCount = 1,
                },
                Source = new SheetPackageSource { SourceId = sourceId },
                PackageId = Guid.NewGuid(),
                ManifestPath = "manifest.json",
                PdfPath = $"{sheetId}.pdf",
                SourceSheetIndex = sourceSheetIndex,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                IsVerified = true,
            },
        };
    }
}
