using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The single album action, held to the decisions that shaped it.
///
/// These read source rather than run the action: it lives on a WPF shell that
/// needs a window, a signed-in account and a cloud project to exercise. What is
/// checked here is therefore the WIRING - which is exactly where this codebase
/// keeps losing things. The rules themselves are covered by unit tests in
/// CloudAlbumStatusTests and AlbumRefreshReportTests; what those cannot see is
/// whether anybody calls them, and in what order.
/// </summary>
public sealed class AlbumRefreshActionTests
{
    [Fact]
    public void AFailedSourceReadSTOPSBeforeAnythingIsSent()
    {
        // Steps 1-2 failing must mean nothing left this machine. If the sync
        // ran anyway, the report's "юу ч илгээгээгүй" line would be a lie, and
        // a half-read album would be published to everyone else.
        string source = ReadAppSource("ShellView.AlbumRefresh.cs");
        int guard = source.IndexOf("if (!sources.Succeeded)", StringComparison.Ordinal);
        int sync = source.IndexOf("await SynchronizeCurrentProjectAsync();", StringComparison.Ordinal);

        Assert.True(guard > 0, "the source-read failure guard is gone");
        Assert.True(sync > guard, "the sync must come after the guard, not before it");

        // Scoped to the guard's OWN block rather than to everything before
        // the sync. The first version of this assertion looked between the
        // guard and the sync and found a `return;` belonging to a DIFFERENT
        // branch entirely - so deleting the guard's own return left it green.
        // A mutation caught that; it is written down because "the assertion
        // matched something else" is invisible from a passing run.
        int successLine = source.IndexOf(
            "steps.Add(AlbumRefreshReport.SourcesRead(", guard, StringComparison.Ordinal);
        Assert.True(successLine > guard, "step 1 no longer reports success after the guard");

        string guarded = source[guard..successLine];
        Assert.Contains("FinishAlbumRefresh(steps);", guarded, StringComparison.Ordinal);
        Assert.Contains("return;", guarded, StringComparison.Ordinal);
    }

    [Fact]
    public void THEProbeRunsAFTERTheSyncSoItDescribesTheNEWState()
    {
        // A probe taken before the sync describes the state the person was in
        // when they pressed the button, not the one they are being told about.
        // The report would then say "the cloud is ahead" about work that had
        // just been pulled in.
        string source = ReadAppSource("ShellView.AlbumRefresh.cs");

        Assert.True(
            source.IndexOf("await SynchronizeCurrentProjectAsync();", StringComparison.Ordinal) <
                source.IndexOf("await ProbeCloudAlbumAsync();", StringComparison.Ordinal),
            "the cloud probe must be taken after the sync");
    }

    [Fact]
    public void STEP1IsAWAITEDRatherThanFiredAndForgotten()
    {
        // 🔴 THE DEFECT THIS ACTION WAS BUILT ON TOP OF. The source refresh used
        // to return void and queue its real work onto the dispatcher, so a
        // caller that read the project straight afterwards saw the state from
        // BEFORE the refresh. An action that reads sources and then sends them
        // cannot be built on that.
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");
        string workspaces = ReadAppSource("ShellView.Workspaces.cs");

        Assert.Contains(
            "await CheckForSourceUpdatesAsync()",
            refresh,
            StringComparison.Ordinal);
        Assert.Contains(
            "private async Task<SourceRefreshOutcome> CheckForSourceUpdatesAsync()",
            workspaces,
            StringComparison.Ordinal);

        // And its deferred half is awaited rather than posted, or the outcome
        // it returns would describe work that has not happened.
        Assert.Contains(
            "return await dispatcher.InvokeAsync(",
            workspaces,
            StringComparison.Ordinal);
    }

    [Fact]
    public void STEP1ReportsTheSCANSOwnCountsAndNotAGuess()
    {
        // An earlier draft inferred "how much changed" from the album's page
        // count before and after - a number that measures neither the sources
        // nor the change. The scan already counts both; the report uses those.
        string source = ReadAppSource("ShellView.AlbumRefresh.cs");
        int step = source.IndexOf("AlbumRefreshReport.SourcesRead(", StringComparison.Ordinal);
        Assert.True(step > 0, "step 1 no longer reports");

        string call = source[step..(step + 160)];
        Assert.Contains("sources.CheckedCount", call, StringComparison.Ordinal);
        Assert.Contains("sources.ChangedCount", call, StringComparison.Ordinal);
    }

