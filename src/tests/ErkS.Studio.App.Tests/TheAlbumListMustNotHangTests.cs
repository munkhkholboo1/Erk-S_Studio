using System.Security.Cryptography;
using System.Text;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Three separate defects that together froze a real machine for a quarter of
/// an hour and took its memory past five gigabytes.
///
/// 🔴 MEASURED ON THE OWNER'S RUNNING PROCESS, not inferred: the UI thread spent
/// 12 008 ms of 12 009 ms inside BCryptHashData; memory went 475 MB → 2 103 MB →
/// 4 941 MB with no ceiling; every other thread was idle. They had just exported
/// three sources, so the intake watcher was absorbing packages the whole time.
///
///   (в) every library change queued its OWN full refresh - unbounded fan-in
///   (б) each refresh rebuilt the album project once PER LIST ITEM
///   (а) building it hashes every visualisation payload in full, synchronously
///
/// They are written separately because fixing one hides the others: with the
/// queue coalesced the twenty-six-fold rebuild still runs, and with the rebuild
/// shared the full hashing still runs. Each is tested for what IT does.
/// </summary>
public sealed class TheAlbumListMustNotHangTests
{
    [Fact]
    public void MANYLibraryChangesProduceONERefresh()
    {
        // 🔴 (в) THE DIVERGENCE. The subscription posted a fresh OnLibraryChanged
        // for every change, and each one is minutes of work - so absorbing ten
        // packages queued ten of them and the queue filled faster than it
        // drained. Every waiting operation held its own object graph alive,
        // which is where the gigabytes went.
        string shell = ReadAppSource("ShellView.cs");

        Assert.DoesNotContain(
            "state.Library.Changed += () => dispatcher.BeginInvoke(new Action(OnLibraryChanged));",
            CodeOnly(shell),
            StringComparison.Ordinal);
        Assert.Contains("state.Library.Changed += QueueLibraryRefresh;", shell, StringComparison.Ordinal);

        string queue = MethodBody(shell, "private void QueueLibraryRefresh() =>");
        Assert.Contains("if (libraryRefreshPending)", queue, StringComparison.Ordinal);
        Assert.Contains("libraryChangedDuringRefresh = true;", queue, StringComparison.Ordinal);
    }

    [Fact]
    public void ACHANGEDuringARefreshIsNotLOST()
    {
        // Coalescing that drops the last change leaves the screen a version
        // behind - a quieter defect than the hang and harder to notice. What
        // arrives mid-flight is owed exactly one more pass.
        string queue = MethodBody(
            ReadAppSource("ShellView.cs"), "private void QueueLibraryRefresh() =>");

        Assert.Contains("do", queue, StringComparison.Ordinal);
        Assert.Contains("while (libraryChangedDuringRefresh);", queue, StringComparison.Ordinal);

        // And the latch is released however the pass ends, or one failure would
        // stop the screen updating for the rest of the session.
        Assert.Contains("finally", queue, StringComparison.Ordinal);
        Assert.Contains("libraryRefreshPending = false;", queue, StringComparison.Ordinal);
    }

    [Fact]
    public void NOLoopBuildsTheAlbumProjectPerITEM()
    {
        // 🔴 (б) THE MULTIPLIER. Building the album project reads and hashes
        // every visualisation payload, and three loops called it once per item -
        // twenty-six times over on the owner's list.
        //
        // Derived from the source: a call REPEATED over a collection must pass a
        // project the caller already built. A one-off question may build its
        // own - the site-context editor asks once and that is not the defect.
        //
        // 🔴 THE FIRST VERSION OF THIS RULE BANNED THE SELF-BUILDING OVERLOAD
        // OUTRIGHT and went red on that single call. A rule wider than the
        // defect makes the honest call site look like the guilty one, and the
        // cheapest way out is to weaken the rule until it catches nothing.
        int shared = 0;
        foreach (string file in new[] { "ShellView.Workspaces.cs", "ShellView.Portfolio.cs" })
        {
            string source = CodeOnly(ReadAppSource(file)).Replace("\r\n", "\n");
            for (int at = source.IndexOf("ResolveBuiltAlbumPage(", StringComparison.Ordinal);
                at >= 0;
                at = source.IndexOf("ResolveBuiltAlbumPage(", at + 1, StringComparison.Ordinal))
            {
                int lineStart = source.LastIndexOf('\n', at) + 1;
                string line = source[lineStart..source.IndexOf('\n', at)];
                if (line.Contains("private int?", StringComparison.Ordinal))
                    continue;

                bool sharesAProject =
                    line.Contains("Project)", StringComparison.Ordinal) ||
                    line.Contains("Project,", StringComparison.Ordinal);
                if (sharesAProject)
                {
                    shared++;
                    continue;
                }

                // Not sharing is only allowed where the call is not repeated.
                string above = source[Math.Max(0, lineStart - 400)..lineStart];
                bool repeated =
                    above.Contains("foreach (", StringComparison.Ordinal) ||
                    above.Contains("for (", StringComparison.Ordinal) ||
                    above.Contains(".Where(", StringComparison.Ordinal) ||
                    above.Contains(".Select(", StringComparison.Ordinal) ||
                    above.Contains(".Any(", StringComparison.Ordinal) ||
                    above.Contains(".FirstOrDefault(", StringComparison.Ordinal);

                Assert.False(
                    repeated,
                    file + " rebuilds the album project per item: " + line.Trim());
            }
        }

        // The positive control: «every repeated call shares» is also true of a
        // codebase where none of them do, and the whole fix was to make them.
        Assert.True(shared >= 4, "only " + shared + " call sites share a built project");
    }

