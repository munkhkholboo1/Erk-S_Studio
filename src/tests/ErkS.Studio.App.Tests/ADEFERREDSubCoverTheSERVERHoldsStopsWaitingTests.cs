using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Erin, 2026-10-20: the hospital's sub-cover was Available on the server
/// (R44, p.34) with the hospital's name, and this device - which cannot draw
/// it - kept it pending «for another member's sync» forever. A deferred
/// component the shared album already holds as it stands here is done.
/// </summary>
public sealed class ADEFERREDSubCoverTheSERVERHoldsStopsWaitingTests
{
    private static ProjectWorkspace Project()
    {
        var project = new ProjectWorkspace();
        project.BuildingGroups.Add(new ProjectBuildingGroup { Id = "hospital", Name = "Эмнэлэг", Order = 1 });
        return project;
    }

    private static string Code(ProjectWorkspace project) =>
        ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(project.BuildingGroups[0]);

    private static StudioCloudAlbumSection Cover(string code, string label, string status = "Available") => new()
    {
        Code = code,
        Label = label,
        Status = status,
        PageNumbers = [34],
    };

    [Fact]
    public void TheSameCoverOnTheServerIsCurrent()
    {
        ProjectWorkspace project = Project();
        Assert.True(StudioSubCoverServerState.IsCurrent(project, Cover(Code(project), "Эмнэлэг · Дэд нүүр хуудас")));
    }

    [Fact]
    public void ARenamedOrRemovedCoverStillWaits()
    {
        ProjectWorkspace project = Project();
        Assert.False(StudioSubCoverServerState.IsCurrent(project, Cover(Code(project), "Хуучин нэр · Дэд нүүр хуудас")));
        Assert.False(StudioSubCoverServerState.IsCurrent(project, Cover(Code(project), "Эмнэлэг · Дэд нүүр хуудас", "Removed")));
    }

    [Fact]
    public void TheDeferredCodeIsConfirmedOnlyWhenTheServerHoldsItCurrent()
    {
        ProjectWorkspace project = Project();
        string code = Code(project);
        var pending = new Dictionary<string, string> { [code] = code };

        IReadOnlyList<string> confirmed = StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
            pending,
            [Cover(code, "Эмнэлэг · Дэд нүүр хуудас")],
            [],
            deferredCodes: [code],
            deferredIsCurrentOnServer: component => StudioSubCoverServerState.IsCurrent(project, component));
        IReadOnlyList<string> stale = StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
            pending,
            [Cover(code, "Хуучин нэр · Дэд нүүр хуудас")],
            [],
            deferredCodes: [code],
            deferredIsCurrentOnServer: component => StudioSubCoverServerState.IsCurrent(project, component));

        Assert.Equal([code], confirmed);
        Assert.Empty(stale);
    }

    [Fact]
    public void TheMergeConfirmsWithTheServerState()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string components = File.ReadAllText(Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App", "ShellView.AlbumComponents.cs"));

        Assert.Contains("component => StudioSubCoverServerState.IsCurrent(state.Project, component));", components, StringComparison.Ordinal);
    }
}
