using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// 🔴 TWO REGRESSIONS FROM ONE EVENING, BOTH REPORTED BY THE USER AS
/// «нэг товч дараад л гацаад алга болчихоод байхын» - press a button and it
/// freezes and disappears. Both were introduced by work that was correct in
/// isolation and wrong in where it ran.
///
/// 1. THE INDICATOR HASHED FILES ON EVERY PAINT. Deciding which waiting
///    components this device could send uses the sync's own rule, and that rule
///    answers "do we hold this payload" by SHA-256 hashing the file. Reached
///    from RefreshSyncUi - which runs on two dozen ordinary UI paths - it put a
///    read-and-hash of every document and image on the UI thread of every
///    refresh.
///
/// 2. THE ACTION COULD CLOSE THE APPLICATION. It is invoked from an `async
///    void` click handler, so any exception escaping it terminates the process
///    rather than printing a message.
///
/// Both are wiring, not rules, which is why they are asserted here.
/// </summary>
public sealed class IndicatorMustNotCostMoreThanTheActionTests
{
    [Fact]
    public void THEPaintPathDoesNOTReachTheFileHashingRule()
    {
        // The painter runs on every UI refresh. Nothing it calls may touch the
        // disk - and the two helpers below are the ones that do, indirectly,
        // through HasVerifiedPayload.
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");
        int painter = refresh.IndexOf("private void RefreshCloudAlbumIndicator()", StringComparison.Ordinal);
        Assert.True(painter > 0, "the painter is gone");

        string body = refresh[painter..refresh.IndexOf(
            "private static string CountTextOf(", painter, StringComparison.Ordinal)];

        Assert.DoesNotContain("HasOwnedAtdDocuments", body, StringComparison.Ordinal);
        Assert.DoesNotContain("HasLocalVisualizationImages", body, StringComparison.Ordinal);
        Assert.DoesNotContain("SelectLocallyRenderableComponents", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEExpensiveSplitIsComputedOnlyWhenTheWaitingSetCHANGES()
    {
        // Cached against the pending set, which is the only thing that changes
        // it in practice. Without this the rule is correct and the application
        // is unusable - which is how it shipped for one evening.
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");
        int split = refresh.IndexOf("private (int Sendable, int Blocked) PendingSplit(", StringComparison.Ordinal);
        Assert.True(split > 0, "the split helper is gone");

        string body = refresh[split..];
        int guard = body.IndexOf("pendingSplitSignature, StringComparison.Ordinal", StringComparison.Ordinal);
        int expensive = body.IndexOf("SelectLocallyRenderableComponents", StringComparison.Ordinal);

        Assert.True(guard > 0, "there is no cache check");
        Assert.True(expensive > guard, "the expensive call must sit behind the cache check");
    }

    [Fact]
    public void THEFirstCallCANNOTBeServedFromAnEmptyCache()
    {
        // The signature starts at a value the real one can never take. Starting
        // it at "" would make an empty cache look like a valid answer of zero -
        // the same "absence encoded as a value" that has bitten this codebase
        // repeatedly.
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");

        Assert.Contains(
            "private string pendingSplitSignature = \"\\u0000\";",
            refresh,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THEOneActionCannotTakeTheProcessDown()
    {
        // 🔴 Invoked from an `async void` click handler: an escaping exception
        // is a closed window, not a stack trace. Every step reaches disk, the
        // network, or another member's data.
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");
        int method = refresh.IndexOf("private async Task RefreshAlbumAsync()", StringComparison.Ordinal);
        Assert.True(method > 0, "the one action is gone");

        // Bounded by the member that FOLLOWS the action. The first version
        // used PendingSplit, which sits ABOVE it in the file - searching forward
        // found nothing and the slice threw. An end marker must come after the
        // start, and "it compiled" does not check that.
        string body = refresh[method..refresh.IndexOf(
            "private AlbumRefreshStepResult RecomposeStep()", method, StringComparison.Ordinal)];

        // Caught broadly on purpose: a typed list is a list of the failures
        // somebody thought of, and the one that closes the app is the one
        // nobody did.
        Assert.Contains("catch (Exception exception)", body, StringComparison.Ordinal);
        Assert.Contains("SetStatus(\"Альбом шинэчлэхэд алдаа гарлаа: \"", body, StringComparison.Ordinal);

        // ...and it still reports, so a crash-turned-message is not a silence.
        int caught = body.IndexOf("catch (Exception exception)", StringComparison.Ordinal);
        Assert.Contains("FinishAlbumRefresh(steps);", body[caught..], StringComparison.Ordinal);
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

    [Fact]
    public void THEExpensiveSourceScanRunsONLYWhenADeliveryIsWaiting()
    {
        // 🔴 MEASURED AT 5 531 ms on the user's project - loading and verifying
        // 45 source packages. On the UI thread, on every press of the one
        // button, that is the freeze they reported.
        //
        // The inbox survey reads manifest headers only and answers "is anything
        // waiting", so the ordinary press costs nothing. This asserts the gate
        // sits BEFORE the expensive call, because putting it after would be an
        // easy and completely invisible mistake.
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");

        int gate = refresh.IndexOf("SurveyPendingDeliveries(source).Any", StringComparison.Ordinal);
        int scan = refresh.IndexOf("await CheckForSourceUpdatesAsync()", StringComparison.Ordinal);

        Assert.True(gate > 0, "the cheap survey gate is gone");
        Assert.True(scan > gate, "the expensive scan must sit behind the survey");

        // And skipping it must still report a step, or the run would look as
        // though step 1 never happened.
        Assert.Contains(
            "SourceRefreshOutcome.Completed(state.Project.Sources.Count, 0)",
            refresh,
            StringComparison.Ordinal);
    }
}
