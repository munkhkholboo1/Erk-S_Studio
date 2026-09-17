using System.Text;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// An identity change empties the sheet library, so something must refill it.
///
/// 🔴 THE OWNER'S TWO COMPLAINTS ARE ONE EVENT. Switching between bot state and owner -
/// which they have been doing constantly because of the PIN defect - runs
/// ConfigureDeviceSeat, and signing in runs ConfigureSourceRuntimeContext. Both do the
/// same thing while a project is open: `Library.Clear()` and re-register the watchers.
/// The library is in memory and is the ONLY thing the received-sheet count and the album
/// read, so after a switch every local source reads «Хүлээн авсан: 0 sheet» and the album
/// stops updating. Deliveries keep arriving on disk and nothing ever reads them again.
///
/// 🔴 AND THE FLAG THAT LOOKS LIKE THE BUG IS NOT THE BUG. Both callers pass
/// `scanExistingPackages: false`, and flipping it to true is the obvious fix and the
/// wrong one: `WatchFolder` runs `ScanFolder` SYNCHRONOUSLY on the calling thread, and
/// these run on the UI thread. That would deserialize every manifest in every source
/// folder during sign-in and every seat switch - reintroducing the freeze the owner
/// already reported in these words: «энэ программ ингэтлээ гацаад байвал хэн ч
/// хэрэглэхгүй». The `false` is protecting the UI thread and is correct.
///
/// What is missing is the FOLLOW-UP: nothing schedules the background rescan that the
/// project-open path already runs off the UI thread. So the fix is an announcement, not
/// a flag - the clear says so, and the shell answers on a worker.
///
/// ⚠ A cleared library that nobody refills is `absence-must-not-be-a-value`: it reads as
/// «this source received nothing», which is indistinguishable from the truth, «nobody has
/// looked since».
/// </summary>
public sealed class ACLEAREDLibraryMustBeREFILLEDTests
{
    [Fact]
    public void ASEATChangeOnAnOpenProjectANNOUNCESThatItClearedTheLibrary()
    {
        using var state = new AppState();
        state.OpenProject(NewProjectPath());

        var announced = 0;
        state.SourceWatchersReset += () => announced++;

        state.ConfigureDeviceSeat("bot-seat@example.com");

        Assert.Equal(1, announced);
        Assert.Empty(state.Library.Snapshot());
    }

    [Fact]
    public void ASIGNINOnAnOpenProjectAnnouncesTheSameThing()
    {
        // The other half of the pair. Two methods, identical bodies, and only one of them
        // being wired would leave the defect alive on whichever path was forgotten.
        using var state = new AppState();
        state.OpenProject(NewProjectPath());

        var announced = 0;
        state.SourceWatchersReset += () => announced++;

        state.ConfigureSourceRuntimeContext("owner@example.com", "fingerprint");

        Assert.Equal(1, announced);
    }

    [Fact]
    public void ANUNCHANGEDIdentityAnnouncesNOTHING()
    {
        // 🔴 THE POSITIVE CONTROL THAT KEEPS THIS FROM BECOMING A RESCAN STORM.
        // UpdateAccountUi calls ConfigureSourceRuntimeContext on every account refresh,
        // and those are frequent. Both methods already return early when the identity is
        // unchanged; announcing regardless would schedule a full background scan on every
        // one of them - trading a stale library for a machine that never stops scanning.
        using var state = new AppState();
        state.OpenProject(NewProjectPath());
        state.ConfigureSourceRuntimeContext("owner@example.com", "fingerprint");

        var announced = 0;
        state.SourceWatchersReset += () => announced++;

        state.ConfigureSourceRuntimeContext("owner@example.com", "fingerprint");
        state.ConfigureSourceRuntimeContext("OWNER@EXAMPLE.COM", "FINGERPRINT");

        Assert.Equal(0, announced);
    }

    [Fact]
    public void WITHNoProjectOpenThereIsNothingToClearAndNothingToSay()
    {
        // The startup order: the session restores before any project is opened, so this
        // fires with nothing open. Announcing there would ask the shell to rescan a
        // project that does not exist.
        using var state = new AppState();

        var announced = 0;
        state.SourceWatchersReset += () => announced++;

        state.ConfigureSourceRuntimeContext("owner@example.com", "fingerprint");

        Assert.Equal(0, announced);
    }

    [Fact]
    public void THESHELLAnswersTheAnnouncementOnAWORKERNotTheUIThread()
    {
        // ⚠ SOURCE-ANCHORED, AND THE LIMIT IS NAMED: the shell needs a WPF dispatcher and
        // an open project. What is held is that the announcement is subscribed and that
        // the answer is the project-open rescan - which does its work inside
        // `await Task.Run(...)`, off the UI thread. The behavioural half is above.
        // 🔴 THE HANDLER, NOT THE FILE - AND SABOTAGE IS WHY. A first version searched
        // the whole of ShellView.cs for RescanOpenedProjectPackagesAsync, which is DEFINED
        // there; replacing the handler's body with SetStatus("") left the definition in
        // place and passed. That is the third time today a Contains was satisfied by a
        // span that had nothing to do with the claim - a comment twice, and a method
        // declaration here.
        string shell = ReadAppSource("ShellView.cs").Replace("\r\n", "\n");
        const string anchor = "state.SourceWatchersReset +=";
        int at = shell.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at > 0, "the announcement is not subscribed at all");

        int end = shell.IndexOf("\n        state.", at + anchor.Length, StringComparison.Ordinal);
        Assert.True(end > at, "the end of the handler was not found");
        string handler = new string(shell[at..end].Where(c => !char.IsWhiteSpace(c)).ToArray());

        Assert.Contains("RescanOpenedProjectPackagesAsync(", handler, StringComparison.Ordinal);

        // And it is scheduled onto the dispatcher rather than run where the announcement
        // arrives, so the scan cannot land on whichever thread changed the identity.
        Assert.Contains("dispatcher.BeginInvoke", handler, StringComparison.Ordinal);

        // 🔴 AND THE FLAG STAYS FALSE. This is the assertion that stops the tempting fix
        // coming back: `WatchFolder` scans synchronously, so passing true here would run
        // every manifest in every source folder on the UI thread during a seat switch.
        string appState = ReadAppSource("AppState.cs");
        string stateCompact = new string(appState.Where(c => !char.IsWhiteSpace(c)).ToArray());

        Assert.Contains("ResetRuntimeServices(scanExistingPackages:false)", stateCompact, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ResetRuntimeServices(scanExistingPackages:true)", stateCompact, StringComparison.Ordinal);
    }

    private static string NewProjectPath()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "erks-cleared-library-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);

        ProjectWorkspace project = ProjectWorkspaceStore.Create(
            "TEST-001", "Номын сангийн туршилт");
        string path = Path.Combine(directory, ProjectWorkspace.DefaultFileName);
        ProjectWorkspaceStore.Save(project, path);
        return path;
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
