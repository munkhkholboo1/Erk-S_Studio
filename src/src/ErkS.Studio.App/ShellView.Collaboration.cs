using System.IO;
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
    private readonly Button removeTeamMemberButton = StudioWidgets.CreateButton("Хасах / урилга цуцлах");
    private readonly Button companyProjectGrantButton = StudioWidgets.CreateButton("Төсөл үүсгэх эрх");
    private StudioProjectMembershipInvitationListResponse notificationInvitations = new();
    private StudioProjectCreationGrantListResponse notificationGrants = new();
    private bool refreshingNotifications;

    private async Task RefreshNotificationsAsync(bool silent = true)
    {
        if (refreshingNotifications)
            return;
        if (!account.IsSignedIn)
        {
            notificationInvitations = new StudioProjectMembershipInvitationListResponse();
            notificationGrants = new StudioProjectCreationGrantListResponse();
            UpdateNotificationsButton();
            return;
        }

        refreshingNotifications = true;
        try
        {
            notificationInvitations = await account.ListMembershipInvitationsAsync();
            notificationGrants = await account.ListProjectCreationGrantsAsync();
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
            notificationGrants.Received.Count(item =>
                item.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
                item.ExpiresAtUtc > DateTimeOffset.UtcNow);
        notificationsButton.IsEnabled = account.IsSignedIn;
        if (notificationsButton.Content is StackPanel stack &&
            stack.Children.Count > 1 &&
            stack.Children[1] is TextBlock label)
        {
            label.Text = count == 0 ? "Мэдэгдэл" : $"Мэдэгдэл ({count})";
        }
        notificationsButton.ToolTip = count == 0
            ? "Шинэ мэдэгдэл алга"
            : $"{count} хүлээгдэж буй мэдэгдэл";
    }

    private async Task ShowNotificationsAsync()
    {
        if (!await EnsureSignedInAsync())
            return;
        await RefreshNotificationsAsync(silent: false);
        var dialog = new StudioNotificationsDialog(account, notificationInvitations, notificationGrants)
        {
            Owner = Window.GetWindow(Root),
        };
        dialog.ShowDialog();
        await RefreshNotificationsAsync();
        if (dialog.ProjectsChanged)
        {
            await RefreshProjectsAsync();
            SetStatus("Зөвшөөрсөн төсөл таны төслийн жагсаалтад нэмэгдлээ.");
        }
    }

    private UIElement BuildTeamActions()
    {
        var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        inviteTeamMemberButton.Click += async (_, _) => await InviteTeamMemberAsync();
        removeTeamMemberButton.Click += async (_, _) => await RemoveSelectedTeamMemberAsync();
        participantsList.SelectionChanged += (_, _) => RefreshTeamActionUi();
        actions.Children.Add(inviteTeamMemberButton);
        actions.Children.Add(removeTeamMemberButton);
        return actions;
    }

    private void RefreshTeamActionUi()
    {
        bool canManage = CanManageProjectTeam();
        inviteTeamMemberButton.IsEnabled = canManage;
        removeTeamMemberButton.IsEnabled = canManage && participantsList.SelectedItem is MemberRow;
        string reason = canManage
            ? "Бүртгэлтэй хэрэглэгчид урилга илгээнэ"
            : "Төслийн баг удирдах role шаардлагатай";
        inviteTeamMemberButton.ToolTip = reason;
        removeTeamMemberButton.ToolTip = reason;
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
        string action = row.IsInvitation ? "урилгыг цуцлах" : "төслийн багаас хасах";
        MessageBoxResult confirmation = MessageBox.Show(
            Window.GetWindow(Root),
            $"{row.Name} хэрэглэгчийг {action} уу?",
            "Erk-S Studio",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
            return;

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

    private List<MemberRow> ActiveProjectMemberRows() =>
        state.Project.Foundation.DesignCompany.Members
            .Select(member => new MemberRow(
                member.FullName,
                string.Join(", ", member.Roles),
                member.Email,
                member.Id,
                "Идэвхтэй",
                false))
            .ToList();

    private void RefreshCompanyGrantActionUi()
    {
        CompanyCatalogEntry? selected = selectedCompanyEntry;
        bool enabled = account.IsSignedIn &&
            selected?.CanManage == true &&
            selected.SyncStatus.Equals(CompanySyncStatuses.Cloud, StringComparison.OrdinalIgnoreCase) &&
            selected.Profile.OrganizationType.Equals("DesignCompany", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(selected.Profile.OrganizationId);
        companyProjectGrantButton.IsEnabled = enabled;
        companyProjectGrantButton.ToolTip = enabled
            ? "Бүртгэлтэй хэрэглэгчид энэ компанийн нэр дээр нэг төсөл үүсгэх эрх олгох"
            : "Идэвхтэй зураг төслийн компанийн admin эрх шаардлагатай";
    }

    private async Task OpenProjectCreationGrantDialogAsync()
    {
        if (selectedCompanyEntry is null || !selectedCompanyEntry.CanManage)
            return;
        try
        {
            IReadOnlyList<StudioCloudOrganization> organizations = await account.ListOrganizationsAsync();
            StudioCloudOrganization? organization = organizations.FirstOrDefault(item =>
                item.OrganizationId.Equals(
                    selectedCompanyEntry.Profile.OrganizationId,
                    StringComparison.OrdinalIgnoreCase));
            if (organization is null)
                throw new StudioAccountException("Cloud ERA компанийн бүртгэл олдсонгүй.");
            var dialog = new ProjectCreationGrantDialog(account, organization)
            {
                Owner = Window.GetWindow(Root),
            };
            dialog.ShowDialog();
            await RefreshNotificationsAsync();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            SetStatus("Төсөл үүсгэх эрхийн хэсэг нээгдсэнгүй: " + exception.Message);
        }
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
