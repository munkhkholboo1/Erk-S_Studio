using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Opening a project must not throw away the album it already built.
///
/// 🔴 IF IT DOES, THE WHOLE OF DECISION №13 IS WORTHLESS. The rule is that a
/// finished album survives closing Studio and restarting the machine - «хааж
/// нээх, комыг унтрааж асаах гэх мэт ямар ч үйлдэл нэгэнт шинэчилчихсэн альбумыг
/// дахин уншихад асуудал үүсгэх учиргүй». Opening the project is the first thing
/// that happens after both, and it is the one moment that reconciles sources,
/// site context and linked assets - each of which can clear the record.
///
/// So this is measured rather than reasoned about: the record either survives an
/// open or it does not.
/// </summary>
public sealed class OPENINGAProjectKeepsItsBuiltAlbumTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "erks-open-keeps-album-tests",
        Guid.NewGuid().ToString("N"));

    public OPENINGAProjectKeepsItsBuiltAlbumTests() => Directory.CreateDirectory(root);

    [Fact]
    public void APROJECTOpenedTwiceStillNamesItsBuiltAlbum()
    {
        // 🔴 THE MEASUREMENT DECISION №13 RESTS ON. Opening runs source metadata
        // upgrades, asset reconciliation and CityGen site reconciliation, and
        // several of those clear the built-album record when they report a
        // change. If any of them reports one on an untouched project, the album
        // record is destroyed on every open and nothing can be reused.
        string path = WriteProjectWithABuiltAlbum();

        using var state = new AppState();
        state.OpenProject(path);

        ProjectAlbumRecord album = state.Project.PrimaryAlbum;
        Assert.False(
            string.IsNullOrWhiteSpace(album.LastPdfPath),
            "opening the project cleared the built album's path");
        Assert.False(
            string.IsNullOrWhiteSpace(album.LastBuildFingerprint),
            "opening the project cleared what the album was built from");
        Assert.Equal("fingerprint-of-record", album.LastBuildFingerprint);
    }

    [Fact]
    public void THERecordSurvivesBeingOpenedAgainAndAgain()
    {
        // Once is not proof: a reconciler can be quiet on the first open and
        // report a change on the second, after the first one wrote something.
        string path = WriteProjectWithABuiltAlbum();

        for (int open = 1; open <= 3; open++)
        {
            using var state = new AppState();
            state.OpenProject(path);
            Assert.False(
                string.IsNullOrWhiteSpace(state.Project.PrimaryAlbum.LastBuildFingerprint),
                "the built-album record was cleared on open number " + open);
        }
    }

    private string WriteProjectWithABuiltAlbum()
    {
        string folder = Path.Combine(root, "P-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "drawings"));
        Directory.CreateDirectory(Path.Combine(folder, "output"));
        File.WriteAllText(Path.Combine(folder, "drawings", "plan.dwg"), "dwg");
        File.WriteAllText(Path.Combine(folder, "output", "album.pdf"), "%PDF-1.7 pretend");

        ProjectWorkspace project = ProjectWorkspaceStore.Create("KEEP-001", "Keeps its album");
        project.Sources.Add(new ProjectDesignSource
        {
            Name = "AutoCAD",
            Kind = DesignSourceKind.AutoCad,
            NativeDocumentPath = Path.Combine(folder, "drawings", "plan.dwg"),
        });
        project.PrimaryAlbum.LastPdfPath = Path.Combine("output", "album.pdf");
        project.PrimaryAlbum.LastPdfSha256 = "deadbeef";
        project.PrimaryAlbum.LastPageCount = 4;
        project.PrimaryAlbum.LastBuildFingerprint = "fingerprint-of-record";

        string path = Path.Combine(folder, ProjectWorkspace.DefaultFileName);
        ProjectWorkspaceStore.Save(project, path);
        return path;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
