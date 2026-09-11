using System.Text;
using System.Text.RegularExpressions;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Every table on the concept cover can be typed into.
///
/// 🔴 THIS CLOSES A HOLE I OPENED MYSELF. ХЯНАСАН was drawn on the sheet and
/// stored nowhere; `1b2bcf6` gave it a list, and for one commit it had storage
/// and no way in - the same defect mirrored. A model that promises a field the
/// screen cannot fill is the quieter half of the pair, because nothing looks
/// broken: the table simply prints empty for ever.
///
/// 🔴 AND THE WIRING IS DERIVED FROM THE ENUM, NOT LISTED. A roster kind reaches
/// seven switches - rows, panel, minimum, maximum, refusal, label, and the
/// enum itself - and forgetting one is a runtime throw on a screen somebody is
/// using. The check walks the declaration instead of trusting a list that would
/// be updated by the same hand that forgot.
/// </summary>
public sealed class EVERYRosterHasAWayInTests
{
    [Fact]
    public void EVERYRosterKindIsHandledByEVERYSwitchThatTakesOne()
    {
        string source = ReadAppSource("ShellView.Approvals.cs");
        IReadOnlyList<string> kinds = RosterKinds(source);

        Assert.True(kinds.Count >= 4, "only " + kinds.Count + " roster kinds were found in the declaration");

        foreach (string method in new[]
        {
            "RowsFor",
            "PanelFor",
            "MinimumFor",
            "MaximumFor",
            "RefusesBeyondMaximum",
            "RosterLabel",
        })
        {
            string body = SwitchBody(source, method);
            foreach (string kind in kinds)
            {
                Assert.True(
                    body.Contains("ApprovalRosterKind." + kind, StringComparison.Ordinal),
                    $"«{method}» has no case for «{kind}» - the screen throws when somebody reaches it");
            }
        }
    }

    [Fact]
    public void THEReviewedRosterIsBOUNDReadBackAndCompared()
    {
        // A panel that is drawn but never bound shows an empty table on a project
        // that has rows; one that is bound but never captured loses what was
        // typed on save. Both are silent, so both are asserted.
        string source = ReadAppSource("ShellView.Approvals.cs");

        Assert.Contains(
            "ApprovalRosterKind.ReviewedBy,\n            state.Project.Foundation.ApprovalWorkflow.ConceptDesign.ReviewedBy);",
            source.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
        Assert.Contains(
            "ReviewedBy = ReadApprovalEntries(reviewedByEditorRows),",
            source,
            StringComparison.Ordinal);

        // 🔴 AND COMPARED, OR THE SAVE PROMPT NEVER FIRES. A roster the change
        // check ignores is one somebody edits and is never asked to save - the
        // work is lost at the moment they close the project, with nothing said.
        Assert.Contains(
            "EntriesDiffer(current.ReviewedBy, draft.ReviewedBy)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THEReviewedRosterHasAButtonAndASectionOfItsOwn()
    {
        string source = ReadAppSource("ShellView.Approvals.cs");

        Assert.Contains("addReviewedByButton.Click", source, StringComparison.Ordinal);
        Assert.Contains("root.Children.Add(reviewedByRowsPanel);", source, StringComparison.Ordinal);
        Assert.Contains(
            "BuildApprovalColumnHeader(ApprovalRosterKind.ReviewedBy)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TWOPlacesIsACEILINGHereAndAWarningNextDoor()
    {
        // 🔴 THE TWO TABLES REFUSE DIFFERENTLY, AND THE REASON IS THE DRAWING.
        // ХЯНАСАН prints at the measured two rows, so a third has nowhere to go.
        // ЗӨВШИЛЦСӨН divides a fixed height by whatever it holds, so a seventh
        // party is cramped rather than impossible - and refusing there would
        // leave a project that really has seven unable to produce a cover at all.
        Assert.Equal(2, ProjectApprovalRosterLimits.MaxReviewedBy);
        Assert.Equal(0, ProjectApprovalRosterLimits.MinReviewedBy);
        Assert.Equal(
            ProjectApprovalRosterLimits.MaxReviewedBy,
            ConceptCoverSheetGrid.MeasuredUpperRowHeightsForTwo.Count);

        string source = ReadAppSource("ShellView.Approvals.cs");
        string body = SwitchBody(source, "RefusesBeyondMaximum");

        Assert.Matches(
            new Regex(@"ApprovalRosterKind\.ReviewedBy\s*=>\s*true", RegexOptions.None, TimeSpan.FromSeconds(5)),
            body);
        Assert.Matches(
            new Regex(@"ApprovalRosterKind\.ConcurredBy\s*=>\s*false", RegexOptions.None, TimeSpan.FromSeconds(5)),
            body);
    }

    [Fact]
    public void THECapIsONSCREENBecauseTheStoreDoesNOTTruncate()
    {
        // 🔴 THE TWO HALVES HAVE TO AGREE OR SOMEBODY LOSES A ROW. The store
        // keeps a third row on purpose - dropping it at save time would destroy
        // typing with nothing on screen having said so - which is exactly why the
        // refusal has to happen where the row is entered. If the store ever
        // starts truncating, this pairing is where to look.
        var roster = new ConceptDesignApprovalRoster();
        for (var index = 0; index < ProjectApprovalRosterLimits.MaxReviewedBy + 1; index++)
            roster.ReviewedBy.Add(new ProjectApprovalEntry { OrganizationName = "Мөр " + index });

        roster.Normalize();

        Assert.Equal(
            ProjectApprovalRosterLimits.MaxReviewedBy + 1,
            roster.ReviewedBy.Count);
    }

    /// <summary>The members of the ApprovalRosterKind enum, read from its declaration.</summary>
    private static IReadOnlyList<string> RosterKinds(string source)
    {
        string normalised = source.Replace("\r\n", "\n");
        int at = normalised.IndexOf("private enum ApprovalRosterKind", StringComparison.Ordinal);
        Assert.True(at > 0, "the ApprovalRosterKind declaration was not found");

        int open = normalised.IndexOf('{', at);
        int close = normalised.IndexOf('}', open);
        Assert.True(open > 0 && close > open, "the ApprovalRosterKind body was not found");

        return normalised[(open + 1)..close]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line => line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            .ToList();
    }

    /// <summary>The body of a switch expression keyed on the roster kind.</summary>
    private static string SwitchBody(string source, string methodName)
    {
        string normalised = source.Replace("\r\n", "\n");
        int at = normalised.IndexOf(
            methodName + "(ApprovalRosterKind kind) => kind switch",
            StringComparison.Ordinal);
        Assert.True(at > 0, methodName + " is no longer a switch on the roster kind");

        int close = normalised.IndexOf("\n    };", at, StringComparison.Ordinal);
        Assert.True(close > at, "the end of " + methodName + " was not found");
        return normalised[at..close];
    }

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
