using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The «has anything changed» answer has to hold on the REAL build input.
///
/// 🔴 A FINGERPRINT THAT NEVER MATCHES IS A DEAD FEATURE THAT LOOKS ALIVE. The
/// guard it feeds would simply never fire, every album would keep rebuilding
/// exactly as it does today - ten minutes and 9.7 GB on the owner's project - and
/// every unit test over a hand-made project would still be green.
///
/// So the question is asked here, against AppState's own
/// <c>CreateAlbumBuildProject</c>, which is what the builder actually consumes.
/// That method CONSTRUCTS objects on each call, and this model is full of
/// <c>DateTimeOffset.UtcNow</c> and <c>Guid.NewGuid()</c> defaults - so whether the
/// value is stable is a fact about the product, not something to reason out.
/// </summary>
public sealed class TheRealBuildInputIsStableTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "erks-build-fingerprint-tests",
        Guid.NewGuid().ToString("N"));

    public TheRealBuildInputIsStableTests() => Directory.CreateDirectory(root);

    [Fact]
    public void TWOBuildInputsFromONEOpenProjectCarryTheSameFingerprint()
    {
        // 🔴 THE MEASUREMENT THE WHOLE FIX RESTS ON. Nothing changed between the
        // two calls, so the album would be drawn identically - and the value that
        // says so must agree.
        using AppState state = OpenSampleProject();

        string first = AlbumBuildFingerprint.Of(
            state.CreateAlbumBuildProject(reconcileLinkedProjectAssets: false));
        string second = AlbumBuildFingerprint.Of(
            state.CreateAlbumBuildProject(reconcileLinkedProjectAssets: false));

        Assert.Equal(first, second);
    }

    [Fact]
    public void CLOSINGAndReopeningTheProjectKeepsTheFingerprint()
    {
        // 🔴 THE OWNER SAID THIS IN SO MANY WORDS: «хааж нээх, комыг унтрааж асаах
        // гэх мэт ямар ч үйлдэл нэгэнт шинэчилчихсэн альбумыг дахин уншихад
        // асуудал үүсгэх учиргүй». A value that only survives inside one process
        // cannot answer that - it has to survive the project being closed and
        // opened again.
        string path = WriteSampleProject();

        string before;
        using (var first = new AppState())
        {
            first.OpenProject(path);
            before = AlbumBuildFingerprint.Of(
                first.CreateAlbumBuildProject(reconcileLinkedProjectAssets: false));
        }

        using var second = new AppState();
        second.OpenProject(path);

        Assert.Equal(
            before,
            AlbumBuildFingerprint.Of(
                second.CreateAlbumBuildProject(reconcileLinkedProjectAssets: false)));
    }

    [Fact]
    public void ANEDITMovesTheFingerprintOnTheRealInputToo()
    {
        // The positive control. A fingerprint that is stable because it reads
        // nothing would pass both tests above and stop the album updating at all,
        // which is a worse defect than the one being fixed.
        using AppState state = OpenSampleProject();
        string before = AlbumBuildFingerprint.Of(
            state.CreateAlbumBuildProject(reconcileLinkedProjectAssets: false));

        state.Project.Identity.Name += " (edited)";

        Assert.NotEqual(
            before,
            AlbumBuildFingerprint.Of(
                state.CreateAlbumBuildProject(reconcileLinkedProjectAssets: false)));
    }

    private AppState OpenSampleProject()
    {
        var state = new AppState();
        state.OpenProject(WriteSampleProject());
        return state;
    }

    private string WriteSampleProject()
    {
        string folder = Path.Combine(root, "FP-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(Path.Combine(folder, "drawings"));
        string drawing = Path.Combine(folder, "drawings", "plan.dwg");
        File.WriteAllText(drawing, "dwg");

        ProjectWorkspace project = ProjectWorkspaceStore.Create("FP-001", "Fingerprint");
        project.Sources.Add(new ProjectDesignSource
        {
            Name = "AutoCAD",
            Kind = DesignSourceKind.AutoCad,
            NativeDocumentPath = drawing,
        });
        project.BuildingGroups.Add(new ProjectBuildingGroup { Id = "b1", Name = "Блок А" });
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
