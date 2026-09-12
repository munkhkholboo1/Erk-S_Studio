using System.Text;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A render overwritten at its source reaches the album.
///
/// 🔴 THE OWNER'S WAY OF WORKING, IN THEIR WORDS: «би төслийн харагдах байдал
/// хангалтгүй байсан ч рендерлээд хуудсанд оруулчихна. дараа нь тэр хангалтгүй
/// хэмжээнд байгаа зурагнуудаа сайжруулсаар байх болно. Тиймээс би эх үүсвэр дээрх
/// зурагнуудыг нэрээр нь дарж хадгалаад байхад альбуманд шинэчлэгдэж байна уу?»
///
/// 🔴 MEASURED 2026-09-12: IT DID NOT. And the failure was self-sealing. The
/// rebuild decision computes its fingerprint from UN-reconciled state, so the
/// stored sha256 was still the old one, the fingerprint did not move, «nothing
/// changed» was the answer - and the reconciliation that would have noticed lives
/// INSIDE the draw that never happened. The only path that could see the new file
/// decided it was unnecessary without looking.
/// </summary>
public sealed class ANEWRenderReachesTheAlbumTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "erks-relink-tests", Guid.NewGuid().ToString("N"));

    public ANEWRenderReachesTheAlbumTests() => Directory.CreateDirectory(root);

    [Fact]
    public void ANOVERWRITTENRenderIsSEENByTheSurvey()
    {
        // 🔴 THE POSITIVE CONTROL. A survey that always answered «nothing moved»
        // would pass every other assertion in this file and prove nothing.
        (ProjectWorkspace project, string projectPath, string sourcePath) = Project();

        LinkedSourceSurvey before =
            ProjectAssetSourceReconciler.SurveyLinkedVisualizationSources(project, projectPath);
        Assert.False(before.AnyMoved, "an untouched render was reported as moved");

        Overwrite(sourcePath, bytes: 4096);

        LinkedSourceSurvey after =
            ProjectAssetSourceReconciler.SurveyLinkedVisualizationSources(project, projectPath);
        Assert.True(after.AnyMoved, "the overwritten render was not noticed");
        Assert.Equal(1, after.MovedCount);
    }

    [Fact]
    public void ASOURCEThatIsGONEIsNotReportedASMOVED()
    {
        // 🔴 A THIRD STATE ON PURPOSE. The owner's renders live on D:\\Cloud Work\\…;
        // an unplugged share read as «changed» would redraw every album whenever
        // that drive was offline - and redraw from the stored copy, achieving
        // nothing but the wait.
        (ProjectWorkspace project, string projectPath, string sourcePath) = Project();

        File.Delete(sourcePath);

        LinkedSourceSurvey survey =
            ProjectAssetSourceReconciler.SurveyLinkedVisualizationSources(project, projectPath);

        Assert.False(survey.AnyMoved);
        Assert.Equal(0, survey.MovedCount);
        Assert.Equal(1, survey.UnreachableCount);
    }

    [Fact]
    public void AMALFORMEDPathIsANSWEREDNotThrown()
    {
        // 🔴 A QUESTION THAT THROWS IS WORSE THAN ONE WITH NO ANSWER. This is asked
        // from inside the rebuild decision, so an exception here stops the album
        // being drawable at all - and the trigger would be something as ordinary as
        // a stored path from another machine.
        (ProjectWorkspace project, string projectPath, _) = Project();

        foreach (string bad in new[] { "", "   ", "::not a path::", "\0bad" })
        {
            project.Visualizations.Images.Add(new ProjectVisualizationImage
            {
                OwnerProjectId = project.ProjectId,
                LinkedSourcePath = bad,
                OriginalFileName = "bad.png",
            });
        }

        LinkedSourceSurvey survey =
            ProjectAssetSourceReconciler.SurveyLinkedVisualizationSources(project, projectPath);

        // Whatever it decides about them, it decides - it does not throw.
        Assert.True(survey.MovedCount >= 0);
        Assert.True(survey.UnreachableCount >= 0);
    }


    [Fact]
    public void ACORRUPTPROJECTPathThrowsHEREAndIsCaughtBYTHECALLER()
    {
        // 🔴 THE LIMIT, WRITTEN AS A TEST RATHER THAN TRUSTED. Probed directly
        // 2026-09-12: a project path containing a NUL makes this throw
        // ArgumentException out of Path.GetFullPath, while a merely nonsensical
        // one («::bad::») does not. So the library reports a malformed argument -
        // which is right, it IS a programming error - and AppState's wrapper
        // declines to let that make the album undrawable.
        //
        // Two concerns, not a duplicated gate: the library says «this argument is
        // wrong», the application says «and I will not die of it». Asserting the
        // throw here is what stops somebody «hardening» this method and silently
        // turning a corrupt path into «nothing moved».
        (ProjectWorkspace project, _, _) = Project();

        Assert.Throws<ArgumentException>(() =>
            ProjectAssetSourceReconciler.SurveyLinkedVisualizationSources(
                project,
                "C:\\\0bad\\x"));
    }

    [Fact]
    public void THESurveyWRITESNothing()
    {
        // «A question about whether to do work must not itself be the work» is the
        // rule the whole rebuild decision rests on. This question is asked from
        // inside that decision, so it must not touch the record.
        (ProjectWorkspace project, string projectPath, string sourcePath) = Project();
        ProjectVisualizationImage image = project.Visualizations.Images[0];

        string sha = image.Sha256;
        long size = image.SizeBytes;
        DateTimeOffset? when = image.LinkedSourceLastWriteTimeUtc;
        int version = image.Version;

        Overwrite(sourcePath, bytes: 8192);
        ProjectAssetSourceReconciler.SurveyLinkedVisualizationSources(project, projectPath);

        Assert.Equal(sha, image.Sha256);
        Assert.Equal(size, image.SizeBytes);
        Assert.Equal(when, image.LinkedSourceLastWriteTimeUtc);
        Assert.Equal(version, image.Version);
    }

    [Fact]
    public void THESurveyAndTheRECONCILERShareONEComparison()
    {
        // 🔴 THE WORSE BUG THIS AVOIDS. A second staleness rule would let the
        // decision say «moved» while the reconciler's own cache check said
        // «unchanged, skip» - and the album would report a rebuild that changed
        // nothing. Asserted by behaviour: whatever the survey calls moved, the
        // reconciler actually updates.
        (ProjectWorkspace project, string projectPath, string sourcePath) = Project();
        Overwrite(sourcePath, bytes: 2048);

        Assert.True(
            ProjectAssetSourceReconciler
                .SurveyLinkedVisualizationSources(project, projectPath)
                .AnyMoved);

        ProjectAssetSourceReconciliationResult result =
            ProjectAssetSourceReconciler.ReconcileProject(project, projectPath);

        Assert.True(
            result.UpdatedVisualizationCount > 0,
            "the survey said moved and the reconciler did nothing - two rules, not one");

        // And afterwards the survey is quiet, or the pair would never settle.
        Assert.False(
            ProjectAssetSourceReconciler
                .SurveyLinkedVisualizationSources(project, projectPath)
                .AnyMoved,
            "the survey still reports a move after reconciliation - the build would never settle");
    }

    [Fact]
    public void RECONCILINGBringsTheNEWContentIn()
    {
        // Not «the album was redrawn» - the NEW IMAGE. A redraw from the old
        // stored copy is the failure this whole fix exists to prevent.
        (ProjectWorkspace project, string projectPath, string sourcePath) = Project();
        ProjectVisualizationImage image = project.Visualizations.Images[0];
        string oldSha = image.Sha256;
        int oldVersion = image.Version;

        Overwrite(sourcePath, bytes: 12288);
        ProjectAssetSourceReconciler.ReconcileProject(project, projectPath);

        Assert.NotEqual(oldSha, image.Sha256);
        Assert.True(image.Version > oldVersion, "the version did not move");
        Assert.Equal(12288, image.SizeBytes);
    }

    [Fact]
    public void THEDecisionASKSTheSurveyBEFORETheFingerprint()
    {
        // 🔴 ORDER MATTERS: the fingerprint is computed from the record, and the
        // record is exactly what a source overwrite does NOT touch. Asked after,
        // the answer would already be «nothing changed».
        string body = MethodBody(
            ReadAppSource("ShellView.cs"),
            "private AlbumRebuildDecision DecideWhetherToDrawAlbum(StudioWorkspaceOperation origin)");

        int survey = body.IndexOf("LinkedVisualizationSourcesHaveMoved()", StringComparison.Ordinal);
        int fingerprint = body.IndexOf("AlbumBuildFingerprint.Of(", StringComparison.Ordinal);

        Assert.True(survey > 0, "the decision no longer asks whether a render moved");
        Assert.True(fingerprint > survey, "the fingerprint is computed before the survey");
        Assert.Contains("AlbumRebuildReason.LinkedSourceMoved", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEBUILDReconcilesBecauseOfTheSurveyWHATEVERTheOrigin()
    {
        // 🔴 MASTER'S POINT, AND IT IS THE OTHER HALF OF THE FIX. Only
        // ExplicitAlbumEdit reconciles by origin; a sync or a source refresh draws
        // from the copy the project already holds. Deciding «the render moved» and
        // then drawing the OLD one is worse than today, because the album would
        // report a rebuild that changed nothing and we would believe it.
        string shell = ReadAppSource("ShellView.cs");

        Assert.Contains(
            "StudioRefreshSyncOperationPolicy.ShouldReconcileLinkedProjectAssets(\n                    origin) ||\n                LinkedVisualizationSourcesHaveMoved();",
            shell.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void THESurveyIsTakenONCEPerPass()
    {
        // Two reads could straddle a file being saved: the album drawn for a reason
        // the build then declined to act on. One answer per pass.
        string shell = ReadAppSource("ShellView.cs");

        Assert.Contains(
            "linkedSourceSurveyThisPass ??= state.SurveyLinkedVisualizationSources();",
            shell,
            StringComparison.Ordinal);
        Assert.Contains("linkedSourceSurveyThisPass = null;", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void EVERYAlbumUpdateIsCoveredByOneHalfOfTheFixOrTheOther()
    {
        // 🔴 A FIX THE OWNER'S PATHS DO NOT REACH CHANGES NOTHING - and this
        // project has produced that shape three times in a day. Counted from the
        // source rather than listed: every call either takes the DEFAULT origin,
        // where the survey gate sits before the fingerprint, or names an origin
        // that always draws, where the reconcile clause carries it instead.
        //
        // A future origin that is neither would be covered by neither, and the
        // symptom would be an owner's render quietly not arriving.
        var uncovered = new List<string>();
        var callers = 0;
        var explicitOrigins = new List<string>();

        foreach (string file in Directory.EnumerateFiles(
            AppSourceDirectory(), "*.cs", SearchOption.AllDirectories))
        {
            string[] lines = File.ReadAllText(file, Encoding.UTF8)
                .Replace("\r\n", "\n")
                .Split('\n');

            for (var index = 0; index < lines.Length; index++)
            {
                if (!lines[index].Contains("UpdateAlbum(", StringComparison.Ordinal))
                    continue;
                if (lines[index].Contains("private bool UpdateAlbum(", StringComparison.Ordinal))
                    continue;

                callers++;

                // The origin, when named, is within the next few lines of the call.
                string window = string.Join(
                    "\n",
                    lines.Skip(index).Take(6));
                int named = window.IndexOf(
                    "origin: StudioWorkspaceOperation.",
                    StringComparison.Ordinal);
                if (named < 0)
                    continue;   // default origin - the survey gate covers it

                string origin = new string(window[(named + 33)..]
                    .TakeWhile(char.IsLetter)
                    .ToArray());
                explicitOrigins.Add(origin);

                bool alwaysDraws = origin is nameof(StudioWorkspaceOperation.CloudSync)
                    or nameof(StudioWorkspaceOperation.SourceRefresh);
                if (!alwaysDraws)
                    uncovered.Add(Path.GetFileName(file) + ":" + (index + 1) + " (" + origin + ")");
            }
        }

        Assert.True(callers >= 20, "only " + callers + " album updates were found to check");
        Assert.NotEmpty(explicitOrigins);
        Assert.True(
            uncovered.Count == 0,
            "these album updates are covered by neither half of the fix: " +
            string.Join(", ", uncovered));

        // And the two named origins really are the always-draw ones, read from the
        // policy rather than trusted from this list.
        foreach (string origin in explicitOrigins.Distinct(StringComparer.Ordinal))
        {
            Assert.True(
                StudioAlbumRebuildPolicy.AlwaysDraws(
                    Enum.Parse<StudioWorkspaceOperation>(origin)),
                origin + " is named at a call site but does not always draw");
        }
    }

    [Fact]
    public void THEReasonHasASentenceTheOwnerCanRead()
    {
        // The user-visible half: «эх рендер шинэчлэгдсэн тул альбом дахин
        // зурагдсан» is a different message from «slow again».
        string words = StudioAlbumRebuildPolicy.DescribeMn(AlbumRebuildReason.LinkedSourceMoved);
        Assert.False(string.IsNullOrWhiteSpace(words));

        var record = new AlbumDrawRecord();
        record.Record(drew: true, AlbumRebuildReason.LinkedSourceMoved.ToString(), DateTimeOffset.UnixEpoch);

        string sentence = StudioAlbumDrawSentence.For(record);
        Assert.Contains(words, sentence, StringComparison.Ordinal);
        Assert.Contains("рендер", sentence, StringComparison.Ordinal);
    }

    /// <summary>A project with one linked render, already reconciled once.</summary>
    private (ProjectWorkspace Project, string ProjectPath, string SourcePath) Project()
    {
        string projectFolder = Path.Combine(root, "project");
        Directory.CreateDirectory(projectFolder);
        string projectPath = Path.Combine(projectFolder, ProjectWorkspace.DefaultFileName);

        string sourceFolder = Path.Combine(root, "renders");
        Directory.CreateDirectory(sourceFolder);
        string sourcePath = Path.Combine(sourceFolder, "Scene 1.png");
        Overwrite(sourcePath, bytes: 1024);

        var project = new ProjectWorkspace();
        project.Visualizations.Images.Add(new ProjectVisualizationImage
        {
            OwnerProjectId = project.ProjectId,
            LinkedSourcePath = sourcePath,
            OriginalFileName = "Scene 1.png",
        });

        // The first pass is what a real import does: it stores a copy and records
        // the sha, the size and the source's write time.
        ProjectAssetSourceReconciler.ReconcileProject(project, projectPath);
        return (project, projectPath, sourcePath);
    }

    /// <summary>
    /// Writes a PNG of the requested byte length. Different lengths mean different
    /// content AND a different size, which is what the comparison reads.
    /// </summary>
    private static void Overwrite(string path, int bytes)
    {
        byte[] png = BuildTruecolourPng(Math.Max(2, bytes / 512), 8);
        var padded = new byte[Math.Max(png.Length, bytes)];
        png.CopyTo(padded, 0);
        File.WriteAllBytes(path, padded);

        // The comparison reads the write time as well as the size; on a fast
        // machine two writes can land in the same clock tick.
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(bytes));
    }

    private static byte[] BuildTruecolourPng(int width, int height)
    {
        var raw = new List<byte>();
        for (var y = 0; y < height; y++)
        {
            raw.Add(0);
            for (var x = 0; x < width; x++)
            {
                raw.Add((byte)((x * 7) % 256));
                raw.Add((byte)((y * 11) % 256));
                raw.Add(96);
            }
        }

        using var idat = new MemoryStream();
        using (var deflate = new System.IO.Compression.ZLibStream(
            idat, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw.ToArray());
        }

        var png = new List<byte> { 137, 80, 78, 71, 13, 10, 26, 10 };
        var header = new List<byte>();
        header.AddRange(BigEndian(width));
        header.AddRange(BigEndian(height));
        header.AddRange(new byte[] { 8, 2, 0, 0, 0 });
        png.AddRange(PngChunk("IHDR"u8.ToArray(), header.ToArray()));
        png.AddRange(PngChunk("IDAT"u8.ToArray(), idat.ToArray()));
        png.AddRange(PngChunk("IEND"u8.ToArray(), []));
        return png.ToArray();
    }

    private static byte[] PngChunk(byte[] tag, byte[] data)
    {
        byte[] body = [.. tag, .. data];
        return [.. BigEndian(data.Length), .. body, .. BigEndian(unchecked((int)Crc32(body)))];
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static byte[] BigEndian(int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
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
