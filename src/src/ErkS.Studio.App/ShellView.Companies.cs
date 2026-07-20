using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using Microsoft.Win32;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private readonly ListView companyLibraryList = new() { MinWidth = 270 };
    private readonly TextBlock companyLibraryStatus = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBox libraryCompanyNameBox = new();
    private readonly TextBox libraryCompanyDisplayNameBox = new();
    private readonly TextBox libraryCompanyShortNameBox = new();
    private readonly TextBox libraryCompanyRegistrationBox = new();
    private readonly TextBox libraryCompanyLegalEntityTypeBox = new();
    private readonly TextBox libraryCompanyLegalFormBox = new();
    private readonly TextBox libraryCompanyRegisteredDateBox = new();
    private readonly TextBox libraryCompanyActivityDirectionsBox = new() { AcceptsReturn = true, MinHeight = 72, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox libraryCompanyOfficialRepresentativeBox = new();
    private readonly TextBox libraryCompanyCityBox = new();
    private readonly TextBox libraryCompanyAddressBox = new() { AcceptsReturn = true, MinHeight = 56, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox libraryCompanyPhonesBox = new() { AcceptsReturn = true, MinHeight = 52, TextWrapping = TextWrapping.Wrap };
    private readonly TextBox libraryCompanyEmailBox = new();
    private readonly TextBox libraryCompanyWebsiteBox = new();
    private readonly TextBox libraryCompanyLicenseScopeBox = new();
    private readonly TextBox libraryCompanyLicenseNumberBox = new();
    private readonly TextBox libraryCompanyDirectorTitleBox = new();
    private readonly TextBox libraryCompanyDirectorNameBox = new();
    private readonly TextBlock libraryCompanyRegistrySourceText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button libraryCompanyOpenRegistryButton = StudioWidgets.CreateButton("Улсын бүртгэлийн сан нээх");
    private readonly TextBlock companyLogoFileText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Image companyLogoPreviewImage = new() { Stretch = Stretch.Fill, SnapsToDevicePixels = true };
    private readonly Canvas companyLogoPreviewCanvas = new() { ClipToBounds = true };
    private readonly TextBlock companyLogoPlaceholder = new()
    {
        Text = "Лого сонгоогүй",
        Foreground = StudioTheme.FaintTextBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Slider companyLogoScaleSlider = new()
    {
        Minimum = 0.25,
        Maximum = 4,
        Value = 1,
        SmallChange = 0.05,
        LargeChange = 0.25,
        TickFrequency = 0.25,
        IsSnapToTickEnabled = false,
    };
    private readonly Slider companyLogoOffsetXSlider = new()
    {
        Minimum = -1,
        Maximum = 1,
        SmallChange = 0.02,
        LargeChange = 0.1,
        TickFrequency = 0.1,
    };
    private readonly Slider companyLogoOffsetYSlider = new()
    {
        Minimum = -1,
        Maximum = 1,
        SmallChange = 0.02,
        LargeChange = 0.1,
        TickFrequency = 0.1,
    };
    private readonly TextBlock companyLogoScaleValue = new();
    private readonly TextBlock companyLogoOffsetXValue = new();
    private readonly TextBlock companyLogoOffsetYValue = new();
    private readonly Button companyChooseLogoButton = StudioWidgets.CreateButton("Лого сонгох");
    private readonly Button companyRemoveLogoButton = StudioWidgets.CreateButton("Лого арилгах");
    private readonly Button companyResetLogoButton = StudioWidgets.CreateButton("Төвд тааруулах");
    private readonly Button companySaveButton = StudioWidgets.CreatePrimaryButton("Хадгалах");
    private readonly Button companyNewButton = StudioWidgets.CreateButton("Шинэ компани");
    private readonly Button companyUseInProjectButton = StudioWidgets.CreateButton("Төсөлд ашиглах");
    private readonly Button companyDeleteButton = StudioWidgets.CreateButton("Устгах");

    private List<CompanyCatalogEntry> companyEntries = [];
    private CompanyCatalogEntry? selectedCompanyEntry;
    private CompanyLibraryStore? companyLibraryStore;
    private string companyLibraryAccount = "";
    private string pendingCompanyLogoPath = "";
    private string requestedCompanySelectionId = "";
    private bool removeCompanyLogoRequested;
    private bool bindingCompanyEditor;
    private bool refreshingCompanies;
    private bool companyCatalogCloudVerified;

    private UIElement BuildCompaniesPage()
    {
        var root = new DockPanel
        {
            Margin = new Thickness(18),
            Background = StudioTheme.WindowBackgroundBrush,
        };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var refreshButton = StudioWidgets.CreateButton("Шинэчлэх");
        refreshButton.Click += async (_, _) => await RefreshCompaniesAsync(forceCloud: true);
        companyNewButton.Click += async (_, _) => await CreateCompanyDraftAsync();
        companySaveButton.Click += async (_, _) => await SaveSelectedCompanyAsync();
        companyUseInProjectButton.Click += async (_, _) => await UseSelectedCompanyInOpenProjectAsync();
        companyDeleteButton.Click += async (_, _) => await DeleteSelectedCompanyAsync();
        actions.Children.Add(refreshButton);
        actions.Children.Add(companyNewButton);
        actions.Children.Add(companySaveButton);
        actions.Children.Add(companyUseInProjectButton);
        actions.Children.Add(companyDeleteButton);
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        header.Children.Add(StudioWidgets.CreateTitle("Компани"));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        companyLibraryStatus.Foreground = StudioTheme.MutedTextBrush;
        companyLibraryStatus.Margin = new Thickness(0, 6, 0, 0);
        DockPanel.SetDock(companyLibraryStatus, Dock.Bottom);
        root.Children.Add(companyLibraryStatus);

        var body = new Grid();
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        ConfigureCompanyLibraryList();
        Grid.SetColumn(companyLibraryList, 0);
        body.Children.Add(companyLibraryList);

        var splitter = new GridSplitter
        {
            Width = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Stretch,
            Background = Brushes.Transparent,
        };
        Grid.SetColumn(splitter, 1);
        body.Children.Add(splitter);

        UIElement editor = BuildCompanyEditor();
        Grid.SetColumn(editor, 2);
        body.Children.Add(editor);
        root.Children.Add(body);
        return root;
    }

    private void ConfigureCompanyLibraryList()
    {
        var view = new GridView();
        view.Columns.Add(new GridViewColumn
        {
            Header = "Байгууллага",
            Width = 182,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(CompanyListRow.Name)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Эрх",
            Width = 88,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(CompanyListRow.Access)),
        });
        companyLibraryList.View = view;
        companyLibraryList.SelectionChanged += (_, _) =>
        {
            if (companyLibraryList.SelectedItem is CompanyListRow row)
            {
                BindCompanyEditor(row.Entry);
            }
            RefreshCompanyProjectActionUi();
        };
    }

    private UIElement BuildCompanyEditor()
    {
        var form = new StackPanel
        {
            Margin = new Thickness(14, 0, 14, 14),
            MaxWidth = 940,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        form.Children.Add(StudioWidgets.CreateSectionHeader("Улсын бүртгэлийн мэдээлэл"));
        form.Children.Add(StudioWidgets.CreateFormRow("Албан ёсны нэр", libraryCompanyNameBox, 180));
        form.Children.Add(StudioWidgets.CreateFormRow("Регистрийн дугаар", libraryCompanyRegistrationBox, 180));
        form.Children.Add(StudioWidgets.CreateFormRow("Хуулийн этгээдийн төрөл", libraryCompanyLegalEntityTypeBox, 180));
        form.Children.Add(StudioWidgets.CreateFormRow("Хуулийн хэлбэр", libraryCompanyLegalFormBox, 180));
        form.Children.Add(StudioWidgets.CreateFormRow("Бүртгэсэн огноо", libraryCompanyRegisteredDateBox, 180));
        form.Children.Add(StudioWidgets.CreateFormRow("Үйл ажиллагааны чиглэл", libraryCompanyActivityDirectionsBox, 180));
        form.Children.Add(StudioWidgets.CreateFormRow("Албан ёсны төлөөлөгч", libraryCompanyOfficialRepresentativeBox, 180));
        form.Children.Add(StudioWidgets.CreateFormRow("Хот / дүүрэг", libraryCompanyCityBox, 180));
        form.Children.Add(StudioWidgets.CreateFormRow("Бүртгэлтэй хаяг", libraryCompanyAddressBox, 180));
        libraryCompanyRegistrySourceText.Foreground = StudioTheme.MutedTextBrush;
        libraryCompanyOpenRegistryButton.Click += (_, _) => OpenOrganizationRegistrySource();
        var registrySourcePanel = new StackPanel();
        registrySourcePanel.Children.Add(libraryCompanyRegistrySourceText);
        registrySourcePanel.Children.Add(libraryCompanyOpenRegistryButton);
        form.Children.Add(StudioWidgets.CreateFormRow("Эх сурвалж", registrySourcePanel, 180));
        form.Children.Add(StudioWidgets.CreateSectionHeader("Байгууллагаас нөхөх мэдээлэл"));
        form.Children.Add(StudioWidgets.CreateFormRow("Харагдах нэр", libraryCompanyDisplayNameBox, 155));
        form.Children.Add(StudioWidgets.CreateFormRow("Товчилсон нэр", libraryCompanyShortNameBox, 155));
        form.Children.Add(StudioWidgets.CreateSectionHeader("Холбоо барих"));
        form.Children.Add(StudioWidgets.CreateFormRow("Утаснууд", libraryCompanyPhonesBox, 155));
        form.Children.Add(StudioWidgets.CreateFormRow("И-мэйл", libraryCompanyEmailBox, 155));
        form.Children.Add(StudioWidgets.CreateFormRow("Вэб сайт", libraryCompanyWebsiteBox, 155));
        form.Children.Add(StudioWidgets.CreateSectionHeader("Зураг төслийн эрх"));
        form.Children.Add(StudioWidgets.CreateFormRow("Лицензийн чиглэл", libraryCompanyLicenseScopeBox, 155));
        form.Children.Add(StudioWidgets.CreateFormRow("Лицензийн дугаар", libraryCompanyLicenseNumberBox, 155));
        form.Children.Add(StudioWidgets.CreateSectionHeader("Байгууллагын гэрчилгээний хуулбар"));
        form.Children.Add(BuildCompanyRegistrationDocumentEditor());
        form.Children.Add(StudioWidgets.CreateSectionHeader("Тусгай зөвшөөрлийн хуулбар"));
        form.Children.Add(BuildCompanyLicenseDocumentEditor());
        form.Children.Add(StudioWidgets.CreateSectionHeader("Зураг төсөлд төлөөлөх хүн"));
        form.Children.Add(StudioWidgets.CreateFormRow("Албан тушаал", libraryCompanyDirectorTitleBox, 155));
        form.Children.Add(StudioWidgets.CreateFormRow("Нэр", libraryCompanyDirectorNameBox, 155));
        form.Children.Add(StudioWidgets.CreateSectionHeader("Лого"));
        form.Children.Add(BuildCompanyLogoEditor());
        return StudioWidgets.CreateScrollHost(form);
    }

    private UIElement BuildCompanyLogoEditor()
    {
        var panel = new StackPanel { MaxWidth = 620, HorizontalAlignment = HorizontalAlignment.Left };
        var previewGrid = new Grid();
        companyLogoPreviewCanvas.Children.Add(companyLogoPreviewImage);
        previewGrid.Children.Add(companyLogoPreviewCanvas);
        previewGrid.Children.Add(companyLogoPlaceholder);
        var preview = new Border
        {
            Width = 480,
            Height = 190,
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brushes.White,
            BorderThickness = new Thickness(0),
            ClipToBounds = true,
            Child = previewGrid,
        };
        preview.SizeChanged += (_, _) => UpdateCompanyLogoPreviewLayout();
        panel.Children.Add(preview);

        companyLogoFileText.Foreground = StudioTheme.MutedTextBrush;
        companyLogoFileText.Margin = new Thickness(0, 6, 0, 6);
        panel.Children.Add(companyLogoFileText);
        var actions = new WrapPanel();
        companyChooseLogoButton.Click += (_, _) => ChooseCompanyLogo();
        companyRemoveLogoButton.Click += (_, _) => RemoveCompanyLogo();
        companyResetLogoButton.Click += (_, _) => ResetCompanyLogoPlacement();
        actions.Children.Add(companyChooseLogoButton);
        actions.Children.Add(companyRemoveLogoButton);
        actions.Children.Add(companyResetLogoButton);
        panel.Children.Add(actions);

        companyLogoScaleSlider.ValueChanged += (_, _) => OnCompanyLogoTransformChanged();
        companyLogoOffsetXSlider.ValueChanged += (_, _) => OnCompanyLogoTransformChanged();
        companyLogoOffsetYSlider.ValueChanged += (_, _) => OnCompanyLogoTransformChanged();
        panel.Children.Add(BuildLogoSliderRow("Томруулах", companyLogoScaleSlider, companyLogoScaleValue));
        panel.Children.Add(BuildLogoSliderRow("Хэвтээ байрлал", companyLogoOffsetXSlider, companyLogoOffsetXValue));
        panel.Children.Add(BuildLogoSliderRow("Босоо байрлал", companyLogoOffsetYSlider, companyLogoOffsetYValue));
        return panel;
    }

    private static UIElement BuildLogoSliderRow(string label, Slider slider, TextBlock value)
    {
        var row = new Grid { Margin = new Thickness(0, 2, 0, 2), Width = 480 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(125) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
        var labelBlock = new TextBlock
        {
            Text = label,
            Foreground = StudioTheme.MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(labelBlock, 0);
        row.Children.Add(labelBlock);
        Grid.SetColumn(slider, 1);
        row.Children.Add(slider);
        value.Foreground = StudioTheme.MutedTextBrush;
        value.TextAlignment = TextAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(value, 2);
        row.Children.Add(value);
        return row;
    }

    private async Task RefreshCompaniesAsync(bool forceCloud = false)
    {
        if (refreshingCompanies)
            return;
        if (!account.IsSignedIn)
        {
            companyCatalogCloudVerified = false;
            companyEntries = [];
            companyLibraryList.ItemsSource = Array.Empty<CompanyListRow>();
            ClearCompanyEditor();
            companyLibraryStatus.Text = "Компанийн санг харахын тулд Studio бүртгэлээр нэвтэрнэ үү.";
            return;
        }

        refreshingCompanies = true;
        try
        {
            CompanyLibraryStore store = EnsureCompanyLibraryStore();
            List<CompanyCatalogEntry> cached = store.Load()
                .Where(item => !item.SyncStatus.Equals(
                    CompanySyncStatuses.ProjectSnapshot,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            string previousId = string.IsNullOrWhiteSpace(requestedCompanySelectionId)
                ? selectedCompanyEntry?.Profile.OrganizationId ?? ""
                : requestedCompanySelectionId;
            try
            {
                IReadOnlyList<StudioCloudOrganization> cloudItems = await account.ListOrganizationsAsync();
                companyCatalogCloudVerified = true;
                var merged = new List<CompanyCatalogEntry>();
                foreach (StudioCloudOrganization cloud in cloudItems)
                {
                    CompanyCatalogEntry? old = cached.FirstOrDefault(item =>
                        item.Profile.OrganizationId.Equals(cloud.OrganizationId, StringComparison.OrdinalIgnoreCase));
                    old ??= cached.FirstOrDefault(item =>
                        item.Profile.OrganizationId.StartsWith("snapshot-", StringComparison.OrdinalIgnoreCase) &&
                        item.Profile.Name.Equals(cloud.LegalName, StringComparison.OrdinalIgnoreCase));
                    if (old?.SyncStatus == CompanySyncStatuses.PendingUpdate)
                    {
                        old.Profile.OrganizationId = cloud.OrganizationId;
                        old.CanManage = cloud.CanManage;
                        old.CurrentUserRole = cloud.CurrentUserRole;
                        merged.Add(old);
                        continue;
                    }
                    CompanyProfile profile = MapCloudCompany(cloud);
                    if (old is not null)
                    {
                        profile.RegistrationCertificateDocuments = old.Profile.RegistrationCertificateDocuments
                            .Select(document => document.Clone())
                            .ToList();
                        profile.DesignLicenseDocuments = old.Profile.DesignLicenseDocuments
                            .Select(document => document.Clone())
                            .ToList();
                    }
                    if (!string.IsNullOrWhiteSpace(cloud.LogoUrl))
                    {
                        bool canReuse = !forceCloud && old?.Profile.UpdatedAtUtc == cloud.UpdatedAtUtc &&
                            File.Exists(old.Profile.LogoPath);
                        if (canReuse)
                        {
                            profile.LogoPath = old!.Profile.LogoPath;
                        }
                        else
                        {
                            StudioDownloadedImage? logo = await account.GetOrganizationLogoAsync(cloud.LogoUrl);
                            if (logo is not null)
                                profile.LogoPath = store.StoreLogo(cloud.OrganizationId, logo.Bytes, logo.ContentType);
                        }
                    }
                    merged.Add(new CompanyCatalogEntry
                    {
                        Profile = profile,
                        CanManage = cloud.CanManage,
                        CurrentUserRole = cloud.CurrentUserRole,
                        SyncStatus = CompanySyncStatuses.Cloud,
                        DocumentsPendingCloudSync = old?.DocumentsPendingCloudSync == true,
                    });
                }
                merged.AddRange(cached.Where(item =>
                    item.SyncStatus is CompanySyncStatuses.PendingCreate or CompanySyncStatuses.PendingUpdate &&
                    merged.All(remote => !remote.Profile.OrganizationId.Equals(item.Profile.OrganizationId, StringComparison.OrdinalIgnoreCase))));
                companyEntries = merged.OrderBy(item => CompanyDisplayName(item.Profile), StringComparer.CurrentCultureIgnoreCase).ToList();
                store.Save(companyEntries);
                RefreshOpenProjectCompanyFromCatalog(companyEntries);
                companyLibraryStatus.Text = $"{companyEntries.Count} байгууллага · Cloud ERA";
            }
            catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
            {
                companyCatalogCloudVerified = false;
                companyEntries = cached.OrderBy(item => CompanyDisplayName(item.Profile), StringComparer.CurrentCultureIgnoreCase).ToList();
                companyLibraryStatus.Text = $"{companyEntries.Count} байгууллага · локал cache · Cloud шинэчлэгдсэнгүй";
                SetStatus("Компанийн cloud сан шинэчлэгдсэнгүй: " + exception.Message);
            }
            RefreshCompanyList(previousId);
            requestedCompanySelectionId = "";
            RefreshCompanyProjectActionUi();
        }
        finally
        {
            refreshingCompanies = false;
        }
    }

    private CompanyLibraryStore EnsureCompanyLibraryStore()
    {
        string email = account.Current?.Email ?? throw new InvalidOperationException("Studio account is required.");
        if (companyLibraryStore is null || !companyLibraryAccount.Equals(email, StringComparison.OrdinalIgnoreCase))
        {
            companyLibraryStore = CompanyLibraryStore.ForAccount(email);
            companyLibraryAccount = email;
        }
        return companyLibraryStore;
    }

    private void RefreshCompanyList(string selectedId)
    {
        List<CompanyListRow> rows = companyEntries.Select(entry => new CompanyListRow(entry)).ToList();
        companyLibraryList.ItemsSource = rows;
        CompanyListRow? selected = rows.FirstOrDefault(row =>
            row.Entry.Profile.OrganizationId.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) ?? rows.FirstOrDefault();
        companyLibraryList.SelectedItem = selected;
        if (selected is null)
            ClearCompanyEditor();
        else
            companyLibraryList.ScrollIntoView(selected);
        RefreshCompanyProjectActionUi();
    }

    private void OpenCompanyLibraryForProject()
    {
        string organizationId = state.HasOpenProject
            ? state.Project.Foundation.DesignCompany.OrganizationId
            : "";
        requestedCompanySelectionId = organizationId;
        if (!string.IsNullOrWhiteSpace(organizationId))
            selectedCompanyEntry = companyEntries.FirstOrDefault(item =>
                item.Profile.OrganizationId.Equals(organizationId, StringComparison.OrdinalIgnoreCase));
        SelectPage(StudioPage.Companies);
        if (selectedCompanyEntry is not null)
            RefreshCompanyList(selectedCompanyEntry.Profile.OrganizationId);
    }

    private void RefreshCompanyProjectActionUi()
    {
        if (!state.HasOpenProject)
        {
            companyUseInProjectButton.Content = "Төсөлд ашиглах";
            companyUseInProjectButton.IsEnabled = false;
            companyUseInProjectButton.ToolTip = "Эхлээд төсөл нээнэ үү.";
            return;
        }

        CompanyProfile? selected = selectedCompanyEntry?.Profile;
        bool designCompany = selected is not null && selected.OrganizationType.Equals(
            "DesignCompany",
            StringComparison.OrdinalIgnoreCase);
        if (!designCompany)
        {
            companyUseInProjectButton.IsEnabled = false;
            companyUseInProjectButton.Content = "Төсөлд ашиглах";
            companyUseInProjectButton.ToolTip = "Зөвхөн зураг төслийн байгууллага сонгоно.";
            return;
        }

        string assignedId = state.Project.Foundation.DesignCompany.OrganizationId;
        bool sameOrganization = !string.IsNullOrWhiteSpace(assignedId) &&
            assignedId.Equals(selected!.OrganizationId, StringComparison.OrdinalIgnoreCase);
        bool canManage = selectedCompanyEntry?.CanManage == true;
        bool canonicalCloudProfile = companyCatalogCloudVerified &&
            selectedCompanyEntry?.SyncStatus.Equals(CompanySyncStatuses.Cloud, StringComparison.OrdinalIgnoreCase) == true &&
            !selected!.OrganizationId.StartsWith("local-", StringComparison.OrdinalIgnoreCase);
        companyUseInProjectButton.IsEnabled = canonicalCloudProfile && canManage && !sameOrganization;
        companyUseInProjectButton.Content = sameOrganization
            ? "Төсөлд сонгогдсон"
            : string.IsNullOrWhiteSpace(assignedId)
                ? "Төслийн компани болгох"
                : "Компани солих";
        companyUseInProjectButton.ToolTip = !canonicalCloudProfile
            ? "Байгууллагын Cloud ERA бүртгэлийг шинэчилж баталгаажуулсны дараа төсөлд ашиглана."
            : sameOrganization
                ? "Энэ компани төсөлд баталгаажсан. Бүртгэлийн шинэчлэлт автоматаар төслийн snapshot-д орно."
            : canManage
                ? ProjectCompanySelectionPolicy(state.Project)
                : "Энэ компанийн нэр дээр төсөл хэрэгжүүлэх эрх шаардлагатай.";
    }

    private async Task UseSelectedCompanyInOpenProjectAsync()
    {
        if (!state.HasOpenProject || selectedCompanyEntry is null)
            return;
        CompanyProfile profile = selectedCompanyEntry.Profile;
        if (!profile.OrganizationType.Equals("DesignCompany", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Төсөлд зөвхөн зураг төслийн байгууллага сонгоно.");
            return;
        }

        ProjectWorkspace project = state.Project;
        ProjectCompanyAssignment assignment = project.Foundation.DesignCompany;
        if (!companyCatalogCloudVerified ||
            !selectedCompanyEntry.SyncStatus.Equals(CompanySyncStatuses.Cloud, StringComparison.OrdinalIgnoreCase) ||
            profile.OrganizationId.StartsWith("local-", StringComparison.OrdinalIgnoreCase))
        {
            SetStatus("Байгууллагын мэдээллийг Cloud ERA-д баталгаажуулж шинэчилсний дараа төсөлд ашиглана.");
            return;
        }
        if (!selectedCompanyEntry.CanManage)
        {
            SetStatus("Сонгосон компанийн нэр дээр төсөл хэрэгжүүлэх эрх баталгаажаагүй байна.");
            return;
        }
        bool sameOrganization = !string.IsNullOrWhiteSpace(assignment.OrganizationId) &&
            assignment.OrganizationId.Equals(profile.OrganizationId, StringComparison.OrdinalIgnoreCase);
        if (sameOrganization)
        {
            SetStatus($"{CompanyDisplayName(profile)} компани энэ төсөлд аль хэдийн баталгаажсан байна.");
            return;
        }

        string currentName = string.IsNullOrWhiteSpace(assignment.OrganizationSnapshot.DisplayName)
            ? assignment.OrganizationName
            : assignment.OrganizationSnapshot.DisplayName;
        if (!StudioRelationshipBoundary.Confirm(
                Window.GetWindow((DependencyObject)Root),
                StudioRelationshipAction.AssignDesignOrganization,
                $"{ValueOrDash(currentName)}  ->  {CompanyDisplayName(profile)}"))
        {
            return;
        }

        bool cloudLinked = project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.Cloud.ServerProjectId);
        ProjectCompanyAssignmentService.AssignToProject(project, profile, DateTimeOffset.UtcNow);
        state.SaveProject();
        BindProjectToUi();
        UpdateAlbum(silent: true, statusPrefix: "Төслийн компани шинэчлэгдлээ");
        if (cloudLinked)
        {
            SetStatus($"{CompanyDisplayName(profile)} компани сонгогдлоо. Cloud ERA assignment шинэчилж байна...");
            try
            {
                await ConfirmPendingProjectCompanyAssignmentAsync(profile);
                BindProjectToUi();
                UpdateAlbum(silent: true, statusPrefix: "Cloud ERA компанийн assignment шинэчлэгдлээ");
                SetStatus($"{CompanyDisplayName(profile)} компани төсөлд сонгогдож, Cloud ERA-д баталгаажлаа.");
            }
            catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
            {
                project.Cloud.SyncStatus = ProjectSyncStatuses.Pending;
                project.Cloud.LastSyncError = exception.Message;
                project.Cloud.LastSyncNote = "Компанийн сонголт локалд хадгалагдсан; Cloud ERA баталгаажуулалт хүлээгдэж байна.";
                state.SaveProject();
                BindProjectToUi();
                SetStatus($"{CompanyDisplayName(profile)} компани локалд сонгогдлоо. Cloud ERA sync хүлээгдэж байна: {exception.Message}");
            }
        }
        else
        {
            SetStatus($"{CompanyDisplayName(profile)} компани төсөлд сонгогдлоо.");
        }

        await RefreshProjectsAsync();
        RefreshCompanyProjectActionUi();
    }

    private async Task ConfirmPendingProjectCompanyAssignmentAsync(CompanyProfile? expectedProfile = null)
    {
        if (!state.HasOpenProject)
            return;
        ProjectWorkspace project = state.Project;
        ProjectCompanyAssignment assignment = project.Foundation.DesignCompany;
        bool cloudLinked = project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.Cloud.ServerProjectId);
        if (!cloudLinked || !assignment.AssignmentSource.Equals("StudioCloudPending", StringComparison.OrdinalIgnoreCase))
            return;

        CompanyProfile profile = (expectedProfile ?? assignment.OrganizationSnapshot).Clone();
        StudioCloudProjectDetail latest = await account.AssignDesignOrganizationAsync(
            project.Cloud.ServerProjectId,
            assignment.OrganizationId);
        state.LinkCurrentProjectToCloud(latest, account.Current!.ServerUrl, preserveCreation: true);
        ProjectCompanyAssignmentService.ConfirmCloudAssignment(state.Project, profile);
        state.SaveProject();
    }

    private async Task CreateCompanyDraftAsync()
    {
        if (!await EnsureSignedInAsync())
            return;
        CompanyCatalogEntry entry = new()
        {
            Profile = new CompanyProfile
            {
                OrganizationId = "local-" + Guid.NewGuid().ToString("N"),
                OrganizationType = "DesignCompany",
                DesignRepresentativeTitle = "Захирал",
                DirectorTitle = "Захирал",
            },
            CanManage = true,
            CurrentUserRole = "Organization Owner",
            SyncStatus = CompanySyncStatuses.PendingCreate,
        };
        companyEntries.Add(entry);
        EnsureCompanyLibraryStore().Save(companyEntries);
        RefreshCompanyList(entry.Profile.OrganizationId);
        libraryCompanyNameBox.Focus();
        SetStatus("Шинэ компанийн profile үүслээ. Мэдээллээ оруулаад хадгална уу.");
    }

    private async Task SaveSelectedCompanyAsync()
    {
        if (selectedCompanyEntry is null || !selectedCompanyEntry.CanManage)
            return;
        CompanyProfile profile = CollectCompanyEditor();

        CompanyLibraryStore store = EnsureCompanyLibraryStore();
        string previousSyncStatus = selectedCompanyEntry.SyncStatus;
        string logoUploadPath = pendingCompanyLogoPath;
        if (!string.IsNullOrWhiteSpace(pendingCompanyLogoPath))
        {
            profile.LogoPath = store.StoreLogo(profile.OrganizationId, pendingCompanyLogoPath);
            logoUploadPath = profile.LogoPath;
        }
        else if (!removeCompanyLogoRequested &&
                 previousSyncStatus is CompanySyncStatuses.PendingCreate or CompanySyncStatuses.PendingUpdate &&
                 File.Exists(profile.LogoPath))
        {
            logoUploadPath = profile.LogoPath;
        }
        selectedCompanyEntry.Profile = profile;
        selectedCompanyEntry.LogoRemovalPending = removeCompanyLogoRequested;
        selectedCompanyEntry.SyncStatus = profile.OrganizationId.StartsWith("local-", StringComparison.OrdinalIgnoreCase)
            ? CompanySyncStatuses.PendingCreate
            : CompanySyncStatuses.PendingUpdate;
        store.Save(companyEntries);

        try
        {
            StudioCloudOrganization cloud = profile.OrganizationId.StartsWith("local-", StringComparison.OrdinalIgnoreCase)
                ? await account.CreateOrganizationAsync(ToCloudCompanyRequest(profile))
                : await account.UpdateOrganizationAsync(profile.OrganizationId, ToCloudCompanyRequest(profile));
            string localLogoPath = profile.LogoPath;
            if (removeCompanyLogoRequested)
            {
                cloud = await account.DeleteOrganizationLogoAsync(cloud.OrganizationId);
                localLogoPath = "";
            }
            else if (!string.IsNullOrWhiteSpace(logoUploadPath))
            {
                cloud = await account.UploadOrganizationLogoAsync(cloud.OrganizationId, logoUploadPath);
            }
            CompanyProfile synced = MapCloudCompany(cloud);
            synced.LogoPath = localLogoPath;
            synced.RegistrationCertificateDocuments = profile.RegistrationCertificateDocuments
                .Select(document => document.Clone())
                .ToList();
            synced.DesignLicenseDocuments = profile.DesignLicenseDocuments
                .Select(document => document.Clone())
                .ToList();
            selectedCompanyEntry.Profile = synced;
            selectedCompanyEntry.CanManage = cloud.CanManage;
            selectedCompanyEntry.CurrentUserRole = cloud.CurrentUserRole;
            selectedCompanyEntry.SyncStatus = CompanySyncStatuses.Cloud;
            selectedCompanyEntry.LogoRemovalPending = false;
            pendingCompanyLogoPath = "";
            removeCompanyLogoRequested = false;
            store.Save(companyEntries);
            ApplyCompanyToOpenProject(synced, rebuildAlbum: true);
            RefreshCompanyList(synced.OrganizationId);
            companyLibraryStatus.Text = $"{companyEntries.Count} байгууллага · Cloud ERA";
            SetStatus("Компанийн мэдээлэл Cloud ERA-д хадгалагдлаа.");
        }
        catch (StudioAccountException exception) when (exception.StatusCode == HttpStatusCode.NotFound)
        {
            SaveCompanyAsPending(profile, store, exception.Message);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            SaveCompanyAsPending(profile, store, exception.Message);
        }
        catch (StudioAccountException exception)
        {
            SetStatus("Компанийн мэдээлэл хадгалагдсангүй: " + exception.Message);
        }
    }

    private async Task DeleteSelectedCompanyAsync()
    {
        CompanyCatalogEntry? entry = selectedCompanyEntry;
        if (entry is null || !entry.CanManage)
            return;

        bool localDraft = entry.Profile.OrganizationId.StartsWith("local-", StringComparison.OrdinalIgnoreCase) &&
            entry.SyncStatus.Equals(CompanySyncStatuses.PendingCreate, StringComparison.OrdinalIgnoreCase);
        bool owner = entry.CurrentUserRole.Equals("Organization Owner", StringComparison.OrdinalIgnoreCase);
        if (!localDraft && !owner)
        {
            SetStatus("Cloud ERA байгууллагыг зөвхөн owner устгах эрхтэй.");
            return;
        }

        MessageBoxResult confirmation = StudioMessageDialog.Show(
            Window.GetWindow(Root),
            "Энэ байгууллагыг устгах уу? Төслийн түүхэн snapshot болон өмнө үүссэн альбум хадгалагдана. Энэ үйлдлийг буцаахгүй.",
            "Байгууллага устгах",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation != MessageBoxResult.Yes)
            return;

        try
        {
            if (!localDraft)
                await account.DeleteOrganizationAsync(entry.Profile.OrganizationId);

            companyEntries.Remove(entry);
            selectedCompanyEntry = null;
            pendingCompanyLogoPath = "";
            removeCompanyLogoRequested = false;
            EnsureCompanyLibraryStore().Save(companyEntries);
            RefreshCompanyList("");
            companyLibraryStatus.Text = $"{companyEntries.Count} байгууллага · Cloud ERA";
            SetStatus("Байгууллага устгагдлаа. Төслийн түүхэн мэдээлэл хэвээр хадгалагдана.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            SetStatus("Байгууллага устгагдсангүй: " + exception.Message);
        }
    }

    private void SaveCompanyAsPending(CompanyProfile profile, CompanyLibraryStore store, string reason)
    {
        pendingCompanyLogoPath = "";
        store.Save(companyEntries);
        ApplyCompanyToOpenProject(profile, rebuildAlbum: true);
        RefreshCompanyList(profile.OrganizationId);
        companyLibraryStatus.Text = $"{companyEntries.Count} байгууллага · cloud sync хүлээгдэж байна";
        SetStatus("Компанийн мэдээлэл локал cache-д хадгалагдлаа. Cloud sync хүлээгдэж байна: " + reason);
    }

    private void BindCompanyEditor(CompanyCatalogEntry entry)
    {
        selectedCompanyEntry = entry;
        pendingCompanyLogoPath = "";
        removeCompanyLogoRequested = entry.LogoRemovalPending;
        CompanyProfile profile = entry.Profile;
        profile.Normalize();
        bindingCompanyEditor = true;
        libraryCompanyNameBox.Text = profile.Name;
        libraryCompanyDisplayNameBox.Text = profile.DisplayName;
        libraryCompanyShortNameBox.Text = profile.ShortName;
        libraryCompanyRegistrationBox.Text = profile.RegistrationNumber;
        libraryCompanyLegalEntityTypeBox.Text = profile.LegalEntityType;
        libraryCompanyLegalFormBox.Text = profile.LegalForm;
        libraryCompanyRegisteredDateBox.Text = profile.RegisteredAtUtc?.ToString("yyyy-MM-dd") ?? "";
        libraryCompanyActivityDirectionsBox.Text = string.Join(Environment.NewLine, profile.ActivityDirections);
        libraryCompanyOfficialRepresentativeBox.Text = profile.OfficialRepresentativeName;
        libraryCompanyCityBox.Text = profile.RegisteredCity;
        libraryCompanyAddressBox.Text = profile.Address;
        libraryCompanyPhonesBox.Text = string.Join(Environment.NewLine, profile.PhoneNumbers);
        libraryCompanyEmailBox.Text = profile.Email;
        libraryCompanyWebsiteBox.Text = profile.WebSite;
        libraryCompanyLicenseScopeBox.Text = profile.LicenseScope;
        libraryCompanyLicenseNumberBox.Text = profile.LicenseNumber;
        libraryCompanyDirectorTitleBox.Text = profile.DesignRepresentativeTitle;
        libraryCompanyDirectorNameBox.Text = profile.DesignRepresentativeName;
        libraryCompanyRegistrySourceText.Text = RegistrySourceLabel(profile);
        libraryCompanyOpenRegistryButton.IsEnabled = !string.IsNullOrWhiteSpace(profile.RegistrySourceUrl);
        companyLogoScaleSlider.Value = profile.LogoScale;
        companyLogoOffsetXSlider.Value = profile.LogoOffsetX;
        companyLogoOffsetYSlider.Value = profile.LogoOffsetY;
        BindCompanyDocumentDrafts(profile);
        bindingCompanyEditor = false;
        SetCompanyEditorEnabled(entry.CanManage);
        libraryCompanyRegistrationBox.IsReadOnly = !entry.CanManage ||
            (!entry.SyncStatus.Equals(CompanySyncStatuses.PendingCreate, StringComparison.OrdinalIgnoreCase) &&
             !string.IsNullOrWhiteSpace(profile.RegistrationNumber));
        LoadCompanyLogoPreview(profile.LogoPath);
        companyLibraryStatus.Text = string.Join(" · ", new[]
        {
            profile.Status,
            profile.VerificationStatus,
            entry.CurrentUserRole,
            CompanySyncLabel(entry.SyncStatus),
            entry.DocumentsPendingCloudSync ? "баримтын cloud sync хүлээгдэж байна" : "",
        }.Where(value => !string.IsNullOrWhiteSpace(value)));
        OnCompanyLogoTransformChanged();
    }

    private CompanyProfile CollectCompanyEditor()
    {
        CompanyProfile original = selectedCompanyEntry!.Profile;
        var profile = original.Clone();
        profile.Name = libraryCompanyNameBox.Text.Trim();
        profile.DisplayName = libraryCompanyDisplayNameBox.Text.Trim();
        profile.ShortName = libraryCompanyShortNameBox.Text.Trim();
        profile.RegistrationNumber = libraryCompanyRegistrationBox.Text.Trim();
        profile.LegalEntityType = libraryCompanyLegalEntityTypeBox.Text.Trim();
        profile.LegalForm = libraryCompanyLegalFormBox.Text.Trim();
        profile.RegisteredAtUtc = ParseCompanyRegisteredDate(libraryCompanyRegisteredDateBox.Text, original.RegisteredAtUtc);
        profile.ActivityDirections = libraryCompanyActivityDirectionsBox.Text
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        profile.OfficialRepresentativeName = libraryCompanyOfficialRepresentativeBox.Text.Trim();
        profile.RegisteredCity = libraryCompanyCityBox.Text.Trim();
        profile.Address = libraryCompanyAddressBox.Text.Trim();
        profile.PhoneNumbers = libraryCompanyPhonesBox.Text
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        profile.Phone = profile.PhoneNumbers.FirstOrDefault() ?? "";
        profile.Email = libraryCompanyEmailBox.Text.Trim();
        profile.WebSite = libraryCompanyWebsiteBox.Text.Trim();
        profile.LicenseScope = libraryCompanyLicenseScopeBox.Text.Trim();
        profile.LicenseNumber = libraryCompanyLicenseNumberBox.Text.Trim();
        profile.DesignRepresentativeTitle = libraryCompanyDirectorTitleBox.Text.Trim();
        profile.DesignRepresentativeName = libraryCompanyDirectorNameBox.Text.Trim();
        profile.DirectorTitle = profile.DesignRepresentativeTitle;
        profile.DirectorName = profile.DesignRepresentativeName;
        profile.LogoScale = companyLogoScaleSlider.Value;
        profile.LogoOffsetX = companyLogoOffsetXSlider.Value;
        profile.LogoOffsetY = companyLogoOffsetYSlider.Value;
        ApplyCompanyDocumentDrafts(profile);
        profile.UpdatedAtUtc = DateTimeOffset.UtcNow;
        profile.Signers = string.IsNullOrWhiteSpace(profile.DesignRepresentativeName)
            ? []
            : [new CompanySigner { Role = profile.DesignRepresentativeTitle, FullName = profile.DesignRepresentativeName }];
        profile.Normalize();
        return profile;
    }

    private void SetCompanyEditorEnabled(bool enabled)
    {
        foreach (TextBox box in CompanyEditorTextBoxes())
            box.IsReadOnly = !enabled;
        companyChooseLogoButton.IsEnabled = enabled;
        companyRemoveLogoButton.IsEnabled = enabled;
        companyResetLogoButton.IsEnabled = enabled;
        companyLogoScaleSlider.IsEnabled = enabled;
        companyLogoOffsetXSlider.IsEnabled = enabled;
        companyLogoOffsetYSlider.IsEnabled = enabled;
        companySaveButton.IsEnabled = enabled;
        bool localDraft = selectedCompanyEntry?.Profile.OrganizationId.StartsWith("local-", StringComparison.OrdinalIgnoreCase) == true &&
            selectedCompanyEntry?.SyncStatus.Equals(CompanySyncStatuses.PendingCreate, StringComparison.OrdinalIgnoreCase) == true;
        bool owner = selectedCompanyEntry?.CurrentUserRole.Equals("Organization Owner", StringComparison.OrdinalIgnoreCase) == true;
        bool protectedAuthority = selectedCompanyEntry?.Profile.OrganizationType.Equals("PlanningAuthority", StringComparison.OrdinalIgnoreCase) == true;
        companyDeleteButton.IsEnabled = enabled && !protectedAuthority && (localDraft || owner);
        companyDeleteButton.ToolTip = companyDeleteButton.IsEnabled
            ? "Туршилтын эсвэл буруу байгууллагыг устгах"
            : "Cloud ERA байгууллагыг зөвхөн owner устгана.";
        RefreshCompanyDocumentLists();
    }

    private IEnumerable<TextBox> CompanyEditorTextBoxes()
    {
        yield return libraryCompanyNameBox;
        yield return libraryCompanyDisplayNameBox;
        yield return libraryCompanyShortNameBox;
        yield return libraryCompanyRegistrationBox;
        yield return libraryCompanyLegalEntityTypeBox;
        yield return libraryCompanyLegalFormBox;
        yield return libraryCompanyRegisteredDateBox;
        yield return libraryCompanyActivityDirectionsBox;
        yield return libraryCompanyOfficialRepresentativeBox;
        yield return libraryCompanyCityBox;
        yield return libraryCompanyAddressBox;
        yield return libraryCompanyPhonesBox;
        yield return libraryCompanyEmailBox;
        yield return libraryCompanyWebsiteBox;
        yield return libraryCompanyLicenseScopeBox;
        yield return libraryCompanyLicenseNumberBox;
        yield return libraryCompanyDirectorTitleBox;
        yield return libraryCompanyDirectorNameBox;
    }

    private void ClearCompanyEditor()
    {
        selectedCompanyEntry = null;
        pendingCompanyLogoPath = "";
        removeCompanyLogoRequested = false;
        foreach (TextBox box in CompanyEditorTextBoxes())
            box.Clear();
        libraryCompanyRegistrySourceText.Text = "";
        libraryCompanyOpenRegistryButton.IsEnabled = false;
        bindingCompanyEditor = true;
        companyLogoScaleSlider.Value = 1;
        companyLogoOffsetXSlider.Value = 0;
        companyLogoOffsetYSlider.Value = 0;
        bindingCompanyEditor = false;
        companyRegistrationDocumentDrafts = [];
        companyLicenseDocumentDrafts = [];
        SetCompanyEditorEnabled(false);
        LoadCompanyLogoPreview("");
        OnCompanyLogoTransformChanged();
    }

    private void ChooseCompanyLogo()
    {
        if (selectedCompanyEntry is null || !selectedCompanyEntry.CanManage)
            return;
        var dialog = new OpenFileDialog
        {
            Title = "Компанийн лого сонгох",
            Filter = "Зураг (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true,
            DereferenceLinks = true,
            ValidateNames = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(Root)) != true)
            return;
        try
        {
            LoadCompanyLogoPreview(dialog.FileName);
            pendingCompanyLogoPath = dialog.FileName;
            removeCompanyLogoRequested = false;
            selectedCompanyEntry.LogoRemovalPending = false;
            companyLogoFileText.Text = Path.GetFileName(dialog.FileName);
            ResetCompanyLogoPlacement();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException or FormatException)
        {
            SetStatus("Лого зураг нээгдсэнгүй: " + exception.Message);
        }
    }

    private void RemoveCompanyLogo()
    {
        if (selectedCompanyEntry is null || !selectedCompanyEntry.CanManage)
            return;
        pendingCompanyLogoPath = "";
        removeCompanyLogoRequested = true;
        selectedCompanyEntry.LogoRemovalPending = true;
        selectedCompanyEntry.Profile.LogoPath = "";
        LoadCompanyLogoPreview("");
    }

    private void ResetCompanyLogoPlacement()
    {
        bindingCompanyEditor = true;
        companyLogoScaleSlider.Value = 1;
        companyLogoOffsetXSlider.Value = 0;
        companyLogoOffsetYSlider.Value = 0;
        bindingCompanyEditor = false;
        OnCompanyLogoTransformChanged();
    }

    private void LoadCompanyLogoPreview(string path)
    {
        companyLogoPreviewImage.Source = null;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            companyLogoPreviewImage.Source = bitmap;
            companyLogoFileText.Text = Path.GetFileName(path);
            companyLogoPlaceholder.Visibility = Visibility.Collapsed;
        }
        else
        {
            companyLogoFileText.Text = "Лого сонгоогүй";
            companyLogoPlaceholder.Visibility = Visibility.Visible;
        }
        UpdateCompanyLogoPreviewLayout();
    }

    private void OnCompanyLogoTransformChanged()
    {
        companyLogoScaleValue.Text = $"{companyLogoScaleSlider.Value:0.00}x";
        companyLogoOffsetXValue.Text = $"{companyLogoOffsetXSlider.Value:+0.00;-0.00;0.00}";
        companyLogoOffsetYValue.Text = $"{companyLogoOffsetYSlider.Value:+0.00;-0.00;0.00}";
        UpdateCompanyLogoPreviewLayout();
        if (!bindingCompanyEditor && selectedCompanyEntry is not null)
        {
            selectedCompanyEntry.Profile.LogoScale = companyLogoScaleSlider.Value;
            selectedCompanyEntry.Profile.LogoOffsetX = companyLogoOffsetXSlider.Value;
            selectedCompanyEntry.Profile.LogoOffsetY = companyLogoOffsetYSlider.Value;
        }
    }

    private void UpdateCompanyLogoPreviewLayout()
    {
        if (companyLogoPreviewImage.Source is not BitmapSource bitmap)
            return;
        double viewportWidth = companyLogoPreviewCanvas.ActualWidth;
        double viewportHeight = companyLogoPreviewCanvas.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0 || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
            return;
        double contain = Math.Min(viewportWidth / bitmap.PixelWidth, viewportHeight / bitmap.PixelHeight);
        double width = bitmap.PixelWidth * contain * companyLogoScaleSlider.Value;
        double height = bitmap.PixelHeight * contain * companyLogoScaleSlider.Value;
        companyLogoPreviewImage.Width = width;
        companyLogoPreviewImage.Height = height;
        Canvas.SetLeft(companyLogoPreviewImage,
            (viewportWidth - width) / 2 + companyLogoOffsetXSlider.Value * viewportWidth * 0.5);
        Canvas.SetTop(companyLogoPreviewImage,
            (viewportHeight - height) / 2 + companyLogoOffsetYSlider.Value * viewportHeight * 0.5);
    }

    private void RefreshOpenProjectCompanyFromLocalCache()
    {
        if (!state.HasOpenProject || !account.IsSignedIn)
            return;
        try
        {
            RefreshOpenProjectCompanyFromCatalog(EnsureCompanyLibraryStore().Load().Where(item =>
                !item.SyncStatus.Equals(CompanySyncStatuses.ProjectSnapshot, StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }
    }

    private void RefreshLocalProjectCompanySnapshotsFromCache()
    {
        if (!account.IsSignedIn)
            return;

        try
        {
            IReadOnlyList<CompanyCatalogEntry> entries = EnsureCompanyLibraryStore().Load()
                .Where(item => !item.SyncStatus.Equals(
                    CompanySyncStatuses.ProjectSnapshot,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
            IReadOnlyList<CompanyProfile> profiles = entries
                .Select(item => item.Profile)
                .Where(profile => !string.IsNullOrWhiteSpace(profile.OrganizationId))
                .ToList();
            foreach (ProjectCatalogItem item in new LocalProjectCatalog().ListProjects().Where(item => !item.IsLegacyProject))
            {
                if (state.HasOpenProject && state.ProjectPath is not null &&
                    item.ProjectPath.Equals(state.ProjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                ProjectWorkspace project = ProjectWorkspaceStore.Load(item.ProjectPath);
                string organizationId = project.Foundation.DesignCompany.OrganizationId;
                if (string.IsNullOrWhiteSpace(organizationId))
                    continue;
                CompanyProfile? current = profiles.FirstOrDefault(profile => profile.OrganizationId.Equals(
                    organizationId,
                    StringComparison.OrdinalIgnoreCase));
                if (current is not null && ProjectCompanyAssignmentService.RefreshAssignedSnapshot(project, current))
                    ProjectWorkspaceStore.Save(project, item.ProjectPath);
            }

            RefreshOpenProjectCompanyFromCatalog(entries);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
        }
    }

    private void RefreshOpenProjectCompanyFromCatalog(IEnumerable<CompanyCatalogEntry> entries)
    {
        if (!state.HasOpenProject)
            return;
        ProjectCompanyAssignment assignment = state.Project.Foundation.DesignCompany;
        if (string.IsNullOrWhiteSpace(assignment.OrganizationId))
            return;
        CompanyProfile? current = entries
            .Select(item => item.Profile)
            .FirstOrDefault(profile => profile.OrganizationId.Equals(
                assignment.OrganizationId,
                StringComparison.OrdinalIgnoreCase));
        if (current is null || !ProjectCompanyAssignmentService.RefreshAssignedSnapshot(state.Project, current))
            return;
        state.MarkFoundationContentChanged();
        if (projectWorkspaceOpen)
            BindProjectToUi();
    }

    private ProjectAssetSourceReconciliationResult ReconcileCompanyAssetSources()
    {
        var total = new ProjectAssetSourceReconciliationResult();
        if (!account.IsSignedIn)
            return total;

        CompanyLibraryStore store = EnsureCompanyLibraryStore();
        List<CompanyCatalogEntry> cached = store.Load()
            .Where(item => !item.SyncStatus.Equals(
                CompanySyncStatuses.ProjectSnapshot,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
        bool changed = false;
        foreach (CompanyCatalogEntry entry in cached)
        {
            ProjectAssetSourceReconciliationResult result =
                ProjectAssetSourceReconciler.ReconcileCompanyProfile(entry.Profile, store);
            total.Merge(result);
            if (!result.Changed)
                continue;
            entry.DocumentsPendingCloudSync = true;
            changed = true;
        }

        if (!changed)
            return total;

        store.Save(cached);
        RefreshOpenProjectCompanyFromCatalog(cached);
        if (companyEntries.Count > 0)
        {
            foreach (CompanyCatalogEntry current in companyEntries)
            {
                CompanyCatalogEntry? refreshed = cached.FirstOrDefault(item =>
                    item.Profile.OrganizationId.Equals(
                        current.Profile.OrganizationId,
                        StringComparison.OrdinalIgnoreCase));
                if (refreshed is null)
                    continue;
                current.Profile = refreshed.Profile;
                current.DocumentsPendingCloudSync = refreshed.DocumentsPendingCloudSync;
            }
        }
        return total;
    }

    private void ApplyCompanyToOpenProject(CompanyProfile profile, bool rebuildAlbum)
    {
        bool matches = state.HasOpenProject &&
            ProjectCompanyAssignmentService.MatchesAssignedOrganization(state.Project, profile);
        bool changed = state.HasOpenProject &&
            ProjectCompanyAssignmentService.RefreshAssignedSnapshot(state.Project, profile);
        if (!matches)
            return;
        if (changed)
        {
            state.MarkFoundationContentChanged();
            BindProjectToUi();
        }
        if (rebuildAlbum)
            UpdateAlbum(silent: true, statusPrefix: "Компанийн мэдээлэл шинэчлэгдлээ");
    }

    private static string CompanyDisplayName(CompanyProfile profile)
    {
        if (!string.IsNullOrWhiteSpace(profile.DisplayName))
            return profile.DisplayName.Trim();
        if (!string.IsNullOrWhiteSpace(profile.Name))
            return profile.Name.Trim();
        return profile.ShortName.Trim();
    }

    private void RefreshProjectCompanySelectorUi()
    {
        bool assigned = state.HasOpenProject &&
            ProjectCompanyAssignmentService.HasAssignedOrganization(state.Project);
        string label = assigned ? "Компани солих" : "Компани сонгох";
        if (projectCompanyLibraryButton.Content is StackPanel stack &&
            stack.Children.OfType<TextBlock>().LastOrDefault() is { } text)
        {
            text.Text = label;
        }
        else
        {
            projectCompanyLibraryButton.Content = label;
        }
        projectCompanyLibraryButton.ToolTip = assigned
            ? "Төслийн баталгаажсан зураг төслийн байгууллагыг санаатайгаар солих"
            : "Төслийн зураг төслийн байгууллагыг компанийн сангаас сонгох";
    }

    private static string ProjectCompanyAssignmentDescription(ProjectWorkspace project)
    {
        ProjectCompanyAssignment assignment = project.Foundation.DesignCompany;
        string stage = string.IsNullOrWhiteSpace(assignment.StageName) ? "Загвар зураг" : assignment.StageName;
        bool cloudLinked = project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.Cloud.ServerProjectId);
        if (cloudLinked && project.Creation.InitiatorType.Equals(ProjectInitiatorTypes.GovernmentAuthority, StringComparison.OrdinalIgnoreCase))
        {
            string authority = string.IsNullOrWhiteSpace(project.Creation.InitiatorOrganizationName)
                ? "эрх бүхий байгууллага"
                : project.Creation.InitiatorOrganizationName;
            return $"{stage} · Cloud ERA assignment · Оноосон тал: {authority} · Компанийн сангаас сольж болно";
        }
        if (cloudLinked)
            return $"{stage} · Cloud ERA assignment · {ProjectCompanyAssignmentSourceLabel(assignment.AssignmentSource)}";
        return $"{stage} · Local assignment · Компанийн сангаас сонгож болно";
    }

    private static string ProjectCompanySelectionPolicy(ProjectWorkspace project)
    {
        bool cloudLinked = project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(project.Cloud.ServerProjectId);
        return cloudLinked
            ? "Сонгосон компанийг төслийн зураг төслийн байгууллага болгож Cloud ERA assignment-ийг шинэчилнэ."
            : "Сонгосон компанийг локал төслийн зураг төслийн байгууллага болгоно.";
    }

    private static string ProjectCompanyAssignmentSourceLabel(string source) => source switch
    {
        "CloudERA" => "Cloud ERA-аас оноосон",
        "StudioSelfCreated" => "Studio-оос үүсгэх үед сонгосон",
        "StudioSelected" => "Studio дээр сонгосон",
        "StudioCloudPending" => "Studio дээр сонгосон · Cloud sync хүлээгдэж байна",
        "StudioCloudSelected" => "Studio дээр сонгосон · Cloud ERA баталгаажсан",
        "LegacyProject" => "Legacy төслөөс шилжсэн",
        _ => string.IsNullOrWhiteSpace(source) ? "эх үүсвэр тодорхойгүй" : source,
    };

    private static CompanyProfile MapCloudCompany(StudioCloudOrganization cloud)
    {
        var profile = new CompanyProfile
        {
            OrganizationId = cloud.OrganizationId,
            Name = cloud.LegalName,
            DisplayName = cloud.DisplayName,
            ShortName = cloud.ShortName,
            RegistrationNumber = cloud.RegistrationNumber,
            LegalEntityType = cloud.LegalEntityType,
            LegalForm = cloud.LegalForm,
            ActivityDirections = [.. (cloud.ActivityDirections ?? [])],
            RegisteredAtUtc = cloud.RegisteredAtUtc,
            OfficialRepresentativeName = cloud.OfficialRepresentativeName,
            RegistrySource = cloud.RegistrySource,
            RegistrySourceUrl = cloud.RegistrySourceUrl,
            RegistryCheckedAtUtc = cloud.RegistryCheckedAtUtc,
            OrganizationType = cloud.OrganizationType,
            Status = cloud.Status,
            VerificationStatus = cloud.VerificationStatus,
            RegisteredCity = cloud.RegisteredCity,
            Address = cloud.Address,
            PhoneNumbers = [.. cloud.PhoneNumbers],
            Phone = cloud.PhoneNumbers.FirstOrDefault() ?? "",
            Email = cloud.Email,
            WebSite = cloud.Website,
            LicenseScope = cloud.LicenseScope,
            LicenseNumber = cloud.LicenseNumber,
            DesignRepresentativeTitle = FirstCompanyValue(cloud.DesignRepresentativeTitle, cloud.DirectorTitle),
            DesignRepresentativeName = FirstCompanyValue(cloud.DesignRepresentativeName, cloud.DirectorName),
            DirectorTitle = FirstCompanyValue(cloud.DesignRepresentativeTitle, cloud.DirectorTitle),
            DirectorName = FirstCompanyValue(cloud.DesignRepresentativeName, cloud.DirectorName),
            LogoScale = cloud.LogoScale,
            LogoOffsetX = cloud.LogoOffsetX,
            LogoOffsetY = cloud.LogoOffsetY,
            UpdatedAtUtc = cloud.UpdatedAtUtc,
        };
        if (!string.IsNullOrWhiteSpace(profile.DesignRepresentativeName))
            profile.Signers.Add(new CompanySigner { Role = profile.DesignRepresentativeTitle, FullName = profile.DesignRepresentativeName });
        profile.Normalize();
        return profile;
    }

    private static StudioCloudOrganizationUpsertRequest ToCloudCompanyRequest(CompanyProfile profile) => new()
    {
        RegistryFieldsIncluded = true,
        LegalName = profile.Name,
        DisplayName = profile.DisplayName,
        ShortName = profile.ShortName,
        RegistrationNumber = profile.RegistrationNumber,
        LegalEntityType = profile.LegalEntityType,
        LegalForm = profile.LegalForm,
        ActivityDirections = [.. profile.ActivityDirections],
        RegisteredAtUtc = profile.RegisteredAtUtc,
        OfficialRepresentativeName = profile.OfficialRepresentativeName,
        OrganizationType = string.IsNullOrWhiteSpace(profile.OrganizationType) ? "DesignCompany" : profile.OrganizationType,
        RegisteredCity = profile.RegisteredCity,
        Address = profile.Address,
        PhoneNumbers = [.. profile.PhoneNumbers],
        Email = profile.Email,
        Website = profile.WebSite,
        LicenseScope = profile.LicenseScope,
        LicenseNumber = profile.LicenseNumber,
        DesignRepresentativeTitle = profile.DesignRepresentativeTitle,
        DesignRepresentativeName = profile.DesignRepresentativeName,
        DirectorTitle = profile.DesignRepresentativeTitle,
        DirectorName = profile.DesignRepresentativeName,
        LogoScale = profile.LogoScale,
        LogoOffsetX = profile.LogoOffsetX,
        LogoOffsetY = profile.LogoOffsetY,
    };

    private void OpenOrganizationRegistrySource()
    {
        string url = selectedCompanyEntry?.Profile.RegistrySourceUrl ?? "";
        if (string.IsNullOrWhiteSpace(url))
            url = "https://opendata.burtgel.gov.mn/les";
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            SetStatus("Улсын бүртгэлийн санг нээж чадсангүй: " + exception.Message);
        }
    }

    private static string RegistrySourceLabel(CompanyProfile profile)
    {
        string source = profile.RegistrySource.Equals("OfficialRegistry", StringComparison.OrdinalIgnoreCase)
            ? "Улсын бүртгэлийн албан ёсны эх сурвалж"
            : "Хэрэглэгчийн оруулсан мэдээлэл";
        return profile.RegistryCheckedAtUtc is null
            ? source
            : $"{source} · {profile.RegistryCheckedAtUtc:yyyy-MM-dd HH:mm}";
    }

    private static DateTimeOffset? ParseCompanyRegisteredDate(string text, DateTimeOffset? fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        return DateTimeOffset.TryParse(text.Trim(), out DateTimeOffset parsed)
            ? new DateTimeOffset(parsed.Year, parsed.Month, parsed.Day, 0, 0, 0, TimeSpan.Zero)
            : fallback;
    }

    private static string FirstCompanyValue(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "";

    private static string CompanySyncLabel(string status) => status switch
    {
        CompanySyncStatuses.Cloud => "Cloud",
        CompanySyncStatuses.PendingCreate => "Cloud үүсгэлт хүлээгдэж байна",
        CompanySyncStatuses.PendingUpdate => "Cloud шинэчлэлт хүлээгдэж байна",
        CompanySyncStatuses.ProjectSnapshot => "Төслийн snapshot",
        _ => status,
    };

    private sealed class CompanyListRow
    {
        public CompanyListRow(CompanyCatalogEntry entry) => Entry = entry;
        public CompanyCatalogEntry Entry { get; }
        public string Name => CompanyDisplayName(Entry.Profile);
        public string Access => Entry.CanManage ? "Удирдах" : "Харах";
    }
}
