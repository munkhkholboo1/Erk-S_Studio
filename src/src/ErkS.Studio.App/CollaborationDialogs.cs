using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace ErkS.Studio;

internal sealed record ProjectInvitationDraft(string Email, IReadOnlyList<string> Roles);

internal sealed class ProjectMemberInvitationDialog : Window
{
    private readonly StudioAccountService account;
    private readonly IReadOnlyList<StudioProjectRole> roles;
    private readonly TextBox emailBox = new();
    private readonly TextBlock accountResult = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly StackPanel rolesPanel = new();
    private readonly Button inviteButton;
    private string verifiedEmail = "";

    public ProjectInvitationDraft? Draft { get; private set; }

    public ProjectMemberInvitationDialog(
        StudioAccountService account,
        IReadOnlyList<StudioProjectRole> roles)
    {
        this.account = account;
        this.roles = roles;
        Title = "Төслийн багт урих";
        Width = 590;
        Height = 620;
        MinWidth = 520;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);

        inviteButton = StudioWidgets.CreatePrimaryButton("Урилга илгээх");
        inviteButton.IsEnabled = false;
        inviteButton.IsDefault = true;
        inviteButton.Click += (_, _) => Accept();
        emailBox.TextChanged += (_, _) =>
        {
            verifiedEmail = "";
            inviteButton.IsEnabled = false;
            accountResult.Text = "И-мэйлийг серверийн бүртгэлээс шалгана.";
        };
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var cancel = StudioWidgets.CreateButton("Болих");
        cancel.IsCancel = true;
        footer.Children.Add(cancel);
        footer.Children.Add(inviteButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var form = new StackPanel();
        form.Children.Add(StudioWidgets.CreateTitle("Багийн гишүүн урих"));
        form.Children.Add(StudioWidgets.CreateHint(
            "Урилга илгээсэн даруйд гишүүн болохгүй. Хүлээн авагч мэдэгдлээс Accept хийсний дараа төсөлд нэвтэрнэ."));
        var lookupRow = new DockPanel();
        var search = StudioWidgets.CreateButton("Хайх");
        search.Margin = new Thickness(8, 0, 0, 0);
        search.Click += async (_, _) => await LookupAsync(search);
        DockPanel.SetDock(search, Dock.Right);
        lookupRow.Children.Add(search);
        lookupRow.Children.Add(emailBox);
        form.Children.Add(StudioWidgets.CreateFormRow("Бүртгэлийн и-мэйл", lookupRow));
        var resultBorder = new Border
        {
            Background = StudioTheme.PanelAltBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(StudioTheme.CornerRadius),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 4, 0, 12),
            Child = accountResult,
        };
        accountResult.Text = "И-мэйлийг серверийн бүртгэлээс шалгана.";
        form.Children.Add(resultBorder);
        form.Children.Add(StudioWidgets.CreateSectionHeader("Төслийн role"));
        foreach (StudioProjectRole role in roles)
        {
            var check = new CheckBox
            {
                Content = BuildRoleLabel(role),
                Tag = role.Code,
                Margin = new Thickness(0, 3, 0, 3),
                Foreground = StudioTheme.TextBrush,
                IsChecked = role.Code.Equals("ProjectViewer", StringComparison.OrdinalIgnoreCase),
            };
            rolesPanel.Children.Add(check);
        }
        form.Children.Add(rolesPanel);
        root.Children.Add(StudioWidgets.CreateScrollHost(form));
        return root;
    }

    private async Task LookupAsync(Button search)
    {
        string email = emailBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            accountResult.Text = "Зөв и-мэйл хаяг оруулна уу.";
            return;
        }

        search.IsEnabled = false;
        accountResult.Text = "Бүртгэл хайж байна...";
        try
        {
            StudioCloudAccountLookupResponse result = await account.LookupAccountAsync(email);
            if (!result.Found)
            {
                verifiedEmail = "";
                inviteButton.IsEnabled = false;
                accountResult.Text = "Энэ и-мэйлээр бүртгэлтэй хэрэглэгч олдсонгүй.";
                return;
            }
            verifiedEmail = result.Email;
            emailBox.Text = result.Email;
            verifiedEmail = result.Email;
            inviteButton.IsEnabled = true;
            accountResult.Text = string.IsNullOrWhiteSpace(result.DisplayName)
                ? $"Бүртгэл олдлоо: {result.Email}"
                : $"{result.DisplayName}\n{result.Email}";
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            verifiedEmail = "";
            inviteButton.IsEnabled = false;
            accountResult.Text = "Бүртгэл шалгаж чадсангүй: " + exception.Message;
        }
        finally
        {
            search.IsEnabled = true;
        }
    }

    private void Accept()
    {
        List<string> selectedRoles = rolesPanel.Children
            .OfType<CheckBox>()
            .Where(item => item.IsChecked == true)
            .Select(item => item.Tag as string ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        if (string.IsNullOrWhiteSpace(verifiedEmail))
        {
            StudioMessageDialog.Show(this, "Эхлээд бүртгэлтэй хэрэглэгчийг хайж баталгаажуулна уу.");
            return;
        }
        if (selectedRoles.Count == 0)
        {
            StudioMessageDialog.Show(this, "Дор хаяж нэг role сонгоно уу.");
            return;
        }
        Draft = new ProjectInvitationDraft(verifiedEmail, selectedRoles);
        DialogResult = true;
    }

    private static string BuildRoleLabel(StudioProjectRole role)
    {
        var capabilities = new List<string>();
        if (role.CanManageTeam)
            capabilities.Add("баг удирдана");
        if (role.CanEditContent)
            capabilities.Add("эх үүсвэр/альбум боловсруулна");
        if (role.CanSubmitAlbum)
            capabilities.Add("альбум илгээнэ");
        return capabilities.Count == 0
            ? role.Label
            : $"{role.Label}  ·  {string.Join(", ", capabilities)}";
    }
}

