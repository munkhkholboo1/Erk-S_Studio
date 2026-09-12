using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The store sweep removes orphans and nothing else - checked on real files.
///
/// 🔴 THIS IS DELETION CODE, SO THE TESTS USE A REAL FOLDER. A rule tested only on
/// string lists proves the rule; it does not prove that the thing which deletes
/// obeys it. The failure being guarded against is the owner's renders going, and
/// that failure happens on a disk.
///
/// 🔴 AND THE NEED WAS CREATED BY THE FIX BESIDE IT. The store names each copy by
/// content hash, so an overwritten render leaves its predecessor behind - about
/// 1.8 GB per round at 26 images. Until the album started noticing overwrites, the
/// new copies were never even made.
/// </summary>
public sealed class THESWEEPRemovesOnlyOrphansTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "erks-sweep-tests", Guid.NewGuid().ToString("N"));

    public THESWEEPRemovesOnlyOrphansTests() => Directory.CreateDirectory(root);

    [Fact]
    public void ANORPHANGoesAndAReferencedCopySTAYS()
    {
        (ProjectWorkspace project, string projectPath, string store) = Project();
        string kept = Copy(store, "kept.png", 1024);
        string orphan = Copy(store, "orphan.png", 2048);
        Reference(project, projectPath, kept);

        VisualizationStoreSweepResult result =
            StudioVisualizationStoreMaintenance.Sweep(project, projectPath);

        Assert.True(File.Exists(kept), "a referenced copy was deleted");
        Assert.False(File.Exists(orphan), "the orphan was left behind");
        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(2048, result.RemovedBytes);
        Assert.Equal(1, result.KeptCount);
        Assert.Equal("", result.RefusalMn);
    }

    [Fact]
    public void ANEMPTYReferenceSetDELETESNOTHINGOnDisk()
    {
        // 🔴 THE ANSWER THAT MUST NEVER BE «DELETE EVERYTHING», proved on files
        // rather than on a list. ImagesForProject answers with an empty list when
        // the source carries another project's id - an ordinary mismatch - and a
        // sweep keyed to that would take every render the owner has.
        (ProjectWorkspace project, string projectPath, string store) = Project();
        string one = Copy(store, "one.png", 512);
        string two = Copy(store, "two.png", 512);

        VisualizationStoreSweepResult result =
            StudioVisualizationStoreMaintenance.Sweep(project, projectPath);

        Assert.True(File.Exists(one));
        Assert.True(File.Exists(two));
        Assert.Equal(0, result.RemovedCount);
        Assert.Equal(
            VisualizationStoreCleanup.NothingReferencedMn,
            result.RefusalMn);
        Assert.False(string.IsNullOrWhiteSpace(result.RefusalMn));
    }

    [Fact]
    public void ARECORDOwnedByANOTHERProjectIdStillProtectsItsCopy()
    {
        // 🔴 THE PARTIAL SUBSET, WHICH THE EMPTY-SET REFUSAL CANNOT CATCH.
        // ImagesForProject gates on the SOURCE's owner id first - a mismatch there
        // returns an empty list, and the refusal above stops that. But once the
        // source DOES belong to this project it goes on to filter IMAGE BY IMAGE on
        // image.OwnerProjectId, and an image whose own id differs is dropped while
        // its siblings are kept. That reference set is not empty, so no refusal
        // fires, and every dropped record's copy reads as an orphan.
        //
        // Records like that are ordinary: Normalize fills an EMPTY image owner id
        // from the project, but leaves a non-empty different one exactly as it
        // found it - which is what a project saved under a new id, or images
        // carried over from another project's file, produces.
        //
        // ⚠ AND THIS IS WHY THE COUNT CROSS-CHECK WAS NOT BUILT. Comparing the
        // reference count with the project's declared count derives both numbers
        // from the SAME enumeration, so narrowing that enumeration moves both and
        // the comparison still agrees. The hazard is in the code, so the guard has
        // to be a test - this one, plus the source lock beside it.
        (ProjectWorkspace project, string projectPath, string store) = Project();

        // The configured state, in which the narrowing would be PARTIAL rather
        // than total - an unconfigured source fails the gate above and is already
        // covered by the empty-set refusal.
        project.Visualizations.OwnerProjectId = project.ProjectId;

        string mine = Copy(store, "mine.png", 512);
        string theirs = Copy(store, "theirs.png", 1024);
        string orphan = Copy(store, "orphan.png", 256);
        Reference(project, projectPath, mine);
        Reference(project, projectPath, theirs).OwnerProjectId =
            "b7e1c0d94f1a4e2f8c3d5a6b7e8f9012";

        VisualizationStoreSweepResult result =
            StudioVisualizationStoreMaintenance.Sweep(project, projectPath);

        Assert.True(
            File.Exists(theirs),
            "a record whose owner id differs from the project's lost its copy");
        Assert.True(File.Exists(mine));
        Assert.False(File.Exists(orphan), "the orphan was left behind");
        Assert.Equal(1, result.RemovedCount);
        Assert.Equal(2, result.KeptCount);
        Assert.Equal("", result.RefusalMn);
    }

    [Fact]
    public void NOTHINGOutsideTheStoreIsTouched()
    {
        // The sweep enumerates ONE folder, so a neighbouring file - a project file,
        // a source package, anything the project keeps elsewhere - is out of its
        // reach by construction. Asserted because «delete the orphans» is one
        // careless Directory.EnumerateFiles from meaning the whole project folder.
        //
        // ⚠ WHAT THIS DOES NOT PROVE, SAID PLAINLY: the sweep also re-checks
        // IsInside at the moment of deletion, and that check is UNREACHABLE from
        // here. The paths it examines come from enumerating the store, so none of
        // them can contain «..». It guards a future change in how that set is
        // built - the same shape as the AppState catch reported earlier today: a
        // guard my tests cannot construct an input for, kept because the thing it
        // protects is the owner's renders.
        (ProjectWorkspace project, string projectPath, string store) = Project();
        string inside = Copy(store, "inside.png", 256);
        string orphan = Copy(store, "orphan.png", 128);
        Reference(project, projectPath, inside);

        string sibling = Path.Combine(Path.GetDirectoryName(store)!, "notes.txt");
        File.WriteAllText(sibling, "kept");
        string faraway = Path.Combine(root, "precious.png");
        File.WriteAllBytes(faraway, new byte[64]);

        VisualizationStoreSweepResult result =
            StudioVisualizationStoreMaintenance.Sweep(project, projectPath);

        Assert.Equal(1, result.RemovedCount);
        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(inside));
        Assert.True(File.Exists(sibling), "a file one folder up was deleted");
        Assert.True(File.Exists(faraway), "a file outside the project was deleted");

        // 🔴 AND THE SCOPE ITSELF, BECAUSE THE OUTCOME ALONE COULD NOT PIN IT. Two
        // guards protect this - «walk only the store» and «check IsInside before
        // deleting» - and sabotage showed each masking the other's removal: widen
        // the walk to the whole project folder and the inside-check still saves the
        // files, so nothing went red. Defence in depth is right here; leaving the
        // scope unmeasured was not. The sweep must have LOOKED AT the store's two
        // files and nothing else.
        Assert.Equal(2, result.ConsideredCount);
    }

    [Fact]
    public void ANEXCLUDEDImageKeepsItsCopyOnDisk()
    {
        // «Хуудаснаас хасах» keeps the file and only drops it from the layout - the
        // button says so. The sweep reads RECORDS, not page membership, so an
        // excluded image survives; this proves the wiring passes records.
        (ProjectWorkspace project, string projectPath, string store) = Project();
        string excluded = Copy(store, "excluded.png", 4096);
        ProjectVisualizationImage image = Reference(project, projectPath, excluded);
        image.IsIncludedInAlbum = false;

        StudioVisualizationStoreMaintenance.Sweep(project, projectPath);

        Assert.True(File.Exists(excluded), "an excluded image lost its copy");
    }

    [Fact]
    public void AMISSINGStoreFolderIsNOTAFailure()
    {
        // A fresh project has no store yet. Nothing to sweep is not a fault and
        // must not report as one.
        string projectFolder = Path.Combine(root, "fresh");
        Directory.CreateDirectory(projectFolder);
        string projectPath = Path.Combine(projectFolder, ProjectWorkspace.DefaultFileName);

        VisualizationStoreSweepResult result =
            StudioVisualizationStoreMaintenance.Sweep(new ProjectWorkspace(), projectPath);

        Assert.Equal(0, result.RemovedCount);
        Assert.Equal("", result.RefusalMn);
    }

    [Fact]
    public void ACORRUPTProjectPathIsANSWEREDNotThrown()
    {
        // A tidy-up that throws takes the album build with it. The store simply
        // goes unswept, which costs disk and nothing else.
        VisualizationStoreSweepResult result =
            StudioVisualizationStoreMaintenance.Sweep(new ProjectWorkspace(), "C:\\\0bad\\x");

        Assert.Equal(0, result.RemovedCount);
    }

    [Fact]
    public void THESweepRunsAFTERReconciliationAndAFTERTheSave()
    {
        // 🔴 ORDER IS THE WHOLE CORRECTNESS OF THE WIRING. Before the record is
        // repointed the OLD copy is still referenced and the new one does not
        // exist, so a sweep there finds nothing; and a sweep before the save could
        // delete a copy the project on disk still names, if the save then failed.
        string source = ReadAppSource("AppState.cs");
        string body = MethodBody(source, "public AlbumProject CreateAlbumBuildProject(");

        int save = body.IndexOf("SaveProject();", StringComparison.Ordinal);
        int sweep = body.IndexOf(
            "StudioVisualizationStoreMaintenance.Sweep(",
            StringComparison.Ordinal);

        Assert.True(save > 0, "the reconciliation no longer saves");
        Assert.True(sweep > save, "the sweep runs before the save");
    }

    [Fact]
    public void THESweepDoesNOTRunWhenNothingReconciled()
    {
        // 🔴 A SWEEP ON EVERY BUILD WOULD READ THE WHOLE STORE FOR NOTHING - and
        // «nothing changed» is the ordinary answer. It is inside the branch that
        // only runs when reconciliation actually moved something.
        string body = MethodBody(
            ReadAppSource("AppState.cs"),
            "public AlbumProject CreateAlbumBuildProject(");

        int branch = body.IndexOf("if (ApplyAssetReconciliation(", StringComparison.Ordinal);
        int sweep = body.IndexOf(
            "StudioVisualizationStoreMaintenance.Sweep(",
            StringComparison.Ordinal);

        Assert.True(branch > 0, "the reconciliation branch is gone");
        Assert.True(sweep > branch, "the sweep escaped the changed-something branch");
    }

    [Fact]
    public void THERemovalIsWrittenDownWHEREItHappensAndSAVED()
    {
        // 🔴 RECORDED AT THE SWEEP, NOT AT THE CALLERS. Half a dozen places ask
        // for a build project and any of them may reconcile; a deletion written down
        // by each caller is a deletion the next caller added forgets to write down.
        //
        // 🔴 AND SAVED AFTERWARDS, WHICH NEEDS ITS OWN SAVE. The save above has
        // to come BEFORE the sweep, so a save that failed could never leave the
        // project on disk naming a file already deleted. The price of that order is
        // that the trace is not in it - and a trace gone by the next restart is no
        // trace of a deletion at all.
        string body = MethodBody(
            ReadAppSource("AppState.cs"),
            "public AlbumProject CreateAlbumBuildProject(");

        int sweep = body.IndexOf(
            "StudioVisualizationStoreMaintenance.Sweep(",
            StringComparison.Ordinal);
        int record = body.IndexOf("RecordStoreSweep(", StringComparison.Ordinal);
        int save = body.IndexOf("SaveProject();", record, StringComparison.Ordinal);

        Assert.True(sweep > 0, "the sweep is gone");
        Assert.True(record > sweep, "the sweep result is no longer written to the record");
        Assert.True(save > record, "the trace is never saved, so it dies with the session");
    }

    /// <summary>A project whose store folder exists and is empty.</summary>
    private (ProjectWorkspace Project, string ProjectPath, string Store) Project()
    {
        string projectFolder = Path.Combine(root, "project");
        string store = Path.Combine(projectFolder, "sources", "visualizations", "images");
        Directory.CreateDirectory(store);
        return (
            new ProjectWorkspace(),
            Path.Combine(projectFolder, ProjectWorkspace.DefaultFileName),
            store);
    }

    private static string Copy(string store, string name, int bytes)
    {
        string path = Path.Combine(store, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    private static ProjectVisualizationImage Reference(
        ProjectWorkspace project,
        string projectPath,
        string fullPath)
    {
        var image = new ProjectVisualizationImage
        {
            OwnerProjectId = project.ProjectId,
            RelativePath = ProjectWorkspacePaths.ToRelativePath(projectPath, fullPath),
            OriginalFileName = Path.GetFileName(fullPath),
        };
        project.Visualizations.Images.Add(image);
        return image;
    }

    private static string MethodBody(string source, string signature)
    {
        string normalised = source.Replace("\r\n", "\n");
        int start = normalised.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = normalised.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return normalised[start..end];
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, System.Text.Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
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
