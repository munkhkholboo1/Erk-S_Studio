using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
        Foundation,
        Participants,
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
    private readonly ProgressBar projectOpenProgressBar = new()
    {
        Height = 3,
        Minimum = 0,
        Maximum = 100,
        Foreground = StudioTheme.SuccessBrush,
        Background = Brushes.Transparent,
        Visibility = Visibility.Collapsed,
    };
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private StudioPage activePage = StudioPage.Projects;
    private bool projectWorkspaceOpen;
    private bool projectOpenInProgress;
    private int projectReplacedUiBindSuppressionDepth;

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
    private readonly Button accountButton = new()
    {
        Background = Brushes.Transparent,
        BorderThickness = new Thickness(0),
        Padding = new Thickness(0),
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };
    private readonly Button notificationsRailButton = StudioWidgets.CreateGlyphTextButton(
        "\uE7F4",
        "Мэдэгдэл",
        "Багийн урилга болон шийдвэр хүлээж буй хүсэлтүүд");
    private readonly TextBlock notificationsRailBadgeText = new()
    {
        FontSize = 8.5,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border notificationsRailBadge = new()
    {
        MinWidth = 16,
        Height = 16,
        Padding = new Thickness(3, 0, 3, 0),
        CornerRadius = new CornerRadius(8),
        Background = StudioTheme.DangerBrush,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        IsHitTestVisible = false,
        Visibility = Visibility.Collapsed,
    };
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
    private readonly ComboBox clientTypeBox = new();
    private readonly TextBox clientNameBox = new();
    private readonly TextBox clientEmailBox = new();
    private readonly TextBox clientRepresentativePositionBox = new();
    private readonly TextBox clientRepresentativeNameBox = new();
    private FrameworkElement? clientNameRow;
    private TextBlock? clientNameLabel;
    private FrameworkElement? clientRepresentativePositionRow;
    private FrameworkElement? clientRepresentativeNameRow;
    private readonly Image clientLogoPreview = new()
    {
        Width = 112,
        Height = 64,
        Stretch = Stretch.Uniform,
        Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock clientLogoText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Button clientChooseLogoButton = StudioWidgets.CreateButton("Лого сонгох");
    private readonly Button clientRemoveLogoButton = StudioWidgets.CreateButton("Лого арилгах");
    private string pendingClientLogoPath = "";
    private bool clientLogoRemovalPending;
    private bool clientEditorInitialized;
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
    private readonly Button foundationEditButton = StudioWidgets.CreateGlyphTextButton(
        "\uE70F",
        "Засварлах",
        "Төслийн мэдээллийг засварлах");
    private readonly Button foundationSaveButton = StudioWidgets.CreateGlyphTextButton(
        "\uE74E",
        "Хадгалах",
        "Төслийн мэдээллийн өөрчлөлтийг хадгалах",
        primary: true);
    private readonly Button foundationCancelButton = StudioWidgets.CreateGlyphTextButton(
        "\uE711",
        "Болих",
        "Хадгалаагүй өөрчлөлтийг буцаах");
    private readonly Button participantsEditButton = StudioWidgets.CreateGlyphTextButton(
        "\uE70F",
        "Засварлах",
        "Төслийн оролцогчдын мэдээллийг засварлах");
    private readonly Button participantsSaveButton = StudioWidgets.CreateGlyphTextButton(
        "\uE74E",
        "Хадгалах",
        "Төслийн оролцогчдын өөрчлөлтийг хадгалах",
        primary: true);
    private readonly Button participantsCancelButton = StudioWidgets.CreateGlyphTextButton(
        "\uE711",
        "Болих",
        "Хадгалаагүй өөрчлөлтийг буцаах");
    private bool foundationEditMode;
    private bool foundationSaveInProgress;

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
    private readonly TextBlock clientParticipantsSummaryText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock designParticipantsSummaryText = new() { TextWrapping = TextWrapping.Wrap };

    // Project sync / deliverables
    private readonly Button cloudSyncButton = StudioWidgets.CreateGlyphButton(
        "\uE753",
        "Cloud Sync");
    private Border? projectContextBlock;
    private TextBlock? projectContextCodeText;
    private TextBlock? projectContextStageText;
    private bool syncInProgress;
    private readonly ListView reportsList = new() { MinHeight = 240 };
    private readonly ListView archiveList = new() { MinHeight = 240 };

    // Album workspace fields shared with ShellView.Workspaces.cs
    private readonly TextBox albumTitleBox = new();
    private Button? editSiteContextButton;
    private readonly CheckBox autoRebuildCheck = new()
    {
        Content = "Эх үүсвэр шинэчлэгдэхэд альбум автоматаар шинэчлэх",
        IsChecked = true,
    };
    private readonly TextBlock albumInfoText = new();
    private readonly DispatcherTimer autoRebuildTimer;
    private readonly DispatcherTimer notificationRefreshTimer;
    private readonly DispatcherTimer projectChatRefreshTimer;
    private bool suppressAutomaticAlbumRebuild;
    private string? lastAlbumPath;

    public UIElement Root { get; }

    public ShellView()
    {
        cloudSyncButton.Width = 40;
        cloudSyncButton.Height = 36;
        cloudSyncButton.Margin = new Thickness(8, 0, 0, 0);
        cloudSyncButton.Background = StudioTheme.AccentBrush;
        cloudSyncButton.BorderBrush = StudioTheme.AccentBrush;
        cloudSyncButton.Foreground = Brushes.White;
        if (cloudSyncButton.Content is TextBlock cloudGlyph)
            cloudGlyph.FontSize = 18;
        cloudSyncButton.Click += async (_, _) => await SynchronizeCurrentProjectAsync();
        autoRebuildTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
        autoRebuildTimer.Tick += (_, _) =>
        {
            autoRebuildTimer.Stop();
            if (autoRebuildCheck.IsChecked == true &&
                state.HasOpenProject &&
                !suppressAutomaticAlbumRebuild &&
                !syncInProgress)
            {
                UpdateAlbum(silent: true);
                if (activePage == StudioPage.Sources)
                {
                    string? selectedSource =
                        (designSourcesWorkspaceList.SelectedItem as SourceWorkspaceItem)?.SelectionKey;
                    RefreshSourceWorkspace(selectedSource);
                }
            }
        };
        notificationRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(45) };
        notificationRefreshTimer.Tick += async (_, _) =>
        {
            await RefreshNotificationsAsync();
            if (projectWorkspaceOpen && state.HasOpenProject)
            {
                await CheckCurrentProjectAccessAsync();
            }
            else if (activePage == StudioPage.Projects)
            {
                await RefreshProjectsAsync(refreshNotifications: false);
            }
        };
        projectChatRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(12) };
        projectChatRefreshTimer.Tick += async (_, _) =>
        {
            if (state.HasOpenProject && account.IsSignedIn)
                await RefreshProjectChatAsync(silent: true);
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
        state.AssetSourcesChanged += () => dispatcher.BeginInvoke(new Action(OnAssetSourcesChanged));
        state.ProjectReplaced += () =>
        {
            bool skipUiBind = Volatile.Read(ref projectReplacedUiBindSuppressionDepth) > 0;
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (skipUiBind)
                    return;

                if (state.HasOpenProject)
                {
                    BindProjectToUi();
                }
                else
                {
                    boundAlbumProjectId = null;
                    lastAlbumPath = null;
                    ResetAlbumPreviewForProjectChange();
                }
            }));
        };
        account.StateChanged += () => dispatcher.BeginInvoke(new Action(UpdateAccountUi));
        ((FrameworkElement)Root).Loaded += OnRootLoaded;
        UpdateAccountUi();
        SetStatus("Erk-S Studio бүртгэл болон лицензийг шалгаж байна...");
    }

    public void Dispose()
    {
        autoRebuildTimer.Stop();
        notificationRefreshTimer.Stop();
        projectChatRefreshTimer.Stop();
        projectChatLoadCancellation?.Cancel();
        projectChatLoadCancellation?.Dispose();
        projectThumbnailLoadCancellation?.Cancel();
        projectThumbnailLoadCancellation?.Dispose();
        CancelVisualizationThumbnailLoading();
        albumPdfViewer.Dispose();
        state.Dispose();
        account.Dispose();
        productUpdates.Dispose();
    }

    private UIElement BuildShell()
    {
        var root = new DockPanel();
        var status = StudioWidgets.CreateStatusBar(statusText);
        var footer = new StackPanel();
        footer.Children.Add(projectOpenProgressBar);
        footer.Children.Add(status);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

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
            BorderThickness = new Thickness(0),
            Child = railLayout,
        };
        DockPanel.SetDock(rail, Dock.Left);
        root.Children.Add(rail);

        pages[StudioPage.Projects] = BuildProjectsPage();
        pages[StudioPage.Companies] = BuildCompaniesPage();
        pages[StudioPage.Foundation] = BuildFoundationPage();
        pages[StudioPage.Participants] = BuildParticipantsPage();
        pages[StudioPage.Sources] = BuildSourcesPage();
        pages[StudioPage.Albums] = BuildAlbumPage();
        pages[StudioPage.Reports] = BuildReportsPage();
        pages[StudioPage.Archive] = BuildArchivePage();

        root.Children.Add(contentHost);
        var shell = new Grid();
        shell.Children.Add(root);
        UIElement chatWidget = BuildProjectChatWidget();
        projectChatPopup.Child = chatWidget;
        projectChatPopup.PlacementTarget = shell;
        projectChatPopup.CustomPopupPlacementCallback = PlaceProjectChatPopup;
        shell.Children.Add(projectChatPopup);
        shell.SizeChanged += (_, _) => RepositionProjectChatPopup();
        RebuildNavigation();
        SelectPage(StudioPage.Projects);
        return shell;
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
            BorderThickness = new Thickness(0),
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
        accountButton.Margin = new Thickness(0);
        accountButton.VerticalAlignment = VerticalAlignment.Center;
        accountButton.Click += async (_, _) =>
        {
            if (!account.IsSignedIn)
            {
                await ToggleAccountAsync();
                return;
            }

            if (accountButton.ContextMenu is { } menu)
            {
                menu.PlacementTarget = accountButton;
                menu.Placement = PlacementMode.Top;
                menu.IsOpen = true;
            }
        };
        notificationsRailButton.Margin = new Thickness(0);
        notificationsRailButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        notificationsRailButton.Click += async (_, _) => await ShowNotificationsAsync();
        notificationsRailBadge.Child = notificationsRailBadgeText;

        var details = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 0, 0),
        };
        details.Children.Add(accountStatusText);
        details.Children.Add(accountLicenseText);

        var row = new DockPanel { LastChildFill = true };
        var avatar = BuildAccountAvatar();
        DockPanel.SetDock(avatar, Dock.Left);
        row.Children.Add(avatar);
        var chevron = new TextBlock
        {
            Text = "\uE70D",
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 10,
            Foreground = StudioTheme.MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 2, 0),
        };
        DockPanel.SetDock(chevron, Dock.Right);
        row.Children.Add(chevron);
        row.Children.Add(details);
        accountButton.Content = row;

        var signOut = new MenuItem { Header = "Гарах" };
        signOut.Click += async (_, _) => await ToggleAccountAsync();
        accountButton.ContextMenu = new ContextMenu
        {
            MinWidth = 150,
            Items = { signOut },
        };
        var accountPanel = new StackPanel();
        var notificationHost = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        notificationHost.Children.Add(notificationsRailButton);
        notificationHost.Children.Add(notificationsRailBadge);
        accountPanel.Children.Add(notificationHost);
        accountPanel.Children.Add(accountButton);
        return new Border
        {
            BorderThickness = new Thickness(0),
            Padding = new Thickness(14, 12, 10, 12),
            Child = accountPanel,
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
            UpdateProjectChatWidgetVisibility();
            return;
        }

        navPanel.Children.Add(BuildProjectContextBlock());
        AddNavItem(StudioPage.Foundation, "Төслийн мэдээлэл", "icon-project.svg");
        AddNavItem(StudioPage.Participants, "Оролцогчид", "icon-company.svg");
        AddNavItem(StudioPage.Sources, ProjectSurfaceLabel("sources", "Эх үүсвэр"), "icon-sources.svg");
        AddNavItem(StudioPage.Albums, ProjectSurfaceLabel("albums", "Альбум"), "icon-album.svg");
        AddNavItem(StudioPage.Reports, ProjectSurfaceLabel("reports", "Тайлан"), "icon-publish.svg");
        AddNavItem(StudioPage.Archive, ProjectSurfaceLabel("archive", "Архив"), "icon-company.svg");
        UpdateProjectChatWidgetVisibility();
    }

    private string ProjectSurfaceLabel(string sectionId, string fallback)
    {
        ProjectServerSurfaceSection? section = state.Project.Cloud.ServerSnapshot.Surface.Sections
            .FirstOrDefault(item => item.Id.Equals(sectionId, StringComparison.OrdinalIgnoreCase));
        return string.IsNullOrWhiteSpace(section?.Label) ? fallback : section.Label.Trim();
    }

    private UIElement BuildProjectContextBlock()
    {
        if (projectContextBlock is not null)
        {
            projectContextCodeText!.Text = state.Project.Code;
            projectContextStageText!.Text = ProjectStageLabel(state.Project.Identity.StageName);
            return projectContextBlock;
        }

        var stack = new StackPanel();
        projectContextCodeText = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        projectContextStageText = new TextBlock
        {
            FontSize = 10.5,
            Foreground = StudioTheme.MutedTextBrush,
        };
        projectContextCodeText.Text = state.Project.Code;
        projectContextStageText.Text = ProjectStageLabel(state.Project.Identity.StageName);
        stack.Children.Add(projectContextCodeText);
        stack.Children.Add(projectContextStageText);
        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(cloudSyncButton, Dock.Right);
        content.Children.Add(cloudSyncButton);
        content.Children.Add(stack);
        projectContextBlock = new Border
        {
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 8, 0, 8),
            Background = StudioTheme.PanelAltBrush,
            CornerRadius = new CornerRadius(StudioTheme.CornerRadius),
            Child = content,
        };
        return projectContextBlock;
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
            RefreshAlbumWorkspace(selectItemKey: selectedAlbumWorkspaceKey);
            RefreshAlbumPagePreview();
            if (previousPage != StudioPage.Albums && !HasCurrentCloudAlbumPreview())
            {
                dispatcher.BeginInvoke(
                    new Action(() => UpdateAlbum(silent: true)),
                    DispatcherPriority.Background);
            }
        }
        else if (page == StudioPage.Sources)
        {
            RefreshSourceWorkspace();
        }
        else if (page == StudioPage.Companies)
        {
            _ = RefreshCompaniesAsync();
        }
        else if (page == StudioPage.Participants)
        {
            RefreshParticipantGroupSummaries();
            RefreshParticipantsList(refreshCloud: true);
        }
        else if (page is StudioPage.Reports or StudioPage.Archive)
        {
            RefreshReportsAndArchive();
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
                await OpenProjectFromFileAsync();
        };
        var refresh = StudioWidgets.CreateGlyphButton("\uE72C", "Төслийн жагсаалтыг шинэчлэх");
        refresh.Click += async (_, _) => await RefreshProjectsAsync();
        notificationsButton.Click += async (_, _) => await ShowNotificationsAsync();
        projectLifecycleButton.Click += async (_, _) => await RunSelectedProjectLifecycleActionAsync();
        productUpdateButton.Click += async (_, _) => await CheckForProductUpdateAsync(interactive: true);
        actions.Children.Add(create);
        actions.Children.Add(openFile);
        actions.Children.Add(notificationsButton);
        actions.Children.Add(projectLifecycleButton);
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
        projectsList.MouseDoubleClick += async (_, _) => await OpenSelectedProjectAsync();
        projectsList.SelectionChanged += (_, _) => UpdateSelectedProjectLifecycleAction();
        projectsList.KeyDown += async (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Enter)
            {
                return;
            }

            args.Handled = true;
            await OpenSelectedProjectAsync();
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
        style.Setters.Add(new Setter(Control.BorderBrushProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
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
        selected.Setters.Add(new Setter(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(30, 49, 70)), "CardBorder"));
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
        notificationRefreshTimer.Start();
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
                StudioMessageDialog.Show(
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
                StudioMessageDialog.Show(
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

    private async Task RefreshProjectsAsync(bool refreshNotifications = true)
    {
        if (!account.IsSignedIn)
        {
            projectRows = Array.Empty<ProjectRow>();
            projectRefreshNotice = "";
            if (refreshNotifications)
                await RefreshNotificationsAsync();
            ApplyProjectFilter();
            return;
        }

        if (refreshNotifications)
            await RefreshNotificationsAsync();
        RefreshLocalProjectCompanySnapshotsFromCache();
        List<ProjectCatalogItem> localProjects = new LocalProjectCatalog().ListProjects().ToList();
        var rows = new List<ProjectRow>();
        string cloudError = "";
        try
        {
            IReadOnlyList<StudioCloudProjectSummary> cloudProjects = await account.ListProjectsAsync();
            var accessibleProjectIds = cloudProjects
                .Select(item => item.ProjectId)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
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
                    local is null,
                    cloud.CurrentUserIsCreator,
                    cloud.CurrentUserScopes));
            }

            rows.AddRange(localProjects
                .Where(item =>
                    !matchedPaths.Contains(item.ProjectPath) &&
                    !item.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase))
                .Select(ToProjectRow));

            if (state.HasOpenProject &&
                state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId) &&
                !accessibleProjectIds.Contains(state.Project.Cloud.ServerProjectId))
            {
                CloseCurrentCloudProjectAfterAccessEnded(
                    "Төслийн гишүүний эрх дууссан тул Cloud төсөл таны жагсаалтаас хасагдлаа. Локал эх файл болон mirror устгагдаагүй.");
            }
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
        UpdateSelectedProjectLifecycleAction();
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
        try
        {
            SetStatus("Бүртгэлд хамаарах байгууллагуудыг уншиж байна...");
            organizations = await account.ListOrganizationsAsync();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            SetStatus("Байгууллагын бүртгэл уншигдсангүй: " + exception.Message);
            return;
        }

        bool hasManagedDesignOrganization = organizations.Any(
            StudioOrganizationAccessPolicy.CanCreateDesignProject);
        if (!hasManagedDesignOrganization)
        {
            SelectPage(StudioPage.Companies);
            SetStatus("Төсөл үүсгэхийн өмнө Компани хэсэгт админ эрхтэй зураг төслийн байгууллага үүсгэнэ үү.");
            return;
        }

        var dialog = new NewProjectDialog(organizations) { Owner = Window.GetWindow(Root) };
        if (dialog.ShowDialog() != true)
            return;
        try
        {
            ProjectCreationRequest request = AttachAccountIdentity(dialog.CreationRequest);
            StudioCloudOrganization? selectedOrganization = dialog.SelectedOrganization;
            if (selectedOrganization is null)
                throw new InvalidOperationException("Төсөл үүсгэх байгууллага сонгогдоогүй байна.");
            string relationshipCounterparty = string.IsNullOrWhiteSpace(request.ClientName)
                ? selectedOrganization.LegalName
                : request.ClientName;
            if (!StudioRelationshipBoundary.Confirm(
                    Window.GetWindow(Root),
                    StudioRelationshipAction.CreateProjectForClient,
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
            StudioCloudProjectDetail cloud = await account.CreateProjectAsync(cloudRequest);
            state.NewProject(request);
            state.LinkCurrentProjectToCloud(cloud, account.Current!.ServerUrl, request);
            await ApplyCloudProjectRenderProfileAsync(cloud);
            if (request.InitiatorType.Equals(ProjectInitiatorTypes.DesignOrganization, StringComparison.OrdinalIgnoreCase))
                ApplyCompanyToOpenProject(MapCloudCompany(selectedOrganization), rebuildAlbum: false);
            EnterProjectWorkspace(StudioPage.Foundation);
            SetStatus($"Cloud ERA төсөл болон локал mirror үүслээ: {state.ProjectPath}");
        }
        catch (Exception exception)
        {
            SetStatus($"Төсөл үүсгэхэд алдаа: {exception.Message}");
        }
    }

    private async Task OpenProjectFromFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = $"Erk-S Studio төсөл (*{ProjectWorkspace.FileExtension};*{AlbumProject.FileExtension})|*{ProjectWorkspace.FileExtension};*{AlbumProject.FileExtension}",
        };
        if (dialog.ShowDialog() == true)
        {
            await OpenProjectAsync(dialog.FileName);
        }
    }

    private async Task OpenSelectedProjectAsync()
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
        await OpenProjectAsync(row.Path);
    }

    private async Task OpenCloudProjectAsync(ProjectRow row)
    {
        try
        {
            suppressAutomaticAlbumRebuild = true;
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
            await DrainSuppressedAlbumRebuildEventsAsync();
            CloudAlbumCacheRefreshResult albumRefresh = await RefreshCloudAlbumPreviewAsync(
                cloud.Project.ProjectId,
                cloud.Albums);
            bool albumCached = albumRefresh.HasCurrentAlbum;
            await DrainSuppressedAlbumRebuildEventsAsync();
            ProjectCloudSyncMetadata.MarkCloudRefreshed(
                state.Project,
                cloud.Project.ConcurrencyToken,
                DateTimeOffset.UtcNow);
            state.SaveProject();
            BindProjectToUi();
            SetStatus(albumCached
                ? $"Cloud ERA төслийн локал mirror болон current album PDF нээгдлээ: {state.ProjectPath}"
                : $"Cloud ERA төслийн локал mirror нээгдлээ; current album PDF одоогоор алга: {state.ProjectPath}");
        }
        catch (Exception exception)
        {
            SetStatus("Cloud ERA төсөл нээхэд алдаа: " + exception.Message);
        }
        finally
        {
            autoRebuildTimer.Stop();
            suppressAutomaticAlbumRebuild = false;
        }
    }

    private async Task DrainSuppressedAlbumRebuildEventsAsync()
    {
        await dispatcher.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
        autoRebuildTimer.Stop();
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
        accountButton.ToolTip = session is null
            ? "Cloud ERA бүртгэлээр нэвтрэх"
            : "Бүртгэлийн сонголт";
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
        RefreshFoundationEditUi();
        RefreshSyncUi();
        if (activePage == StudioPage.Sources && state.HasOpenProject)
        {
            RefreshSourceDetails();
        }
        UpdateProjectChatWidgetVisibility();
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
                StrokeThickness = 0,
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
        false,
        false,
        []);

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

    private async Task OpenProjectAsync(string path)
    {
        if (projectOpenInProgress)
            return;

        projectOpenInProgress = true;
        try
        {
            await UpdateProjectOpenProgressAsync(12, "Төслийн локал мэдээллийг уншиж байна...");
            await Task.Run(() => SuppressProjectReplacedUiBind(() => state.OpenProject(path)));
            await UpdateProjectOpenProgressAsync(52, "Төслийн мэдээллийг дэлгэцэд бэлтгэж байна...");
            EnterProjectWorkspace();
            await UpdateProjectOpenProgressAsync(86, "Төслийн ажлын орчныг нээж байна...");
            lastAlbumPath = ResolveCurrentProjectAlbumPath();
            if (state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId))
            {
                autoRebuildTimer.Stop();
            }
            SetStatus(state.LastOpenMigratedLegacyProject
                ? $"Legacy project шинэ workspace болсон. Эх файл хэвээр: {path}"
                : $"Төсөл нээгдлээ: {state.ProjectPath}");
            _ = RescanOpenedProjectPackagesAsync(state.Project.ProjectId, state.ProjectPath);
            _ = RefreshCurrentProjectCloudAccessAsync();
            await UpdateProjectOpenProgressAsync(100, "Төсөл нээгдлээ.");
        }
        catch (Exception exception)
        {
            SetStatus($"Төсөл нээхэд алдаа: {exception.Message}");
        }
        finally
        {
            await HideProjectOpenProgressAsync();
            projectOpenInProgress = false;
        }
    }

    private async Task UpdateProjectOpenProgressAsync(double value, string message)
    {
        projectOpenProgressBar.Visibility = Visibility.Visible;
        projectOpenProgressBar.Value = Math.Clamp(value, 0, 100);
        SetStatus(message);
        await Dispatcher.Yield(DispatcherPriority.Render);
    }

    private async Task HideProjectOpenProgressAsync()
    {
        await Dispatcher.Yield(DispatcherPriority.Render);
        await Task.Delay(120);
        projectOpenProgressBar.Visibility = Visibility.Collapsed;
        projectOpenProgressBar.Value = 0;
    }

    private void SuppressProjectReplacedUiBind(Action action)
    {
        Interlocked.Increment(ref projectReplacedUiBindSuppressionDepth);
        try
        {
            action();
        }
        finally
        {
            Interlocked.Decrement(ref projectReplacedUiBindSuppressionDepth);
        }
    }

    private async Task RescanOpenedProjectPackagesAsync(string projectId, string? projectPath)
    {
        try
        {
            IReadOnlyList<SheetPackageCheckpoint> checkpoints =
                state.CurrentSourcePackageCheckpoints();
            SheetIntakeScanResult scan = await Task.Run(() =>
                state.Intake.RescanCurrentSnapshots(checkpoints));
            if (!state.HasOpenProject ||
                !state.Project.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(state.ProjectPath, projectPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (scan.ErrorCount > 0)
                SetStatus($"Source background scan: {scan.ErrorCount} алдаа илэрлээ.");
            else if (scan.SilentlyHydratedManifestCount > 0 && activePage == StudioPage.Sources)
                RefreshSourceWorkspace();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            if (state.HasOpenProject &&
                state.Project.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
            {
                SetStatus("Source background scan алдаа: " + exception.Message);
            }
        }
    }

    private void EnterProjectWorkspace(StudioPage startPage = StudioPage.Foundation)
    {
        projectWorkspaceOpen = true;
        RefreshOpenProjectCompanyFromLocalCache();
        BindProjectToUi();
        RebuildNavigation();
        SelectPage(startPage);
    }

    private UIElement BuildFoundationPage()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        foundationEditButton.Click += (_, _) => BeginFoundationEdit();
        foundationSaveButton.Click += async (_, _) => await SaveFoundationChangesAsync();
        foundationCancelButton.Click += (_, _) => CancelFoundationEdit();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        actions.Children.Add(foundationCancelButton);
        actions.Children.Add(foundationSaveButton);
        actions.Children.Add(foundationEditButton);
        DockPanel.SetDock(actions, Dock.Right);
        toolbar.Children.Add(actions);
        toolbar.Children.Add(StudioWidgets.CreateTitle("Төслийн мэдээлэл"));
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var tabs = new TabControl
        {
            Background = StudioTheme.WindowBackgroundBrush,
            Foreground = StudioTheme.TextBrush,
            BorderBrush = StudioTheme.BorderBrush,
        };
        tabs.Items.Add(new TabItem { Header = "Төслийн мэдээлэл", Content = BuildInitiationBasisTab() });
        tabs.Items.Add(new TabItem { Header = "Уялдаа, баталгаажуулалт", Content = BuildPlanningTaskTab() });
        root.Children.Add(tabs);
        RefreshFoundationEditUi();
        return root;
    }

    private UIElement BuildInitiationBasisTab()
    {
        var form = FoundationForm();
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн код", projectCodeBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн нэр", projectNameBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Үндэслэлийн төрөл", basisSourceBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Хүсэлтийн дугаар", requestNumberBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Төслийн хаяг", siteAddressBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Газрын холбоос", landReferenceBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Эх байгууллага", basisSourceOrganizationBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Товч мэдээлэл", basisSummaryBox));
        form.Children.Add(BuildProjectCompanyAssignmentSection());
        form.Children.Add(StudioWidgets.CreateFormRow("Cloud ERA", cloudLinkText));
        return StudioWidgets.CreateScrollHost(form);
    }

    private UIElement BuildClientTypeEditor()
    {
        if (!clientEditorInitialized)
        {
            clientTypeBox.ItemsSource = new[]
            {
                new ClientTypeOption("Иргэн", ProjectClientTypes.Citizen),
                new ClientTypeOption("Байгууллага", ProjectClientTypes.Organization),
                new ClientTypeOption("Төрийн байгууллага", ProjectClientTypes.GovernmentAuthority),
            };
            clientTypeBox.SelectionChanged += (_, _) => RefreshClientLogoEditor();
            clientChooseLogoButton.Click += (_, _) => ChooseClientLogo();
            clientRemoveLogoButton.Click += (_, _) => RemoveClientLogo();
            clientEditorInitialized = true;
        }

        return clientTypeBox;
    }

    private UIElement BuildClientLogoEditor()
    {
        var details = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
        details.Children.Add(clientLogoText);
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 7, 0, 0),
        };
        actions.Children.Add(clientChooseLogoButton);
        actions.Children.Add(clientRemoveLogoButton);
        details.Children.Add(actions);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(clientLogoPreview);
        panel.Children.Add(details);
        return panel;
    }

    private string SelectedClientType
    {
        get
        {
            if (clientTypeBox.SelectedItem is ClientTypeOption option)
                return option.Value;
            if (!state.HasOpenProject)
                return ProjectClientTypes.Citizen;

            ProjectInitiationBasis basis = state.Project.Foundation.InitiationBasis;
            return ProjectClientTypes.ResolveStoredType(
                basis.ClientType,
                basis.ClientOrganizationSnapshot);
        }
    }

    private void SelectClientType(string? value)
    {
        if (!clientEditorInitialized)
            _ = BuildClientTypeEditor();
        string normalized = ProjectClientTypes.Normalize(value);
        clientTypeBox.SelectedItem = clientTypeBox.Items
            .OfType<ClientTypeOption>()
            .FirstOrDefault(option => option.Value.Equals(normalized, StringComparison.Ordinal));
        if (clientTypeBox.SelectedItem is null && clientTypeBox.Items.Count > 0)
            clientTypeBox.SelectedIndex = 0;
    }

    private void ChooseClientLogo()
    {
        if (!foundationEditMode || !ProjectClientTypes.UsesLogo(SelectedClientType))
            return;

        var dialog = new OpenFileDialog
        {
            Title = "Захиалагч байгууллагын лого сонгох",
            Filter = "Зургийн файл (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Multiselect = false,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(Root)) != true)
            return;

        pendingClientLogoPath = dialog.FileName;
        clientLogoRemovalPending = false;
        RefreshClientLogoEditor();
    }

    private void RemoveClientLogo()
    {
        if (!foundationEditMode)
            return;
        pendingClientLogoPath = "";
        clientLogoRemovalPending = true;
        RefreshClientLogoEditor();
    }

    private void RefreshClientLogoEditor()
    {
        bool usesLogo = ProjectClientTypes.UsesLogo(SelectedClientType);
        bool editable = foundationEditMode && !foundationSaveInProgress && usesLogo;
        if (clientNameRow is not null)
            clientNameRow.Visibility = Visibility.Visible;
        if (clientNameLabel is not null)
            clientNameLabel.Text = ProjectClientTypes.ClientNameFieldLabel(SelectedClientType);
        if (clientRepresentativePositionRow is not null)
            clientRepresentativePositionRow.Visibility = usesLogo ? Visibility.Visible : Visibility.Collapsed;
        if (clientRepresentativeNameRow is not null)
            clientRepresentativeNameRow.Visibility = usesLogo ? Visibility.Visible : Visibility.Collapsed;
        clientRepresentativePositionBox.IsReadOnly = !editable;
        clientRepresentativeNameBox.IsReadOnly = !editable;
        clientChooseLogoButton.IsEnabled = editable;
        clientRemoveLogoButton.IsEnabled = editable &&
            (!string.IsNullOrWhiteSpace(pendingClientLogoPath) ||
             (!clientLogoRemovalPending && state.HasOpenProject &&
              !string.IsNullOrWhiteSpace(state.Project.Foundation.InitiationBasis
                  .ClientOrganizationSnapshot.LogoPath)));

        if (!usesLogo)
        {
            clientLogoPreview.Source = null;
            clientLogoPreview.Visibility = Visibility.Collapsed;
            clientLogoText.Text = "Иргэн захиалагчийн логоны нүд нүүр хуудсанд хоосон байна.";
            return;
        }

        CompanyProfile clientOrganization = state.HasOpenProject
            ? state.Project.Foundation.InitiationBasis.ClientOrganizationSnapshot
            : new CompanyProfile();
        string storedPath = state.HasOpenProject && !clientLogoRemovalPending
            ? clientOrganization.LogoPath
            : "";
        string path = string.IsNullOrWhiteSpace(pendingClientLogoPath)
            ? ResolveClientLogoPath(storedPath)
            : pendingClientLogoPath;
        clientLogoText.Text = !string.IsNullOrWhiteSpace(pendingClientLogoPath)
            ? Path.GetFileName(pendingClientLogoPath)
            : string.IsNullOrWhiteSpace(storedPath)
                ? "Лого сонгоогүй"
                : ProjectAssetDisplayName.Resolve(
                    clientOrganization.LogoOriginalFileName,
                    storedPath,
                    "Захиалагчийн лого");
        clientLogoPreview.Source = LoadLocalBitmap(path);
        clientLogoPreview.Visibility = clientLogoPreview.Source is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private string ResolveClientLogoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";
        if (Path.IsPathRooted(path) || string.IsNullOrWhiteSpace(state.ProjectPath))
            return path;
        try
        {
            return ProjectWorkspacePaths.ResolveInsideProject(state.ProjectPath, path);
        }
        catch
        {
            return "";
        }
    }

    private static BitmapSource? LoadLocalBitmap(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private UIElement BuildPlanningTaskTab()
    {
        var form = FoundationForm();
        form.Children.Add(StudioWidgets.CreateFormRow("АТД дугаар", atdNumberBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Олгосон байгууллага", atdAuthorityBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Төлөв", atdStatusBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Шаардлага, нөхцөл", atdSummaryBox));
        form.Children.Add(StudioWidgets.CreateSectionHeader("АТД-ийн батлагдсан хуулбар"));
        form.Children.Add(BuildAtdDocumentEditor());
        return StudioWidgets.CreateScrollHost(form);
    }

    private UIElement BuildProjectCompanyAssignmentSection()
    {
        var form = new StackPanel();
        form.Children.Add(StudioWidgets.CreateSectionHeader("Зураг төсөл боловсруулагч байгууллага"));
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
        return form;
    }

    private UIElement BuildParticipantsPage()
    {
        var root = new DockPanel { Margin = new Thickness(18) };
        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        participantsEditButton.Click += (_, _) => BeginFoundationEdit(focusProjectName: false);
        participantsSaveButton.Click += async (_, _) => await SaveFoundationChangesAsync();
        participantsCancelButton.Click += (_, _) => CancelFoundationEdit();
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        actions.Children.Add(participantsCancelButton);
        actions.Children.Add(participantsSaveButton);
        actions.Children.Add(participantsEditButton);
        DockPanel.SetDock(actions, Dock.Right);
        toolbar.Children.Add(actions);
        toolbar.Children.Add(StudioWidgets.CreateTitle("Төслийн оролцогчид"));
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var tabs = new TabControl
        {
            Background = StudioTheme.WindowBackgroundBrush,
            Foreground = StudioTheme.TextBrush,
            BorderBrush = StudioTheme.BorderBrush,
        };
        tabs.Items.Add(new TabItem { Header = "Захиалагч тал", Content = BuildClientParticipantsTab() });
        tabs.Items.Add(new TabItem { Header = "Боловсруулагч тал", Content = BuildDesignParticipantsTab() });
        tabs.Items.Add(new TabItem { Header = "Баталгаажуулагч тал", Content = BuildApprovalParticipantsTab() });
        root.Children.Add(tabs);
        return root;
    }

    private UIElement BuildClientParticipantsTab()
    {
        var form = FoundationForm();
        clientParticipantsSummaryText.Foreground = StudioTheme.MutedTextBrush;
        clientParticipantsSummaryText.Margin = new Thickness(0, 0, 0, 12);
        form.Children.Add(clientParticipantsSummaryText);
        form.Children.Add(StudioWidgets.CreateFormRow("Захиалагчийн төрөл", BuildClientTypeEditor()));
        clientNameRow = StudioWidgets.CreateFormRow("Захиалагчийн нэр", clientNameBox);
        clientNameLabel = (clientNameRow as Grid)?.Children.OfType<TextBlock>().FirstOrDefault();
        form.Children.Add(clientNameRow);
        form.Children.Add(StudioWidgets.CreateFormRow("Захиалагчийн и-мэйл", clientEmailBox));
        clientRepresentativePositionRow = StudioWidgets.CreateFormRow(
            "Төлөөлөгчийн албан тушаал",
            clientRepresentativePositionBox);
        clientRepresentativeNameRow = StudioWidgets.CreateFormRow(
            "Төлөөлөгчийн нэр",
            clientRepresentativeNameBox);
        form.Children.Add(clientRepresentativePositionRow);
        form.Children.Add(clientRepresentativeNameRow);
        form.Children.Add(StudioWidgets.CreateFormRow("Захиалагчийн лого", BuildClientLogoEditor()));
        form.Children.Add(StudioWidgets.CreateHint(
            "Энд хадгалсан захиалагчийн мэдээлэл нүүр хуудас, альбум болон Cloud ERA төслийн мэдээлэлд нэг эх сурвалжаас хэрэглэгдэнэ."));
        return StudioWidgets.CreateScrollHost(form);
    }

    private UIElement BuildDesignParticipantsTab()
    {
        var form = FoundationForm();
        designParticipantsSummaryText.Foreground = StudioTheme.MutedTextBrush;
        designParticipantsSummaryText.Margin = new Thickness(0, 0, 0, 12);
        form.Children.Add(designParticipantsSummaryText);
        form.Children.Add(StudioWidgets.CreateSectionHeader("Төслийн архитектор"));
        form.Children.Add(BuildProjectArchitectAssignment());
        form.Children.Add(StudioWidgets.CreateSectionHeader("Төслийн баг"));
        form.Children.Add(BuildTeamActions());
        ConfigureParticipantsList();
        form.Children.Add(participantsList);
        return StudioWidgets.CreateScrollHost(form);
    }

    private UIElement BuildApprovalParticipantsTab()
    {
        var form = FoundationForm();
        form.Children.Add(BuildConceptApprovalEditor());
        form.Children.Add(StudioWidgets.CreateSectionHeader("Cloud ERA эрхтэй оролцогчид"));
        form.Children.Add(StudioWidgets.CreateHint(
            "Энэ жагсаалт нь төрийн байгууллагын төслийн эрх, гишүүнчлэлийн лавлагаа. БАТЛАВ, ЗӨВШӨӨРӨЛЦСӨН, ЗӨВШИЛЦСӨН, ХЯНАВ мөрүүд тусдаа утгатай хэвээр хадгалагдана."));
        ConfigureMemberList(atdParticipantsList);
        form.Children.Add(atdParticipantsList);
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

    private void BeginFoundationEdit(bool focusProjectName = true)
    {
        if (!state.HasOpenProject)
            return;
        if (!CanEditProjectInformation())
        {
            SetStatus("Таны project role төслийн мэдээлэл засварлах эрхгүй байна.");
            return;
        }

        pendingClientLogoPath = "";
        clientLogoRemovalPending = false;
        BindFoundationFieldsToUi();
        foundationEditMode = true;
        StartAtdDocumentEdit();
        RefreshFoundationEditUi();
        if (focusProjectName)
        {
            projectNameBox.Focus();
            projectNameBox.CaretIndex = projectNameBox.Text.Length;
        }
        SetStatus("Засварлах төлөв нээгдлээ. Өөрчлөлтөө Хадгалах эсвэл Болих дарна уу.");
    }

    private void CancelFoundationEdit()
    {
        if (!foundationEditMode || foundationSaveInProgress)
            return;

        foundationEditMode = false;
        pendingClientLogoPath = "";
        clientLogoRemovalPending = false;
        BindFoundationFieldsToUi();
        RefreshFoundationEditUi();
        SetStatus("Хадгалаагүй өөрчлөлтийг буцаалаа.");
    }

    private async Task SaveFoundationChangesAsync()
    {
        if (!state.HasOpenProject || !foundationEditMode || foundationSaveInProgress)
            return;
        StudioPage returnPage = activePage == StudioPage.Participants
            ? StudioPage.Participants
            : StudioPage.Foundation;
        if (string.IsNullOrWhiteSpace(projectNameBox.Text))
        {
            SetStatus("Төслийн нэр хоосон байж болохгүй.");
            projectNameBox.Focus();
            return;
        }
        if (string.IsNullOrWhiteSpace(clientNameBox.Text))
        {
            SetStatus(ProjectClientTypes.ClientNameFieldLabel(SelectedClientType) + " хоосон байж болохгүй.");
            clientNameBox.Focus();
            return;
        }
        if (!CanEditProjectInformation())
        {
            SetStatus("Таны project role төслийн мэдээлэл хадгалах эрхгүй байна.");
            return;
        }

        ProjectFoundationEditDraft draft;
        try
        {
            draft = CaptureFoundationEditDraft();
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SetStatus("Захиалагчийн лого хадгалахад алдаа: " + exception.Message);
            return;
        }
        bool linked = state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId);
        bool shouldUploadClientLogo = linked &&
            !string.IsNullOrWhiteSpace(pendingClientLogoPath) &&
            ProjectClientTypes.UsesLogo(draft.ClientType) &&
            !string.IsNullOrWhiteSpace(draft.ClientLogoPath);
        bool shouldDeleteClientLogo = linked && clientLogoRemovalPending;
        bool foundationContentChanged = FoundationDraftDiffersFromProject(draft);
        if (linked && !await EnsureSignedInAsync())
            return;

        foundationSaveInProgress = true;
        RefreshFoundationEditUi();
        bool cloudUpdateQueued = false;
        string cloudSaveNotice = "";
        try
        {
            int foundationVersionBefore = state.Project.Foundation.Version;
            if (linked)
            {
                StudioCloudProjectInformationUpdateRequest request = draft.CreateCloudRequest(
                    state.Project.DesignOrganizationName,
                    state.Project.Cloud.ServerSnapshot.Information.CapacityUnit);
                try
                {
                    StudioCloudProjectDetail updated = await account.UpdateProjectInformationAsync(
                        state.Project.Cloud.ServerProjectId,
                        request,
                        await EnsureProjectConcurrencyTokenAsync(state.Project.Cloud.ServerProjectId));
                    ProjectInformationReconciliationResult reconciliation =
                        ProjectInformationSaveReconciler.Compare(request, updated, DateTimeOffset.UtcNow);
                    state.Project.Cloud.PendingProjectInformation = reconciliation.PendingUpdate;
                    state.LinkCurrentProjectToCloud(
                        updated,
                        account.Current!.ServerUrl,
                        preserveCreation: true,
                        preserveSyncState: true);
                    if (reconciliation.AcceptedByServer && shouldUploadClientLogo)
                    {
                        updated = await account.UploadProjectClientLogoAsync(
                            state.Project.Cloud.ServerProjectId,
                            ResolveClientLogoPath(draft.ClientLogoPath),
                            updated.Project.ConcurrencyToken);
                        state.LinkCurrentProjectToCloud(
                            updated,
                            account.Current!.ServerUrl,
                            preserveCreation: true,
                            preserveSyncState: true);
                    }
                    else if (reconciliation.AcceptedByServer && shouldDeleteClientLogo)
                    {
                        updated = await account.DeleteProjectClientLogoAsync(
                            state.Project.Cloud.ServerProjectId,
                            updated.Project.ConcurrencyToken);
                        state.LinkCurrentProjectToCloud(
                            updated,
                            account.Current!.ServerUrl,
                            preserveCreation: true,
                            preserveSyncState: true);
                    }
                    if (!reconciliation.AcceptedByServer)
                    {
                        PendingProjectInformationUpdate pending = reconciliation.PendingUpdate!;
                        cloudSaveNotice = BuildCanonicalDifferenceNotice(reconciliation.Differences);
                        ProjectCloudSyncMetadata.MarkConflict(
                            state.Project,
                            pending,
                            state.Project.Cloud.ServerSnapshot.ConcurrencyToken,
                            cloudSaveNotice);
                        cloudUpdateQueued = true;
                    }

                    bool studioMetadataChanged = ApplyStudioFoundationMetadata(draft);
                    if (studioMetadataChanged && state.Project.Foundation.Version <= foundationVersionBefore)
                        state.Project.Foundation.Version = foundationVersionBefore + 1;
                }
                catch (StudioAccountException exception) when (
                    exception.StatusCode is System.Net.HttpStatusCode.NotFound or
                        System.Net.HttpStatusCode.MethodNotAllowed)
                {
                    bool changed = FoundationDraftDiffersFromProject(draft);
                    ApplyCanonicalFoundation(draft);
                    _ = ApplyStudioFoundationMetadata(draft);
                    if (changed && state.Project.Foundation.Version <= foundationVersionBefore)
                        state.Project.Foundation.Version = foundationVersionBefore + 1;
                    state.Project.Cloud.PendingProjectInformation =
                        ProjectInformationSaveReconciler.CreatePendingUpdate(request, DateTimeOffset.UtcNow);
                    state.Project.Cloud.SyncStatus = ProjectSyncStatuses.Pending;
                    state.Project.Cloud.LastSyncError = "";
                    state.Project.Cloud.LastSyncNote =
                        "Төслийн мэдээлэл локал mirror-т хадгалагдсан; Cloud ERA server update хүлээгдэж байна.";
                    cloudUpdateQueued = true;
                }
                catch (StudioAccountException exception) when (
                    exception.StatusCode is System.Net.HttpStatusCode.Conflict or
                        System.Net.HttpStatusCode.PreconditionFailed)
                {
                    ApplyCanonicalFoundation(draft);
                    _ = ApplyStudioFoundationMetadata(draft);
                    PendingProjectInformationUpdate pending =
                        ProjectInformationSaveReconciler.CreatePendingUpdate(request, DateTimeOffset.UtcNow);
                    state.Project.Cloud.PendingProjectInformation = pending;
                    try
                    {
                        StudioCloudProjectDetail latest = await account.GetProjectAsync(
                            state.Project.Cloud.ServerProjectId);
                        state.LinkCurrentProjectToCloud(
                            latest,
                            account.Current!.ServerUrl,
                            preserveCreation: true,
                            preserveSyncState: true);
                    }
                    catch (Exception refreshError) when (
                        refreshError is StudioAccountException or HttpRequestException or TaskCanceledException)
                    {
                        state.Project.Cloud.LastSyncNote =
                            "Conflict илэрсэн боловч server snapshot refresh амжилтгүй: " + refreshError.Message;
                    }

                    string currentToken = state.Project.Cloud.ServerSnapshot.ConcurrencyToken;
                    ProjectCloudSyncMetadata.MarkConflict(
                        state.Project,
                        pending,
                        currentToken,
                        exception.Message);
                    state.SaveProject();
                    foundationEditMode = true;
                    BindFoundationFieldsToUi();
                    RefreshFoundationEditUi();
                    RefreshSyncUi();
                    StudioMessageDialog.Show(
                        Window.GetWindow(Root),
                        BuildProjectInformationConflictMessage(pending, state.Project.Cloud.ServerSnapshot),
                        "Төслийн мэдээллийн зөрчил",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    SetStatus("Cloud conflict: локал засварыг хадгаллаа. Server мэдээллийг харьцуулж дахин засна уу.");
                    return;
                }
                catch (Exception exception) when (
                    exception is StudioAccountException or HttpRequestException or TaskCanceledException)
                {
                    bool changed = FoundationDraftDiffersFromProject(draft);
                    ApplyCanonicalFoundation(draft);
                    _ = ApplyStudioFoundationMetadata(draft);
                    if (changed && state.Project.Foundation.Version <= foundationVersionBefore)
                        state.Project.Foundation.Version = foundationVersionBefore + 1;
                    state.Project.Cloud.PendingProjectInformation =
                        ProjectInformationSaveReconciler.CreatePendingUpdate(request, DateTimeOffset.UtcNow);
                    ProjectCloudSyncMetadata.MarkError(state.Project, exception.Message);
                    state.Project.Cloud.LastSyncNote =
                        "Төслийн мэдээллийн өөрчлөлт локал mirror-т хадгалагдсан бөгөөд Cloud ERA update хүлээгдэж байна.";
                    cloudUpdateQueued = true;
                    cloudSaveNotice =
                        "Өөрчлөлт локал төсөлд хадгалагдлаа. Cloud ERA шинэчлэлт амжилтгүй: " + exception.Message;
                }
            }
            else
            {
                bool changed = FoundationDraftDiffersFromProject(draft);
                ApplyFoundationDraft(draft);
                if (changed)
                    state.Project.Foundation.Version++;
            }

            if (foundationContentChanged)
            {
                ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
                    state.Project,
                    [
                        ProjectCloudSyncMetadata.CoverComponentCode,
                        ProjectCloudSyncMetadata.CompanyRegistrationComponentCode,
                        ProjectCloudSyncMetadata.CompanyLicenseComponentCode,
                        ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                    ]);
            }
            state.SaveProject();
            foundationEditMode = false;
            pendingClientLogoPath = "";
            clientLogoRemovalPending = false;
            BindProjectToUi();
            if (!linked || !HasCurrentCloudAlbumPreview())
                UpdateAlbum(silent: true, statusPrefix: "Төслийн мэдээлэл шинэчлэгдлээ");
            RefreshProjects();
            RebuildNavigation();
            SelectPage(returnPage);
            SetStatus(!string.IsNullOrWhiteSpace(cloudSaveNotice)
                ? cloudSaveNotice
                : cloudUpdateQueued
                ? "Төслийн мэдээлэл хадгалагдлаа. Cloud ERA server update бэлэн болмогц Sync-ээр илгээгдэнэ."
                : linked
                    ? "Төслийн мэдээлэл Cloud ERA болон локал mirror-т хадгалагдлаа."
                : $"Төслийн мэдээлэл хадгалагдлаа: {state.ProjectPath}");
        }
        catch (Exception exception)
        {
            SetStatus("Төслийн мэдээлэл хадгалахад алдаа: " + exception.Message);
        }
        finally
        {
            foundationSaveInProgress = false;
            RefreshFoundationEditUi();
        }
    }

    private ProjectFoundationEditDraft CaptureFoundationEditDraft()
    {
        string clientType = ProjectClientTypes.Normalize(SelectedClientType);
        string clientLogoPath = state.Project.Foundation.InitiationBasis
            .ClientOrganizationSnapshot.LogoPath;
        string clientLogoOriginalFileName = state.Project.Foundation.InitiationBasis
            .ClientOrganizationSnapshot.LogoOriginalFileName;
        if (!ProjectClientTypes.UsesLogo(clientType) || clientLogoRemovalPending)
        {
            clientLogoPath = "";
            clientLogoOriginalFileName = "";
        }
        else if (!string.IsNullOrWhiteSpace(pendingClientLogoPath))
        {
            clientLogoPath = ProjectDocumentFileStore.StoreInsideProject(
                state.ProjectPath ?? throw new InvalidOperationException("Project path is missing."),
                "client-logo",
                pendingClientLogoPath);
            clientLogoOriginalFileName = Path.GetFileName(pendingClientLogoPath);
        }

        return new ProjectFoundationEditDraft(
            projectNameBox.Text,
            basisSourceBox.Text,
            requestNumberBox.Text,
            clientType,
            clientNameBox.Text,
            clientEmailBox.Text,
            ProjectClientTypes.UsesLogo(clientType) ? clientRepresentativePositionBox.Text : "",
            ProjectClientTypes.UsesLogo(clientType) ? clientRepresentativeNameBox.Text : "",
            clientLogoPath,
            clientLogoOriginalFileName,
            siteAddressBox.Text,
            landReferenceBox.Text,
            basisSourceOrganizationBox.Text,
            basisSummaryBox.Text,
            atdNumberBox.Text,
            atdAuthorityBox.Text,
            atdStatusBox.Text,
            atdSummaryBox.Text,
            atdDocumentDrafts,
            CaptureConceptDesignApprovalDraft());
    }

    private bool FoundationDraftDiffersFromProject(ProjectFoundationEditDraft draft)
    {
        ProjectWorkspace project = state.Project;
        ProjectInitiationBasis basis = project.Foundation.InitiationBasis;
        PlanningTaskInformation atd = project.Foundation.PlanningTask;
        return !string.Equals(project.Identity.Name, draft.Name, StringComparison.Ordinal) ||
            !string.Equals(basis.SourceType, draft.BasisSourceType, StringComparison.Ordinal) ||
            !string.Equals(basis.RequestNumber, draft.RequestNumber, StringComparison.Ordinal) ||
            !string.Equals(ProjectClientTypes.Normalize(basis.ClientType), draft.ClientType, StringComparison.Ordinal) ||
            !string.Equals(basis.ClientName, draft.ClientName, StringComparison.Ordinal) ||
            !string.Equals(basis.ClientEmail, draft.ClientEmail, StringComparison.Ordinal) ||
            !string.Equals(basis.ClientRepresentativePosition, draft.ClientRepresentativePosition, StringComparison.Ordinal) ||
            !string.Equals(basis.ClientRepresentativeName, draft.ClientRepresentativeName, StringComparison.Ordinal) ||
            !string.Equals(basis.ClientOrganizationSnapshot.LogoPath, draft.ClientLogoPath, StringComparison.Ordinal) ||
            !string.Equals(basis.ClientOrganizationSnapshot.LogoOriginalFileName, draft.ClientLogoOriginalFileName, StringComparison.Ordinal) ||
            !string.Equals(basis.SiteAddress, draft.SiteAddress, StringComparison.Ordinal) ||
            !string.Equals(basis.LandReference, draft.LandReference, StringComparison.Ordinal) ||
            !string.Equals(basis.SourceOrganizationName, draft.SourceOrganizationName, StringComparison.Ordinal) ||
            !string.Equals(basis.Summary, draft.BasisSummary, StringComparison.Ordinal) ||
            !string.Equals(atd.AtdNumber, draft.AtdNumber, StringComparison.Ordinal) ||
            !string.Equals(atd.IssuingAuthorityName, draft.AtdAuthorityName, StringComparison.Ordinal) ||
            !string.Equals(atd.Status, draft.AtdStatus, StringComparison.Ordinal) ||
            !string.Equals(atd.Summary, draft.AtdSummary, StringComparison.Ordinal) ||
            AtdDocumentDraftsDifferFromProject(draft.AtdDocuments) ||
            ConceptApprovalDiffers(project.Foundation.ApprovalWorkflow, draft.ConceptDesignApproval);
    }

    private bool ApplyStudioFoundationMetadata(ProjectFoundationEditDraft draft)
    {
        ProjectInitiationBasis basis = state.Project.Foundation.InitiationBasis;
        PlanningTaskInformation atd = state.Project.Foundation.PlanningTask;
        bool changed =
            !string.Equals(basis.SourceType, draft.BasisSourceType, StringComparison.Ordinal) ||
            !string.Equals(basis.RequestNumber, draft.RequestNumber, StringComparison.Ordinal) ||
            !string.Equals(ProjectClientTypes.Normalize(basis.ClientType), draft.ClientType, StringComparison.Ordinal) ||
            !string.Equals(basis.ClientEmail, draft.ClientEmail, StringComparison.Ordinal) ||
            !string.Equals(basis.ClientRepresentativePosition, draft.ClientRepresentativePosition, StringComparison.Ordinal) ||
            !string.Equals(basis.ClientRepresentativeName, draft.ClientRepresentativeName, StringComparison.Ordinal) ||
            !string.Equals(basis.ClientOrganizationSnapshot.LogoPath, draft.ClientLogoPath, StringComparison.Ordinal) ||
            !string.Equals(basis.ClientOrganizationSnapshot.LogoOriginalFileName, draft.ClientLogoOriginalFileName, StringComparison.Ordinal) ||
            !string.Equals(basis.SourceOrganizationName, draft.SourceOrganizationName, StringComparison.Ordinal) ||
            !string.Equals(atd.AtdNumber, draft.AtdNumber, StringComparison.Ordinal) ||
            !string.Equals(atd.Status, draft.AtdStatus, StringComparison.Ordinal) ||
            !string.Equals(atd.Summary, draft.AtdSummary, StringComparison.Ordinal) ||
            AtdDocumentDraftsDifferFromProject(draft.AtdDocuments) ||
            ConceptApprovalDiffers(state.Project.Foundation.ApprovalWorkflow, draft.ConceptDesignApproval);

        basis.SourceType = draft.BasisSourceType;
        basis.RequestNumber = draft.RequestNumber;
        basis.ClientType = draft.ClientType;
        basis.ClientEmail = draft.ClientEmail;
        basis.ClientRepresentativePosition = draft.ClientRepresentativePosition;
        basis.ClientRepresentativeName = draft.ClientRepresentativeName;
        basis.SourceOrganizationName = draft.SourceOrganizationName;
        basis.ClientOrganizationSnapshot.Name = draft.ClientName;
        basis.ClientOrganizationSnapshot.DisplayName = draft.ClientName;
        basis.ClientOrganizationSnapshot.OrganizationType = draft.ClientType switch
        {
            ProjectClientTypes.GovernmentAuthority => "GovernmentAuthority",
            ProjectClientTypes.Organization => "ClientOrganization",
            _ => "Citizen",
        };
        basis.ClientOrganizationSnapshot.LogoPath = draft.ClientLogoPath;
        basis.ClientOrganizationSnapshot.LogoOriginalFileName = draft.ClientLogoOriginalFileName;
        atd.AtdNumber = draft.AtdNumber;
        atd.Status = draft.AtdStatus;
        atd.Summary = draft.AtdSummary;
        ApplyAtdDocumentDrafts(draft.AtdDocuments);
        ApplyConceptApprovalDraft(draft.ConceptDesignApproval);
        return changed;
    }

    private void ApplyCanonicalFoundation(ProjectFoundationEditDraft draft)
    {
        ProjectInitiationBasis basis = state.Project.Foundation.InitiationBasis;
        state.Project.Identity.Name = draft.Name;
        state.Project.Identity.Description = draft.BasisSummary.Trim();
        basis.ClientName = draft.ClientName;
        basis.SiteAddress = draft.SiteAddress;
        basis.Summary = draft.BasisSummary;
        state.Project.Foundation.PlanningTask.IssuingAuthorityName = draft.AtdAuthorityName;
    }

    private void ApplyFoundationDraft(ProjectFoundationEditDraft draft)
    {
        ApplyCanonicalFoundation(draft);
        state.Project.Foundation.InitiationBasis.LandReference = draft.LandReference;
        _ = ApplyStudioFoundationMetadata(draft);
    }

    private static StudioCloudProjectInformationUpdateRequest ToCloudProjectInformationRequest(
        PendingProjectInformationUpdate pending) => ProjectInformationSaveReconciler.CreateRequest(pending);

    private static string BuildCanonicalDifferenceNotice(IReadOnlyList<string> differences)
    {
        string[] labels = differences.Select(field => field switch
        {
            "Name" => "төслийн нэр",
            "ClientName" => "захиалагч",
            "PlanningAuthorityName" => "АТД олгосон байгууллага",
            "DesignOrganizationName" => "зураг төслийн байгууллага",
            "Location" => "төслийн хаяг",
            "BuildingPurpose" => "товч мэдээлэл",
            "CapacityUnit" => "хүчин чадлын нэгж",
            _ => field,
        }).ToArray();

        return "Өөрчлөлт локал төсөлд хадгалагдлаа. Cloud ERA эрх болон эх сурвалжийн " +
            $"дүрмээр дараах мэдээллийг баталгаажуулаагүй: {string.Join(", ", labels)}.";
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
            AlbumBuildResult result = TryBuildCloudUnionAlbumPreview(out AlbumBuildResult cloudUnion)
                ? cloudUnion
                : BuildLatestAlbum();
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

    private AlbumBuildResult BuildLatestAlbum(bool collectUi = true)
    {
        if (collectUi)
            CollectUiToProject();
        string outputFolder = state.ResolveOutputFolder();
        Directory.CreateDirectory(outputFolder);
        string outputPath = Path.Combine(outputFolder, $"{SafeFileName(state.Album.Title)}.pdf");
        AlbumBuildResult result = state.Builder.Build(state.CreateAlbumBuildProject(), state.Library, outputPath);
        lastAlbumPath = result.OutputPath;
        state.RecordBuiltAlbum(
            result.OutputPath,
            result.PageCount,
            "Studio generated album",
            account.Current?.Email ?? Environment.UserName);
        state.SaveProject();
        if (activePage == StudioPage.Albums)
            RefreshAlbumWorkspace(selectItemKey: selectedAlbumWorkspaceKey);
        RefreshSyncUi();
        return result;
    }

    private async Task SynchronizeCurrentProjectAsync()
    {
        if (!state.HasOpenProject || refreshingCurrentProjectAccess || syncInProgress)
            return;
        if (!await EnsureSignedInAsync())
            return;

        string projectId = state.Project.Cloud.ServerProjectId;
        bool refreshed = await RefreshCurrentProjectCloudAccessAsync(reportResult: true);
        if (!refreshed ||
            !state.HasOpenProject ||
            !state.Project.Cloud.ServerProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!CanEditProjectContent())
        {
            SetStatus("Cloud ERA өөрчлөлт татагдлаа. Таны project role локал өөрчлөлт илгээх эрхгүй байна.");
            return;
        }

        await SyncCurrentProjectAsync();
    }

    private async Task SyncCurrentProjectAsync()
    {
        if (!state.HasOpenProject || syncInProgress)
            return;
        if (!CanEditProjectContent())
        {
            SetStatus("Таны project role source package болон album publish хийх эрхгүй байна.");
            return;
        }
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
            PendingProjectInformationUpdate? pendingInformation = cloud.PendingProjectInformation;
            ProjectInformationReconciliationResult? informationReconciliation = null;
            string projectInformationNotice = "";
            StudioCloudProjectDetail canonical;
            if (pendingInformation is not null)
            {
                SetStatus("Хүлээгдэж буй төслийн мэдээллийг Cloud ERA руу илгээж байна...");
                StudioCloudProjectInformationUpdateRequest informationRequest =
                    ToCloudProjectInformationRequest(pendingInformation);
                canonical = await account.UpdateProjectInformationAsync(
                    projectId,
                    informationRequest,
                    await EnsureProjectConcurrencyTokenAsync(projectId));
                informationReconciliation = ProjectInformationSaveReconciler.Compare(
                    informationRequest,
                    canonical,
                    pendingInformation.QueuedAtUtc);
                cloud.PendingProjectInformation = informationReconciliation.PendingUpdate;
                if (!informationReconciliation.AcceptedByServer)
                    projectInformationNotice = BuildCanonicalDifferenceNotice(informationReconciliation.Differences);
            }
            else
            {
                SetStatus("Cloud ERA серверийн төслийн мэдээллийг татаж байна...");
                canonical = await account.GetProjectAsync(projectId);
            }
            state.LinkCurrentProjectToCloud(
                canonical,
                account.Current!.ServerUrl,
                preserveCreation: true,
                preserveSyncState: true);
            await ApplyCloudProjectRenderProfileAsync(canonical);
            cloud = state.Project.Cloud;
            cloud.SyncStatus = ProjectSyncStatuses.Syncing;
            state.SaveProject();

            ControlledDocumentSyncResult documentSync =
                await ReconcileAtdControlledDocumentAsync(
                    projectId,
                    canonical.Project.ConcurrencyToken,
                    allowUpload: true);
            if (documentSync.HasPendingOrConflict && !documentSync.Uploaded)
            {
                state.SaveProject();
                BindProjectToUi();
                SetStatus("Sync зогслоо: " + documentSync.Message + " Локал АТД устгагдаагүй.");
                return;
            }
            if (documentSync.Uploaded)
            {
                canonical = await account.GetProjectAsync(projectId);
                state.LinkCurrentProjectToCloud(
                    canonical,
                    account.Current!.ServerUrl,
                    preserveCreation: true,
                    preserveSyncState: true);
                await ApplyCloudProjectRenderProfileAsync(canonical);
                cloud = state.Project.Cloud;
                cloud.SyncStatus = ProjectSyncStatuses.Syncing;
                state.SaveProject();
            }

            SetStatus("Cloud ERA төслийн бүтэц болон empty template album-ыг шалгаж байна...");
            StudioCloudAlbum ensuredAlbum = await account.EnsureConceptAlbumAsync(projectId);
            IReadOnlyList<ProjectSourceSyncCandidate> sourcePackages =
                ProjectCloudSyncMetadata.PendingSourcePackages(state.Project);
            foreach (ProjectSourceSyncCandidate source in sourcePackages)
            {
                StudioCloudSourcePackage acknowledgement = await account.RegisterSourcePackageAsync(
                    projectId,
                    new StudioCloudSourcePackageCreateRequest
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
                ProjectCloudSyncMetadata.ValidateSourceAcknowledgement(
                    source.ManifestId,
                    source.ContentHash,
                    acknowledgement.ManifestId,
                    acknowledgement.ContentHash);
                ProjectCloudSyncMetadata.BindCloudOwner(
                    source.Source,
                    string.IsNullOrWhiteSpace(acknowledgement.CustodianEmail)
                        ? acknowledgement.RegisteredBy
                        : acknowledgement.CustodianEmail);
            }

            // Server-side source registration can advance the canonical
            // project version. Pull it again so the PDF is generated from the
            // exact snapshot that the upload endpoint will accept.
            canonical = await account.GetProjectAsync(projectId);
            state.LinkCurrentProjectToCloud(
                canonical,
                account.Current!.ServerUrl,
                preserveCreation: true,
                preserveSyncState: true);
            await ApplyCloudProjectRenderProfileAsync(canonical);
            BindFoundationFieldsToUi();
            string canonicalProjectToken = canonical.Project.ConcurrencyToken;
            if (state.Project.Cloud.BuildingCompositionPending)
            {
                SetStatus("Барилгын бүлэг болон хуудасны харьяаллыг Cloud ERA-д нэгтгэж байна...");
                StudioCloudBuildingCompositionUpdateRequest compositionRequest =
                    StudioBuildingCompositionSync.CreateUpdate(
                        state.Project,
                        state.Library);
                canonical = await account.UpdateBuildingCompositionAsync(
                    projectId,
                    compositionRequest,
                    canonicalProjectToken);
                ProjectCloudSyncMetadata.MarkBuildingCompositionSynced(state.Project);
                state.LinkCurrentProjectToCloud(
                    canonical,
                    account.Current!.ServerUrl,
                    preserveCreation: true,
                    preserveSyncState: true);
                await ApplyCloudProjectRenderProfileAsync(canonical);
                cloud = state.Project.Cloud;
                cloud.SyncStatus = ProjectSyncStatuses.Syncing;
                state.SaveProject();
                BindFoundationFieldsToUi();
                canonicalProjectToken = canonical.Project.ConcurrencyToken;
            }

            IReadOnlyList<ProjectSourceSyncCandidate> localSources =
                ProjectCloudSyncMetadata.SourcePackages(state.Project);
            IReadOnlyList<StudioCloudDesignPackage> designPackages =
                await account.ListDesignPackagesAsync(projectId);
            List<StudioCloudSourcePackage> activeServerSources =
                StudioCloudSourcePackageReconciliation.ActiveCanonical(
                    designPackages
                        .Where(item => item.DesignPackageType.Equals(
                            ProjectWorkspace.BuildingArchitectureConcept,
                            StringComparison.OrdinalIgnoreCase))
                        .SelectMany(item => item.SourcePackages))
                .ToList();
            List<StudioCloudSourcePackage> missingRemoteSources = activeServerSources
                .Where(server => !IsServerSourceAvailableLocally(
                    server,
                    localSources,
                    account.Current?.Email ?? ""))
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
            // Once a canonical revision has a component manifest, every later
            // contribution is a component patch. A complete local mirror must
            // not regain permission to replace the whole shared album because
            // it may still lack generated content owned by another member.
            bool canPatchCurrentRevision = currentRevision is not null &&
                HasCompleteComponentManifest(currentRevision);
            if (currentRevision is not null &&
                (canPatchCurrentRevision || missingRemoteSources.Count > 0))
            {
                bool manifestBootstrapped = false;
                if (currentRevision is not null && !HasCompleteComponentManifest(currentRevision))
                {
                    SetStatus("Хуучин Cloud album-д component manifest аюулгүй үүсгэх боломжийг шалгаж байна...");
                    StudioCloudAlbumRevision? bootstrapped =
                        await TryBootstrapAlbumComponentManifestAsync(
                            projectId,
                            serverAlbum,
                            currentRevision,
                            activeServerSources);
                    if (bootstrapped is not null)
                    {
                        currentRevision = bootstrapped;
                        canonical = await account.GetProjectAsync(projectId);
                        state.LinkCurrentProjectToCloud(
                            canonical,
                            account.Current!.ServerUrl,
                            preserveCreation: true,
                            preserveSyncState: true);
                        canonicalProjectToken = canonical.Project.ConcurrencyToken;
                        manifestBootstrapped = true;
                    }
                }
                IReadOnlyList<string> rendererMigrationCodes = currentRevision is null
                    ? []
                    : PrepareAlbumRendererMigration(currentRevision);
                int pendingComponentCount =
                    ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project).Count;
                if (sourcePackages.Count == 0 && pendingComponentCount == 0)
                {
                    syncNote = manifestBootstrapped
                        ? "Одоогийн Cloud album-ийн component manifest SHA-256 тулгалтаар баталгаажлаа. " +
                          $"{missingRemoteSources.Count} remote source хэвээр хадгалагдсан."
                        : $"{missingRemoteSources.Count} remote source энэ төхөөрөмжид байхгүй. " +
                          "Локал contribution өөрчлөгдөөгүй тул canonical album дахин upload хийгдээгүй.";
                }
                else
                {
                    if (currentRevision is null)
                    {
                        throw new InvalidOperationException(
                            "Remote source-той төсөлд component merge хийх current Cloud album алга. " +
                            "Бүх source-той төхөөрөмжөөс нэг удаа бүтэн Sync хийнэ үү.");
                    }

                    SetStatus(
                        $"{missingRemoteSources.Count} remote source локалд байхгүй. " +
                        "Зөвхөн энэ төхөөрөмжийн өөрчлөгдсөн бүрдлийг Cloud album-д merge хийж байна...");
                    AlbumComponentMergeOutcome outcome = await MergePendingAlbumComponentsAsync(
                        projectId,
                        serverAlbum,
                        currentRevision,
                        canonicalProjectToken,
                        sourcePackages,
                        activeServerSources,
                        rendererMigrationCodes);
                    currentRevision = outcome.Revision;
                    syncedAlbumHash = outcome.Revision.PdfSha256.Trim().ToLowerInvariant();
                    syncedRevisionId = outcome.Revision.RevisionId;
                    syncNote = outcome.ComponentCount == 0
                        ? $"{missingRemoteSources.Count} remote source хадгалагдсан; component өөрчлөлт байгаагүй."
                        : $"{outcome.ComponentCount} component Cloud album R{outcome.Revision.RevisionNumber}-д merge хийгдлээ. " +
                          $"Бусад {missingRemoteSources.Count} remote source хэвээр хадгалагдсан.";
                }
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
                    SetStatus(localSources.Count == 0
                        ? "Studio-ийн автомат хуудаснуудаар album revision бэлтгэж байна..."
                        : "Бүх source бүрэн байна. Studio album revision бэлтгэж байна...");
                    AlbumBuildResult build = BuildLatestAlbum(collectUi: false);
                    cloud.SyncStatus = ProjectSyncStatuses.Syncing;
                    state.SaveProject();
                    string localHash = state.Project.PrimaryAlbum.LastPdfSha256;
                    StudioCloudAlbumRevision syncedRevision;
                    List<StudioCloudAlbumSection> componentManifest =
                        CreateCanonicalComponentManifest(build, activeServerSources);
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
                            state.Project.PrimaryAlbum.LastPageSizeSummary,
                            canonicalProjectToken);
                    }
                    if (!ComponentManifestsEqual(componentManifest, syncedRevision.SectionManifest))
                    {
                        syncedRevision = await account.SetAlbumComponentManifestAsync(
                            projectId,
                            serverAlbum.AlbumId,
                            syncedRevision.RevisionId,
                            componentManifest);
                    }
                    ProjectCloudSyncMetadata.ValidateAlbumAcknowledgement(
                        localHash,
                        syncedRevision.PdfSha256,
                        syncedRevision.RevisionId);
                    foreach (ProjectSourceSyncCandidate source in sourcePackages)
                        ProjectCloudSyncMetadata.MarkSourceSynced(source);
                    ProjectCloudSyncMetadata.MarkAlbumComponentsSynced(
                        state.Project,
                        ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project));
                    syncedAlbumHash = syncedRevision.PdfSha256.Trim().ToLowerInvariant();
                    syncedRevisionId = syncedRevision.RevisionId;
                    syncNote = localSources.Count == 0
                        ? $"Studio-ийн автомат {build.PageCount} хуудастай album R{syncedRevision.RevisionNumber} sync хийгдлээ."
                        : $"Бүтэн album R{syncedRevision.RevisionNumber} sync хийгдлээ.";
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
            if (ProjectCloudSyncMetadata.PendingSourcePackages(state.Project).Count > 0 ||
                ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project).Count > 0 ||
                state.Project.Cloud.BuildingCompositionPending)
            {
                state.Project.Cloud.SyncStatus = ProjectSyncStatuses.Pending;
            }
            ProjectCloudSyncMetadata.MarkCloudRefreshed(
                state.Project,
                latest.Project.ConcurrencyToken,
                DateTimeOffset.UtcNow);
            if (informationReconciliation is { AcceptedByServer: false, PendingUpdate: not null })
            {
                ProjectCloudSyncMetadata.MarkConflict(
                    state.Project,
                    informationReconciliation.PendingUpdate,
                    latest.Project.ConcurrencyToken,
                    projectInformationNotice);
            }
            state.SaveProject();
            await TryCacheCurrentCloudAlbumPreviewAsync(projectId);
            BindProjectToUi();
            await RefreshProjectsAsync();
            if (!string.IsNullOrWhiteSpace(projectInformationNotice))
            {
                SetStatus(projectInformationNotice);
                return;
            }
            SetStatus($"Sync дууслаа: {sourcePackages.Count} source package. {syncNote}");
        }
        catch (StudioAccountException exception) when (
            exception.StatusCode is System.Net.HttpStatusCode.Conflict or
                System.Net.HttpStatusCode.PreconditionFailed)
        {
            PendingProjectInformationUpdate pending = state.Project.Cloud.PendingProjectInformation
                ?? new PendingProjectInformationUpdate { QueuedAtUtc = DateTimeOffset.UtcNow };
            try
            {
                StudioCloudProjectDetail latest = await account.GetProjectAsync(state.Project.Cloud.ServerProjectId);
                state.LinkCurrentProjectToCloud(
                    latest,
                    account.Current!.ServerUrl,
                    preserveCreation: true,
                    preserveSyncState: true);
            }
            catch (Exception refreshError) when (
                refreshError is StudioAccountException or HttpRequestException or TaskCanceledException)
            {
                state.Project.Cloud.LastSyncNote =
                    "Conflict илэрсэн боловч server snapshot refresh амжилтгүй: " + refreshError.Message;
            }

            ProjectCloudSyncMetadata.MarkConflict(
                state.Project,
                pending,
                state.Project.Cloud.ServerSnapshot.ConcurrencyToken,
                exception.Message);
            state.SaveProject();
            RefreshCloudLinkText();
            SetStatus("Sync зогслоо: server төсөл өөрчлөгдсөн. Локал засвар хадгалагдсан, Refresh хийж шийдвэрлэнэ үү.");
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
        string currentOwnerEmail)
    {
        string owner = (currentOwnerEmail ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(owner) ||
            !server.RegisteredBy.Equals(owner, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(server.SourceKey))
        {
            return localSources.Any(local =>
                local.SourceKey.Equals(server.SourceKey, StringComparison.OrdinalIgnoreCase) &&
                local.ContentHash.Equals(server.ContentHash, StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(ProjectCloudSyncMetadata.CloudOwnerEmail(local.Source)) ||
                 ProjectCloudSyncMetadata.CloudOwnerEmail(local.Source).Equals(
                     owner,
                     StringComparison.OrdinalIgnoreCase)));
        }

        if (localSources.Any(local =>
            local.ManifestId.Equals(server.ManifestId, StringComparison.OrdinalIgnoreCase) &&
            local.ContentHash.Equals(server.ContentHash, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(ProjectCloudSyncMetadata.CloudOwnerEmail(local.Source)) ||
             ProjectCloudSyncMetadata.CloudOwnerEmail(local.Source).Equals(
                 owner,
                 StringComparison.OrdinalIgnoreCase))))
        {
            return true;
        }

        // RegisteredBy is audit metadata, not evidence that this device still
        // has the source package. Treat an unverified source as remote so a
        // stale or newly-created mirror can never publish a partial full PDF.
        return false;
    }

    private async Task<string> EnsureProjectConcurrencyTokenAsync(string projectId)
    {
        string token = state.Project.Cloud.ServerSnapshot.ConcurrencyToken?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(token))
            return token;

        StudioCloudProjectDetail canonical = await account.GetProjectAsync(projectId);
        state.LinkCurrentProjectToCloud(
            canonical,
            account.Current!.ServerUrl,
            preserveCreation: true,
            preserveSyncState: true);
        token = state.Project.Cloud.ServerSnapshot.ConcurrencyToken?.Trim() ?? "";
        return !string.IsNullOrWhiteSpace(token)
            ? token
            : throw new StudioAccountException(
                "Cloud ERA server concurrency token буцаасангүй. Төслийн мэдээллийг өөрчлөхгүй.");
    }

    private static string BuildProjectInformationConflictMessage(
        PendingProjectInformationUpdate local,
        ProjectServerSnapshot server)
    {
        return
            "Энэ төслийг өөр хэрэглэгч эсвэл өөр төхөөрөмж дээр шинэчилсэн байна. " +
            "Studio server мэдээллийг дарж бичээгүй, таны локал засварыг хадгалсан.\n\n" +
            $"ЛОКАЛ\nНэр: {local.Name}\nХаяг: {local.Location}\nЗориулалт: {local.BuildingPurpose}\n\n" +
            $"SERVER\nНэр: {server.Name}\nХаяг: {server.Information.Location}\n" +
            $"Зориулалт: {server.Information.BuildingPurpose}\n\n" +
            "Засварлах төлөв нээлттэй үлдсэн. Server хувилбарыг ашиглах бол Болих, " +
            "локал өөрчлөлтөө дахин илгээх бол мэдээллээ шалгаад Хадгалах дарна уу.";
    }

    private void RefreshSyncUi()
    {
        if (!state.HasOpenProject)
        {
            cloudSyncButton.IsEnabled = false;
            return;
        }

        ProjectCloudLink cloud = state.Project.Cloud;
        bool linked = cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(cloud.ServerProjectId);
        bool busy = refreshingCurrentProjectAccess || syncInProgress;
        cloudSyncButton.IsEnabled = linked && account.IsSignedIn && !busy;
        cloudSyncButton.ToolTip = busy
            ? "Cloud ERA sync хийж байна"
            : !linked
                ? "Энэ төслийг Cloud ERA project-той холбоход sync идэвхжинэ"
                : !account.IsSignedIn
                    ? "Cloud Sync хийхийн тулд бүртгэлээрээ нэвтэрнэ үү"
                    : CanEditProjectContent()
                        ? "Cloud өөрчлөлтийг татаж merge хийгээд локал pending өөрчлөлтийг илгээнэ"
                        : "Cloud өөрчлөлтийг энэ төхөөрөмж рүү татаж шинэчилнэ";
    }

    private void BindProjectToUi()
    {
        if (!state.HasOpenProject)
        {
            return;
        }

        if (!string.Equals(
                boundAlbumProjectId,
                state.Project.ProjectId,
                StringComparison.OrdinalIgnoreCase))
        {
            ResetAlbumPreviewForProjectChange();
            boundAlbumProjectId = state.Project.ProjectId;
        }
        lastAlbumPath = ResolveCurrentProjectAlbumPath();
        foundationEditMode = false;
        var project = state.Project;
        var assignment = project.Foundation.DesignCompany;
        var company = assignment.OrganizationSnapshot;

        BindFoundationFieldsToUi();
        RefreshFoundationEditUi();

        var atd = project.Foundation.PlanningTask;
        atdParticipantsList.ItemsSource = atd.AuthorityMembers
            .Select(member => new MemberRow(
                MongolianPersonNameFormatter.ForDisplay(
                    member.FamilyName,
                    member.GivenName,
                    member.FullName),
                string.Join(", ", member.Roles),
                member.Email))
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
        RefreshProjectCompanySelectorUi();

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
        RefreshParticipantGroupSummaries();
        RefreshParticipantsList();
        if (activePage == StudioPage.Sources)
            RefreshSourceWorkspace();
        if (activePage == StudioPage.Albums)
            RefreshAlbumWorkspace();
        if (activePage is StudioPage.Reports or StudioPage.Archive)
            RefreshReportsAndArchive();
        RefreshSyncUi();
    }

    private string? ResolveCurrentProjectAlbumPath()
    {
        if (!state.HasOpenProject || string.IsNullOrWhiteSpace(state.ProjectPath) ||
            string.IsNullOrWhiteSpace(state.Project.PrimaryAlbum.LastPdfPath))
        {
            return null;
        }

        try
        {
            return ProjectWorkspacePaths.ResolveInsideProject(
                state.ProjectPath,
                state.Project.PrimaryAlbum.LastPdfPath);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private string? ResolveLastReceivedCloudAlbumPath()
    {
        if (!state.HasOpenProject || string.IsNullOrWhiteSpace(state.ProjectPath))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(state.Project.Cloud.LastReceivedAlbumPdfPath))
        {
            // Backward compatibility: older mirrors used the primary album
            // pointer for the canonical Cloud cache. Keep it readable until
            // the next Cloud check records the dedicated canonical pointer.
            string? legacyPath = ResolveCurrentProjectAlbumPath();
            string cloudCacheFolder = Path.Combine(state.ResolveOutputFolder(), "cloud");
            return !string.IsNullOrWhiteSpace(state.Project.Cloud.LastReceivedAlbumRevisionId) &&
                !string.IsNullOrWhiteSpace(legacyPath) &&
                ProjectWorkspacePaths.IsInside(cloudCacheFolder, legacyPath)
                    ? legacyPath
                    : null;
        }

        try
        {
            return ProjectWorkspacePaths.ResolveInsideProject(
                state.ProjectPath,
                state.Project.Cloud.LastReceivedAlbumPdfPath);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private bool HasCurrentCloudAlbumPreview()
    {
        if (!state.HasOpenProject ||
            !state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(state.Project.Cloud.LastReceivedAlbumRevisionId))
        {
            return false;
        }

        string? albumPath = ResolveLastReceivedCloudAlbumPath();
        string cloudCacheFolder = Path.Combine(state.ResolveOutputFolder(), "cloud");
        return CloudAlbumCacheMaintenance.IsPresent(cloudCacheFolder, albumPath);
    }

    private bool CloudMirrorNeedsFullRefresh()
    {
        if (!state.HasOpenProject || state.Project.Cloud.LastCloudRefreshedAtUtc is null)
            return true;

        ProjectCloudLink cloud = state.Project.Cloud;
        string? albumPath = ResolveCurrentProjectAlbumPath();
        string? canonicalAlbumPath = ResolveLastReceivedCloudAlbumPath();
        string cloudCacheFolder = Path.Combine(state.ResolveOutputFolder(), "cloud");
        bool legacyCloudAlbumPointer =
            string.IsNullOrWhiteSpace(cloud.LastReceivedAlbumPdfPath) &&
            !string.IsNullOrWhiteSpace(albumPath) &&
            ProjectWorkspacePaths.IsInside(cloudCacheFolder, albumPath);
        if (legacyCloudAlbumPointer ||
            (!string.IsNullOrWhiteSpace(cloud.LastReceivedAlbumRevisionId) &&
             (!CloudAlbumCacheMaintenance.IsPresent(cloudCacheFolder, canonicalAlbumPath) ||
              !HasCurrentCloudAlbumPreview())))
        {
            return true;
        }

        CompanyProfile client = state.Project.Foundation.InitiationBasis.ClientOrganizationSnapshot;
        if (!string.IsNullOrWhiteSpace(cloud.LastReceivedClientLogoKey) &&
            !CachedProjectLogoIsCurrent(
                client.LogoPath,
                cloud.LastReceivedClientLogoKey,
                cloud.LastReceivedClientLogoKey))
        {
            return true;
        }

        CompanyProfile design = state.Project.Foundation.DesignCompany.OrganizationSnapshot;
        return !string.IsNullOrWhiteSpace(cloud.LastReceivedDesignOrganizationLogoKey) &&
            !CachedProjectLogoIsCurrent(
                design.LogoPath,
                cloud.LastReceivedDesignOrganizationLogoKey,
                cloud.LastReceivedDesignOrganizationLogoKey);
    }

    private async Task<bool> TryCacheCurrentCloudAlbumPreviewAsync(
        string projectId,
        IReadOnlyList<StudioCloudAlbum>? knownAlbums = null) =>
        (await RefreshCloudAlbumPreviewAsync(projectId, knownAlbums)).HasCurrentAlbum;

    private async Task<CloudAlbumCacheRefreshResult> RefreshCloudAlbumPreviewAsync(
        string projectId,
        IReadOnlyList<StudioCloudAlbum>? knownAlbums = null)
    {
        if (!state.HasOpenProject ||
            string.IsNullOrWhiteSpace(state.ProjectPath) ||
            string.IsNullOrWhiteSpace(projectId) ||
            !account.IsSignedIn)
        {
            return CloudAlbumCacheRefreshResult.None;
        }

        try
        {
            IReadOnlyList<StudioCloudAlbum> albums = knownAlbums ?? await account.ListAlbumsAsync(projectId);
            StudioCloudAlbum? album = albums.FirstOrDefault(item =>
                    item.AlbumType.Equals(
                        ProjectWorkspace.BuildingArchitectureConcept,
                        StringComparison.OrdinalIgnoreCase))
                ?? albums.FirstOrDefault();
            StudioCloudAlbumRevision? revision = album is null
                ? null
                : CurrentCloudAlbumRevision(album);
            if (album is null ||
                revision is null ||
                string.IsNullOrWhiteSpace(revision.PdfFileId) ||
                revision.PageCount < 1)
            {
                ClearCloudAlbumPreviewCache();
                return CloudAlbumCacheRefreshResult.None;
            }

            string outputPath = CloudAlbumPreviewPath(album, revision);
            string relativePath = ProjectWorkspacePaths.ToRelativePath(state.ProjectPath, outputPath);
            string expectedSha256 = CleanSha256(revision.PdfSha256);
            ProjectCloudLink cloud = state.Project.Cloud;
            bool trustedCache = TryGetTrustedCloudAlbumSha256(
                outputPath,
                relativePath,
                revision,
                expectedSha256,
                cloud,
                out string actualSha256);
            bool download = !File.Exists(outputPath);

            if (!download && !trustedCache)
            {
                actualSha256 = await Task.Run(() => ComputeFileSha256(outputPath));
                download = !string.IsNullOrWhiteSpace(expectedSha256) &&
                    !actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
            }

            if (download)
            {
                SetStatus($"Cloud ERA album PDF татаж байна: R{revision.RevisionNumber}...");
                await account.DownloadAlbumRevisionPdfAsync(revision, outputPath);
                actualSha256 = await Task.Run(() => ComputeFileSha256(outputPath));
            }

            if (string.IsNullOrWhiteSpace(actualSha256))
                actualSha256 = await Task.Run(() => ComputeFileSha256(outputPath));
            if (!string.IsNullOrWhiteSpace(expectedSha256) &&
                !actualSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("Downloaded Cloud ERA album PDF hash did not match the server revision.");
            }

            ProjectCloudSyncMetadata.RecordReceivedAlbum(
                state.Project,
                revision.RevisionId,
                revision.RevisionNumber,
                actualSha256,
                relativePath);
            revision.PdfSha256 = actualSha256;
            cloud.LastSyncedAlbumSha256 = actualSha256;
            cloud.LastSyncedRevisionId = revision.RevisionId?.Trim() ?? "";
            cloud.LastServerConcurrencyToken = FirstNonEmpty(
                cloud.ServerSnapshot.ConcurrencyToken,
                cloud.LastServerConcurrencyToken);
            bool hasPendingLocalWork = cloud.PendingProjectInformation is not null ||
                ProjectCloudSyncMetadata.PendingSourcePackages(state.Project).Count > 0 ||
                ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project).Count > 0 ||
                cloud.BuildingCompositionPending;
            bool hasPendingAlbumWork =
                ProjectCloudSyncMetadata.PendingSourcePackages(state.Project).Count > 0 ||
                ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project).Count > 0 ||
                cloud.BuildingCompositionPending;
            bool usingUnionPreview = hasPendingAlbumWork &&
                TryBuildCloudUnionAlbumPreview(outputPath, revision, out _);
            if (!usingUnionPreview)
                _ = PointPrimaryAlbumAtCanonical(outputPath, revision);
            if (!hasPendingLocalWork &&
                !cloud.SyncStatus.Equals(ProjectSyncStatuses.Conflict, StringComparison.OrdinalIgnoreCase))
            {
                cloud.SyncStatus = ProjectSyncStatuses.Synced;
                cloud.LastSyncError = "";
                if (string.IsNullOrWhiteSpace(cloud.LastSyncNote))
                    cloud.LastSyncNote = $"Cloud ERA album R{revision.RevisionNumber} preview cached locally.";
            }

            _ = CloudAlbumCacheMaintenance.Cleanup(
                Path.Combine(state.ResolveOutputFolder(), "cloud"),
                outputPath);
            state.SaveProject();
            if (activePage == StudioPage.Albums)
                RefreshAlbumWorkspace(selectItemKey: selectedAlbumWorkspaceKey);
            RefreshSyncUi();
            return new CloudAlbumCacheRefreshResult(
                true,
                download,
                revision.RevisionId?.Trim() ?? "",
                revision.RevisionNumber,
                actualSha256);
        }
        catch (Exception exception) when (
            exception is StudioAccountException or
                HttpRequestException or
                IOException or
                UnauthorizedAccessException or
                InvalidDataException or
                TaskCanceledException)
        {
            SetStatus("Cloud ERA album PDF cache алдаа: " + exception.Message);
            return CloudAlbumCacheRefreshResult.None;
        }
    }

    private static bool TryGetTrustedCloudAlbumSha256(
        string outputPath,
        string relativePath,
        StudioCloudAlbumRevision revision,
        string expectedSha256,
        ProjectCloudLink cloud,
        out string actualSha256)
    {
        actualSha256 = "";
        if (!File.Exists(outputPath) ||
            !cloud.LastReceivedAlbumPdfPath.Equals(relativePath, StringComparison.OrdinalIgnoreCase) ||
            !cloud.LastReceivedAlbumRevisionId.Equals(
                revision.RevisionId,
                StringComparison.OrdinalIgnoreCase) ||
            cloud.LastReceivedAlbumRevisionNumber != revision.RevisionNumber)
        {
            return false;
        }

        string receivedSha256 = CleanSha256(cloud.LastReceivedAlbumSha256);
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            if (!receivedSha256.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            actualSha256 = expectedSha256;
            return true;
        }

        if (string.IsNullOrWhiteSpace(receivedSha256))
            return false;

        actualSha256 = receivedSha256;
        return true;
    }

    private void ClearCloudAlbumPreviewCache()
    {
        if (!state.HasOpenProject || string.IsNullOrWhiteSpace(state.ProjectPath))
            return;

        string cacheFolder = Path.Combine(state.ResolveOutputFolder(), "cloud");
        string localPreviewFolder = Path.Combine(state.ResolveOutputFolder(), "cloud-local");
        string? currentPath = ResolveCurrentProjectAlbumPath();
        bool currentAlbumUsesCloudCache = !string.IsNullOrWhiteSpace(currentPath) &&
            (ProjectWorkspacePaths.IsInside(cacheFolder, currentPath) ||
             ProjectWorkspacePaths.IsInside(localPreviewFolder, currentPath));
        bool metadataChanged = currentAlbumUsesCloudCache ||
            !string.IsNullOrWhiteSpace(state.Project.Cloud.LastReceivedAlbumRevisionId) ||
            state.Project.Cloud.LastReceivedAlbumRevisionNumber > 0 ||
            !string.IsNullOrWhiteSpace(state.Project.Cloud.LastReceivedAlbumSha256) ||
            !string.IsNullOrWhiteSpace(state.Project.Cloud.LastReceivedAlbumPdfPath);
        if (currentAlbumUsesCloudCache)
        {
            ProjectAlbumRecord album = state.Project.PrimaryAlbum;
            album.LastPdfPath = "";
            album.LastPdfSha256 = "";
            album.LastPageCount = 0;
            album.LastPageSizeSummary = "";
            lastAlbumPath = null;
        }

        ProjectCloudSyncMetadata.ClearReceivedAlbum(state.Project);
        int cleanedFiles = CloudAlbumCacheMaintenance.Cleanup(cacheFolder, keepPdfPath: null);
        cleanedFiles += CloudAlbumCacheMaintenance.Cleanup(localPreviewFolder, keepPdfPath: null);
        if (metadataChanged || cleanedFiles > 0)
            state.SaveProject();
        if (activePage == StudioPage.Albums)
            RefreshAlbumWorkspace(selectItemKey: selectedAlbumWorkspaceKey);
    }

    private void CleanupCurrentCloudAlbumCache()
    {
        if (!state.HasOpenProject)
            return;
        string cacheFolder = Path.Combine(state.ResolveOutputFolder(), "cloud");
        string localPreviewFolder = Path.Combine(state.ResolveOutputFolder(), "cloud-local");
        string? canonical = ResolveLastReceivedCloudAlbumPath();
        string? keepCanonical = !string.IsNullOrWhiteSpace(canonical) &&
            File.Exists(canonical) &&
            ProjectWorkspacePaths.IsInside(cacheFolder, canonical)
                ? canonical
                : null;
        string? current = ResolveCurrentProjectAlbumPath();
        string? keepLocalPreview = !string.IsNullOrWhiteSpace(current) &&
            File.Exists(current) &&
            ProjectWorkspacePaths.IsInside(localPreviewFolder, current)
                ? current
                : null;
        CloudAlbumCacheMaintenance.Cleanup(cacheFolder, keepCanonical);
        CloudAlbumCacheMaintenance.Cleanup(localPreviewFolder, keepLocalPreview);
    }

    private sealed record CloudAlbumCacheRefreshResult(
        bool HasCurrentAlbum,
        bool Downloaded,
        string RevisionId,
        int RevisionNumber,
        string Sha256)
    {
        public static CloudAlbumCacheRefreshResult None { get; } = new(false, false, "", 0, "");
    }

    private string CloudAlbumPreviewPath(StudioCloudAlbum album, StudioCloudAlbumRevision revision)
    {
        string revisionId = revision.RevisionId?.Trim() ?? "";
        string revisionSegment = string.IsNullOrWhiteSpace(revisionId)
            ? revision.RevisionNumber.ToString()
            : revisionId[..Math.Min(8, revisionId.Length)];
        return Path.Combine(
            state.ResolveOutputFolder(),
            "cloud",
            $"{SafeFileName(album.Title)}-R{revision.RevisionNumber}-{SafeFileName(revisionSegment)}.pdf");
    }

    private static StudioCloudAlbumRevision? CurrentCloudAlbumRevision(StudioCloudAlbum album) =>
        album.Revisions.FirstOrDefault(item =>
                item.RevisionId.Equals(album.CurrentRevisionId, StringComparison.OrdinalIgnoreCase))
            ?? album.Revisions.OrderByDescending(item => item.RevisionNumber).FirstOrDefault();

    private static string ComputeFileSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string CleanSha256(string? value) => value?.Trim().ToLowerInvariant() ?? "";

    private void BindFoundationFieldsToUi()
    {
        if (!state.HasOpenProject)
            return;

        ProjectWorkspace project = state.Project;
        ProjectInitiationBasis basis = project.Foundation.InitiationBasis;
        PlanningTaskInformation atd = project.Foundation.PlanningTask;
        projectNameBox.Text = project.Name;
        projectCodeBox.Text = project.Code;
        basisSourceBox.Text = basis.SourceType;
        requestNumberBox.Text = basis.RequestNumber;
        SelectClientType(ProjectClientTypes.ResolveStoredType(
            basis.ClientType,
            basis.ClientOrganizationSnapshot));
        clientNameBox.Text = basis.ClientName;
        clientEmailBox.Text = basis.ClientEmail;
        clientRepresentativePositionBox.Text = basis.ClientRepresentativePosition;
        clientRepresentativeNameBox.Text = basis.ClientRepresentativeName;
        pendingClientLogoPath = "";
        clientLogoRemovalPending = false;
        RefreshClientLogoEditor();
        siteAddressBox.Text = basis.SiteAddress;
        landReferenceBox.Text = basis.LandReference;
        basisSourceOrganizationBox.Text = basis.SourceOrganizationName;
        basisSummaryBox.Text = basis.Summary;
        atdNumberBox.Text = atd.AtdNumber;
        atdAuthorityBox.Text = atd.IssuingAuthorityName;
        atdStatusBox.Text = atd.Status;
        atdSummaryBox.Text = atd.Summary;
        BindAtdDocumentsFromProject();
        BindConceptApprovalEditor();
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
        basis.ClientType = ProjectClientTypes.Normalize(SelectedClientType);
        basis.ClientName = clientNameBox.Text.Trim();
        basis.ClientEmail = clientEmailBox.Text.Trim();
        basis.ClientRepresentativePosition = ProjectClientTypes.UsesLogo(basis.ClientType)
            ? clientRepresentativePositionBox.Text.Trim()
            : "";
        basis.ClientRepresentativeName = ProjectClientTypes.UsesLogo(basis.ClientType)
            ? clientRepresentativeNameBox.Text.Trim()
            : "";
        basis.ClientOrganizationSnapshot.Name = basis.ClientName;
        basis.ClientOrganizationSnapshot.DisplayName = basis.ClientName;
        basis.SiteAddress = siteAddressBox.Text.Trim();
        basis.LandReference = landReferenceBox.Text.Trim();
        basis.SourceOrganizationName = basisSourceOrganizationBox.Text.Trim();
        basis.Summary = basisSummaryBox.Text;

        var atd = project.Foundation.PlanningTask;
        atd.AtdNumber = atdNumberBox.Text.Trim();
        atd.IssuingAuthorityName = atdAuthorityBox.Text.Trim();
        atd.Status = atdStatusBox.Text.Trim();
        atd.Summary = atdSummaryBox.Text;
        ApplyAtdDocumentDrafts();

        state.Album.Title = string.IsNullOrWhiteSpace(albumTitleBox.Text)
            ? "Барилга архитектурын загвар зургийн альбум"
            : albumTitleBox.Text.Trim();
    }

    private void RefreshFoundationEditUi()
    {
        bool hasProject = state.HasOpenProject;
        bool canEdit = hasProject && CanEditProjectInformation();
        bool editing = foundationEditMode && canEdit;
        bool fieldsEditable = editing && !foundationSaveInProgress;
        bool linked = hasProject && state.Project.Cloud.Origin.Equals(
            ProjectOrigins.Cloud,
            StringComparison.OrdinalIgnoreCase);

        foundationEditButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        foundationSaveButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        foundationCancelButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        participantsEditButton.Visibility = editing ? Visibility.Collapsed : Visibility.Visible;
        participantsSaveButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        participantsCancelButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        foundationEditButton.IsEnabled = canEdit && !foundationSaveInProgress;
        foundationSaveButton.IsEnabled = fieldsEditable;
        foundationCancelButton.IsEnabled = editing && !foundationSaveInProgress;
        participantsEditButton.IsEnabled = canEdit && !foundationSaveInProgress;
        participantsSaveButton.IsEnabled = fieldsEditable;
        participantsCancelButton.IsEnabled = editing && !foundationSaveInProgress;
        foundationEditButton.ToolTip = !hasProject
            ? "Төсөл нээгээгүй байна"
            : !canEdit
                ? "Таны project role төслийн мэдээлэл засварлах эрхгүй байна"
                : "Төслийн мэдээллийг засварлах";
        participantsEditButton.ToolTip = !hasProject
            ? "Төсөл нээгээгүй байна"
            : !canEdit
                ? "Таны project role төслийн оролцогчдын мэдээлэл засварлах эрхгүй байна"
                : "Захиалагчийн төлөөлөгч болон баталгаажуулалтын оролцогчдыг засварлах";

        foreach (var box in new[]
                 {
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
            box.IsReadOnly = !fieldsEditable;
        }

        projectCodeBox.IsReadOnly = true;
        projectCodeBox.ToolTip = "Төслийн код нь төслийн тогтвортой таних тэмдэг тул эндээс солигдохгүй.";
        landReferenceBox.IsReadOnly = !fieldsEditable || linked;
        landReferenceBox.ToolTip = linked
            ? "Газрын мэдээлэл Cloud ERA дахь эрх бүхий эх сурвалжаас шинэчлэгдэнэ."
            : null;
        atdAddDocumentsButton.IsEnabled = fieldsEditable;
        atdRemoveDocumentButton.IsEnabled = fieldsEditable && atdDocumentsList.SelectedItem is not null;
        clientTypeBox.IsEnabled = fieldsEditable;
        RefreshClientLogoEditor();
        RefreshConceptApprovalEditorUi();
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

    private void RefreshParticipantGroupSummaries()
    {
        if (!state.HasOpenProject)
        {
            clientParticipantsSummaryText.Text = "Төсөл нээгээгүй байна.";
            designParticipantsSummaryText.Text = "Төсөл нээгээгүй байна.";
            return;
        }

        ProjectInitiationBasis basis = state.Project.Foundation.InitiationBasis;
        clientParticipantsSummaryText.Text =
            $"{ProjectClientTypes.DisplayName(basis.ClientType)} · {ValueOrDash(basis.ClientName)}";

        CompanyProfile company = state.Project.Foundation.DesignCompany.OrganizationSnapshot;
        designParticipantsSummaryText.Text =
            $"Зураг төсөл боловсруулагч байгууллага · {ValueOrDash(CompanyDisplayName(company))}\n" +
            ProjectCompanyAssignmentDescription(state.Project);
    }

    private void RefreshParticipantsList(bool refreshCloud = false)
    {
        BindParticipantRows(ActiveProjectMemberRows());
        RefreshProjectArchitectUi();
        RefreshTeamActionUi();
        if (refreshCloud && account.IsSignedIn &&
            state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId))
        {
            _ = RefreshProjectTeamAsync();
        }
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

    private void OnAssetSourcesChanged()
    {
        if (!state.HasOpenProject)
            return;
        if (suppressAutomaticAlbumRebuild || syncInProgress)
            return;
        try
        {
            CityGenProjectSiteReconciliationResult siteResult =
                state.ReconcileCityGenProjectSite();
            if (siteResult.Changed)
                SetStatus(siteResult.Message);
        }
        catch (Exception exception)
        {
            SetStatus($"CityGen төслийн талбай шинэчлэхэд алдаа: {exception.Message}");
            return;
        }
        if (autoRebuildCheck.IsChecked == true)
        {
            SetStatus("Холбосон PDF/зураг өөрчлөгдлөө. Альбумыг шинэчилж байна...");
            autoRebuildTimer.Stop();
            autoRebuildTimer.Start();
        }
        else
        {
            SetStatus("Холбосон PDF/зураг өөрчлөгдсөн байна. Альбумын 'Эх үүсвэрээс шинэчлэх' үйлдлийг ажиллуулна уу.");
        }
    }

    private void OnLibraryChanged()
    {
        if (!state.HasOpenProject)
        {
            return;
        }
        if (activePage == StudioPage.Sources)
            RefreshSourceWorkspace();
        if (activePage == StudioPage.Albums)
            RefreshAlbumWorkspace();
        if (suppressAutomaticAlbumRebuild || syncInProgress)
            return;
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
            if (activePage == StudioPage.Sources)
                RefreshSourceWorkspace(recorded.SourceId);
            if (activePage == StudioPage.Albums)
                RefreshAlbumWorkspace();
            RefreshSyncUi();
        }
        if (!result.IsLossless)
        {
            SetStatus($"Rejected package: {Path.GetFileName(result.ManifestPath)} - {string.Join("; ", result.Issues.Take(2))}");
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

    private static string FirstNonEmpty(string? primary, string? fallback) =>
        !string.IsNullOrWhiteSpace(primary)
            ? primary.Trim()
            : fallback?.Trim() ?? "";

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
            bool isCloudOnly,
            bool currentUserIsCreator,
            IReadOnlyList<string> currentUserScopes)
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
            CurrentUserIsCreator = currentUserIsCreator;
            CurrentUserScopes = currentUserScopes.ToArray();
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
        public bool CurrentUserIsCreator { get; }
        public IReadOnlyList<string> CurrentUserScopes { get; }
        public bool CanDelete =>
            CurrentUserIsCreator &&
            !string.IsNullOrWhiteSpace(ServerProjectId) &&
            CurrentUserScopes.Any(scope => scope.Equals("project.delete", StringComparison.OrdinalIgnoreCase));
        public bool CanLeave =>
            !CurrentUserIsCreator &&
            !string.IsNullOrWhiteSpace(ServerProjectId) &&
            CurrentUserScopes.Any(scope => scope.Equals("project.leave", StringComparison.OrdinalIgnoreCase));
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

    private sealed record ClientTypeOption(string Label, string Value)
    {
        public override string ToString() => Label;
    }

    private sealed record MemberRow(
        string Name,
        string Roles,
        string Email,
        string Identifier = "",
        string Status = "Идэвхтэй",
        bool IsInvitation = false,
        string[]? RoleCodes = null);
    private sealed record ReportRow(string Type, string Title, string Status, int Version);
    private sealed record ArchiveRow(string Type, string Title, string Status, string ArchivedAt);
}
