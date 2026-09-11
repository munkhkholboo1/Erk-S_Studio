using System.Net;
using System.Net.Http;
using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The refusal that nobody could name.
///
/// 🔴 A REAL USER LOST THEIR SEATED MACHINE AND FIVE HYPOTHESES WERE BUILT AND
/// MEASURED AGAINST IT - misclassification, a stale queued release, a changed
/// fingerprint, a stale token, two sides disagreeing on a rule. Every one fell.
/// What was missing was never a theory: it was WHICH REFUSAL ARRIVED, a fact
/// that existed for a few milliseconds on one machine and then nowhere. The
/// server keeps no log of it either.
///
/// This started as a store for ONE concern - the seat resume - written at ONE
/// call site. That was the same shape as every defect it was meant to diagnose:
/// a record that exists only where somebody remembered to write it. It is now
/// taken at the boundary itself, so every route is covered without any caller
/// cooperating.
/// </summary>
[Collection(StudioDataRootCollection.Name)]
public sealed class BoundaryRefusalIsRecordedTests : IDisposable
{
    private readonly string dataRoot = Path.Combine(
        Path.GetTempPath(), "erks-boundary-refusal-tests", Guid.NewGuid().ToString("N"));
    private readonly string? previousRoot =
        Environment.GetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT");

    public BoundaryRefusalIsRecordedTests()
    {
        Directory.CreateDirectory(dataRoot);
        Environment.SetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT", dataRoot);

        // 🔴 THE STORE KEEPS PROCESS-WIDE STATE AND A TEST THAT ASSUMES A FRESH
        // ONE IS FLAKY. A private data root is not enough on its own: the class
        // caches whether anything is recorded, so a test starting after another
        // one's writes can read a flag that belongs to a directory it has never
        // seen. This failed about one run in five before it was reset here -
        // intermittently, which is the expensive kind.
        StudioBoundaryRefusals.Clear();
    }

    [Fact]
    public void ANUNKNOWNCodeIsWrittenDownVERBATIM()
    {
        // 🔴 THE ONE RULE THAT MAKES THIS WORTH HAVING. The server can add words
        // - bot_state_device_required is one this build has never heard of - and
        // a reader that folds an unrecognised code into «unknown» destroys the
        // only part of the answer that was new.
        StudioBoundaryRefusals.Note(
            "api/cloud-era/v1/bot-states/{id}/resume",
            "bot_state_device_required",
            400,
            "Төхөөрөмжийн таних тэмдэг байхгүй байна.");

        StudioBoundaryRefusal? noted = StudioBoundaryRefusals.Latest(
            "api/cloud-era/v1/bot-states/{id}/resume");
        Assert.NotNull(noted);
        Assert.Equal("bot_state_device_required", noted.Code);
        Assert.Equal(400, noted.Status);
    }

    [Fact]
    public void AFailureWithNOCodeStillLeavesTheSTATUS()
    {
        // An uncaught exception on the server arrives as a bare 500 with no body
        // at all, so there is no code to write. The status is then the whole of
        // what is knowable, and «nothing was recorded» would be worse than
        // «500, no code». SRV's contract names this case explicitly.
        StudioBoundaryRefusals.Note("api/cloud-era/v1/projects", "", 500, "Cloud ERA server алдаа: 500");

        StudioBoundaryRefusal? noted = StudioBoundaryRefusals.Latest("api/cloud-era/v1/projects");
        Assert.NotNull(noted);
        Assert.Equal("", noted.Code);
        Assert.Equal(500, noted.Status);
    }

    [Fact]
    public void THESameRefusalTwiceIsCOUNTEDRatherThanDuplicated()
    {
        // «Once, while the network was down» and «every single time» lead to
        // different places, and only a count separates them.
        StudioBoundaryRefusals.Note("api/cloud-era/v1/projects/{id}", "project_not_found", 404, "Алга.");
        StudioBoundaryRefusals.Note("api/cloud-era/v1/projects/{id}", "project_not_found", 404, "Алга.");

        Assert.Equal(2, StudioBoundaryRefusals.Latest("api/cloud-era/v1/projects/{id}")!.Attempts);
        // Counted on THIS route rather than across the file. The suite writes
        // to one process-wide store, so a global count measures whatever else
        // happened to be running - which is how this test failed one run in
        // five on a 502 that arrived from somewhere else entirely.
        Assert.Single(
            StudioBoundaryRefusals.Read(),
            refusal => refusal.Route == "api/cloud-era/v1/projects/{id}");

        // A DIFFERENT code is a different failure and starts again.
        StudioBoundaryRefusals.Note(
            "api/cloud-era/v1/projects/{id}", "bot_project_out_of_scope", 403, "Хамрахгүй.");
        Assert.Equal(1, StudioBoundaryRefusals.Latest("api/cloud-era/v1/projects/{id}")!.Attempts);
    }

