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

    private static string OrganizationLabel(StudioCloudOrganization organization) =>
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
            StudioCloudBotSeat seat = await account.CreateBotSeatAsync(
                organization.OrganizationId,
                nameBox.Text.Trim(),
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
            resultText.Text = exception is StudioAccountException
                ? exception.Message
                : "Бот болгож чадсангүй. Энэ машины нэвтрэлтийг устгаж чадаагүй тул " +
                  "шилжилтийг зогсоов — хагас шилжсэн төхөөрөмж үлдээхгүйн тулд. " +
                  "Studio-г дахин нээгээд оролдоно уу. (" + exception.Message + ")";
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
    private readonly StudioCloudOrganization organization;
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

    public BotSeatManagementDialog(StudioAccountService account, StudioCloudOrganization organization)
    {
        this.account = account;
        this.organization = organization;
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
        Button close = StudioWidgets.CreateButton("Хаах");
        close.IsCancel = true;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        foreach (Button button in new[] { reveal, change, unlock, invite, release, close })
        {
            actions.Children.Add(button);
        }

        var panel = new DockPanel { Margin = new Thickness(18) };
        var header = new StackPanel();
        header.Children.Add(StudioWidgets.CreateTitle("Ботын удирдлага"));
        header.Children.Add(summaryText);
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        var footer = new StackPanel();
        footer.Children.Add(resultText);
        footer.Children.Add(actions);
        DockPanel.SetDock(footer, Dock.Bottom);
        panel.Children.Add(footer);
        panel.Children.Add(seatList);
        Content = panel;

        Loaded += async (_, _) => await RefreshAsync();
    }

    private sealed record SeatRow(string BotId, string DisplayName, string InternalEmail, string Status, string Created);

    private SeatRow? Selected => seatList.SelectedItem as SeatRow;

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
                    seat.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd")))
                .ToList();
            summaryText.Text =
                $"Эзэлсэн суудал: {response.OccupiedSeats} / {response.DeviceRights}" +
                (response.LicenceActive ? "" : "  ·  ⚠ лиценз идэвхгүй");
        }
        catch (Exception exception)
        {
            summaryText.Text = "Суудлын жагсаалт уншигдсангүй: " + exception.Message;
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
            resultText.Text = "ПИН харагдсангүй: " + exception.Message;
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
            resultText.Text = "ПИН солигдсонгүй: " + exception.Message;
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
            resultText.Text = "Тайлагдсангүй: " + exception.Message;
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

    private async Task ReleaseAsync()
    {
        if (!RequireSelection())
            return;
        if (StudioMessageDialog.Show(
                this,
                $"«{Selected!.DisplayName}» суудлыг чөлөөлөх үү? Тэр төхөөрөмж дараагийн " +
                "холболтдоо чөлөөлөгдсөнөө мэдэж, ботын төлөвөөс гарна.",
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
            resultText.Text = "Чөлөөлж чадсангүй: " + exception.Message;
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
    private readonly TextBox projectBox = new();
    private readonly TextBox rolesBox = new() { Text = "Member" };
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
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.IsCancel = true;
        emailBox.TextChanged += (_, _) => UpdateEnabled();
        projectBox.TextChanged += (_, _) => UpdateEnabled();

        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(StudioWidgets.CreateTitle($"«{botDisplayName}» суудалд урих"));
        panel.Children.Add(StudioWidgets.CreateFormRow("Мэйл", emailBox));
        panel.Children.Add(StudioWidgets.CreateFormRow("Төслийн дугаар", projectBox));
        panel.Children.Add(StudioWidgets.CreateFormRow("Үүрэг", rolesBox));
        panel.Children.Add(StudioWidgets.CreateHint(
            "Урилгыг тэр хүн өөрийн төхөөрөмж дээрээ зөвшөөрнө. Өмнөөс нь зөвшөөрөх зам байхгүй."));
        panel.Children.Add(resultText);
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        buttons.Children.Add(cancel);
        buttons.Children.Add(sendButton);
        panel.Children.Add(buttons);
        Content = panel;
    }

    private void UpdateEnabled() =>
        sendButton.IsEnabled =
            !string.IsNullOrWhiteSpace(emailBox.Text) &&
            !string.IsNullOrWhiteSpace(projectBox.Text);

    private async Task SendAsync()
    {
        sendButton.IsEnabled = false;
        resultText.Text = "Илгээж байна…";
        try
        {
            string[] roles = (rolesBox.Text ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            StudioCloudBotInvitation invitation = await account.InviteBotMemberAsync(
                organization.OrganizationId,
                botId,
                projectBox.Text.Trim(),
                roles.Length == 0 ? ["Member"] : roles,
                emailBox.Text.Trim());
            ResultMessage =
                $"{invitation.TargetEmail} рүү урилга илгээгдлээ " +
                $"({invitation.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd} хүртэл).";
            DialogResult = true;
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
