using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The one action reports the work it did, and names the work it skipped.
///
/// 🔴 THE SKIP WAS DRESSED AS A RESULT AND IT COST A PERSON A MORNING. Reading
/// every source package takes five and a half seconds, so a cheap survey decides
/// whether the expensive read runs. When it said no, the orchestrator built
/// <c>SourceRefreshOutcome.Completed(state.Project.Sources.Count, 0)</c> - a
/// count of sources presented as a count of sources CHECKED - and the report
/// said «Эх үүсвэр: 3 шалгав, 0 өөрчлөгдсөн».
///
/// The owner had changed exactly three sources and the project had exactly
/// three. The coincidence turned a wrong line into a convincing one.
/// </summary>
public sealed class TheCloudButtonReportsWhatItDidTests
{
    [Fact]
    public void THESkipBranchCannotBuildAnOUTCOMEAtAll()
    {
        // 🔴 THE FIX IS STRUCTURAL, NOT A BETTER SENTENCE. The invented number
        // came from a SourceRefreshOutcome constructed on the skip path; that
        // path no longer constructs one, so there is nothing for a future
        // wording change to put back.
        string body = MethodBody(
            ReadAppSource("ShellView.AlbumRefresh.cs"), "private async Task RefreshAlbumAsync()");

        Assert.DoesNotContain("SourceRefreshOutcome.Completed(", CodeOnly(body), StringComparison.Ordinal);
        Assert.Contains("AlbumRefreshReport.SourcesNotChecked(", body, StringComparison.Ordinal);

        // The expensive read still runs when something IS waiting - the skip is
        // a performance decision, not a removal.
        Assert.Contains("await CheckForSourceUpdatesAsync()", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THESourceCountIsNeverPassedAsACheckedCount()
    {
        // 🔴 THE EXACT EXPRESSION THAT LIED, BANNED BY NAME. It read
        // «Sources.Count» into a field the report prints as «N шалгав», and a
        // reader of either half alone would have found nothing wrong with it.
        string body = MethodBody(
            ReadAppSource("ShellView.AlbumRefresh.cs"), "private async Task RefreshAlbumAsync()");

        Assert.DoesNotContain("state.Project.Sources.Count", CodeOnly(body), StringComparison.Ordinal);
    }

    [Fact]
    public void ALocalProjectIsNotToldAboutACloudItDoesNotHave()
    {
        // The same defect, found by sweeping the other three report lines
        // instead of fixing only the one that was reported: an unlinked project
        // claimed «өгөх шинэ хэсэг байсангүй» and «өөрчлөгдөөгүй, дахин
        // татаагүй» for steps that cannot run at all without a cloud.
        string body = MethodBody(
            ReadAppSource("ShellView.AlbumRefresh.cs"), "private async Task RefreshAlbumAsync()");

        Assert.Equal(2, Occurrences(body, "AlbumRefreshReport.SkippedForLocalProject("));
        Assert.DoesNotContain("AlbumRefreshReport.CloudFetched(false, \"\")", body, StringComparison.Ordinal);
    }

    [Fact]
    public void REDRAWINGOwnComponentsStillFollowsAREALChange()
    {
        // The work «Бүрэн дахин байгуулах» used to do is folded into this
        // action and runs when the sources actually changed. Splitting the read
        // into two branches must not have left that reading a variable that is
        // now always zero on one of them.
        string body = MethodBody(
            ReadAppSource("ShellView.AlbumRefresh.cs"), "private async Task RefreshAlbumAsync()");

        int assigned = body.IndexOf("changedCount = sources.ChangedCount;", StringComparison.Ordinal);
        int used = body.IndexOf("if (changedCount > 0)", StringComparison.Ordinal);
        Assert.True(assigned > 0, "the real change count is no longer captured");
        Assert.True(used > assigned, "the redraw no longer follows the captured count");
        Assert.Contains("MarkOwnAlbumComponentsForRerender()", body, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static string CodeOnly(string source) =>
        string.Join(
            "\n",
            source.Split('\n').Where(line =>
            {
                string text = line.TrimStart();
                return !text.StartsWith("//", StringComparison.Ordinal) &&
                    !text.StartsWith("*", StringComparison.Ordinal);
            }));

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return source[start..end];
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
