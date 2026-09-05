using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The ЗӨВШИЛЦСӨН section existed as a heading and a paragraph of text with no
/// way to add a row - a section that described a list nobody could fill.
///
/// The rules that decide where a row goes, how many are allowed and what the
/// section is called were all written as "ApprovedBy ? a : b". A third roster
/// meeting a two-valued question does not fail: it takes the second branch, so
/// its rows would have been written into ЗӨВШӨӨРӨЛЦСӨН's list, bounded by
/// ЗӨВШӨӨРӨЛЦСӨН's limits, and reported under ЗӨВШӨӨРӨЛЦСӨН's name. Nothing
/// would have thrown.
///
/// These read the view's source because the view is WPF: what it builds cannot
/// be asked at test time, only what it is written to build.
/// </summary>
public sealed class ConcurredByEditorReachabilityTests
{
    [Fact]
    public void TheSectionHasRowsAndAWayToAddThem()
    {
        string view = ReadApprovalsView();

        Assert.Contains("concurredByRowsPanel", view, StringComparison.Ordinal);
        Assert.Contains("addConcurredByButton", view, StringComparison.Ordinal);
        Assert.Contains(
            "AddApprovalRow(ApprovalRosterKind.ConcurredBy)",
            view,
            StringComparison.Ordinal);
    }

    [Fact]
    public void WhatIsTypedIsREADBackAndCompared()
    {
        // A panel that collects rows nobody reads is the same defect wearing
        // the opposite face: the user fills it in and the album never sees it.
        string view = ReadApprovalsView();

        Assert.Contains("ConcurredBy = ReadApprovalEntries(concurredByEditorRows)", view, StringComparison.Ordinal);
        Assert.Contains("EntriesDiffer(current.ConcurredBy, draft.ConcurredBy)", view, StringComparison.Ordinal);
        Assert.Contains(
            "ReplaceApprovalRows(\n            ApprovalRosterKind.ConcurredBy",
            view.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RowsFor")]
    [InlineData("PanelFor")]
    [InlineData("MinimumFor")]
    [InlineData("MaximumFor")]
    [InlineData("RosterLabel")]
    public void EveryPerRosterRuleNamesTheThirdRosterEXPLICITLY(string ruleName)
    {
        // The point is not that a third arm exists but that these are switches
        // at all. A ternary answers a two-valued question and goes on answering
        // it silently after the question grows a third value.
        string view = ReadApprovalsView().Replace("\r\n", "\n");
        int start = view.IndexOf(ruleName + "(ApprovalRosterKind kind)", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{ruleName} was renamed; check this test with it");

        string body = view[start..(start + 500)];
        Assert.Contains("ApprovalRosterKind.ConcurredBy =>", body, StringComparison.Ordinal);
        Assert.DoesNotContain("kind == ApprovalRosterKind.ApprovedBy ?", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TheElevationFlagIsHIDDENForConcurringBodies()
    {
        // «ХЯНАСАН» - a table of its own on the concept cover - and «ХЯНАВ» - a
        // flag on a ЗӨВШӨӨРӨЛЦСӨН row - are different things that read alike.
        // Offering the flag here would let somebody tick a box that changes
        // nothing, and believe they had put the body on the cover.
        string view = ReadApprovalsView();

        Assert.Contains(
            "kind == ApprovalRosterKind.ConcurredBy",
            view,
            StringComparison.Ordinal);
        Assert.Contains("Visibility.Collapsed", view, StringComparison.Ordinal);
    }

    private static string ReadApprovalsView()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", "ShellView.Approvals.cs");
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail("ShellView.Approvals.cs was not found; this test reads it from source");
        return "";
    }
}
