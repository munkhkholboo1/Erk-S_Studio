using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumRendererMigrationTests
{
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
