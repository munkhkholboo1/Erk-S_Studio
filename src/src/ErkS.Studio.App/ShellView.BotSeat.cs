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
            await account.RequestBotTokenAsync();
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
            await RefreshProjectsAsync();
            SetStatus(resumed.AssignedProjects.Count == 0
                ? $"«{seat.DisplayName}» — томилогдсон төсөл алга."
                : $"«{seat.DisplayName}» — {resumed.AssignedProjects.Count} төсөлд томилогдсон.");
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
                BotMenuEntry.ManageSeats => Item("Ботын удирдлага…", ShowBotManagementAsync),
                BotMenuEntry.SeatThisDevice => Item("Энэ төхөөрөмжийг бот болгох…", SeatThisDeviceAsync),
                BotMenuEntry.LeaveBotState => Item("Ботын төлөвөөс гарах…", LeaveBotStateAsync),
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

        RemoveBotLock();
        UpdateAccountUi();
        SetStatus("Эзэмшигчээр баталгаажлаа. Энэ төхөөрөмж ботын суудал хэвээр.");
        await FlushPendingBotSeatReleasesAsync();
    }

    private async Task<IReadOnlyList<StudioCloudOrganization>?> LoadOrganizationsAsync()
    {
        if (!await EnsureSignedInAsync())
            return null;
        try
        {
            IReadOnlyList<StudioCloudOrganization> organizations =
                await account.ListOrganizationsAsync();
            if (organizations.Count != 0)
                return organizations;
            SetStatus("Ботын суудал үүсгэхэд байгууллага шаардлагатай.");
            return null;
        }
        catch (Exception exception)
        {
            SetStatus("Байгууллагын жагсаалт уншигдсангүй: " + exception.Message);
            return null;
        }
    }

    private async Task<StudioCloudOrganization?> PickOrganizationAsync()
    {
        if (!await EnsureSignedInAsync())
            return null;
        try
        {
            IReadOnlyList<StudioCloudOrganization> organizations =
                await account.ListOrganizationsAsync();
            if (organizations.Count == 0)
            {
                SetStatus("Ботын суудал үүсгэхэд байгууллага шаардлагатай.");
                return null;
            }
            return organizations[0];
        }
        catch (Exception exception)
        {
            SetStatus("Байгууллагын жагсаалт уншигдсангүй: " + exception.Message);
            return null;
        }
    }

    private async Task ShowBotManagementAsync()
    {
        if (RefuseSeatManagementWhenSeated())
            return;
        // Seats this machine left behind are the owner's business, and this is
        // the screen they came to for exactly that.
        await FlushPendingBotSeatReleasesAsync();
        IReadOnlyList<StudioCloudOrganization>? organizations = await LoadOrganizationsAsync();
        if (organizations is null)
            return;
        var dialog = new BotSeatManagementDialog(account, organizations)
        {
            Owner = Window.GetWindow(Root),
        };
        dialog.ShowDialog();
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
        IReadOnlyList<StudioCloudOrganization> organizations;
        try
        {
            organizations = await account.ListOrganizationsAsync();
        }
        catch (Exception exception)
        {
            SetStatus("Байгууллагын жагсаалт уншигдсангүй: " + exception.Message);
            return;
        }
        if (organizations.Count == 0)
        {
            SetStatus("Ботын суудал үүсгэхэд байгууллага шаардлагатай.");
            return;
        }

        var dialog = new BotSeatCreateDialog(account, organizations)
        {
            Owner = Window.GetWindow(Root),
        };
        if (dialog.ShowDialog() != true || dialog.Seated is null)
            return;

        // Only written once the server seated the device AND this machine's
        // owner credential was erased - the dialog does not report success
        // before both.
        StudioBotDeviceStateStore.Write(dialog.Seated);
        ApplyDeviceSeat();
        UpdateAccountUi();
        SetStatus(
            $"Энэ төхөөрөмж «{dialog.Seated.DisplayName}» ботын суудал боллоо. " +
            "Эзэмшигчийн нэвтрэлт энэ машинаас устсан.");
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
        ApplyDeviceSeat();
        UpdateAccountUi();

        try
        {
            await account.LeaveBotStateAsync(seat.OrganizationId, seat.BotId);
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
    private async Task FlushPendingBotSeatReleasesAsync()
    {
        IReadOnlyList<PendingBotSeatRelease> pending = StudioPendingBotSeatReleases.Read();
        if (pending.Count == 0 || !account.IsSignedIn)
            return;

        int released = 0;
        var stillHeld = new List<string>();
        foreach (PendingBotSeatRelease item in pending)
        {
            try
            {
                await account.LeaveBotStateAsync(item.OrganizationId, item.BotId);
                StudioPendingBotSeatReleases.Forget(item.OrganizationId, item.BotId);
                released++;
            }
            catch (Exception exception)
            {
                stillHeld.Add($"«{item.DisplayName}» ({exception.Message})");
            }
        }

        if (stillHeld.Count > 0)
        {
            SetStatus($"Цуцлагдаагүй ботын суудал: {string.Join(", ", stillHeld)}");
            return;
        }
        if (released > 0)
            SetStatus($"Өмнө цуцлагдаагүй {released} ботын суудал серверт чөлөөлөгдлөө.");
    }
}
