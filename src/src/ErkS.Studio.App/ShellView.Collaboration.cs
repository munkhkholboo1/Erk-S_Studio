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
                SetStatus("Төсөл устгагдсангүй: " + exception.Message);
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
            SetStatus("Төслөөс гарах хүсэлт илгээгдсэнгүй: " + exception.Message);
        }
    }

    private async Task<StudioCloudProjectRefreshResult> InspectCurrentProjectCloudChangesAsync(
        string projectId)
    {
        ProjectCloudLink cloud = state.Project.Cloud;
        string knownToken = !string.IsNullOrWhiteSpace(cloud.LastServerConcurrencyToken)
            ? cloud.LastServerConcurrencyToken
            : cloud.ServerSnapshot.ConcurrencyToken;
        bool forceFullRefresh =
            CloudMirrorNeedsFullRefresh() ||
            !cloud.PermissionSnapshotBelongsTo(account.Current?.Email);
        return forceFullRefresh
            ? new StudioCloudProjectRefreshResult(true, await account.GetProjectAsync(projectId))
            : await account.GetProjectChangesAsync(projectId, knownToken);
    }

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
        autoRebuildTimer.Stop();
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
                CloseCurrentCloudProjectAfterAccessEnded(
                    "Төслийн access дууссан тул төсөл таны Studio жагсаалтаас хасагдлаа. Локал эх файл болон mirror устгагдаагүй.");
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
            autoRebuildTimer.Stop();
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
            CloseCurrentCloudProjectAfterAccessEnded(
                "Төслийн access дууссан тул төсөл таны Studio жагсаалтаас хасагдлаа. Локал эх файл болон mirror устгагдаагүй.");
            await RefreshProjectsAsync(refreshNotifications: false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            if (!IsOperationContextCurrent(operationContext))
                return;
            // Түр сүлжээ тасрах нь project access цуцлагдсан гэсэн үг биш.
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
            notificationInvitations = invitations;
            notificationExitRequests = exitRequests;
            notificationBotInvitations = botInvitations;
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
                item.ExpiresAtUtc > DateTimeOffset.UtcNow);
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
            notificationBotInvitations)
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
            SetStatus("Төслийн архитектор томилогдсонгүй: " + exception.Message);
        }
        finally
        {
            assigningProjectArchitect = false;
            RefreshProjectArchitectUi();
        }
    }

    private bool CanManageProjectTeam() =>
        state.HasOpenProject &&
        account.IsSignedIn &&
        state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
        state.Project.Cloud.HasScope(
            "team.manage",
            account.Current?.Email);

    private bool CanEditProjectContent()
    {
        if (!state.HasOpenProject)
            return false;
        if (!state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase))
            return true;
        return account.IsSignedIn &&
            state.Project.Cloud.HasScope(
                "concept.write",
                account.Current?.Email);
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
                    dialog.Draft.Email))
            {
                return;
            }
            StudioProjectMembershipInvitation invitation = await account.InviteProjectMemberAsync(
                state.Project.Cloud.ServerProjectId,
                dialog.Draft.Email,
                dialog.Draft.Roles);
            await RefreshProjectTeamAsync();
            SetStatus(
                $"{invitation.TargetEmail} бүртгэлд урилга илгээлээ. Accept хийх хүртэл төсөлд нэвтрэхгүй.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            SetStatus("Багийн урилга илгээж чадсангүй: " + exception.Message);
        }
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
            SetStatus("Гишүүний role шинэчлэгдсэнгүй: " + exception.Message);
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
            SetStatus("Багийн өөрчлөлт хийгдсэнгүй: " + exception.Message);
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
            SetStatus("Төслөөс гарах хүсэлт илгээгдсэнгүй: " + exception.Message);
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
                member.Roles.ToArray()))
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
            if (File.Exists(fullPath) && local.IsAvailable)
                continue;

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

        if (fetched > 0)
            state.SaveProject();

        // A scan that did not arrive leaves a placeholder page in a printed
        // album, so it is named rather than counted.
        if (failures.Count > 0)
            SetStatus("Байгууллагын баримт татагдсангүй — " + string.Join("; ", failures));
        else if (fetched > 0)
            SetStatus($"Байгууллагын {fetched} хуулбар татагдаж, альбомд орлоо.");
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
}
