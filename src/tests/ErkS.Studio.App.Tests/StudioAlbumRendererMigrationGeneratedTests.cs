using ErkS.Platform.Core;

namespace ErkS.Studio.Tests;

public sealed class StudioAlbumRendererMigrationGeneratedTests
{
    // The cover, the drawing list and the location scheme are drawn by Studio
    // from project data, so no device needs a source to redraw them. They were
    // skipped on every device, which left a generated page drawn by an older
    // build in the shared album with no way to replace it.
    [Fact]
    public void GeneratedPagesAreRedrawnByAnAccountThatMayRewriteThem()
    {
        ProjectWorkspace project = CloudProject();

        IReadOnlyList<string> codes =
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                project,
                GeneratedManifest(),
                "member@erks.local",
                hasOwnedAtd: false,
                hasVisualizations: false,
                canManageCanonicalMetadata: true);

        Assert.Equal(
            ["generated:cover", "generated:site-context"],
            codes);
    }

    [Fact]
    public void GeneratedPagesAreLeftAloneWithoutThatAuthority()
    {
        ProjectWorkspace project = CloudProject();

        IReadOnlyList<string> codes =
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                project,
                GeneratedManifest(),
                "member@erks.local",
                hasOwnedAtd: false,
                hasVisualizations: false,
                canManageCanonicalMetadata: false);

        Assert.Empty(codes);
    }

    private static IReadOnlyList<ProjectCloudAlbumComponentReference> GeneratedManifest() =>
    [
        new()
        {
            Code = "generated:cover",
            ComponentKind = StudioAlbumComponentIdentity.GeneratedComponentKind,
        },
        new()
        {
            Code = "generated:site-context",
            ComponentKind = StudioAlbumComponentIdentity.GeneratedComponentKind,
        },
    ];

    private static ProjectWorkspace CloudProject()
    {
        ProjectWorkspace project = ProjectWorkspaceStore.Create("REN-001", "Renderer migration");
        project.Cloud.Origin = ProjectOrigins.Cloud;
        project.Cloud.ServerProjectId = "server-project";
        return project;
    }
}
