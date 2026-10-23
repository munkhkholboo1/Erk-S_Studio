using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumRendererMigrationTests
{
    // 🔴 STUDIO-20260724-1332, 2026-10-23: the school's and the kindergarten's sheets come
    // from a collaborator's source, so this device draws no sub-cover for them. The sync
    // already keeps such a sub-cover in the Cloud and pending - and then the renderer
    // check, run before it, stopped the WHOLE sync on the same two codes, every time.
    [Fact]
    public void UnrenderedBlockers_ADeferredBuildingSubCoverNeverStopsTheSync()
    {
        const string school = "generated:building-sub-cover:studio-building:school";
        const string kindergarten = "generated:building-sub-cover:studio-building:kindergarten";
        const string cover = "generated:cover:Cover";

        IReadOnlyList<string> blockers = StudioAlbumRendererMigration.UnrenderedBlockers(
            missing: [school, kindergarten, cover],
            migrationCodes: [school, kindergarten, cover],
            deferredCodes: [school, kindergarten],
            isReplacedBySource: _ => false);

        Assert.Equal([cover], blockers);
    }

    [Fact]
    public void UnrenderedBlockers_AReplacedListAndANonMigrationCodeDoNotBlock()
    {
        IReadOnlyList<string> blockers = StudioAlbumRendererMigration.UnrenderedBlockers(
            missing: ["generated:list", "source:x"],
            migrationCodes: ["generated:list"],
            deferredCodes: [],
            isReplacedBySource: code => code == "generated:list");

        Assert.Empty(blockers);
    }

    [Fact]
    public void SelectLocallyRenderableComponents_OnlyReturnsOwnedLocalSources()
    {
        const string owner = "architect@example.com";
        const string collaborator = "collaborator@example.com";
        ProjectWorkspace project = ProjectWorkspaceStore.Create("MIGRATION-01", "Renderer migration");
        var source = new ProjectDesignSource { Id = "local-revit", Name = "Revit" };
        project.Sources.Add(source);
        ProjectCloudSyncMetadata.RecordPackage(
            project,
            source,
            new SheetPackageManifest
            {
                PackageId = Guid.NewGuid(),
                Source = new SheetPackageSource
                {
                    SourceId = source.Id,
                    Application = SheetSourceApplication.Revit,
                },
            },
            new string('a', 64));
        ProjectCloudSyncMetadata.BindCloudOwner(source, owner);

        string ownedCode = StudioAlbumComponentIdentity.SourceCode(owner, source.Id);
        string foreignCode = StudioAlbumComponentIdentity.SourceCode(collaborator, source.Id);
        ProjectCloudAlbumComponentReference[] manifest =
        [
            SourceComponent(ownedCode, source.Id, owner),
            SourceComponent(foreignCode, source.Id, collaborator),
            new ProjectCloudAlbumComponentReference
            {
                Code = ProjectCloudSyncMetadata.CoverComponentCode,
                ComponentKind = StudioAlbumComponentIdentity.GeneratedComponentKind,
            },
        ];

        IReadOnlyList<string> selected =
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                project,
                manifest,
                owner,
                hasOwnedAtd: false,
                hasVisualizations: false);

        Assert.Equal([ownedCode], selected);
    }

    [Fact]
    public void SelectLocallyRenderableComponents_IncludesOwnedStudioAssetSources()
    {
        const string owner = "architect@example.com";
        ProjectWorkspace project = ProjectWorkspaceStore.Create("MIGRATION-02", "Asset migration");
        string atdCode = StudioAlbumComponentIdentity.SourceCode(
            owner,
            StudioAlbumComponentIdentity.AtdSourceKey);
        string visualizationCode = StudioAlbumComponentIdentity.SourceCode(
            owner,
            StudioAlbumComponentIdentity.VisualizationSourceKey);
        ProjectCloudAlbumComponentReference[] manifest =
        [
            SourceComponent(atdCode, StudioAlbumComponentIdentity.AtdSourceKey, owner),
            SourceComponent(
                visualizationCode,
                StudioAlbumComponentIdentity.VisualizationSourceKey,
                owner),
        ];

        IReadOnlyList<string> selected =
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                project,
                manifest,
                owner,
                hasOwnedAtd: true,
                hasVisualizations: true);

        Assert.Equal([atdCode, visualizationCode], selected);
    }

    private static ProjectCloudAlbumComponentReference SourceComponent(
        string code,
        string sourceKey,
        string ownerEmail) => new()
    {
        Code = code,
        SourceKey = sourceKey,
        OwnerEmail = ownerEmail,
        ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
    };
}
