using ErkS.Platform.Core;
﻿using System.Windows;
using System.Windows.Controls;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    /// <summary>
    /// Applies this machine's seat, if it holds one, to the runtime identity.
    ///
    /// Called wherever the account UI is refreshed, because that is also where
    /// the signed-in person changes - and the seat must survive that. A machine
    /// seated to an organisation keeps owning and receiving for the seat while
    /// somebody signs in with their own account; without this call the two
    /// would collapse back into one and the seat would stop receiving the
    /// moment an employee looked at their own projects.
    /// </summary>
    /// <summary>
    /// The seat identity, once the PIN has opened it in this run. Null on a
    /// machine that holds no seat, and on a seated one that is still locked -
    /// which is the point of sealing it: a locked machine does not act as the
    /// seat at all.
    /// </summary>
    private string? unlockedSeatIdentity;

    /// <summary>
    /// What this seat is assigned to, as the server answered. NULL means the
    /// answer is not in hand - and that hides everything, because "not read
    /// yet" must never read as "no restriction".
    /// </summary>
    private IReadOnlySet<string>? botAssignedProjectIds;

    /// <summary>
    /// What the SEAT may do, project by project, in the server's own scope
    /// words. Null until the seat has resumed - and null means nothing is
    /// granted, never "fall back to whoever is signed in".
    /// </summary>
    private IReadOnlyDictionary<string, IReadOnlyCollection<string>>? botAssignedProjectScopes;

    /// <summary>Who is appointed to this seat, or null when nobody is yet.</summary>
    private StudioCloudBotSeatMember? botSeatMember;

    /// <summary>
    /// Which authority this session acts with. The seat's, whenever the machine
    /// holds one - never a blend of the two.
    /// </summary>
    private StudioSessionKind SessionKind =>
        SeatedAsBot ? StudioSessionKind.BotSeat : StudioSessionKind.Personal;

    /// <summary>
    /// Whether the CURRENT session may do something in the open project.
    ///
    /// One place, so the rule cannot be spelled differently in two windows: a
    /// seated machine is judged by its seat's assignment, a person by their own
    /// participation, and neither borrows from the other.
    /// </summary>
    private bool HasProjectScope(string scope)
    {
        if (!state.HasOpenProject)
            return false;

        string projectId = state.Project.Cloud.ServerProjectId ?? "";
        IReadOnlyCollection<string>? seatScopes = null;
        if (botAssignedProjectScopes is not null &&
            !string.IsNullOrWhiteSpace(projectId) &&
            botAssignedProjectScopes.TryGetValue(projectId.Trim(), out IReadOnlyCollection<string>? found))
        {
            seatScopes = found;
        }
        else if (botAssignedProjectScopes is not null)
        {
            // The seat has answered and this project is not in the answer.
            // That is "nothing here", not "unknown".
            seatScopes = [];
        }

        return StudioEffectiveAuthority.Allows(
            SessionKind,
            state.Project.Cloud.PermissionSnapshotBelongsTo(account.Current?.Email)
                ? state.Project.Cloud.CurrentUserScopes
                : null,
            seatScopes,
            scope);
    }

    /// <summary>True while this machine is seated, whether or not it is unlocked.</summary>
    private bool SeatedAsBot => StudioBotDeviceStateStore.Read() is not null;


    /// <summary>
    /// Whether seat management may be offered at all: creating, releasing,
    /// deleting, changing the PIN, inviting members. These are the LICENCE
    /// OWNER's actions - the owner contains the bot, never the other way round -
    /// so on a seated machine they exist only for a verified owner.
    ///
    /// The way back in is the passport - the FULL sign-in, never the PIN. On a
    /// seated machine that is exactly what an owner session means: entering bot
    /// state erases the owner credential, and the seat's own token is not a
    /// session at all, so account.IsSignedIn can only have become true because
    /// somebody typed their whole passport at this keyboard.
    ///
    /// This used to be a separate latch that one method set. The latch was true
    /// and the menu still showed the old entries, because the menu was built
    /// once at start-up - so the door opened onto nothing. A fact that is read
    /// where it is needed cannot go stale that way.
    ///
    /// Leaving bot state sits behind this too: a seat that can release itself is
    /// a seat that manages itself.
    /// </summary>
    private bool MayManageSeats => !SeatedAsBot || account.IsSignedIn;

    /// <summary>
    /// Refuses a seat-management action and says why. Called by each action for
    /// itself: a hidden menu item is not a boundary.
    /// </summary>
    private bool RefuseSeatManagementWhenSeated()
    {
        if (MayManageSeats)
            return false;

        SetStatus(
            "Энэ төхөөрөмж ботын суудалд байна. Суудлын удирдлагыг " +
            "зөвхөн лиценз эзэмшигч хийнэ — түгжээний дэлгэцээс " +
            "«Эзэмшигчээр нэвтрэх»-ээр орно уу.");
        return true;
    }

    private bool MaySeeProject(string? projectId) =>
        StudioBotProjectVisibility.IsVisible(SeatedAsBot, botAssignedProjectIds, projectId);

    /// <summary>
    /// The identity the project file at <paramref name="path"/> claims for
    /// itself, or null when it cannot be read. Null refuses: a machine holding
    /// a seat has no business opening something it cannot identify.
    /// </summary>
    private static string? ReadProjectIdentity(string path)
    {
        try
        {
            ProjectWorkspace project = ProjectWorkspaceStore.Load(path);
            return string.IsNullOrWhiteSpace(project.Cloud.ServerProjectId)
                ? project.ProjectId
                : project.Cloud.ServerProjectId;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Whether this seat may open the project a route is about to open. True
    /// on a machine that holds no seat, so nothing changes for a person.
    ///
    /// EVERY route that ends with a project on screen calls this, and each one
    /// calls it for itself rather than trusting the route before it. The first
    /// version of the gate sat on the local-file route alone; the cloud route
    /// branches away one line earlier, so a project that was never assigned
    /// opened in full, album and all. A row on this screen is not a right to
    /// the project, and neither is a file on this disk.
    ///
    /// The file is asked when there is one, because it carries its own
    /// identity; a cloud-only project has no file yet, so there the row's
    /// server id is all there is - and it is the id the server itself would
    /// check.
    /// </summary>
    private bool SeatMayOpen(string? serverProjectId, string? path)
    {
        if (!SeatedAsBot)
            return true;

        bool hasFile = !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path);
        bool allowed = StudioBotProjectVisibility.MayOpen(
            seatedAsBot: true,
            botAssignedProjectIds,
            hasFile,
            hasFile ? ReadProjectIdentity(path!) : null,
            serverProjectId);
        if (!allowed)
            SetStatus(StudioBotProjectVisibility.ExplainRefusal(botAssignedProjectIds));
        return allowed;
    }

    /// <summary>Holds the shell, and the bot lock on top of it while seated.</summary>
    private Grid? botLockHost;
    private BotLockScreen? botLockScreen;

    /// <summary>
    /// Covers the shell with the bot tile when this machine holds a seat. The
    /// shell underneath is built and live either way - what the lock withholds
    /// is the SEAT: until the PIN opens it, ConfigureDeviceSeat gets nothing
    /// and the machine does not own or receive as the bot.
    /// </summary>
    private void InstallBotLockIfSeated()
    {
        StudioBotDeviceState? seat = StudioBotDeviceStateStore.Read();
        if (seat is null || botLockHost is null)
            return;

        // Called from more than one place now - start-up, seating, and the
        // switch back - so it has to be safe to ask twice. Two overlays would
        // leave a PIN box that unlocks nothing behind another that does.
        if (botLockScreen is not null)
            return;

        // The organisation id is what the seat carries offline; the readable
        // name needs the server, which a locked machine cannot reach. Showing
        // the id is honest - showing nothing would leave the person guessing
        // which organisation handed them this machine.
        botLockScreen = new BotLockScreen(seat, seat.OrganizationId);
        botLockScreen.Unlocked += async identity =>
        {
            unlockedSeatIdentity = identity;
            ApplyDeviceSeat();
            RemoveBotLock();
            UpdateAccountUi();
            SetStatus($"«{seat.DisplayName}» ботын суудлаар нээгдлээ.");
            await ResumeAsBotAsync(seat);
        };
        // The same door as the account menu's, in the place a locked machine
        // shows it. One step, so the two cannot drift apart.
        botLockScreen.OwnerSignInRequested += async () =>
            await VerifyOwnerOnSeatedDeviceAsync();
        botLockScreen.LockedOut += async () => await ReportBotLockoutAsync();
        botLockHost.Children.Add(botLockScreen);
    }

    private void RemoveBotLock()
    {
        if (botLockScreen is null || botLockHost is null)
            return;
        botLockHost.Children.Remove(botLockScreen);
        botLockScreen = null;
    }

    /// <summary>
    /// Tells the server this device locked itself, so the owner can clear it
    /// remotely. Needs a session; on a seated machine there is none until the
    /// bot token exists, so a failure here is reported and not hidden - the
    /// lock itself already holds locally.
    /// </summary>
    /// <summary>
    /// Picks up the seat's own credential and reads what it may open. Runs only
    /// after the PIN, because the credential is what the PIN was guarding.
    /// </summary>
    private async Task ResumeAsBotAsync(StudioBotDeviceState seat)
    {
        try
        {
            // ISSUE, not renew. The renewal route proves possession of a token
            // this machine does not have on a cold start - the gap both sides
            // measured on 2026-09-05. This one proves the machine itself, with a
            // signature, and works after a restart or a month offline.
            await account.IssueBotSessionAsync();
            StudioCloudBotStateResume resumed = await account.ResumeAsBotAsync();
            if (resumed.PinLocked)
            {
                SetStatus("Энэ суудал серверт түгжээтэй байна. Эзэмшигч алсаас тайлна.");
                return;
            }
            botAssignedProjectIds = resumed.AssignedProjects
                .Select(item => item.ProjectId?.Trim() ?? "")
                .Where(item => item.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            botSeatMember = resumed.Member;
            botAssignedProjectScopes = resumed.AssignedProjects
                .Where(item => !string.IsNullOrWhiteSpace(item.ProjectId))
                .GroupBy(item => item.ProjectId.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => (IReadOnlyCollection<string>)[.. group.SelectMany(item => item.Scopes)],
                    StringComparer.OrdinalIgnoreCase);
            await RefreshProjectsAsync();
            // The roles are shown, never acted on. They are what the person
            // asked for when they accepted, and seeing them is how anybody can
            // tell the appointment arrived at all - the server does not yet
            // GRANT anything through a seat, so nothing else would show it.
            string appointment = string.Join(
                "; ",
                resumed.AssignedProjects
                    .Take(3)
                    .Select(item => item.Roles.Count == 0
                        ? item.ProjectId
                        : item.ProjectId + ": " + string.Join(", ", item.Roles)));
            string more = resumed.AssignedProjects.Count > 3
                ? $" (+{resumed.AssignedProjects.Count - 3})"
                : "";
            string member = resumed.Member is null
                ? "гишүүн томилогдоогүй"
                : "гишүүн: " + (string.IsNullOrWhiteSpace(resumed.Member.DisplayName)
                    ? resumed.Member.AccountEmail
                    : resumed.Member.DisplayName);
            SetStatus(resumed.AssignedProjects.Count == 0
                ? $"«{seat.DisplayName}» — {member} · томилогдсон төсөл алга."
                : $"«{seat.DisplayName}» — {member} · {appointment}{more}");
        }
        catch (StudioAccountException released) when (BotSeatErrors.SeatIsGone(released))
        {
            // 🔴 THE SEAT ENDED WHILE THIS MACHINE WAS AWAY. Three ways it can
            // happen and the server names which - the owner freed it, the seat
            // was deleted, or somebody signed in as the owner here and took the
            // machine back. Keeping the local seat after any of them would leave
            // a machine claiming a seat that no longer exists, and asking for a
            // PIN that now guards nothing.
            //
            // Cleared locally FIRST and unconditionally, for the same reason
            // leaving bot state clears first: the one state a person cannot get
            // themselves out of is a seat the server has already ended.
            StudioBotDeviceStateStore.Clear();
            account.UseBotToken(null);
            unlockedSeatIdentity = null;
            botAssignedProjectIds = null;
            botAssignedProjectScopes = null;
            botSeatMember = null;
            ApplyDeviceSeat();
            UpdateAccountUi();
            SetStatus(BotSeatErrors.Describe(
                released,
                "Энэ төхөөрөмжийн суудал дуусгавар болсон байна."));
        }
        catch (Exception exception)
        {
            // The machine is unlocked locally either way; what is missing is
            // the server's half. Saying so beats a screen that looks ready and
            // quietly has no assignments behind it.
            SetStatus(BotSeatErrors.Describe(
                exception,
                "Ботын эрхээр сервертэй холбогдож чадсангүй. Локал ажил үргэлжилнэ."));
        }
    }

    private async Task ReportBotLockoutAsync()
    {
        try
        {
            await account.ReportBotLockoutAsync();
            SetStatus("Түгжигдсэнийг серверт мэдэгдэв. Эзэмшигч алсаас тайлна.");
        }
        catch (Exception exception)
        {
            SetStatus(
                "Энэ төхөөрөмж түгжигдлээ. Серверт мэдэгдэж чадсангүй (" +
                exception.Message + ") — эзэмшигчид өөрөө хэлнэ үү.");
        }
    }

    private void ApplyDeviceSeat() => state.ConfigureDeviceSeat(unlockedSeatIdentity);

    /// <summary>
    /// Registers this machine's device key, once per account.
    ///
    /// Runs right after a sign-in, deliberately: that is where a window and a
    /// person are, so a failure can be shown and acted on. Inside a plugin
    /// request there is no such place - and with SSO a plugin has no sign-in of
    /// its own to fall back to, so a failure there would stop three products
    /// with no button to press.
    ///
    /// A live session is also what makes the ordering safe: the ordinary
    /// validate has just carried this machine onto the trait-canonical
    /// fingerprint, so the key can be registered on top of it without stranding
    /// the records held under the older form.
    /// </summary>
    /// <summary>
    /// Registers this machine's key with the server if it is not already, and
    /// says whether the machine now HAS a registered key.
    ///
    /// The answer matters because seating a device is irreversible without it:
    /// a seated machine has no session left to register with. Everywhere else
    /// the result is ignored - an unregistered machine keeps working exactly as
    /// it did before keys existed.
    /// </summary>
    private async Task<bool> EnsureDeviceKeyRegisteredAsync()
    {
        string email = account.Current?.Email ?? "";
        if (string.IsNullOrWhiteSpace(email))
            return false;
        string fingerprint;
        try
        {
            fingerprint = StudioDeviceKeyStore.Fingerprint();
        }
        catch (Exception exception)
        {
            SetStatus("Энэ төхөөрөмжийн түлхүүр үүсгэгдсэнгүй: " + exception.Message);
            return false;
        }
        if (StudioDeviceKeyStore.IsRegistered(fingerprint, email))
        {
            StudioDeviceIdentity.UseRegisteredKeyFingerprint(fingerprint);
            return true;
        }

        try
        {
            StudioCloudDeviceKeyRegistration registered = await account.RegisterDeviceKeyAsync();
            StudioDeviceKeyStore.MarkRegistered(registered.DeviceFingerprint, email);
            return true;
        }
        catch (StudioAccountException exception)
            when (exception.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // A server that has not been updated has no such route. Nothing is
            // broken for ordinary work: this machine keeps using the fingerprint
            // it always has. Seating, however, must not go ahead - the device
            // would have no way to prove itself after a restart.
            SetStatus(
                "Сервер төхөөрөмжийн түлхүүрийн бүртгэлийг дэмжихгүй байна — " +
                "энэ хувилбарт өмнөх таних тэмдгээр үргэлжилнэ.");
            return false;
        }
        catch (Exception exception)
        {
            SetStatus("Төхөөрөмжийн түлхүүр бүртгэгдсэнгүй: " + exception.Message);
            return false;
        }
    }

    private static bool IsSeatedAsBot => StudioBotDeviceStateStore.Read() is not null;

    /// <summary>
    /// Builds the bot entries of the account menu. Both are hidden while signed
    /// out, and "make this a bot" is hidden on a machine that already holds a
    /// seat - one device, one seat.
    ///
    /// Hiding is not the protection: the server refuses an unlicensed create
    /// and names the refusal. The menu shows the road that works.
    /// </summary>
    private IEnumerable<MenuItem> BuildBotMenuItems()
    {
        // The rule itself lives in StudioBotMenuPlan, where it can be stated in
        // a test. This method only turns entries into controls.
        foreach (BotMenuEntry entry in StudioBotMenuPlan.For(SeatedAsBot, account.IsSignedIn))
        {
            MenuItem item = entry switch
            {
                BotMenuEntry.OwnerPassport => Item("Эзэмшигчээр нэвтрэх…", VerifyOwnerOnSeatedDeviceAsync),
                BotMenuEntry.ManageSeats => Item("Ботын суудлын удирдлага…", ShowBotManagementAsync),
                BotMenuEntry.SeatThisDevice => Item("Энэ төхөөрөмжийг суудалд суулгах…", SeatThisDeviceAsync),
                BotMenuEntry.EnterBotState => Item("Ботын төлөвт буцах…", EnterBotStateAsync),
                // Named for what it gives up, because it now stands beside an
                // entry that merely switches. Two lines both starting «бот»
                // and only one of them destructive is a mis-click that costs a
                // seat.
                BotMenuEntry.LeaveBotState => Item(
                    "Ботын суудлыг сулалж, төхөөрөмжийг чөлөөлөх…",
                    LeaveBotStateAsync),
                _ => throw new InvalidOperationException("Unknown bot menu entry: " + entry),
            };
            yield return item;
        }

        static MenuItem Item(string header, Func<Task> action)
        {
            var item = new MenuItem { Header = header };
            item.Click += async (_, _) => await action();
            return item;
        }
    }

    /// <summary>
    /// Asks for the owner's own credential on a seated machine and, if it is
    /// given, opens seat management for this run.
    ///
    /// The condition is deliberately the FULL sign-in and not the PIN: the PIN
    /// opens the seat, the passport opens the owner. Making the door reachable
    /// from the account menu changes WHERE it is, not what it asks for.
    /// </summary>
    private async Task VerifyOwnerOnSeatedDeviceAsync()
    {
        if (!await EnsureSignedInAsync())
        {
            SetStatus("Эзэмшигчээр нэвтрээгүй тул суудлын удирдлага нээгдсэнгүй.");
            return;
        }

        await ResumeAsOwnerNowAsync();

        // The chore runs first and speaks second: what this person just did is
        // the sentence they are waiting to read, and a queued seat release must
        // not be able to stand in its place.
        BotSeatFlushOutcome flushed = await FlushPendingBotSeatReleasesAsync();
        SetStatus(flushed.After("Эзэмшигчээр баталгаажлаа. Энэ төхөөрөмж ботын суудал хэвээр."));
    }

    /// <summary>
    /// Puts the running program back under the OWNER, and is the exact
    /// counterpart of <see cref="EnterBotStateNowAsync"/>.
    ///
    /// 🔴 THE DOOR WAS BUILT ONE WAY ROUND. Going INTO bot state ended the other
    /// session, dropped everything read for it, re-applied the device seat,
    /// rebuilt the project list and changed the screen - six things. Coming back
    /// out took the lock off and refreshed the account panel - two. So the owner
    /// signed in on their own machine and met the bot's assignments, the bot's
    /// member line and «Бот: <name>» where their name belongs, and said the only
    /// thing there is to say about it: «гэтэл бот төлөв хэвээрээ».
    ///
    /// The two directions now run the same list against the same fields, which
    /// is the only arrangement in which fixing one reaches the other.
    ///
    /// The seat is NOT released. This is a switch of who is acting on a machine
    /// that goes on holding its seat - «Ботын төлөвт буцах…» takes it back with
    /// the PIN, and nothing here needs the server.
    /// </summary>
    private async Task ResumeAsOwnerNowAsync()
    {
        // 🔴 THE SEAT'S CREDENTIAL GOES WITH THE SEAT'S READS. Going the other
        // way ends the owner's session with account.SignOut(); this direction
        // has to end the SEAT's, and did not - the machine kept a live bot token
        // while the owner was the one acting. Whichever identity is being left,
        // its credential leaves with it.
        //
        // It was missed because the test that pins the two directions together
        // compared the FIELDS each one clears, and a credential is dropped by a
        // CALL. The test now compares both.
        account.UseBotToken(null);

        // Read for the SEAT, by the seat's own credential. With the owner acting
        // they answer for somebody else - and an assignment list that belongs to
        // another identity is worse than none, because it looks like an answer.
        unlockedSeatIdentity = null;
        botAssignedProjectIds = null;
        botAssignedProjectScopes = null;
        botSeatMember = null;

        ApplyDeviceSeat();

        // Off BEFORE the list is rebuilt: work done behind a lock is work the
        // person never sees happen. Going the other way the lock goes on last,
        // for the mirror-image reason - what it covers is already correct.
        RemoveBotLock();
        UpdateAccountUi();

        // Rebuilt with the owner's session in hand, so their projects come back
        // instead of the seat's assignments staying on screen.
        await RefreshProjectsAsync();
    }

    private async Task ShowBotManagementAsync()
    {
        if (RefuseSeatManagementWhenSeated())
            return;
        // Seats this machine left behind are the owner's business, and this is
        // the screen they came to for exactly that - so here the chore IS the
        // answer and says so on its own.
        BotSeatFlushOutcome flushed = await FlushPendingBotSeatReleasesAsync();
        if (!flushed.IsSilent)
            SetStatus(flushed.Clause());
        var dialog = new BotSeatManagementDialog(account)
        {
            Owner = Window.GetWindow(Root),
        };
        dialog.ShowDialog();

        // 🔴 THE WINDOW BEHIND IT WAS NEVER TOLD. Releasing or deleting this
        // machine's own seat inside that dialog changes what this device is,
        // and the shell went on printing «Бот: <name>» until somebody restarted
        // Studio - the same shape as seating, where the disk changed and the
        // running program did not.
        //
        // Read back rather than assumed: the dialog reports no result and the
        // seat may or may not have moved, so the state on disk is the answer.
        StudioBotDeviceState? seat = StudioBotDeviceStateStore.Read();
        if (seat is null)
        {
            // The seat this machine held is gone. Whatever was read for it
            // answers for nothing now.
            unlockedSeatIdentity = null;
            botAssignedProjectIds = null;
            botAssignedProjectScopes = null;
            botSeatMember = null;
            ApplyDeviceSeat();
        }

        UpdateAccountUi();
    }

    private async Task SeatThisDeviceAsync()
    {
        if (RefuseSeatManagementWhenSeated())
            return;
        if (!await EnsureSignedInAsync())
            return;

        // The requirements are checked here, together, against facts read now.
        // The device key one used to ride along inside EnsureSignedInAsync,
        // which returns early when somebody is already signed in - so seating a
        // machine without a fresh sign-in skipped it silently, and the machine
        // only found out after a restart, when it could no longer be repaired.
        BotSeatingRefusal refusal = StudioBotSeatingRequirements.Check(
            alreadySeated: SeatedAsBot,
            ownerSignedIn: account.IsSignedIn,
            deviceKeyRegistered: await EnsureDeviceKeyRegisteredAsync());
        if (refusal != BotSeatingRefusal.None)
        {
            SetStatus(StudioBotSeatingRequirements.Describe(refusal));
            return;
        }
        // 🔴 A COMPANY USED TO BE A PRECONDITION OF SEATING A MACHINE, and
        // an owner with none was refused outright - «Ботын суудал үүсгэхэд
        // байгууллага шаардлагатай». The seat is spent against the owner's own
        // licence and never belonged to a company, so the fetch, the failure it
        // could produce, and the refusal it fed are all gone rather than made
        // optional. An owner with no company can seat a machine.
        var dialog = new BotSeatCreateDialog(account)
        {
            Owner = Window.GetWindow(Root),
        };
        if (dialog.ShowDialog() != true || dialog.Seated is null)
            return;

        // Only written once the server seated the device AND this machine's
        // owner credential was erased - the dialog does not report success
        // before both.
        StudioBotDeviceStateStore.Write(dialog.Seated);
        await EnterBotStateNowAsync(dialog.Seated);
        SetStatus(
            $"Энэ төхөөрөмж «{dialog.Seated.DisplayName}» ботын суудал боллоо. " +
            "Эзэмшигчийн нэвтрэлт энэ машинаас устсан. ПИН оруулна уу.");
    }

    /// <summary>
    /// Puts this RUNNING program into bot state, rather than only the disk.
    ///
    /// 🔴 SEATING USED TO CHANGE THE STORAGE AND LEAVE THE PROGRAM ALONE. The
    /// server erased the owner's credential and the seat was written to disk,
    /// while the window carried on as that person: their projects still listed,
    /// their menu still built, their session still live. The lock is installed
    /// by exactly one caller - start-up - so the owner's own guess was right:
    /// closing and reopening «fixed» it. They should not have to.
    ///
    /// The owner's session is ENDED rather than covered. A lock screen is a Grid
    /// laid on top; leaving a live session under it would run a person's rights
    /// behind the bot's PIN, which is the thing a seat exists to prevent - and
    /// the owner asked for their things to be «огт харагдахгүй», not hidden.
    ///
    /// One method, used by both ways in: seating a fresh machine and switching
    /// back after an owner sign-in. Two callers doing their own subset is how
    /// this went wrong - five identity paths already do five different things.
    /// </summary>
    private async Task EnterBotStateNowAsync(StudioBotDeviceState seat)
    {
        ArgumentNullException.ThrowIfNull(seat);

        // The owner stops being signed in HERE, in the running program.
        account.SignOut();

        // Locked, not merely seated: the seat identity is sealed under the PIN
        // and nothing has opened it yet. Anything read for the previous
        // identity goes with it rather than lingering as a stale answer.
        unlockedSeatIdentity = null;
        botAssignedProjectIds = null;
        botAssignedProjectScopes = null;
        botSeatMember = null;

        ApplyDeviceSeat();
        UpdateAccountUi();

        // Rebuilt with no session in hand, so what was the owner's is gone from
        // the list rather than sitting under the lock.
        await RefreshProjectsAsync();

        InstallBotLockIfSeated();
    }

    /// <summary>
    /// The way back into bot state after an owner sign-in took the lock off.
    ///
    /// The seat itself is untouched: this is a switch, not a release. The PIN is
    /// asked for by the lock screen, which already holds the attempt limit and
    /// the remote unlock - there is no second place that asks.
    /// </summary>
    private async Task EnterBotStateAsync()
    {
        StudioBotDeviceState? seat = StudioBotDeviceStateStore.Read();
        if (seat is null)
        {
            SetStatus("Энэ төхөөрөмж ботын суудалгүй тул ботын төлөвт шилжих зүйл алга.");
            return;
        }

        await EnterBotStateNowAsync(seat);
        SetStatus($"«{seat.DisplayName}» ботын төлөвт шилжлээ. ПИН оруулна уу.");
    }

    private async Task LeaveBotStateAsync()
    {
        if (RefuseSeatManagementWhenSeated())
            return;
        StudioBotDeviceState? seat = StudioBotDeviceStateStore.Read();
        if (seat is null)
            return;
        if (StudioMessageDialog.Show(
                Window.GetWindow(Root),
                $"«{seat.DisplayName}» ботын төлөвөөс гарах уу? Гарахад эзэмшигч " +
                "дахин нэвтрэх шаардлагатай — орох, гарах нэг хаалга.",
                "Ботын төлөвөөс гарах",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        // The owner still has to prove themselves - that condition is the whole
        // point of the seat and does not change here. What changes is what
        // happens AFTER: the device leaves whether or not the server answers.
        if (!await EnsureSignedInAsync())
        {
            SetStatus("Ботын төлөвөөс гарахад эзэмшигч нэвтрэх шаардлагатай.");
            return;
        }

        // LOCAL FIRST. The old order called the server and cleared the seat only
        // on success, so an unreachable server locked the machine in bot state
        // for good - the one state a person cannot get themselves out of.
        StudioBotDeviceStateStore.Clear();
        account.UseBotToken(null);
        unlockedSeatIdentity = null;
        botAssignedProjectIds = null;
        botAssignedProjectScopes = null;
        botSeatMember = null;
        ApplyDeviceSeat();
        UpdateAccountUi();

        try
        {
            await account.LeaveBotStateAsync(seat.BotId);
            StudioPendingBotSeatReleases.Forget(seat.OrganizationId, seat.BotId);
            SetStatus("Ботын төлөвөөс гарлаа.");
        }
        catch (Exception exception)
        {
            // The device is out. The SEAT is not - it is still occupied on the
            // server, and a seat nobody can see is the same defect one layer
            // over. So it is written down and retried, and if even the note
            // cannot be written the user is told the id to release by hand.
            bool noted = StudioPendingBotSeatReleases.Record(new PendingBotSeatRelease
            {
                OrganizationId = seat.OrganizationId,
                BotId = seat.BotId,
                DisplayName = seat.DisplayName,
                DeviceFingerprint = StudioDeviceIdentity.Fingerprints.Canonical,
                LeftAtUtc = DateTimeOffset.UtcNow,
                LastFailure = exception.Message,
            });
            SetStatus(noted
                ? "Ботын төлөвөөс гарлаа. Суудал серверт цуцлагдаагүй байна " +
                  $"({exception.Message}) — дараа нэвтэрэхэд дахин оролдоно."
                : "Ботын төлөвөөс гарлаа, ГЭХДЭЭ суудал серверт цуцлагдаагүй бөгөөд " +
                  $"тэмдэглэл ч хадгалагдсангүй. Эзэмшигч гараар чөлөөлнө үү: botId = {seat.BotId}");
        }
    }

    /// <summary>
    /// Retries the seat releases this machine left behind. Runs whenever an
    /// owner session is in hand, because that is the credential the release
    /// needs and the moment it is most likely to work.
    ///
    /// Silence is only correct when there is nothing to do: a retry that fails
    /// keeps its note and says so.
    /// </summary>
    private async Task<BotSeatFlushOutcome> FlushPendingBotSeatReleasesAsync()
    {
        IReadOnlyList<PendingBotSeatRelease> pending = StudioPendingBotSeatReleases.Read();
        if (pending.Count == 0 || !account.IsSignedIn)
            return BotSeatFlushOutcome.Nothing;

        int released = 0;
        int alreadyFree = 0;
        var stillHeld = new List<string>();
        foreach (PendingBotSeatRelease item in pending)
        {
            try
            {
                await account.LeaveBotStateAsync(item.BotId);
                StudioPendingBotSeatReleases.Forget(item.OrganizationId, item.BotId);
                released++;
            }
            catch (Exception exception) when (BotSeatErrors.SeatIsGone(exception))
            {
                // 🔴 THIS REFUSAL IS THE GOAL, NOT A FAILURE. The server answers
                // «this seat's device is not in bot state» when no device holds
                // the seat - which is precisely what a release is FOR. Treating
                // it as an error kept the entry forever, and this list is
                // flushed on every owner sign-in: the person signed in and the
                // last thing Studio said was a bot-seat refusal, on a machine
                // that had left bot state days earlier. It reads as «I signed in
                // as the owner and it came up as the bot», and the sign-in
                // message it replaced never appeared.
                //
                // The predicate for this already existed and nothing here called
                // it - the same shape three times today: the rule is written,
                // the caller does not ask.
                StudioPendingBotSeatReleases.Forget(item.OrganizationId, item.BotId);
                alreadyFree++;
            }
            catch (Exception exception)
            {
                // What this retry met, on the entry itself. Without it the note
                // froze at whatever the FIRST attempt said, and a stale sentence
                // read as today's evidence is how the wrong cause gets named.
                // The code goes down beside the sentence because the code is
                // what SeatIsGone reads - if an entry survives, the code on file
                // says immediately whether it was classified or missed.
                StudioPendingBotSeatReleases.NoteAttempt(
                    item.OrganizationId,
                    item.BotId,
                    exception is StudioAccountException known ? known.ErrorCode : "",
                    exception.Message);
                stillHeld.Add($"«{item.DisplayName}» ({exception.Message})");
            }
        }

        // Reported, not announced. This runs at every owner sign-in, and a
        // queued chore that writes the status line itself takes the place of
        // the answer to what the person actually did.
        return new BotSeatFlushOutcome(released, alreadyFree, stillHeld);
    }
}
