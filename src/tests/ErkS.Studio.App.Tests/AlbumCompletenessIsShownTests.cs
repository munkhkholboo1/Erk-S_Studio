using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// 🔴 THE CONNECTION, NOT THE RULE. AlbumCompletenessReport is covered by its
/// own unit tests; what those cannot see is whether anything asks it. That is
/// exactly how the fact it reports came to be missing in the first place -
/// BuildingPageTypeOrder.IsUnclassified was written, documented and tested, and
/// had no caller outside its own test file for months while a user stared at a
/// page they could not explain.
///
/// So this file asserts the wire.
/// </summary>
public sealed class AlbumCompletenessIsShownTests
{
    [Fact]
    public void THEAlbumWorkspaceASKSWhatItCouldNotPlace()
    {
        string view = ReadAppSource("ShellView.Workspaces.cs");

        // 🔴 THE CALL MUST NOT BE COMMENTED OUT, and saying so is not pedantry:
        // a mutation that turned the call into `// ReportAlbumCompleteness();`
        // left the first version of this test GREEN, because "contains" is
        // happy with a call that will never run. That is the same failure the
        // whole file is about - an assertion matching something other than what
        // it means - caught here by sabotage rather than by a user.
        // Matched WITH its indentation and no comment marker, which needs no
        // escape sequence and so cannot be mangled by the tooling that
        // writes this file.
        Assert.Contains("        ReportAlbumCompleteness();", view, StringComparison.Ordinal);
        Assert.DoesNotContain("// ReportAlbumCompleteness();", view, StringComparison.Ordinal);
        Assert.Contains("AlbumCompletenessReport.Create(", view, StringComparison.Ordinal);
    }

    [Fact]
    public void THEAnswerReachesASCREENRatherThanAVariable()
    {
        // A report computed and dropped is the same as no report. The notice
        // has to reach SetStatus.
        string view = ReadAppSource("ShellView.Workspaces.cs");
        int method = view.IndexOf("private void ReportAlbumCompleteness()", StringComparison.Ordinal);
        Assert.True(method > 0, "the reporter is gone");

        string body = view[method..view.IndexOf("\n    private string lastAlbumCompletenessNotice", method, StringComparison.Ordinal)];

        Assert.Contains("SetStatus(notice);", body, StringComparison.Ordinal);
        Assert.Contains("UnplacedNoticeMn", body, StringComparison.Ordinal);
        Assert.Contains("EmptySlotsNoticeMn", body, StringComparison.Ordinal);
    }

    [Fact]
    public void REPEATINGTheSameNoticeIsSuppressed()
    {
        // The album list refreshes on every selection change. A notice that
        // reappears each time trains a person to read past it, and then the one
        // that matters goes past unread too.
        string view = ReadAppSource("ShellView.Workspaces.cs");
        int method = view.IndexOf("private void ReportAlbumCompleteness()", StringComparison.Ordinal);
        string body = view[method..view.IndexOf("\n    private string lastAlbumCompletenessNotice", method, StringComparison.Ordinal)];

        Assert.Contains("lastAlbumCompletenessNotice", body, StringComparison.Ordinal);
        Assert.True(
            body.IndexOf("Equals(lastAlbumCompletenessNotice", StringComparison.Ordinal) <
                body.IndexOf("SetStatus(notice);", StringComparison.Ordinal),
            "the repeat check must come before the message");
    }

    [Fact]
    public void ACLEANAlbumRESETSTheMemoryRatherThanStayingSilentForever()
    {
        // Otherwise fixing the last unclassified page and then introducing a
        // new one would say nothing: the remembered text would still match.
        string view = ReadAppSource("ShellView.Workspaces.cs");
        int method = view.IndexOf("private void ReportAlbumCompleteness()", StringComparison.Ordinal);
        string body = view[method..view.IndexOf("\n    private string lastAlbumCompletenessNotice", method, StringComparison.Ordinal)];

        int emptyBranch = body.IndexOf("IsNullOrWhiteSpace(notice)", StringComparison.Ordinal);
        Assert.True(emptyBranch > 0, "there is no silent branch");
        Assert.Contains(
            "lastAlbumCompletenessNotice = \"\";",
            body[emptyBranch..],
            StringComparison.Ordinal);
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8).Replace("\r\n", "\n");
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
