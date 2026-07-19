using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed class NewProjectDialog : Window
{
    private readonly IReadOnlyList<StudioCloudOrganization> organizations;
    private readonly ComboBox organizationBox = new();
    private readonly TextBlock organizationDetails = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 19,
    };
    private readonly TextBox codeBox = new();
    private readonly TextBox nameBox = new();
    private readonly ComboBox clientTypeBox = new();
    private readonly TextBox clientNameBox = new();
    private Grid? clientNameRow;
    private TextBlock? clientNameLabel;
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

    public ProjectCreationRequest CreationRequest
    {
        get
        {
            OrganizationOption? organization = SelectedOption;
            return new ProjectCreationRequest
            {
                Code = codeBox.Text.Trim(),
                Name = nameBox.Text.Trim(),
                Description = descriptionBox.Text.Trim(),
                Channel = ProjectCreationChannels.Studio,
                InitiatorType = ProjectInitiatorTypes.DesignOrganization,
                InitiatorOrganizationId = organization?.OrganizationId ?? "",
                InitiatorOrganizationName = organization?.LegalName ?? "",
                ClientType = (clientTypeBox.SelectedItem as ClientTypeOption)?.Value ??
                    ProjectClientTypes.Citizen,
                ClientName = clientNameBox.Text.Trim(),
                ClientEmail = clientEmailBox.Text.Trim(),
                SiteAddress = siteAddressBox.Text.Trim(),
            };
        }
    }

    public NewProjectDialog(IReadOnlyList<StudioCloudOrganization> organizations)
    {
        this.organizations = organizations
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.OrganizationId) &&
                item.OrganizationType.Equals("DesignCompany", StringComparison.OrdinalIgnoreCase) &&
                (item.CurrentUserRole.Equals("Organization Owner", StringComparison.OrdinalIgnoreCase) ||
                 item.CurrentUserRole.Equals("Organization Admin", StringComparison.OrdinalIgnoreCase)))
            .GroupBy(item => item.OrganizationId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(OrganizationDisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        Title = "Шинэ төсөл";
        Width = 720;
        Height = 690;
        MinWidth = 620;
        MinHeight = 610;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        codeBox.Text = $"STUDIO-{DateTime.Now:yyyyMMdd-HHmm}";
        organizationBox.SelectionChanged += (_, _) => RefreshOrganizationDetails();
        clientTypeBox.ItemsSource = new[]
        {
            new ClientTypeOption("Иргэн", ProjectClientTypes.Citizen),
            new ClientTypeOption("Байгууллага", ProjectClientTypes.Organization),
            new ClientTypeOption("Төрийн байгууллага", ProjectClientTypes.GovernmentAuthority),
        };
        clientTypeBox.SelectionChanged += (_, _) => RefreshClientNameEditor();
        clientTypeBox.SelectedIndex = 0;
        TextSearch.SetTextPath(organizationBox, nameof(OrganizationOption.SearchText));
        createButton = StudioWidgets.CreatePrimaryButton("Төсөл үүсгэх");
        createButton.IsDefault = true;
        createButton.Click += (_, _) => Accept();
        StudioTheme.Apply(this);
        Content = BuildContent();
        RefreshClientNameEditor();

        RefreshOrganizationOptions();
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
            "Төсөл таны админ эрхтэй зураг төслийн байгууллагын Cloud workspace-д үүснэ."));
        form.Children.Add(StudioWidgets.CreateFormRow("Үүсгэгч байгууллага", organizationBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Эрхийн мэдээлэл", BuildOrganizationDetails()));
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн код", codeBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн нэр", nameBox));
        form.Children.Add(StudioWidgets.CreateFormRow(
            "Төслийн төрөл",
            ReadOnlyValue("Барилга архитектурын загвар зураг")));
        form.Children.Add(StudioWidgets.CreateFormRow("Үе шат", ReadOnlyValue("Загвар зураг")));
        form.Children.Add(StudioWidgets.CreateFormRow("Захиалагчийн төрөл", clientTypeBox));
        clientNameRow = StudioWidgets.CreateFormRow("Захиалагчийн нэр", clientNameBox);
        clientNameLabel = clientNameRow.Children.OfType<TextBlock>().FirstOrDefault();
        form.Children.Add(clientNameRow);
        form.Children.Add(StudioWidgets.CreateFormRow("Захиалагчийн и-мэйл", clientEmailBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн хаяг", siteAddressBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Товч мэдээлэл", descriptionBox));
        root.Children.Add(StudioWidgets.CreateScrollHost(form));
        return root;
    }

    private UIElement BuildOrganizationDetails() => new Border
    {
        Background = StudioTheme.PanelBrush,
        BorderThickness = new Thickness(0),
        CornerRadius = new CornerRadius(StudioTheme.CornerRadius),
        Padding = new Thickness(12, 9, 12, 9),
        MinHeight = 58,
        Child = organizationDetails,
    };

    private void RefreshClientNameEditor()
    {
        string clientType = (clientTypeBox.SelectedItem as ClientTypeOption)?.Value ??
            ProjectClientTypes.Citizen;
        if (clientNameLabel is not null)
            clientNameLabel.Text = ProjectClientTypes.ClientNameFieldLabel(clientType);
        clientNameBox.ToolTip = ProjectClientTypes.ShowsDirectClientName(clientType)
            ? "Захиалагч иргэний овог, нэр"
            : "Нүүр хуудас болон төслийн мэдээлэлд бичигдэх байгууллагын нэр";
    }

    private void RefreshOrganizationOptions()
    {
        IEnumerable<OrganizationOption> ownOptions = organizations
            .Select(item => new OrganizationOption(item));
        OrganizationOption[] options = ownOptions.ToArray();
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
                "Таны бүртгэлд админ эрхтэй зураг төслийн байгууллага алга.";
            return;
        }

        StudioCloudOrganization organization = option.Organization!;
        string role = string.IsNullOrWhiteSpace(organization.CurrentUserRole)
            ? "Гишүүний эрх"
            : organization.CurrentUserRole;
        string displayName = OrganizationDisplayName(organization);
        string legalName = OrganizationLegalName(organization);
        var lines = new List<string> { displayName };
        if (!displayName.Equals(legalName, StringComparison.OrdinalIgnoreCase))
            lines.Add("Хуулийн нэр: " + legalName);
        lines.Add(string.Join("  ·  ", organization.Status, role));
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
            ShowRequiredMessage("Төсөл үүсгэх байгууллагаа сонгоно уу.");
            return;
        }
        if (string.IsNullOrWhiteSpace(request.ClientName))
        {
            ShowRequiredMessage(ProjectClientTypes.ClientNameFieldLabel(request.ClientType) + "-ийг оруулна уу.");
            clientNameBox.Focus();
            return;
        }
        DialogResult = true;
    }

    private void ShowRequiredMessage(string message) => StudioMessageDialog.Show(
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

    private sealed record ClientTypeOption(string Label, string Value)
    {
        public override string ToString() => Label;
    }

    private sealed record OrganizationOption(StudioCloudOrganization Organization)
    {
        public string OrganizationId => Organization.OrganizationId;
        public string LegalName => OrganizationLegalName(Organization);
        public string SearchText => string.Join(
            " ",
            OrganizationDisplayName(Organization),
            Organization.LegalName,
            Organization.DisplayName,
            Organization.ShortName);

        public override string ToString() => OrganizationDisplayName(Organization);
    }
}
