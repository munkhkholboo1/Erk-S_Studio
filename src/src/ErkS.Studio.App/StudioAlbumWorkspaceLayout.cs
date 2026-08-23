namespace ErkS.Studio;

internal enum StudioAlbumWorkspacePane
{
    Preview,
    Properties,
}

internal static class StudioAlbumWorkspaceLayout
{
    /// <summary>
    /// The album workspace is the drawing and the settings for the page on
    /// screen. It used to carry a third pane listing the album's pages, which
    /// held only the pages this device contributed and could not show the page
    /// it selected; the width it took belongs to the drawing.
    /// </summary>
    public static IReadOnlyList<StudioAlbumWorkspacePane> PrimaryPanes { get; } =
    [
        StudioAlbumWorkspacePane.Preview,
        StudioAlbumWorkspacePane.Properties,
    ];

    public const double PreviewMinimumWidth = 420;
    public const double PropertiesWidth = 350;
    public const double PropertiesMinimumWidth = 310;
}
