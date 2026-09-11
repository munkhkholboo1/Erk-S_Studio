using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The two things that tell a person whether they are up to date must read the
/// same buckets.
///
/// 🔴 ONE READ ONE BUCKET AND THE OTHER READ THE SAME ONE. The cloud indicator
/// painted GREEN with «✓ Шинэчлэлт алга» while two changed sources sat unsent,
/// and in the same run the refresh report said «Таны оруулга: илгээх зүйл
/// байсангүй» - because both counted album components alone. Meanwhile the sync
/// preview dialog, which counts every kind, listed five things to send. One
/// state, three readers, two answers.
/// </summary>
public sealed class BothReadersCountEveryBucketTests
{
    [Fact]
    public void THEIndicatorCountsEVERYKindOfUnsentWork()
    {
        // 🔴 THE EXACT EXPRESSION THAT PAINTED IT GREEN, BANNED. It read
        // PendingAlbumComponentCodes and called the answer «what is waiting».
        string body = MethodBody(
            ReadAppSource("ShellView.AlbumRefresh.cs"),
            "private CloudAlbumStatus CurrentCloudAlbumStatus()");

        Assert.Contains("StudioPendingWork.Of(", body, StringComparison.Ordinal);
        Assert.Contains("pending.Total", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEContributionLineCountsEVERYKindToo()
    {
        // The report and the indicator have to agree, and they only agree by
        // asking the same question of the same type. Counting album components
        // here is what produced «илгээх зүйл байсангүй» beside a dialog that had
        // just named five.
        string body = MethodBody(
            ReadAppSource("ShellView.AlbumRefresh.cs"), "private async Task RefreshAlbumAsync()");

        Assert.Contains(
            "int pendingBefore = StudioPendingWork.Of(state.Project).Total;",
            body,
            StringComparison.Ordinal);
        Assert.Contains(
            "int pendingAfter = StudioPendingWork.Of(state.Project).Total;",
            body,
            StringComparison.Ordinal);

        // And neither counts the one bucket on its own any more.
        Assert.DoesNotContain(
            "int pendingBefore = (cloud.PendingAlbumComponentCodes ?? []).Count;",
            CodeOnly(body),
            StringComparison.Ordinal);
    }

    [Fact]
    public void NOReaderOfWHATISWAITINGCountsASingleBucketAlone()
    {
        // 🔴 DERIVED: any place that reads PendingAlbumComponentCodes.Count and
        // treats it as «how much is waiting» is the defect returning. The split
        // between sendable and blocked components legitimately reads that list -
        // it is about components specifically - so the rule is about the two
        // methods that answer the GENERAL question.
        string source = CodeOnly(ReadAppSource("ShellView.AlbumRefresh.cs"));

        foreach (string method in new[]
        {
            "private CloudAlbumStatus CurrentCloudAlbumStatus()",
            "private async Task RefreshAlbumAsync()",
        })
        {
            string body = MethodBody(source, method);
            Assert.DoesNotContain(
                "PendingAlbumComponentCodes ?? []).Count",
                body,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void THEIndicatorDoesNotHASHToAnswer()
    {
        // 🔴 THE INDICATOR REPAINTS CONSTANTLY. Asking «could this device send
        // each piece» reads and hashes files - the cost that froze the window -
        // so the general count must stay structural. The sendable/blocked split
        // keeps its own cache and is a different question.
        string body = MethodBody(
            ReadAppSource("ShellView.AlbumRefresh.cs"),
            "private CloudAlbumStatus CurrentCloudAlbumStatus()");

        Assert.DoesNotContain("CreateAlbumBuildProject(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("CloudSyncPreviewPlanner.Build(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("HasVerifiedPayload(", body, StringComparison.Ordinal);
    }

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
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
