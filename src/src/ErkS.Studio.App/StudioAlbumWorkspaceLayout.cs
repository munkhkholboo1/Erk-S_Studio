namespace ErkS.Studio;

internal enum StudioAlbumWorkspacePane
{
    Navigator,
    Preview,
    Properties,
}

internal static class StudioAlbumWorkspaceLayout
{
    public static IReadOnlyList<StudioAlbumWorkspacePane> PrimaryPanes { get; } =
    [
        StudioAlbumWorkspacePane.Navigator,
        StudioAlbumWorkspacePane.Preview,
        StudioAlbumWorkspacePane.Properties,
    ];

    public const double NavigatorWidth = 285;
    public const double NavigatorMinimumWidth = 240;
    public const double PreviewMinimumWidth = 420;
    public const double PropertiesWidth = 350;
    public const double PropertiesMinimumWidth = 310;
}
