using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// An album whose sheets have not been received yet references pages nothing
/// can resolve. That is an ordinary state for a project waiting on a delivery,
/// and asking about such an album must not be fatal - the album workspace once
/// took the whole application down over it.
/// </summary>
public sealed class AlbumBuildRequestTolerationTests
{
    [Fact]
    public void UnresolvedPages_AreReportedRatherThanThrown()
    {
        AlbumProject project = ProjectWithPageForMissingSheet();

        bool built = AlbumBuilder.TryCreateRequest(project, new SheetLibrary(), out AlbumBuildRequest request);

        Assert.False(built);
        Assert.Null(request);
    }

    [Fact]
    public void TheThrowingFormStillThrows()
    {
        // Building for real must still refuse: a half-resolved album is not a
        // document anyone should receive.
        AlbumProject project = ProjectWithPageForMissingSheet();

        AlbumBuildException error = Assert.Throws<AlbumBuildException>(
            () => AlbumBuilder.CreateRequest(project, new SheetLibrary()));

        Assert.Contains(
            error.Issues,
            issue => issue.Contains("missing or unverified", StringComparison.Ordinal));
    }

    [Fact]
    public void AnAlbumWithNothingUnresolved_BuildsThroughBothForms()
    {
        var project = new AlbumProject
        {
            Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept"),
        };

        Assert.True(AlbumBuilder.TryCreateRequest(project, new SheetLibrary(), out AlbumBuildRequest request));
        Assert.NotNull(request);
    }

    private static AlbumProject ProjectWithPageForMissingSheet()
    {
        AlbumDefinition album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");
        album.Pages.Add(new AlbumPageDefinition
        {
            SheetKey = "source-that-was-never-received|sheet-1",
        });
        return new AlbumProject { Album = album };
    }
}