    [Fact]
    public void SUCCESSOnOneRouteDoesNotErasANOTHERSEvidence()
    {
        // 🔴 THE REASON CLEARING IS PER ROUTE. The album upload is the failure
        // somebody is chasing; the project list refreshes on a timer and works
        // fine. A wholesale clear on success would wipe the album's evidence
        // several times a minute, and the answer would vanish exactly as it did
        // the first time.
        StudioBoundaryRefusals.Note("api/cloud-era/v1/projects/{id}/albums/{id}/revisions", "album_revision_conflict", 409, "Зөрчил.");
        StudioBoundaryRefusals.Note("api/cloud-era/v1/projects", "project_list_failed", 500, "Алдаа.");

        StudioBoundaryRefusals.Cleared("api/cloud-era/v1/projects");

        Assert.Null(StudioBoundaryRefusals.Latest("api/cloud-era/v1/projects"));
        Assert.NotNull(StudioBoundaryRefusals.Latest("api/cloud-era/v1/projects/{id}/albums/{id}/revisions"));
    }

    [Fact]
    public void THEListIsBOUNDED()
    {
        // A diagnosis needs the last few; a person's disk does not need the
        // rest. The oldest go first, because what is failing NOW is the question.
        for (int index = 0; index < StudioBoundaryRefusals.Capacity + 5; index++)
            StudioBoundaryRefusals.Note("route/" + index, "code_" + index, 500, "x");

        IReadOnlyList<StudioBoundaryRefusal> stored = StudioBoundaryRefusals.Read();
        Assert.Equal(StudioBoundaryRefusals.Capacity, stored.Count);
        Assert.Null(StudioBoundaryRefusals.Latest("route/0"));
        Assert.NotNull(StudioBoundaryRefusals.Latest("route/" + (StudioBoundaryRefusals.Capacity + 4)));
    }

    [Fact]
    public void ADETAILIsAttachedWithoutDisturbingTheCOUNT()
    {
        // The funnel records; a caller adds what only it knows. The seat resume
        // is the case: the question is «did this machine send the same
        // fingerprint as last time», and the funnel has never heard of one.
        StudioBoundaryRefusals.Note("bot-states/{id}/resume", "bot_state_not_found", 404, "Алга.");
        StudioBoundaryRefusals.Note("bot-states/{id}/resume", "bot_state_not_found", 404, "Алга.");
        StudioBoundaryRefusals.AnnotateLatest("sent-fingerprint: 63b30cb2");

        StudioBoundaryRefusal? noted = StudioBoundaryRefusals.Latest("bot-states/{id}/resume");
        Assert.NotNull(noted);
        Assert.Equal("sent-fingerprint: 63b30cb2", noted.Detail);
        Assert.Equal(2, noted.Attempts);
    }