    [Fact]
    public void THEIndicatorStartsIGNORANTRatherThanGREEN()
    {
        // 🔴 A field defaulting to "answered" would paint green on a freshly
        // opened project before anything had been asked - the single failure
        // the fifth state exists to prevent.
        string source = ReadAppSource("ShellView.AlbumRefresh.cs");

        // The DECLARATION is what is asserted, not some assignment elsewhere.
        // An earlier version of this test forbade a string that happened to
        // carry a Windows line ending, which made it pass for a reason that had
        // nothing to do with the field's initial value.
        Assert.Contains(
            "private CloudProbeOutcome lastCloudProbeOutcome = CloudProbeOutcome.NotAttempted;",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NOFailurePathLeavesTheOLDANSWEREDStateStanding()
    {
        // A probe that fails while leaving the previous "answered" verdict in
        // place keeps a stale green on screen - the worst of both states, since
        // it looks freshly confirmed.
        string source = ReadAppSource("ShellView.AlbumRefresh.cs");

        int recorder = source.IndexOf("private void RecordCloudProbeFailure(", StringComparison.Ordinal);
        Assert.True(recorder > 0, "the shared failure recorder is gone");

        string body = source[recorder..source.IndexOf("\n    }", recorder, StringComparison.Ordinal)];
        Assert.Contains("CloudProbeOutcome.Failed", body, StringComparison.Ordinal);
        Assert.Contains("lastCloudProbeFoundNewerRevision = false;", body, StringComparison.Ordinal);

        // The probe's own catch must go through that same recorder rather than
        // writing its own idea of a failed state - which is how the two would
        // drift apart.
        int probeCatch = source.IndexOf("catch (Exception exception) when (", recorder, StringComparison.Ordinal);
        Assert.True(probeCatch > 0, "the probe no longer catches");
        Assert.Contains(
            "RecordCloudProbeFailure(exception);",
            source[probeCatch..],
            StringComparison.Ordinal);
    }

    [Fact]
    public void THEUnknownStateIsNEVERPaintedWithASuccessColour()
    {
        // The colour table is the last place the "never green when unknown"
        // rule can be undone, and it is undone by a single wrong arm.
        string source = ReadAppSource("ShellView.AlbumRefresh.cs");
        int table = source.IndexOf("private static Brush IndicatorBrush(", StringComparison.Ordinal);
        Assert.True(table > 0, "the colour table is gone");

        string body = source[table..source.IndexOf("\n    };", table, StringComparison.Ordinal)];

        // The default arm - which Unknown and NotLinked fall through to - must
        // be the muted one.
        int fallthrough = body.IndexOf("_ =>", StringComparison.Ordinal);
        Assert.True(fallthrough > 0, "the colour table has no default arm");
        Assert.Contains("MutedTextBrush", body[fallthrough..], StringComparison.Ordinal);
        Assert.DoesNotContain("SuccessBrush", body[fallthrough..], StringComparison.Ordinal);
    }

    [Fact]
    public void THECloudProbeAsksTheCHEAPQuestionRatherThanDownloadingTheAlbum()
    {
        // The whole justification for checking on every album-page visit is
        // that the check is a conditional GET. A probe that pulled the
        // canonical PDF would cost more than the action it describes.
        string source = ReadAppSource("ShellView.AlbumRefresh.cs");
        int probe = source.IndexOf("private async Task<CloudProbeOutcome> ProbeCloudAlbumAsync()", StringComparison.Ordinal);
        Assert.True(probe > 0, "the probe is gone");

        string body = source[probe..source.IndexOf("\n    /// <summary>", probe, StringComparison.Ordinal)];

        // The dedicated route: it answers the one question and stops. The
        // routes that also carry the revision id return every revision's
        // section manifest with it - fine once per sync, far too much to pull
        // every time a colour needs deciding.
        Assert.Contains("GetAlbumChangeSummaryAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("GetProjectAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("DownloadAlbum", body, StringComparison.Ordinal);
        Assert.DoesNotContain("AlbumPdf", body, StringComparison.Ordinal);

        // 🔴 THE PROJECT TOKEN, NOT THE REVISION ID. A component-manifest
        // reorder changes the album everyone pulls back without creating a
        // revision, so an indicator built on the revision id stays green
        // through the commonest change in a multi-member project.
        Assert.Contains("ProjectConcurrencyToken", body, StringComparison.Ordinal);

        // 🔴 COMPARED AGAINST THE LEVEL TOKEN. The token moves on this device's
        // own writes too; comparing with the newest token seen would report the
        // user's own upload as somebody else's work.
        Assert.Contains("LastLevelCloudToken", body, StringComparison.Ordinal);
        Assert.DoesNotContain("LastReceivedAlbumRevisionId", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEAlbumSelectionRuleExistsONCE()
    {
        // Two rules answering "which album, which revision" is how the view and
        // the build came to disagree before. The rule was extracted rather than
        // copied, so the inline version must be gone from AppState.
        string selection = ReadAppSource("StudioCloudAlbumSelection.cs");
        string appState = ReadAppSource("AppState.cs");

        Assert.Contains("CurrentRevision(StudioCloudProjectDetail? detail)", selection, StringComparison.Ordinal);
        Assert.Contains("StudioCloudAlbumSelection.CurrentRevision(", appState, StringComparison.Ordinal);
        // 🔴 A SINGLE TOKEN, NOT A MULTI-LINE FRAGMENT. The first version of
        // this assertion spelled out a Windows line ending inside the pattern.
        // Git normalises these files, so on a fresh checkout the pattern would
        // match nothing and DoesNotContain would pass for the wrong reason -
        // the test would go on reporting "the rule exists once" long after a
        // copy came back.
        // The positive control for the line below: this token is real, and it
        // lives in the extracted rule. Without this, a typo in the forbidden
        // string would make the DoesNotContain pass forever.
        Assert.Contains("album.CurrentRevisionId", selection, StringComparison.Ordinal);
        Assert.DoesNotContain("album.CurrentRevisionId", appState, StringComparison.Ordinal);
    }

    [Fact]
    public void THEAlbumToolbarHasNOAlbumCommandsLeft()
    {
        // 🔴 THE CONDITION REVERSED, AND THAT IS THE POINT. This test used to
        // assert the three old commands were still present - Master's caution
        // while the one action was unproven. The user asked four times for
        // fewer things on screen, and each round had ADDED one. The buttons are
        // gone now, and this asserts they stay gone: bringing one back is an
        // easy edit and it is the wrong answer to "the one action missed a
        // case". The right answer is to extend the one action.
        string workspaces = ReadAppSource("ShellView.Workspaces.cs");
        int ribbon = workspaces.IndexOf("documentGroup.Children.Add(save);", StringComparison.Ordinal);
        Assert.True(ribbon > 0, "the album ribbon is gone");

        string added = workspaces[ribbon..workspaces.IndexOf("ribbon.Children.Add(documentGroup);", ribbon, StringComparison.Ordinal)];

        Assert.DoesNotContain("Add(updateAlbum)", added, StringComparison.Ordinal);
        Assert.DoesNotContain("Add(rebuildAlbum)", added, StringComparison.Ordinal);
        Assert.DoesNotContain("Add(refreshAlbum)", added, StringComparison.Ordinal);
        Assert.DoesNotContain("Add(cloudAlbumIndicator)", added, StringComparison.Ordinal);
    }

    [Fact]
    public void THECloudIconRUNSTheOneActionRatherThanPlainSync()
    {
        // The user pointed at the cloud on the project card and said they
        // wanted to do everything with it. Wiring it to the old sync would give
        // them a third of the job under the icon they were promised the whole
        // job under.
        string shell = ReadAppSource("ShellView.cs");

        Assert.Contains(
            "cloudSyncButton.Click += async (_, _) => await RefreshAlbumAsync();",
            shell,
            StringComparison.Ordinal);
    }

    [Fact]
    public void THEOneActionSTILLCoversWhatTheRemovedCommandsDid()
    {
        // 🔴 REMOVING A BUTTON MUST NOT REMOVE ITS WORK. «Бүрэн дахин
        // байгуулах» marked this device's own components for redrawing so a
        // rebuild reached the SHARED album rather than only the local copy.
        // Dropping the button without folding that in would have left the pages
        // looking right here while every other member kept the old ones - a
        // silent loss, visible to nobody on this machine.
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");
        int method = refresh.IndexOf("private async Task RefreshAlbumAsync()", StringComparison.Ordinal);
        Assert.True(method > 0, "the one action is gone");

        string body = refresh[method..refresh.IndexOf("private AlbumRefreshStepResult RecomposeStep()", method, StringComparison.Ordinal)];

        // read the sources
        Assert.Contains("await CheckForSourceUpdatesAsync()", body, StringComparison.Ordinal);
        // redraw this device's own components - the folded-in rebuild
        Assert.Contains("MarkOwnAlbumComponentsForRerender()", body, StringComparison.Ordinal);
        // exchange with the cloud
        Assert.Contains("await SynchronizeCurrentProjectAsync();", body, StringComparison.Ordinal);
        // recompute composition and order
        Assert.Contains("RecomposeStep()", body, StringComparison.Ordinal);
        // and report
        Assert.Contains("FinishAlbumRefresh(steps);", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THERedrawRunsWHENTheSourcesActuallyChanged()
    {
        // Unconditionally re-marking every own component on every press would
        // re-upload work that had not changed. The condition is the one that
        // made it a decision worth a button: the sources moved.
        string refresh = ReadAppSource("ShellView.AlbumRefresh.cs");
        int mark = refresh.IndexOf("MarkOwnAlbumComponentsForRerender()", StringComparison.Ordinal);
        Assert.True(mark > 0, "the redraw is gone");

        Assert.Contains(
            "if (sources.ChangedCount > 0)",
            refresh[Math.Max(0, mark - 400)..mark],
            StringComparison.Ordinal);

        // ...and before the count that reports what was sent, or the components
        // it adds would be missing from that number.
        Assert.True(
            mark < refresh.IndexOf("int pendingBefore =", StringComparison.Ordinal),
            "the redraw must be marked before the sent-count is taken");
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
            {
                // Line endings normalised, because git rewrites them on
                // checkout and every anchor below would otherwise depend on
                // which machine cloned the repository.
                return File.ReadAllText(candidate, Encoding.UTF8).Replace("\r\n", "\n");
            }
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