    [Fact]
    public void THESharedOverloadDoesNotBuildOneOfItsOwn()
    {
        // The positive control for the rule above: passing a project in is
        // worth nothing if the method builds another one anyway.
        string body = MethodBody(
            ReadAppSource("ShellView.Workspaces.cs"),
            "    private int? ResolveBuiltAlbumPage(\n        AlbumPageWorkspaceItem selected,\n        AlbumProject project)");

        Assert.DoesNotContain("CreateAlbumBuildProject(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AVERIFIEDPayloadIsNotReHashedWhileTheFileSitsStill()
    {
        // 🔴 (а) THE COST OF ONE PASS. Even one rebuild hashes every payload in
        // full; the answer is remembered against the file as it was, so a second
        // ask costs nothing while nothing has moved.
        string path = Path.Combine(
            Path.GetTempPath(), "erks-payload-cache-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes("visualisation payload");
            File.WriteAllBytes(path, payload);
            string sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

            var image = new ProjectVisualizationImage { RelativePath = path, Sha256 = sha };
            Assert.True(StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload("", image));
            Assert.True(StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload("", image));

            // 🔴 AND THE ANSWER EXPIRES BY ITSELF. Rewriting the file with
            // different content must not be answered from the remembered
            // verdict - the key carries length and write time, so it cannot be.
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("a different payload entirely"));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));
            Assert.False(StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload("", image));
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void THESecondAskIsANSWEREDFromMemoryRatherThanRehashed()
    {
        // 🔴 SABOTAGE FOUND THIS TEST MISSING. Deleting the cache read left every
        // other assertion green - they check that the ANSWER is right, and
        // re-hashing gives the right answer too, slowly. Nothing proved the
        // expensive work was actually avoided, which is the entire point.
        //
        // Probed by changing the file's CONTENT while holding its length and
        // write time still: a reader that hashes again sees the new bytes and
        // says no; a reader answering from memory says yes. The second is what
        // must happen, and this is the only way to tell them apart from outside.
        //
        // It also documents the cache's limit honestly - an edit that keeps both
        // the length and the timestamp is NOT noticed. Real edits move one or
        // the other; a test that pretended otherwise would be claiming more than
        // the key can support.
        string path = Path.Combine(
            Path.GetTempPath(), "erks-payload-probe-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            byte[] payload = Encoding.UTF8.GetBytes("0123456789");
            File.WriteAllBytes(path, payload);
            DateTime stamp = File.GetLastWriteTimeUtc(path);
            string sha = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

            var image = new ProjectVisualizationImage { RelativePath = path, Sha256 = sha };
            Assert.True(StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload("", image));

            // Same length, same timestamp, different bytes.
            File.WriteAllBytes(path, Encoding.UTF8.GetBytes("9876543210"));
            File.SetLastWriteTimeUtc(path, stamp);

            Assert.True(
                StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload("", image),
                "the payload was hashed again instead of being answered from memory");
        }
        finally
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public void AMISSINGPayloadIsStillRefusedAndNotRemembered()
    {
        // The negative control. A file that is not there has no length and no
        // write time to key on, and answering «verified» from a stale entry
        // would let a missing image count as present.
        var image = new ProjectVisualizationImage
        {
            RelativePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".none"),
            Sha256 = new string('a', 64),
        };

        Assert.False(StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload("", image));
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
        int start = normalised.IndexOf(signature.Replace("\r\n", "\n"), StringComparison.Ordinal);
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
