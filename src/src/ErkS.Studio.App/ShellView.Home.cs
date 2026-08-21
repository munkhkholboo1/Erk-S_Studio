using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErkS.Studio;

/// <summary>
/// What the Studio opens on: the Platform itself rather than a project list.
/// It carries the practice's own recent work, the programs the site publishes
/// with the site's own artwork, and the way to reach both — so starting the
/// Studio shows what the Platform has become before it shows a file list.
/// </summary>
internal sealed partial class ShellView
{
    private const double RecentCardWidth = 218d;
    private const double RecentCardImageHeight = 122d;
    private const double ProductCardWidth = 330d;
    private const double ProductCardImageHeight = 172d;

    private readonly StudioProductCatalogService productCatalog = new();
    private readonly StudioSiteImageCache siteImages = new();

    private readonly StackPanel homeRecentStrip = new() { Orientation = Orientation.Horizontal };
    private readonly TextBlock homeRecentEmpty = new()
    {
        FontSize = 12.5,
        Foreground = StudioTheme.MutedTextBrush,
        Margin = new Thickness(2, 4, 0, 10),
        Text = "Cloud ERA бүртгэлээр нэвтэрмэгц сүүлийн төслүүд энд харагдана.",
    };

    private readonly Border homeFeatureBanner = new()
    {
        Height = 268,
        CornerRadius = new CornerRadius(16),
        Background = StudioTheme.PanelBrush,
        BorderBrush = StudioTheme.BorderBrush,
        BorderThickness = new Thickness(1),
        Margin = new Thickness(0, 0, 0, 30),
        ClipToBounds = true,
    };
    private readonly Border homeFeatureArt = new()
    {
        CornerRadius = new CornerRadius(0, 16, 16, 0),
        Background = StudioTheme.InputBrush,
        ClipToBounds = true,
    };
    private readonly TextBlock homeFeatureKicker = new()
    {
        Text = "CLOUD ERA DOCUMENT STUDIO",
        FontSize = 10.5,
        FontWeight = FontWeights.Bold,
        Foreground = StudioTheme.AccentSoftBrush,
        Margin = new Thickness(0, 0, 0, 10),
    };
    private readonly TextBlock homeFeatureHeadline = new()
    {
        Text = "Эх үүсвэрээс альбум хүртэл нэг урсгалд",
        FontSize = 26,
        FontWeight = FontWeights.SemiBold,
        Foreground = StudioTheme.TextBrush,
        TextWrapping = TextWrapping.Wrap,
        LineHeight = 33,
    };
    private readonly TextBlock homeFeatureSummary = new()
    {
        Text = "Revit, AutoCAD, CityGen эх үүсвэрүүдийг нэг төсөлд зангидаж, стандарт PDF альбум болгоно.",
        FontSize = 13,
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 11, 0, 0),
    };

    private readonly WrapPanel homeProductGrid = new();
    private readonly TextBlock homeCatalogStatus = new()
    {
        FontSize = 12.5,
        Foreground = StudioTheme.MutedTextBrush,
        Margin = new Thickness(0, 0, 0, 14),
        Text = "Сайтаас программын мэдээллийг уншиж байна…",
    };

    private readonly Border homePartnerArt = new()
    {
        CornerRadius = new CornerRadius(0, 14, 14, 0),
        Background = StudioTheme.InputBrush,
        ClipToBounds = true,
    };
    private readonly Border homePartnerBanner = new()
    {
        Height = 222,
        CornerRadius = new CornerRadius(14),
        Background = StudioTheme.PanelBrush,
        BorderBrush = StudioTheme.BorderBrush,
        BorderThickness = new Thickness(1),
        Margin = new Thickness(0, 0, 0, 8),
        Cursor = System.Windows.Input.Cursors.Hand,
        ClipToBounds = true,
        Visibility = Visibility.Collapsed,
    };

    private string homeSiteUrl = "";
    private string homePartnerUrl = "";
    private string homeFeatureUrl = "";
    private bool homeCatalogRequested;

    private UIElement BuildHomePage()
    {
        var page = new StackPanel { Margin = new Thickness(30, 22, 30, 40) };
        page.Children.Add(BuildHomeMasthead());
        page.Children.Add(BuildHomeRecentSection());
        page.Children.Add(BuildHomeFeatureSection());
        page.Children.Add(BuildHomeProductSection());
        page.Children.Add(BuildHomePartnerSection());

        return new ScrollViewer
        {
            Content = page,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>The page's own name and the two things most often wanted from it.</summary>
    private UIElement BuildHomeMasthead()
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 26) };

        var actions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var newProject = StudioWidgets.CreateGlyphTextButton("", "Шинэ төсөл", primary: true);
        newProject.Margin = new Thickness(0, 0, 10, 0);
        newProject.Click += async (_, _) => await CreateProjectAsync();
        actions.Children.Add(newProject);
        var openProjects = StudioWidgets.CreateGlyphTextButton("", "Төслүүд");
        openProjects.Click += (_, _) => SelectPage(StudioPage.Projects);
        actions.Children.Add(openProjects);
        DockPanel.SetDock(actions, Dock.Right);
        row.Children.Add(actions);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal };
        titleRow.Children.Add(new TextBlock
        {
            Text = "Erk-S Platform",
            FontSize = 24,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
        });
        titleRow.Children.Add(new Border
        {
            Background = StudioTheme.InputBrush,
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(12, 4, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock
            {
                Text = "Studio " + StudioReleaseInfo.DisplayVersion.Split('+')[0],
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = StudioTheme.MutedTextBrush,
            },
        });
        words.Children.Add(titleRow);
        words.Children.Add(new TextBlock
        {
            Text = "Зураг төслийн нэгдсэн ажлын орчин · Cloud ERA",
            FontSize = 12.5,
            Foreground = StudioTheme.MutedTextBrush,
            Margin = new Thickness(0, 5, 0, 0),
        });
        row.Children.Add(words);
        return row;
    }

    private UIElement BuildHomeRecentSection()
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 30) };
        section.Children.Add(BuildHomeSectionHeading(
            "Сүүлд ажилласан",
            "Энэ бүртгэлээр хамгийн сүүлд хөдөлсөн төслүүд",
            "Бүх төсөл",
            () => SelectPage(StudioPage.Projects)));
        section.Children.Add(homeRecentEmpty);
        section.Children.Add(new ScrollViewer
        {
            Content = homeRecentStrip,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(0, 2, 0, 0),
        });
        return section;
    }

    /// <summary>
    /// One wide banner carrying the site's own product photograph, darkened
    /// from the left so the words sit on the quiet part of the picture.
    /// </summary>
    private UIElement BuildHomeFeatureSection()
    {
        // Two columns rather than one photograph stretched behind everything:
        // across a banner this wide a full-bleed picture is cropped to an
        // unreadable band, while a right-hand panel keeps the shot intact.
        var layers = new Grid();
        layers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.92, GridUnitType.Star) });

        Grid.SetColumn(homeFeatureArt, 1);
        layers.Children.Add(homeFeatureArt);

        var seam = new Border
        {
            CornerRadius = new CornerRadius(0, 16, 16, 0),
            Background = new LinearGradientBrush(
                [
                    new GradientStop(StudioTheme.PanelColor, 0d),
                    new GradientStop(Color.FromArgb(210, 24, 27, 32), 0.22d),
                    new GradientStop(Color.FromArgb(60, 24, 27, 32), 0.55d),
                    new GradientStop(Color.FromArgb(0, 24, 27, 32), 0.8d),
                ],
                new Point(0, 0.5),
                new Point(1, 0.5)),
        };
        Grid.SetColumn(seam, 1);
        layers.Children.Add(seam);

        var words = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(36, 0, 0, 0),
            MaxWidth = 560,
        };
        words.Children.Add(homeFeatureKicker);
        words.Children.Add(homeFeatureHeadline);
        words.Children.Add(homeFeatureSummary);

        var buttons = new WrapPanel { Margin = new Thickness(0, 22, 0, 0) };
        var open = StudioWidgets.CreateGlyphTextButton("", "Төслүүд рүү", primary: true);
        open.Margin = new Thickness(0, 0, 10, 0);
        open.Click += (_, _) => SelectPage(StudioPage.Projects);
        buttons.Children.Add(open);
        var learn = StudioWidgets.CreateGlyphTextButton("", "Дэлгэрэнгүй");
        learn.Click += (_, _) => OpenExternal(
            homeFeatureUrl.Length > 0 ? homeFeatureUrl : homeSiteUrl);
        buttons.Children.Add(learn);
        words.Children.Add(buttons);
        Grid.SetColumnSpan(words, 2);
        layers.Children.Add(words);

        homeFeatureBanner.Child = layers;
        return homeFeatureBanner;
    }

    private UIElement BuildHomeProductSection()
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 30) };
        section.Children.Add(BuildHomeSectionHeading(
            "Platform-ийн программууд",
            "Зураг төслийн ажлаа хурдлуулах Erk-S хэрэгслүүд",
            "Сайт нээх",
            () => OpenExternal(homeSiteUrl)));
        section.Children.Add(homeCatalogStatus);
        section.Children.Add(homeProductGrid);
        return section;
    }

    private UIElement BuildHomePartnerSection()
    {
        var section = new StackPanel();
        section.Children.Add(BuildHomeSectionHeading(
            "Хамтрагчийн эрх",
            "Хөгжүүлэлтийн шатны насан туршийн Partner эрх",
            "Дэлгэрэнгүй",
            () => OpenExternal(homePartnerUrl)));

        // Same two-column composition as the feature banner, for the same
        // reason: full-bleed would crop the plaques out of their own picture.
        var layers = new Grid();
        layers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layers.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star) });

        Grid.SetColumn(homePartnerArt, 1);
        layers.Children.Add(homePartnerArt);

        var seam = new Border
        {
            CornerRadius = new CornerRadius(0, 14, 14, 0),
            Background = new LinearGradientBrush(
                [
                    new GradientStop(StudioTheme.PanelColor, 0d),
                    new GradientStop(Color.FromArgb(205, 24, 27, 32), 0.2d),
                    new GradientStop(Color.FromArgb(55, 24, 27, 32), 0.52d),
                    new GradientStop(Color.FromArgb(0, 24, 27, 32), 0.78d),
                ],
                new Point(0, 0.5),
                new Point(1, 0.5)),
        };
        Grid.SetColumn(seam, 1);
        layers.Children.Add(seam);

        var words = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(32, 0, 0, 0),
            MaxWidth = 460,
        };
        words.Children.Add(new TextBlock
        {
            Text = "BRONZE · SILVER · GOLDEN · PLATINUM",
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Foreground = StudioTheme.AccentSoftBrush,
            Margin = new Thickness(0, 0, 0, 9),
        });
        words.Children.Add(new TextBlock
        {
            Text = "Насан туршийн Partner эрх",
            FontSize = 21,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        words.Children.Add(new TextBlock
        {
            Text = "Эхний хөрөнгө оруулагчийн account эрх — албан ёсны subscription нээгдэх хүртэл.",
            FontSize = 12.5,
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0),
        });
        Grid.SetColumnSpan(words, 2);
        layers.Children.Add(words);
        homePartnerBanner.Child = layers;
        homePartnerBanner.MouseLeftButtonUp += (_, _) => OpenExternal(homePartnerUrl);
        section.Children.Add(homePartnerBanner);
        return section;
    }

    private static UIElement BuildHomeSectionHeading(
        string title,
        string subtitle,
        string actionText,
        Action action)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
        Button link = StudioWidgets.CreateGlyphTextButton("", actionText);
        link.VerticalAlignment = VerticalAlignment.Center;
        link.Click += (_, _) => action();
        DockPanel.SetDock(link, Dock.Right);
        row.Children.Add(link);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
        });
        words.Children.Add(new TextBlock
        {
            Text = subtitle,
            FontSize = 12,
            Foreground = StudioTheme.MutedTextBrush,
            Margin = new Thickness(0, 3, 0, 0),
        });
        row.Children.Add(words);
        return row;
    }

    /// <summary>
    /// The recent strip, rebuilt whenever the project list moves. It reads the
    /// same rows the project screen does, so nothing can drift between them.
    /// </summary>
    private void RefreshHomeRecentProjects()
    {
        homeRecentStrip.Children.Clear();
        List<ProjectRow> recent = projectRows
            .OrderByDescending(row => row.UpdatedAtUtc)
            .ThenBy(row => row.Code, StringComparer.OrdinalIgnoreCase)
            .Take(8)
            .ToList();
        homeRecentEmpty.Visibility = recent.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (ProjectRow row in recent)
            homeRecentStrip.Children.Add(BuildRecentProjectCard(row));
    }

    private UIElement BuildRecentProjectCard(ProjectRow row)
    {
        var card = new Border
        {
            Width = RecentCardWidth,
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(0, 0, 12, 6),
            Cursor = System.Windows.Input.Cursors.Hand,
            ToolTip = row.Code + " · " + row.Name,
        };
        var stack = new StackPanel();

        var preview = new Border
        {
            Height = RecentCardImageHeight,
            Background = StudioTheme.InputBrush,
            CornerRadius = new CornerRadius(9, 9, 0, 0),
            ClipToBounds = true,
        };
        var previewLayers = new Grid();
        previewLayers.Children.Add(new Image
        {
            Source = SvgIconLoader.TryLoad(StudioWidgets.GetAssetPath("logo-erks.svg")),
            Width = 40,
            Height = 40,
            Opacity = 0.16,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        // The album preview is painted as the border's own background so it is
        // clipped to the card's rounded top corners; an Image child would square
        // them off. It arrives later, so the card listens for it.
        ApplyThumbnailBrush(preview, row.ThumbnailSource);
        row.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ProjectRow.ThumbnailSource))
                ApplyThumbnailBrush(preview, row.ThumbnailSource);
        };
        previewLayers.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(205, 16, 18, 22)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(8),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = row.Code,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = StudioTheme.AccentSoftBrush,
            },
        });
        preview.Child = previewLayers;
        stack.Children.Add(preview);

        var words = new StackPanel { Margin = new Thickness(12, 10, 12, 12) };
        words.Children.Add(new TextBlock
        {
            Text = row.Name,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        words.Children.Add(new TextBlock
        {
            Text = row.Stage,
            FontSize = 11,
            Foreground = StudioTheme.MutedTextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 4, 0, 0),
        });
        words.Children.Add(new TextBlock
        {
            Text = row.UpdatedLabel,
            FontSize = 10.5,
            Foreground = StudioTheme.FaintTextBrush,
            Margin = new Thickness(0, 5, 0, 0),
        });
        stack.Children.Add(words);

        card.Child = stack;
        card.MouseLeftButtonUp += async (_, _) => await OpenProjectRowAsync(row);
        card.MouseEnter += (_, _) => card.BorderBrush = StudioTheme.AccentBrush;
        card.MouseLeave += (_, _) => card.BorderBrush = StudioTheme.BorderBrush;
        return card;
    }

    /// <summary>
    /// Reads the site's catalogue once per session, the first time the home
    /// page is looked at, so starting the Studio never waits on the network.
    /// </summary>
    private async Task EnsureHomeCatalogAsync()
    {
        if (homeCatalogRequested)
            return;

        homeCatalogRequested = true;
        StudioCatalogSnapshot snapshot = await productCatalog.ReadAsync();
        homeSiteUrl = snapshot.SiteUrl;
        homePartnerUrl = snapshot.PartnerUrl;

        homeProductGrid.Children.Clear();
        foreach (StudioCatalogRelease release in snapshot.Releases)
            homeProductGrid.Children.Add(BuildProductCard(release));

        int available = snapshot.Releases.Count(item => item.IsAvailable);
        homeCatalogStatus.Text = available > 0
            ? $"{snapshot.SiteUrl} дээрээс {available} программ татах боломжтой."
            : "Сайттай холбогдож чадсангүй. Программын жагсаалт офлайн харагдаж байна.";

        ApplyFeatureCopy(snapshot.Featured);
        await ApplyBackgroundImageAsync(homeFeatureArt, snapshot.HeroImageUrl, AlignmentX.Right);
        await ApplyBackgroundImageAsync(homePartnerArt, snapshot.PartnerImageUrl, AlignmentX.Center);
        homePartnerBanner.Visibility = homePartnerArt.Background is ImageBrush
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    /// <summary>
    /// The banner says what the site says about the Studio. When the site is
    /// silent the wording written into this build stands, so the banner is
    /// never a blank rectangle.
    /// </summary>
    private void ApplyFeatureCopy(StudioCatalogRelease? featured)
    {
        if (featured is null)
            return;

        homeFeatureUrl = featured.Product.ProductUrl;
        if (featured.Product.Kicker.Length > 0)
            homeFeatureKicker.Text = featured.Product.Kicker.ToUpperInvariant();
        if (featured.Product.Headline.Length > 0)
            homeFeatureHeadline.Text = featured.Product.Headline;
        if (featured.Product.Summary.Length > 0)
            homeFeatureSummary.Text = featured.Product.Summary;
    }

    private static void ApplyThumbnailBrush(Border preview, ImageSource? thumbnail)
    {
        preview.Background = thumbnail is null
            ? StudioTheme.InputBrush
            : new ImageBrush(thumbnail)
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Top,
            };
    }

    /// <summary>
    /// Paints a downloaded picture as a panel's background. A brush, not a
    /// child image, because a Border only clips its background to its own
    /// rounded corners — an image child would square them off again.
    /// </summary>
    private async Task ApplyBackgroundImageAsync(Border target, string url, AlignmentX alignment)
    {
        ImageSource? image = await siteImages.GetAsync(url);
        if (image is null)
            return;

        target.Background = new ImageBrush(image)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = alignment,
            AlignmentY = AlignmentY.Center,
        };
    }

    private UIElement BuildProductCard(StudioCatalogRelease release)
    {
        var card = new Border
        {
            Width = ProductCardWidth,
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 14, 14),
        };
        var stack = new StackPanel();

        var art = new Border
        {
            Height = ProductCardImageHeight,
            Background = StudioTheme.InputBrush,
            CornerRadius = new CornerRadius(11, 11, 0, 0),
            ClipToBounds = true,
        };
        var artLayers = new Grid();
        artLayers.Children.Add(new Image
        {
            Source = SvgIconLoader.TryLoad(StudioWidgets.GetAssetPath("logo-erks.svg")),
            Width = 44,
            Height = 44,
            Opacity = 0.14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        artLayers.Children.Add(BuildAvailabilityBadge(release));
        art.Child = artLayers;
        _ = ApplyBackgroundImageAsync(art, release.Product.ImageUrl, AlignmentX.Center);
        stack.Children.Add(art);

        var words = new StackPanel { Margin = new Thickness(16, 14, 16, 12), Height = 108 };
        if (release.Product.Kicker.Length > 0)
        {
            words.Children.Add(new TextBlock
            {
                Text = release.Product.Kicker.ToUpperInvariant(),
                FontSize = 9.5,
                FontWeight = FontWeights.Bold,
                Foreground = StudioTheme.AccentSoftBrush,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 0, 0, 6),
            });
        }
        words.Children.Add(new TextBlock
        {
            Text = release.Product.Name,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        words.Children.Add(new TextBlock
        {
            Text = release.Product.Summary,
            FontSize = 12,
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 50,
            Margin = new Thickness(0, 7, 0, 0),
        });
        stack.Children.Add(words);

        var footer = new DockPanel { Margin = new Thickness(16, 0, 16, 16) };
        Button action = release.IsAvailable
            ? StudioWidgets.CreateGlyphTextButton("", "Татах", release.DownloadUrl, primary: true)
            : StudioWidgets.CreateGlyphTextButton("", "Дэлгэрэнгүй");
        action.VerticalAlignment = VerticalAlignment.Center;
        action.Click += (_, _) => OpenExternal(
            release.IsAvailable ? release.DownloadUrl : release.Product.ProductUrl);
        DockPanel.SetDock(action, Dock.Right);
        footer.Children.Add(action);
        footer.Children.Add(new TextBlock
        {
            Text = release.VersionLabel,
            FontSize = 11,
            Foreground = StudioTheme.FaintTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 0, 10, 0),
        });
        stack.Children.Add(footer);

        card.Child = stack;
        card.MouseEnter += (_, _) => card.BorderBrush = StudioTheme.BorderHoverBrush;
        card.MouseLeave += (_, _) => card.BorderBrush = StudioTheme.BorderBrush;
        return card;
    }

    private static UIElement BuildAvailabilityBadge(StudioCatalogRelease release)
    {
        string label = release.Product.AvailabilityLabel.Trim();
        if (label.Length == 0)
            label = release.Product.IsRoadmapOnly ? "Coming soon" : "Available";

        Brush foreground = release.Product.IsRoadmapOnly
            ? StudioTheme.MutedTextBrush
            : release.Product.IsFree ? StudioTheme.SuccessBrush : StudioTheme.AccentSoftBrush;
        return new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(220, 14, 16, 20)),
            CornerRadius = new CornerRadius(5),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = label,
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground,
            },
        };
    }

    /// <summary>
    /// Hands a site address to the machine's browser. The Studio does not fetch
    /// installers itself — a download is the person's own decision, made in
    /// their browser, where they can see what they are taking.
    /// </summary>
    private void OpenExternal(string url)
    {
        string value = (url ?? "").Trim();
        if (value.Length == 0)
            value = productCatalog.SiteUrl;
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("https" or "http"))
        {
            SetStatus("Хаяг буруу байна.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            SetStatus("Хөтчийг нээж чадсангүй.");
        }
    }
}
