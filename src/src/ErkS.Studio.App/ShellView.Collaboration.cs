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
    private StudioProjectMembershipExitRequestListResponse notificationExitRequests = new();
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

    private async Task RunSelectedProjectLifecycleActionAsync()
    {
        if (!account.IsSignedIn || projectsList.SelectedItem is not ProjectRow selected)
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

    private async Task RefreshCurrentProjectCloudAccessAsync(bool reportResult = false)
    {
        if (refreshingCurrentProjectAccess ||
            !state.HasOpenProject ||
            !account.IsSignedIn ||
            !state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId))
        {
            RefreshTeamActionUi();
            RefreshSyncUi();
            return;
        }

        string projectId = state.Project.Cloud.ServerProjectId;
        refreshingCurrentProjectAccess = true;
        bool previousAlbumRebuildSuppression = suppressAutomaticAlbumRebuild;
        suppressAutomaticAlbumRebuild = true;
        autoRebuildTimer.Stop();
        RefreshTeamActionUi();
        RefreshSyncUi();
        try
        {
            StudioCloudProjectDetail latest = await account.GetProjectAsync(projectId);
            if (!state.HasOpenProject ||
                !state.Project.Cloud.ServerProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            state.LinkCurrentProjectToCloud(
                latest,
                account.Current!.ServerUrl,
                preserveCreation: true,
                preserveSyncState: true);
            await ApplyCloudProjectRenderProfileAsync(latest);
            await DrainSuppressedAlbumRebuildEventsAsync();
            await TryCacheCurrentCloudAlbumPreviewAsync(projectId);
            await DrainSuppressedAlbumRebuildEventsAsync();
            BindProjectToUi();
            if (reportResult)
                SetStatus("Cloud ERA access болон төслийн багийн мэдээлэл шинэчлэгдлээ.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            if (exception is StudioAccountException accountException &&
                accountException.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
            {
                CloseCurrentCloudProjectAfterAccessEnded(
                    "Төслийн access дууссан тул төсөл таны Studio жагсаалтаас хасагдлаа. Локал эх файл болон mirror устгагдаагүй.");
                _ = RefreshProjectsAsync();
                return;
            }
            if (reportResult || state.Project.Cloud.CurrentUserScopes.Count == 0)
                SetStatus("Cloud ERA access эрхийг шинэчилж чадсангүй: " + exception.Message);
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
        try
        {
            await account.GetProjectAsync(projectId);
        }
        catch (StudioAccountException exception) when (
            exception.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.NotFound)
        {
            CloseCurrentCloudProjectAfterAccessEnded(
                "Төслийн access дууссан тул төсөл таны Studio жагсаалтаас хасагдлаа. Локал эх файл болон mirror устгагдаагүй.");
            await RefreshProjectsAsync(refreshNotifications: false);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Түр сүлжээ тасрах нь project access цуцлагдсан гэсэн үг биш.
        }
    }

    private async Task RefreshNotificationsAsync(bool silent = true)
    {
        if (refreshingNotifications)
            return;
        if (!account.IsSignedIn)
        {
            notificationInvitations = new StudioProjectMembershipInvitationListResponse();
            notificationExitRequests = new StudioProjectMembershipExitRequestListResponse();
            UpdateNotificationsButton();
            return;
        }

        refreshingNotifications = true;
        try
        {
            notificationInvitations = await account.ListMembershipInvitationsAsync();
            notificationExitRequests = await account.ListMembershipExitRequestsAsync();
            UpdateNotificationsButton();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            if (!silent)
                SetStatus("Мэдэгдэл шинэчлэгдсэнгүй: " + exception.Message);
        }
        finally
        {
            refreshingNotifications = false;
        }
    }

    private void UpdateNotificationsButton()
    {
        int count = notificationInvitations.Received.Count(item =>
                item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase)) +
            notificationExitRequests.AwaitingApproval.Count(item =>
                item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase)) +
            notificationExitRequests.Requested.Count(item =>
                item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase));
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
        await RefreshNotificationsAsync(silent: false);
        var dialog = new StudioNotificationsDialog(
            account,
            notificationInvitations,
            notificationExitRequests)
        {
            Owner = Window.GetWindow(Root),
        };
        dialog.ShowDialog();
        await RefreshNotificationsAsync();
        if (dialog.ProjectsChanged)
        {
            await RefreshProjectsAsync();
            if (state.HasOpenProject &&
                state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId))
            {
                try
                {
                    StudioCloudProjectDetail latest = await account.GetProjectAsync(state.Project.Cloud.ServerProjectId);
                    state.LinkCurrentProjectToCloud(latest, account.Current!.ServerUrl, preserveCreation: true);
                    await ApplyCloudProjectRenderProfileAsync(latest);
                    await RefreshProjectTeamAsync();
                }
                catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
                {
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
        projectArchitectBox.SelectedItem = current ?? options.FirstOrDefault();
        projectArchitectSummaryText.Text = current is null
            ? "Үндсэн архитектор томилогдоогүй. Булангийн хүснэгтийн Архитектор мөр хоосон байна."
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
        state.Project.Cloud.HasScope("team.manage");

    private bool CanEditProjectContent()
    {
        if (!state.HasOpenProject)
            return false;
        if (!state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase))
            return true;
        return state.Project.Cloud.HasScope("concept.write");
    }

    private bool CanEditProjectInformation()
    {
        if (!state.HasOpenProject)
            return false;
        if (!state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!account.IsSignedIn)
            return false;

        HashSet<string> editableRoles = new(StringComparer.OrdinalIgnoreCase)
        {
            "ProjectAdmin",
            "Client",
            "AuthoritySpecialist",
            "AuthorityDepartmentHead",
            "ChiefArchitect",
            "DesignCompanyAdmin",
            "MajorArchitect",
            "Architect",
        };
        return state.Project.Cloud.CurrentUserRoles.Any(editableRoles.Contains);
    }

    private bool EnsureProjectContentPermission()
    {
        if (CanEditProjectContent())
            return true;
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

        try
        {
            string projectId = state.Project.Cloud.ServerProjectId;
            if (row.IsInvitation)
            {
                await account.RevokeMembershipInvitationAsync(projectId, row.Identifier);
            }
            else
            {
                await account.DeactivateParticipantAsync(projectId, row.Identifier);
                StudioCloudProjectDetail latest = await account.GetProjectAsync(projectId);
                state.LinkCurrentProjectToCloud(latest, account.Current!.ServerUrl, preserveCreation: true);
                await ApplyCloudProjectRenderProfileAsync(latest);
            }
            await RefreshProjectTeamAsync();
            SetStatus(row.IsInvitation ? "Хүлээгдэж байсан урилгыг цуцаллаа." : "Багийн гишүүнийг хаслаа.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
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
        string projectId = state.Project.Cloud.ServerProjectId;
        List<MemberRow> rows = ActiveProjectMemberRows();
        if (CanManageProjectTeam())
        {
            try
            {
                StudioProjectMembershipInvitationListResponse invitations =
                    await account.ListMembershipInvitationsAsync();
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
                SetStatus("Хүлээгдэж буй багийн урилга уншигдсангүй: " + exception.Message);
            }
        }
        if (!state.HasOpenProject ||
            !state.Project.Cloud.ServerProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
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

        StudioCloudOrganizationRenderProfile? profile = cloud.DesignOrganizationProfile;
        if (profile is null)
            return;
        string projectId = state.Project.ProjectId;
        string? projectPath = state.ProjectPath;
        CompanyProfile snapshot = state.Project.Foundation.DesignCompany.OrganizationSnapshot;
        if (string.IsNullOrWhiteSpace(profile.LogoUrl))
        {
            snapshot.LogoPath = "";
            state.SaveProject();
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
            await File.WriteAllBytesAsync(logoPath, image.Bytes);
            snapshot.LogoPath = logoPath;
            state.SaveProject();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            SetStatus("Төслийн компанийн лого татагдсангүй: " + exception.Message);
        }
    }
    private async Task ApplyCloudClientLogoAsync(StudioCloudProjectDetail cloud)
    {
        StudioCloudProjectInitiationBasis? basis = cloud.Foundation?.InitiationBasis;
        if (basis is null ||
            string.IsNullOrWhiteSpace(basis.ClientLogoUrl) ||
            !state.HasOpenProject)
        {
            return;
        }

        string projectId = state.Project.ProjectId;
        string? projectPath = state.ProjectPath;
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
            await File.WriteAllBytesAsync(logoPath, image.Bytes);
            CompanyProfile snapshot = state.Project.Foundation.InitiationBasis.ClientOrganizationSnapshot;
            snapshot.LogoPath = ProjectWorkspacePaths.ToRelativePath(projectPath, logoPath);
            snapshot.LogoOriginalFileName = Path.GetFileName(logoPath);
            state.SaveProject();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus("Ð—Ð°Ñ…Ð¸Ð°Ð»Ð°Ð³Ñ‡Ð¸Ð¹Ð½ Ð»Ð¾Ð³Ð¾ Ñ‚Ð°Ñ‚Ð°Ð³Ð´ÑÐ°Ð½Ð³Ò¯Ð¹: " + exception.Message);
        }
    }
}
