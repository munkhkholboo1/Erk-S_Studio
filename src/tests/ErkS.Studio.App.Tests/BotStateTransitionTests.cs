using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Becoming a bot must change the RUNNING program, not only the disk.
///
/// 🔴 THE OWNER FOUND THIS AND GUESSED THE CAUSE CORRECTLY. «Төхөөрөмжийг бот
/// болгоход хэрэглэгчийн төслүүд хэвээрээ харагдаж үлдэж байна. Ботын удирдлага
/// гэх мэт үйлдлүүд байсаар байна. Магадгүй студиог бүрэн хааж нээхэд
/// засагддаг байх. Гэхдээ хэрэглэгч ийм илүү үйлдэл хийхгүй.»
///
/// They were right about the mechanism: the seat was written to disk and the
/// server erased the owner's credential, while the window carried on as that
/// person. The lock screen has exactly one caller - start-up - so reopening
/// Studio was the only thing that applied the change.
///
/// These read the source because the transition needs a window, a signed-in
/// account service and a live shell. The assertions are aimed at the parts that
/// were missing, not at how they are spelled.
/// </summary>
public sealed class BotStateTransitionTests
{
    [Fact]
    public void SEATINGThisDeviceAppliesTheChangeImmediately()
    {
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private async Task SeatThisDeviceAsync()");

        // Written to disk AND applied to the running program, in that order.
        Assert.Contains("StudioBotDeviceStateStore.Write(", body, StringComparison.Ordinal);
        Assert.Contains("await EnterBotStateNowAsync(", body, StringComparison.Ordinal);
        Assert.True(
            body.IndexOf("StudioBotDeviceStateStore.Write(", StringComparison.Ordinal) <
                body.IndexOf("await EnterBotStateNowAsync(", StringComparison.Ordinal),
            "the seat must be stored before the program is switched to it");
    }

    [Fact]
    public void THETransitionENDSTheOwnersSessionRatherThanCoveringIt()
    {
        // 🔴 A LOCK SCREEN IS A GRID LAID ON TOP. Leaving a live owner session
        // under it would run a person's rights behind the bot's PIN - the exact
        // thing a seat exists to prevent - and the owner asked for their things
        // to be «огт харагдахгүй», which covering does not achieve.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private async Task EnterBotStateNowAsync(");

        Assert.Contains("account.SignOut();", body, StringComparison.Ordinal);
        Assert.Contains("await RefreshProjectsAsync();", body, StringComparison.Ordinal);
        Assert.Contains("InstallBotLockIfSeated();", body, StringComparison.Ordinal);

        // The list is rebuilt with no session in hand, so what belonged to the
        // owner is gone rather than sitting under the lock.
        Assert.True(
            body.IndexOf("account.SignOut();", StringComparison.Ordinal) <
                body.IndexOf("await RefreshProjectsAsync();", StringComparison.Ordinal),
            "the session must end before the project list is rebuilt, or the old list survives");
    }

