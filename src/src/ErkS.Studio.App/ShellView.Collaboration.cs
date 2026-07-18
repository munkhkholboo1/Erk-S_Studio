using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private readonly Button notificationsButton = StudioWidgets.CreateGlyphTextButton(
        "\uE7F4",
        "Мэдэгдэл",
        "Багийн урилга болон төсөл үүсгэх эрх");
    private readonly Button inviteTeamMemberButton = StudioWidgets.CreateGlyphTextButton(
        "\uE710",
        "Гишүүн урих",
        "Бүртгэлтэй хэрэглэгчид багийн урилга илгээх",
        primary: true);
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
        removeTeamMemberButton.Click += async (_, _) => await RemoveSelectedTeamMemberAsync();
        leaveProjectButton.Click += async (_, _) => await RequestLeaveProjectAsync();
        participantsList.SelectionChanged += (_, _) => RefreshTeamActionUi();
        actions.Children.Add(inviteTeamMemberButton);
        actions.Children.Add(removeTeamMemberButton);
        actions.Children.Add(leaveProjectButton);
        return actions;
    }

    private void RefreshTeamActionUi()
    {
        bool canManage = CanManageProjectTeam();
        MemberRow? selected = participantsList.SelectedItem as MemberRow;
        bool selectedIsCurrentAccount = selected is not null &&
            selected.Email.Equals(account.Current?.Email ?? "", StringComparison.OrdinalIgnoreCase);
        inviteTeamMemberButton.IsEnabled = canManage;
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
        removeTeamMemberButton.ToolTip = reason;
        leaveProjectButton.ToolTip = pendingExit
            ? "Төсөл үүсгэгч байгууллагын шийдвэр хүлээгдэж байна"
            : "Төсөл үүсгэгч байгууллагад гарах хүсэлт илгээх";
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
                        true)));
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
        participantsList.ItemsSource = rows;
        RefreshTeamActionUi();
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
                member.FullName,
                string.Join(", ", member.Roles),
                member.Email,
                member.Id,
                pendingExitEmails.Contains(member.Email)
                    ? "Гарах хүсэлт хүлээгдэж байна"
                    : "Идэвхтэй",
                false))
            .ToList();
    }

    private async Task ApplyCloudProjectRenderProfileAsync(StudioCloudProjectDetail cloud)
    {
        StudioCloudOrganizationRenderProfile? profile = cloud.DesignOrganizationProfile;
        if (profile is null || !state.HasOpenProject)
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
}
