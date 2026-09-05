using System.Windows;
using System.Windows.Controls;

namespace ErkS.Studio;

/// <summary>
/// Turns this machine into an organisation's bot seat.
///
/// Only reachable from a licence-verified owner session. Hiding it elsewhere is
/// not the protection - the server refuses an unlicensed create - but the menu
/// should show the one road that works rather than one that ends in a refusal.
/// </summary>
internal sealed class BotSeatCreateDialog : Window
{
    private readonly StudioAccountService account;
    private readonly IReadOnlyList<StudioCloudOrganization> organizations;
    private readonly ComboBox organizationBox = new();
    private readonly TextBox nameBox = new();
    private readonly TextBox emailBox = new();
    private readonly TextBox pinBox = new() { MaxLength = 4 };
    private readonly TextBlock resultText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button createButton;

    /// <summary>Set once the machine is actually seated, so the caller can apply it.</summary>
    public StudioBotDeviceState? Seated { get; private set; }

    public BotSeatCreateDialog(
        StudioAccountService account,
        IReadOnlyList<StudioCloudOrganization> organizations)
    {
        this.account = account;
        this.organizations = organizations;
        Title = "Энэ төхөөрөмжийг бот болгох";
        Width = 560;
        Height = 520;
        MinWidth = 500;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);

        foreach (StudioCloudOrganization organization in organizations)
        {
            organizationBox.Items.Add(OrganizationLabel(organization));
        }
        if (organizationBox.Items.Count > 0)
            organizationBox.SelectedIndex = 0;

        createButton = StudioWidgets.CreatePrimaryButton("Бот болгох");
        createButton.IsDefault = true;
        createButton.Click += async (_, _) => await CreateAsync();
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.IsCancel = true;

        nameBox.TextChanged += (_, _) => UpdateEnabled();
        pinBox.TextChanged += (_, _) => UpdateEnabled();

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(StudioWidgets.CreateTitle("Энэ төхөөрөмжийг бот болгох"));
        panel.Children.Add(StudioWidgets.CreateFormRow("Байгууллага", organizationBox));
        panel.Children.Add(StudioWidgets.CreateFormRow("Ботын нэр", nameBox));
        panel.Children.Add(StudioWidgets.CreateFormRow("Дотоод мэйл", emailBox));
        panel.Children.Add(StudioWidgets.CreateHint(
            "Дотоод мэйл сонголтоор. Байхгүй бол ботын дугаараар нэрлэгдэнэ."));
        panel.Children.Add(StudioWidgets.CreateFormRow("ПИН (4 тоо)", pinBox));

        // No strength meter, no complexity rule: 0000 and 1234 are allowed on
        // purpose. The PIN unlocks the seat on this machine; it never restores
        // owner rights, so four digits are not what carries the security.
        panel.Children.Add(StudioWidgets.CreateHint(
            "ПИН нь энэ машин дээр ботын түгжээг тайлна. Эзэмшигчийн эрхийг сэргээдэггүй."));

        var warning = new StackPanel { Margin = new Thickness(0, 12, 0, 8) };
        warning.Children.Add(StudioWidgets.CreateSectionHeader("Энэ төхөөрөмж бот болмогц"));
        warning.Children.Add(StudioWidgets.CreateText("• таны нэвтрэлт энэ машинаас УСТАНА"));
        warning.Children.Add(StudioWidgets.CreateText("• буцаах ганц зам нь дахин нэвтрэх"));
        warning.Children.Add(StudioWidgets.CreateText("• энэ машины хувийн төслүүд ботын төлөвт үл харагдана"));
        panel.Children.Add(StudioWidgets.CreateCard(warning));

        panel.Children.Add(resultText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(createButton);
        panel.Children.Add(buttons);

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = panel,
        };
        UpdateEnabled();
    }

    internal static string OrganizationLabel(StudioCloudOrganization organization) =>
        string.IsNullOrWhiteSpace(organization.DisplayName)
            ? string.IsNullOrWhiteSpace(organization.LegalName)
                ? organization.OrganizationId
                : organization.LegalName
            : organization.DisplayName;

    private static bool IsFourDigits(string value) =>
        value.Length == 4 && value.All(char.IsAsciiDigit);

    private void UpdateEnabled() =>
        createButton.IsEnabled =
            organizationBox.SelectedIndex >= 0 &&
            !string.IsNullOrWhiteSpace(nameBox.Text) &&
            IsFourDigits(pinBox.Text ?? "");

    private async Task CreateAsync()
    {
        createButton.IsEnabled = false;
        resultText.Text = "Ботын суудал үүсгэж байна…";
        StudioCloudOrganization organization = organizations[organizationBox.SelectedIndex];
        try
        {
            // A seat is created before the PIN is set and before the device is
            // seated, so a failure in either later step used to leave one
            // behind - and there is no way to delete a seat, so every retry
            // added another. Reuse a seat of the same name instead: it is the
            // one this owner made a moment ago for this very purpose.
            string wantedName = nameBox.Text.Trim();
            StudioCloudBotSeat? seat = null;
            try
            {
                StudioCloudBotSeatListResponse existing =
                    await account.ListBotSeatsAsync(organization.OrganizationId);
                seat = existing.Items.FirstOrDefault(item =>
                    item.DisplayName.Trim().Equals(wantedName, StringComparison.OrdinalIgnoreCase));
                if (seat is not null)
                    resultText.Text = "Ижил нэртэй суудал байсныг ашиглаж байна…";
            }
            catch (Exception)
            {
                // Could not look: fall through and create. A duplicate is
                // recoverable; refusing to seat the machine is not.
            }

            seat ??= await account.CreateBotSeatAsync(
                organization.OrganizationId,
                wantedName,
                emailBox.Text.Trim());

            resultText.Text = "ПИН тавьж байна…";
            _ = await account.SetBotPinAsync(organization.OrganizationId, seat.BotId, pinBox.Text.Trim());

            // The seat is created and the PIN is set; the last step erases this
            // machine's owner credential. If that fails the whole transition is
            // rolled back inside the service - a machine that is half seated is
            // worse than one that refused.
            resultText.Text = "Бот төлөвт шилжиж байна…";
            _ = await account.EnterBotStateAsync(organization.OrganizationId, seat.BotId);

            Seated = new StudioBotDeviceState
            {
                BotId = seat.BotId,
                OrganizationId = organization.OrganizationId,
                DisplayName = string.IsNullOrWhiteSpace(seat.DisplayName)
                    ? nameBox.Text.Trim()
                    : seat.DisplayName,
                // Sealed, not stored in the clear: until the PIN is entered
                // the machine does not act as the seat at all.
                SealedSeat = Convert.ToBase64String(StudioBotPinVault.Seal(
                    seat.BotId,
                    pinBox.Text.Trim(),
                    StudioBotDeviceState.ResolveSeatIdentity(seat.BotId, seat.InternalEmail)).Blob),
                EnteredAtUtc = DateTimeOffset.UtcNow,
                EnteredByEmail = account.Current?.Email ?? "",
            };
            DialogResult = true;
        }
        catch (Exception exception)
        {
            resultText.Foreground = StudioTheme.DangerBrush;
            resultText.Text = BotSeatErrors.Describe(
                exception,
                "Бот болгож чадсангүй. Энэ машины нэвтрэлтийг устгаж чадаагүй тул " +
                "шилжилтийг зогсоов — хагас шилжсэн төхөөрөмж үлдээхгүйн тулд. " +
                "Studio-г дахин нээгээд оролдоно уу.");
            UpdateEnabled();
        }
    }
}

