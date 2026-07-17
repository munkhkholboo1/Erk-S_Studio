using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed class NewProjectDialog : Window
{
    private readonly IReadOnlyList<StudioCloudOrganization> organizations;
    private readonly IReadOnlyList<StudioProjectCreationGrant> creationGrants;
    private readonly ComboBox initiatorTypeBox = new();
    private readonly ComboBox organizationBox = new();
    private readonly TextBlock organizationDetails = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 19,
    };
    private readonly TextBox codeBox = new();
    private readonly TextBox nameBox = new();
    private readonly TextBox clientNameBox = new();
    private readonly TextBox clientEmailBox = new();
    private readonly TextBox siteAddressBox = new();
    private readonly TextBox descriptionBox = new()
    {
        AcceptsReturn = true,
        MinHeight = 72,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };
    private readonly Button createButton;

    private OrganizationOption? SelectedOption => organizationBox.SelectedItem as OrganizationOption;
    public StudioCloudOrganization? SelectedOrganization => SelectedOption?.Organization;
    public StudioProjectCreationGrant? SelectedCreationGrant => SelectedOption?.CreationGrant;

    public ProjectCreationRequest CreationRequest
    {
        get
        {
            InitiatorOption? initiator = initiatorTypeBox.SelectedItem as InitiatorOption;
            OrganizationOption? organization = SelectedOption;
            return new ProjectCreationRequest
            {
                Code = codeBox.Text.Trim(),
                Name = nameBox.Text.Trim(),
                Description = descriptionBox.Text.Trim(),
                Channel = ProjectCreationChannels.Studio,
                InitiatorType = initiator?.Value ?? ProjectInitiatorTypes.DesignOrganization,
                InitiatorOrganizationId = organization?.OrganizationId ?? "",
                InitiatorOrganizationName = organization?.LegalName ?? "",
                ClientName = clientNameBox.Text.Trim(),
                ClientEmail = clientEmailBox.Text.Trim(),
                SiteAddress = siteAddressBox.Text.Trim(),
            };
        }
    }

    public NewProjectDialog(
        IReadOnlyList<StudioCloudOrganization> organizations,
        IReadOnlyList<StudioProjectCreationGrant>? creationGrants = null)
    {
        this.organizations = organizations
            .Where(item => !string.IsNullOrWhiteSpace(item.OrganizationId))
            .GroupBy(item => item.OrganizationId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(OrganizationDisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        this.creationGrants = (creationGrants ?? [])
            .Where(item => item.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
                item.ExpiresAtUtc > DateTimeOffset.UtcNow &&
                !string.IsNullOrWhiteSpace(item.OrganizationId))
            .GroupBy(item => item.GrantId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.OrganizationName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();

        Title = "Шинэ төсөл";
        Width = 720;
        Height = 690;
        MinWidth = 620;
        MinHeight = 610;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        codeBox.Text = $"STUDIO-{DateTime.Now:yyyyMMdd-HHmm}";
        initiatorTypeBox.ItemsSource = new[]
        {
            new InitiatorOption("Зураг төслийн байгууллага", ProjectInitiatorTypes.DesignOrganization, "DesignCompany"),
            new InitiatorOption("Төрийн байгууллага", ProjectInitiatorTypes.GovernmentAuthority, "PlanningAuthority"),
        };
        initiatorTypeBox.SelectionChanged += (_, _) => RefreshOrganizationOptions();
        organizationBox.SelectionChanged += (_, _) => RefreshOrganizationDetails();
        TextSearch.SetTextPath(organizationBox, nameof(OrganizationOption.SearchText));
        createButton = StudioWidgets.CreatePrimaryButton("Төсөл үүсгэх");
        createButton.IsDefault = true;
        createButton.Click += (_, _) => Accept();
        StudioTheme.Apply(this);
        Content = BuildContent();

        bool hasDesignOption = this.organizations.Any(item =>
            item.OrganizationType.Equals("DesignCompany", StringComparison.OrdinalIgnoreCase)) ||
            this.creationGrants.Any(item =>
                item.OrganizationType.Equals("DesignCompany", StringComparison.OrdinalIgnoreCase));
        initiatorTypeBox.SelectedIndex = hasDesignOption ? 0 : 1;
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var cancel = StudioWidgets.CreateButton("Болих");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => DialogResult = false;
        actions.Children.Add(cancel);
        actions.Children.Add(createButton);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var form = new StackPanel { MaxWidth = 760 };
        form.Children.Add(StudioWidgets.CreateTitle("Шинэ төсөл"));
        form.Children.Add(StudioWidgets.CreateHint(
            "Төсөл өөрийн байгууллагын бүртгэлээр эсвэл танд олгосон нэг удаагийн эрхээр үүснэ."));
        form.Children.Add(StudioWidgets.CreateFormRow("Үүсгэгч тал", initiatorTypeBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Үүсгэгч байгууллага", organizationBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Эрхийн мэдээлэл", BuildOrganizationDetails()));
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн код", codeBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн нэр", nameBox));
        form.Children.Add(StudioWidgets.CreateFormRow(
            "Төслийн төрөл",
            ReadOnlyValue("Барилга архитектурын загвар зураг")));
        form.Children.Add(StudioWidgets.CreateFormRow("Үе шат", ReadOnlyValue("Загвар зураг")));
        form.Children.Add(StudioWidgets.CreateFormRow("Захиалагч", clientNameBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Захиалагчийн и-мэйл", clientEmailBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн хаяг", siteAddressBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Товч мэдээлэл", descriptionBox));
        root.Children.Add(StudioWidgets.CreateScrollHost(form));
        return root;
    }

    private UIElement BuildOrganizationDetails() => new Border
    {
        Background = StudioTheme.PanelBrush,
        BorderBrush = StudioTheme.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(StudioTheme.CornerRadius),
        Padding = new Thickness(12, 9, 12, 9),
        MinHeight = 58,
        Child = organizationDetails,
    };

    private void RefreshOrganizationOptions()
    {
        InitiatorOption? initiator = initiatorTypeBox.SelectedItem as InitiatorOption;
        IEnumerable<OrganizationOption> ownOptions = organizations
            .Where(item => initiator is null || item.OrganizationType.Equals(
                initiator.RequiredOrganizationType,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => new OrganizationOption(item));
        IEnumerable<OrganizationOption> grantOptions = creationGrants
            .Where(item => initiator is null || item.OrganizationType.Equals(
                initiator.RequiredOrganizationType,
                StringComparison.OrdinalIgnoreCase))
            .Select(item => new OrganizationOption(item));
        OrganizationOption[] options = ownOptions.Concat(grantOptions).ToArray();
        organizationBox.ItemsSource = options;
        organizationBox.SelectedIndex = options.Length > 0 ? 0 : -1;
        organizationBox.IsEnabled = options.Length > 0;
        createButton.IsEnabled = options.Length > 0;
        RefreshOrganizationDetails();
    }

    private void RefreshOrganizationDetails()
    {
        OrganizationOption? option = SelectedOption;
        if (option is null)
        {
            organizationDetails.Text =
                "Таны бүртгэлд тохирох байгууллага эсвэл нэг удаагийн төсөл үүсгэх эрх алга.";
            return;
        }

        if (option.CreationGrant is not null)
        {
            organizationDetails.Text = string.Join(
                Environment.NewLine,
                option.CreationGrant.OrganizationName,
                "Нэг удаагийн төсөл үүсгэх эрх",
                $"Дуусах: {option.CreationGrant.ExpiresAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}",
                "Компанийн хувийн бүртгэл харагдахгүй. Төсөлд шаардлагатай snapshot автоматаар холбоно.");
            return;
        }

        StudioCloudOrganization organization = option.Organization!;
        string registration = string.IsNullOrWhiteSpace(organization.RegistrationNumber)
            ? "Регистр оруулаагүй"
            : "РД: " + organization.RegistrationNumber;
        string role = string.IsNullOrWhiteSpace(organization.CurrentUserRole)
            ? "Гишүүний эрх"
            : organization.CurrentUserRole;
        string displayName = OrganizationDisplayName(organization);
        string legalName = OrganizationLegalName(organization);
        var lines = new List<string> { displayName };
        if (!displayName.Equals(legalName, StringComparison.OrdinalIgnoreCase))
            lines.Add("Хуулийн нэр: " + legalName);
        lines.Add(string.Join("  ·  ", registration, organization.Status, role));
        organizationDetails.Text = string.Join(Environment.NewLine, lines);
    }

    private void Accept()
    {
        ProjectCreationRequest request = CreationRequest;
        if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.Name))
        {
            ShowRequiredMessage("Төслийн код болон нэрийг бөглөнө үү.");
            return;
        }
        if (string.IsNullOrWhiteSpace(request.InitiatorOrganizationId) || SelectedOption is null)
        {
            ShowRequiredMessage("Төсөл үүсгэх байгууллага эсвэл олгогдсон эрхээ сонгоно уу.");
            return;
        }
        DialogResult = true;
    }

    private void ShowRequiredMessage(string message) => MessageBox.Show(
        this,
        message,
        "Erk-S Studio",
        MessageBoxButton.OK,
        MessageBoxImage.Information);

    private static TextBox ReadOnlyValue(string value) => new()
    {
        Text = value,
        IsReadOnly = true,
        IsTabStop = false,
    };

    private static string OrganizationDisplayName(StudioCloudOrganization? organization)
    {
        if (organization is null)
            return "";
        if (!string.IsNullOrWhiteSpace(organization.DisplayName))
            return organization.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(organization.LegalName))
            return organization.LegalName.Trim();
        return organization.ShortName.Trim();
    }

    private static string OrganizationLegalName(StudioCloudOrganization? organization)
    {
        if (organization is null)
            return "";
        return string.IsNullOrWhiteSpace(organization.LegalName)
            ? OrganizationDisplayName(organization)
            : organization.LegalName.Trim();
    }

    private sealed record InitiatorOption(string Label, string Value, string RequiredOrganizationType)
    {
        public override string ToString() => Label;
    }

    private sealed record OrganizationOption(
        StudioCloudOrganization? Organization,
        StudioProjectCreationGrant? CreationGrant)
    {
        public OrganizationOption(StudioCloudOrganization organization) : this(organization, null)
        {
        }

        public OrganizationOption(StudioProjectCreationGrant grant) : this(null, grant)
        {
        }

        public string OrganizationId => Organization?.OrganizationId ?? CreationGrant?.OrganizationId ?? "";
        public string LegalName => Organization is null
            ? CreationGrant?.OrganizationName ?? ""
            : OrganizationLegalName(Organization);
        public string OrganizationType => Organization?.OrganizationType ?? CreationGrant?.OrganizationType ?? "";
        public string SearchText => string.Join(
            " ",
            Organization is null ? CreationGrant?.OrganizationName : OrganizationDisplayName(Organization),
            Organization?.LegalName,
            Organization?.DisplayName,
            Organization?.ShortName,
            Organization?.RegistrationNumber);

        public override string ToString()
        {
            if (CreationGrant is not null)
                return $"{CreationGrant.OrganizationName}  ·  нэг удаагийн эрх";
            string name = OrganizationDisplayName(Organization);
            return string.IsNullOrWhiteSpace(Organization!.RegistrationNumber)
                ? name
                : $"{name}  ·  РД {Organization.RegistrationNumber}";
        }
    }
}
