using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumWorkspaceLayoutTests
{
    [Fact]
    public void PrimaryWorkspace_GivesTheWidthToTheDrawingAndItsSettings()
    {
        IReadOnlyList<StudioAlbumWorkspacePane> panes =
            StudioAlbumWorkspaceLayout.PrimaryPanes;

        Assert.Equal(
            [
                StudioAlbumWorkspacePane.Preview,
                StudioAlbumWorkspacePane.Properties,
            ],
            panes);
    }
}