    [Fact]
    public async Task AREFUSALPassingThroughTheFUNNELRecordsITSELF()
    {
        // 🔴 THE TEST THAT ACTUALLY HOLDS THIS SHUT, AND SABOTAGE IS WHY IT
        // EXISTS. The source-reading test below was satisfied by wrapping the
        // recording in «if (false)» - the text was still there, the call was
        // unreachable, and every assertion stayed green. A test that reads
        // source can see that a line was written; only running the thing can
        // see that it runs.
        var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                new Uri("https://erk-s.mn/api/cloud-era/v1/projects/PRJ-2026-0042/albums/9f2a1c")),
            Content = new StringContent(
                """{"code":"bot_project_out_of_scope","message":"Энэ төсөл энэ суудалд томилогдоогүй байна."}""",
                Encoding.UTF8,
                "application/json"),
        };

        await Assert.ThrowsAsync<StudioAccountException>(
            () => StudioAccountService.ThrowIfFailedAsync(response, CancellationToken.None));

        StudioBoundaryRefusal? noted = StudioBoundaryRefusals.Latest(
            "api/cloud-era/v1/projects/{id}/albums/{id}");
        Assert.NotNull(noted);
        Assert.Equal("bot_project_out_of_scope", noted.Code);
        Assert.Equal(403, noted.Status);
        Assert.Contains("томилогдоогүй", noted.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SUCCESSPassingThroughTheFUNNELForgetsThatRoute()
    {
        // The other half, and the same reason: «cleared on success» is a claim
        // about behaviour, so it is asserted by behaving.
        var uri = new Uri("https://erk-s.mn/api/cloud-era/v1/projects/PRJ-1");
        var refused = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, uri),
            Content = new StringContent(
                """{"code":"project_not_found","message":"Алга."}""",
                Encoding.UTF8,
                "application/json"),
        };
        await Assert.ThrowsAsync<StudioAccountException>(
            () => StudioAccountService.ThrowIfFailedAsync(refused, CancellationToken.None));
        Assert.NotNull(StudioBoundaryRefusals.Latest("api/cloud-era/v1/projects/{id}"));

        var succeeded = new HttpResponseMessage(HttpStatusCode.OK)
        {
            RequestMessage = new HttpRequestMessage(HttpMethod.Get, uri),
        };
        await StudioAccountService.ThrowIfFailedAsync(succeeded, CancellationToken.None);

        Assert.Null(StudioBoundaryRefusals.Latest("api/cloud-era/v1/projects/{id}"));
    }

    [Fact]
    public async Task ABAREFiveHundredWithNoBodyStillLeavesTheSTATUS()
    {
        // SRV's contract names this exactly: UseExceptionHandler is not
        // registered, so an uncaught exception arrives as a naked 500 with no
        // JSON at all. The status is then the whole of what is knowable, and
        // recording nothing would be worse than recording «500, no code».
        var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Post, new Uri("https://erk-s.mn/api/cloud-era/v1/bot-seats")),
            Content = new StringContent("<html>Server Error</html>", Encoding.UTF8, "text/html"),
        };

        await Assert.ThrowsAsync<StudioAccountException>(
            () => StudioAccountService.ThrowIfFailedAsync(response, CancellationToken.None));

        StudioBoundaryRefusal? noted = StudioBoundaryRefusals.Latest("api/cloud-era/v1/bot-seats");
        Assert.NotNull(noted);
        Assert.Equal("", noted.Code);
        Assert.Equal(500, noted.Status);
    }

    [Fact]
    public void THEBoundaryRecordIsTakenWHERETheRefusalIsBORN()
    {
        // 🔴 THE WHOLE DESIGN, IN ONE ASSERTION. Studio catches this exception
        // in seventeen places; three of them show the server's own sentence and
        // NONE reads the code. Asking seventeen callers to also record is the
        // «rule written, caller never asks» shape that produced the defects this
        // record exists to diagnose - so the recording sits at the single point
        // every refusal is constructed at, and no caller is asked for anything.
        string funnel = MethodBody(
            ReadAppSource("StudioAccountService.cs"),
            "internal static async Task ThrowIfFailedAsync(");

        Assert.Contains("StudioBoundaryRefusals.Note(", funnel, StringComparison.Ordinal);
        Assert.Contains("StudioBoundaryRefusals.Cleared(", funnel, StringComparison.Ordinal);

        // Forgetting is bound to SUCCESS, not to anything a caller does.
        int success = funnel.IndexOf("if (response.IsSuccessStatusCode)", StringComparison.Ordinal);
        int forget = funnel.IndexOf("StudioBoundaryRefusals.Cleared(", StringComparison.Ordinal);
        int record = funnel.IndexOf("StudioBoundaryRefusals.Note(", StringComparison.Ordinal);
        Assert.True(success > 0 && forget > success, "forgetting no longer sits on the success path");
        Assert.True(record > forget, "the recording must be on the failure path, after the early return");
    }

    [Fact]
    public void THERecordCarriesNOIdentifiersFromTheURL()
    {
        // Written to disk, a list of URLs would be a list of what the person is
        // working on. The symbol answers «which door refused» and carries
        // nothing else - so ids, hashes and account emails all leave.
        Assert.Equal(
            "api/cloud-era/v1/projects/{id}/albums/{id}/revisions",
            StudioBoundaryRoute.Symbol(
                new Uri("https://erk-s.mn/api/cloud-era/v1/projects/PRJ-2026-0042/albums/9f2a1c/revisions")));

        Assert.Equal(
            "api/cloud-era/v1/bot-seats/{id}",
            StudioBoundaryRoute.Symbol(
                new Uri("https://erk-s.mn/api/cloud-era/v1/bot-seats/" + Guid.NewGuid().ToString("D"))));

        // An account email is a path segment on some routes and is the most
        // sensitive of the lot.
        Assert.Equal(
            "api/accounts/{id}/devices",
            StudioBoundaryRoute.Symbol(new Uri("https://erk-s.mn/api/accounts/somebody@erk-s.mn/devices")));

        // The query goes whole: filters and tokens live there.
        Assert.Equal(
            "api/cloud-era/v1/projects",
            StudioBoundaryRoute.Symbol(
                new Uri("https://erk-s.mn/api/cloud-era/v1/projects?organizationId=ORG-7&token=abc")));

        // The positive control: a route made only of names keeps every one of
        // them, or the assertions above are true of a function that returns
        // «{id}» for everything.
        //
        // 🔴 AND «v1» IS ONE OF THOSE NAMES. The first version of the reader
        // called it an identifier because it contains a digit, and every route
        // collapsed into «api/cloud-era/{id}/…» - the symbol stopped answering
        // the only question it exists for.
        Assert.Equal(
            "api/cloud-era/v1/bot-seats",
            StudioBoundaryRoute.Symbol(new Uri("https://erk-s.mn/api/cloud-era/v1/bot-seats")));

        // The version exception stays narrow: a bare numeric id is still an id.
        Assert.Equal(
            "api/cloud-era/v1/projects/{id}",
            StudioBoundaryRoute.Symbol(new Uri("https://erk-s.mn/api/cloud-era/v1/projects/42")));
    }

    [Fact]
    public void ANULLRequestUriIsNotAFailureOfItsOwn()
    {
        // Recording must never become a way for a refusal to turn into a crash.
        Assert.Equal(StudioBoundaryRoute.Unnamed, StudioBoundaryRoute.Symbol(null));
    }

    [Fact]
    public void BOTHRefusalPathsAnnotateAndThePersonIsToldWhereToLook()
    {
        // Read from source: the resume needs a window and a live shell. What is
        // asserted is that NEITHER refusal branch is silent, and that the person
        // is told where the answer is rather than having to be sent the path by
        // somebody who already knows.
        string body = MethodBody(
            ReadAppSource("ShellView.BotSeat.cs"), "private async Task ResumeAsBotAsync(");

        Assert.Equal(2, Occurrences(body, "NoteResumeFailure("));
        Assert.Contains("StudioBoundaryRefusals.StorePath", body, StringComparison.Ordinal);

        // And the note is no longer cleared by hand on success - the funnel
        // forgets the route that succeeded, so a line nobody has to remember
        // replaced a line somebody had to.
        Assert.DoesNotContain("StudioBoundaryRefusals.Clear();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ASTALEQueuedReleaseCannotReachASeatReEnteredSince()
    {
        // 🔴 A QUEUED RELEASE NAMES A SEAT, AND A SEAT CAN BE SAT IN TWICE. If
        // this machine has re-entered the same seat since the request was
        // queued, sending it releases a state the person is using. The
        // discriminator is WHEN, and the note already carries it.
        string body = MethodBody(
            ReadAppSource("ShellView.BotSeat.cs"),
            "private async Task<BotSeatFlushOutcome> FlushPendingBotSeatReleasesAsync()");

        Assert.Contains("seatedNow.EnteredAtUtc > item.LeftAtUtc", body, StringComparison.Ordinal);

        int guard = body.IndexOf("seatedNow.EnteredAtUtc > item.LeftAtUtc", StringComparison.Ordinal);
        int send = body.IndexOf("await account.LeaveBotStateAsync(", StringComparison.Ordinal);
        Assert.True(guard > 0 && send > guard, "the guard must run before the release is sent");
    }

    [Fact]
    public void THEOLDSingleConcernStoreIsGONE()
    {
        // 🔴 A SECOND STORE FOR THE SAME QUESTION IS HOW TWO ANSWERS DISAGREE.
        // The resume note was written for one call site before the boundary
        // record existed; leaving it in place would mean a machine with two
        // files, each holding half of what happened.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string app = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App");
            if (Directory.Exists(app))
            {
                Assert.False(
                    File.Exists(Path.Combine(app, "StudioBotResumeFailures.cs")),
                    "the single-concern store came back beside the general one");
                return;
            }
            directory = directory.Parent;
        }
        Assert.Fail("the app source was not found");
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
