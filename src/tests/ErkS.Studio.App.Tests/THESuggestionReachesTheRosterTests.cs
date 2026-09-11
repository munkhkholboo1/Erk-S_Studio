using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The last link of №25: the address actually fills the roster.
///
/// 🔴 A RULE WRITTEN, TESTED AND NEVER CALLED IS ITS OWN DEFECT. For four
/// commits `OfficialsProposal` existed with a suite behind it and no caller -
/// finished from every side except the one the owner uses. This file asserts the
/// wire, because a green proposal test says nothing about whether anything
/// presses it.
/// </summary>
public sealed class THESuggestionReachesTheRosterTests
{
    [Fact]
    public void THEProposalHasACALLERInTheProduct()
    {
        // 🔴 COUNTED IN THE APPLICATION, NOT IN THE TESTS. A proposal called only
        // from a test is a proposal nobody can reach.
        var callers = new List<string>();
        foreach (string file in Directory.EnumerateFiles(
            AppSourceDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file, Encoding.UTF8);
            if (text.Contains("OfficialsProposal.For(", StringComparison.Ordinal))
                callers.Add(Path.GetFileName(file));
        }

        Assert.True(
            callers.Count > 0,
            "nothing in the application offers the address's officials");
    }

    [Fact]
    public void THEButtonRunsTheWholeChain()
    {
        // address → key → directory → proposal → plan → rows. A link missing
        // anywhere leaves a button that looks like it worked.
        string body = MethodBody(
            ReadAppSource("ShellView.Approvals.cs"),
            "private void SuggestApprovalsFromAddress()");

        foreach (string link in new[]
        {
            "OfficialsLookupKey.For(",
            "StudioOfficialsDirectory.Live",
            "directory.Officials(unitCode)",
            "OfficialsProposal.For(",
            "OfficialsProposalPlan.For(",
            "ReplaceApprovalRows(ApprovalRosterKind.ConcurredBy",
            "ReplaceApprovalRows(ApprovalRosterKind.ReviewedBy",
        })
        {
            Assert.Contains(link, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ONLYTheRowsThePlanADMITTEDAreAdded()
    {
        // 🔴 THE PLAN'S REFUSALS MUST BE OBEYED, NOT JUST REPORTED. Adding every
        // proposed row and describing the refusals separately would put a third
        // reviewer where the sheet has two places - and say it had not.
        string body = MethodBody(
            ReadAppSource("ShellView.Approvals.cs"),
            "private void SuggestApprovalsFromAddress()");

        Assert.Contains("if (!outcome.WasAdded)", body, StringComparison.Ordinal);
        Assert.Contains("continue;", body, StringComparison.Ordinal);
    }

    [Fact]
    public void NOTHINGReachesTheProjectWithoutASave()
    {
        // 🔴 «СОНГОДОГ БАЙНА» - THE AUTOMATIC SITS UNDER A MANUAL CHOICE. The
        // rows land in the draft the person is editing; the project is written by
        // the ordinary save and by nothing else.
        string body = MethodBody(
            ReadAppSource("ShellView.Approvals.cs"),
            "private void SuggestApprovalsFromAddress()");

        Assert.DoesNotContain("ApplyConceptApprovalDraft", body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "state.Project.Foundation.ApprovalWorkflow.ConceptDesign =",
            body,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ANAddressWithNoDistrictSaysTHATRatherThanNOBODYFound()
    {
        // The two are acted on in different places: one is fixed on the project's
        // address, the other in the directory. Reporting the first as the second
        // sends somebody to hunt for a row that was never going to be consulted.
        string body = MethodBody(
            ReadAppSource("ShellView.Approvals.cs"),
            "private void SuggestApprovalsFromAddress()");

        Assert.Contains("unitCode.Length == 0", body, StringComparison.Ordinal);
        Assert.Contains("сум, дүүрэг сонгогдоогүй", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEDirectorysOwnTroublesReachThePersonToo()
    {
        // 🔴 «NOBODY FOUND» AND «THE DIRECTORY WOULD NOT READ» LOOK IDENTICAL
        // FROM THIS BUTTON, and only one of them is about the address. The
        // directory already distinguishes them; this screen must not re-merge
        // what it took three sentences to separate.
        string body = MethodBody(
            ReadAppSource("ShellView.Approvals.cs"),
            "private void SuggestApprovalsFromAddress()");

        Assert.Contains("directory.UnavailableReasonMn", body, StringComparison.Ordinal);
        Assert.Contains("directory.LossMn", body, StringComparison.Ordinal);
        Assert.Contains("OfficialsProposalPlan.DescribeMn(outcomes)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEButtonIsONSCREENAndWired()
    {
        // A method nothing calls is the same hole under another name.
        string source = ReadAppSource("ShellView.Approvals.cs");

        Assert.Contains("suggestFromAddressButton.Click", source, StringComparison.Ordinal);
        Assert.Contains(
            "root.Children.Add(suggestFromAddressButton);",
            source,
            StringComparison.Ordinal);
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

    private static string ReadAppSource(string fileName) =>
        File.ReadAllText(Path.Combine(AppSourceDirectory(), fileName), Encoding.UTF8);

    private static string AppSourceDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        Assert.Fail("the application's source folder was not found; this test reads it");
        return "";
    }
}
