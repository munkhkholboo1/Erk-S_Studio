using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The server types an album by its stage since SRV 88d049f (2026-10-20): a
/// working-drawing album is «BuildingWorkingDrawings». Every fallback that
/// looked for the concept type alone must find it too.
/// </summary>
public sealed class AWORKINGDrawingAlbumIsTheBUILDINGAlbumTests
{
    [Fact]
    public void BothServerTypesAreTheBuildingAlbum()
    {
        Assert.True(StudioCloudAlbumSelection.IsBuildingAlbumType(ProjectWorkspace.BuildingArchitectureConcept));
        Assert.True(StudioCloudAlbumSelection.IsBuildingAlbumType("BuildingWorkingDrawings"));
        Assert.False(StudioCloudAlbumSelection.IsBuildingAlbumType("UrbanPlanning"));
        Assert.False(StudioCloudAlbumSelection.IsBuildingAlbumType(null));
    }

    [Fact]
    public void TheRebuildSignalIsReadFromAWorkingDrawingAlbumBesideAnother()
    {
        var cloud = new StudioCloudProjectDetail
        {
            Albums =
            [
                new StudioCloudAlbum { AlbumId = "other", AlbumType = "UrbanPlanning" },
                new StudioCloudAlbum
                {
                    AlbumId = "wd",
                    AlbumType = "BuildingWorkingDrawings",
                    CanonicalRebuildPending = true,
                },
            ],
        };

        Assert.True(StudioCanonicalAlbumRebuildPolicy.Resolve(new ProjectWorkspace(), cloud).IsPending);
    }

    [Fact]
    public void NoFallbackAsksForTheConceptTypeAlone()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string app = Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App");
        foreach (string file in new[] { "ShellView.cs", "StudioCanonicalAlbumRebuildPolicy.cs" })
        {
            string text = File.ReadAllText(Path.Combine(app, file));
            Assert.DoesNotMatch(@"AlbumType\.Equals\(\s*ProjectWorkspace\.BuildingArchitectureConcept", text);
        }
    }
}