/// <summary>
/// The owner's view of the organisation's seats: who is linked, the PIN, and
/// the two ways out. Owner session only.
/// </summary>
internal sealed class BotSeatManagementDialog : Window
{
    private readonly StudioAccountService account;
    private StudioCloudOrganization organization;
    private readonly ListView seatList = new();
    private readonly TextBlock summaryText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock resultText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
    };

    private readonly ComboBox organizationBox = new();
    private readonly IReadOnlyList<StudioCloudOrganization> organizations;

    // Assignment is its own act. A seat is put ON a project by the owner;
    // WHO fills the seat is a separate question, answered by an invitation.
    // They used to be one step - the invitation created the assignment - so an
    // empty seat could not be assigned at all, and nobody could see which seat
    // worked on what.
    private readonly ListView assignmentList = new() { Height = 150 };
    private readonly TextBlock assignmentSummary = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 4),
    };
    private List<StudioCloudBotAssignment> assignments = [];
    private readonly Button assignButton = StudioWidgets.CreateButton("Төсөлд томилох");
    private readonly Button changeRolesButton = StudioWidgets.CreateButton("Үүрэг солих");
    private readonly Button unassignButton = StudioWidgets.CreateButton("Томилолт хасах");

    private StudioCloudBotAssignment? SelectedAssignment =>
        assignmentList.SelectedItem is AssignmentRow row
            ? assignments.FirstOrDefault(item =>
                item.AssignmentId.Equals(row.AssignmentId, StringComparison.OrdinalIgnoreCase))
            : null;

    private sealed record AssignmentRow(
        string AssignmentId,
        string Project,
        string Roles,
        string Assigned);

    public BotSeatManagementDialog(
        StudioAccountService account,
        IReadOnlyList<StudioCloudOrganization> organizations)
    {
        this.account = account;
        this.organizations = organizations;
        this.organization = organizations[0];
        Title = "Ботын удирдлага";
        Width = 720;
        Height = 560;
        MinWidth = 640;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);

        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Нэр", Width = 200, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(SeatRow.DisplayName)) });
        view.Columns.Add(new GridViewColumn { Header = "Дотоод мэйл", Width = 210, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(SeatRow.InternalEmail)) });
        view.Columns.Add(new GridViewColumn { Header = "Гишүүн", Width = 210, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(SeatRow.Member)) });
        view.Columns.Add(new GridViewColumn { Header = "Төхөөрөмж", Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(SeatRow.Device)) });
        view.Columns.Add(new GridViewColumn { Header = "Төлөв", Width = 120, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(SeatRow.Status)) });
        view.Columns.Add(new GridViewColumn { Header = "Үүсгэсэн", Width = 130, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(SeatRow.Created)) });
        seatList.View = view;

        Button reveal = StudioWidgets.CreateButton("ПИН харах");
        reveal.Click += async (_, _) => await RevealPinAsync();
        Button change = StudioWidgets.CreateButton("ПИН солих");
        change.Click += async (_, _) => await ChangePinAsync();
        Button unlock = StudioWidgets.CreateButton("Түгжээ тайлах");
        unlock.Click += async (_, _) => await UnlockAsync();
        Button invite = StudioWidgets.CreateButton("Гишүүн урих");
        invite.Click += async (_, _) => await InviteAsync();
        Button release = StudioWidgets.CreateDangerButton("Суудал чөлөөлөх");
        release.Click += async (_, _) => await ReleaseAsync();
        Button delete = StudioWidgets.CreateDangerButton("Хоосон суудал устгах");
        // The condition is on the button, not discovered by pressing it. A seat
        // that still holds sources or a member is refused BY THE SERVER - this
        // client does not guess at emptiness, it only says what the rule is.
        delete.ToolTip = "Зөвхөн гишүүнгүй, эх үүсвэргүй суудлыг устгана. " +
            "Эх үүсвэртэй суудлыг устгавал тэдгээр нь эзэнгүй үлдэх тул сервер татгалзана.";
        delete.Click += async (_, _) => await DeleteAsync();
        Button close = StudioWidgets.CreateButton("Хаах");
        close.IsCancel = true;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        foreach (Button button in new[] { reveal, change, unlock, invite, release, delete, close })
        {
            actions.Children.Add(button);
        }

        var panel = new DockPanel { Margin = new Thickness(18) };
        // The organisation is CHOSEN here, not assumed. Listing the first one
        // silently was the bug: seats created under the organisation the owner
        // picked did not appear under the one this dialog happened to read.
        foreach (StudioCloudOrganization item in organizations)
        {
            organizationBox.Items.Add(BotSeatCreateDialog.OrganizationLabel(item));
        }
        organizationBox.SelectedIndex = 0;
        organizationBox.SelectionChanged += async (_, _) =>
        {
            if (organizationBox.SelectedIndex >= 0)
            {
                organization = organizations[organizationBox.SelectedIndex];
                await RefreshAsync();
            }
        };

        var header = new StackPanel();
        header.Children.Add(StudioWidgets.CreateTitle("Ботын удирдлага"));
        if (organizations.Count > 1)
            header.Children.Add(StudioWidgets.CreateFormRow("Байгууллага", organizationBox));
        header.Children.Add(summaryText);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        var footer = new StackPanel();
        footer.Children.Add(resultText);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        var assignmentActions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0),
        };
        assignButton.Click += async (_, _) => await AssignProjectAsync();
        changeRolesButton.Click += async (_, _) => await ChangeAssignmentRolesAsync();
        unassignButton.Click += async (_, _) => await RemoveAssignmentAsync();
        foreach (Button button in new[] { assignButton, changeRolesButton, unassignButton })
            assignmentActions.Children.Add(button);

        var assignmentView = new GridView();
        assignmentView.Columns.Add(new GridViewColumn { Header = "Төсөл", Width = 300, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(AssignmentRow.Project)) });
        assignmentView.Columns.Add(new GridViewColumn { Header = "Үүрэг", Width = 250, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(AssignmentRow.Roles)) });
        assignmentView.Columns.Add(new GridViewColumn { Header = "Томилсон", Width = 130, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(AssignmentRow.Assigned)) });
        assignmentList.View = assignmentView;
        assignmentList.SelectionChanged += (_, _) => RefreshAssignmentActions();

        var assignmentPanel = new StackPanel();
        assignmentPanel.Children.Add(assignmentSummary);
        assignmentPanel.Children.Add(assignmentList);
        assignmentPanel.Children.Add(assignmentActions);
        DockPanel.SetDock(assignmentPanel, Dock.Bottom);
        panel.Children.Add(assignmentPanel);

        seatList.SelectionChanged += async (_, _) => await RefreshAssignmentsAsync();
        panel.Children.Add(seatList);
        Content = panel;

        Loaded += async (_, _) => await RefreshAsync();
    }

    /// <summary>
    /// One seat as the owner needs to see it.
    ///
    /// Member and Device are two facts, kept apart on purpose: the seat belongs
    /// to the organisation, the person staffed on it can change without the seat
    /// moving, and the machine sitting on it is a third thing again. The server
    /// has published all three for a while; this window showed none of them, so
    /// an owner could not tell which seat to release or who was on it.
    /// </summary>
    private sealed record SeatRow(
        string BotId,
        string DisplayName,
        string InternalEmail,
        string Status,
        string Created,
        string Member,
        string Device);

    private SeatRow? Selected => seatList.SelectedItem as SeatRow;

    /// <summary>
    /// The projects the selected seat works on. Read whenever the selection
    /// moves, because "which seat is on which project" is the question this
    /// window is for.
    /// </summary>
    private async Task RefreshAssignmentsAsync()
    {
        assignments = [];
        assignmentList.ItemsSource = null;
        if (Selected is null)
        {
            assignmentSummary.Text = "Суудлаа сонгоно уу.";
            RefreshAssignmentActions();
            return;
        }

        assignmentSummary.Text = $"«{Selected.DisplayName}» — томилолт уншиж байна…";
        try
        {
            StudioCloudBotAssignmentListResponse response =
                await account.ListBotAssignmentsAsync(organization.OrganizationId, Selected.BotId);
            assignments = [.. response.Assignments];
            assignmentList.ItemsSource = assignments
                .Select(item => new AssignmentRow(
                    item.AssignmentId,
                    string.IsNullOrWhiteSpace(item.ProjectName) ? item.ProjectId : item.ProjectName,
                    item.Roles.Count == 0 ? "—" : string.Join(", ", item.Roles),
                    item.AssignedAtUtc.ToLocalTime().ToString("yyyy-MM-dd")))
                .ToList();
            // Nothing assigned is an answer, not an empty screen to wonder at.
            assignmentSummary.Text = assignments.Count == 0
                ? $"«{Selected.DisplayName}» ямар ч төсөлд томилогдоогүй байна."
                : $"«{Selected.DisplayName}» — {assignments.Count} төсөлд томилогдсон.";
        }
        catch (Exception exception)
        {
            assignmentSummary.Text = BotSeatErrors.Describe(exception, "Томилолт уншигдсангүй.");
        }
        RefreshAssignmentActions();
    }

    private void RefreshAssignmentActions()
    {
        assignButton.IsEnabled = Selected is not null;
        bool hasAssignment = SelectedAssignment is not null;
        changeRolesButton.IsEnabled = hasAssignment;
        unassignButton.IsEnabled = hasAssignment;
    }

    private async Task<IReadOnlyList<StudioProjectRole>> RoleCatalogueAsync()
    {
        try
        {
            return await account.ListProjectRolesAsync();
        }
        catch (Exception exception)
        {
            assignmentSummary.Text = BotSeatErrors.Describe(exception, "Үүргийн жагсаалт уншигдсангүй.");
            return [];
        }
    }

    private async Task AssignProjectAsync()
    {
        if (!RequireSelection())
            return;

        var dialog = new BotAssignmentDialog(
            Selected!.DisplayName,
            await ProjectChoicesAsync(),
            await RoleCatalogueAsync())
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || dialog.ProjectId.Length == 0)
            return;

        try
        {
            StudioCloudBotAssignment created = await account.AssignBotProjectAsync(
                organization.OrganizationId,
                Selected!.BotId,
                dialog.ProjectId,
                dialog.Roles);
            resultText.Text =
                $"«{Selected!.DisplayName}» — {(string.IsNullOrWhiteSpace(created.ProjectName) ? created.ProjectId : created.ProjectName)} " +
                $"төсөлд томилогдлоо ({string.Join(", ", created.Roles)}).";
            await RefreshAssignmentsAsync();
        }
        catch (Exception exception)
        {
            resultText.Text = BotSeatErrors.Describe(exception, "Томилолт үүсээгүй.");
        }
    }

    private async Task<IReadOnlyList<StudioCloudProjectSummary>> ProjectChoicesAsync()
    {
        try
        {
            return await account.ListProjectsAsync();
        }
        catch (Exception exception)
        {
            resultText.Text = BotSeatErrors.Describe(exception, "Төслийн жагсаалт уншигдсангүй.");
            return [];
        }
    }

    private async Task ChangeAssignmentRolesAsync()
    {
        if (SelectedAssignment is not { } assignment)
            return;

        // The same catalogue and the same picker the team roster uses.
        var dialog = new ProjectMemberRoleDialog(
            Selected!.DisplayName,
            string.IsNullOrWhiteSpace(assignment.ProjectName) ? assignment.ProjectId : assignment.ProjectName,
            await RoleCatalogueAsync(),
            assignment.Roles)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || dialog.Draft is null)
            return;

        try
        {
            StudioCloudBotAssignment changed = await account.ChangeBotAssignmentRolesAsync(
                organization.OrganizationId,
                Selected!.BotId,
                assignment.AssignmentId,
                dialog.Draft.Roles);
            resultText.Text = $"Үүрэг шинэчлэгдлээ: {string.Join(", ", changed.Roles)}.";
            await RefreshAssignmentsAsync();
        }
        catch (Exception exception)
        {
            resultText.Text = BotSeatErrors.Describe(exception, "Үүрэг солигдсонгүй.");
        }
    }

    private async Task RemoveAssignmentAsync()
    {
        if (SelectedAssignment is not { } assignment)
            return;

        string project = string.IsNullOrWhiteSpace(assignment.ProjectName)
            ? assignment.ProjectId
            : assignment.ProjectName;
        if (StudioMessageDialog.Show(
                this,
                $"«{Selected!.DisplayName}» суудлыг «{project}» төслөөс хасах уу?" +
                Environment.NewLine + Environment.NewLine +
                "Суудал, түүний гишүүн, ба энэ суудлын үүсгэсэн ЭХ ҮҮСВЭРҮҮД хэвээр " +
                "үлдэнэ — ажил зогссоноос эзэмшил шилждэггүй.",
                "Томилолт хасах",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            await account.RemoveBotAssignmentAsync(
                organization.OrganizationId,
                Selected!.BotId,
                assignment.AssignmentId);
            resultText.Text = $"«{project}» төслөөс хасагдлаа.";
            await RefreshAssignmentsAsync();
        }
        catch (Exception exception)
        {
            resultText.Text = BotSeatErrors.Describe(exception, "Томилолт хасагдсангүй.");
        }
    }

    private async Task RefreshAsync()
    {
        try
        {
            StudioCloudBotSeatListResponse response =
                await account.ListBotSeatsAsync(organization.OrganizationId);
            seatList.ItemsSource = response.Items
                .Select(seat => new SeatRow(
                    seat.BotId,
                    seat.DisplayName,
                    seat.InternalEmail,
                    seat.Status,
                    seat.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd"),
                    string.IsNullOrWhiteSpace(seat.MemberEmail)
                        ? "—"
                        : seat.MemberEmail +
                          (seat.MemberSinceUtc is { } since
                              ? $" ({since.ToLocalTime():yyyy-MM-dd})"
                              : ""),
                    seat.DeviceSeated
                        ? "сууж байна" +
                          (seat.DeviceSeatedAtUtc is { } seated
                              ? $" ({seated.ToLocalTime():yyyy-MM-dd})"
                              : "")
                        : "—"))
                .ToList();
            // An empty grid is not an answer. Say which organisation was read
            // and that it has no seats, so "nothing here" cannot be mistaken
            // for "nothing loaded".
            summaryText.Text =
                BotSeatCreateDialog.OrganizationLabel(organization) + "  ·  " +
                (response.Items.Count == 0
                    ? "энэ байгууллагад ботын суудал алга"
                    : $"{response.Items.Count} суудал") +
                "  ·  эзэлсэн: " +
                StudioBotSeatCounts.DescribeOccupancy(
                    response.OccupiedSeats,
                    response.DeviceRights,
                    response.DeviceRightsUnlimited) +
                (response.LicenceActive ? "" : "  ·  ⚠ лиценз идэвхгүй");
        }
        catch (Exception exception)
        {
            summaryText.Text = BotSeatErrors.Describe(exception, "Суудлын жагсаалт уншигдсангүй.");
        }
    }

    private bool RequireSelection()
    {
        if (Selected is not null)
            return true;
        resultText.Text = "Эхлээд суудал сонгоно уу.";
        return false;
    }

    private async Task RevealPinAsync()
    {
        if (!RequireSelection())
            return;
        try
        {
            StudioCloudBotPinReveal pin =
                await account.RevealBotPinAsync(organization.OrganizationId, Selected!.BotId);
            resultText.Text = pin.Locked
                ? $"ПИН: {pin.Pin}  ·  ⚠ энэ суудал түгжигдсэн байна."
                : $"ПИН: {pin.Pin}";
        }
        catch (Exception exception)
        {
            resultText.Text = BotSeatErrors.Describe(exception, "ПИН харагдсангүй.");
        }
    }

    private async Task ChangePinAsync()
    {
        if (!RequireSelection())
            return;
        var prompt = new BotPinPromptDialog("Шинэ ПИН", "Шинэ ПИН (4 тоо)") { Owner = this };
        if (prompt.ShowDialog() != true)
            return;
        try
        {
            StudioCloudBotPinSetResponse changed = await account.SetBotPinAsync(
                organization.OrganizationId,
                Selected!.BotId,
                prompt.Pin);

            // Both facts in one sentence: a new PIN that needs a re-registration
            // and does not say so leaves the employee typing it with nothing
            // happening and no reason shown anywhere.
            resultText.Text = changed.DeviceMustReRegister
                ? $"ПИН {prompt.Pin} болж солигдлоо. Тэр төхөөрөмж дээр ДАХИН БҮРТГҮҮЛЭХ " +
                  "шаардлагатай — шинэ ПИН-ийг дангаар нь оруулахад ажиллахгүй."
                : $"ПИН {prompt.Pin} болж солигдлоо.";
        }
        catch (Exception exception)
        {
            resultText.Text = BotSeatErrors.Describe(exception, "ПИН солигдсонгүй.");
        }
    }

    private async Task UnlockAsync()
    {
        if (!RequireSelection())
            return;
        try
        {
            await account.UnlockBotPinAsync(organization.OrganizationId, Selected!.BotId);
            resultText.Text = "Түгжээ тайлагдлаа.";
        }
        catch (Exception exception)
        {
            resultText.Text = BotSeatErrors.Describe(exception, "Түгжээ тайлагдсангүй.");
        }
    }

    private async Task InviteAsync()
    {
        if (!RequireSelection())
            return;
        var dialog = new BotMemberInvitationDialog(account, organization, Selected!.BotId, Selected!.DisplayName)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() == true)
        {
            resultText.Text = dialog.ResultMessage;
        }
        await Task.CompletedTask;
    }

    private async Task DeleteAsync()
    {
        if (!RequireSelection())
            return;
        if (StudioMessageDialog.Show(
                this,
                $"«{Selected!.DisplayName}» суудлыг устгах уу?" +
                Environment.NewLine + Environment.NewLine +
                "Зөвхөн ГИШҮҮНГҮЙ, ЭХ ҮҮСВЭРГҮЙ суудал устана. Аль нэг нь байвал " +
                "сервер татгалзаж, юу нь саад болсныг хэлнэ — эх үүсвэр ботын нэр " +
                "дээр бүртгэгддэг тул суудал уствал тэдгээр эзэнгүй үлдэх байсан." +
                Environment.NewLine + Environment.NewLine +
                "Суудалд төхөөрөмж сууж байвал устгал өөрөө чөлөөлнө — тэр машин " +
                "ботын төлөвөөс гарна." +
                Environment.NewLine + Environment.NewLine +
                "Энэ суудлын карьерын түүх, төслийн " +
                "томилолт УСТАХГҮЙ: тэдгээр нь ботын дугаар дээр тогтдог ба тэр " +
                "дугаар дахин ашиглагдахгүй.",
                "Суудал устгах",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        try
        {
            StudioCloudBotSeatDeleted deleted =
                await account.DeleteBotSeatAsync(organization.OrganizationId, Selected!.BotId);
            resultText.Text = deleted.DeviceReleased
                // Same correction as the release above: the SEAT is released on
                // the server; the machine that sat in it still has to be taken
                // out of bot state by its owner, at that machine.
                ? $"Суудал устлаа. Тэнд сууж байсан төхөөрөмжийн эрх цуцлагдав; " +
                  "ботын төлөвөөс нь тэр машин дээр эзэмшигч гаргана. " +
                  "Эзэлсэн: " + StudioBotSeatCounts.DescribeOccupancy(
                      deleted.OccupiedSeats,
                      deleted.DeviceRights,
                      deleted.DeviceRightsUnlimited)
                : "Суудал устлаа. Эзэлсэн: " +
                  StudioBotSeatCounts.DescribeOccupancy(
                      deleted.OccupiedSeats,
                      deleted.DeviceRights,
                      deleted.DeviceRightsUnlimited);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            resultText.Text = BotSeatErrors.Describe(exception, "Суудал устгагдсангүй.");
        }
    }

    private async Task ReleaseAsync()
    {
        if (!RequireSelection())
            return;
        if (StudioMessageDialog.Show(
                this,
                // PENDING (STU+SRV): when the initial bot-session route exists
                // (SRV contract docs/contracts/bot-session-initial-issue.example.json)
                // the device WILL learn of its release on the next start, and
                // this sentence goes back to promising it. Restore it only once
                // the client acts on bot_state_released_remotely by clearing the
                // local seat - not before, or the promise is empty again.
                //
                // Says what the code does. It used to promise that the device
                // would learn of the release and leave bot state by itself, and
                // nothing anywhere does that: the seat file on that machine is
                // untouched by this call, and the device cannot even ask - its
                // resume carries no credential the server will accept. A promise
                // the code does not keep is worse than a plainer sentence.
                $"«{Selected!.DisplayName}» суудлыг чөлөөлөх үү? Сервер дээрх эрх нь шууд " +
                "цуцлагдана. Харин тэр төхөөрөмж дээрх ботын төлөв өөрөө унтрахгүй — тэнд " +
                "эзэмшигч нэвтэрч «Ботын төлөвөөс гарах» дарж гаргана.",
                "Суудал чөлөөлөх",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }
        try
        {
            await account.LeaveBotStateAsync(organization.OrganizationId, Selected!.BotId);
            resultText.Text = "Суудал чөлөөлөгдлөө.";
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            resultText.Text = BotSeatErrors.Describe(exception, "Суудал чөлөөлөгдсөнгүй.");
        }
    }
}

/// <summary>Four digits, nothing else asked of them.</summary>
internal sealed class BotPinPromptDialog : Window
{
    private readonly TextBox pinBox = new() { MaxLength = 4 };
    private readonly Button okButton;

    public string Pin => pinBox.Text.Trim();

    public BotPinPromptDialog(string title, string label)
    {
        Title = title;
        Width = 360;
        Height = 200;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);

        okButton = StudioWidgets.CreatePrimaryButton("Болно");
        okButton.IsDefault = true;
        okButton.IsEnabled = false;
        okButton.Click += (_, _) => DialogResult = true;
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.IsCancel = true;
        pinBox.TextChanged += (_, _) =>
            okButton.IsEnabled = Pin.Length == 4 && Pin.All(char.IsAsciiDigit);

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(StudioWidgets.CreateFormRow(label, pinBox));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(okButton);
        panel.Children.Add(buttons);
        Content = panel;
    }
}

/// <summary>
/// Invites a person to work this seat. There is deliberately no "add directly":
/// the owner invites and the person accepts on their own device, so nobody's
/// name lands in a career record without their own act.
/// </summary>
internal sealed class BotMemberInvitationDialog : Window
{
    private readonly StudioAccountService account;
    private readonly StudioCloudOrganization organization;
    private readonly string botId;
    private readonly TextBox emailBox = new();
    private readonly ComboBox projectBox = new();
    private List<StudioCloudProjectSummary> projects = [];

    // Roles are CHOSEN from the server's catalogue, never typed. A free-text
    // box sat here while ProjectMemberRoleDialog and GET /project-roles - the
    // real catalogue, already used for ordinary team members - were a few lines
    // away. Whatever was typed went to the server unchecked and stayed in the
    // record; SRV confirmed nothing validates it yet.
    private readonly List<StudioProjectRole> roleCatalogue = [];
    private readonly List<string> selectedRoleCodes = [];
    private readonly TextBlock rolesText = new()
    {
        Foreground = StudioTheme.TextBrush,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Button chooseRolesButton = StudioWidgets.CreateButton("Үүрэг сонгох…");

    // Cancelling reaches only the invitation THIS dialog just sent, because the
    // id is the only handle there is: the server has no route that lists a
    // seat's outstanding invitations, so a sender cannot find one again after
    // closing this window. Named here rather than left implicit - it is a real
    // limit, not a design.
    private StudioCloudBotInvitation? sentInvitation;
    private readonly Button cancelInvitationButton =
        StudioWidgets.CreateDangerButton("Илгээсэн урилгыг цуцлах");
    private readonly Button closeButton = StudioWidgets.CreateButton("Болих");
    private readonly TextBlock resultText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly Button sendButton;

    public string ResultMessage { get; private set; } = "";

    public BotMemberInvitationDialog(
        StudioAccountService account,
        StudioCloudOrganization organization,
        string botId,
        string botDisplayName)
    {
        this.account = account;
        this.organization = organization;
        this.botId = botId;
        Title = "Гишүүн урих";
        Width = 520;
        Height = 400;
        MinWidth = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);

        sendButton = StudioWidgets.CreatePrimaryButton("Урилга илгээх");
        sendButton.IsDefault = true;
        sendButton.IsEnabled = false;
        sendButton.Click += async (_, _) => await SendAsync();
        closeButton.IsCancel = true;
        emailBox.TextChanged += (_, _) => UpdateEnabled();
        projectBox.SelectionChanged += (_, _) => UpdateEnabled();
        chooseRolesButton.Click += (_, _) => ChooseRoles();

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(StudioWidgets.CreateTitle($"«{botDisplayName}» суудалд урих"));
        panel.Children.Add(StudioWidgets.CreateFormRow("Мэйл", emailBox));
        panel.Children.Add(StudioWidgets.CreateFormRow("Төсөл", projectBox));
        var rolesRow = new DockPanel();
        DockPanel.SetDock(chooseRolesButton, Dock.Right);
        rolesRow.Children.Add(chooseRolesButton);
        rolesRow.Children.Add(rolesText);
        panel.Children.Add(StudioWidgets.CreateFormRow("Үүрэг", rolesRow));
        panel.Children.Add(StudioWidgets.CreateHint(
            "Урилгыг тэр хүн өөрийн төхөөрөмж дээрээ зөвшөөрнө. Өмнөөс нь зөвшөөрөх зам байхгүй."));
        panel.Children.Add(resultText);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        cancelInvitationButton.Visibility = Visibility.Collapsed;
        cancelInvitationButton.Click += async (_, _) => await CancelSentAsync();
        buttons.Children.Add(cancelInvitationButton);
        buttons.Children.Add(closeButton);
        buttons.Children.Add(sendButton);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += async (_, _) => await LoadProjectsAsync();
    }

    private void UpdateEnabled()
    {
        rolesText.Text = selectedRoleCodes.Count == 0
            ? "Сонгоогүй"
            : string.Join(", ", selectedRoleCodes.Select(DescribeRole));
        chooseRolesButton.IsEnabled = roleCatalogue.Count > 0;
        // No role, no invitation. The old default sent "Member" whether anyone
        // meant it or not.
        sendButton.IsEnabled =
            !string.IsNullOrWhiteSpace(emailBox.Text) &&
            projectBox.SelectedIndex >= 0 &&
            selectedRoleCodes.Count > 0;
    }

    private string DescribeRole(string code) =>
        roleCatalogue.FirstOrDefault(role =>
            role.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is { } known &&
        !string.IsNullOrWhiteSpace(known.Label)
            ? known.Label
            : code;

    private void ChooseRoles()
    {
        // The same dialog the ordinary team roster uses, over the same
        // catalogue. One place decides what a role is.
        var dialog = new ProjectMemberRoleDialog(
            string.IsNullOrWhiteSpace(emailBox.Text) ? "Ботын суудлын гишүүн" : emailBox.Text.Trim(),
            emailBox.Text.Trim(),
            roleCatalogue,
            selectedRoleCodes)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || dialog.Draft is null)
            return;

        selectedRoleCodes.Clear();
        selectedRoleCodes.AddRange(dialog.Draft.Roles);
        UpdateEnabled();
    }

    /// <summary>
    /// Projects are chosen from the list rather than typed. A project id is
    /// not something anyone knows by heart, and a mistyped one is refused by
    /// the server with nothing to correct.
    /// </summary>
    private async Task LoadProjectsAsync()
    {
        try
        {
            projects = [.. await account.ListProjectsAsync()];
            roleCatalogue.Clear();
            roleCatalogue.AddRange(await account.ListProjectRolesAsync());
            foreach (StudioCloudProjectSummary project in projects)
            {
                projectBox.Items.Add(string.IsNullOrWhiteSpace(project.ProjectCode)
                    ? project.Name
                    : project.ProjectCode + " · " + project.Name);
            }
            if (projectBox.Items.Count > 0)
                projectBox.SelectedIndex = 0;
            else
                resultText.Text = "Урих төсөл олдсонгүй.";
        }
        catch (Exception exception)
        {
            resultText.Text = BotSeatErrors.Describe(exception, "Төслийн жагсаалт уншигдсангүй.");
        }
        UpdateEnabled();
    }

    private async Task CancelSentAsync()
    {
        if (sentInvitation is null)
            return;

        cancelInvitationButton.IsEnabled = false;
        resultText.Text = "Урилгыг цуцалж байна…";
        try
        {
            await account.CancelBotInvitationAsync(sentInvitation.InvitationId);
            ResultMessage = $"{sentInvitation.TargetEmail} рүү илгээсэн урилга цуцлагдлаа.";
            resultText.Text = ResultMessage;
            sentInvitation = null;
            cancelInvitationButton.Visibility = Visibility.Collapsed;
        }
        catch (Exception exception)
        {
            resultText.Text = BotSeatErrors.Describe(exception, "Урилга цуцлагдсангүй.");
            cancelInvitationButton.IsEnabled = true;
        }
    }

    private async Task SendAsync()
    {
        sendButton.IsEnabled = false;
        resultText.Text = "Илгээж байна…";
        try
        {
            string[] roles = [.. selectedRoleCodes];
            StudioCloudBotInvitation invitation = await account.InviteBotMemberAsync(
                organization.OrganizationId,
                botId,
                projects[projectBox.SelectedIndex].ProjectId,
                roles,
                emailBox.Text.Trim());
            ResultMessage =
                $"{invitation.TargetEmail} рүү урилга илгээгдлээ " +
                $"({invitation.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd} хүртэл).";

            // The window stays open so the invitation can be taken back at
            // once. It closes on "Хаах", which is where DialogResult is set.
            sentInvitation = invitation;
            resultText.Text = ResultMessage;
            cancelInvitationButton.Visibility = Visibility.Visible;
            emailBox.IsEnabled = false;
            projectBox.IsEnabled = false;
            chooseRolesButton.IsEnabled = false;
            closeButton.Content = "Хаах";
            closeButton.IsCancel = false;
            closeButton.Click += (_, _) => DialogResult = true;
        }
        catch (StudioAccountException exception)
        {
            // A refusal that cannot say what to do next is an obstacle, not a
            // message. The unregistered case is the one a person can act on.
            resultText.Foreground = StudioTheme.DangerBrush;
            resultText.Text = exception.ErrorCode.Equals(
                "invitation_target_not_registered",
                StringComparison.OrdinalIgnoreCase)
                ? $"{emailBox.Text.Trim()} нэрээр бүртгэл олдсонгүй. Тэр хүн эхлээд " +
                  "Erk-S-д бүртгүүлэх шаардлагатай."
                : exception.Message;
            UpdateEnabled();
        }
        catch (Exception exception)
        {
            resultText.Foreground = StudioTheme.DangerBrush;
            resultText.Text = exception.Message;
            UpdateEnabled();
        }
    }
}

/// <summary>
/// Turns a failed bot call into something the person can act on.
///
/// 404 is the one worth naming: every bot route is new, so a server that has
/// not been updated answers Not Found for all of them - and "404" on its own
/// reads as a bug in Studio rather than as a server that does not have the
/// feature yet.
/// </summary>
internal static class BotSeatErrors
{
    /// <summary>
    /// Turns a failure into the sentence the person needs.
    ///
    /// A 404 used to be rewritten wholesale as "this server does not support
    /// bots yet". That is true of a route that is not there, and false of every
    /// 404 the bot routes themselves answer - "this seat no longer exists" would
    /// have been reported as an out-of-date server, sending the owner to look in
    /// entirely the wrong place. A server that speaks names its reason in the
    /// error code; a route that is missing cannot. So the CODE decides, and the
    /// status only stands in when there is none.
    /// </summary>
    public static string Describe(Exception exception, string fallback)
    {
        if (exception is not StudioAccountException known)
            return fallback + " (" + exception.Message + ")";

        // The server spoke. Its own words are more specific than anything that
        // can be reconstructed from a status code - and for a released seat they
        // are the difference between "the owner released this device", "the seat
        // was deleted" and "it was handed back", which are three different
        // things to do next.
        if (!string.IsNullOrWhiteSpace(known.ErrorCode))
            return known.Message;

        if (known.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return "Сервер ботын боломжийг хараахан дэмжихгүй байна (404). " +
                "Энэ нь Studio-гийн алдаа биш — серверийн шинэчлэл шаардлагатай.";
        }

        return known.Message;
    }
}

/// <summary>
/// Puts a seat on a project: which project, and with which roles.
///
/// Separate from inviting somebody, because they are separate acts. An empty
/// seat can hold assignments, one seat can hold several, and filling the seat
/// later neither creates nor moves them.
/// </summary>
internal sealed class BotAssignmentDialog : Window
{
    private readonly ComboBox projectBox = new();
    private readonly IReadOnlyList<StudioCloudProjectSummary> projects;
    private readonly IReadOnlyList<StudioProjectRole> roleCatalogue;
    private readonly List<string> selectedRoles = [];
    private readonly TextBlock rolesText = new()
    {
        Foreground = StudioTheme.TextBrush,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Button chooseRolesButton = StudioWidgets.CreateButton("Үүрэг сонгох…");
    private readonly Button assignButton;

    public string ProjectId { get; private set; } = "";

    public IReadOnlyList<string> Roles => selectedRoles;

    public BotAssignmentDialog(
        string seatName,
        IReadOnlyList<StudioCloudProjectSummary> projects,
        IReadOnlyList<StudioProjectRole> roleCatalogue)
    {
        this.projects = projects;
        this.roleCatalogue = roleCatalogue;
        Title = "Төсөлд томилох";
        Width = 520;
        Height = 320;
        MinWidth = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);

        assignButton = StudioWidgets.CreatePrimaryButton("Томилох");
        assignButton.IsDefault = true;
        assignButton.IsEnabled = false;
        assignButton.Click += (_, _) => Accept();
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.IsCancel = true;
        chooseRolesButton.Click += (_, _) => ChooseRoles();
        projectBox.SelectionChanged += (_, _) => UpdateEnabled();

        foreach (StudioCloudProjectSummary project in projects)
        {
            projectBox.Items.Add(string.IsNullOrWhiteSpace(project.ProjectCode)
                ? project.Name
                : project.ProjectCode + " · " + project.Name);
        }
        if (projectBox.Items.Count > 0)
            projectBox.SelectedIndex = 0;

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(StudioWidgets.CreateTitle($"«{seatName}» суудлыг төсөлд томилох"));
        panel.Children.Add(StudioWidgets.CreateFormRow("Төсөл", projectBox));
        var rolesRow = new DockPanel();
        DockPanel.SetDock(chooseRolesButton, Dock.Right);
        rolesRow.Children.Add(chooseRolesButton);
        rolesRow.Children.Add(rolesText);
        panel.Children.Add(StudioWidgets.CreateFormRow("Үүрэг", rolesRow));
        panel.Children.Add(StudioWidgets.CreateHint(
            "Томилолт нь СУУДЛЫНХ. Гишүүн солигдоход энэ томилолт хэвээр үлдэнэ."));
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(assignButton);
        panel.Children.Add(buttons);
        Content = panel;
        UpdateEnabled();
    }

    private void UpdateEnabled()
    {
        rolesText.Text = selectedRoles.Count == 0
            ? "Сонгоогүй"
            : string.Join(", ", selectedRoles.Select(code =>
                roleCatalogue.FirstOrDefault(role =>
                    role.Code.Equals(code, StringComparison.OrdinalIgnoreCase)) is { } known &&
                !string.IsNullOrWhiteSpace(known.Label) ? known.Label : code));
        chooseRolesButton.IsEnabled = roleCatalogue.Count > 0;
        assignButton.IsEnabled = projectBox.SelectedIndex >= 0 && selectedRoles.Count > 0;
    }

    private void ChooseRoles()
    {
        var dialog = new ProjectMemberRoleDialog(
            "Ботын суудал",
            projectBox.SelectedIndex >= 0 ? (string)projectBox.Items[projectBox.SelectedIndex]! : "",
            roleCatalogue,
            selectedRoles)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true || dialog.Draft is null)
            return;

        selectedRoles.Clear();
        selectedRoles.AddRange(dialog.Draft.Roles);
        UpdateEnabled();
    }

    private void Accept()
    {
        if (projectBox.SelectedIndex < 0 || selectedRoles.Count == 0)
            return;
        ProjectId = projects[projectBox.SelectedIndex].ProjectId;
        DialogResult = true;
    }
}