    [Fact]
    public void THETransitionDropsWhatWasReadForTheOTHERIdentity()
    {
        // Assignments, scopes and the seat member were read for whoever was
        // signed in a moment ago. Kept, they answer for the wrong identity -
        // and «not read yet» must never behave as «no restriction».
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private async Task EnterBotStateNowAsync(");

        foreach (string field in new[]
        {
            "unlockedSeatIdentity = null;",
            "botAssignedProjectIds = null;",
            "botAssignedProjectScopes = null;",
            "botSeatMember = null;",
        })
        {
            Assert.Contains(field, body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void BOTHWaysIntoBotStateGoThroughTheSAMETransition()
    {
        // 🔴 FIVE IDENTITY PATHS ALREADY DO FIVE DIFFERENT SUBSETS OF THE SAME
        // WORK, which is how a half state gets written with nothing failing.
        // These two are the ones this change adds and touches, and they are held
        // to one method so a fix to either reaches both.
        string source = ReadAppSource("ShellView.BotSeat.cs");

        Assert.Contains(
            "await EnterBotStateNowAsync(",
            MethodBody(source, "private async Task SeatThisDeviceAsync()"),
            StringComparison.Ordinal);
        Assert.Contains(
            "await EnterBotStateNowAsync(",
            MethodBody(source, "private async Task EnterBotStateAsync()"),
            StringComparison.Ordinal);

        // Defined once, called twice: three occurrences in the file.
        Assert.Equal(3, Occurrences(source, "EnterBotStateNowAsync("));
    }

    [Fact]
    public void THELockIsSafeToInstallTwice()
    {
        // It has more than one caller now - start-up, seating, and the switch
        // back. Two overlays would leave a PIN box that unlocks nothing in front
        // of the one that does.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private void InstallBotLockIfSeated()");

        Assert.Contains("if (botLockScreen is not null)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEDestructiveEntryNamesWhatItGivesUpAndTheOtherOneDoesNot()
    {
        // It stands beside an entry that merely switches, and both concern the
        // bot. Two adjacent lines where only one is irreversible is a mis-click
        // that costs a seat.
        //
        // Read from the menu arms rather than pinned to today's words: the
        // rule is that the destructive label says what it gives up and the
        // switching one does not claim to, which has to keep holding when the
        // wording is improved. A test that pins the sentence goes red for
        // rewording and blind for the defect.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string switching = MenuLabel(source, "BotMenuEntry.EnterBotState");
        string destructive = MenuLabel(source, "BotMenuEntry.LeaveBotState");

        Assert.NotEqual(switching, destructive);
        foreach (string surrender in new[] { "сулал", "чөлөөл" })
        {
            Assert.True(
                destructive.Contains(surrender, StringComparison.Ordinal) ||
                    destructive.Contains("сулалж", StringComparison.Ordinal),
                "the entry that releases the seat does not say so: " + destructive);
            Assert.DoesNotContain(surrender, switching, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void THEMenuSeparatesTheSEATFromTheMACHINE()
    {
        // 🔴 ONE WORD NAMED TWO THINGS. «Бот» was the job position the
        // organisation pays for AND the machine standing in the office, so
        // «энэ төхөөрөмжийг бот болгох» read as though the computer turned
        // into something. It takes a SEAT; the seat is what exists without it.
        string source = ReadAppSource("ShellView.BotSeat.cs");

        foreach (string entry in new[]
        {
            "BotMenuEntry.ManageSeats",
            "BotMenuEntry.SeatThisDevice",
            "BotMenuEntry.LeaveBotState",
        })
        {
            string label = MenuLabel(source, entry);
            Assert.True(
                label.Contains("суудал", StringComparison.Ordinal) ||
                    label.Contains("суудл", StringComparison.Ordinal),
                entry + " is about a seat and does not say the word: " + label);
        }

        // And nothing offers to turn the machine INTO one.
        Assert.DoesNotContain("бот болгох", source, StringComparison.Ordinal);
    }

    /// <summary>
    /// The label on one menu arm, read out of the switch that builds it.
    /// </summary>
    private static string MenuLabel(string source, string entry)
    {
        int at = source.IndexOf(entry + " => Item(", StringComparison.Ordinal);
        Assert.True(at > 0, entry + " has no menu arm");
        int open = source.IndexOf('"', at);
        int close = source.IndexOf('"', open + 1);
        Assert.True(open > 0 && close > open, "the label for " + entry + " was not found");
        return source[(open + 1)..close];
    }

    [Fact]
    public void BOTHDirectionsDropTheSAMEFieldsAsEachOther()
    {
        // 🔴 THE DOOR WAS BUILT ONE WAY ROUND, AND A GREEN SUITE SAID NOTHING.
        // Every test here pointed at the way IN, so the way OUT could do a
        // quarter of the work and stay green: the owner signed in on their own
        // seated machine and met the bot's assignments, the bot's member line
        // and «Бот: <name>» in place of their name. «Гэтэл бот төлөв хэвээрээ».
        //
        // Derived, not listed. A hand-written list of the four fields would
        // stay true the day a FIFTH is added to one side only - which is the
        // exact way this defect was born.
        //
        // ⚠️ WHAT THIS CANNOT SEE: assignments only. A direction that fails to
        // CALL something - dropping a credential, say - passes here untouched,
        // and one did. EACHDirectionDropsTheCREDENTIALOfTheIdentityItLeaves is
        // the other half; neither is sufficient alone.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        IReadOnlyCollection<string> intoBotState =
            FieldsClearedIn(MethodBody(source, "private async Task EnterBotStateNowAsync("));
        IReadOnlyCollection<string> backToOwner =
            FieldsClearedIn(MethodBody(source, "private async Task ResumeAsOwnerNowAsync()"));

        // The positive control. Both empty would compare equal and prove
        // nothing - and empty is what a renamed method or a mis-parsed body
        // produces.
        Assert.True(
            intoBotState.Count >= 4,
            "no cleared fields were found going INTO bot state; this test is reading nothing");

        Assert.Equal(intoBotState.Order(), backToOwner.Order());
    }

    [Fact]
    public void EACHDirectionDropsTheCREDENTIALOfTheIdentityItLeaves()
    {
        // 🔴 THE TEST NEXT DOOR COMPARED FIELDS AND MISSED A CALL. It reads the
        // `x = null;` assignments out of both bodies and holds the sets equal -
        // which is worth having, and which structurally cannot see that one
        // direction ends a session and the other does not. The owner direction
        // shipped without dropping the seat's token: a machine kept a live bot
        // credential while the owner was the one acting.
        //
        // A credential is dropped by a CALL, so calls are what this compares.
        // The two directions are NOT symmetric here and must not be: each ends
        // the credential of the identity it is LEAVING, and they leave
        // different ones.
        // 🔴 AND IT READS CODE, NOT PROSE. The first run of this test went red
        // on the COMMENT that explains it - the sentence «the other way ends
        // the owner's session with account.SignOut()» contains the very call it
        // was asserting absent. A source-reading test that cannot tell a line
        // of code from a line about code produces false reds today and false
        // greens the moment somebody names a call in a comment.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string intoBotState = CodeOnly(MethodBody(source, "private async Task EnterBotStateNowAsync("));
        string backToOwner = CodeOnly(MethodBody(source, "private async Task ResumeAsOwnerNowAsync()"));

        // Into bot state: the PERSON's session ends.
        Assert.Contains("account.SignOut();", intoBotState, StringComparison.Ordinal);
        // Back to the owner: the SEAT's credential ends.
        Assert.Contains("account.UseBotToken(null);", backToOwner, StringComparison.Ordinal);

        // And neither may keep the other's alive by omission - the failure this
        // catches is a direction that drops nothing at all.
        Assert.DoesNotContain("account.SignOut();", backToOwner, StringComparison.Ordinal);
    }

    [Fact]
    public void THEOwnerDirectionREBUILDSTheScreenRatherThanUncoveringIt()
    {
        // Taking the lock off shows what was already there. The list underneath
        // was built for the seat, so it has to be built again for the person -
        // the same four steps the other direction runs, aimed the other way.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private async Task ResumeAsOwnerNowAsync()");

        Assert.Contains("ApplyDeviceSeat();", body, StringComparison.Ordinal);
        Assert.Contains("RemoveBotLock();", body, StringComparison.Ordinal);
        Assert.Contains("UpdateAccountUi();", body, StringComparison.Ordinal);
        Assert.Contains("await RefreshProjectsAsync();", body, StringComparison.Ordinal);

        // The lock comes off BEFORE the list is rebuilt: work done behind a
        // lock is work nobody sees happen.
        Assert.True(
            body.IndexOf("RemoveBotLock();", StringComparison.Ordinal) <
                body.IndexOf("await RefreshProjectsAsync();", StringComparison.Ordinal),
            "the lock must come off before the rebuild, or the refresh happens behind it");
    }

    [Fact]
    public void SIGNINGInAsTheOwnerGOESThroughThatTransition()
    {
        // The door from the lock screen and the door from the account menu are
        // one method, and that method is where the whole list lives. Two callers
        // each doing their own subset is how this went wrong the first time.
        string source = ReadAppSource("ShellView.BotSeat.cs");

        Assert.Contains(
            "await ResumeAsOwnerNowAsync();",
            MethodBody(source, "private async Task VerifyOwnerOnSeatedDeviceAsync()"),
            StringComparison.Ordinal);

        // Defined once, called once: two occurrences in the file.
        Assert.Equal(2, Occurrences(source, "ResumeAsOwnerNowAsync()"));
    }

    [Fact]
    public void THESwitchBackDoesNOTGiveUpTheSeat()
    {
        // ⚠️ A SWITCH, NOT A RELEASE. The machine goes on holding its seat and
        // «Ботын төлөвт буцах…» takes it back with the PIN. Releasing here would
        // turn signing in as the owner into an irreversible act nobody asked
        // for - and it needs the server, which this must not.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private async Task ResumeAsOwnerNowAsync()");

        Assert.DoesNotContain("LeaveBotStateAsync", body, StringComparison.Ordinal);
        Assert.DoesNotContain("StudioBotDeviceStateStore.Clear", body, StringComparison.Ordinal);
        Assert.DoesNotContain("StudioPendingBotSeatReleases.Record", body, StringComparison.Ordinal);
    }

    /// <summary>
    /// The fields a transition sets back to null, read out of its body.
    ///
    /// Reading them rather than listing them is the whole point: the comparison
    /// has to keep holding when a field is added, and a list written today
    /// stays true on the day somebody adds a fifth to one side.
    /// </summary>
    private static IReadOnlyCollection<string> FieldsClearedIn(string body)
    {
        var found = new List<string>();
        foreach (string line in body.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.EndsWith(" = null;", StringComparison.Ordinal))
                continue;
            string name = trimmed[..^" = null;".Length].Trim();
            // A bare field name. Anything with a dot, a cast or a call in it is
            // not the shape this compares.
            if (name.Length > 0 && name.All(c => char.IsLetterOrDigit(c) || c == '_'))
                found.Add(name);
        }
        return found;
    }

    /// <summary>
    /// The body with its line comments removed, so an assertion about a CALL
    /// cannot be answered by a sentence that merely names one.
    /// </summary>
    private static string CodeOnly(string body)
    {
        var kept = new List<string>();
        foreach (string line in body.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal))
                continue;
            int comment = line.IndexOf("//", StringComparison.Ordinal);
            kept.Add(comment < 0 ? line : line[..comment]);
        }
        return string.Join("\n", kept);
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return source[start..end];
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

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
