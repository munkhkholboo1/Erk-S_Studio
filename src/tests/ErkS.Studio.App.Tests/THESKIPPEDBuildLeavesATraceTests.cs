using System.Text;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// «The album did not rebuild» is a claim about work that did not happen, and
/// until now nothing recorded it.
///
/// 🔴 THE ONLY EVIDENCE WAS A STOPWATCH. Fast meant «it skipped», slow meant «it
/// drew» - a guess that gets worse on a faster machine and on a smaller album,
/// and that nobody can check after the fact. The owner reports a delay hours
/// later; by then the feeling is gone and the file timestamp says when a build
/// ENDED, not whether one ran.
///
/// A build leaves a file behind. A build correctly SKIPPED leaves nothing at
/// all - which is exactly why the skip is the thing that has to be written down.
/// </summary>
public sealed class THESKIPPEDBuildLeavesATraceTests
{
    [Fact]
    public void ASKIPIsRecordedWithItsReason()
    {
        var record = new AlbumDrawRecord();
        record.Record(
            drew: false,
            AlbumRebuildReason.NothingChanged.ToString(),
            new DateTimeOffset(2026, 9, 12, 10, 0, 0, TimeSpan.Zero));

        Assert.NotNull(record.DecidedAtUtc);
        Assert.False(record.Drew);
        Assert.Equal("NothingChanged", record.ReasonCode);

        string sentence = StudioAlbumDrawSentence.For(record);
        Assert.Contains("ДАХИН ЗУРАГДААГҮЙ", sentence, StringComparison.Ordinal);
        Assert.Contains("юу ч өөрчлөгдөөгүй", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void ACHANGEDFingerprintIsRecordedASADraw()
    {
        // 🔴 THE POSITIVE CONTROL, AND MASTER NAMED IT: an observation that only
        // ever says «skipped» proves nothing at all. A changed project must make
        // this say «drew», in so many words.
        AlbumRebuildDecision decision = StudioAlbumRebuildPolicy.Decide(
            StudioWorkspaceOperation.ExplicitAlbumEdit,
            currentFingerprint: "aaaa",
            builtFingerprint: "bbbb",
            builtAlbumIsPresent: true);

        Assert.True(decision.MustDraw);
        Assert.Equal(AlbumRebuildReason.FingerprintChanged, decision.Reason);

        var record = new AlbumDrawRecord();
        record.Record(decision.MustDraw, decision.Reason.ToString(), DateTimeOffset.UnixEpoch);

        string sentence = StudioAlbumDrawSentence.For(record);
        Assert.Contains("дахин зурагдсан", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain("ДАХИН ЗУРАГДААГҮЙ", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void THEONETIMERebuildOfAddingAFieldIsVISIBLEThroughThis()
    {
        // 🔴 MASTER'S SECOND MEASUREMENT, MADE OBSERVABLE. Adding
        // ConceptDesign.ReviewedBy changed every existing project's fingerprint,
        // forcing exactly one redraw each. That redraw is what this record must
        // show - and if it does not, the observation is in the wrong place.
        var project = new AlbumProject { Name = "Хэмжилт" };
        string before = AlbumBuildFingerprint.Of(project);

        project.ApprovalWorkflow.ConceptDesign.ReviewedBy.Add(
            new ProjectApprovalEntry { OrganizationName = "Хот байгуулалтын алба" });
        string after = AlbumBuildFingerprint.Of(project);

        Assert.NotEqual(before, after);

        AlbumRebuildDecision decision = StudioAlbumRebuildPolicy.Decide(
            StudioWorkspaceOperation.ExplicitAlbumEdit,
            after,
            before,
            builtAlbumIsPresent: true);

        Assert.True(decision.MustDraw, "the one-time rebuild is invisible to this record");
        Assert.Equal(AlbumRebuildReason.FingerprintChanged, decision.Reason);

        // And the second open, with nothing further changed, must skip - or the
        // record would say «drew» for ever and prove nothing again.
        Assert.False(
            StudioAlbumRebuildPolicy.Decide(
                StudioWorkspaceOperation.ExplicitAlbumEdit,
                after,
                after,
                builtAlbumIsPresent: true).MustDraw);
    }

    [Fact]
    public void EVERYSentenceNamesITSOwnReason()
    {
        // 🔴 THE OWNER'S NEXT STUDIO OPEN IS THE REASON THIS IS ASSERTED. Adding
        // ConceptDesign.ReviewedBy changed every project's fingerprint, so their
        // 2 GB album redraws ONCE - and a line that said only «дахин зурагдсан»
        // would read as «slow again» to somebody who has spent a week on exactly
        // that complaint. «Төсөл өөрчлөгдсөн тул альбом дахин зурагдсан» is a
        // different message: it says what happened and that it was expected.
        //
        // Derived over every reason and BOTH branches - a sentence that named its
        // reason when skipping and not when drawing would fail exactly where it
        // matters, which is the drawing one.
        foreach (AlbumRebuildReason reason in Enum.GetValues<AlbumRebuildReason>())
        {
            string words = StudioAlbumRebuildPolicy.DescribeMn(reason);

            foreach (bool drew in new[] { true, false })
            {
                var record = new AlbumDrawRecord();
                record.Record(drew, reason.ToString(), DateTimeOffset.UnixEpoch);

                string sentence = StudioAlbumDrawSentence.For(record);
                Assert.True(
                    sentence.Contains(words, StringComparison.Ordinal),
                    $"«{reason}» ({(drew ? "drew" : "skipped")}) does not say why: {sentence}");
            }
        }
    }

    [Fact]
    public void THEOwnersONETIMERedrawReadsASEXPECTEDNotASSlowAgain()
    {
        // The exact line the owner will meet, end to end: the decision the policy
        // makes for a changed fingerprint, recorded, and read back.
        AlbumRebuildDecision decision = StudioAlbumRebuildPolicy.Decide(
            StudioWorkspaceOperation.ExplicitAlbumEdit,
            currentFingerprint: "after-reviewedby",
            builtFingerprint: "before-reviewedby",
            builtAlbumIsPresent: true);

        var record = new AlbumDrawRecord();
        record.Record(decision.MustDraw, decision.Reason.ToString(), DateTimeOffset.UnixEpoch);
        record.RecordDrawFinished(DateTimeOffset.UnixEpoch, seconds: 96.4);

        string sentence = StudioAlbumDrawSentence.For(record);

        Assert.Contains("төсөл өөрчлөгдсөн тул", sentence, StringComparison.Ordinal);
        Assert.Contains("дахин зурагдсан", sentence, StringComparison.Ordinal);

        // And how long it took, so «slow» has a number beside it rather than a
        // memory of waiting.
        //
        // 🔴 THE SEPARATOR IS THE MACHINE'S, NOT THIS TEST'S. Written as
        // «96.4 секунд» this passes here and goes red on a colleague whose
        // culture writes «96,4» - a false red for no product reason, which is
        // how a suite starts collecting exemptions instead of trust. The product
        // formats in the reader's culture like every other Mongolian line in it,
        // so the expectation is formed the same way.
        string seconds = 96.4.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
        Assert.Contains(seconds + " секунд", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void EVERYReasonHasASentenceAndTheyAreDistinct()
    {
        // 🔴 DERIVED FROM THE ENUM. A reason with no sentence prints as a blank
        // where the explanation goes, which reads as «no reason» - the one thing
        // this record exists to rule out.
        var sentences = new List<string>();
        foreach (AlbumRebuildReason reason in Enum.GetValues<AlbumRebuildReason>())
        {
            string text = StudioAlbumRebuildPolicy.DescribeMn(reason);
            Assert.False(string.IsNullOrWhiteSpace(text), $"«{reason}» has no sentence");
            sentences.Add(text);
        }

        Assert.Equal(sentences.Count, sentences.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void EVERYDecisionPathProducesADecidedReason()
    {
        // Each branch of the policy, derived from its own inputs rather than
        // listed - a path that produced no reason would be recorded as an empty
        // string and read as «never asked».
        var cases = new (StudioWorkspaceOperation Origin, string Now, string Built, bool Present)[]
        {
            (StudioWorkspaceOperation.CloudSync, "a", "a", true),
            (StudioWorkspaceOperation.SourceRefresh, "a", "a", true),
            (StudioWorkspaceOperation.ExplicitAlbumEdit, "a", "a", false),
            (StudioWorkspaceOperation.ExplicitAlbumEdit, "", "a", true),
            (StudioWorkspaceOperation.ExplicitAlbumEdit, "a", "", true),
            (StudioWorkspaceOperation.ExplicitAlbumEdit, "a", "b", true),
            (StudioWorkspaceOperation.ExplicitAlbumEdit, "a", "a", true),
        };

        var seen = new HashSet<AlbumRebuildReason>();
        foreach ((StudioWorkspaceOperation origin, string now, string built, bool present) in cases)
        {
            AlbumRebuildDecision decision =
                StudioAlbumRebuildPolicy.Decide(origin, now, built, present);
            Assert.True(Enum.IsDefined(decision.Reason));
            seen.Add(decision.Reason);
        }

        // Exactly one of them is a skip, and it is the only one.
        Assert.Contains(AlbumRebuildReason.NothingChanged, seen);
        Assert.False(
            StudioAlbumRebuildPolicy.Decide(
                StudioWorkspaceOperation.ExplicitAlbumEdit, "a", "a", true).MustDraw);
    }

    [Fact]
    public void THEDecisionAndTheREASONCannotDisagree()
    {
        // 🔴 ONE SOURCE, NOT TWO. MustDraw is the decision's own answer, so a
        // screen cannot be told «drew» while the rule decided otherwise.
        foreach (StudioWorkspaceOperation origin in Enum.GetValues<StudioWorkspaceOperation>())
        {
            foreach (bool present in new[] { true, false })
            {
                foreach ((string now, string built) in new[] { ("a", "a"), ("a", "b"), ("", "b") })
                {
                    Assert.Equal(
                        StudioAlbumRebuildPolicy.MustDraw(origin, now, built, present),
                        StudioAlbumRebuildPolicy.Decide(origin, now, built, present).MustDraw);
                }
            }
        }
    }

    [Fact]
    public void ASKIPDoesNOTErasTheDrawItSkipped()
    {
        // «Nothing changed» is the ordinary answer and happens many times between
        // builds. If each one cleared the last-drawn time, the record would say
        // «never drawn» about a project whose album is sitting on disk.
        var drewAt = new DateTimeOffset(2026, 9, 10, 8, 0, 0, TimeSpan.Zero);
        var record = new AlbumDrawRecord();
        record.Record(drew: true, "FingerprintChanged", drewAt);
        record.RecordDrawFinished(drewAt, seconds: 42);

        record.Record(drew: false, "NothingChanged", drewAt.AddDays(2));

        Assert.Equal(drewAt, record.LastDrewAtUtc);
        Assert.Equal(42, record.LastDrewSeconds);
        Assert.Contains("Сүүлд", StudioAlbumDrawSentence.For(record), StringComparison.Ordinal);
    }

    [Fact]
    public void NEVERDecidedIsItsOwnStateAndNotASkip()
    {
        // «Never asked» and «asked and skipped» are different: the second means
        // the rule ran. Reporting the first as the second would claim a guard
        // worked on a project it never saw.
        Assert.Equal(StudioAlbumDrawSentence.NeverDecidedMn, StudioAlbumDrawSentence.For(null));
        Assert.Equal(
            StudioAlbumDrawSentence.NeverDecidedMn,
            StudioAlbumDrawSentence.For(new AlbumDrawRecord()));
    }

    [Fact]
    public void ANUnknownReasonCodeIsREPORTEDNotGuessed()
    {
        // 🔴 A CONFIDENT WRONG EXPLANATION IS WORSE THAN NONE. Falling back to
        // the first reason would put a sentence on screen that the record does
        // not support - and being trustworthy about what happened is this
        // record's only job.
        var record = new AlbumDrawRecord();
        record.Record(drew: false, "SomethingNobodyDefined", DateTimeOffset.UnixEpoch);

        string sentence = StudioAlbumDrawSentence.For(record);
        Assert.Contains("SomethingNobodyDefined", sentence, StringComparison.Ordinal);
        Assert.DoesNotContain(
            StudioAlbumRebuildPolicy.DescribeMn(AlbumRebuildReason.OriginAlwaysDraws),
            sentence,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THERecordIsWrittenAtTheONEExitOfTheDecision()
    {
        // 🔴 A BRANCH ADDED LATER MUST NOT ESCAPE BEING WRITTEN DOWN. The guard
        // used to return true from three places; recording at each of them would
        // have been three chances to forget.
        string source = ReadAppSource("ShellView.cs");
        string body = MethodBody(source, "private bool AlbumMustBeDrawn(StudioWorkspaceOperation origin)");

        Assert.Equal(1, Occurrences(body, "LastDraw.Record("));
        Assert.Equal(1, Occurrences(body, "return decision.MustDraw;"));
        Assert.Equal(1, Occurrences(body, "return "));
    }

    [Fact]
    public void THERecordIsNOTPartOfTheFingerprintItObserves()
    {
        // 🔴 OBSERVING THE RULE MUST NOT TRIGGER THE THING IT OBSERVES. A field
        // added to the ALBUM PROJECT would change every existing fingerprint and
        // force one redraw each - the exact cost measured for ReviewedBy. This
        // record lives on the workspace, which is not hashed.
        var project = new AlbumProject { Name = "Хэмжилт" };
        string before = AlbumBuildFingerprint.Of(project);

        string json = System.Text.Json.JsonSerializer.Serialize(
            project,
            ErkS.Platform.Contracts.SheetPackageJson.Options);

        Assert.DoesNotContain("lastDraw", json, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(before, AlbumBuildFingerprint.Of(project));
    }

    [Fact]
    public void THESentenceReachesTheAlbumPage()
    {
        // A record nobody can read is the stopwatch again, in a file.
        string workspaces = ReadAppSource("ShellView.Workspaces.cs");

        Assert.Contains(
            "StudioAlbumDrawSentence.For(state.Project.PrimaryAlbum.LastDraw)",
            workspaces,
            StringComparison.Ordinal);
        Assert.Contains("panel.Children.Add(albumDrawRecordText);", workspaces, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

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
