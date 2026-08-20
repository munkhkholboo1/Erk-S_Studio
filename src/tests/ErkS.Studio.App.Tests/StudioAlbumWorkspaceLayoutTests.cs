using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumWorkspaceLayoutTests
{
    [Fact]
    public void PrimaryWorkspace_ShowsNavigatorPreviewAndAlbumProperties()
    {
        IReadOnlyList<StudioAlbumWorkspacePane> panes =
            StudioAlbumWorkspaceLayout.PrimaryPanes;

        Assert.Equal(
            [
                StudioAlbumWorkspacePane.Navigator,
                StudioAlbumWorkspacePane.Preview,
                StudioAlbumWorkspacePane.Properties,
            ],
            panes);
    }
}
