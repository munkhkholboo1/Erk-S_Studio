using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The connection the user found missing: correcting a building assignment must
/// put the STORED page order back in step, not only the built PDF.
///
/// 🔴 THE DEFECT WAS NEVER IN THE RULE. The sequencer orders buildings by their
/// group's Order and does it correctly - measured, and now covered by
/// BuildingSectionOrderTests. What was missing was a caller:
/// UpdateBuildingComposition normalised the groups, marked the composition
/// pending, invalidated the built album and saved, and left Album.Pages in the
/// sequence the OLD assignments produced.
///
/// Studio's album view reads that stored sequence. So the user corrected a
/// building type, looked at the album, and saw their correction ignored - while
/// the built PDF was right, because the builder derives its own order. The two
/// disagreeing is what the whole report was about.
///
/// This is the shape that recurred all through 2026-09-07: the rule is right,
/// nobody asks it. A test of the rule stays green through it, which is why the
/// assertion here is aimed at the CALL SITES.
/// </summary>
public sealed class StoredAlbumOrderFollowsCompositionTests
{
    [Fact]
    public void EDITINGTheCompositionReordersTheSTOREDPages()
    {
        // The user's own route: the building-groups dialog calls this, and
        // nothing else on that route touches the page order.
        string state = ReadAppSource("AppState.cs");
        int method = state.IndexOf("public void UpdateBuildingComposition(", StringComparison.Ordinal);
        Assert.True(method > 0, "UpdateBuildingComposition was not found");

        string body = state[method..state.IndexOf("\n    }", method, StringComparison.Ordinal)];

        Assert.Contains("ReorderStoredAlbumPages();", body, StringComparison.Ordinal);

        // Ordered BEFORE the save, or the file keeps the old sequence and the
        // list is only right until the project is reopened.
        Assert.True(
            body.IndexOf("ReorderStoredAlbumPages();", StringComparison.Ordinal) <
                body.IndexOf("SaveProject();", StringComparison.Ordinal),
            "the pages must be reordered before the project is saved");
    }

    [Fact]
    public void EVERYPathThatChangesTheCompositionEndsAtTheSameReorder()
    {
        // Three routes change groups or assignments - the dialog, the cloud
        // sync, and the package record - and each one that forgets to reorder
        // recreates the same defect on its own route. Counted, because a fourth
        // route added later without the call is exactly how this came back.
        // Counted by INTENT rather than by arithmetic. The first version of this
        // assertion was a sum - calls plus inline uses minus one - and it broke
        // the moment a second caller was added, for a reason that had nothing to
        // do with what it was checking. That is the third time today a
        // source-reading test has failed on its own bookkeeping.
        string state = ReadAppSource("AppState.cs");

        // Exactly three places derive the order: the two older inline sites and
        // the shared helper. A fourth means somebody wrote their own again.
        Assert.Equal(3, Occurrences(state, "BuildingArchitectureConceptAlbumSequencer.OrderPages("));

        // And both of the routes that reach it do so through the helper, not by
        // copying it.
        Assert.Contains("ReorderStoredAlbumPages();", state, StringComparison.Ordinal);
        Assert.True(
            Occurrences(state, "ReorderStoredAlbumPages();") >= 2,
            "the composition edit and the heal must both go through the helper");
    }

    [Fact]
    public void THEOrderComesFromTheSEQUENCERRatherThanFromAnythingLocal()
    {
        // The rule stays in one place. A second ordering written here - even a
        // small one, even "just for the list" - is how the view and the build
        // came to disagree in the first place.
        string state = ReadAppSource("AppState.cs");
        int method = state.IndexOf("private void ReorderStoredAlbumPages()", StringComparison.Ordinal);
        Assert.True(method > 0, "the reorder helper was not found");

        string body = state[method..Math.Min(state.Length, method + 900)];

        Assert.Contains("BuildingArchitectureConceptAlbumSequencer.OrderPages(", body, StringComparison.Ordinal);
        Assert.Contains("Project.BuildingGroups", body, StringComparison.Ordinal);
        Assert.Contains("Project.SheetBuildingAssignments", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".OrderBy(", body, StringComparison.Ordinal);
        Assert.DoesNotContain(".Sort(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void APROJECTSavedWithTheOldSequenceHEALSItself()
    {
        // 🔴 PREVENTING IS NOT ENOUGH. Reordering when the composition is edited
        // keeps new work consistent and leaves every project already saved with
        // the stale sequence exactly as it was - curable only by asking the
        // person to reopen the dialog and press OK on assignments that are
        // already correct. Their data was never wrong; the order derived from it
        // was stale.
        string state = ReadAppSource("AppState.cs");
        string view = ReadAppSource("ShellView.Workspaces.cs");

        Assert.Contains("public bool EnsureStoredAlbumOrder()", state, StringComparison.Ordinal);
        Assert.Contains("state.EnsureStoredAlbumOrder()", view, StringComparison.Ordinal);
    }

    [Fact]
    public void THEHealDoesNothingOnNOInformation()
    {
        // The order is derived by resolving each page's sheet. Deriving it
        // against a library that has not been filled yet would reorder a project
        // on no information - the same class of mistake as writing an empty
        // location over a stored one, which cost the same user their address
        // earlier today.
        string state = ReadAppSource("AppState.cs");
        int method = state.IndexOf("public bool EnsureStoredAlbumOrder()", StringComparison.Ordinal);
        string body = state[method..Math.Min(state.Length, method + 900)];

        Assert.Contains("Library.Snapshot().Count == 0", body, StringComparison.Ordinal);
        Assert.Contains("Album.Pages.Count == 0", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEHealIsSILENTWhenThereIsNothingToCorrect()
    {
        // It runs every time the album is shown, so it must not save - or
        // announce - on a project that is already in order. A message on every
        // visit teaches people to ignore it.
        string state = ReadAppSource("AppState.cs");
        int method = state.IndexOf("public bool EnsureStoredAlbumOrder()", StringComparison.Ordinal);
        string body = state[method..Math.Min(state.Length, method + 1200)];

        Assert.Contains("before.SequenceEqual(after)", body, StringComparison.Ordinal);
        Assert.True(
            body.IndexOf("return false;", body.IndexOf("SequenceEqual", StringComparison.Ordinal), StringComparison.Ordinal) > 0,
            "an unchanged order must return false before saving");
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
