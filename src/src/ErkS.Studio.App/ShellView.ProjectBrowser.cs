using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Files the project list into folders rather than one growing wall of cards.
/// The first level is the design organization, because that is how a practice
/// keeps its work; inside one, the projects are gathered by design stage.
/// </summary>
internal sealed partial class ShellView
{
    private string projectBrowserOrganization = "";
    private bool projectBrowserListLayout;
    private readonly TextBlock projectsSectionHeading = new();
    private readonly Dictionary<string, ImageSource> partnerOrganizationLogos =
        new(StringComparer.CurrentCultureIgnoreCase);
    private readonly HashSet<string> partnerOrganizationLogoAttempts =
        new(StringComparer.CurrentCultureIgnoreCase);

    private readonly Button projectBrowserBackButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Байгууллагууд",
        "Байгууллагын жагсаалт руу буцах");
    // Inside a folder the organization is what the page is about, so it is shown
    // as the page's own banner instead of a small word beside a back button.
    private readonly Border projectBrowserBanner = new()
    {
        Background = StudioTheme.PanelBrush,
        BorderBrush = StudioTheme.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = new Thickness(18, 14, 18, 14),
        Margin = new Thickness(0, 0, 0, 18),
        Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock projectBrowserBannerName = new()
    {
        FontSize = 21,
        FontWeight = FontWeights.SemiBold,
        Foreground = StudioTheme.TextBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock projectBrowserBannerCount = new()
    {
        FontSize = 12.5,
        Foreground = StudioTheme.MutedTextBrush,
        Margin = new Thickness(0, 3, 0, 0),
    };
    private readonly Image projectBrowserBannerLogo = new()
    {
        Stretch = Stretch.Uniform,
        Width = 54,
        Height = 54,
        Margin = new Thickness(0, 0, 14, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border projectBrowserBannerCrest = new()
    {
        Width = 54,
        Height = 54,
        CornerRadius = new CornerRadius(27),
        Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
        Margin = new Thickness(0, 0, 14, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock projectBrowserBannerMonogram = new()
    {
        FontSize = 19,
        FontWeight = FontWeights.SemiBold,
        Foreground = StudioTheme.TextBrush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Button projectBrowserLayoutButton = StudioWidgets.CreateGlyphButton(
        "",
        "Жагсаалт болон хавтангийн харагдацыг сэлгэх");

    /// <summary>One design organization, shown as a folder of its projects.</summary>
    private sealed class ProjectFolderRow
    {
        public required string Company { get; init; }

        public required int ProjectCount { get; init; }

        public ImageSource? LogoSource { get; init; }

        public string CountLabel => $"{ProjectCount} төсөл";

        public string Monogram => StudioOrganizationCrest.Initials(Company);

        public Visibility InitialsVisibility => LogoSource is null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// The organization's own heading for the page: back on the left, then the
    /// logo and name together, so it is plain whose projects are on screen.
    /// </summary>
    private UIElement BuildProjectBrowserBanner()
    {
        // A grid, not a dock: the name is centred on the banner itself rather
        // than on whatever space the back button leaves behind.
        var layout = new Grid();
        projectBrowserBackButton.VerticalAlignment = VerticalAlignment.Center;
        projectBrowserBackButton.HorizontalAlignment = HorizontalAlignment.Left;
        projectBrowserBackButton.Margin = new Thickness(0);

        var identity = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var crest = new Grid { VerticalAlignment = VerticalAlignment.Center };
        projectBrowserBannerCrest.Child = projectBrowserBannerMonogram;
        crest.Children.Add(projectBrowserBannerCrest);
        crest.Children.Add(projectBrowserBannerLogo);
        identity.Children.Add(crest);
        var naming = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        naming.Children.Add(projectBrowserBannerName);
        naming.Children.Add(projectBrowserBannerCount);
        identity.Children.Add(naming);
        // The name is laid out first and the button drawn over it, so a long
        // organization name is trimmed rather than pushed under the button.
        identity.Margin = new Thickness(140, 0, 140, 0);
        layout.Children.Add(identity);
        layout.Children.Add(projectBrowserBackButton);

        projectBrowserBanner.Child = layout;
        return projectBrowserBanner;
    }

    private void InitializeProjectBrowser()
    {
        projectBrowserBackButton.Click += (_, _) =>
        {
            projectBrowserOrganization = "";
            ApplyProjectFilter();
        };
        projectBrowserLayoutButton.Click += (_, _) =>
        {
            projectBrowserListLayout = !projectBrowserListLayout;
            ApplyProjectFilter();
        };
    }

    /// <summary>
    /// The rows the list shows: organization folders at the top level, that
    /// organization's projects once one is open.
    /// </summary>
    private IReadOnlyList<object> BuildProjectBrowserItems(IReadOnlyList<ProjectRow> rows)
    {
        if (projectBrowserOrganization.Length == 0)
        {
            return rows
                .GroupBy(row => row.CompanyLabel, StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(group => group.Key, StringComparer.CurrentCulture)
                .Select(group => (object)new ProjectFolderRow
                {
                    Company = group.Key,
                    ProjectCount = group.Count(),
                    LogoSource = ResolveOrganizationLogo(group.Key),
                })
                .ToList();
        }

        return rows
            .Where(row => row.CompanyLabel.Equals(
                projectBrowserOrganization,
                StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(row => row.Stage, StringComparer.CurrentCulture)
            .ThenBy(row => row.Code, StringComparer.CurrentCulture)
            .Cast<object>()
            .ToList();
    }

    private ImageSource? ResolveOrganizationLogo(string company)
    {
        string name = (company ?? "").Trim();
        if (name.Length == 0)
            return null;

        CompanyProfile? profile = companyEntries
            .Select(entry => entry.Profile)
            .FirstOrDefault(candidate =>
                candidate.Name.Trim().Equals(name, StringComparison.CurrentCultureIgnoreCase) ||
                candidate.DisplayName.Trim().Equals(name, StringComparison.CurrentCultureIgnoreCase));
        ImageSource? own = LoadLogoImage(profile?.LogoPath);
        if (own is not null)
            return own;

        // A practice this account does not belong to still has a logo, and its
        // projects are on this screen. It is fetched through one of those
        // projects, which is exactly the right to see it.
        return partnerOrganizationLogos.TryGetValue(name, out ImageSource? partner) ? partner : null;
    }

    /// <summary>
    /// Fetches the logo of every organization on screen that this account has
    /// no company record for, once per name. Each fetch goes through a project
    /// of that organization, so the server grants it on project membership.
    /// </summary>
    private async Task EnsurePartnerOrganizationLogosAsync(IReadOnlyList<ProjectRow> rows)
    {
        if (!account.IsSignedIn)
            return;

        var wanted = rows
            .Where(row => row.ServerProjectId.Length > 0)
            .GroupBy(row => row.CompanyLabel, StringComparer.CurrentCultureIgnoreCase)
            .Where(group =>
                !partnerOrganizationLogoAttempts.Contains(group.Key) &&
                ResolveOrganizationLogo(group.Key) is null)
            .ToList();
        if (wanted.Count == 0)
            return;

        bool changed = false;
        foreach (var group in wanted)
        {
            partnerOrganizationLogoAttempts.Add(group.Key);
            foreach (ProjectRow row in group)
            {
                ImageSource? logo = await DownloadPartnerLogoAsync(row.ServerProjectId);
                if (logo is null)
                    continue;

                partnerOrganizationLogos[group.Key] = logo;
                changed = true;
                break;
            }
        }

        if (changed)
            ApplyProjectFilter();
    }

    private async Task<ImageSource?> DownloadPartnerLogoAsync(string serverProjectId)
    {
        try
        {
            StudioDownloadedImage? image = await account.GetOrganizationLogoAsync(
                "/api/cloud-era/v1/projects/" +
                Uri.EscapeDataString(serverProjectId) +
                "/design-organization/logo");
            return image is null ? null : DecodeLogo(image.Bytes);
        }
        catch (StudioAccountException)
        {
            return null;
        }
    }

    private static ImageSource? DecodeLogo(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.DecodePixelHeight = 96;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (exception is NotSupportedException or IOException)
        {
            return null;
        }
    }

    /// <summary>A logo scaled for a card, or null when there is none to show.</summary>
    private static ImageSource? LoadLogoImage(string? logoPath)
    {
        string path = (logoPath ?? "").Trim();
        if (path.Length == 0 || !File.Exists(path))
            return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(Path.GetFullPath(path), UriKind.Absolute);
            bitmap.DecodePixelHeight = 96;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or UriFormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Applies the browser's own view state to the list: which template each row
    /// gets, and whether the projects of an organization are gathered by stage.
    /// </summary>
    private void ApplyProjectBrowserView(IReadOnlyList<object> items)
    {
        bool folderLevel = projectBrowserOrganization.Length == 0;
        projectBrowserBanner.Visibility = folderLevel
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (!folderLevel)
        {
            projectBrowserBannerName.Text = projectBrowserOrganization;
            projectBrowserBannerCount.Text = $"{items.Count} төсөл";
            ImageSource? logo = ResolveOrganizationLogo(projectBrowserOrganization);
            projectBrowserBannerLogo.Source = logo;
            projectBrowserBannerMonogram.Text =
                StudioOrganizationCrest.Initials(projectBrowserOrganization);
            projectBrowserBannerCrest.Visibility = logo is null
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        // Inside a folder the organization's banner is the heading; keeping the
        // library heading as well crowded the two into each other.
        projectsSectionHeading.Text = "Байгууллагууд";
        projectsSectionHeading.Visibility = folderLevel
            ? Visibility.Visible
            : Visibility.Collapsed;
        projectsSummaryText.Visibility = folderLevel
            ? Visibility.Visible
            : Visibility.Collapsed;

        bool listLayout = projectBrowserListLayout && !folderLevel;
        // A card container is a fixed 292x282 tile. Left in place it turned the
        // list rows into a column of empty tiles.
        projectsList.ItemContainerStyle = listLayout
            ? CreateProjectListItemStyle()
            : CreateProjectCardItemStyle();

        projectsList.GroupStyle.Clear();
        if (folderLevel)
        {
            // The top level is one card per organization; there is nothing to
            // gather them by.
            projectsList.ItemsPanel = CreateProjectItemsPanel();
            projectsList.ItemsSource = items;
            return;
        }

        // Inside a folder the projects are gathered by design stage in both
        // layouts — that grouping is the reason to open a folder at all.
        // With grouping on, GroupStyle.Panel lays out the stage headings and
        // ItemsPanel lays out the projects inside one heading; measured, not
        // assumed — the two were the wrong way round and the stages came out
        // side by side.
        projectsList.ItemsPanel = listLayout
            ? CreateProjectRowsPanel()
            : CreateProjectItemsPanel();
        projectsList.GroupStyle.Add(CreateProjectStageGroupStyle());
        var grouped = new CollectionViewSource { Source = items };
        grouped.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProjectRow.Stage)));
        projectsList.ItemsSource = grouped.View;
    }

    /// <summary>A plain full-width row, so the list reads as a list.</summary>
    private static Style CreateProjectListItemStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
        style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2)));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 2, 8, 2)));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        style.Setters.Add(new Setter(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand));

        var template = new ControlTemplate(typeof(ListViewItem));
        var border = new FrameworkElementFactory(typeof(Border), "RowBorder");
        border.SetBinding(Border.BackgroundProperty, new Binding(nameof(Control.Background))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetBinding(ContentPresenter.ContentProperty, new Binding(nameof(ContentControl.Content))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding(nameof(ContentControl.ContentTemplate))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        presenter.SetBinding(ContentPresenter.ContentTemplateSelectorProperty, new Binding(nameof(ContentControl.ContentTemplateSelector))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
        });
        border.AppendChild(presenter);
        template.VisualTree = border;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty, StudioTheme.PanelBrush, "RowBorder"));
        template.Triggers.Add(hover);
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static ItemsPanelTemplate CreateProjectRowsPanel()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
        return new ItemsPanelTemplate { VisualTree = panel };
    }

    /// <summary>
    /// One design stage as a heading that folds its projects away, so a
    /// practice with many stages can look at one of them at a time.
    /// </summary>
    private static GroupStyle CreateProjectStageGroupStyle()
    {
        var expander = new FrameworkElementFactory(typeof(Expander));
        expander.SetValue(Expander.IsExpandedProperty, true);
        expander.SetValue(Control.ForegroundProperty, StudioTheme.TextBrush);
        expander.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        expander.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        expander.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 10, 0, 4));

        var header = new FrameworkElementFactory(typeof(StackPanel));
        header.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding("Name"));
        title.SetValue(TextBlock.FontSizeProperty, 12.5);
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.ForegroundProperty, StudioTheme.TextBrush);
        header.AppendChild(title);
        var count = new FrameworkElementFactory(typeof(TextBlock));
        count.SetBinding(TextBlock.TextProperty, new Binding("ItemCount")
        {
            StringFormat = "{0} төсөл",
        });
        count.SetValue(TextBlock.FontSizeProperty, 11.5);
        count.SetValue(TextBlock.ForegroundProperty, StudioTheme.MutedTextBrush);
        count.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 1, 0, 0));
        header.AppendChild(count);
        // Bound to the group itself. An empty object was put here before, so the
        // header template had nothing to read and the heading came out blank.
        expander.SetBinding(HeaderedContentControl.HeaderProperty, new Binding());
        expander.SetValue(
            HeaderedContentControl.HeaderTemplateProperty,
            new DataTemplate { VisualTree = header });

        var items = new FrameworkElementFactory(typeof(ItemsPresenter));
        expander.AppendChild(items);

        return new GroupStyle
        {
            ContainerStyle = CreateGroupContainerStyle(expander),
            // The stage headings themselves always run down the page.
            Panel = CreateProjectRowsPanel(),
        };
    }

    private static Style CreateGroupContainerStyle(FrameworkElementFactory expander)
    {
        var template = new ControlTemplate(typeof(GroupItem)) { VisualTree = expander };
        var style = new Style(typeof(GroupItem));
        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private void EnterProjectFolder(ProjectFolderRow folder)
    {
        projectBrowserOrganization = folder.Company;
        ApplyProjectFilter();
    }

    /// <summary>
    /// The card's own menu. A single "project action" button in the header could
    /// only ever mean one thing at a time and hid the rest; the actions belong
    /// on the project they act on.
    /// </summary>
    private ContextMenu BuildProjectCardMenu(ProjectRow row)
    {
        var menu = new ContextMenu
        {
            Background = StudioTheme.PanelBrush,
            Foreground = StudioTheme.TextBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
        };

        var open = new MenuItem { Header = "Нээх" };
        open.Click += async (_, _) =>
        {
            projectsList.SelectedItem = row;
            await OpenSelectedProjectAsync();
        };
        menu.Items.Add(open);

        if (row.CanDelete || row.CanLeave)
        {
            menu.Items.Add(new Separator());
            var lifecycle = new MenuItem
            {
                Header = row.CanDelete ? "Устгах" : "Төслөөс гарах",
            };
            lifecycle.Click += async (_, _) =>
            {
                projectsList.SelectedItem = row;
                await RunSelectedProjectLifecycleActionAsync();
            };
            menu.Items.Add(lifecycle);
        }

        return menu;
    }

    private void ShowProjectCardMenu(object sender)
    {
        if (sender is not FrameworkElement source || source.DataContext is not ProjectRow row)
            return;

        ContextMenu menu = BuildProjectCardMenu(row);
        menu.PlacementTarget = source;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    /// <summary>A folder card: the organization's logo, its name and how much is inside.</summary>
    private static DataTemplate CreateProjectFolderTemplate()
    {
        var root = new FrameworkElementFactory(typeof(StackPanel));

        var crest = new FrameworkElementFactory(typeof(Border));
        crest.SetValue(FrameworkElement.HeightProperty, 158d);
        crest.SetValue(Border.BackgroundProperty, StudioTheme.InputBrush);
        crest.SetValue(Border.CornerRadiusProperty, new CornerRadius(7, 7, 0, 0));
        crest.SetValue(Border.ClipToBoundsProperty, true);
        var crestGrid = new FrameworkElementFactory(typeof(Grid));

        StudioOrganizationCrest.AppendTo(
            crestGrid,
            nameof(ProjectFolderRow.Monogram),
            nameof(ProjectFolderRow.LogoSource),
            initialsVisibilityPath: nameof(ProjectFolderRow.InitialsVisibility));

        var count = new FrameworkElementFactory(typeof(Border));
        count.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(210, 24, 27, 32)));
        count.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        count.SetValue(Border.PaddingProperty, new Thickness(7, 3, 7, 3));
        count.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        count.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        count.SetValue(FrameworkElement.MarginProperty, new Thickness(10));
        var countText = new FrameworkElementFactory(typeof(TextBlock));
        countText.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProjectFolderRow.CountLabel)));
        countText.SetValue(TextBlock.FontSizeProperty, 10.5);
        countText.SetValue(TextBlock.ForegroundProperty, Brushes.White);
        count.AppendChild(countText);
        crestGrid.AppendChild(count);
        crest.AppendChild(crestGrid);
        root.AppendChild(crest);

        var body = new FrameworkElementFactory(typeof(StackPanel));
        body.SetValue(FrameworkElement.MarginProperty, new Thickness(13, 11, 13, 13));
        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(ProjectFolderRow.Company)));
        name.SetValue(TextBlock.FontSizeProperty, 13.5);
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.ForegroundProperty, StudioTheme.TextBrush);
        name.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        name.SetValue(TextBlock.MaxHeightProperty, 40d);
        body.AppendChild(name);
        var hint = new FrameworkElementFactory(typeof(TextBlock));
        hint.SetValue(TextBlock.TextProperty, "Гүйцэтгэгч байгууллага");
        hint.SetValue(TextBlock.FontSizeProperty, 11.5);
        hint.SetValue(TextBlock.ForegroundProperty, StudioTheme.MutedTextBrush);
        hint.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 5, 0, 0));
        body.AppendChild(hint);
        root.AppendChild(body);

        return new DataTemplate(typeof(ProjectFolderRow)) { VisualTree = root };
    }

    /// <summary>One project on a single line, for when the cards are too much.</summary>
    private DataTemplate CreateProjectListRowTemplate()
    {
        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(FrameworkElement.MarginProperty, new Thickness(4, 3, 4, 3));
        // Code, name, stage, connection, menu. The name takes the slack but the
        // trailing columns keep a share, so the row reads as a table rather
        // than a name marooned from its own details.
        foreach (double width in new[] { 170d, -2d, -1d, -0.9d, 40d })
        {
            var column = new FrameworkElementFactory(typeof(ColumnDefinition));
            column.SetValue(
                ColumnDefinition.WidthProperty,
                width > 0 ? new GridLength(width) : new GridLength(-width, GridUnitType.Star));
            root.AppendChild(column);
        }

        AppendProjectListCell(root, nameof(ProjectRow.Code), 0, StudioTheme.MutedTextBrush, 11.5);
        AppendProjectListCell(root, nameof(ProjectRow.Name), 1, StudioTheme.TextBrush, 13);
        AppendProjectListCell(root, nameof(ProjectRow.Stage), 2, StudioTheme.MutedTextBrush, 12);
        AppendProjectListCell(root, nameof(ProjectRow.Connection), 3, StudioTheme.MutedTextBrush, 11.5);
        FrameworkElementFactory menu = CreateProjectMenuGlyph(transparent: true);
        menu.SetValue(Grid.ColumnProperty, 4);
        root.AppendChild(menu);
        return new DataTemplate(typeof(ProjectRow)) { VisualTree = root };
    }

    /// <summary>
    /// The three-dot handle a project is managed by. It sits on the project
    /// itself, so what it acts on is never in doubt.
    /// </summary>
    private FrameworkElementFactory CreateProjectMenuGlyph(bool transparent)
    {
        var button = new FrameworkElementFactory(typeof(Border));
        button.SetValue(FrameworkElement.WidthProperty, 26d);
        button.SetValue(FrameworkElement.HeightProperty, 26d);
        button.SetValue(Border.CornerRadiusProperty, new CornerRadius(13));
        button.SetValue(
            Border.BackgroundProperty,
            transparent
                ? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
                : new SolidColorBrush(Color.FromArgb(210, 24, 27, 32)));
        button.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        button.SetValue(
            FrameworkElement.VerticalAlignmentProperty,
            transparent ? VerticalAlignment.Center : VerticalAlignment.Top);
        button.SetValue(
            FrameworkElement.MarginProperty,
            transparent ? new Thickness(0, 0, 2, 0) : new Thickness(0, 9, 9, 0));
        button.SetValue(FrameworkElement.CursorProperty, System.Windows.Input.Cursors.Hand);
        button.SetValue(FrameworkElement.ToolTipProperty, "Төслийн үйлдлүүд");

        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.SetValue(TextBlock.TextProperty, "");
        glyph.SetValue(TextBlock.FontFamilyProperty, new FontFamily("Segoe MDL2 Assets"));
        glyph.SetValue(TextBlock.FontSizeProperty, 13d);
        glyph.SetValue(TextBlock.ForegroundProperty, StudioTheme.TextBrush);
        glyph.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        glyph.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        button.AppendChild(glyph);

        button.AddHandler(
            UIElement.MouseLeftButtonUpEvent,
            new System.Windows.Input.MouseButtonEventHandler((sender, args) =>
            {
                args.Handled = true;
                ShowProjectCardMenu(sender);
            }));
        return button;
    }

    private static void AppendProjectListCell(
        FrameworkElementFactory root,
        string path,
        int column,
        Brush foreground,
        double fontSize)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new Binding(path));
        text.SetValue(Grid.ColumnProperty, column);
        text.SetValue(TextBlock.ForegroundProperty, foreground);
        text.SetValue(TextBlock.FontSizeProperty, fontSize);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 7, 6, 7));
        root.AppendChild(text);
    }

    private sealed class ProjectBrowserTemplateSelector : DataTemplateSelector
    {
        public required DataTemplate FolderTemplate { get; init; }

        public required DataTemplate CardTemplate { get; init; }

        public required DataTemplate ListRowTemplate { get; init; }

        public required Func<bool> UsesListLayout { get; init; }

        public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
            item switch
            {
                ProjectFolderRow => FolderTemplate,
                ProjectRow => UsesListLayout() ? ListRowTemplate : CardTemplate,
                _ => null,
            };
    }
}
