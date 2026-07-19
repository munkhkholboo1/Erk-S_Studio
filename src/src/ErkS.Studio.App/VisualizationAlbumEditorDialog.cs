using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed class VisualizationAlbumEditorDialog : Window
{
    private const double PagePreviewWidth = 760d;
    private readonly ProjectVisualizationSource workingSource;
    private readonly string projectId;
    private readonly int firstPageNumber;
    private readonly Func<ProjectVisualizationImage, string?> imagePathResolver;
    private readonly Dictionary<string, bool> initialInclusion;
    private readonly HashSet<string> selectedPageImageIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> selectedInactiveImageIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly StackPanel pagesPanel = new();
    private readonly StackPanel inactivePanel = new();
    private readonly TextBlock summaryText = new();
    private readonly Button excludeButton = StudioWidgets.CreateButton("Хуудаснаас хасах");
    private readonly Button includeButton = StudioWidgets.CreateButton("Хуудсанд оруулах");

    public IReadOnlyDictionary<string, bool> InclusionByImageId { get; private set; } =
        new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    public int ChangedCount { get; private set; }

    public VisualizationAlbumEditorDialog(
        ProjectVisualizationSource source,
        string projectId,
        int firstPageNumber,
        Func<ProjectVisualizationImage, string?> imagePathResolver)
    {
        this.projectId = projectId;
        this.firstPageNumber = Math.Max(1, firstPageNumber);
        this.imagePathResolver = imagePathResolver;
        workingSource = source.CreateProjectSnapshot(projectId);
        initialInclusion = workingSource.ImagesForProject(projectId).ToDictionary(
            image => image.Id,
            image => image.IsIncludedInAlbum,
            StringComparer.OrdinalIgnoreCase);

        Title = "Харагдах байдлын хуудаслалт";
        Width = 1120;
        Height = 820;
        MinWidth = 820;
        MinHeight = 620;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        StudioTheme.Apply(this);

        Content = BuildContent();
        RefreshLayout();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(20) };

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.Click += (_, _) => DialogResult = false;
        Button save = StudioWidgets.CreatePrimaryButton("Хадгалах");
        save.IsDefault = true;
        save.Click += (_, _) => Accept();
        footer.Children.Add(cancel);
        footer.Children.Add(save);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(StudioWidgets.CreateTitle("Харагдах байдлын хуудаслалт"));
        summaryText.Foreground = StudioTheme.MutedTextBrush;
        summaryText.Margin = new Thickness(0, 3, 0, 0);
        heading.Children.Add(summaryText);
        header.Children.Add(heading);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        excludeButton.Click += (_, _) => ExcludeSelectedImages();
        includeButton.Click += (_, _) => IncludeSelectedImages();
        actions.Children.Add(excludeButton);
        actions.Children.Add(includeButton);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var content = new StackPanel();
        content.Children.Add(pagesPanel);
        content.Children.Add(inactivePanel);
        root.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        });
        return root;
    }

    private void RefreshLayout()
    {
        IReadOnlyList<ProjectVisualizationImage> images = workingSource.ImagesForProject(projectId);
        IReadOnlyList<VisualizationAlbumPagePlan> plans = VisualizationPageLayoutPlanner.Create(
            workingSource,
            projectId,
            firstPageNumber);
        int includedCount = images.Count(image => image.IsAvailable && image.IsIncludedInAlbum);
        int inactiveCount = images.Count(image => !image.IsIncludedInAlbum);
        int missingCount = images.Count(image => !image.IsAvailable);
        summaryText.Text =
            $"Хуудас: {plans.Count} · Альбумд: {includedCount} · Идэвхгүй: {inactiveCount}" +
            (missingCount > 0 ? $" · Эх файл олдоогүй: {missingCount}" : "");

        pagesPanel.Children.Clear();
        if (plans.Count == 0)
        {
            pagesPanel.Children.Add(CreateEmptyState("Альбумд идэвхтэй харагдах байдал алга."));
        }
        else
        {
            foreach (VisualizationAlbumPagePlan plan in plans)
                pagesPanel.Children.Add(CreatePagePreview(plan));
        }

        inactivePanel.Children.Clear();
        List<ProjectVisualizationImage> inactive = images
            .Where(image => !image.IsIncludedInAlbum)
            .ToList();
        List<ProjectVisualizationImage> missingIncluded = images
            .Where(image => image.IsIncludedInAlbum && !image.IsAvailable)
            .ToList();

        if (inactive.Count > 0)
        {
            inactivePanel.Children.Add(StudioWidgets.CreateSectionHeader("ИДЭВХГҮЙ ХАРАГДАХ БАЙДАЛ"));
            var inactiveImages = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            foreach (ProjectVisualizationImage image in inactive)
                inactiveImages.Children.Add(CreateInactiveImageToggle(image));
            inactivePanel.Children.Add(inactiveImages);
        }

        if (missingIncluded.Count > 0)
        {
            inactivePanel.Children.Add(StudioWidgets.CreateSectionHeader("ЭХ ФАЙЛ ОЛДСОНГҮЙ"));
            var missingImages = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
            foreach (ProjectVisualizationImage image in missingIncluded)
                missingImages.Children.Add(CreateUnavailableImageCard(image));
            inactivePanel.Children.Add(missingImages);
        }

        UpdateActionState();
    }

    private UIElement CreatePagePreview(VisualizationAlbumPagePlan plan)
    {
        var section = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 0, 0, 18),
        };
        section.Children.Add(new TextBlock
        {
            Text = $"Хуудас {plan.Number}",
            Foreground = StudioTheme.TextBrush,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7),
        });

        var canvas = new Canvas
        {
            Width = BuildingArchitectureConceptPageLayout.PageWidthMm,
            Height = BuildingArchitectureConceptPageLayout.PageHeightMm,
            Background = Brushes.White,
        };
        AddPageChrome(canvas, plan);
        foreach (VisualizationImageTilePlan tile in plan.Tiles)
            canvas.Children.Add(CreatePageImageToggle(tile));

        section.Children.Add(new Border
        {
            Width = PagePreviewWidth,
            Height = PagePreviewWidth * BuildingArchitectureConceptPageLayout.PageHeightMm /
                     BuildingArchitectureConceptPageLayout.PageWidthMm,
            Background = Brushes.White,
            BorderBrush = StudioTheme.BorderHoverBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Child = new Viewbox
            {
                Stretch = Stretch.Uniform,
                Child = canvas,
            },
        });
        return section;
    }

    private UIElement CreatePageImageToggle(VisualizationImageTilePlan tile)
    {
        PageRectMm frame = tile.Frame;
        var content = new Grid
        {
            Width = frame.Width,
            Height = frame.Height,
            Background = Brushes.White,
            ClipToBounds = true,
        };
        ImageSource? thumbnail = TryLoadThumbnail(tile.Image, 760);
        if (thumbnail is not null)
        {
            content.Children.Add(new Image
            {
                Source = thumbnail,
                Stretch = tile.FitMode == VisualizationImageFitMode.CenterCrop
                    ? Stretch.UniformToFill
                    : Stretch.Uniform,
            });
        }
        else
        {
            content.Children.Add(new TextBlock
            {
                Text = "Зураг олдсонгүй",
                Foreground = Brushes.DimGray,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }

        var toggle = new ToggleButton
        {
            Width = frame.Width,
            Height = frame.Height,
            MinHeight = 0,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(1.4),
            BorderBrush = Brushes.Transparent,
            Background = Brushes.White,
            Content = content,
            ToolTip = tile.Image.OriginalFileName,
            IsChecked = selectedPageImageIds.Contains(tile.Image.Id),
        };
        toggle.Checked += (_, _) =>
        {
            selectedPageImageIds.Add(tile.Image.Id);
            UpdateActionState();
        };
        toggle.Unchecked += (_, _) =>
        {
            selectedPageImageIds.Remove(tile.Image.Id);
            UpdateActionState();
        };
        Canvas.SetLeft(toggle, frame.X);
        Canvas.SetTop(toggle, frame.Y);
        return toggle;
    }

    private UIElement CreateInactiveImageToggle(ProjectVisualizationImage image)
    {
        var content = CreateImageCardContent(image);
        var toggle = new ToggleButton
        {
            Width = 210,
            Height = 156,
            MinHeight = 0,
            Padding = new Thickness(5),
            Margin = new Thickness(0, 0, 8, 8),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
            Content = content,
            ToolTip = image.OriginalFileName,
            IsChecked = selectedInactiveImageIds.Contains(image.Id),
        };
        toggle.Checked += (_, _) =>
        {
            selectedInactiveImageIds.Add(image.Id);
            UpdateActionState();
        };
        toggle.Unchecked += (_, _) =>
        {
            selectedInactiveImageIds.Remove(image.Id);
            UpdateActionState();
        };
        return toggle;
    }

    private UIElement CreateUnavailableImageCard(ProjectVisualizationImage image) => new Border
    {
        Width = 210,
        Height = 156,
        Padding = new Thickness(5),
        Margin = new Thickness(0, 0, 8, 8),
        Background = StudioTheme.PanelAltBrush,
        CornerRadius = new CornerRadius(5),
        Opacity = 0.62,
        Child = CreateImageCardContent(image),
    };

    private UIElement CreateImageCardContent(ProjectVisualizationImage image)
    {
        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ImageSource? thumbnail = TryLoadThumbnail(image, 360);
        UIElement preview = thumbnail is null
            ? new TextBlock
            {
                Text = "Эх файл олдсонгүй",
                Foreground = StudioTheme.MutedTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            }
            : new Image
            {
                Source = thumbnail,
                Stretch = Stretch.Uniform,
            };
        content.Children.Add(preview);
        var fileName = new TextBlock
        {
            Text = image.OriginalFileName,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxHeight = 38,
            Margin = new Thickness(4, 5, 4, 2),
        };
        Grid.SetRow(fileName, 1);
        content.Children.Add(fileName);
        return content;
    }

    private ImageSource? TryLoadThumbnail(ProjectVisualizationImage image, int decodePixelWidth)
    {
        if (!image.IsAvailable)
            return null;
        string? path = imagePathResolver(image);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = Math.Max(120, decodePixelWidth);
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static UIElement CreateEmptyState(string text) => new Border
    {
        MinHeight = 150,
        Margin = new Thickness(0, 4, 0, 18),
        Background = StudioTheme.PanelBrush,
        CornerRadius = new CornerRadius(6),
        Child = new TextBlock
        {
            Text = text,
            Foreground = StudioTheme.MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
    };

    private static void AddPageChrome(Canvas canvas, VisualizationAlbumPagePlan plan)
    {
        canvas.Children.Add(new Rectangle
        {
            Width = 410,
            Height = 287,
            Stroke = Brushes.Black,
            StrokeThickness = 0.35,
        });
        Canvas.SetLeft(canvas.Children[^1], 5);
        Canvas.SetTop(canvas.Children[^1], 5);

        var footerLine = new Line
        {
            X1 = 5,
            Y1 = 269,
            X2 = 415,
            Y2 = 269,
            Stroke = Brushes.Black,
            StrokeThickness = 0.35,
        };
        canvas.Children.Add(footerLine);
        var title = new TextBlock
        {
            Text = plan.Title,
            Foreground = Brushes.Black,
            FontFamily = new FontFamily(BuildingArchitectureConceptPageLayout.FontFamilyName),
            FontSize = 4.2,
        };
        Canvas.SetLeft(title, 275);
        Canvas.SetTop(title, 274);
        canvas.Children.Add(title);
        var number = new TextBlock
        {
            Text = plan.Number,
            Foreground = Brushes.Black,
            FontFamily = new FontFamily(BuildingArchitectureConceptPageLayout.FontFamilyName),
            FontWeight = FontWeights.SemiBold,
            FontSize = 5,
        };
        Canvas.SetLeft(number, 397);
        Canvas.SetTop(number, 282);
        canvas.Children.Add(number);
    }

    private void ExcludeSelectedImages()
    {
        if (selectedPageImageIds.Count == 0)
            return;
        foreach (ProjectVisualizationImage image in workingSource.ImagesForProject(projectId)
                     .Where(image => selectedPageImageIds.Contains(image.Id)))
        {
            image.IsIncludedInAlbum = false;
        }
        selectedPageImageIds.Clear();
        RefreshLayout();
    }

    private void IncludeSelectedImages()
    {
        if (selectedInactiveImageIds.Count == 0)
            return;
        foreach (ProjectVisualizationImage image in workingSource.ImagesForProject(projectId)
                     .Where(image => selectedInactiveImageIds.Contains(image.Id)))
        {
            image.IsIncludedInAlbum = true;
        }
        selectedInactiveImageIds.Clear();
        RefreshLayout();
    }

    private void UpdateActionState()
    {
        excludeButton.IsEnabled = selectedPageImageIds.Count > 0;
        excludeButton.Content = selectedPageImageIds.Count == 0
            ? "Хуудаснаас хасах"
            : $"Хуудаснаас хасах ({selectedPageImageIds.Count})";
        includeButton.IsEnabled = selectedInactiveImageIds.Count > 0;
        includeButton.Content = selectedInactiveImageIds.Count == 0
            ? "Хуудсанд оруулах"
            : $"Хуудсанд оруулах ({selectedInactiveImageIds.Count})";
    }

    private void Accept()
    {
        Dictionary<string, bool> result = workingSource.ImagesForProject(projectId).ToDictionary(
            image => image.Id,
            image => image.IsIncludedInAlbum,
            StringComparer.OrdinalIgnoreCase);
        ChangedCount = result.Count(pair =>
            initialInclusion.TryGetValue(pair.Key, out bool initial) && initial != pair.Value);
        InclusionByImageId = result;
        DialogResult = true;
    }
}
