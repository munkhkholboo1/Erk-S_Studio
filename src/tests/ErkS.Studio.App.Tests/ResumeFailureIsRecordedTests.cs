using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The refusal that nobody could name.
///
/// 🔴 A REAL USER LOST THEIR MACHINE AND FIVE HYPOTHESES WERE BUILT AND
/// MEASURED AGAINST IT - misclassification, a stale queued release, a changed
/// fingerprint, a stale token, two sides disagreeing on a rule. Every one fell.
/// What was missing was never a theory: it was WHICH REFUSAL ARRIVED, a fact
/// that existed for a few milliseconds on one machine and then nowhere. The
/// server keeps no log of it either.
/// </summary>
[Collection(StudioDataRootCollection.Name)]
public sealed class ResumeFailureIsRecordedTests : IDisposable
{
    private readonly string dataRoot = Path.Combine(
        Path.GetTempPath(), "erks-resume-failure-tests", Guid.NewGuid().ToString("N"));
    private readonly string? previousRoot =
        Environment.GetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT");

    public ResumeFailureIsRecordedTests()
    {
        Directory.CreateDirectory(dataRoot);
        Environment.SetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT", dataRoot);
    }

    [Fact]
    public void ANUNKNOWNCodeIsWrittenDownVERBATIM()
    {
        // 🔴 THE ONE RULE THAT MAKES THIS WORTH HAVING. The server can add
        // words - bot_state_device_required is one this build has never heard
        // of - and a reader that folds an unrecognised code into «unknown»
        // destroys the only part of the answer that was new.
        StudioBotResumeFailures.Note(
            "bot_state_device_required", 400, "Төхөөрөмжийн таних тэмдэг байхгүй байна.", new string('A', 64));

        StudioBotResumeFailure? noted = StudioBotResumeFailures.Read();
        Assert.NotNull(noted);
        Assert.Equal("bot_state_device_required", noted.Code);
        Assert.Equal(400, noted.Status);
    }

    [Fact]
    public void AFailureWithNOCodeStillLeavesTheSTATUS()
    {
        // An uncaught exception on the server arrives as a bare 500 with no
        // body at all, so there is no code to write. The status is then the
        // whole of what is knowable, and «nothing was recorded» would be worse
        // than «500, no code».
        StudioBotResumeFailures.Note("", 500, "Cloud ERA server алдаа: 500", new string('B', 64));

        StudioBotResumeFailure? noted = StudioBotResumeFailures.Read();
        Assert.NotNull(noted);
        Assert.Equal("", noted.Code);
        Assert.Equal(500, noted.Status);
    }

    [Fact]
    public void THESameRefusalTwiceIsCOUNTEDRatherThanReplaced()
    {
        // «Once, while the network was down» and «every single time» lead to
        // different places, and only a count separates them.
        StudioBotResumeFailures.Note("bot_state_not_found", 404, "Алга.", new string('C', 64));
        StudioBotResumeFailures.Note("bot_state_not_found", 404, "Алга.", new string('C', 64));

        Assert.Equal(2, StudioBotResumeFailures.Read()!.Attempts);

        // A DIFFERENT code is a different failure and starts again.
        StudioBotResumeFailures.Note("bot_session_device_mismatch", 403, "Өөр төхөөрөмж.", new string('C', 64));
        Assert.Equal(1, StudioBotResumeFailures.Read()!.Attempts);
    }

    [Fact]
    public void ONLYEightCharactersOfTheFingerprintAreKept()
    {
        // Enough to tell «the same one as last time» from «a different one»,
        // which is the whole question a fingerprint raises - and useless for
        // anything else. The value itself is not the client's to spread.
        StudioBotResumeFailures.Note("x", 1, "y", "0123456789ABCDEF0123456789ABCDEF");

        StudioBotResumeFailure? noted = StudioBotResumeFailures.Read();
        Assert.NotNull(noted);
        Assert.Equal("01234567", noted.SentFingerprintPrefix);
        Assert.Equal(8, noted.SentFingerprintPrefix.Length);
    }

    [Fact]
    public void ASUCCESSFULResumeFORGETSIt()
    {
        // A note that outlives its cause is read by the next person as a live
        // failure - the same defect the queued-release note had, one file over.
        StudioBotResumeFailures.Note("bot_state_not_found", 404, "Алга.", new string('D', 64));
        Assert.NotNull(StudioBotResumeFailures.Read());

        StudioBotResumeFailures.Clear();

        Assert.Null(StudioBotResumeFailures.Read());
        Assert.False(File.Exists(StudioBotResumeFailures.StorePath));
    }

    [Fact]
    public void BOTHRefusalPathsRecordAndSuccessClears()
    {
        // Read from source: the resume needs a window and a live shell. What is
        // asserted is that NEITHER refusal branch is silent, and that the
        // success path drops the note.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private async Task ResumeAsBotAsync(");

        Assert.Equal(2, Occurrences(body, "NoteResumeFailure("));
        Assert.Contains("StudioBotResumeFailures.Clear();", body, StringComparison.Ordinal);

        // And the person is told where the answer is, rather than having to be
        // sent the path by somebody who already knows.
        Assert.Contains("StudioBotResumeFailures.StorePath", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ASTALEQueuedReleaseCannotReachASeatReEnteredSince()
    {
        // 🔴 A QUEUED RELEASE NAMES A SEAT, AND A SEAT CAN BE SAT IN TWICE. If
        // this machine has re-entered the same seat since the request was
        // queued, sending it releases a state the person is using. The
        // discriminator is WHEN, and the note already carries it.
        //
        // Latent until the organisation segment left the path: before that, such
        // a request 404'd on the way out and failed silently.
        string body = MethodBody(
            ReadAppSource("ShellView.BotSeat.cs"),
            "private async Task<BotSeatFlushOutcome> FlushPendingBotSeatReleasesAsync()");

        Assert.Contains("seatedNow.EnteredAtUtc > item.LeftAtUtc", body, StringComparison.Ordinal);

        // Forgotten rather than sent, and the guard sits BEFORE the call.
        int guard = body.IndexOf("seatedNow.EnteredAtUtc > item.LeftAtUtc", StringComparison.Ordinal);
        int send = body.IndexOf("await account.LeaveBotStateAsync(", StringComparison.Ordinal);
        Assert.True(guard > 0 && send > guard, "the guard must run before the release is sent");
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

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

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT", previousRoot);
        try
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
