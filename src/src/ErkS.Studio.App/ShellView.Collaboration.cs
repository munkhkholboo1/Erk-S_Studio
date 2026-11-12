using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ErkS.CloudEra.Client;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private readonly ComboBox projectArchitectBox = new()
    {
        MinWidth = 360,
        MaxWidth = 620,
        HorizontalAlignment = HorizontalAlignment.Left,
        DisplayMemberPath = nameof(ProjectArchitectOption.Label),
    };
    private readonly Button assignProjectArchitectButton = StudioWidgets.CreateGlyphTextButton(
        "\uE73E",
        "Баталгаажуулах",
        "Сонгосон бүртгэлтэй оролцогчийн архитекторын томилгоо ба profile нэрийг баталгаажуулах",
        primary: true);
    private readonly TextBlock projectArchitectSummaryText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = StudioTheme.MutedTextBrush,
    };
    private bool assigningProjectArchitect;
    private readonly Button notificationsButton = StudioWidgets.CreateGlyphTextButton(
        "\uE7F4",
        "Мэдэгдэл",
        "Багийн урилга болон төсөл үүсгэх эрх");
    private readonly Button inviteTeamMemberButton = StudioWidgets.CreateGlyphTextButton(
        "\uE710",
        "Гишүүн урих",
        "Бүртгэлтэй хэрэглэгчид багийн урилга илгээх",
        primary: true);
    private readonly Button editTeamMemberRolesButton = StudioWidgets.CreateGlyphTextButton(
        "\uE70F",
        "Үүрэг засах",
        "Сонгосон багийн гишүүний төслийн role-уудыг өөрчлөх");
    private readonly Button removeTeamMemberButton = StudioWidgets.CreateButton("Багаас хасах");
    private readonly Button leaveProjectButton = StudioWidgets.CreateButton("Төслөөс гарах хүсэлт");
    private readonly Button projectLifecycleButton = StudioWidgets.CreateGlyphTextButton(
        "\uE74D",
        "Төслийн үйлдэл",
        "Сонгосон төслийг устгах эсвэл гарах хүсэлт илгээх");
    private StudioProjectMembershipInvitationListResponse notificationInvitations = new();
    private StudioCloudBotInvitationListResponse notificationBotInvitations = new();
    private StudioFriendsResponse notificationFriends = new();
    private StudioProjectMembershipExitRequestListResponse notificationExitRequests = new();
    private long notificationAccountEpoch = -1;
    private bool refreshingNotifications;
    private bool refreshingCurrentProjectAccess;
    private bool updatingTeamMemberRoles;

    private void UpdateSelectedProjectLifecycleAction()
    {
        ProjectRow? selected = projectsList.SelectedItem as ProjectRow;
        bool canDelete = selected?.CanDelete == true;
        bool canLeave = selected?.CanLeave == true;
        projectLifecycleButton.IsEnabled = account.IsSignedIn && (canDelete || canLeave);
        string label = canDelete
            ? "Төсөл устгах"
            : canLeave ? "Төслөөс гарах" : "Төслийн үйлдэл";
        if (projectLifecycleButton.Content is StackPanel stack &&
            stack.Children.Count > 1 &&
            stack.Children[1] is TextBlock text)
        {
            text.Text = label;
        }
        projectLifecycleButton.ToolTip = canDelete
            ? "Cloud төслийг soft-delete хийх; локал файлууд хэвээр үлдэнэ"
            : canLeave
                ? "Байгууллагад төслөөс гарах хүсэлт илгээх"
                : "Эхлээд Cloud төсөл сонгоно уу";
    }

    /// <param name="row">
    /// The project to act on. The card's own menu names it; without that the
    /// action reads the list's selection, which is not the project the menu was
    /// opened on whenever the list is showing something else.
    /// </param>
    private async Task RunSelectedProjectLifecycleActionAsync(ProjectRow? row = null)
    {
        ProjectRow? selected = row ?? projectsList.SelectedItem as ProjectRow;
        if (!account.IsSignedIn || selected is null)
            return;

        if (selected.CanDelete)
        {
            if (!StudioRelationshipBoundary.Confirm(
                    Window.GetWindow(Root),
                    StudioRelationshipAction.DeleteProject,
                    $"{selected.Code} · {selected.Name}"))
            {
                return;
            }
            var dialog = new ProjectDeletionDialog(selected.Code, selected.Name)
            {
                Owner = Window.GetWindow(Root),
            };
            if (dialog.ShowDialog() != true)
                return;
            try
            {
                await account.DeleteProjectAsync(
                    selected.ServerProjectId,
                    selected.Code,
                    dialog.Reason);
                if (state.HasOpenProject &&
                    state.Project.Cloud.ServerProjectId.Equals(selected.ServerProjectId, StringComparison.OrdinalIgnoreCase))
                {
                    CloseCurrentCloudProjectAfterAccessEnded(
                        "Төсөл идэвхтэй Cloud жагсаалтаас устгагдлаа. Локал файлууд хэвээр үлдсэн.");
                }
                await RefreshProjectsAsync();
                SetStatus("Төсөл идэвхтэй Cloud жагсаалтаас устгагдлаа. Canonical мэдээлэл ба аудитын түүх хадгалагдсан.");
            }
            catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
            {
                ReportWriteRefusal("Төсөл устгагдсангүй", exception);
            }
            return;
        }

        if (!selected.CanLeave || !StudioRelationshipBoundary.Confirm(
                Window.GetWindow(Root),
                StudioRelationshipAction.RequestProjectExit,
                $"{selected.Code} · {selected.CompanyLabel}"))
        {
            return;
        }
        try
        {
            StudioProjectMembershipExitRequest request = await account.RequestProjectExitAsync(
                selected.ServerProjectId,
                "Studio төслийн жагсаалтаас гарах хүсэлт илгээв.");
            await RefreshNotificationsAsync();
            UpdateSelectedProjectLifecycleAction();
            SetStatus(
                $"Гарах хүсэлтийг {request.ApprovalOrganizationName} байгууллагад илгээлээ. " +
                "Зөвшөөрөх хүртэл төсөл таны жагсаалтад хэвээр байна.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            ReportWriteRefusal("Төслөөс гарах хүсэлт илгээгдсэнгүй", exception);
        }
    }

    private async Task<StudioCloudProjectRefreshResult> InspectCurrentProjectCloudChangesAsync(
        string projectId)
    {
        ProjectCloudLink cloud = state.Project.Cloud;
        string knownToken = !string.IsNullOrWhiteSpace(cloud.LastServerConcurrencyToken)
            ? cloud.LastServerConcurrencyToken
            : cloud.ServerSnapshot.ConcurrencyToken;
        // ⚠ The project's ETag follows the project, not the caller's rights: a permission the
        // server changes on its own (the platform Owner's, 2026-10-31) leaves the project
        // unchanged, the answer 304, and the cached scopes stale for good. So the first look at
        // a project in each signed-in session fetches it whole; later ones may use the ETag.
        string permissionKey = $"{account.SessionEpoch}|{projectId}";
        bool forceFullRefresh =
            CloudMirrorNeedsFullRefresh() ||
            !cloud.PermissionSnapshotBelongsTo(account.Current?.Email) ||
            !permissionsFetchedThisSession.Contains(permissionKey);
        StudioCloudProjectRefreshResult result = forceFullRefresh
            ? new StudioCloudProjectRefreshResult(true, await account.GetProjectAsync(projectId))
            : await account.GetProjectChangesAsync(projectId, knownToken);
        permissionsFetchedThisSession.Add(permissionKey);
        return result;
    }

    /// <summary>Projects whose rights were read whole in the current signed-in session.</summary>
    private readonly HashSet<string> permissionsFetchedThisSession = new(StringComparer.OrdinalIgnoreCase);

    private async Task<bool> RefreshCurrentProjectCloudAccessAsync(
        bool reportResult = false,
        StudioCloudProjectRefreshResult? inspectedRefresh = null)
    {
        if (refreshingCurrentProjectAccess ||
            !state.HasOpenProject ||
            !account.IsSignedIn ||
            !state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId))
        {
            RefreshTeamActionUi();
            RefreshSyncUi();
            return false;
        }

        string projectId = state.Project.Cloud.ServerProjectId;
        StudioOperationContext operationContext = CaptureOperationContext();
        refreshingCurrentProjectAccess = true;
        bool previousAlbumRebuildSuppression = suppressAutomaticAlbumRebuild;
        suppressAutomaticAlbumRebuild = true;
        RefreshTeamActionUi();
        RefreshSyncUi();
        await Task.Yield();
        try
        {
            RequireOperationContext(
                operationContext,
                "project_access_refresh_start");
            StudioCloudProjectRefreshResult refresh = inspectedRefresh
                ?? await InspectCurrentProjectCloudChangesAsync(projectId);
            RequireOperationContext(
                operationContext,
                "project_access_refresh_inspect");

            DateTimeOffset checkedAtUtc = DateTimeOffset.UtcNow;
            ProjectCloudSyncMetadata.MarkCloudChecked(state.Project, checkedAtUtc);
            if (!refresh.IsModified)
            {
                CleanupCurrentCloudAlbumCache();
                state.SaveProject();
                RefreshSyncUi();
                if (reportResult)
                {
                    SetStatus(
                        "Cloud ERA өөрчлөлт алга. ETag dirty detector төслийн мэдээлэл, " +
                        "байгууллага, багийн эрх болон album revision өөрчлөгдөөгүйг баталгаажууллаа; файл дахин татаагүй.");
                }
                return true;
            }

            StudioCloudProjectDetail latest = refresh.Project
                ?? throw new InvalidDataException("Cloud ERA changed response did not include the canonical project.");

            SuppressProjectReplacedUiBind(() =>
                state.LinkCurrentProjectToCloud(
                    latest,
                    account.Current!.ServerUrl,
                    account.Current.Email,
                    preserveCreation: true,
                    preserveSyncState: true));
            await ApplyCloudProjectRenderProfileAsync(latest);
            RequireOperationContext(
                operationContext,
                "project_access_refresh_render_profile");
            ControlledDocumentSyncResult documentRefresh =
                await ReconcileAtdControlledDocumentAsync(
                    projectId,
                    latest.Project.ConcurrencyToken,
                    allowUpload: false);
            RequireOperationContext(
                operationContext,
                "project_access_refresh_documents");
            await DrainSuppressedAlbumRebuildEventsAsync();
            RequireOperationContext(
                operationContext,
                "project_access_refresh_document_events");
            CloudAlbumCacheRefreshResult albumRefresh = await RefreshCloudAlbumPreviewAsync(
                projectId,
                latest.Albums);
            RequireOperationContext(
                operationContext,
                "project_access_refresh_album");
            await DrainSuppressedAlbumRebuildEventsAsync();
            RequireOperationContext(
                operationContext,
                "project_access_refresh_album_events");
            ProjectCloudSyncMetadata.MarkCloudRefreshed(
                state.Project,
                latest.Project.ConcurrencyToken,
                checkedAtUtc);
            state.SaveProject();
            BindProjectToUi();
            if (reportResult)
            {
                string albumStatus = state.Project.Cloud.CanonicalAlbumRebuildPending
                    ? albumRefresh.HasCurrentAlbum
                        ? "canonical album rebuild pending; хамгийн сүүлийн баталгаатай PDF харагдаж байна " +
                          $"[reason: {StudioCanonicalAlbumRebuildPolicy.DiagnosticReasonCode}]"
                        : "canonical album rebuild pending; баталгаатай PDF одоогоор алга " +
                          $"[reason: {StudioCanonicalAlbumRebuildPolicy.DiagnosticReasonCode}]"
                    : albumRefresh.HasCurrentAlbum
                        ? albumRefresh.Downloaded
                            ? $"current album R{albumRefresh.RevisionNumber} татагдлаа"
                            : $"current album R{albumRefresh.RevisionNumber} cache-д өөрчлөлтгүй байна"
                        : "current album одоогоор алга";
                string pendingNotice = state.Project.Cloud.PendingProjectInformation is null
                    ? ""
                    : " Илгээгдээгүй локал төслийн засварыг дарж бичээгүй, pending хэвээр хадгаллаа.";
                string documentNotice = documentRefresh.HasPendingOrConflict
                    ? " " + documentRefresh.Message + " Локал АТД-г Cloud таталт устгаагүй."
                    : " " + documentRefresh.Message;
                SetStatus(
                    $"Cloud ERA canonical мэдээлэл шинэчлэгдлээ: төсөл, байгууллагын snapshot/logo, багийн эрх; {albumStatus}." +
                    pendingNotice + documentNotice);
            }
            return true;
        }
        catch (StudioOperationContextChangedException)
        {
            return false;
        }
        catch (Exception exception) when (
            exception is StudioAccountException or
                HttpRequestException or
                IOException or
                InvalidDataException or
                TaskCanceledException)
        {
            if (!IsOperationContextCurrent(operationContext))
                return false;
            if (exception is StudioAccountException accountException &&
                accountException.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                // The status alone decides nothing - see StudioProjectAccessRefusal
                // for what the server can and cannot support here.
                StudioProjectAccessVerdict verdict = StudioProjectAccessRefusal.Read(
                    accountException.ErrorCode, accountException.Message, SeatedAsBot);
                if (verdict.ProjectEnded)
                    CloseCurrentCloudProjectAfterAccessEnded(verdict.Sentence);
                else
                    SetStatus(verdict.Sentence);
                _ = RefreshProjectsAsync();
                return false;
            }
            if (reportResult ||
                (state.HasOpenProject && state.Project.Cloud.CurrentUserScopes.Count == 0))
                SetStatus("Cloud ERA өөрчлөлт шалгаж чадсангүй: " + exception.Message);
            return false;
        }
        finally
        {
            suppressAutomaticAlbumRebuild = previousAlbumRebuildSuppression;
            refreshingCurrentProjectAccess = false;
            RefreshTeamActionUi();
            RefreshFoundationEditUi();
            RefreshSyncUi();
        }
    }

    private async Task CheckCurrentProjectAccessAsync()
    {
        if (!state.HasOpenProject ||
            !account.IsSignedIn ||
            !state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId))
        {
            return;
        }

        string projectId = state.Project.Cloud.ServerProjectId;
        StudioOperationContext operationContext = CaptureOperationContext();
        try
        {
            await account.GetProjectAsync(projectId);
            if (!IsOperationContextCurrent(operationContext))
                return;
        }
        catch (StudioAccountException exception) when (
            exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            if (!IsOperationContextCurrent(operationContext))
                return;

            // 🔴 THE CHEAPEST WAY TO LOSE SOMEBODY'S WORKSPACE. This runs on a
            // timer against a route that answers `project_not_found` for four
            // different worlds, and it used to close the open project on every
            // one of them - including a seat whose assignment list had not been
            // read. The branch below already knows that a dropped network is not
            // a revoked access; a refusal is no more conclusive.
            StudioProjectAccessVerdict verdict = StudioProjectAccessRefusal.Read(
                exception.ErrorCode, exception.Message, SeatedAsBot);
            if (verdict.ProjectEnded)
                CloseCurrentCloudProjectAfterAccessEnded(verdict.Sentence);
            else
                SetStatus(verdict.Sentence);
            await RefreshProjectsAsync(refreshNotifications: false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            if (!IsOperationContextCurrent(operationContext))
                return;
            // Түр сүлжээ тасрах нь project access цуцлагдсан гэсэн үг биш.
        }
        catch (StudioAccountException)
        {
            // Any other answer - a 502 from the gateway, a 500 - says the
            // server could not answer now, not that the access ended. It
            // used to escape this timer check and end Studio with the owner's
            // unsaved work in it (2026-10-20, 502 from GetProjectAsync).
        }
    }

    private async Task RefreshNotificationsAsync(bool silent = true)
    {
        if (refreshingNotifications)
            return;
        ResetAccountBoundNotificationState();
        if (!account.IsSignedIn)
        {
            return;
        }

        StudioOperationContext operationContext = CaptureOperationContext();
        refreshingNotifications = true;
        try
        {
            StudioProjectMembershipInvitationListResponse invitations =
                await account.ListMembershipInvitationsAsync();
            if (!IsOperationContextCurrent(operationContext))
                return;
            StudioProjectMembershipExitRequestListResponse exitRequests =
                await account.ListMembershipExitRequestsAsync();
            if (!IsOperationContextCurrent(operationContext))
                return;
            // A bot seat invitation arrives in the invitee's own account. It was
            // read by nothing until now, so an invitation could be sent and
            // never answered - the whole flow stopped here.
            StudioCloudBotInvitationListResponse botInvitations =
                await account.ListMyBotInvitationsAsync();
            if (!IsOperationContextCurrent(operationContext))
                return;
            // Friend requests waiting on this person (SRV ea75fe6; the device event «friend-request» brings this
            // refresh). An older server without the route (404), or a seated bot token (403 friends_seat_forbidden), has none -
            // not a failure of the rest.
            StudioFriendsResponse friends;
            try
            {
                friends = await account.ListFriendsAsync();
            }
            catch (StudioAccountException exception) when (exception.StatusCode is System.Net.HttpStatusCode.NotFound or System.Net.HttpStatusCode.Forbidden)
            {
                friends = new StudioFriendsResponse();
            }
            if (!IsOperationContextCurrent(operationContext))
                return;
            notificationInvitations = invitations;
            notificationExitRequests = exitRequests;
            notificationBotInvitations = botInvitations;
            notificationFriends = friends;
            notificationAccountEpoch = account.SessionEpoch;
            UpdateNotificationsButton();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            if (IsOperationContextCurrent(operationContext) && !silent)
                SetStatus("Мэдэгдэл шинэчлэгдсэнгүй: " + exception.Message);
        }
        finally
        {
            refreshingNotifications = false;
            if (!IsOperationContextCurrent(operationContext) &&
                Application.Current?.Dispatcher.HasShutdownStarted != true)
            {
                _ = RefreshNotificationsAsync();
            }
        }
    }

    private void ResetAccountBoundNotificationState()
    {
        if (notificationAccountEpoch == account.SessionEpoch)
            return;

        notificationAccountEpoch = account.SessionEpoch;
        notificationInvitations =
            new StudioProjectMembershipInvitationListResponse();
        notificationExitRequests =
            new StudioProjectMembershipExitRequestListResponse();
        notificationBotInvitations = new StudioCloudBotInvitationListResponse();
        notificationFriends = new StudioFriendsResponse();
        UpdateNotificationsButton();
    }

    private void UpdateNotificationsButton()
    {
        int count = notificationInvitations.Received.Count(item =>
                item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase)) +
            notificationExitRequests.AwaitingApproval.Count(item =>
                item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase)) +
            notificationExitRequests.Requested.Count(item =>
                item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase)) +
            notificationBotInvitations.Items.Count(item =>
                item.State.Equals("Sent", StringComparison.OrdinalIgnoreCase) &&
                item.ExpiresAtUtc > DateTimeOffset.UtcNow) +
            notificationFriends.Incoming.Count;
        notificationsButton.IsEnabled = account.IsSignedIn;
        notificationsRailButton.IsEnabled = account.IsSignedIn;
        notificationsRailBadgeText.Text = count > 99
            ? "99+"
            : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
        notificationsRailBadge.Visibility = count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (notificationsButton.Content is StackPanel stack &&
            stack.Children.Count > 1 &&
            stack.Children[1] is TextBlock label)
        {
            label.Text = count == 0 ? "Мэдэгдэл" : $"Мэдэгдэл ({count})";
        }
        notificationsButton.ToolTip = count == 0
            ? "Шинэ мэдэгдэл алга"
            : $"{count} хүлээгдэж буй мэдэгдэл";
        notificationsRailButton.ToolTip = count == 0
            ? "Багийн урилга болон шийдвэр хүлээж буй хүсэлт алга"
            : $"{count} хүлээгдэж буй мэдэгдэл. Нээж шийдвэрлэх";
    }

    private void CloseCurrentCloudProjectAfterAccessEnded(string message)
    {
        if (state.HasOpenProject)
            state.CloseProject();
        projectWorkspaceOpen = false;
        RebuildNavigation();
        SelectPage(StudioPage.Projects);
        SetStatus(message);
    }

    private async Task ShowNotificationsAsync()
    {
        if (!await EnsureSignedInAsync())
            return;
        StudioOperationContext operationContext = CaptureOperationContext();
        await RefreshNotificationsAsync(silent: false);
        if (!IsOperationContextCurrent(operationContext))
            return;
        var dialog = new StudioNotificationsDialog(
            account,
            notificationInvitations,
            notificationExitRequests,
            notificationBotInvitations,
            notificationFriends)
        {
            Owner = Window.GetWindow(Root),
        };
        dialog.ShowDialog();
        if (!IsOperationContextCurrent(operationContext))
            return;
        await RefreshNotificationsAsync();
        if (!IsOperationContextCurrent(operationContext))
            return;
        if (dialog.ProjectsChanged)
        {
            await RefreshProjectsAsync();
            if (!IsOperationContextCurrent(operationContext))
                return;
            if (state.HasOpenProject &&
                state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId))
            {
                try
                {
                    StudioCloudProjectDetail latest = await account.GetProjectAsync(state.Project.Cloud.ServerProjectId);
                    RequireOperationContext(
                        operationContext,
                        "Notification project refresh");
                    state.LinkCurrentProjectToCloud(
                        latest,
                        account.Current!.ServerUrl,
                        account.Current.Email,
                        preserveCreation: true);
                    await ApplyCloudProjectRenderProfileAsync(latest);
                    RequireOperationContext(
                        operationContext,
                        "Notification project refresh");
                    await RefreshProjectTeamAsync();
                    RequireOperationContext(
                        operationContext,
                        "Notification project refresh");
                }
                catch (StudioOperationContextChangedException)
                {
                    return;
                }
                catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
                {
                    if (!IsOperationContextCurrent(operationContext))
                        return;
                    SetStatus("Төслийн баг шинэчлэгдсэн боловч нээлттэй төслийг дахин уншиж чадсангүй: " + exception.Message);
                    return;
                }
            }
            SetStatus("Төслийн access болон багийн мэдээлэл шинэчлэгдлээ.");
        }
    }

    private UIElement BuildTeamActions()
    {
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        inviteTeamMemberButton.Click += async (_, _) => await InviteTeamMemberAsync();
        editTeamMemberRolesButton.Click += async (_, _) => await EditSelectedTeamMemberRolesAsync();
        removeTeamMemberButton.Click += async (_, _) => await RemoveSelectedTeamMemberAsync();
        leaveProjectButton.Click += async (_, _) => await RequestLeaveProjectAsync();
        participantsList.SelectionChanged += (_, _) => RefreshTeamActionUi();
        participantsList.MouseDoubleClick += async (_, _) =>
        {
            if (editTeamMemberRolesButton.IsEnabled)
                await EditSelectedTeamMemberRolesAsync();
        };
        actions.Children.Add(inviteTeamMemberButton);
        actions.Children.Add(editTeamMemberRolesButton);
        actions.Children.Add(removeTeamMemberButton);
        actions.Children.Add(leaveProjectButton);
        return actions;
    }

    private UIElement BuildProjectArchitectAssignment()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        projectArchitectSummaryText.Margin = new Thickness(0, 0, 0, 8);
        panel.Children.Add(projectArchitectSummaryText);

        var actions = new WrapPanel();
        projectArchitectBox.Margin = new Thickness(0, 0, 8, 0);
        projectArchitectBox.SelectionChanged += (_, _) => RefreshProjectArchitectActionUi();
        assignProjectArchitectButton.Click += async (_, _) => await AssignSelectedProjectArchitectAsync();
        actions.Children.Add(projectArchitectBox);
        actions.Children.Add(assignProjectArchitectButton);
        panel.Children.Add(actions);
        return panel;
    }

    private void RefreshTeamActionUi()
    {
        bool canManage = CanManageProjectTeam();
        MemberRow? selected = participantsList.SelectedItem as MemberRow;
        bool selectedIsCurrentAccount = selected is not null &&
            selected.Email.Equals(account.Current?.Email ?? "", StringComparison.OrdinalIgnoreCase);
        bool serverSupportsRoleManagement = account.CurrentCapabilities is { } capabilities &&
            CloudEraCapabilityPolicy.Supports(
                capabilities,
                CloudEraFeatures.ParticipantRoleManagement);
        bool workflowManaged = selected?.RoleCodes?.Any(IsWorkflowManagedRole) == true;
        inviteTeamMemberButton.IsEnabled = canManage;
        editTeamMemberRolesButton.IsEnabled = canManage &&
            !updatingTeamMemberRoles && selected is { IsInvitation: false } && !workflowManaged;
        removeTeamMemberButton.IsEnabled = canManage && selected is not null &&
            (selected.IsInvitation || !selectedIsCurrentAccount);
        bool pendingExit = state.HasOpenProject && notificationExitRequests.Requested.Any(item =>
            item.ProjectId.Equals(state.Project.Cloud.ServerProjectId, StringComparison.OrdinalIgnoreCase) &&
            item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
        leaveProjectButton.IsEnabled = state.HasOpenProject &&
            account.IsSignedIn &&
            state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !pendingExit;
        removeTeamMemberButton.Content = selected?.IsInvitation == true
            ? "Урилга цуцлах"
            : "Багаас хасах";
        string reason = canManage
            ? "Бүртгэлтэй хэрэглэгчид урилга илгээнэ"
            : refreshingCurrentProjectAccess
                ? "Cloud ERA access эрхийг шинэчилж байна"
                : "Төслийн баг удирдах role шаардлагатай";
        inviteTeamMemberButton.ToolTip = reason;
        editTeamMemberRolesButton.ToolTip = !serverSupportsRoleManagement
            ? "Засварлах үед Cloud ERA server-ийн role API боломжийг дахин шалгана"
            : workflowManaged
                ? "Захиалагч болон төрийн байгууллагын role workflow хэсгээс удирдагдана"
                : selected is null
                    ? "Эхлээд багийн гишүүн сонгоно уу"
                    : selected.IsInvitation
                        ? "Урилгыг хүлээн авсны дараа role засна"
                        : "Сонгосон гишүүний нэг эсвэл олон role-г өөрчлөх";
        removeTeamMemberButton.ToolTip = reason;
        leaveProjectButton.ToolTip = pendingExit
            ? "Төсөл үүсгэгч байгууллагын шийдвэр хүлээгдэж байна"
            : "Төсөл үүсгэгч байгууллагад гарах хүсэлт илгээх";
        RefreshProjectArchitectActionUi();
    }

    private void RefreshProjectArchitectUi()
    {
        if (!state.HasOpenProject)
        {
            projectArchitectBox.ItemsSource = Array.Empty<ProjectArchitectOption>();
            projectArchitectSummaryText.Text = "Төсөл нээгээгүй байна.";
            RefreshProjectArchitectActionUi();
            return;
        }

        List<ProjectArchitectOption> options = state.Project.Foundation.DesignCompany.Members
            .Where(member => !string.IsNullOrWhiteSpace(member.Id) && !string.IsNullOrWhiteSpace(member.Email))
            .Select(member => new ProjectArchitectOption(
                member.Id,
                member.FamilyName,
                member.GivenName,
                member.FullName,
                member.Email,
                member.Roles.Any(ProjectRoleSemantics.IsAppointedArchitect)))
            .OrderByDescending(option => option.IsCurrent)
            .ThenBy(option => option.ProfileName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        projectArchitectBox.ItemsSource = options;
        ProjectArchitectOption? current = options.FirstOrDefault(option => option.IsCurrent);

        // Nobody appointed leaves the picker empty. It used to fall back to the
        // first member, which on a new project is whoever created it - so the
        // director arrived pre-selected as the architect and one press of the
        // button made it true. The client's instruction was exactly this:
        // «Захирал төсөл үүсгэхдээ ерөнхий архитектороор шууд томилогддоггүй
        // болгочих. Томилохоор бол өөрөө тохируулчихаж чадна.»
        //
        // The line below the picker already said nobody was appointed. The
        // pre-selection contradicted it, and a control that disagrees with its
        // own caption is read as the caption being out of date.
        projectArchitectBox.SelectedItem = current;
        projectArchitectSummaryText.Text = current is null
            ? "Үндсэн архитектор томилогдоогүй. Булангийн хүснэгтийн Архитектор мөр хоосон гарна. " +
              "Томилохын тулд хүнээ сонгоод «Томилох» дарна уу."
            : $"Одоогийн архитектор: {current.DocumentName}";
        RefreshProjectArchitectActionUi();
    }

    private void RefreshProjectArchitectActionUi()
    {
        ProjectArchitectOption? selected = projectArchitectBox.SelectedItem as ProjectArchitectOption;
        bool canManage = CanManageProjectTeam();
        bool serverSupportsAssignment = account.CurrentCapabilities is { } capabilities &&
            CloudEraCapabilityPolicy.Supports(
                capabilities,
                CloudEraFeatures.ConceptArchitectAssignment);
        assignProjectArchitectButton.IsEnabled =
            !assigningProjectArchitect && canManage && selected is not null;
        projectArchitectBox.IsEnabled = !assigningProjectArchitect && canManage &&
            projectArchitectBox.Items.Count > 0;
        assignProjectArchitectButton.ToolTip = !state.HasOpenProject
            ? "Төсөл нээгээгүй байна"
            : !account.IsSignedIn
                ? "Studio бүртгэлээр нэвтэрнэ үү"
                : !serverSupportsAssignment
                    ? "Баталгаажуулах үед Cloud ERA server-ийн шинэ API боломжийг дахин шалгана"
                : !canManage
                    ? "Төслийн баг удирдах role шаардлагатай"
                    : selected?.IsCurrent == true
                        ? "Одоогийн томилгоог profile мэдээллээр дахин баталгаажуулж, булангийн хүснэгтийг шинэчлэх"
                        : "Profile нэрийг булангийн хүснэгтийн Архитектор мөртэй холбоно";
    }

    private async Task AssignSelectedProjectArchitectAsync()
    {
        if (!CanManageProjectTeam() ||
            projectArchitectBox.SelectedItem is not ProjectArchitectOption selected ||
            assigningProjectArchitect)
        {
            return;
        }
        if (!StudioRelationshipBoundary.Confirm(
                Window.GetWindow(Root),
                StudioRelationshipAction.AssignProjectArchitect,
                $"{selected.ProfileName} · {selected.Email}"))
        {
            return;
        }

        assigningProjectArchitect = true;
        RefreshProjectArchitectActionUi();
        try
        {
            string projectId = state.Project.Cloud.ServerProjectId;
            StudioCloudProjectDetail latest = await account.AssignConceptArchitectAsync(
                projectId,
                selected.ParticipantId);
            state.LinkCurrentProjectToCloud(
                latest,
                account.Current!.ServerUrl,
                account.Current.Email,
                preserveCreation: true,
                preserveSyncState: true);
            await ApplyCloudProjectRenderProfileAsync(latest);
            BindProjectToUi();
            UpdateAlbum(
                silent: true,
                statusPrefix: "Төслийн архитекторын мэдээлэл шинэчлэгдлээ");
            string appointedProfileName = state.Project.Foundation.DesignCompany.Members
                .FirstOrDefault(member => member.Roles.Any(
                    ProjectRoleSemantics.IsAppointedArchitect))?.FullName
                ?? selected.ProfileName;
            SetStatus(
                $"{MongolianPersonNameFormatter.ForDocument(
                    selected.FamilyName,
                    selected.GivenName,
                    appointedProfileName)} төслийн архитектороор томилогдлоо. " +
                "Булангийн хүснэгт profile нэрээр шинэчлэгдсэн.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            ReportWriteRefusal("Төслийн архитектор томилогдсонгүй", exception);
        }
        finally
        {
            assigningProjectArchitect = false;
            RefreshProjectArchitectUi();
        }
    }

    /// <summary>
    /// Whether this session may manage the open project's team.
    ///
    /// Asked of the SESSION's authority, not of whoever is signed in: a seated
    /// machine is judged by its seat's assignment. It used to read
    /// account.Current?.Email, which on a seated machine is null - so every
    /// team action went dark, not by any rule but because the question was put
    /// to an identity that was not there. A member appointed as an admin gets
    /// the admin's buttons; the seat's scopes say so, and the server decides
    /// what those scopes are.
    /// </summary>
    private bool CanManageProjectTeam() =>
        state.HasOpenProject &&
        (account.IsSignedIn || SeatedAsBot) &&
        state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
        HasProjectScope("team.manage");

    private bool CanEditProjectContent()
    {
        if (!state.HasOpenProject)
            return false;
        if (!state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase))
            return true;
        // Same question, same authority: the seat's assignment on a seated
        // machine, the person's own participation otherwise.
        return (account.IsSignedIn || SeatedAsBot) &&
            HasProjectScope("concept.write");
    }

    private bool CanEditProjectInformation()
    {
        if (!state.HasOpenProject)
            return false;
        if (!state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase))
            return true;
        return account.IsSignedIn &&
            ProjectCloudSyncAuthority.CanManageCanonicalMetadata(
                state.Project.Cloud,
                account.Current?.Email);
    }

    private bool EnsureProjectContentPermission()
    {
        if (CanEditProjectContent())
            return true;
        if (state.HasOpenProject &&
            account.IsSignedIn &&
            state.Project.Cloud.Origin.Equals(
                ProjectOrigins.Cloud,
                StringComparison.OrdinalIgnoreCase) &&
            !state.Project.Cloud.PermissionSnapshotBelongsTo(
                account.Current?.Email))
        {
            SetStatus(
                "Энэ төслийн cached Cloud эрх өөр бүртгэлд хамаарч байна. " +
                "Одоогийн бүртгэлийн эрхийг баталгаажуулахын тулд Cloud Sync хийнэ үү.");
            return false;
        }
        SetStatus("Таны project role эх үүсвэр болон альбум боловсруулах эрхгүй байна.");
        return false;
    }

    private async Task InviteTeamMemberAsync()
    {
        if (!CanManageProjectTeam())
        {
            SetStatus("Төслийн баг удирдах role шаардлагатай.");
            return;
        }
        try
        {
            IReadOnlyList<StudioProjectRole> roles = await account.ListProjectRolesAsync();
            var dialog = new ProjectMemberInvitationDialog(account, roles)
            {
                Owner = Window.GetWindow(Root),
            };
            if (dialog.ShowDialog() != true || dialog.Draft is null)
                return;
            if (!StudioRelationshipBoundary.Confirm(
                    Window.GetWindow(Root),
                    StudioRelationshipAction.InviteProjectMember,
                    dialog.Draft.Counterparty))
            {
                return;
            }
            if (dialog.Draft.FriendKey.Length > 0)
                await SendTeamInvitationToFriendAsync(dialog.Draft.FriendKey, dialog.Draft.FriendName, dialog.Draft.Roles);
            else
                await SendTeamInvitationAsync(dialog.Draft.Email, dialog.Draft.Roles);
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            ReportWriteRefusal("Багийн урилга илгээж чадсангүй", exception);
        }
    }

    /// <summary>The «Гишүүн урих» send itself - the button (after its dialog) and the bridge's «team-invite» both call it.</summary>
    private async Task<StudioProjectMembershipInvitation> SendTeamInvitationAsync(string email, IReadOnlyList<string> roles)
    {
        StudioProjectMembershipInvitation invitation = await account.InviteProjectMemberAsync(
            state.Project.Cloud.ServerProjectId,
            email,
            roles);
        await RefreshProjectTeamAsync();
        SetStatus(
            $"{invitation.TargetEmail} бүртгэлд урилга илгээлээ. Accept хийх хүртэл төсөлд нэвтрэхгүй.");
        return invitation;
    }

    /// <summary>
    /// «Найзаас сонгох»: the invitation goes by the friend's key; what is said here is the friend's name - the address the
    /// server answers with is never shown (SRV ea75fe6, Мастер 2026-11-12: «имэйл хаяг хэзээ ч ил гарахгүй»).
    /// </summary>
    private async Task SendTeamInvitationToFriendAsync(string friendKey, string friendName, IReadOnlyList<string> roles)
    {
        await account.InviteProjectFriendAsync(state.Project.Cloud.ServerProjectId, friendKey, roles);
        await RefreshProjectTeamAsync();
        SetStatus($"Найз «{friendName}»-д урилга илгээлээ. Accept хийх хүртэл төсөлд нэвтрэхгүй.");
    }

    private async Task EditSelectedTeamMemberRolesAsync()
    {
        if (!CanManageProjectTeam() ||
            updatingTeamMemberRoles ||
            participantsList.SelectedItem is not MemberRow { IsInvitation: false } row ||
            row.RoleCodes is null ||
            row.RoleCodes.Any(IsWorkflowManagedRole))
        {
            return;
        }

        try
        {
            IReadOnlyList<StudioProjectRole> roles = await account.ListProjectRolesAsync();
            var dialog = new ProjectMemberRoleDialog(
                row.Name,
                row.Email,
                roles,
                row.RoleCodes)
            {
                Owner = Window.GetWindow(Root),
            };
            if (dialog.ShowDialog() != true || dialog.Draft is null)
                return;
            if (!StudioRelationshipBoundary.Confirm(
                    Window.GetWindow(Root),
                    StudioRelationshipAction.UpdateProjectMemberRoles,
                    $"{row.Name} · {row.Email}"))
            {
                return;
            }

            updatingTeamMemberRoles = true;
            RefreshTeamActionUi();
            StudioCloudProjectDetail latest = await account.UpdateParticipantRolesAsync(
                state.Project.Cloud.ServerProjectId,
                row.Identifier,
                dialog.Draft.Roles);
            state.LinkCurrentProjectToCloud(
                latest,
                account.Current!.ServerUrl,
                account.Current.Email,
                preserveCreation: true,
                preserveSyncState: true);
            await ApplyCloudProjectRenderProfileAsync(latest);
            BindProjectToUi();
            UpdateAlbum(
                silent: true,
                statusPrefix: "Төслийн багийн role шинэчлэгдлээ");
            SetStatus(
                $"{row.Name} гишүүний role шинэчлэгдлээ: {string.Join(", ", dialog.Draft.Roles)}.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            ReportWriteRefusal("Гишүүний role шинэчлэгдсэнгүй", exception);
        }
        finally
        {
            updatingTeamMemberRoles = false;
            RefreshProjectArchitectUi();
            RefreshTeamActionUi();
        }
    }

    private async Task RemoveSelectedTeamMemberAsync()
    {
        if (!CanManageProjectTeam() || participantsList.SelectedItem is not MemberRow row)
            return;
        StudioOperationContext operationContext = CaptureOperationContext();
        if (row.IsInvitation)
        {
            MessageBoxResult confirmation = StudioMessageDialog.Show(
                Window.GetWindow(Root),
                $"{row.Name} хэрэглэгчийн хүлээгдэж буй урилгыг цуцлах уу?",
                "Erk-S Studio",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            if (confirmation != MessageBoxResult.Yes)
                return;
        }
        else if (!StudioRelationshipBoundary.Confirm(
                     Window.GetWindow(Root),
                     StudioRelationshipAction.RemoveProjectMember,
                     row.Name))
        {
            return;
        }
        if (!IsOperationContextCurrent(operationContext))
            return;

        try
        {
            string projectId = state.Project.Cloud.ServerProjectId;
            if (row.IsInvitation)
            {
                await account.RevokeMembershipInvitationAsync(projectId, row.Identifier);
                RequireOperationContext(operationContext, "Team invitation revocation");
            }
            else
            {
                await account.DeactivateParticipantAsync(projectId, row.Identifier);
                RequireOperationContext(operationContext, "Team member removal");
                StudioCloudProjectDetail latest = await account.GetProjectAsync(projectId);
                RequireOperationContext(operationContext, "Team member removal");
                state.LinkCurrentProjectToCloud(
                    latest,
                    account.Current!.ServerUrl,
                    account.Current.Email,
                    preserveCreation: true);
                await ApplyCloudProjectRenderProfileAsync(latest);
                RequireOperationContext(operationContext, "Team member removal");
            }
            await RefreshProjectTeamAsync();
            RequireOperationContext(operationContext, "Team member removal");
            SetStatus(row.IsInvitation ? "Хүлээгдэж байсан урилгыг цуцаллаа." : "Багийн гишүүнийг хаслаа.");
        }
        catch (StudioOperationContextChangedException)
        {
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            if (!IsOperationContextCurrent(operationContext))
                return;
            ReportWriteRefusal("Багийн өөрчлөлт хийгдсэнгүй", exception);
        }
    }

    private async Task RequestLeaveProjectAsync()
    {
        if (!state.HasOpenProject || !account.IsSignedIn ||
            !state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        if (!StudioRelationshipBoundary.Confirm(
                Window.GetWindow(Root),
                StudioRelationshipAction.RequestProjectExit,
                CompanyDisplayName(state.Project.Foundation.DesignCompany.OrganizationSnapshot)))
        {
            return;
        }

        try
        {
            StudioProjectMembershipExitRequest request = await account.RequestProjectExitAsync(
                state.Project.Cloud.ServerProjectId,
                "Studio-оос төслөөс гарах хүсэлт илгээв.");
            await RefreshNotificationsAsync();
            RefreshTeamActionUi();
            SetStatus(
                $"Төслөөс гарах хүсэлтийг {request.ApprovalOrganizationName} байгууллагад илгээлээ. " +
                "Зөвшөөрөх хүртэл таны эрх хэвээр байна.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            ReportWriteRefusal("Төслөөс гарах хүсэлт илгээгдсэнгүй", exception);
        }
    }

    private async Task RefreshProjectTeamAsync()
    {
        if (!state.HasOpenProject)
            return;
        StudioOperationContext operationContext = CaptureOperationContext();
        string projectId = state.Project.Cloud.ServerProjectId;
        List<MemberRow> rows = ActiveProjectMemberRows();
        if (CanManageProjectTeam())
        {
            try
            {
                StudioProjectMembershipInvitationListResponse invitations =
                    await account.ListMembershipInvitationsAsync();
                if (!IsOperationContextCurrent(operationContext))
                    return;
                rows.AddRange(invitations.Issued
                    .Where(item =>
                        item.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase) &&
                        item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
                    .Select(item => new MemberRow(
                        string.IsNullOrWhiteSpace(item.TargetDisplayName)
                            ? item.TargetEmail
                            : item.TargetDisplayName,
                        string.Join(", ", item.Roles),
                        item.TargetEmail,
                        item.InvitationId,
                        "Урилга хүлээгдэж байна",
                        true,
                        item.Roles)));
            }
            catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
            {
                if (!IsOperationContextCurrent(operationContext))
                    return;
                SetStatus("Хүлээгдэж буй багийн урилга уншигдсангүй: " + exception.Message);
            }
        }
        if (!IsOperationContextCurrent(operationContext))
            return;
        BindParticipantRows(rows);
        RefreshProjectArchitectUi();
        RefreshTeamActionUi();
    }

    private void BindParticipantRows(IReadOnlyList<MemberRow> rows)
    {
        string selectedIdentifier = (participantsList.SelectedItem as MemberRow)?.Identifier ?? "";
        participantsList.ItemsSource = rows;
        if (!string.IsNullOrWhiteSpace(selectedIdentifier))
        {
            participantsList.SelectedItem = rows.FirstOrDefault(row =>
                row.Identifier.Equals(selectedIdentifier, StringComparison.OrdinalIgnoreCase));
        }
    }

    private List<MemberRow> ActiveProjectMemberRows()
    {
        string projectId = state.Project.Cloud.ServerProjectId;
        HashSet<string> pendingExitEmails = notificationExitRequests.Requested
            .Concat(notificationExitRequests.AwaitingApproval)
            .Where(item =>
                item.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase) &&
                item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            .Select(item => item.ParticipantEmail)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return state.Project.Foundation.DesignCompany.Members
            .Select(member => new MemberRow(
                MongolianPersonNameFormatter.ForDisplay(
                    member.FamilyName,
                    member.GivenName,
                    member.FullName),
                string.Join(", ", member.Roles),
                member.Email,
                member.Id,
                pendingExitEmails.Contains(member.Email)
                    ? "Гарах хүсэлт хүлээгдэж байна"
                    : "Идэвхтэй",
                false,
                member.Roles.ToArray(),
                // The fact travels beside the sentence. Reading it back out of
                // the sentence is how a reworded label becomes a wrong screen.
                pendingExitEmails.Contains(member.Email)))
            .ToList();
    }

    private static bool IsWorkflowManagedRole(string role) => role is
        "Client" or "Applicant" or "AuthoritySpecialist" or
        "AuthorityDepartmentHead" or "ChiefArchitect";

    private sealed record ProjectArchitectOption(
        string ParticipantId,
        string FamilyName,
        string GivenName,
        string ProfileName,
        string Email,
        bool IsCurrent)
    {
        public string DocumentName => MongolianPersonNameFormatter.ForDocument(
            FamilyName,
            GivenName,
            ProfileName);

        public string Label => $"{DocumentName} · {Email}";
    }
    private async Task ApplyCloudProjectRenderProfileAsync(StudioCloudProjectDetail cloud)
    {
        if (!state.HasOpenProject)
            return;
        await ApplyCloudClientLogoAsync(cloud);
        await ApplyCloudOrganizationDocumentsAsync(cloud);

        StudioCloudOrganizationRenderProfile? profile = cloud.DesignOrganizationProfile;
        if (profile is null)
            return;
        string projectId = state.Project.ProjectId;
        string? projectPath = state.ProjectPath;
        CompanyProfile snapshot = state.Project.Foundation.DesignCompany.OrganizationSnapshot;
        if (string.IsNullOrWhiteSpace(profile.LogoUrl))
        {
            bool changed = !string.IsNullOrWhiteSpace(snapshot.LogoPath) ||
                !string.IsNullOrWhiteSpace(state.Project.Cloud.LastReceivedDesignOrganizationLogoKey);
            snapshot.LogoPath = "";
            snapshot.LogoOriginalFileName = "";
            state.Project.Cloud.LastReceivedDesignOrganizationLogoKey = "";
            int deleted = CleanupCachedProjectLogoFiles("design-organization-logo", keepPath: null);
            if (changed || deleted > 0)
                state.SaveProject();
            return;
        }

        string logoKey = profile.LogoUrl.Trim();
        if (CachedProjectLogoIsCurrent(
                snapshot.LogoPath,
                state.Project.Cloud.LastReceivedDesignOrganizationLogoKey,
                logoKey))
        {
            return;
        }

        try
        {
            StudioDownloadedImage? image = await account.GetOrganizationLogoAsync(profile.LogoUrl);
            if (image is null || projectPath is null ||
                !state.HasOpenProject ||
                !state.Project.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
                return;
            string projectFolder = Path.GetDirectoryName(projectPath)
                ?? throw new InvalidOperationException("Project folder is unavailable.");
            string assetsFolder = Path.Combine(projectFolder, "assets");
            Directory.CreateDirectory(assetsFolder);
            string extension = image.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
                ? ".jpg"
                : ".png";
            string logoPath = Path.Combine(assetsFolder, "design-organization-logo" + extension);
            await using var logoBytes = new MemoryStream(image.Bytes, writable: false);
            await StudioAccountService.ReplaceDownloadedFileAsync(logoBytes, logoPath);
            snapshot.LogoPath = logoPath;
            snapshot.LogoOriginalFileName = Path.GetFileName(logoPath);
            state.Project.Cloud.LastReceivedDesignOrganizationLogoKey = logoKey;
            CleanupCachedProjectLogoFiles("design-organization-logo", logoPath);
            state.SaveProject();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            SetStatus("Төслийн компанийн лого татагдсангүй: " + exception.Message);
        }
    }
    /// <summary>
    /// Brings the organisation's certificate and licence onto this device, so
    /// the album can draw them instead of a placeholder.
    ///
    /// Somebody uploads these into their organisation once. Before this they
    /// lived only on the machine of whoever added them, and a colleague opening
    /// the same project found the certificate page empty - told, in effect,
    /// that they had not uploaded something they had.
    ///
    /// A file already on disk with the same fingerprint is left alone: the
    /// scans do not change, and re-fetching them on every sync would spend a
    /// user's connection to produce the bytes they already have.
    /// </summary>
    private async Task ApplyCloudOrganizationDocumentsAsync(StudioCloudProjectDetail cloud)
    {
        StudioCloudOrganizationRenderProfile? profile = cloud.DesignOrganizationProfile;
        if (profile is null || !state.HasOpenProject || state.ProjectPath is null)
            return;

        await SendProjectOrganizationDocumentsMissingFromServerAsync(profile);

        (StudioCloudOrganizationDocument Cloud, List<ProjectFileReference> Target)[] wanted =
        [
            .. profile.RegistrationCertificateDocuments.Select(document =>
                (document, state.Project.Foundation.DesignCompany.OrganizationSnapshot.RegistrationCertificateDocuments)),
            .. profile.DesignLicenseDocuments.Select(document =>
                (document, state.Project.Foundation.DesignCompany.OrganizationSnapshot.DesignLicenseDocuments)),
        ];
        if (wanted.Length == 0)
            return;

        string projectFolder = ProjectWorkspacePaths.GetProjectFolder(state.ProjectPath);
        string assetsFolder = Path.Combine(projectFolder, "assets", "organization-documents");
        string projectId = state.Project.ProjectId;
        int fetched = 0;
        var failures = new List<string>();

        foreach ((StudioCloudOrganizationDocument document, List<ProjectFileReference> target) in wanted)
        {
            ProjectFileReference? local = target.FirstOrDefault(item =>
                item.ServerDocumentId.Equals(document.DocumentId, StringComparison.OrdinalIgnoreCase));
            if (local is null)
                continue;

            string extension = StudioOrganizationDocumentFormats.Extension(document.ContentType);
            if (extension.Length == 0)
            {
                failures.Add($"{document.Title}: '{document.ContentType}' төрлийг альбомд зурах боломжгүй");
                continue;
            }

            string fileName = document.DocumentId + extension;
            string fullPath = Path.Combine(assetsFolder, fileName);
            // A scan replaced on the server under the same id (a page that came in sideways
            // and was set upright, 2026-10-31) has a new fingerprint; the copy on disk is
            // then the old one and is fetched again.
            bool sameScan = string.IsNullOrWhiteSpace(document.Sha256) ||
                string.Equals(document.Sha256.Trim(), (local.Sha256 ?? "").Trim(), StringComparison.OrdinalIgnoreCase);
            if (File.Exists(fullPath) && local.IsAvailable && sameScan &&
                StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(state.ProjectPath, local))
            {
                continue;
            }

            try
            {
                StudioDownloadedDocument? content =
                    await account.GetOrganizationDocumentAsync(document.ContentUrl);
                if (content is null)
                {
                    failures.Add($"{document.Title}: сервер дээр олдсонгүй");
                    continue;
                }

                // The project may have been closed or swapped while this was in
                // flight; writing into the previous project's folder would put
                // a stranger's certificate in it.
                if (!state.HasOpenProject ||
                    !state.Project.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Directory.CreateDirectory(assetsFolder);
                await using var bytes = new MemoryStream(content.Bytes, writable: false);
                await StudioAccountService.ReplaceDownloadedFileAsync(bytes, fullPath);

                local.RelativePath = Path.GetRelativePath(projectFolder, fullPath);
                local.ContentType = content.ContentType;
                if (!string.IsNullOrWhiteSpace(document.Sha256))
                    local.Sha256 = document.Sha256.Trim().ToUpperInvariant();
                local.SizeBytes = content.Bytes.LongLength;
                if (document.PageCount > 0)
                    local.PageCount = document.PageCount;
                local.IsAvailable = true;
                local.IsCloudPlaceholder = false;
                fetched++;
            }
            catch (Exception exception) when (
                exception is StudioAccountException or HttpRequestException or
                    TaskCanceledException or IOException or UnauthorizedAccessException)
            {
                failures.Add($"{document.Title}: {exception.Message}");
            }
        }

        if (MarkCompanyPagesWhoseCopiesArrived() || fetched > 0)
            state.SaveProject();

        // A scan that did not arrive leaves a placeholder page in a printed
        // album, so it is named rather than counted.
        if (failures.Count > 0)
            SetStatus("Байгууллагын баримт татагдсангүй — " + string.Join("; ", failures));
        else if (fetched > 0)
            SetStatus($"Байгууллагын {fetched} хуулбар татагдаж, альбомд орлоо.");
    }

    private readonly HashSet<string> organizationDocumentUploadsRefused = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Puts a scan this project holds, and its organisation on the server does not, into that
    /// organisation, so every member's album can draw it.
    ///
    /// Мастер, 2026-10-31: NANUTO's certificate and licence lived only in Studio's own copy of
    /// the company while the album was put together on one machine; when the company moved to
    /// the server the scans did not move with it, and an update must not make work a person
    /// already did disappear. Matched by fingerprint, so a scan the server holds is never sent
    /// twice. Without the right to manage the organisation the server refuses; the person is
    /// told who can add it, once per scan and session, instead of being asked on every sync.
    /// </summary>
    private async Task SendProjectOrganizationDocumentsMissingFromServerAsync(
        StudioCloudOrganizationRenderProfile profile)
    {
        CompanyProfile snapshot = state.Project.Foundation.DesignCompany.OrganizationSnapshot;
        if (string.IsNullOrWhiteSpace(profile.OrganizationId) ||
            !profile.OrganizationId.Equals(snapshot.OrganizationId, StringComparison.OrdinalIgnoreCase) ||
            state.ProjectPath is null)
        {
            return;
        }

        string projectPath = state.ProjectPath;
        string projectFolder = ProjectWorkspacePaths.GetProjectFolder(projectPath);
        string projectId = state.Project.ProjectId;
        IReadOnlyList<CompanyDocumentUpload> uploads = CompanyDocumentUploadPlan.Decide(
                snapshot.RegistrationCertificateDocuments,
                snapshot.DesignLicenseDocuments,
                profile.RegistrationCertificateDocuments
                    .Concat(profile.DesignLicenseDocuments)
                    .Select(document => document.Sha256),
                canManage: true)
            .Where(upload =>
                !organizationDocumentUploadsRefused.Contains(profile.OrganizationId + "|" + upload.Document.Sha256) &&
                StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(projectPath, upload.Document))
            .ToList();
        if (uploads.Count == 0)
            return;

        int sent = 0;
        var refused = new List<string>();
        foreach (CompanyDocumentUpload upload in uploads)
        {
            string path = Path.IsPathRooted(upload.Document.RelativePath)
                ? upload.Document.RelativePath
                : Path.Combine(projectFolder, upload.Document.RelativePath);
            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(path);
                StudioCloudOrganizationDocument stored = await account.UploadOrganizationDocumentAsync(
                    profile.OrganizationId,
                    ToServerDocumentCategory(upload.Category),
                    upload.Document.Title,
                    upload.Document.OriginalFileName,
                    upload.Document.ContentType,
                    bytes);
                if (!state.HasOpenProject ||
                    !state.Project.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                upload.Document.ServerDocumentId = stored.DocumentId;
                upload.Document.CloudSyncStatus = ProjectDocumentCloudSyncStatuses.Synced;
                sent++;
            }
            catch (Exception exception) when (
                exception is StudioAccountException or HttpRequestException or
                    TaskCanceledException or IOException or UnauthorizedAccessException)
            {
                organizationDocumentUploadsRefused.Add(profile.OrganizationId + "|" + upload.Document.Sha256);
                refused.Add(exception is StudioAccountException { StatusCode: System.Net.HttpStatusCode.Forbidden }
                    ? $"{upload.Document.Title}: байгууллагын админ (удирдах эрхтэй гишүүн) оруулах хэрэгтэй"
                    : $"{upload.Document.Title}: {exception.Message}");
            }
        }

        if (sent > 0)
        {
            state.SaveProject();
            SetStatus($"Төслийн {sent} байгууллагын хуулбар серверт байгаагүй тул байгууллагад нь орууллаа.");
        }
        if (refused.Count > 0)
            SetStatus("Төслийн байгууллагын хуулбар серверт ороогүй — " + string.Join("; ", refused));
    }

    /// <summary>
    /// A company page whose scans are now all on this device is drawn and sent once, so the
    /// Cloud album's placeholder gives way to the copy (ProjectCloudSyncMetadata).
    /// </summary>
    private bool MarkCompanyPagesWhoseCopiesArrived()
    {
        if (!state.HasOpenProject || state.ProjectPath is null)
            return false;
        string projectPath = state.ProjectPath;
        if (!ProjectCloudSyncMetadata.MarkCompanyPagesWhoseCopiesArrived(
                state.Project,
                document => StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(projectPath, document)))
        {
            return false;
        }
        return true;
    }

    private async Task ApplyCloudClientLogoAsync(StudioCloudProjectDetail cloud)
    {
        StudioCloudProjectInitiationBasis? basis = cloud.Foundation?.InitiationBasis;
        if (basis is null || !state.HasOpenProject)
            return;

        CompanyProfile clientSnapshot = state.Project.Foundation.InitiationBasis.ClientOrganizationSnapshot;
        if (string.IsNullOrWhiteSpace(basis.ClientLogoUrl))
        {
            bool changed = !string.IsNullOrWhiteSpace(clientSnapshot.LogoPath) ||
                !string.IsNullOrWhiteSpace(state.Project.Cloud.LastReceivedClientLogoKey);
            clientSnapshot.LogoPath = "";
            clientSnapshot.LogoOriginalFileName = "";
            state.Project.Cloud.LastReceivedClientLogoKey = "";
            int deleted = CleanupCachedProjectLogoFiles("client-organization-logo", keepPath: null);
            if (changed || deleted > 0)
                state.SaveProject();
            return;
        }

        string projectId = state.Project.ProjectId;
        string? projectPath = state.ProjectPath;
        string logoKey = basis.ClientLogoUrl.Trim();
        if (CachedProjectLogoIsCurrent(
                clientSnapshot.LogoPath,
                state.Project.Cloud.LastReceivedClientLogoKey,
                logoKey))
        {
            return;
        }

        try
        {
            StudioDownloadedImage? image = await account.GetOrganizationLogoAsync(basis.ClientLogoUrl);
            if (image is null || projectPath is null ||
                !state.HasOpenProject ||
                !state.Project.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
                return;
            string projectFolder = Path.GetDirectoryName(projectPath)
                ?? throw new InvalidOperationException("Project folder is unavailable.");
            string assetsFolder = Path.Combine(projectFolder, "assets");
            Directory.CreateDirectory(assetsFolder);
            string extension = image.ContentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase)
                ? ".jpg"
                : ".png";
            string logoPath = Path.Combine(assetsFolder, "client-organization-logo" + extension);
            await using var logoBytes = new MemoryStream(image.Bytes, writable: false);
            await StudioAccountService.ReplaceDownloadedFileAsync(logoBytes, logoPath);
            CompanyProfile snapshot = state.Project.Foundation.InitiationBasis.ClientOrganizationSnapshot;
            snapshot.LogoPath = ProjectWorkspacePaths.ToRelativePath(projectPath, logoPath);
            snapshot.LogoOriginalFileName = Path.GetFileName(logoPath);
            state.Project.Cloud.LastReceivedClientLogoKey = logoKey;
            CleanupCachedProjectLogoFiles("client-organization-logo", logoPath);
            state.SaveProject();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus("Захиалагчийн лого татагдсангүй: " + exception.Message);
        }
    }

    private bool CachedProjectLogoIsCurrent(string logoPath, string receivedKey, string expectedKey)
    {
        if (string.IsNullOrWhiteSpace(state.ProjectPath) ||
            string.IsNullOrWhiteSpace(logoPath) ||
            string.IsNullOrWhiteSpace(expectedKey) ||
            !expectedKey.Equals(receivedKey, StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            string resolved = ProjectWorkspacePaths.ResolveInsideProject(state.ProjectPath, logoPath);
            string assetsFolder = Path.Combine(ProjectWorkspacePaths.GetProjectFolder(state.ProjectPath), "assets");
            return ProjectWorkspacePaths.IsInside(assetsFolder, resolved) && File.Exists(resolved);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private int CleanupCachedProjectLogoFiles(string fileNamePrefix, string? keepPath)
    {
        if (string.IsNullOrWhiteSpace(state.ProjectPath))
            return 0;

        string assetsFolder = Path.Combine(ProjectWorkspacePaths.GetProjectFolder(state.ProjectPath), "assets");
        if (!Directory.Exists(assetsFolder))
            return 0;

        string keep = string.IsNullOrWhiteSpace(keepPath) ? "" : Path.GetFullPath(keepPath);
        int deleted = 0;
        foreach (string extension in new[] { ".png", ".jpg", ".jpeg" })
        {
            string candidate = Path.Combine(assetsFolder, fileNamePrefix + extension);
            if (!File.Exists(candidate) ||
                (!string.IsNullOrWhiteSpace(keep) &&
                 Path.GetFullPath(candidate).Equals(keep, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            try
            {
                File.Delete(candidate);
                deleted++;
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return deleted;
    }

    /// <summary>
    /// A team or project write the server refused: on the status line AND in a window, in words
    /// (StudioWriteRefusal). The owner took a refused invitation for a sent one when it was only a
    /// status line the next one replaced (Мастер, 2026-10-31).
    /// </summary>
    private void ReportWriteRefusal(string whatFailedMn, Exception exception)
    {
        string text = StudioWriteRefusal.Describe(whatFailedMn, exception);
        SetStatus(text);
        StudioMessageDialog.Show(
            Window.GetWindow(Root),
            text,
            "Erk-S Studio",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
}
