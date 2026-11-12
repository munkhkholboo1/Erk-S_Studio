using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace ErkS.Studio;

/// <param name="FriendKey">A friend picked from «Найзаас сонгох» - then <paramref name="Email"/> is empty and the friend's name is <paramref name="FriendName"/>.</param>
internal sealed record ProjectInvitationDraft(string Email, IReadOnlyList<string> Roles, string FriendKey = "", string FriendName = "")
{
    /// <summary>Who the invitation goes to, as the person sees it: the friend's name, else the address they typed.</summary>
    public string Counterparty => FriendKey.Length > 0 ? FriendName : Email;
}
internal sealed record ProjectMemberRoleDraft(IReadOnlyList<string> Roles);

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
    private readonly ComboBox friendsBox = new() { DisplayMemberPath = nameof(StudioFriend.Name), IsEnabled = false };
    private string verifiedEmail = "";
    private StudioFriend? pickedFriend;
    private bool pickingFriend;

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
            if (!pickingFriend)
            {
                pickedFriend = null;
                friendsBox.SelectedItem = null;
            }
            inviteButton.IsEnabled = pickedFriend is not null;
            if (pickedFriend is null)
                accountResult.Text = "И-мэйлийг серверийн бүртгэлээс шалгана.";
        };
        friendsBox.SelectionChanged += (_, _) => PickFriend(friendsBox.SelectedItem as StudioFriend);
        Content = BuildContent();
        Loaded += async (_, _) => await LoadFriendsAsync();
    }

    /// <summary>
    /// «Найзаас сонгох» (owner, 2026-11-12: «найзуудаа хялбархан урьдаг болгоорой»): the person's friends, by name only -
    /// their addresses never reach Studio (SRV ea75fe6). An older server without the route leaves the list off.
    /// </summary>
    private async Task LoadFriendsAsync()
    {
        try
        {
            StudioFriendsResponse friends = await account.ListFriendsAsync();
            friendsBox.ItemsSource = friends.Friends.OrderBy(friend => friend.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            friendsBox.IsEnabled = friends.Friends.Count > 0;
            friendsBox.ToolTip = friends.Friends.Count > 0
                ? "Найзаа сонгоход и-мэйл бичих шаардлагагүй."
                : "Найз алга — «Мэдэгдэл» › «Найзууд»-аас найз нэмнэ.";
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            friendsBox.IsEnabled = false;
            friendsBox.ToolTip = "Найзын жагсаалтыг авч чадсангүй: " + exception.Message;
        }
    }

    private void PickFriend(StudioFriend? friend)
    {
        pickedFriend = friend;
        if (friend is null)
            return;
        pickingFriend = true;
        try
        {
            emailBox.Text = "";
        }
        finally
        {
            pickingFriend = false;
        }
        verifiedEmail = "";
        inviteButton.IsEnabled = true;
        accountResult.Text = $"Найз: {friend.Name}";
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
        form.Children.Add(StudioWidgets.CreateFormRow("Найзаас сонгох", friendsBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Эсвэл бүртгэлийн и-мэйл", lookupRow));
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
        if (pickedFriend is null && string.IsNullOrWhiteSpace(verifiedEmail))
        {
            StudioMessageDialog.Show(this, "Найзаа сонгох эсвэл бүртгэлтэй хэрэглэгчийг хайж баталгаажуулна уу.");
            return;
        }
        if (selectedRoles.Count == 0)
        {
            StudioMessageDialog.Show(this, "Дор хаяж нэг role сонгоно уу.");
            return;
        }
        Draft = pickedFriend is { } friend
            ? new ProjectInvitationDraft("", selectedRoles, friend.FriendKey, friend.Name)
            : new ProjectInvitationDraft(verifiedEmail, selectedRoles);
        DialogResult = true;
    }

    internal static string BuildRoleLabel(StudioProjectRole role)
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

internal sealed class ProjectMemberRoleDialog : Window
{
    private readonly StackPanel rolesPanel = new();
    private readonly Button saveButton = StudioWidgets.CreatePrimaryButton("Хадгалах");

    public ProjectMemberRoleDraft? Draft { get; private set; }

    public ProjectMemberRoleDialog(
        string memberName,
        string email,
        IReadOnlyList<StudioProjectRole> roles,
        IReadOnlyCollection<string> assignedRoles)
    {
        Title = "Багийн гишүүний үүрэг";
        Width = 620;
        Height = 650;
        MinWidth = 540;
        MinHeight = 540;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);
        Content = BuildContent(memberName, email, roles, assignedRoles);
    }

    private UIElement BuildContent(
        string memberName,
        string email,
        IReadOnlyList<StudioProjectRole> roles,
        IReadOnlyCollection<string> assignedRoles)
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
        saveButton.IsDefault = true;
        saveButton.Click += (_, _) => Accept();
        footer.Children.Add(cancel);
        footer.Children.Add(saveButton);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        HashSet<string> selected = assignedRoles
            .Where(role => !string.IsNullOrWhiteSpace(role))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var form = new StackPanel();
        form.Children.Add(StudioWidgets.CreateTitle("Багийн гишүүний үүрэг"));
        form.Children.Add(StudioWidgets.CreateHint(
            $"{memberName}\n{email}\n\nНэг хэрэглэгч олон role-той байж болно. Role хадгалахад access эрх шууд шинэчлэгдэнэ."));
        form.Children.Add(StudioWidgets.CreateSectionHeader("Төслийн role"));
        foreach (StudioProjectRole role in roles)
        {
            rolesPanel.Children.Add(new CheckBox
            {
                Content = ProjectMemberInvitationDialog.BuildRoleLabel(role),
                Tag = role.Code,
                Margin = new Thickness(0, 3, 0, 3),
                Foreground = StudioTheme.TextBrush,
                IsChecked = selected.Contains(role.Code),
            });
        }
        form.Children.Add(rolesPanel);
        form.Children.Add(StudioWidgets.CreateHint(
            "Major architect сонговол төслийн үндсэн архитектор энэ гишүүн рүү шилжиж, өмнөх хүн Architect role-тойгоо үлдэнэ."));
        root.Children.Add(StudioWidgets.CreateScrollHost(form));
        return root;
    }

    private void Accept()
    {
        List<string> selectedRoles = rolesPanel.Children
            .OfType<CheckBox>()
            .Where(item => item.IsChecked == true)
            .Select(item => item.Tag as string ?? "")
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (selectedRoles.Count == 0)
        {
            StudioMessageDialog.Show(this, "Дор хаяж нэг role сонгоно уу.");
            return;
        }
        Draft = new ProjectMemberRoleDraft(selectedRoles);
        DialogResult = true;
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
    private StudioCloudBotInvitationListResponse botInvitationData;
    private StudioFriendsResponse friendData;

    public bool ProjectsChanged { get; private set; }

    public StudioNotificationsDialog(
        StudioAccountService account,
        StudioProjectMembershipInvitationListResponse invitations,
        StudioProjectMembershipExitRequestListResponse exitRequests,
        StudioCloudBotInvitationListResponse botInvitations,
        StudioFriendsResponse? friends = null)
    {
        this.account = account;
        invitationData = invitations;
        exitRequestData = exitRequests;
        botInvitationData = botInvitations;
        friendData = friends ?? new StudioFriendsResponse();
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
        var friendsButton = StudioWidgets.CreateButton("Найзууд");
        friendsButton.ToolTip = "Найзаа нэмэх, хүсэлтэд хариулах, хасах";
        friendsButton.Click += (_, _) => ShowFriends();
        actions.Children.Add(friendsButton);
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
        // A bot seat invitation reaches the person in their OWN account, which
        // is the point: the seat belongs to the organisation, the membership
        // behind it belongs to them. Sent but never shown, it existed only on
        // the server - one side could invite and nobody could answer.
        rows.AddRange(botInvitationData.Items
            .Where(item => item.State.Equals("Sent", StringComparison.OrdinalIgnoreCase))
            // Expiry is read against the clock, never stored, and nothing on the
            // server sweeps it. Shown as pending, an expired invitation is a row
            // whose only two buttons both fail - so it is not shown as pending.
            .Where(item => item.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .Select(item => new NotificationRow(
                "Ботын суудлын урилга",
                $"«{item.BotDisplayName}» ботын суудал  ·  {item.ProjectId}",
                string.Join(", ", item.Roles),
                item.ExpiresAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
                null,
                null,
                item)));
        // A friend request waiting on this person (SRV ea75fe6): by name only, never an address.
        rows.AddRange(friendData.Incoming.Select(friend => new NotificationRow(
            "Найзын хүсэлт",
            friend.Name,
            "Найз болох хүсэлт илгээсэн",
            "",
            null,
            null,
            null,
            friend)));
        notifications.ItemsSource = rows;
        status.Text = rows.Count == 0
            ? "Шинэ мэдэгдэл алга."
            : $"{rows.Count} хүлээгдэж буй мэдэгдэл";
        RefreshActions();
    }

    private void RefreshActions()
    {
        NotificationRow? selected = notifications.SelectedItem as NotificationRow;
        bool decisionSelected = selected?.Invitation is not null ||
            selected?.ExitRequest is not null ||
            selected?.BotInvitation is not null ||
            selected?.FriendRequest is not null;
        acceptButton.IsEnabled = decisionSelected;
        declineButton.IsEnabled = decisionSelected;
    }

    private async Task AcceptSelectedAsync()
    {
        NotificationRow? selected = notifications.SelectedItem as NotificationRow;
        StudioProjectMembershipInvitation? invitation = selected?.Invitation;
        StudioProjectMembershipExitRequest? exitRequest = selected?.ExitRequest;
        if (selected?.BotInvitation is { } botInvitation)
        {
            await AcceptBotInvitationAsync(botInvitation);
            return;
        }
        if (selected?.FriendRequest is { } friendRequest)
        {
            await AnswerFriendRequestAsync(friendRequest, accept: true);
            return;
        }
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
        if (selected?.BotInvitation is { } botInvitation)
        {
            await DeclineBotInvitationAsync(botInvitation);
            return;
        }
        if (selected?.FriendRequest is { } friendRequest)
        {
            await AnswerFriendRequestAsync(friendRequest, accept: false);
            return;
        }
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

    /// <summary>
    /// Accepting a bot seat invitation. ONE call, and the appointment is what
    /// comes back from it - the server answers with the bot, the project and
    /// the roles already linked. Nothing is written here afterwards, so there is
    /// no gap in which acceptance could exist without the appointment.
    /// </summary>
    private async Task AcceptBotInvitationAsync(StudioCloudBotInvitation invitation)
    {
        if (!StudioRelationshipBoundary.Confirm(
                this,
                StudioRelationshipAction.AcceptProjectMembership,
                invitation.InvitedByEmail))
        {
            return;
        }

        SetBusy(true, "Ботын суудлын урилгыг зөвшөөрч байна...");
        try
        {
            StudioCloudBotInvitationAccepted accepted =
                await account.AcceptBotInvitationAsync(invitation.InvitationId);
            botInvitationData.Items.RemoveAll(item =>
                item.InvitationId.Equals(invitation.InvitationId, StringComparison.OrdinalIgnoreCase));
            ProjectsChanged = true;
            RefreshRows();
            // Two sentences, not one: joining and gaining another project are
            // different events to the person reading this.
            status.Text = accepted.OpenedNewInterval
                ? $"«{invitation.BotDisplayName}» ботын суудалд нэгдлээ. Үүрэг: " +
                  $"{string.Join(", ", accepted.Roles)}"
                : $"«{invitation.BotDisplayName}» ботын суудалд {accepted.ProjectId} төсөл нэмэгдлээ. " +
                  $"Үүрэг: {string.Join(", ", accepted.Roles)}";
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            status.Text = BotSeatErrors.Describe(exception, "Урилгыг зөвшөөрч чадсангүй.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task DeclineBotInvitationAsync(StudioCloudBotInvitation invitation)
    {
        SetBusy(true, "Ботын суудлын урилгаас татгалзаж байна...");
        try
        {
            await account.DeclineBotInvitationAsync(invitation.InvitationId);
            botInvitationData.Items.RemoveAll(item =>
                item.InvitationId.Equals(invitation.InvitationId, StringComparison.OrdinalIgnoreCase));
            RefreshRows();
            status.Text = $"«{invitation.BotDisplayName}» ботын суудлын урилгаас татгалзлаа.";
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            status.Text = BotSeatErrors.Describe(exception, "Татгалзлыг хадгалж чадсангүй.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task AnswerFriendRequestAsync(StudioFriend friend, bool accept)
    {
        SetBusy(true, accept ? "Найзын хүсэлтийг зөвшөөрч байна..." : "Найзын хүсэлтээс татгалзаж байна...");
        try
        {
            await account.AnswerFriendRequestAsync(friend.FriendKey, accept);
            friendData.Incoming.RemoveAll(item => item.FriendKey.Equals(friend.FriendKey, StringComparison.Ordinal));
            if (accept)
                friendData.Friends.Add(friend);
            RefreshRows();
            status.Text = accept ? $"«{friend.Name}» таны найз боллоо." : $"«{friend.Name}»-ийн хүсэлтээс татгалзлаа.";
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

    private void ShowFriends()
    {
        var dialog = new StudioFriendsDialog(account, friendData) { Owner = this };
        dialog.ShowDialog();
        friendData = dialog.Friends;
        RefreshRows();
    }

    private sealed record NotificationRow(
        string Type,
        string Title,
        string Detail,
        string Expires,
        StudioProjectMembershipInvitation? Invitation,
        StudioProjectMembershipExitRequest? ExitRequest,
        StudioCloudBotInvitation? BotInvitation = null,
        StudioFriend? FriendRequest = null);
}

/// <summary>
/// «Найзууд» (owner, 2026-11-12: «бидний бүртгэлд найзууд болдог функц нэмээрэй»; SRV ea75fe6): the person's friends
/// (remove), requests waiting on them (accept, decline), the ones they sent, and a new request by a registered
/// e-mail. Friends appear by name only - an address is typed to ask, never shown back.
/// </summary>
internal sealed class StudioFriendsDialog : Window
{
    private readonly StudioAccountService account;
    private readonly ListView list = new();
    private readonly TextBox emailBox = new();
    private readonly TextBlock status = new() { Foreground = StudioTheme.MutedTextBrush, TextWrapping = TextWrapping.Wrap };
    private readonly Button acceptButton = StudioWidgets.CreatePrimaryButton("Зөвшөөрөх");
    private readonly Button declineButton = StudioWidgets.CreateButton("Татгалзах");
    private readonly Button removeButton = StudioWidgets.CreateButton("Найзаас хасах");

    public StudioFriendsResponse Friends { get; private set; }

    public StudioFriendsDialog(StudioAccountService account, StudioFriendsResponse friends)
    {
        this.account = account;
        Friends = friends;
        Title = "Найзууд";
        Width = 620;
        Height = 520;
        MinWidth = 520;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);
        Content = BuildContent();
        Refresh();
        Loaded += async (_, _) => await ReloadAsync();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var title = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        title.Children.Add(StudioWidgets.CreateTitle("Найзууд"));
        title.Children.Add(StudioWidgets.CreateHint(
            "Найзаа төслийн багт и-мэйл бичилгүй шууд урина. Найзын и-мэйл хаяг хэзээ ч харагдахгүй."));
        var addRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var send = StudioWidgets.CreatePrimaryButton("Хүсэлт илгээх");
        send.Margin = new Thickness(8, 0, 0, 0);
        send.Click += async (_, _) => await RequestAsync(send);
        DockPanel.SetDock(send, Dock.Right);
        addRow.Children.Add(send);
        addRow.Children.Add(emailBox);
        title.Children.Add(StudioWidgets.CreateFormRow("Найз нэмэх (бүртгэлийн и-мэйл)", addRow));
        DockPanel.SetDock(title, Dock.Top);
        root.Children.Add(title);

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        acceptButton.Click += async (_, _) => await AnswerAsync(accept: true);
        declineButton.Click += async (_, _) => await AnswerAsync(accept: false);
        removeButton.Click += async (_, _) => await RemoveAsync();
        var close = StudioWidgets.CreateButton("Хаах");
        close.IsCancel = true;
        actions.Children.Add(acceptButton);
        actions.Children.Add(declineButton);
        actions.Children.Add(removeButton);
        actions.Children.Add(close);
        DockPanel.SetDock(actions, Dock.Right);
        footer.Children.Add(actions);
        status.VerticalAlignment = VerticalAlignment.Center;
        footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Нэр", Width = 300, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FriendRow.Name)) });
        view.Columns.Add(new GridViewColumn { Header = "Төлөв", Width = 220, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(FriendRow.State)) });
        list.View = view;
        list.SelectionChanged += (_, _) => RefreshActions();
        root.Children.Add(list);
        return root;
    }

    private async Task ReloadAsync()
    {
        try
        {
            Friends = await account.ListFriendsAsync();
            Refresh();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            status.Text = "Найзын жагсаалтыг шинэчилж чадсангүй: " + exception.Message;
        }
    }

    private void Refresh()
    {
        list.ItemsSource = FriendRows(Friends);
        status.Text = $"{Friends.Friends.Count} найз · {Friends.Incoming.Count} хүсэлт хүлээж байна";
        RefreshActions();
    }

    /// <summary>Requests waiting on the person first, then friends by name, then the requests they sent.</summary>
    internal static List<FriendRow> FriendRows(StudioFriendsResponse friends)
    {
        var rows = new List<FriendRow>();
        rows.AddRange(friends.Incoming.Select(friend => new FriendRow(friend.Name, "Танд хүсэлт илгээсэн", friend, FriendRowKind.Incoming)));
        rows.AddRange(friends.Friends.OrderBy(friend => friend.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(friend => new FriendRow(friend.Name, "Найз", friend, FriendRowKind.Friend)));
        rows.AddRange(friends.Outgoing.Select(friend => new FriendRow(friend.Name, "Хүсэлт илгээсэн — хариу хүлээж байна", friend, FriendRowKind.Outgoing)));
        return rows;
    }

    private void RefreshActions()
    {
        FriendRow? row = list.SelectedItem as FriendRow;
        acceptButton.IsEnabled = row?.Kind == FriendRowKind.Incoming;
        declineButton.IsEnabled = row?.Kind == FriendRowKind.Incoming;
        removeButton.IsEnabled = row?.Kind == FriendRowKind.Friend;
    }

    private async Task RequestAsync(Button send)
    {
        string email = emailBox.Text.Trim();
        if (email.Length == 0 || !email.Contains('@'))
        {
            status.Text = "Найзынхаа бүртгэлийн и-мэйлийг бичнэ үү.";
            return;
        }
        send.IsEnabled = false;
        try
        {
            StudioFriendActionResult result = await account.RequestFriendAsync(email);
            emailBox.Text = "";
            status.Text = string.IsNullOrWhiteSpace(result.Message) ? "Найзын хүсэлт илгээлээ." : result.Message;
            await ReloadAsync();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            status.Text = "Хүсэлт илгээж чадсангүй: " + exception.Message;
        }
        finally
        {
            send.IsEnabled = true;
        }
    }

    private async Task AnswerAsync(bool accept)
    {
        if (list.SelectedItem is not FriendRow { Kind: FriendRowKind.Incoming } row)
            return;
        try
        {
            await account.AnswerFriendRequestAsync(row.Friend.FriendKey, accept);
            status.Text = accept ? $"«{row.Name}» таны найз боллоо." : $"«{row.Name}»-ийн хүсэлтээс татгалзлаа.";
            await ReloadAsync();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            status.Text = "Шийдвэрийг хадгалж чадсангүй: " + exception.Message;
        }
    }

    private async Task RemoveAsync()
    {
        if (list.SelectedItem is not FriendRow { Kind: FriendRowKind.Friend } row)
            return;
        if (StudioMessageDialog.Show(this, $"«{row.Name}»-ийг найзаас хасах уу?", "Найзууд", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
            return;
        try
        {
            await account.RemoveFriendAsync(row.Friend.FriendKey);
            status.Text = $"«{row.Name}»-ийг найзаас хаслаа.";
            await ReloadAsync();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            status.Text = "Найзаас хасаж чадсангүй: " + exception.Message;
        }
    }

    internal enum FriendRowKind
    {
        Incoming,
        Friend,
        Outgoing,
    }

    internal sealed record FriendRow(string Name, string State, StudioFriend Friend, FriendRowKind Kind);
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
