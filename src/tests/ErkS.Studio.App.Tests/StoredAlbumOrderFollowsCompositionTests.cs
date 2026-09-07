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
        string state = ReadAppSource("AppState.cs");

        int reorderCalls = Occurrences(state, "ReorderStoredAlbumPages();");
        int inlineReorders = Occurrences(state, "BuildingArchitectureConceptAlbumSequencer.OrderPages(");

        Assert.True(reorderCalls >= 1, "the dialog route must reorder");
        Assert.Equal(3, reorderCalls + inlineReorders - 1);
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