internal sealed class StudioNotificationsDialog : Window
{
    private readonly StudioAccountService account;
    private readonly ListView notifications = new();
    private readonly TextBlock status = new() { Foreground = StudioTheme.MutedTextBrush, TextWrapping = TextWrapping.Wrap };
    private readonly Button acceptButton = StudioWidgets.CreatePrimaryButton("Зөвшөөрөх");
    private readonly Button declineButton = StudioWidgets.CreateButton("Татгалзах");
    private StudioProjectMembershipInvitationListResponse invitationData;
    private StudioProjectMembershipExitRequestListResponse exitRequestData;

    public bool ProjectsChanged { get; private set; }

    public StudioNotificationsDialog(
        StudioAccountService account,
        StudioProjectMembershipInvitationListResponse invitations,
        StudioProjectMembershipExitRequestListResponse exitRequests)
    {
        this.account = account;
        invitationData = invitations;
        exitRequestData = exitRequests;
        Title = "Мэдэгдэл";
        Width = 820;
        Height = 540;
        MinWidth = 680;
        MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);
        Content = BuildContent();
        RefreshRows();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        status.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(status);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        acceptButton.Click += async (_, _) => await AcceptSelectedAsync();
        declineButton.Click += async (_, _) => await DeclineSelectedAsync();
        var close = StudioWidgets.CreateButton("Хаах");
        close.IsCancel = true;
        actions.Children.Add(acceptButton);
        actions.Children.Add(declineButton);
        actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        title.Children.Add(StudioWidgets.CreateTitle("Мэдэгдэл"));
        title.Children.Add(StudioWidgets.CreateHint(
            "Багийн урилгыг зөвшөөрсний дараа л төсөл таны жагсаалтад орно."));
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Төрөл", Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(NotificationRow.Type)) });
        view.Columns.Add(new GridViewColumn { Header = "Төсөл / байгууллага", Width = 270, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(NotificationRow.Title)) });
        view.Columns.Add(new GridViewColumn { Header = "Role / эрх", Width = 210, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(NotificationRow.Detail)) });
        view.Columns.Add(new GridViewColumn { Header = "Огноо", Width = 130, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(NotificationRow.Expires)) });
        notifications.View = view;
        notifications.SelectionChanged += (_, _) => RefreshActions();
        root.Children.Add(notifications);
        return root;
    }

    private void RefreshRows()
    {
        var rows = new List<NotificationRow>();
        rows.AddRange(invitationData.Received
            .Where(item => item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            .Select(item => new NotificationRow(
                "Багийн урилга",
                $"{item.ProjectCode}  {item.ProjectName}",
                string.Join(", ", item.Roles),
                item.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                item,
                null)));
        rows.AddRange(exitRequestData.AwaitingApproval
            .Where(item => item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            .Select(item => new NotificationRow(
                "Төслөөс гарах хүсэлт",
                $"{item.ProjectCode}  {item.ProjectName}",
                $"{item.ParticipantDisplayName}  ·  {item.AffectedSourceKeys.Length} source",
                item.RequestedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                null,
                null)));
        rows.AddRange(exitRequestData.Requested
            .Where(item => item.Status.Equals("Pending", StringComparison.OrdinalIgnoreCase))
            .Select(item => new NotificationRow(
                "Гарах хүсэлт · хүлээгдэж байна",
                $"{item.ProjectCode}  {item.ProjectName}",
                string.IsNullOrWhiteSpace(item.ApprovalOrganizationName)
                    ? "Төсөл үүсгэгчийн шийдвэр хүлээж байна"
                    : $"{item.ApprovalOrganizationName} байгууллагын шийдвэр хүлээж байна",
                item.RequestedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                null,
                null)));
        notifications.ItemsSource = rows;
        status.Text = rows.Count == 0
            ? "Шинэ мэдэгдэл алга."
            : $"{rows.Count} хүлээгдэж буй мэдэгдэл";
        RefreshActions();
    }

    private void RefreshActions()
    {
        NotificationRow? selected = notifications.SelectedItem as NotificationRow;
        bool decisionSelected = selected?.Invitation is not null || selected?.ExitRequest is not null;
        acceptButton.IsEnabled = decisionSelected;
        declineButton.IsEnabled = decisionSelected;
    }

    private async Task AcceptSelectedAsync()
    {
        NotificationRow? selected = notifications.SelectedItem as NotificationRow;
        StudioProjectMembershipInvitation? invitation = selected?.Invitation;
        StudioProjectMembershipExitRequest? exitRequest = selected?.ExitRequest;
        if (invitation is null && exitRequest is null)
            return;
        StudioRelationshipAction action = invitation is not null
            ? StudioRelationshipAction.AcceptProjectMembership
            : StudioRelationshipAction.DecideProjectExit;
        string counterparty = invitation is not null
            ? invitation.InvitedByEmail
            : exitRequest!.ParticipantDisplayName;
        if (!StudioRelationshipBoundary.Confirm(this, action, counterparty))
            return;
        SetBusy(true, invitation is not null
            ? "Урилгыг зөвшөөрч байна..."
            : "Гарах хүсэлтийг зөвшөөрч байна...");
        try
        {
            if (invitation is not null)
            {
                await account.AcceptMembershipInvitationAsync(invitation.InvitationId);
                invitationData.Received.RemoveAll(item =>
                    item.InvitationId.Equals(invitation.InvitationId, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                await account.DecideProjectExitAsync(exitRequest!.RequestId, approve: true);
                exitRequestData.AwaitingApproval.RemoveAll(item =>
                    item.RequestId.Equals(exitRequest.RequestId, StringComparison.OrdinalIgnoreCase));
            }
            ProjectsChanged = true;
            RefreshRows();
            status.Text = invitation is not null
                ? $"{invitation.ProjectCode} төсөлд нэгдлээ."
                : $"{exitRequest!.ParticipantDisplayName} хэрэглэгчийн гарах хүсэлтийг зөвшөөрлөө.";
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            status.Text = "Шийдвэрийг хадгалж чадсангүй: " + exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeclineSelectedAsync()
    {
        NotificationRow? selected = notifications.SelectedItem as NotificationRow;
        StudioProjectMembershipInvitation? invitation = selected?.Invitation;
        StudioProjectMembershipExitRequest? exitRequest = selected?.ExitRequest;
        if (invitation is null && exitRequest is null)
            return;
        if (exitRequest is not null && !StudioRelationshipBoundary.Confirm(
                this,
                StudioRelationshipAction.DecideProjectExit,
                exitRequest.ParticipantDisplayName))
        {
            return;
        }
        SetBusy(true, invitation is not null
            ? "Урилгаас татгалзаж байна..."
            : "Гарах хүсэлтээс татгалзаж байна...");
        try
        {
            if (invitation is not null)
            {
                await account.DeclineMembershipInvitationAsync(invitation.InvitationId);
                invitationData.Received.RemoveAll(item =>
                    item.InvitationId.Equals(invitation.InvitationId, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                await account.DecideProjectExitAsync(exitRequest!.RequestId, approve: false);
                exitRequestData.AwaitingApproval.RemoveAll(item =>
                    item.RequestId.Equals(exitRequest.RequestId, StringComparison.OrdinalIgnoreCase));
            }
            RefreshRows();
            status.Text = invitation is not null
                ? "Урилгаас татгалзлаа."
                : "Гарах хүсэлтээс татгалзлаа. Гишүүний access хэвээр үлдэнэ.";
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            status.Text = "Шийдвэрийг хадгалж чадсангүй: " + exception.Message;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? message = null)
    {
        acceptButton.IsEnabled = !busy;
        declineButton.IsEnabled = !busy;
        notifications.IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(message))
            status.Text = message;
        if (!busy)
            RefreshActions();
    }

    private sealed record NotificationRow(
        string Type,
        string Title,
        string Detail,
        string Expires,
        StudioProjectMembershipInvitation? Invitation,
        StudioProjectMembershipExitRequest? ExitRequest);
}

internal sealed class ProjectDeletionDialog : Window
{
    private readonly string projectCode;
    private readonly TextBox confirmationBox = new();
    private readonly TextBox reasonBox = new()
    {
        AcceptsReturn = true,
        MinHeight = 72,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly Button deleteButton = StudioWidgets.CreatePrimaryButton("Төсөл устгах");

    public string Reason => reasonBox.Text.Trim();

    public ProjectDeletionDialog(string projectCode, string projectName)
    {
        this.projectCode = projectCode.Trim();
        Title = "Төсөл устгах";
        Width = 610;
        Height = 455;
        MinWidth = 540;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        StudioTheme.Apply(this);
        Content = BuildContent(projectName);
    }

    private UIElement BuildContent(string projectName)
    {
        var root = new DockPanel { Margin = new Thickness(24) };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancel = StudioWidgets.CreateButton("Болих");
        cancel.IsCancel = true;
        deleteButton.IsEnabled = false;
        deleteButton.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel);
        actions.Children.Add(deleteButton);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var content = new StackPanel();
        content.Children.Add(StudioWidgets.CreateTitle("Төслийг идэвхтэй жагсаалтаас устгах"));
        content.Children.Add(StudioWidgets.CreateHint($"{projectCode} · {projectName}"));
        content.Children.Add(new TextBlock
        {
            Text = "Төсөл бүх оролцогчийн Cloud жагсаалтаас хасагдана. Серверийн canonical мэдээлэл, approval ба аудитын түүх хадгалагдана; энэ төхөөрөмж дээрх эх файл, mirror болон PDF устахгүй.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = StudioTheme.WarningBrush,
            Margin = new Thickness(0, 16, 0, 18),
            LineHeight = 19,
        });
        content.Children.Add(FormLabel("Баталгаажуулахын тулд төслийн кодыг бичнэ үү"));
        confirmationBox.Margin = new Thickness(0, 6, 0, 14);
        confirmationBox.TextChanged += (_, _) => deleteButton.IsEnabled =
            confirmationBox.Text.Trim().Equals(projectCode, StringComparison.OrdinalIgnoreCase);
        content.Children.Add(confirmationBox);
        content.Children.Add(FormLabel("Шалтгаан (заавал биш)"));
        reasonBox.Margin = new Thickness(0, 6, 0, 0);
        content.Children.Add(reasonBox);
        root.Children.Add(content);
        return root;
    }

    private static TextBlock FormLabel(string text) => new()
    {
        Text = text,
        Foreground = StudioTheme.TextBrush,
        FontWeight = FontWeights.SemiBold,
    };
}

internal sealed class ProjectCreationGrantDialog : Window
{
    private readonly StudioAccountService account;
    private readonly StudioCloudOrganization organization;
    private readonly TextBox emailBox = new();
    private readonly TextBlock lookupResult = new() { Foreground = StudioTheme.MutedTextBrush, TextWrapping = TextWrapping.Wrap };
    private readonly ListView issuedList = new();
    private readonly Button sendButton = StudioWidgets.CreatePrimaryButton("Эрх илгээх");
    private readonly Button revokeButton = StudioWidgets.CreateButton("Эрх цуцлах");
    private string verifiedEmail = "";

    public ProjectCreationGrantDialog(StudioAccountService account, StudioCloudOrganization organization)
    {
        this.account = account;
        this.organization = organization;
        Title = "Төсөл үүсгэх эрх";
        Width = 760;
        Height = 620;
        MinWidth = 650;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);
        Content = BuildContent();
        Loaded += async (_, _) => await RefreshIssuedAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var close = StudioWidgets.CreateButton("Хаах");
        close.IsCancel = true;
        revokeButton.Click += async (_, _) => await RevokeSelectedAsync();
        footer.Children.Add(revokeButton);
        footer.Children.Add(close);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(StudioWidgets.CreateTitle("Нэг удаагийн төсөл үүсгэх эрх"));
        header.Children.Add(StudioWidgets.CreateHint(
            $"{organization.DisplayName} компанийн нэр дээр нэг төсөл үүсгэх эрх олгоно. Эрх нь зөвхөн сонгосон бүртгэлд очно."));
        var lookup = new DockPanel();
        var search = StudioWidgets.CreateButton("Хайх");
        search.Margin = new Thickness(8, 0, 0, 0);
        search.Click += async (_, _) => await LookupAsync(search);
        DockPanel.SetDock(search, Dock.Right);
        lookup.Children.Add(search);
        lookup.Children.Add(emailBox);
        header.Children.Add(StudioWidgets.CreateFormRow("Бүртгэлийн и-мэйл", lookup));
        lookupResult.Text = "И-мэйлийг бүртгэлээс шалгана.";
        header.Children.Add(lookupResult);
        sendButton.IsEnabled = false;
        sendButton.Click += async (_, _) => await SendAsync();
        header.Children.Add(sendButton);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Хүлээн авагч", Width = 260, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(GrantRow.Email)) });
        view.Columns.Add(new GridViewColumn { Header = "Төлөв", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(GrantRow.Status)) });
        view.Columns.Add(new GridViewColumn { Header = "Дуусах", Width = 170, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(GrantRow.Expires)) });
        issuedList.View = view;
        issuedList.SelectionChanged += (_, _) =>
            revokeButton.IsEnabled = (issuedList.SelectedItem as GrantRow)?.Grant.Status.Equals(
                "Active",
                StringComparison.OrdinalIgnoreCase) == true;
        root.Children.Add(issuedList);
        return root;
    }

    private async Task LookupAsync(Button search)
    {
        verifiedEmail = "";
        sendButton.IsEnabled = false;
        string email = emailBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
        {
            lookupResult.Text = "Зөв и-мэйл хаяг оруулна уу.";
            return;
        }
        search.IsEnabled = false;
        try
        {
            StudioCloudAccountLookupResponse result = await account.LookupAccountAsync(email);
            if (!result.Found)
            {
                lookupResult.Text = "Бүртгэлтэй хэрэглэгч олдсонгүй.";
                return;
            }
            verifiedEmail = result.Email;
            emailBox.Text = result.Email;
            verifiedEmail = result.Email;
            lookupResult.Text = string.IsNullOrWhiteSpace(result.DisplayName)
                ? result.Email
                : $"{result.DisplayName}  ·  {result.Email}";
            sendButton.IsEnabled = true;
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            lookupResult.Text = "Бүртгэл шалгаж чадсангүй: " + exception.Message;
        }
        finally
        {
            search.IsEnabled = true;
        }
    }

    private async Task SendAsync()
    {
        if (string.IsNullOrWhiteSpace(verifiedEmail))
            return;
        if (!StudioRelationshipBoundary.Confirm(
                this,
                StudioRelationshipAction.IssueProjectCreationGrant,
                verifiedEmail))
        {
            return;
        }
        sendButton.IsEnabled = false;
        try
        {
            await account.CreateProjectCreationGrantAsync(organization.OrganizationId, verifiedEmail);
            lookupResult.Text = $"{verifiedEmail} бүртгэлд нэг удаагийн эрх илгээлээ.";
            verifiedEmail = "";
            emailBox.Clear();
            await RefreshIssuedAsync();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            lookupResult.Text = "Эрх илгээж чадсангүй: " + exception.Message;
        }
    }

    private async Task RefreshIssuedAsync()
    {
        try
        {
            StudioProjectCreationGrantListResponse data = await account.ListProjectCreationGrantsAsync();
            issuedList.ItemsSource = data.Issued
                .Where(item => item.OrganizationId.Equals(organization.OrganizationId, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(item => item.IssuedAtUtc)
                .Select(item => new GrantRow(
                    item.TargetEmail,
                    item.Status,
                    item.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                    item))
                .ToList();
            revokeButton.IsEnabled = false;
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            lookupResult.Text = "Олгосон эрхийн жагсаалт уншигдсангүй: " + exception.Message;
        }
    }

    private async Task RevokeSelectedAsync()
    {
        GrantRow? row = issuedList.SelectedItem as GrantRow;
        if (row is null)
            return;
        try
        {
            await account.RevokeProjectCreationGrantAsync(row.Grant.GrantId);
            await RefreshIssuedAsync();
            lookupResult.Text = $"{row.Email} бүртгэлд олгосон эрхийг цуцаллаа.";
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            lookupResult.Text = "Эрх цуцалж чадсангүй: " + exception.Message;
        }
    }

    private sealed record GrantRow(
        string Email,
        string Status,
        string Expires,
        StudioProjectCreationGrant Grant);
}
