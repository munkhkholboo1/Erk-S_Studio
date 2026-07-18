using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using Microsoft.Win32;

namespace ErkS.Studio;

/// <summary>Project-centric Erk-S Studio shell.</summary>
internal sealed partial class ShellView : IDisposable
{
    private enum StudioPage
    {
        Projects,
        Companies,
        Overview,
        Foundation,
        Sources,
        Albums,
        Reports,
        Archive,
    }

    private readonly AppState state = new();
    private readonly StudioAccountService account = new();
    private readonly StudioUpdateService productUpdates = new();
    private readonly Grid contentHost = new();
    private readonly StackPanel navPanel = new();
    private readonly Dictionary<StudioPage, Border> navItems = [];
    private readonly Dictionary<StudioPage, UIElement> pages = [];
    private readonly TextBlock statusText = new();
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private StudioPage activePage = StudioPage.Projects;
    private bool projectWorkspaceOpen;

    // Project catalog
    private readonly ListView projectsList = new() { MinHeight = 360 };
    private readonly TextBlock projectsSummaryText = new();
    private readonly TextBox projectSearchBox = new() { Width = 248, ToolTip = "Төслүүдээс хайх" };
    private readonly TextBlock projectSearchHint = new() { Text = "Төсөл хайх", IsHitTestVisible = false };
    private readonly Border projectsEmptyState = new() { Visibility = Visibility.Collapsed };
    private readonly TextBlock projectsEmptyTitle = new();
    private readonly TextBlock projectsEmptyMessage = new();
    private readonly PdfPageImageCache projectThumbnailImages = new();
    private CancellationTokenSource? projectThumbnailLoadCancellation;
    private IReadOnlyList<ProjectRow> projectRows = Array.Empty<ProjectRow>();
    private string projectRefreshNotice = "";
    private readonly Grid accountAvatarHost = new() { Width = 34, Height = 34 };
    private readonly Image accountAvatarImage = new() { Width = 32, Height = 32, Stretch = Stretch.UniformToFill, Visibility = Visibility.Collapsed };
    private readonly TextBlock accountAvatarInitials = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        Foreground = StudioTheme.AccentSoftBrush,
    };
    private readonly TextBlock accountStatusText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock accountLicenseText = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button accountButton = StudioWidgets.CreateGlyphButton("\uE77B", "Нэвтрэх");
    private bool accountInitialized;
    private bool accountAvatarLoading;
    private string loadedProfileImageKey = "";
    private string requestedProfileImageKey = "";
    private readonly Button productUpdateButton = StudioWidgets.CreateGlyphTextButton("\uE72C", "Шинэчлэлт", "Erk-S Studio шинэчлэлт шалгах");
    private StudioUpdateLatestResponse? availableProductUpdate;
    private bool productUpdateCheckInProgress;

    // Foundation: initiation basis
    private readonly TextBox projectNameBox = new();
    private readonly TextBox projectCodeBox = new();
    private readonly TextBox basisSourceBox = new();
    private readonly TextBox requestNumberBox = new();
    private readonly TextBox clientNameBox = new();
    private readonly TextBox clientEmailBox = new();
    private readonly TextBox siteAddressBox = new();
    private readonly TextBox landReferenceBox = new();
    private readonly TextBox basisSourceOrganizationBox = new();
    private readonly TextBox basisSummaryBox = MultilineBox();
    private readonly TextBlock cloudLinkText = new() { TextWrapping = TextWrapping.Wrap };

    // Foundation: ATD
    private readonly TextBox atdNumberBox = new();
    private readonly TextBox atdAuthorityBox = new();
    private readonly TextBox atdStatusBox = new();
    private readonly TextBox atdSummaryBox = MultilineBox();
    private readonly ListView atdParticipantsList = new() { MinHeight = 140, MaxHeight = 260 };

    // Foundation: assigned company snapshot
    private readonly TextBox companyDisplayNameBox = new();
    private readonly TextBox companyNameBox = new();
    private readonly TextBox companyRegistrationBox = new();
    private readonly TextBox companyOrganizationIdBox = new();
    private readonly TextBox companyShortNameBox = new();
    private readonly TextBox companyAddressBox = new();
    private readonly TextBox companyPhoneBox = new();
    private readonly TextBox companyEmailBox = new();
    private readonly TextBox companyWebSiteBox = new();
    private readonly TextBox companyLogoBox = new();
    private readonly TextBlock companyAssignmentText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock companyAssignmentPolicyText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button projectCompanyLibraryButton = StudioWidgets.CreateIconTextButton(
        "icon-company.svg",
        "Компанийн сан",
        "Төслийн зураг төслийн байгууллагын бүртгэлийг нээх");
    private readonly ListView participantsList = new() { MinHeight = 140, MaxHeight = 260 };

    // Overview / deliverables
    private readonly TextBlock projectOverviewText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock syncSummaryText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button syncButton = StudioWidgets.CreatePrimaryButton("Sync");
    private bool syncInProgress;
    private readonly ListView reportsList = new() { MinHeight = 240 };
    private readonly ListView archiveList = new() { MinHeight = 240 };

    // Album workspace fields shared with ShellView.Workspaces.cs
    private readonly TextBox albumTitleBox = new();
    private readonly CheckBox autoRebuildCheck = new()
    {
        Content = "Эх үүсвэр шинэчлэгдэхэд альбум автоматаар шинэчлэх",
        IsChecked = true,
    };
    private readonly TextBlock albumInfoText = new();
    private readonly DispatcherTimer autoRebuildTimer;
    private string? lastAlbumPath;

    public UIElement Root { get; }

    public ShellView()
    {
        autoRebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        autoRebuildTimer.Tick += (_, _) =>
        {
            autoRebuildTimer.Stop();
            if (autoRebuildCheck.IsChecked == true && state.HasOpenProject)
            {
                UpdateAlbum(silent: true);
            }
        };

        var rootBorder = new Border
        {
            Background = StudioTheme.WindowBackgroundBrush,
            Child = BuildShell(),
        };
        StudioTheme.ApplyToRoot(rootBorder);
        Root = rootBorder;

        state.Library.Changed += () => dispatcher.BeginInvoke(new Action(OnLibraryChanged));
        state.Intake.PackageProcessed += result => dispatcher.BeginInvoke(new Action(() => OnPackageProcessed(result)));
        state.Intake.IntakeError += message => dispatcher.BeginInvoke(new Action(() => SetStatus(message)));
        state.ProjectReplaced += () => dispatcher.BeginInvoke(new Action(() =>
        {
            if (state.HasOpenProject)
            {
                BindProjectToUi();
            }
        }));
        account.StateChanged += () => dispatcher.BeginInvoke(new Action(UpdateAccountUi));
        ((FrameworkElement)Root).Loaded += OnRootLoaded;
        UpdateAccountUi();
        SetStatus("Erk-S Studio бүртгэл болон лицензийг шалгаж байна...");
    }

    public void Dispose()
    {
        autoRebuildTimer.Stop();
        projectThumbnailLoadCancellation?.Cancel();
        projectThumbnailLoadCancellation?.Dispose();
        albumPdfViewer.Dispose();
        state.Dispose();
        account.Dispose();
        productUpdates.Dispose();
    }

    private UIElement BuildShell()
    {
        var root = new DockPanel();
        var status = StudioWidgets.CreateStatusBar(statusText);
        DockPanel.SetDock(status, Dock.Bottom);
        root.Children.Add(status);

        navPanel.Margin = new Thickness(12, 6, 12, 8);
        var railLayout = new DockPanel { Width = 216 };
        var brand = BuildBrandBlock();
        DockPanel.SetDock(brand, Dock.Top);
        railLayout.Children.Add(brand);
        var accountPanel = BuildAccountPanel();
        DockPanel.SetDock(accountPanel, Dock.Bottom);
        railLayout.Children.Add(accountPanel);
        railLayout.Children.Add(new ScrollViewer
        {
            Content = navPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        var rail = new Border
        {
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = railLayout,
        };
        DockPanel.SetDock(rail, Dock.Left);
        root.Children.Add(rail);

        pages[StudioPage.Projects] = BuildProjectsPage();
        pages[StudioPage.Companies] = BuildCompaniesPage();
        pages[StudioPage.Overview] = BuildOverviewPage();
        pages[StudioPage.Foundation] = BuildFoundationPage();
        pages[StudioPage.Sources] = BuildSourcesPage();
        pages[StudioPage.Albums] = BuildAlbumPage();
        pages[StudioPage.Reports] = BuildReportsPage();
        pages[StudioPage.Archive] = BuildArchivePage();

        root.Children.Add(contentHost);
        RebuildNavigation();
        SelectPage(StudioPage.Projects);
        return root;
    }

    private static UIElement BuildBrandBlock()
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(new Image
        {
            Source = SvgIconLoader.TryLoad(StudioWidgets.GetAssetPath("logo-erks.svg")),
            Width = 30,
            Height = 30,
            Margin = new Thickness(0, 0, 10, 0),
        });
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = "Erk-S Studio",
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
        });
        words.Children.Add(new TextBlock
        {
            Text = "CLOUD ERA",
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.MutedTextBrush,
        });
        stack.Children.Add(words);
        return new Border
        {
            Width = 216,
            Padding = new Thickness(18, 16, 14, 16),
            Margin = new Thickness(0, 0, 0, 8),
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = stack,
        };
    }

    private UIElement BuildAccountPanel()
    {
        accountStatusText.FontSize = 12.5;
        accountStatusText.FontWeight = FontWeights.SemiBold;
        accountStatusText.Foreground = StudioTheme.TextBrush;
        accountStatusText.TextTrimming = TextTrimming.CharacterEllipsis;
        accountStatusText.MaxWidth = 116;
        accountLicenseText.FontSize = 10.5;
        accountLicenseText.Foreground = StudioTheme.MutedTextBrush;
        accountLicenseText.TextTrimming = TextTrimming.CharacterEllipsis;
        accountLicenseText.MaxWidth = 116;
        accountButton.Margin = new Thickness(4, 0, 0, 0);
        accountButton.VerticalAlignment = VerticalAlignment.Center;
        accountButton.Click += async (_, _) => await ToggleAccountAsync();

        var details = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        details.Children.Add(accountStatusText);
        details.Children.Add(accountLicenseText);

        var row = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(accountButton, Dock.Right);
        row.Children.Add(accountButton);
        var avatar = BuildAccountAvatar();
        DockPanel.SetDock(avatar, Dock.Left);
        row.Children.Add(avatar);
        row.Children.Add(details);
        return new Border
        {
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(14, 12, 10, 12),
            Child = row,
        };
    }

    private async Task ToggleAccountAsync()
    {
        if (account.IsSignedIn)
        {
            try
            {
                if (state.HasOpenProject)
                    state.CloseProject();
                account.SignOut();
                projectWorkspaceOpen = false;
                RebuildNavigation();
                await RefreshProjectsAsync();
            }
            catch (Win32Exception exception)
            {
                SetStatus("Гарах үйлдэл дууссангүй: " + exception.Message);
            }
            return;
        }
        await EnsureSignedInAsync();
    }

    private void RebuildNavigation()
    {
        navPanel.Children.Clear();
        navItems.Clear();
        AddNavItem(StudioPage.Projects, "Төслүүд", "icon-project.svg");
        AddNavItem(StudioPage.Companies, "Компани", "icon-company.svg");
        if (!projectWorkspaceOpen || !state.HasOpenProject)
        {
            return;
        }

        navPanel.Children.Add(BuildProjectContextBlock());
        AddNavItem(StudioPage.Overview, "Ерөнхий", "icon-project.svg");
        AddNavItem(StudioPage.Foundation, "Суурь", "icon-project.svg");
        AddNavItem(StudioPage.Sources, "Эх үүсвэр", "icon-sources.svg");
        AddNavItem(StudioPage.Albums, "Альбум", "icon-album.svg");
        AddNavItem(StudioPage.Reports, "Тайлан", "icon-publish.svg");
        AddNavItem(StudioPage.Archive, "Архив", "icon-company.svg");
    }

    private UIElement BuildProjectContextBlock()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = state.Project.Code,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        stack.Children.Add(new TextBlock
        {
            Text = ProjectStageLabel(state.Project.Identity.StageName),
            FontSize = 10.5,
            Foreground = StudioTheme.MutedTextBrush,
        });
        return new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 8, 0, 8),
            Background = StudioTheme.PanelAltBrush,
            CornerRadius = new CornerRadius(StudioTheme.CornerRadius),
            Child = stack,
        };
    }

    private void AddNavItem(StudioPage page, string label, string iconAsset)
    {
        var stack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        stack.Children.Add(new Image
        {
            Source = SvgIconLoader.TryLoad(StudioWidgets.GetAssetPath(iconAsset)),
            Width = 19,
            Height = 19,
            Margin = new Thickness(0, 0, 11, 0),
        });
        stack.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 13,
            Foreground = StudioTheme.MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var item = new Border
        {
            MinHeight = 42,
            Padding = new Thickness(12, 8, 12, 8),
            Margin = new Thickness(0, 0, 0, 3),
            CornerRadius = new CornerRadius(StudioTheme.CornerRadius),
            Background = Brushes.Transparent,
            Child = stack,
            Cursor = System.Windows.Input.Cursors.Hand,
        };
        item.MouseLeftButtonUp += (_, _) => SelectPage(page);
        item.MouseEnter += (_, _) =>
        {
            if (activePage != page)
            {
                item.Background = StudioTheme.PanelAltBrush;
            }
        };
        item.MouseLeave += (_, _) =>
        {
            if (activePage != page)
            {
                item.Background = Brushes.Transparent;
            }
        };
        navItems[page] = item;
        navPanel.Children.Add(item);
    }

    private void SelectPage(StudioPage page)
    {
        var previousPage = activePage;
        if (page == StudioPage.Projects && projectWorkspaceOpen)
        {
            projectWorkspaceOpen = false;
            state.CloseProject();
            RefreshProjects();
            RebuildNavigation();
        }
        if (page is not StudioPage.Projects and not StudioPage.Companies && !state.HasOpenProject)
        {
            return;
        }

        activePage = page;
        contentHost.Children.Clear();
        contentHost.Children.Add(pages[page]);
        foreach (var (candidate, item) in navItems)
        {
            var isActive = candidate == page;
            item.Background = isActive
                ? new SolidColorBrush(Color.FromArgb(70, StudioTheme.AccentColor.R, StudioTheme.AccentColor.G, StudioTheme.AccentColor.B))
                : Brushes.Transparent;
            if (item.Child is StackPanel stack && stack.Children[1] is TextBlock label)
            {
                label.Foreground = isActive ? StudioTheme.TextBrush : StudioTheme.MutedTextBrush;
                label.FontWeight = isActive ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        if (page == StudioPage.Albums)
        {
            RefreshAlbumPagePreview();
            if (previousPage != StudioPage.Albums)
            {
                dispatcher.BeginInvoke(
                    new Action(() => UpdateAlbum(silent: true)),
                    DispatcherPriority.Background);
            }
        }
        else if (page == StudioPage.Companies)
        {
            _ = RefreshCompaniesAsync();
        }
    }

    private UIElement BuildProjectsPage()
    {
        var root = new DockPanel
        {
            Margin = new Thickness(30, 26, 30, 22),
            Background = StudioTheme.WindowBackgroundBrush,
        };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 28) };
        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var create = StudioWidgets.CreateGlyphTextButton("\uE710", "Шинэ төсөл", primary: true);
        create.Click += async (_, _) => await CreateProjectAsync();
        var openFile = StudioWidgets.CreateGlyphTextButton("\uE8E5", "Файлаас нээх");
        openFile.Click += async (_, _) =>
        {
            if (await EnsureSignedInAsync())
                OpenProjectFromFile();
        };
        var refresh = StudioWidgets.CreateGlyphButton("\uE72C", "Төслийн жагсаалтыг шинэчлэх");
        refresh.Click += async (_, _) => await RefreshProjectsAsync();
        notificationsButton.Click += async (_, _) => await ShowNotificationsAsync();
        productUpdateButton.Click += async (_, _) => await CheckForProductUpdateAsync(interactive: true);
        actions.Children.Add(create);
        actions.Children.Add(openFile);
        actions.Children.Add(notificationsButton);
        actions.Children.Add(productUpdateButton);
        actions.Children.Add(refresh);
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);
        var title = new StackPanel();
        title.Children.Add(new TextBlock
        {
            Text = "Төслүүд",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
        });
        title.Children.Add(new TextBlock
        {
            Text = "Cloud ERA болон энэ төхөөрөмж дээрх ажлын орчин",
            FontSize = 12.5,
            Foreground = StudioTheme.MutedTextBrush,
            Margin = new Thickness(0, 5, 0, 0),
        });
        header.Children.Add(title);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var section = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var search = BuildProjectSearchBox();
        DockPanel.SetDock(search, Dock.Right);
        section.Children.Add(search);
        var sectionTitle = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        sectionTitle.Children.Add(new TextBlock
        {
            Text = "Сүүлийн төслүүд",
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        projectsSummaryText.Foreground = StudioTheme.MutedTextBrush;
        projectsSummaryText.FontSize = 12;
        projectsSummaryText.Margin = new Thickness(10, 1, 0, 0);
        projectsSummaryText.VerticalAlignment = VerticalAlignment.Center;
        sectionTitle.Children.Add(projectsSummaryText);
        section.Children.Add(sectionTitle);
        DockPanel.SetDock(section, Dock.Top);
        root.Children.Add(section);

        projectsList.Background = Brushes.Transparent;
        projectsList.BorderThickness = new Thickness(0);
        projectsList.Padding = new Thickness(0);
        projectsList.HorizontalContentAlignment = HorizontalAlignment.Left;
        projectsList.ItemsPanel = CreateProjectItemsPanel();
        projectsList.ItemContainerStyle = CreateProjectCardItemStyle();
        projectsList.ItemTemplate = CreateProjectCardTemplate();
        ScrollViewer.SetHorizontalScrollBarVisibility(projectsList, ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(projectsList, ScrollBarVisibility.Auto);
        projectsList.MouseDoubleClick += (_, _) => OpenSelectedProject();
        projectsList.KeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Enter)
            {
                return;
            }

            args.Handled = true;
            OpenSelectedProject();
        };
        var projectContent = new Grid();
        projectContent.Children.Add(projectsList);
        BuildProjectsEmptyState();
        projectContent.Children.Add(projectsEmptyState);
        root.Children.Add(projectContent);
        return root;
    }

    private UIElement BuildProjectSearchBox()
    {
        projectSearchBox.Padding = new Thickness(32, 5, 10, 5);
        projectSearchBox.TextChanged += (_, _) =>
        {
            projectSearchHint.Visibility = string.IsNullOrWhiteSpace(projectSearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
            ApplyProjectFilter();
        };
        projectSearchHint.Foreground = StudioTheme.FaintTextBrush;
        projectSearchHint.VerticalAlignment = VerticalAlignment.Center;
        projectSearchHint.Margin = new Thickness(32, 0, 8, 0);

        var search = new Grid { Width = 248 };
        search.Children.Add(projectSearchBox);
        search.Children.Add(new TextBlock
        {
            Text = "\uE721",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            Foreground = StudioTheme.MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(11, 0, 0, 0),
            IsHitTestVisible = false,
        });
        search.Children.Add(projectSearchHint);
        return search;
    }

    private void BuildProjectsEmptyState()
    {
        var content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 360,
        };
        content.Children.Add(new Image
        {
            Source = SvgIconLoader.TryLoad(StudioWidgets.GetAssetPath("logo-erks.svg")),
            Width = 48,
            Height = 48,
            Opacity = 0.28,
            Margin = new Thickness(0, 0, 0, 14),
        });
        projectsEmptyTitle.FontSize = 15;
        projectsEmptyTitle.FontWeight = FontWeights.SemiBold;
        projectsEmptyTitle.Foreground = StudioTheme.TextBrush;
        projectsEmptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
        content.Children.Add(projectsEmptyTitle);
        projectsEmptyMessage.FontSize = 12;
        projectsEmptyMessage.Foreground = StudioTheme.MutedTextBrush;
        projectsEmptyMessage.TextAlignment = TextAlignment.Center;
        projectsEmptyMessage.TextWrapping = TextWrapping.Wrap;
        projectsEmptyMessage.Margin = new Thickness(0, 6, 0, 0);
        content.Children.Add(projectsEmptyMessage);
        projectsEmptyState.Child = content;
    }

    private static ItemsPanelTemplate CreateProjectItemsPanel()
    {
        var template = new ItemsPanelTemplate();
        var panel = new FrameworkElementFactory(typeof(WrapPanel));
        panel.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        template.VisualTree = panel;
        return template;
    }

    private static Style CreateProjectCardItemStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(FrameworkElement.WidthProperty, 292d));
        style.Setters.Add(new Setter(FrameworkElement.HeightProperty, 282d));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 16, 16)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, StudioTheme.PanelBrush));
        style.Setters.Add(new Setter(Control.BorderBrushProperty, StudioTheme.BorderBrush));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));

        var template = new ControlTemplate(typeof(ListViewItem));
        var border = new FrameworkElementFactory(typeof(Border), "CardBorder");
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Control.Background))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
        });
        border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding(nameof(Control.BorderBrush))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
        });
        border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding(nameof(Control.BorderThickness))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
        });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding(nameof(ContentControl.Content))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
        });
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new System.Windows.Data.Binding(nameof(ContentControl.ContentTemplate))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
        });
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BorderBrushProperty, StudioTheme.BorderHoverBrush, "CardBorder"));
        hover.Setters.Add(new Setter(Border.BackgroundProperty, StudioTheme.PanelAltBrush, "CardBorder"));
        template.Triggers.Add(hover);
        var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Border.BorderBrushProperty, StudioTheme.AccentBrush, "CardBorder"));
        template.Triggers.Add(selected);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static DataTemplate CreateProjectCardTemplate()
    {
        var template = new DataTemplate(typeof(ProjectRow));
        var root = new FrameworkElementFactory(typeof(StackPanel));

        var preview = new FrameworkElementFactory(typeof(Border));
        preview.SetValue(FrameworkElement.HeightProperty, 158d);
        preview.SetValue(Border.BackgroundProperty, StudioTheme.InputBrush);
        preview.SetValue(Border.CornerRadiusProperty, new CornerRadius(7, 7, 0, 0));
        preview.SetValue(Border.ClipToBoundsProperty, true);
        var previewGrid = new FrameworkElementFactory(typeof(Grid));
        var placeholder = new FrameworkElementFactory(typeof(Image));
        placeholder.SetValue(Image.SourceProperty, SvgIconLoader.TryLoad(StudioWidgets.GetAssetPath("logo-erks.svg")));
        placeholder.SetValue(FrameworkElement.WidthProperty, 54d);
        placeholder.SetValue(FrameworkElement.HeightProperty, 54d);
        placeholder.SetValue(UIElement.OpacityProperty, 0.16d);
        placeholder.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        placeholder.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        previewGrid.AppendChild(placeholder);
        var thumbnail = new FrameworkElementFactory(typeof(Image));
        thumbnail.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(ProjectRow.ThumbnailSource)));
        thumbnail.SetValue(Image.StretchProperty, Stretch.Uniform);
        thumbnail.SetValue(FrameworkElement.MarginProperty, new Thickness(10));
        previewGrid.AppendChild(thumbnail);
        var sourceBadge = new FrameworkElementFactory(typeof(Border));
        sourceBadge.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(210, 24, 27, 32)));
        sourceBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        sourceBadge.SetValue(Border.PaddingProperty, new Thickness(7, 3, 7, 3));
        sourceBadge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        sourceBadge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        sourceBadge.SetValue(FrameworkElement.MarginProperty, new Thickness(10));
        var sourceText = new FrameworkElementFactory(typeof(TextBlock));
        sourceText.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ProjectRow.PreviewLabel)));
        sourceText.SetValue(TextBlock.FontSizeProperty, 9.5d);
        sourceText.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        sourceText.SetValue(TextBlock.ForegroundProperty, StudioTheme.AccentSoftBrush);
        sourceBadge.AppendChild(sourceText);
        previewGrid.AppendChild(sourceBadge);
        preview.AppendChild(previewGrid);
        root.AppendChild(preview);

        var details = new FrameworkElementFactory(typeof(StackPanel));
        details.SetValue(FrameworkElement.MarginProperty, new Thickness(14, 12, 14, 14));
        var code = new FrameworkElementFactory(typeof(TextBlock));
        code.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ProjectRow.Code)));
        code.SetValue(TextBlock.FontSizeProperty, 10.5d);
        code.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        code.SetValue(TextBlock.ForegroundProperty, StudioTheme.AccentSoftBrush);
        code.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 5));
        details.AppendChild(code);
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ProjectRow.Name)));
        name.SetValue(TextBlock.FontSizeProperty, 14d);
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.ForegroundProperty, StudioTheme.TextBrush);
        name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetValue(FrameworkElement.HeightProperty, 39d);
        details.AppendChild(name);
        var company = new FrameworkElementFactory(typeof(TextBlock));
        company.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ProjectRow.CompanyLabel)));
        company.SetValue(TextBlock.FontSizeProperty, 11.5d);
        company.SetValue(TextBlock.ForegroundProperty, StudioTheme.MutedTextBrush);
        company.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        company.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 0));
        details.AppendChild(company);

        var footer = new FrameworkElementFactory(typeof(DockPanel));
        footer.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 11, 0, 0));
        var connection = new FrameworkElementFactory(typeof(TextBlock));
        connection.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ProjectRow.Connection)));
        connection.SetValue(TextBlock.FontSizeProperty, 10.5d);
        connection.SetValue(TextBlock.ForegroundProperty, StudioTheme.SuccessBrush);
        connection.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        connection.SetValue(DockPanel.DockProperty, Dock.Right);
        footer.AppendChild(connection);
        var stageBadge = new FrameworkElementFactory(typeof(Border));
        stageBadge.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(38, StudioTheme.AccentColor.R, StudioTheme.AccentColor.G, StudioTheme.AccentColor.B)));
        stageBadge.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        stageBadge.SetValue(Border.PaddingProperty, new Thickness(7, 3, 7, 3));
        stageBadge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        var stage = new FrameworkElementFactory(typeof(TextBlock));
        stage.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ProjectRow.Stage)));
        stage.SetValue(TextBlock.FontSizeProperty, 10.5d);
        stage.SetValue(TextBlock.ForegroundProperty, StudioTheme.AccentSoftBrush);
        stageBadge.AppendChild(stage);
        footer.AppendChild(stageBadge);
        details.AppendChild(footer);
        root.AppendChild(details);
        template.VisualTree = root;
        return template;
    }

    private async void OnRootLoaded(object sender, RoutedEventArgs args)
    {
        if (accountInitialized)
            return;
        accountInitialized = true;
        await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ApplicationIdle);
        _ = CheckForProductUpdateAsync(interactive: false);
        bool restored = await account.TryRestoreAsync();
        UpdateAccountUi();
        if (restored)
        {
            await RefreshProjectsAsync();
            SetStatus("Cloud ERA төслүүд шинэчлэгдлээ.");
            return;
        }

        if (string.IsNullOrWhiteSpace(account.LastError))
        {
            await EnsureSignedInAsync();
            return;
        }

        await RefreshProjectsAsync();
        SetStatus("Studio session сэргээж чадсангүй: " + account.LastError + " Нэвтрэх товчоор лицензээ дахин шалгана уу.");
    }

    private async Task CheckForProductUpdateAsync(bool interactive)
    {
        if (productUpdateCheckInProgress)
            return;

        if (interactive && availableProductUpdate?.IsUpdateAvailable == true)
        {
            ShowProductUpdateDialog(availableProductUpdate);
            return;
        }

        productUpdateCheckInProgress = true;
        productUpdateButton.IsEnabled = false;
        SetProductUpdateButtonLabel("Шалгаж байна");
        try
        {
            StudioUpdateLatestResponse result = await productUpdates.CheckAsync();
            availableProductUpdate = result.IsUpdateAvailable ? result : null;
            if (result.IsUpdateAvailable)
            {
                SetProductUpdateButtonLabel(string.IsNullOrWhiteSpace(result.Version) ? "Шинэ хувилбар" : result.Version);
                productUpdateButton.ToolTip = $"Erk-S Studio {result.Version} татаж суулгах";
                SetStatus($"Erk-S Studio {result.Version} шинэ хувилбар бэлэн байна.");
                if (interactive)
                    ShowProductUpdateDialog(result);
                return;
            }

            SetProductUpdateButtonLabel("Шинэчлэлт");
            productUpdateButton.ToolTip = "Erk-S Studio шинэчлэлт шалгах";
            if (interactive)
            {
                MessageBox.Show(
                    Window.GetWindow(Root),
                    $"Erk-S Studio {StudioReleaseInfo.DisplayVersion} хамгийн сүүлийн хувилбар байна.",
                    "Шинэчлэлт",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            SetProductUpdateButtonLabel("Шинэчлэлт");
            productUpdateButton.ToolTip = "Шинэчлэлт шалгах үед алдаа гарлаа. Дахин оролдох";
            if (interactive)
            {
                MessageBox.Show(
                    Window.GetWindow(Root),
                    "Шинэчлэлт шалгаж чадсангүй. Интернет холболтоо шалгаад дахин оролдоно уу.\n\n" + exception.Message,
                    "Erk-S Studio шинэчлэлт",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        finally
        {
            productUpdateCheckInProgress = false;
            productUpdateButton.IsEnabled = true;
        }
    }

    private void ShowProductUpdateDialog(StudioUpdateLatestResponse update)
    {
        var dialog = new StudioUpdateDialog(productUpdates, update)
        {
            Owner = Window.GetWindow(Root),
        };
        dialog.ShowDialog();
    }

    private void SetProductUpdateButtonLabel(string label)
    {
        if (productUpdateButton.Content is StackPanel stack &&
            stack.Children.Count > 1 &&
            stack.Children[1] is TextBlock text)
        {
            text.Text = label;
        }
    }

    private void RefreshProjects()
    {
        if (string.IsNullOrWhiteSpace(loadedProfileImageKey))
        {
            requestedProfileImageKey = "";
            UpdateAccountUi();
        }
        _ = RefreshProjectsAsync();
    }

    private async Task RefreshProjectsAsync()
    {
        if (!account.IsSignedIn)
        {
            projectRows = Array.Empty<ProjectRow>();
            projectRefreshNotice = "";
            await RefreshNotificationsAsync();
            ApplyProjectFilter();
            return;
        }

        await RefreshNotificationsAsync();
        RefreshLocalProjectCompanySnapshotsFromCache();
        List<ProjectCatalogItem> localProjects = new LocalProjectCatalog().ListProjects().ToList();
        var rows = new List<ProjectRow>();
        string cloudError = "";
        try
        {
            IReadOnlyList<StudioCloudProjectSummary> cloudProjects = await account.ListProjectsAsync();
            var matchedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (StudioCloudProjectSummary cloud in cloudProjects)
            {
                ProjectCatalogItem? local = localProjects.FirstOrDefault(item =>
                    !string.IsNullOrWhiteSpace(cloud.ProjectId) &&
                    item.ProjectId.Equals(cloud.ProjectId, StringComparison.OrdinalIgnoreCase));
                if (local is not null)
                    matchedPaths.Add(local.ProjectPath);
                string initiatorType = string.IsNullOrWhiteSpace(cloud.PlanningAuthorityName)
                    ? ProjectInitiatorTypes.DesignOrganization
                    : ProjectInitiatorTypes.GovernmentAuthority;
                string initiatorOrganization = string.IsNullOrWhiteSpace(cloud.PlanningAuthorityName)
                    ? cloud.DesignOrganizationName
                    : cloud.PlanningAuthorityName;
                string designOrganization = local is not null && !string.IsNullOrWhiteSpace(local.DesignOrganization)
                    ? local.DesignOrganization
                    : cloud.DesignOrganizationName;
                rows.Add(new ProjectRow(
                    cloud.ProjectCode,
                    cloud.Name,
                    ProjectStageLabel(cloud.CurrentStage),
                    ProjectCreatorLabel(initiatorType, initiatorOrganization),
                    designOrganization,
                    local is null ? "Cloud · Mirror үүсээгүй" : "Cloud · Linked",
                    local?.ProjectPath ?? "",
                    local?.IsLegacyProject ?? false,
                    cloud.ProjectId,
                    local is null));
            }

            rows.AddRange(localProjects
                .Where(item => !matchedPaths.Contains(item.ProjectPath))
                .Select(ToProjectRow));
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            cloudError = exception.Message;
            rows.AddRange(localProjects.Select(ToProjectRow));
            SetStatus("Cloud ERA төслийн жагсаалт шинэчлэгдсэнгүй: " + cloudError);
        }

        rows = rows
            .OrderByDescending(item => item.IsCloudOnly)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        projectRows = rows;
        projectRefreshNotice = string.IsNullOrWhiteSpace(cloudError) ? "" : "Cloud түр холбогдсонгүй";
        projectsSummaryText.ToolTip = LocalProjectCatalog.DefaultRoot;
        ApplyProjectFilter();
        StartProjectThumbnailLoading(rows);
    }

    private void ApplyProjectFilter()
    {
        string query = projectSearchBox.Text.Trim();
        IReadOnlyList<ProjectRow> visibleRows = string.IsNullOrWhiteSpace(query)
            ? projectRows
            : projectRows.Where(item =>
                    item.Code.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    item.Stage.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    item.Company.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    item.Creator.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();
        projectsList.ItemsSource = visibleRows;

        if (!account.IsSignedIn)
        {
            projectsSummaryText.Text = "Нэвтрээгүй";
            projectsEmptyTitle.Text = "Cloud ERA бүртгэлээр нэвтэрнэ үү";
            projectsEmptyMessage.Text = "Лицензтэй бүртгэлээр нэвтэрсний дараа cloud болон локал төслүүд энд харагдана.";
        }
        else if (projectRows.Count == 0)
        {
            projectsSummaryText.Text = "0 төсөл";
            projectsEmptyTitle.Text = "Төсөл одоогоор алга";
            projectsEmptyMessage.Text = "Шинэ төсөл үүсгэх эсвэл өмнөх төслийн файлыг нээнэ үү.";
        }
        else if (visibleRows.Count == 0)
        {
            projectsSummaryText.Text = $"0/{projectRows.Count} төсөл";
            projectsEmptyTitle.Text = "Хайлтад тохирох төсөл олдсонгүй";
            projectsEmptyMessage.Text = "Код, төслийн нэр, үе шат эсвэл байгууллагын нэрээр дахин хайна уу.";
        }
        else
        {
            projectsSummaryText.Text = string.IsNullOrWhiteSpace(query)
                ? $"{projectRows.Count} төсөл"
                : $"{visibleRows.Count}/{projectRows.Count} төсөл";
            if (!string.IsNullOrWhiteSpace(projectRefreshNotice))
                projectsSummaryText.Text += " · " + projectRefreshNotice;
        }
        projectsEmptyState.Visibility = visibleRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void StartProjectThumbnailLoading(IReadOnlyList<ProjectRow> rows)
    {
        projectThumbnailLoadCancellation?.Cancel();
        projectThumbnailLoadCancellation?.Dispose();
        projectThumbnailLoadCancellation = null;
        if (rows.All(item => string.IsNullOrWhiteSpace(item.Path)))
            return;

        var cancellation = new CancellationTokenSource();
        projectThumbnailLoadCancellation = cancellation;
        _ = LoadProjectThumbnailsAsync(rows, cancellation.Token);
    }

    private async Task LoadProjectThumbnailsAsync(IReadOnlyList<ProjectRow> rows, CancellationToken cancellationToken)
    {
        foreach (ProjectRow row in rows)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? pdfPath = FindProjectPreviewPdf(row);
                if (pdfPath is null)
                    continue;
                BitmapSource? thumbnail = await projectThumbnailImages.GetPageAsync(
                    pdfPath,
                    pageNumber: 1,
                    pixelWidth: 640,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                row.SetThumbnail(thumbnail);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // The card keeps its branded placeholder when no readable album preview is available.
            }
        }
    }

    private static string? FindProjectPreviewPdf(ProjectRow row)
    {
        if (string.IsNullOrWhiteSpace(row.Path))
            return null;
        string? projectFolder = Path.GetDirectoryName(row.Path);
        if (string.IsNullOrWhiteSpace(projectFolder))
            return null;
        string albumsFolder = Path.Combine(projectFolder, ProjectWorkspace.DefaultOutputRelativePath);
        if (!Directory.Exists(albumsFolder))
            return null;
        return Directory.EnumerateFiles(albumsFolder, "*.pdf", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private async Task CreateProjectAsync()
    {
        if (!await EnsureSignedInAsync())
            return;

        IReadOnlyList<StudioCloudOrganization> organizations;
        StudioProjectCreationGrantListResponse creationGrants;
        try
        {
            SetStatus("Бүртгэлд хамаарах байгууллагуудыг уншиж байна...");
            organizations = await account.ListOrganizationsAsync();
            creationGrants = await account.ListProjectCreationGrantsAsync();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            SetStatus("Байгууллагын бүртгэл уншигдсангүй: " + exception.Message);
            return;
        }

        List<StudioProjectCreationGrant> activeGrants = creationGrants.Received
            .Where(item =>
                item.Status.Equals("Active", StringComparison.OrdinalIgnoreCase) &&
                item.ExpiresAtUtc > DateTimeOffset.UtcNow)
            .ToList();
        if (organizations.Count == 0 && activeGrants.Count == 0)
        {
            SelectPage(StudioPage.Companies);
            SetStatus("Төсөл үүсгэхийн өмнө Компани хэсэгт байгууллагын бүртгэл үүсгэж эсвэл байгууллагын эрх авна уу.");
            return;
        }

        var dialog = new NewProjectDialog(organizations, activeGrants) { Owner = Window.GetWindow(Root) };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            ProjectCreationRequest request = AttachAccountIdentity(dialog.CreationRequest);
            StudioCloudOrganization? selectedOrganization = dialog.SelectedOrganization;
            StudioProjectCreationGrant? selectedGrant = dialog.SelectedCreationGrant;
            if (selectedOrganization is null && selectedGrant is null)
                throw new InvalidOperationException("Төсөл үүсгэх байгууллага эсвэл эрх сонгогдоогүй байна.");
            StudioRelationshipAction relationshipAction = selectedGrant is null
                ? StudioRelationshipAction.CreateProjectForClient
                : StudioRelationshipAction.RedeemProjectCreationGrant;
            string relationshipCounterparty = selectedGrant is null
                ? string.IsNullOrWhiteSpace(request.ClientName) ? "Захиалагч" : request.ClientName
                : $"{selectedGrant.OrganizationName} / {request.ClientName}";
            if (!StudioRelationshipBoundary.Confirm(
                    Window.GetWindow(Root),
                    relationshipAction,
                    relationshipCounterparty))
            {
                return;
            }
            string localPath = Path.Combine(
                ProjectWorkspacePaths.DefaultRoot,
                SafePathSegment(request.Code),
                ProjectWorkspace.DefaultFileName);
            if (File.Exists(localPath))
                throw new InvalidOperationException($"Ижил кодтой локал төсөл байна: {localPath}");

            SetStatus("Cloud ERA дээр төсөл үүсгэж байна...");
            var cloudRequest = new StudioCloudProjectCreateRequest
            {
                ProjectCode = request.Code,
                Name = request.Name,
                Location = request.SiteAddress,
                Description = request.Description,
                ClientName = request.ClientName,
                ClientEmail = request.ClientEmail,
                InitiatorType = request.InitiatorType,
                InitiatorOrganizationId = request.InitiatorOrganizationId,
                InitiatorOrganizationName = request.InitiatorOrganizationName,
            };
            StudioCloudProjectDetail cloud = selectedGrant is null
                ? await account.CreateProjectAsync(cloudRequest)
                : await account.CreateProjectFromGrantAsync(selectedGrant.GrantId, cloudRequest);
            state.NewProject(request);
            state.LinkCurrentProjectToCloud(cloud, account.Current!.ServerUrl, request);
            await ApplyCloudProjectRenderProfileAsync(cloud);
            if (selectedGrant is null &&
                selectedOrganization is not null &&
                request.InitiatorType.Equals(ProjectInitiatorTypes.DesignOrganization, StringComparison.OrdinalIgnoreCase))
                ApplyCompanyToOpenProject(MapCloudCompany(selectedOrganization), rebuildAlbum: false);
            EnterProjectWorkspace(StudioPage.Foundation);
            SetStatus($"Cloud ERA төсөл болон локал mirror үүслээ: {state.ProjectPath}");
        }
        catch (Exception exception)
        {
            SetStatus($"Төсөл үүсгэхэд алдаа: {exception.Message}");
        }
    }

    private void OpenProjectFromFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"Erk-S Studio төсөл (*{ProjectWorkspace.FileExtension};*{AlbumProject.FileExtension})|*{ProjectWorkspace.FileExtension};*{AlbumProject.FileExtension}",
        };
        if (dialog.ShowDialog() == true)
        {
            OpenProject(dialog.FileName);
        }
    }

    private async void OpenSelectedProject()
    {
        if (!await EnsureSignedInAsync())
            return;
        if (projectsList.SelectedItem is not ProjectRow row)
        {
            SetStatus("Нээх төслөө сонгоно уу.");
            return;
        }
        if (row.IsCloudOnly)
        {
            await OpenCloudProjectAsync(row);
            return;
        }
        OpenProject(row.Path);
    }

    private async Task OpenCloudProjectAsync(ProjectRow row)
    {
        try
        {
            SetStatus($"{row.Code} локал mirror үүсгэж байна...");
            StudioCloudProjectDetail cloud = await account.GetProjectAsync(row.ServerProjectId);
            string folderCode = cloud.Project.ProjectCode;
            string expectedPath = Path.Combine(
                ProjectWorkspacePaths.DefaultRoot,
                SafePathSegment(folderCode),
                ProjectWorkspace.DefaultFileName);
            if (File.Exists(expectedPath))
                folderCode += "-" + cloud.Project.ProjectId[..Math.Min(8, cloud.Project.ProjectId.Length)];
            string clientEmail = cloud.Participants
                .FirstOrDefault(item => item.Roles.Contains("Client", StringComparer.OrdinalIgnoreCase))?.AccountEmail ?? "";
            string initiatorType = string.IsNullOrWhiteSpace(cloud.Project.PlanningAuthorityName)
                ? ProjectInitiatorTypes.DesignOrganization
                : ProjectInitiatorTypes.GovernmentAuthority;
            string initiatorOrganization = string.IsNullOrWhiteSpace(cloud.Project.PlanningAuthorityName)
                ? cloud.Project.DesignOrganizationName
                : cloud.Project.PlanningAuthorityName;
            state.NewProject(new ProjectCreationRequest
            {
                Code = folderCode,
                Name = cloud.Project.Name,
                Description = cloud.ProjectInformation.BuildingPurpose,
                Channel = ProjectCreationChannels.Server,
                InitiatorType = initiatorType,
                InitiatorOrganizationName = initiatorOrganization,
                InitiatorUserId = account.Current!.Email,
                InitiatorDisplayName = AccountActorName(account.Current),
                ClientName = cloud.Project.ClientName,
                ClientEmail = clientEmail,
                SiteAddress = cloud.ProjectInformation.Location,
            });
            state.LinkCurrentProjectToCloud(cloud, account.Current!.ServerUrl);
            await ApplyCloudProjectRenderProfileAsync(cloud);
            EnterProjectWorkspace();
            SetStatus($"Cloud ERA төслийн локал mirror нээгдлээ: {state.ProjectPath}");
        }
        catch (Exception exception)
        {
            SetStatus("Cloud ERA төсөл нээхэд алдаа: " + exception.Message);
        }
    }

    private async Task<bool> EnsureSignedInAsync()
    {
        if (account.IsSignedIn)
            return true;
        var dialog = new StudioLoginDialog(account) { Owner = Window.GetWindow(Root) };
        if (dialog.ShowDialog() != true)
        {
            UpdateAccountUi();
            await RefreshProjectsAsync();
            SetStatus("Төсөл нээхийн тулд Erk-S Studio лицензтэй бүртгэлээр нэвтэрнэ үү.");
            return false;
        }
        UpdateAccountUi();
        await RefreshProjectsAsync();
        SetStatus("Cloud ERA бүртгэлээр нэвтэрлээ.");
        return true;
    }

    private void UpdateAccountUi()
    {
        StudioAccountSession? session = account.Current;
        string displayName = session is null ? "" : AccountDisplayName(session);
        accountStatusText.Text = session is null
            ? "Нэвтрээгүй"
            : displayName;
        accountLicenseText.Text = session is null
            ? "Cloud ERA"
            : string.IsNullOrWhiteSpace(session.LicenseType) ? "Cloud ERA" : session.LicenseType;
        accountStatusText.ToolTip = session is null ? null : "Cloud ERA бүртгэл";
        accountStatusText.Foreground = StudioTheme.TextBrush;
        accountLicenseText.Foreground = session is null ? StudioTheme.WarningBrush : StudioTheme.SuccessBrush;
        accountButton.Content = CreateAccountActionGlyph(session is null ? "\uE77B" : "\uF3B1");
        accountButton.ToolTip = session is null ? "Cloud ERA бүртгэлээр нэвтрэх" : "Бүртгэлээс гарах";
        accountAvatarInitials.Text = session is null ? "" : Initials(displayName);
        if (session is null)
        {
            accountAvatarImage.Source = null;
            accountAvatarImage.Visibility = Visibility.Collapsed;
            loadedProfileImageKey = "";
            requestedProfileImageKey = "";
        }
        else
        {
            string profileImageKey = ProfileImageKey(session);
            if (!string.IsNullOrWhiteSpace(loadedProfileImageKey) &&
                !profileImageKey.Equals(loadedProfileImageKey, StringComparison.Ordinal))
            {
                accountAvatarImage.Source = null;
                accountAvatarImage.Visibility = Visibility.Collapsed;
                loadedProfileImageKey = "";
            }
            if (!accountAvatarLoading &&
                !profileImageKey.Equals(loadedProfileImageKey, StringComparison.Ordinal) &&
                !profileImageKey.Equals(requestedProfileImageKey, StringComparison.Ordinal))
            {
                requestedProfileImageKey = profileImageKey;
                _ = LoadAccountAvatarAsync(session, profileImageKey);
            }
        }
        RefreshSyncUi();
    }

    private UIElement BuildAccountAvatar()
    {
        if (accountAvatarHost.Children.Count == 0)
        {
            accountAvatarHost.Children.Add(new System.Windows.Shapes.Ellipse
            {
                Width = 32,
                Height = 32,
                Fill = StudioTheme.PanelAltBrush,
                Stroke = StudioTheme.BorderBrush,
                StrokeThickness = 1,
            });
            accountAvatarHost.Children.Add(accountAvatarInitials);
            accountAvatarImage.Clip = new EllipseGeometry(new Rect(0, 0, 32, 32));
            accountAvatarHost.Children.Add(accountAvatarImage);
        }
        return accountAvatarHost;
    }

    private async Task LoadAccountAvatarAsync(StudioAccountSession requestedSession, string requestedProfileImageKeyValue)
    {
        accountAvatarLoading = true;
        try
        {
            byte[]? bytes = await account.GetProfileImageAsync();
            StudioAccountSession? currentSession = account.Current;
            if (bytes is null || currentSession is null ||
                currentSession.Email != requestedSession.Email ||
                !ProfileImageKey(currentSession).Equals(requestedProfileImageKeyValue, StringComparison.Ordinal))
            {
                return;
            }
            using var stream = new MemoryStream(bytes, writable: false);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            accountAvatarImage.Source = bitmap;
            accountAvatarImage.Visibility = Visibility.Visible;
            loadedProfileImageKey = requestedProfileImageKeyValue;
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or NotSupportedException)
        {
            SetStatus("Profile зураг ачаалагдсангүй: " + exception.Message);
        }
        finally
        {
            accountAvatarLoading = false;
        }
    }

    private static string Initials(string displayName)
    {
        string[] words = (displayName ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return "ES";
        return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    private static TextBlock CreateAccountActionGlyph(string glyph) => new()
    {
        Text = glyph,
        FontFamily = new FontFamily("Segoe MDL2 Assets"),
        FontSize = 14,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static string AccountDisplayName(StudioAccountSession session)
    {
        string displayName = session.DisplayName.Trim();
        return string.IsNullOrWhiteSpace(displayName) ||
               displayName.Equals(session.Email, StringComparison.OrdinalIgnoreCase)
            ? "Миний бүртгэл"
            : displayName;
    }

    private static string AccountActorName(StudioAccountSession session)
    {
        string displayName = session.DisplayName.Trim();
        return string.IsNullOrWhiteSpace(displayName) ||
               displayName.Equals(session.Email, StringComparison.OrdinalIgnoreCase)
            ? "Бүртгэлтэй хэрэглэгч"
            : displayName;
    }

    private static string ProfileImageKey(StudioAccountSession session)
    {
        string path = string.IsNullOrWhiteSpace(session.ProfileImageUrl)
            ? "/api/studio/profile/photo"
            : session.ProfileImageUrl;
        return session.ServerUrl.TrimEnd('/') + path;
    }

    private ProjectCreationRequest AttachAccountIdentity(ProjectCreationRequest request) => new()
    {
        Code = request.Code,
        Name = request.Name,
        Description = request.Description,
        Channel = ProjectCreationChannels.Studio,
        InitiatorType = request.InitiatorType,
        InitiatorOrganizationId = request.InitiatorOrganizationId,
        InitiatorOrganizationName = request.InitiatorOrganizationName,
        InitiatorUserId = account.Current!.Email,
        InitiatorDisplayName = AccountActorName(account.Current),
        ClientName = request.ClientName,
        ClientEmail = request.ClientEmail,
        SiteAddress = request.SiteAddress,
    };

    private static ProjectRow ToProjectRow(ProjectCatalogItem project) => new(
        project.ProjectCode,
        project.DisplayName,
        ProjectStageLabel(project.StageName),
        ProjectCreatorLabel(project.InitiatorType, project.InitiatorOrganization),
        project.DesignOrganization,
        ProjectConnectionLabel(project),
        project.ProjectPath,
        project.IsLegacyProject,
        project.ProjectId,
        false);

    private static string ProjectStageLabel(string stage)
    {
        string value = (stage ?? "").Trim();
        if (value.Equals(ProjectWorkspace.ConceptDesignStage, StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Concept Design", StringComparison.OrdinalIgnoreCase))
        {
            return "Загвар зураг";
        }
        if (value.Equals("WorkingDesign", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("DetailedDesign", StringComparison.OrdinalIgnoreCase) ||
            value.Equals("Working Design", StringComparison.OrdinalIgnoreCase))
        {
            return "Ажлын зураг";
        }
        return string.IsNullOrWhiteSpace(value) ? "Үе шат тодорхойгүй" : value;
    }

    private static string SafePathSegment(string value)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        string safe = new(value.Trim().Select(character => invalid.Contains(character) ? '_' : character).ToArray());
        safe = safe.Trim('.', ' ');
        return string.IsNullOrWhiteSpace(safe) ? "project" : safe;
    }

    private void OpenProject(string path)
    {
        try
        {
            state.OpenProject(path);
            EnterProjectWorkspace();
            lastAlbumPath = string.IsNullOrWhiteSpace(state.Project.PrimaryAlbum.LastPdfPath)
                ? null
                : ProjectWorkspacePaths.ResolveInsideProject(state.ProjectPath!, state.Project.PrimaryAlbum.LastPdfPath);
            SetStatus(state.LastOpenMigratedLegacyProject
                ? $"Legacy project шинэ workspace болсон. Эх файл хэвээр: {path}"
                : $"Төсөл нээгдлээ: {state.ProjectPath}");
            _ = RefreshCurrentProjectCloudAccessAsync();
        }
        catch (Exception exception)
        {
            SetStatus($"Төсөл нээхэд алдаа: {exception.Message}");
        }
    }

    private void EnterProjectWorkspace(StudioPage startPage = StudioPage.Overview)
    {
        projectWorkspaceOpen = true;
        RefreshOpenProjectCompanyFromLocalCache();
        BindProjectToUi();
        RebuildNavigation();
        SelectPage(startPage);
    }

    private UIElement BuildOverviewPage()
    {
        var panel = new StackPanel { Margin = new Thickness(18), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        syncButton.Click += async (_, _) => await SyncCurrentProjectAsync();
        syncButton.ToolTip = "AutoCAD/Revit package болон Studio альбумын одоогийн snapshot-ийг Cloud ERA руу илгээх";
        DockPanel.SetDock(syncButton, Dock.Right);
        header.Children.Add(syncButton);
        header.Children.Add(StudioWidgets.CreateTitle("Төслийн ерөнхий мэдээлэл"));
        panel.Children.Add(header);
        projectOverviewText.Foreground = StudioTheme.TextBrush;
        projectOverviewText.Margin = new Thickness(0, 8, 0, 0);
        panel.Children.Add(StudioWidgets.CreateCard(projectOverviewText));
        syncSummaryText.Foreground = StudioTheme.MutedTextBrush;
        syncSummaryText.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(StudioWidgets.CreateCard(syncSummaryText));
        return StudioWidgets.CreateScrollHost(panel);
    }

    private UIElement BuildFoundationPage()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var save = StudioWidgets.CreatePrimaryButton("Хадгалах");
        save.Click += (_, _) => SaveProject();
        DockPanel.SetDock(save, Dock.Right);
        toolbar.Children.Add(save);
        toolbar.Children.Add(StudioWidgets.CreateTitle("Төслийн суурь мэдээлэл"));
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var tabs = new TabControl
        {
            Background = StudioTheme.WindowBackgroundBrush,
            Foreground = StudioTheme.TextBrush,
            BorderBrush = StudioTheme.BorderBrush,
        };
        tabs.Items.Add(new TabItem { Header = "Эхлүүлэх үндэслэл", Content = BuildInitiationBasisTab() });
        tabs.Items.Add(new TabItem { Header = "АТД", Content = BuildPlanningTaskTab() });
        tabs.Items.Add(new TabItem { Header = "Компани", Content = BuildCompanyTab() });
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildInitiationBasisTab()
    {
        var form = FoundationForm();
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн код", projectCodeBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн нэр", projectNameBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Үндэслэлийн төрөл", basisSourceBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Хүсэлтийн дугаар", requestNumberBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Захиалагч", clientNameBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Захиалагчийн и-мэйл", clientEmailBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн хаяг", siteAddressBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Газрын холбоос", landReferenceBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Эх байгууллага", basisSourceOrganizationBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Товч мэдээлэл", basisSummaryBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Cloud ERA", cloudLinkText));
        return StudioWidgets.CreateScrollHost(form);
    }

    private UIElement BuildPlanningTaskTab()
    {
        var form = FoundationForm();
        form.Children.Add(StudioWidgets.CreateFormRow("АТД дугаар", atdNumberBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Олгосон байгууллага", atdAuthorityBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Төлөв", atdStatusBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Шаардлага, нөхцөл", atdSummaryBox));
        form.Children.Add(StudioWidgets.CreateSectionHeader("Зөвшөөрөх ба батлах эрхтэй оролцогчид"));
        ConfigureMemberList(atdParticipantsList);
        form.Children.Add(atdParticipantsList);
        return StudioWidgets.CreateScrollHost(form);
    }

    private UIElement BuildCompanyTab()
    {
        var form = FoundationForm();
        companyAssignmentText.FontSize = 15;
        companyAssignmentText.FontWeight = FontWeights.SemiBold;
        companyAssignmentText.Foreground = StudioTheme.TextBrush;
        companyAssignmentText.Margin = new Thickness(0, 0, 0, 4);
        form.Children.Add(companyAssignmentText);
        companyAssignmentPolicyText.Foreground = StudioTheme.MutedTextBrush;
        companyAssignmentPolicyText.Margin = new Thickness(0, 0, 0, 10);
        form.Children.Add(companyAssignmentPolicyText);

        projectCompanyLibraryButton.HorizontalAlignment = HorizontalAlignment.Left;
        projectCompanyLibraryButton.Click += (_, _) => OpenCompanyLibraryForProject();
        form.Children.Add(projectCompanyLibraryButton);

        form.Children.Add(StudioWidgets.CreateHint(
            "Компанийн project snapshot нь нүүр хуудас, компанийн мэдээллийн хуудас болон булангийн хүснэгтэд автоматаар хэрэглэгдэнэ. Задгай profile болон эх файлууд энэ төсөлд нээгдэхгүй."));
        form.Children.Add(StudioWidgets.CreateSectionHeader("Төслийн баг"));
        form.Children.Add(BuildTeamActions());
        ConfigureParticipantsList();
        form.Children.Add(participantsList);
        return StudioWidgets.CreateScrollHost(form);
    }

    private UIElement BuildReportsPage()
    {
        var panel = new StackPanel { Margin = new Thickness(18), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(StudioWidgets.CreateTitle("Тайлан"));
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Төрөл", Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(ReportRow.Type)) });
        view.Columns.Add(new GridViewColumn { Header = "Тайлан", Width = 390, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(ReportRow.Title)) });
        view.Columns.Add(new GridViewColumn { Header = "Төлөв", Width = 130, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(ReportRow.Status)) });
        view.Columns.Add(new GridViewColumn { Header = "Хувилбар", Width = 90, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(ReportRow.Version)) });
        reportsList.View = view;
        panel.Children.Add(reportsList);
        return StudioWidgets.CreateScrollHost(panel);
    }

    private UIElement BuildArchivePage()
    {
        var panel = new StackPanel { Margin = new Thickness(18), MaxWidth = 980, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(StudioWidgets.CreateTitle("Архив"));
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Төрөл", Width = 150, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(ArchiveRow.Type)) });
        view.Columns.Add(new GridViewColumn { Header = "Баримт", Width = 390, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(ArchiveRow.Title)) });
        view.Columns.Add(new GridViewColumn { Header = "Төлөв", Width = 130, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(ArchiveRow.Status)) });
        view.Columns.Add(new GridViewColumn { Header = "Архивласан", Width = 160, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(ArchiveRow.ArchivedAt)) });
        archiveList.View = view;
        panel.Children.Add(archiveList);
        return StudioWidgets.CreateScrollHost(panel);
    }

    private void SaveProject()
    {
        if (!state.HasOpenProject)
        {
            return;
        }
        try
        {
            CollectUiToProject();
            state.SaveProject();
            RefreshProjectOverview();
            RefreshProjects();
            RebuildNavigation();
            SelectPage(activePage);
            SetStatus($"Хадгалагдлаа: {state.ProjectPath}");
        }
        catch (Exception exception)
        {
            SetStatus($"Хадгалахад алдаа: {exception.Message}");
        }
    }

    private void UpdateAlbum(bool silent, string? statusPrefix = null)
    {
        if (!state.HasOpenProject)
        {
            return;
        }
        if (!CanEditProjectContent())
        {
            if (!silent)
                SetStatus("Таны project role альбум боловсруулах эрхгүй байна.");
            return;
        }
        try
        {
            AlbumBuildResult result = BuildLatestAlbum();
            var warningSuffix = result.Warnings.Count > 0 ? $" ({result.Warnings.Count} анхааруулга)" : "";
            var updateMessage = $"Альбум шинэчлэгдлээ: {result.SheetCount} sheet, {result.PageCount} хуудас - {result.OutputPath}{warningSuffix}";
            SetStatus(string.IsNullOrWhiteSpace(statusPrefix)
                ? updateMessage
                : $"{statusPrefix}. {updateMessage}");
        }
        catch (Exception exception)
        {
            if (!silent)
            {
                SetStatus($"Альбум шинэчлэхэд алдаа: {exception.Message}");
            }
        }
    }

    private AlbumBuildResult BuildLatestAlbum()
    {
        CollectUiToProject();
        string outputFolder = state.ResolveOutputFolder();
        Directory.CreateDirectory(outputFolder);
        string outputPath = Path.Combine(outputFolder, $"{SafeFileName(state.Album.Title)}.pdf");
        AlbumBuildResult result = state.Builder.Build(state.CreateAlbumBuildProject(), state.Library, outputPath);
        lastAlbumPath = result.OutputPath;
        state.RecordBuiltAlbum(result.OutputPath, result.PageCount, "Studio generated album");
        state.SaveProject();
        if (activePage == StudioPage.Albums)
            RefreshAlbumWorkspace(selectItemKey: selectedAlbumWorkspaceKey);
        RefreshSyncUi();
        return result;
    }

    private async Task SyncCurrentProjectAsync()
    {
        if (!state.HasOpenProject || syncInProgress)
            return;
        if (!await EnsureSignedInAsync())
            return;

        ProjectCloudLink cloud = state.Project.Cloud;
        if (!cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(cloud.ServerProjectId))
        {
            SetStatus("Энэ локал төсөл Cloud ERA project-той холбогдоогүй байна.");
            return;
        }

        syncInProgress = true;
        cloud.SyncStatus = ProjectSyncStatuses.Syncing;
        cloud.LastSyncError = "";
        state.SaveProject();
        RefreshSyncUi();
        try
        {
            CollectUiToProject();
            state.SaveProject();

            await ConfirmPendingProjectCompanyAssignmentAsync();
            cloud.SyncStatus = ProjectSyncStatuses.Syncing;
            state.SaveProject();

            string projectId = cloud.ServerProjectId;
            SetStatus("Cloud ERA төслийн бүтэц болон empty template album-ыг шалгаж байна...");
            _ = await account.GetProjectAsync(projectId);
            StudioCloudAlbum ensuredAlbum = await account.EnsureConceptAlbumAsync(projectId);
            IReadOnlyList<ProjectSourceSyncCandidate> sourcePackages =
                ProjectCloudSyncMetadata.PendingSourcePackages(state.Project);
            foreach (ProjectSourceSyncCandidate source in sourcePackages)
            {
                await account.RegisterSourcePackageAsync(projectId, new StudioCloudSourcePackageCreateRequest
                {
                    SourceKey = source.SourceKey,
                    SourceApplication = source.SourceApplication,
                    SourceDocumentReference = source.SourceDocumentReference,
                    ManifestId = source.ManifestId,
                    ManifestSchemaVersion = source.ManifestSchemaVersion,
                    ExportedAtUtc = source.ExportedAtUtc,
                    WorkPackageId = source.WorkPackageId,
                    SheetCount = source.SheetCount,
                    ContentHash = source.ContentHash,
                });
                ProjectCloudSyncMetadata.MarkSourceSynced(source);
            }

            IReadOnlyList<ProjectSourceSyncCandidate> localSources =
                ProjectCloudSyncMetadata.SourcePackages(state.Project);
            IReadOnlyList<StudioCloudDesignPackage> designPackages =
                await account.ListDesignPackagesAsync(projectId);
            List<StudioCloudSourcePackage> activeServerSources = designPackages
                .Where(item => item.DesignPackageType.Equals(
                    ProjectWorkspace.BuildingArchitectureConcept,
                    StringComparison.OrdinalIgnoreCase))
                .SelectMany(item => item.SourcePackages)
                .Where(item => !item.Status.Equals("Superseded", StringComparison.OrdinalIgnoreCase))
                .ToList();
            string currentAccount = account.Current?.Email ?? "";
            List<StudioCloudSourcePackage> missingRemoteSources = activeServerSources
                .Where(server => !IsServerSourceAvailableLocally(server, localSources, currentAccount))
                .ToList();

            IReadOnlyList<StudioCloudAlbum> albums = await account.ListAlbumsAsync(projectId);
            StudioCloudAlbum serverAlbum = albums.FirstOrDefault(item =>
                    item.AlbumId.Equals(ensuredAlbum.AlbumId, StringComparison.OrdinalIgnoreCase))
                ?? albums.FirstOrDefault(item =>
                    item.AlbumType.Equals(ProjectWorkspace.BuildingArchitectureConcept, StringComparison.OrdinalIgnoreCase))
                ?? ensuredAlbum;
            StudioCloudAlbumRevision? currentRevision = serverAlbum.Revisions.FirstOrDefault(item =>
                item.RevisionId.Equals(serverAlbum.CurrentRevisionId, StringComparison.OrdinalIgnoreCase));
            string syncedAlbumHash = currentRevision?.PdfSha256 ?? "";
            string syncedRevisionId = currentRevision?.RevisionId ?? "";
            string syncNote;
            if (localSources.Count == 0)
            {
                syncNote = currentRevision is null
                    ? "Empty template album sync хийгдлээ; source package болон PDF revision одоогоор байхгүй."
                    : $"Энэ төхөөрөмжид source байхгүй тул server album R{currentRevision.RevisionNumber} хэвээр хадгалагдлаа.";
            }
            else if (missingRemoteSources.Count > 0)
            {
                syncNote = $"{missingRemoteSources.Count} remote source энэ төхөөрөмжид байхгүй тул хэсэгчилсэн PDF-ээр бүтэн album солигдоогүй.";
            }
            else
            {
                int expectedSheetCount = localSources.Sum(item => item.SheetCount);
                int loadedSheetCount = state.Library.Snapshot().Count;
                if (loadedSheetCount < expectedSheetCount)
                {
                    syncNote = $"Source metadata sync хийгдсэн; {expectedSheetCount - loadedSheetCount} sheet local library-д уншигдаагүй тул album PDF солигдоогүй.";
                }
                else
                {
                    SetStatus("Бүх source бүрэн байна. Studio album revision бэлтгэж байна...");
                    AlbumBuildResult build = BuildLatestAlbum();
                    cloud.SyncStatus = ProjectSyncStatuses.Syncing;
                    state.SaveProject();
                    string localHash = state.Project.PrimaryAlbum.LastPdfSha256;
                    StudioCloudAlbumRevision syncedRevision;
                    if (currentRevision != null && currentRevision.PdfSha256.Equals(localHash, StringComparison.OrdinalIgnoreCase))
                    {
                        syncedRevision = currentRevision;
                    }
                    else
                    {
                        syncedRevision = await account.UploadAlbumRevisionAsync(
                            projectId,
                            serverAlbum.AlbumId,
                            build.OutputPath,
                            build.PageCount,
                            state.Project.PrimaryAlbum.LastPageSizeSummary);
                    }
                    syncedAlbumHash = localHash;
                    syncedRevisionId = syncedRevision.RevisionId;
                    syncNote = $"Бүтэн album R{syncedRevision.RevisionNumber} sync хийгдлээ.";
                }
            }

            StudioCloudProjectDetail latest = await account.GetProjectAsync(projectId);
            state.LinkCurrentProjectToCloud(latest, account.Current!.ServerUrl, preserveCreation: true);
            await ApplyCloudProjectRenderProfileAsync(latest);
            ProjectCloudSyncMetadata.MarkSynced(
                state.Project,
                syncedAlbumHash,
                syncedRevisionId,
                latest.Project.ConcurrencyToken,
                DateTimeOffset.UtcNow,
                syncNote);
            state.SaveProject();
            BindProjectToUi();
            await RefreshProjectsAsync();
            SetStatus($"Sync дууслаа: {sourcePackages.Count} source package. {syncNote}");
        }
        catch (Exception exception)
        {
            if (state.HasOpenProject)
            {
                ProjectCloudSyncMetadata.MarkError(state.Project, exception.Message);
                state.SaveProject();
                RefreshCloudLinkText();
            }
            SetStatus("Sync алдаа: " + exception.Message);
        }
        finally
        {
            syncInProgress = false;
            RefreshSyncUi();
        }
    }

    private static bool IsServerSourceAvailableLocally(
        StudioCloudSourcePackage server,
        IReadOnlyList<ProjectSourceSyncCandidate> localSources,
        string currentAccount)
    {
        if (!string.IsNullOrWhiteSpace(server.SourceKey))
        {
            return localSources.Any(local =>
                local.SourceKey.Equals(server.SourceKey, StringComparison.OrdinalIgnoreCase) &&
                local.ContentHash.Equals(server.ContentHash, StringComparison.OrdinalIgnoreCase));
        }

        if (localSources.Any(local =>
            local.ManifestId.Equals(server.ManifestId, StringComparison.OrdinalIgnoreCase) &&
            local.ContentHash.Equals(server.ContentHash, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(currentAccount) &&
            server.RegisteredBy.Equals(currentAccount, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshSyncUi()
    {
        if (!state.HasOpenProject)
        {
            syncButton.IsEnabled = false;
            syncButton.Content = "Sync";
            syncSummaryText.Text = "";
            return;
        }

        ProjectCloudLink cloud = state.Project.Cloud;
        bool linked = cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(cloud.ServerProjectId);
        syncButton.IsEnabled = linked && account.IsSignedIn && !syncInProgress && CanEditProjectContent();
        syncButton.Content = syncInProgress ? "Syncing..." : "Sync";
        syncButton.ToolTip = refreshingCurrentProjectAccess
            ? "Cloud ERA access эрхийг шинэчилж байна"
            : !linked
                ? "Энэ төслийг Cloud ERA project-той холбоход Sync идэвхжинэ"
                : !account.IsSignedIn
                    ? "Sync хийхийн тулд бүртгэлээрээ нэвтэрнэ үү"
                    : !CanEditProjectContent()
                        ? "Таны project role Sync хийх эрхгүй байна"
                        : "Эх үүсвэр, album snapshot-ийг Cloud ERA руу sync хийх";
        if (!linked)
        {
            syncSummaryText.Foreground = StudioTheme.MutedTextBrush;
            syncSummaryText.Text = "Энэ төсөл одоогоор локал байна. Cloud ERA project-той холбоход Sync идэвхжинэ.";
            return;
        }

        int pendingSources = ProjectCloudSyncMetadata.PendingSourcePackages(state.Project).Count;
        string lastSync = cloud.LastSyncedAtUtc.HasValue
            ? cloud.LastSyncedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "хийгдээгүй";
        syncSummaryText.Foreground = cloud.SyncStatus switch
        {
            ProjectSyncStatuses.Error => StudioTheme.DangerBrush,
            ProjectSyncStatuses.Pending => StudioTheme.WarningBrush,
            ProjectSyncStatuses.Synced => StudioTheme.SuccessBrush,
            _ => StudioTheme.MutedTextBrush,
        };
        syncSummaryText.Text =
            $"Cloud ERA: {cloud.SyncStatus}\n" +
            $"Сүүлийн sync: {lastSync}\n" +
            $"Хүлээгдэж буй source package: {pendingSources}" +
            (string.IsNullOrWhiteSpace(cloud.LastSyncNote) ? "" : $"\n{cloud.LastSyncNote}") +
            (string.IsNullOrWhiteSpace(cloud.LastSyncError) ? "" : $"\nАлдаа: {cloud.LastSyncError}");
    }

    private void BindProjectToUi()
    {
        if (!state.HasOpenProject)
        {
            return;
        }
        var project = state.Project;
        var basis = project.Foundation.InitiationBasis;
        var atd = project.Foundation.PlanningTask;
        var assignment = project.Foundation.DesignCompany;
        var company = assignment.OrganizationSnapshot;

        projectNameBox.Text = project.Name;
        projectCodeBox.Text = project.Code;
        basisSourceBox.Text = basis.SourceType;
        requestNumberBox.Text = basis.RequestNumber;
        clientNameBox.Text = basis.ClientName;
        clientEmailBox.Text = basis.ClientEmail;
        siteAddressBox.Text = basis.SiteAddress;
        landReferenceBox.Text = basis.LandReference;
        basisSourceOrganizationBox.Text = basis.SourceOrganizationName;
        basisSummaryBox.Text = basis.Summary;
        atdNumberBox.Text = atd.AtdNumber;
        atdAuthorityBox.Text = atd.IssuingAuthorityName;
        atdStatusBox.Text = atd.Status;
        atdSummaryBox.Text = atd.Summary;
        atdParticipantsList.ItemsSource = atd.AuthorityMembers
            .Select(member => new MemberRow(member.FullName, string.Join(", ", member.Roles), member.Email))
            .ToList();

        companyDisplayNameBox.Text = CompanyDisplayName(company);
        companyNameBox.Text = company.Name;
        companyRegistrationBox.Text = company.RegistrationNumber;
        companyOrganizationIdBox.Text = string.IsNullOrWhiteSpace(assignment.OrganizationId)
            ? company.OrganizationId
            : assignment.OrganizationId;
        companyShortNameBox.Text = company.ShortName;
        companyAddressBox.Text = company.Address;
        companyPhoneBox.Text = company.Phone;
        companyEmailBox.Text = company.Email;
        companyWebSiteBox.Text = company.WebSite;
        companyLogoBox.Text = company.LogoPath;
        companyAssignmentText.Text = string.IsNullOrWhiteSpace(CompanyDisplayName(company))
            ? "Зураг төслийн байгууллага сонгогдоогүй"
            : CompanyDisplayName(company);
        companyAssignmentPolicyText.Text = ProjectCompanyAssignmentDescription(project);

        var linked = string.Equals(project.Cloud.Origin, ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase);
        SetCanonicalFoundationReadOnly(linked);
        companyNameBox.IsReadOnly = linked;
        foreach (TextBox box in new[]
                 {
                     companyNameBox,
                     companyDisplayNameBox,
                     companyRegistrationBox,
                     companyOrganizationIdBox,
                     companyShortNameBox,
                     companyAddressBox,
                     companyPhoneBox,
                     companyEmailBox,
                     companyWebSiteBox,
                     companyLogoBox,
                 })
        {
            box.IsReadOnly = true;
        }

        albumTitleBox.Text = state.Album.Title;
        RefreshCloudLinkText();
        RefreshParticipantsList();
        RefreshSourceWorkspace();
        RefreshAlbumWorkspace();
        RefreshReportsAndArchive();
        RefreshProjectOverview();
        RefreshSyncUi();
    }

    private void CollectUiToProject()
    {
        var project = state.Project;
        project.Identity.Name = projectNameBox.Text.Trim();
        project.Identity.Code = projectCodeBox.Text.Trim();
        project.Identity.Description = basisSummaryBox.Text;
        var basis = project.Foundation.InitiationBasis;
        basis.SourceType = basisSourceBox.Text.Trim();
        basis.RequestNumber = requestNumberBox.Text.Trim();
        basis.ClientName = clientNameBox.Text.Trim();
        basis.ClientEmail = clientEmailBox.Text.Trim();
        basis.SiteAddress = siteAddressBox.Text.Trim();
        basis.LandReference = landReferenceBox.Text.Trim();
        basis.SourceOrganizationName = basisSourceOrganizationBox.Text.Trim();
        basis.Summary = basisSummaryBox.Text;

        var atd = project.Foundation.PlanningTask;
        atd.AtdNumber = atdNumberBox.Text.Trim();
        atd.IssuingAuthorityName = atdAuthorityBox.Text.Trim();
        atd.Status = atdStatusBox.Text.Trim();
        atd.Summary = atdSummaryBox.Text;

        state.Album.Title = string.IsNullOrWhiteSpace(albumTitleBox.Text)
            ? "Барилга архитектурын загвар зургийн альбум"
            : albumTitleBox.Text.Trim();
    }

    private void SetCanonicalFoundationReadOnly(bool readOnly)
    {
        foreach (var box in new[]
                 {
                     projectCodeBox,
                     projectNameBox,
                     basisSourceBox,
                     requestNumberBox,
                     clientNameBox,
                     clientEmailBox,
                     siteAddressBox,
                     landReferenceBox,
                     basisSourceOrganizationBox,
                     basisSummaryBox,
                     atdNumberBox,
                     atdAuthorityBox,
                     atdStatusBox,
                     atdSummaryBox,
                 })
        {
            box.IsReadOnly = readOnly;
        }
    }

    private void RefreshCloudLinkText()
    {
        var project = state.Project;
        var cloud = project.Cloud;
        cloudLinkText.Foreground = StudioTheme.MutedTextBrush;
        cloudLinkText.Text = string.Equals(cloud.Origin, ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase)
            ? $"{cloud.SyncStatus}  |  {cloud.ServerUrl}  |  ProjectId: {cloud.ServerProjectId}"
            : $"{project.Creation.Channel}  |  " +
              ProjectCreatorLabel(project.Creation.InitiatorType, project.Creation.InitiatorOrganizationName);
    }

    private void RefreshParticipantsList()
    {
        participantsList.ItemsSource = ActiveProjectMemberRows();
        RefreshTeamActionUi();
        _ = RefreshProjectTeamAsync();
    }

    private void ConfigureParticipantsList()
    {
        ConfigureMemberList(participantsList);
    }

    private static void ConfigureMemberList(ListView list)
    {
        if (list.View is not null)
        {
            return;
        }
        var view = new GridView();
        view.Columns.Add(new GridViewColumn { Header = "Нэр", Width = 250, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(MemberRow.Name)) });
        view.Columns.Add(new GridViewColumn { Header = "Үүрэг", Width = 250, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(MemberRow.Roles)) });
        view.Columns.Add(new GridViewColumn { Header = "И-мэйл", Width = 260, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(MemberRow.Email)) });
        view.Columns.Add(new GridViewColumn { Header = "Төлөв", Width = 170, DisplayMemberBinding = new System.Windows.Data.Binding(nameof(MemberRow.Status)) });
        list.View = view;
    }

    private void RefreshReportsAndArchive()
    {
        reportsList.ItemsSource = state.Project.Deliverables.Reports
            .Select(report => new ReportRow(report.Type, report.Title, report.Status, report.Version))
            .ToList();
        archiveList.ItemsSource = state.Project.Archive.Items
            .Select(item => new ArchiveRow(item.Type, item.Title, item.Status, item.ArchivedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")))
            .ToList();
    }

    private void RefreshProjectOverview()
    {
        var project = state.Project;
        var album = project.PrimaryAlbum;
        projectOverviewText.Text =
            $"{project.Code}\n{project.Name}\n\n" +
            $"Үе шат: {project.Identity.StageName}\n" +
            $"Үүсгэсэн тал: {ProjectCreatorLabel(project.Creation.InitiatorType, project.Creation.InitiatorOrganizationName)}\n" +
            $"Захиалагч: {ValueOrDash(project.Foundation.InitiationBasis.ClientName)}\n" +
            $"АТД: {ValueOrDash(project.Foundation.PlanningTask.AtdNumber)} · {ValueOrDash(project.Foundation.PlanningTask.Status)}\n" +
            $"Зураг төслийн байгууллага: {ValueOrDash(project.DesignOrganizationName)}\n" +
            $"Эх үүсвэр: {project.Sources.Count}\n" +
            $"Альбум: {album.Title} · {album.Status}\n" +
            $"Тайлан: {project.Deliverables.Reports.Count}\n" +
            $"Архив: {project.Archive.Items.Count}\n" +
            $"Холболт: {project.Cloud.Origin} · {project.Cloud.SyncStatus}";
    }

    private void OnLibraryChanged()
    {
        if (!state.HasOpenProject)
        {
            return;
        }
        RefreshSourceWorkspace();
        RefreshAlbumWorkspace();
        if (autoRebuildCheck.IsChecked == true)
        {
            autoRebuildTimer.Stop();
            autoRebuildTimer.Start();
        }
    }

    private void OnPackageProcessed(SheetPackageLoadResult result)
    {
        PackageRecordResult? recorded = null;
        try
        {
            recorded = state.RecordPackageReceived(result);
        }
        catch (Exception exception)
        {
            SetStatus($"Багц хүлээн авсан боловч source төлөв хадгалагдсангүй: {exception.Message}");
            return;
        }

        if (recorded is not null)
        {
            RefreshSourceWorkspace(recorded.SourceId);
            RefreshAlbumWorkspace();
            RefreshSyncUi();
        }
        if (!result.IsLossless)
        {
            SetStatus($"Багцын алдаа: {Path.GetFileName(result.ManifestPath)} - {string.Join("; ", result.Issues.Take(2))}");
        }
        else if (result.Manifest!.PackageScope == SheetPackageScope.FullSnapshot)
        {
            SetStatus($"Source-ийн бүтэн snapshot хүлээн авлаа: {result.Manifest.Sheets.Count} sheet" +
                (recorded?.RemovedAlbumPageCount > 0
                    ? $", альбумаас {recorded.RemovedAlbumPageCount} устсан хуудас хасагдлаа"
                    : ""));
        }
        else
        {
            SetStatus($"Source-ийн өөрчлөлт хүлээн авлаа: {Path.GetFileName(result.ManifestPath)} ({result.Manifest.Sheets.Count} sheet)");
        }
    }

    private static StackPanel FoundationForm() => new()
    {
        Margin = new Thickness(14),
        MaxWidth = 900,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static TextBox MultilineBox() => new()
    {
        AcceptsReturn = true,
        MinHeight = 88,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private static string SafeFileName(string name)
    {
        var cleaned = string.IsNullOrWhiteSpace(name) ? "album" : name.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            cleaned = cleaned.Replace(invalid, '_');
        }
        return cleaned;
    }

    private static string ValueOrDash(string value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    private static string ProjectCreatorLabel(string initiatorType, string organization)
    {
        var typeLabel = initiatorType switch
        {
            ProjectInitiatorTypes.GovernmentAuthority => "Төрийн байгууллага",
            ProjectInitiatorTypes.DesignOrganization => "Зураг төслийн байгууллага",
            _ => "Тодорхойгүй",
        };
        return string.IsNullOrWhiteSpace(organization)
            ? typeLabel
            : $"{typeLabel}: {organization}";
    }

    private static string ProjectConnectionLabel(ProjectCatalogItem project)
    {
        var channel = string.Equals(project.Origin, ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase)
            ? "Cloud"
            : project.CreationChannel;
        return $"{channel} · {project.SyncStatus}";
    }

    private void SetStatus(string message) => statusText.Text = message;

    private sealed class ProjectRow : INotifyPropertyChanged
    {
        private ImageSource? thumbnailSource;

        public ProjectRow(
            string code,
            string name,
            string stage,
            string creator,
            string company,
            string connection,
            string path,
            bool isLegacy,
            string serverProjectId,
            bool isCloudOnly)
        {
            Code = code;
            Name = name;
            Stage = stage;
            Creator = creator;
            Company = company;
            Connection = connection;
            Path = path;
            IsLegacy = isLegacy;
            ServerProjectId = serverProjectId;
            IsCloudOnly = isCloudOnly;
        }

        public string Code { get; }
        public string Name { get; }
        public string Stage { get; }
        public string Creator { get; }
        public string Company { get; }
        public string CompanyLabel => string.IsNullOrWhiteSpace(Company) ? "Байгууллага сонгогдоогүй" : Company;
        public string Connection { get; }
        public string Path { get; }
        public bool IsLegacy { get; }
        public string ServerProjectId { get; }
        public bool IsCloudOnly { get; }
        public string PreviewLabel => IsCloudOnly
            ? "CLOUD ERA"
            : Connection.StartsWith("Cloud", StringComparison.OrdinalIgnoreCase) ? "CLOUD MIRROR" : "LOCAL PROJECT";
        public ImageSource? ThumbnailSource => thumbnailSource;

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetThumbnail(ImageSource? source)
        {
            thumbnailSource = source;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailSource)));
        }
    }

    private sealed record MemberRow(
        string Name,
        string Roles,
        string Email,
        string Identifier = "",
        string Status = "Идэвхтэй",
        bool IsInvitation = false);
    private sealed record ReportRow(string Type, string Title, string Status, int Version);
    private sealed record ArchiveRow(string Type, string Title, string Status, string ArchivedAt);
}
