using System.IO;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ErkS.Platform.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ErkS.Studio;

internal sealed class SiteContextMapEditorDialog : Window
{
    private readonly SiteContextMapEditorControl editor;

    public SiteContextMapEditorDialog(
        string projectFolder,
        string projectId,
        ProjectSiteContextMap source)
    {
        Title = "Байршлын схем / Орчны тойм";
        Width = Math.Min(1260, SystemParameters.WorkArea.Width * 0.94);
        Height = Math.Min(820, SystemParameters.WorkArea.Height * 0.92);
        MinWidth = 940;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        StudioTheme.Apply(this);

        editor = new SiteContextMapEditorControl(projectFolder, projectId, source);
        editor.Completed += HandleCompleted;
        Content = editor;
    }

    public bool HasSavedChanges => editor.HasSavedChanges;

    public ProjectSiteContextMap Result => editor.Result;

    public event Action<ProjectSiteContextMap>? SiteContextSaved
    {
        add => editor.SiteContextSaved += value;
        remove => editor.SiteContextSaved -= value;
    }

    protected override void OnClosed(EventArgs e)
    {
        editor.Completed -= HandleCompleted;
        editor.Dispose();
        base.OnClosed(e);
    }

    private void HandleCompleted(bool saved)
    {
        DialogResult = saved;
        Close();
    }
}

internal sealed class SiteContextMapEditorControl : UserControl, IDisposable
{
    private const int CaptureBasePixelWidth = 1500;
    private const double CaptureDeviceScale = 1d;
    private const int MaximumRasterDetailOffset = 1;
    private static readonly int CaptureBasePixelHeight = (int)Math.Round(
        CaptureBasePixelWidth *
        BuildingArchitectureConceptPageLayout.SiteContextLocationMapArea.Height /
        BuildingArchitectureConceptPageLayout.SiteContextLocationMapArea.Width);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly string projectFolder;
    private readonly string projectId;
    private readonly string googleMapsApiKey =
        Environment.GetEnvironmentVariable("ERKS_GOOGLE_MAPS_API_KEY")?.Trim() ?? "";
    private readonly string azureMapsKey =
        Environment.GetEnvironmentVariable("ERKS_AZURE_MAPS_SUBSCRIPTION_KEY")?.Trim() ?? "";
    private readonly ProjectSiteContextMap workingCopy;
    private readonly Grid mapsGrid = new();
    private readonly Border pageBorder = new();
    private readonly Canvas pageCanvas = new();
    private readonly Image pageBackground = new();
    private readonly StackPanel editingTools = new() { Orientation = Orientation.Horizontal };
    private readonly WrapPanel annotationTools = new()
    {
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 0, 0, 8),
    };
    private readonly ComboBox providerBox = new() { Width = 210, Margin = new Thickness(8, 0, 8, 0) };
    private readonly ComboBox resolutionBox = new() { Width = 160, Margin = new Thickness(8, 0, 8, 0) };
    private readonly Button editButton = StudioWidgets.CreateIconTextButton("icon-project.svg", "Засварлах");
    private readonly Button saveEditButton = StudioWidgets.CreateIconTextButton("icon-project.svg", "Хадгалах");
    private readonly Button cancelEditButton = StudioWidgets.CreateButton("Болих");
    private readonly Button doneButton = StudioWidgets.CreateButton("Дуусгах");
    private readonly Button panToolButton = StudioWidgets.CreateIconButton(
        "icon-pan.svg",
        "Газрын зургийг хөдөлгөх",
        "↔");
    private readonly Button landmarkToolButton = StudioWidgets.CreateIconButton(
        "icon-location-marker.svg",
        "Онцгой байршил тэмдэглэх",
        "●");
    private readonly Button distanceToolButton = StudioWidgets.CreateIconButton(
        "icon-distance.svg",
        "Зам дагуулж зай хэмжих",
        "⌁");
    private readonly Button radiusToolButton = StudioWidgets.CreateIconButton(
        "icon-radius.svg",
        "Радиусын цагираг байрлуулах",
        "○");
    private readonly Button finishDrawingButton = StudioWidgets.CreateButton("Шугам дуусгах");
    private readonly Button deleteAnnotationButton = StudioWidgets.CreateButton("Хасах");
    private readonly ComboBox annotationBox = new()
    {
        Width = 210,
        Margin = new Thickness(4, 0, 8, 4),
    };
    private readonly TextBlock annotationNameLabel = CreateToolLabel("Нэр");
    private readonly TextBox annotationNameBox = new()
    {
        Width = 180,
        Margin = new Thickness(4, 0, 8, 4),
        ToolTip = "Таних тэмдэгт харагдах нэр",
    };
    private readonly TextBlock annotationColorLabel = CreateToolLabel("Өнгө");
    private readonly ComboBox annotationColorBox = new()
    {
        Width = 118,
        Margin = new Thickness(4, 0, 8, 4),
        ToolTip = "Өнгө",
    };
    private readonly TextBlock annotationScaleLabel = CreateToolLabel("Хэмжээ");
    private readonly Slider annotationScaleSlider = new()
    {
        Minimum = 0.65,
        Maximum = 1.8,
        Value = 1,
        Width = 100,
        TickFrequency = 0.05,
        IsSnapToTickEnabled = true,
        Margin = new Thickness(4, 0, 4, 4),
    };
    private readonly TextBlock annotationScaleValue = new()
    {
        Width = 38,
        VerticalAlignment = VerticalAlignment.Center,
        Foreground = StudioTheme.MutedTextBrush,
        Margin = new Thickness(0, 0, 8, 4),
    };
    private readonly TextBlock annotationFeedbackText = new()
    {
        MinWidth = 280,
        MaxWidth = 520,
        Padding = new Thickness(10, 6, 10, 6),
        Margin = new Thickness(8, 0, 0, 4),
        Foreground = StudioTheme.MutedTextBrush,
        Background = new SolidColorBrush(Color.FromArgb(28, 54, 162, 105)),
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBox radiusMetersBox = new()
    {
        Width = 92,
        Text = "500",
        Margin = new Thickness(4, 0, 8, 4),
        ToolTip = "Радиус, метрээр",
    };
    private readonly ComboBox radiusRingBox = new()
    {
        Width = 132,
        Margin = new Thickness(4, 0, 4, 4),
        ToolTip = "Засах эсвэл хасах радиусаа сонгоно.",
    };
    private readonly TextBlock radiusRingLabel = CreateToolLabel("Цагираг");
    private readonly TextBlock radiusMetersLabel = CreateToolLabel("Радиус (м)");
    private readonly Button addRadiusButton = StudioWidgets.CreateButton("+");
    private readonly Button removeRadiusButton = StudioWidgets.CreateButton("−");
    private readonly CheckBox projectSiteBox = new()
    {
        Content = "Төслийн байршил",
        Margin = new Thickness(4, 4, 10, 4),
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock selectedTitle = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        FontWeight = FontWeights.SemiBold,
    };
    private readonly TextBlock statusText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        VerticalAlignment = VerticalAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly List<MapProviderChoice> providers;
    private readonly MapPane locationPane;
    private readonly MapPane overviewPane;
    private MapPane? selectedPane;
    private MapPane? editingPane;
    private ProjectMapViewport? editBaseline;
    private bool bindingProvider;
    private bool bindingAnnotations;
    private string activeToolMode = "pan";
    private bool mapsInitialized;
    private bool initializationStarted;
    private bool disposed;

    public SiteContextMapEditorControl(
        string projectFolder,
        string projectId,
        ProjectSiteContextMap source,
        ImageSource? pageBackgroundSource = null)
    {
        this.projectFolder = Path.GetFullPath(projectFolder);
        this.projectId = projectId.Trim();
        workingCopy = source.CreateProjectSnapshot(this.projectId);
        workingCopy.ConfigureForProject(this.projectId);

        providers =
        [
            new(ProjectMapProviderIds.OpenStreetMap, "OpenStreetMap", true, ""),
            new(ProjectMapProviderIds.OpenTopoMap, "OpenTopoMap", true, ""),
            new(ProjectMapProviderIds.GoogleRoad, "Google Maps", googleMapsApiKey.Length > 0,
                "ERKS_GOOGLE_MAPS_API_KEY шаардлагатай."),
            new(ProjectMapProviderIds.GoogleSatellite, "Google Satellite", googleMapsApiKey.Length > 0,
                "ERKS_GOOGLE_MAPS_API_KEY шаардлагатай."),
            new(ProjectMapProviderIds.AzureRoad, "Bing / Azure Map", azureMapsKey.Length > 0,
                "ERKS_AZURE_MAPS_SUBSCRIPTION_KEY шаардлагатай."),
            new(ProjectMapProviderIds.AzureAerial, "Bing / Azure Aerial", azureMapsKey.Length > 0,
                "ERKS_AZURE_MAPS_SUBSCRIPTION_KEY шаардлагатай."),
        ];

        locationPane = CreateMapPane(
            ProjectMapViewportKinds.LocationScheme,
            "БАЙРШЛЫН СХЕМ",
            workingCopy.LocationScheme);
        overviewPane = CreateMapPane(
            ProjectMapViewportKinds.SurroundingsOverview,
            "ОРЧНЫ ТОЙМ",
            workingCopy.SurroundingsOverview);

        pageBackground.Source = pageBackgroundSource;
        Content = BuildContent();
        SelectPane(locationPane);
        Loaded += HandleLoaded;
    }

    public bool HasSavedChanges { get; private set; }

    public ProjectSiteContextMap Result => workingCopy.CreateProjectSnapshot(projectId);

    public event Action<ProjectSiteContextMap>? SiteContextSaved;

    public event Action<bool>? Completed;

    private UIElement BuildContent()
    {
        var root = new Grid { Margin = new Thickness(10) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var commandBar = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 8),
        };
        editButton.Click += async (_, _) => await BeginEditAsync();
        DockPanel.SetDock(editButton, Dock.Right);
        commandBar.Children.Add(editButton);

        editingTools.Visibility = Visibility.Collapsed;
        providerBox.ItemsSource = providers;
        providerBox.SelectionChanged += async (_, _) => await ChangeProviderAsync();
        var zoomOut = StudioWidgets.CreateButton("−");
        zoomOut.Width = 38;
        zoomOut.ToolTip = "Холдуулах";
        zoomOut.Click += async (_, _) => await ZoomAsync(-1);
        var zoomIn = StudioWidgets.CreateButton("+");
        zoomIn.Width = 38;
        zoomIn.ToolTip = "Ойртуулах";
        zoomIn.Click += async (_, _) => await ZoomAsync(1);
        saveEditButton.Background = StudioTheme.AccentBrush;
        saveEditButton.BorderBrush = StudioTheme.AccentBrush;
        saveEditButton.Click += async (_, _) => await SaveSelectedMapAsync();
        cancelEditButton.Click += async (_, _) => await CancelEditAsync();
        editingTools.Children.Add(selectedTitle);
        editingTools.Children.Add(providerBox);
        editingTools.Children.Add(zoomOut);
        editingTools.Children.Add(zoomIn);
        editingTools.Children.Add(new TextBlock
        {
            Text = "Нарийвчлал",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StudioTheme.MutedTextBrush,
            Margin = new Thickness(10, 0, 0, 0),
        });
        editingTools.Children.Add(resolutionBox);
        editingTools.Children.Add(saveEditButton);
        editingTools.Children.Add(cancelEditButton);
        commandBar.Children.Add(editingTools);
        root.Children.Add(commandBar);

        ConfigureAnnotationTools();
        Grid.SetRow(annotationTools, 1);
        root.Children.Add(annotationTools);

        mapsGrid.Background = new SolidColorBrush(Color.FromRgb(54, 58, 64));
        mapsGrid.ClipToBounds = true;
        mapsGrid.SizeChanged += (_, _) => LayoutPageSurface();

        pageBackground.Stretch = Stretch.Fill;
        pageBackground.IsHitTestVisible = false;
        pageCanvas.Background = Brushes.White;
        pageCanvas.Children.Add(pageBackground);
        pageCanvas.Children.Add(locationPane.Container);
        pageCanvas.Children.Add(overviewPane.Container);

        pageBorder.Background = Brushes.White;
        pageBorder.BorderBrush = Brushes.Black;
        pageBorder.BorderThickness = new Thickness(1);
        pageBorder.HorizontalAlignment = HorizontalAlignment.Center;
        pageBorder.VerticalAlignment = VerticalAlignment.Center;
        pageBorder.Child = pageCanvas;
        pageBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
        {
            BlurRadius = 10,
            ShadowDepth = 3,
            Opacity = 0.35,
        };
        mapsGrid.Children.Add(pageBorder);
        Grid.SetRow(mapsGrid, 2);
        root.Children.Add(mapsGrid);

        var footer = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        doneButton.Click += (_, _) =>
        {
            if (editingPane is not null)
                return;
            Completed?.Invoke(HasSavedChanges);
        };
        DockPanel.SetDock(doneButton, Dock.Right);
        footer.Children.Add(doneButton);
        footer.Children.Add(statusText);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        return root;
    }

    private void ConfigureAnnotationTools()
    {
        annotationColorBox.Items.Clear();
        foreach (MapColorChoice choice in MapColorChoice.Defaults)
        {
            var swatch = new Border
            {
                Width = 16,
                Height = 16,
                Background = new SolidColorBrush(choice.Color),
                BorderBrush = StudioTheme.BorderBrush,
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 6, 0),
            };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(swatch);
            content.Children.Add(new TextBlock
            {
                Text = choice.Name,
                VerticalAlignment = VerticalAlignment.Center,
            });
            annotationColorBox.Items.Add(new ComboBoxItem
            {
                Tag = choice.Hex,
                Content = content,
            });
        }
        annotationColorBox.SelectedIndex = 0;

        panToolButton.Click += async (_, _) => await SetAnnotationToolAsync("pan");
        landmarkToolButton.Click += async (_, _) => await SetAnnotationToolAsync("landmark");
        distanceToolButton.Click += async (_, _) => await SetAnnotationToolAsync("distance");
        radiusToolButton.Click += async (_, _) => await SetAnnotationToolAsync("radius");
        finishDrawingButton.Click += async (_, _) => await FinishDistanceAsync();
        deleteAnnotationButton.Click += async (_, _) => await DeleteSelectedAnnotationAsync();
        annotationBox.SelectionChanged += async (_, _) => await SelectAnnotationAsync();
        annotationNameBox.TextChanged += async (_, _) => await ApplyAnnotationInputsAsync();
        annotationNameBox.LostFocus += async (_, _) => await ApplyAnnotationInputsAsync();
        annotationColorBox.SelectionChanged += async (_, _) => await ApplyAnnotationInputsAsync();
        annotationScaleSlider.ValueChanged += async (_, _) =>
        {
            UpdateScaleText();
            await ApplyAnnotationInputsAsync();
        };
        radiusMetersBox.KeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter)
                return;
            args.Handled = true;
            await ApplyAnnotationInputsAsync();
        };
        radiusMetersBox.LostFocus += async (_, _) => await ApplyAnnotationInputsAsync();
        radiusRingBox.SelectionChanged += async (_, _) => await SelectRadiusRingAsync();
        addRadiusButton.Click += async (_, _) => await AddRadiusAsync();
        removeRadiusButton.Click += async (_, _) => await RemoveSelectedRadiusAsync();
        addRadiusButton.ToolTip =
            "Шинэ цагираг нэмнэ. Ижил радиус байвал дараагийн боломжит хэмжээг автоматаар сонгоно.";
        removeRadiusButton.ToolTip = "Сонгосон радиусыг хасна.";
        projectSiteBox.Click += async (_, _) => await ApplyAnnotationInputsAsync();

        annotationTools.Children.Add(CreateToolLabel("Багаж"));
        annotationTools.Children.Add(panToolButton);
        annotationTools.Children.Add(landmarkToolButton);
        annotationTools.Children.Add(distanceToolButton);
        annotationTools.Children.Add(radiusToolButton);
        annotationTools.Children.Add(finishDrawingButton);
        annotationTools.Children.Add(CreateToolLabel("Сонголт"));
        annotationTools.Children.Add(annotationBox);
        annotationTools.Children.Add(annotationNameLabel);
        annotationTools.Children.Add(annotationNameBox);
        annotationTools.Children.Add(annotationColorLabel);
        annotationTools.Children.Add(annotationColorBox);
        annotationTools.Children.Add(annotationScaleLabel);
        annotationTools.Children.Add(annotationScaleSlider);
        annotationTools.Children.Add(annotationScaleValue);
        annotationTools.Children.Add(projectSiteBox);
        annotationTools.Children.Add(radiusRingLabel);
        annotationTools.Children.Add(radiusRingBox);
        annotationTools.Children.Add(radiusMetersLabel);
        annotationTools.Children.Add(radiusMetersBox);
        annotationTools.Children.Add(addRadiusButton);
        annotationTools.Children.Add(removeRadiusButton);
        annotationTools.Children.Add(deleteAnnotationButton);
        annotationTools.Children.Add(annotationFeedbackText);

        UpdateAnnotationToolUi();
    }

    private static TextBlock CreateToolLabel(string text) => new()
    {
        Text = text,
        Foreground = StudioTheme.MutedTextBrush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(6, 0, 0, 4),
    };

    private void LayoutPageSurface()
    {
        const double outerPadding = 24.0;
        double availableWidth = Math.Max(1, mapsGrid.ActualWidth - outerPadding);
        double availableHeight = Math.Max(1, mapsGrid.ActualHeight - outerPadding);
        double scale = Math.Min(
            availableWidth / BuildingArchitectureConceptPageLayout.PageWidthMm,
            availableHeight / BuildingArchitectureConceptPageLayout.PageHeightMm);
        if (!double.IsFinite(scale) || scale <= 0)
            return;

        double pageWidth = BuildingArchitectureConceptPageLayout.PageWidthMm * scale;
        double pageHeight = BuildingArchitectureConceptPageLayout.PageHeightMm * scale;
        pageCanvas.Width = pageWidth;
        pageCanvas.Height = pageHeight;
        pageBorder.Width = pageWidth;
        pageBorder.Height = pageHeight;
        pageBackground.Width = pageWidth;
        pageBackground.Height = pageHeight;

        LayoutMapPane(
            locationPane,
            BuildingArchitectureConceptPageLayout.SiteContextLocationPanel,
            scale);
        LayoutMapPane(
            overviewPane,
            BuildingArchitectureConceptPageLayout.SiteContextOverviewPanel,
            scale);
    }

    private static void LayoutMapPane(MapPane pane, PageRectMm panel, double scale)
    {
        pane.Container.Width = panel.Width * scale;
        pane.Container.Height = panel.Height * scale;
        Canvas.SetLeft(pane.Container, panel.X * scale);
        Canvas.SetTop(pane.Container, panel.Y * scale);
        pane.Header.Height = BuildingArchitectureConceptPageLayout.SiteContextPanelTitleHeightMm * scale;
        pane.HeaderTitle.FontSize = Math.Clamp(3.0 * scale, 9.0, 14.0);
        pane.LockState.FontSize = Math.Clamp(2.5 * scale, 8.0, 12.0);
        double horizontalMargin = Math.Clamp(4.0 * scale, 6.0, 12.0);
        pane.HeaderTitle.Margin = new Thickness(horizontalMargin, 0, horizontalMargin, 0);
        pane.LockState.Margin = new Thickness(horizontalMargin, 0, horizontalMargin, 0);
    }

    private MapPane CreateMapPane(string kind, string title, ProjectMapViewport viewport)
    {
        var headerTitle = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var lockState = new TextBlock
        {
            Text = "Түгжээтэй",
            Foreground = StudioTheme.MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var header = new DockPanel
        {
            Height = 42,
            Background = StudioTheme.PanelAltBrush,
            Margin = new Thickness(0),
        };
        DockPanel.SetDock(lockState, Dock.Right);
        lockState.Margin = new Thickness(12, 0, 12, 0);
        header.Children.Add(lockState);
        headerTitle.Margin = new Thickness(12, 0, 12, 0);
        header.Children.Add(headerTitle);

        var webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        var shield = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)),
            Cursor = Cursors.Hand,
        };
        var mapHost = new Grid { Background = StudioTheme.InputBrush };
        mapHost.Children.Add(webView);
        mapHost.Children.Add(shield);

        var content = new Grid();
        content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(header);
        Grid.SetRow(mapHost, 1);
        content.Children.Add(mapHost);

        var container = new Border
        {
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(0),
            Child = content,
        };
        var pane = new MapPane(
            kind,
            title,
            viewport,
            container,
            header,
            headerTitle,
            webView,
            shield,
            lockState);
        shield.MouseLeftButtonDown += (_, _) =>
        {
            if (editingPane is null)
                SelectPane(pane);
        };
        header.MouseLeftButtonDown += (_, _) =>
        {
            if (editingPane is null)
                SelectPane(pane);
        };
        return pane;
    }

    private void SelectPane(MapPane pane)
    {
        if (editingPane is not null)
            return;
        selectedPane = pane;
        foreach (MapPane item in AllPanes())
        {
            bool selected = ReferenceEquals(item, pane);
            item.Header.Background = selected ? StudioTheme.AccentBrush : StudioTheme.PanelAltBrush;
        }
        editButton.IsEnabled = mapsInitialized && pane.IsInitialized;
        statusText.Text = pane.IsInitialized ? "" : "Газрын зураг ачаалж байна...";
    }

    private async void HandleLoaded(object sender, RoutedEventArgs e)
    {
        LayoutPageSurface();
        if (initializationStarted)
            return;
        initializationStarted = true;
        await InitializeMapsAsync();
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        Loaded -= HandleLoaded;
        foreach (MapPane pane in AllPanes())
        {
            pane.WebView.Dispose();
        }
    }

    private async Task InitializeMapsAsync()
    {
        try
        {
            string runtimePath = SiteContextMapRuntimeAsset.EnsureExtracted();
            await Task.WhenAll(
                InitializePaneAsync(locationPane, runtimePath),
                InitializePaneAsync(overviewPane, runtimePath));
            mapsInitialized = true;
            editButton.IsEnabled = true;
            statusText.Text = "";
        }
        catch (Exception exception)
        {
            statusText.Text = $"Газрын зураг ачаалсангүй: {exception.Message}";
        }
        finally
        {
            if (selectedPane is not null)
                SelectPane(selectedPane);
        }
    }

    private async Task InitializePaneAsync(MapPane pane, string runtimePath)
    {
        await pane.WebView.EnsureCoreWebView2Async();
        CoreWebView2 core = pane.WebView.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = false;
        core.Settings.AreDevToolsEnabled = false;
        core.Settings.IsStatusBarEnabled = false;
        core.Settings.UserAgent = $"{core.Settings.UserAgent} Erk-S-Studio/0.1";
        core.WebMessageReceived += HandleMapWebMessage;

        string assetsFolder = Path.GetDirectoryName(runtimePath)
            ?? throw new InvalidOperationException("Map runtime folder could not be resolved.");
        core.SetVirtualHostNameToFolderMapping(
            "app.erks.local",
            assetsFolder,
            CoreWebView2HostResourceAccessKind.Allow);
        bool mappedNavigationSucceeded = await NavigateAsync(
            core,
            () => core.Navigate("https://app.erks.local/site-context-map.html"));
        if (!mappedNavigationSucceeded)
        {
            string embeddedHtml = SiteContextMapRuntimeAsset.ReadEmbeddedHtml();
            bool embeddedNavigationSucceeded = await NavigateAsync(
                core,
                () => core.NavigateToString(embeddedHtml));
            if (!embeddedNavigationSucceeded)
                throw new InvalidOperationException("Map page navigation failed.");
        }

        var initialization = new MapInitialization
        {
            Viewport = MapState.FromViewport(pane.Viewport),
            Boundary = workingCopy.Boundary.Clone(),
            PlanFeatures = workingCopy.PlanFeatures.Clone(),
            Editing = false,
            GoogleApiKey = googleMapsApiKey,
            AzureMapsKey = azureMapsKey,
        };
        string payload = JsonSerializer.Serialize(initialization, JsonOptions);
        await core.ExecuteScriptAsync($"window.erks.init({payload});");
        await WaitForReadyAsync(pane);
        pane.IsInitialized = true;
        await SetMapEditingAsync(pane, false);
    }

    private static async Task<bool> NavigateAsync(CoreWebView2 core, Action navigate)
    {
        var navigation = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Navigated(object? sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            navigation.TrySetResult(args.IsSuccess);
        }

        core.NavigationCompleted += Navigated;
        try
        {
            navigate();
            return await navigation.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        finally
        {
            core.NavigationCompleted -= Navigated;
        }
    }

    private static async Task WaitForReadyAsync(MapPane pane)
    {
        for (int attempt = 0; attempt < 80; attempt++)
        {
            MapState? state = await GetMapStateAsync(pane);
            if (state?.Ready == true)
                return;
            await Task.Delay(100);
        }
        throw new TimeoutException($"{pane.Title} газрын зураг хугацаандаа ачаалсангүй.");
    }

    private async Task BeginEditAsync()
    {
        if (selectedPane is null || !selectedPane.IsInitialized || editingPane is not null)
            return;
        editingPane = selectedPane;
        editBaseline = editingPane.Viewport.Clone();
        selectedTitle.Text = editingPane.Title;
        BindProvider(editingPane.Viewport.ProviderId);
        BindResolution(editingPane.Viewport);
        editButton.Visibility = Visibility.Collapsed;
        editingTools.Visibility = Visibility.Visible;
        annotationTools.Visibility = Visibility.Visible;
        doneButton.IsEnabled = false;
        editingPane.LockState.Text = "Засварлаж байна";
        editingPane.LockState.Foreground = StudioTheme.AccentSoftBrush;
        editingPane.Shield.Visibility = Visibility.Collapsed;
        foreach (MapPane pane in AllPanes().Where(pane => !ReferenceEquals(pane, editingPane)))
            pane.Shield.Visibility = Visibility.Visible;
        await SetMapEditingAsync(editingPane, true);
        activeToolMode = "pan";
        await SetAnnotationToolAsync(activeToolMode);
        MapState? state = await GetMapStateAsync(editingPane);
        if (state is not null)
            RefreshAnnotationControls(state);
        statusText.Text = "";
    }

    private async Task ChangeProviderAsync()
    {
        if (bindingProvider || editingPane is null || providerBox.SelectedItem is not MapProviderChoice choice)
            return;
        if (!choice.IsAvailable)
        {
            statusText.Text = choice.UnavailableReason;
            BindProvider(editingPane.Viewport.ProviderId);
            return;
        }

        try
        {
            saveEditButton.IsEnabled = false;
            string provider = JsonSerializer.Serialize(choice.Id);
            await editingPane.WebView.CoreWebView2.ExecuteScriptAsync(
                $"window.erks.setProvider({provider});");
            await WaitForReadyAsync(editingPane);
            editingPane.Viewport.ProviderId = choice.Id;
            MapState? state = await GetMapStateAsync(editingPane);
            BindResolution(
                state?.Zoom ?? editingPane.Viewport.Zoom,
                choice.Id,
                SelectedResolutionOffset());
            statusText.Text = "";
        }
        catch (Exception exception)
        {
            statusText.Text = $"Provider солигдсонгүй: {exception.Message}";
            BindProvider(editingPane.Viewport.ProviderId);
        }
        finally
        {
            saveEditButton.IsEnabled = true;
        }
    }

    private async Task ZoomAsync(int delta)
    {
        if (editingPane is null)
            return;
        int selectedOffset = SelectedResolutionOffset();
        await editingPane.WebView.CoreWebView2.ExecuteScriptAsync($"window.erks.zoomBy({delta});");
        await Task.Delay(80);
        MapState? state = await GetMapStateAsync(editingPane);
        if (state is not null)
        {
            BindResolution(
                state.Zoom,
                state.ProviderId,
                selectedOffset);
        }
    }

    private async Task SetAnnotationToolAsync(string mode)
    {
        if (editingPane is null)
            return;
        activeToolMode = mode is "landmark" or "distance" or "radius" ? mode : "pan";
        bool startsNewLandmark = activeToolMode == "landmark";
        if (startsNewLandmark)
        {
            bindingAnnotations = true;
            annotationBox.SelectedItem = null;
            annotationNameBox.Text = "";
            projectSiteBox.IsChecked = false;
            bindingAnnotations = false;
        }
        UpdateAnnotationToolUi();
        string modeJson = JsonSerializer.Serialize(activeToolMode);
        string optionsJson = JsonSerializer.Serialize(
            CurrentAnnotationOptions(clearLandmarkName: startsNewLandmark),
            JsonOptions);
        string clearSelectionScript = startsNewLandmark
            ? "window.erks.selectAnnotation('', '', false);"
            : "";
        await editingPane.WebView.CoreWebView2.ExecuteScriptAsync(
            $"{clearSelectionScript}window.erks.setTool({modeJson}, {optionsJson});");
        string message = activeToolMode == "pan"
            ? "Газрын зургийг хөдөлгөх горим."
            : activeToolMode == "landmark"
                ? "Шинэ байршлын тэмдэг бэлэн · нэрээ оруулаад газрын зураг дээр дарна."
                : $"{ToolDisplayName(activeToolMode)} бэлэн · тохиргоог газрын зураг дээр байрлуулна.";
        ShowAnnotationFeedback(message);
    }

    private async Task FinishDistanceAsync()
    {
        if (editingPane is null)
            return;
        await editingPane.WebView.CoreWebView2.ExecuteScriptAsync(
            "window.erks.finishDrawing();");
        activeToolMode = "pan";
        await SetAnnotationToolAsync(activeToolMode);
    }

    private async Task SelectAnnotationAsync()
    {
        if (bindingAnnotations || editingPane is null)
            return;
        AnnotationChoice? choice = annotationBox.SelectedItem as AnnotationChoice;
        string kind = JsonSerializer.Serialize(choice?.Kind ?? "");
        string id = JsonSerializer.Serialize(choice?.Id ?? "");
        await editingPane.WebView.CoreWebView2.ExecuteScriptAsync(
            $"window.erks.selectAnnotation({kind}, {id});");
    }

    private async Task SelectRadiusRingAsync()
    {
        if (bindingAnnotations ||
            editingPane is null ||
            annotationBox.SelectedItem is not AnnotationChoice { Kind: "radius" } annotation ||
            radiusRingBox.SelectedItem is not RadiusChoice radius)
        {
            return;
        }

        string id = JsonSerializer.Serialize(annotation.Id);
        await editingPane.WebView.CoreWebView2.ExecuteScriptAsync(
            $"window.erks.selectRadiusRing({id}, {radius.Index});");
    }

    private async Task AddRadiusAsync()
    {
        if (editingPane is null ||
            annotationBox.SelectedItem is not AnnotationChoice { Kind: "radius" })
        {
            return;
        }

        double radius = CurrentAnnotationOptions().RadiusMeters;
        string value = radius.ToString(CultureInfo.InvariantCulture);
        await editingPane.WebView.CoreWebView2.ExecuteScriptAsync(
            $"window.erks.addRadius({value});");
    }

    private async Task RemoveSelectedRadiusAsync()
    {
        if (editingPane is null ||
            annotationBox.SelectedItem is not AnnotationChoice { Kind: "radius" })
        {
            return;
        }

        await editingPane.WebView.CoreWebView2.ExecuteScriptAsync(
            "window.erks.removeSelectedRadius();");
    }

    private async Task DeleteSelectedAnnotationAsync()
    {
        if (editingPane is null || annotationBox.SelectedItem is not AnnotationChoice)
            return;
        await editingPane.WebView.CoreWebView2.ExecuteScriptAsync(
            "window.erks.deleteSelectedAnnotation();");
    }

    private async Task ApplyAnnotationInputsAsync()
    {
        if (bindingAnnotations || editingPane is null)
            return;

        AnnotationChoice? selected = annotationBox.SelectedItem as AnnotationChoice;
        AnnotationToolOptions selectedOptions = CurrentAnnotationOptions();
        AnnotationToolOptions toolOptions = CurrentAnnotationOptions(
            clearLandmarkName: selected?.Kind == "landmark");
        string selectedOptionsJson = JsonSerializer.Serialize(selectedOptions, JsonOptions);
        string toolOptionsJson = JsonSerializer.Serialize(toolOptions, JsonOptions);
        string mode = JsonSerializer.Serialize(activeToolMode);
        string updateSelectionScript = selected is null
            ? ""
            : $"window.erks.updateSelectedAnnotation({selectedOptionsJson});";
        await editingPane.WebView.CoreWebView2.ExecuteScriptAsync(
            $"window.erks.setTool({mode}, {toolOptionsJson});" +
            updateSelectionScript);
        string message = selected is null && activeToolMode == "landmark"
            ? $"Нэр бэлэн: {DisplayAnnotationName(selectedOptions.Name)} · газрын зураг дээр дарж байрлуулна."
            : selected is null
                ? $"{ToolDisplayName(activeToolMode)}-ийн тохиргоо бэлэн."
            : $"Тохиргоо хэрэглэгдлээ · {selected.DisplayName}";
        ShowAnnotationFeedback(message, confirmed: true);
    }

    private static string DisplayAnnotationName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? "Нэргүй" : $"“{name.Trim()}”";

    private void ShowAnnotationFeedback(string message, bool confirmed = false)
    {
        annotationFeedbackText.Text = message;
        annotationFeedbackText.Foreground = confirmed
            ? StudioTheme.SuccessBrush
            : StudioTheme.MutedTextBrush;
        statusText.Text = message;
    }

    private AnnotationToolOptions CurrentAnnotationOptions(bool clearLandmarkName = false)
    {
        double radius = 500d;
        string radiusText = radiusMetersBox.Text.Trim();
        if (!double.TryParse(
                radiusText,
                NumberStyles.Float,
                CultureInfo.CurrentCulture,
                out radius) &&
            !double.TryParse(
                radiusText,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out radius))
        {
            radius = 500d;
        }

        return new AnnotationToolOptions
        {
            Name = clearLandmarkName ? "" : annotationNameBox.Text.Trim(),
            Color = SelectedAnnotationColor(),
            Size = Math.Clamp(annotationScaleSlider.Value, 0.65d, 1.8d),
            StrokeWidth = Math.Clamp(annotationScaleSlider.Value, 0.65d, 2.5d),
            RadiusMeters = Math.Clamp(radius, 1d, 1_000_000d),
            IsProjectSite = projectSiteBox.IsChecked == true,
        };
    }

    private static string ToolDisplayName(string mode) => mode switch
    {
        "landmark" => "Байршлын тэмдэг",
        "distance" => "Зайн хэмжээс",
        "radius" => "Радиусын хэмжээс",
        _ => "Зөөх горим",
    };

    private string SelectedAnnotationColor() =>
        (annotationColorBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "#e5484d";

    private void SelectAnnotationColor(string color)
    {
        ComboBoxItem? match = annotationColorBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(
                item.Tag as string,
                color,
                StringComparison.OrdinalIgnoreCase));
        annotationColorBox.SelectedItem = match ?? annotationColorBox.Items[0];
    }

    private void HandleMapWebMessage(
        object? sender,
        CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (editingPane is null ||
            sender is not CoreWebView2 core ||
            !ReferenceEquals(editingPane.WebView.CoreWebView2, core))
        {
            return;
        }

        try
        {
            MapState? state = JsonSerializer.Deserialize<MapState>(
                args.WebMessageAsJson,
                JsonOptions);
            if (state is null)
                return;
            Dispatcher.Invoke(() => RefreshAnnotationControls(state));
        }
        catch
        {
        }
    }

    private void RefreshAnnotationControls(MapState state)
    {
        bindingAnnotations = true;
        try
        {
            var choices = new List<AnnotationChoice>();
            choices.AddRange(state.Landmarks
                .OrderBy(item => item.Number)
                .Select(item => new AnnotationChoice(
                    "landmark",
                    item.Id,
                    $"{item.Number:00} · " +
                    (string.IsNullOrWhiteSpace(item.Name)
                        ? item.IsProjectSite ? "Төслийн байршил" : "Онцгой байршил"
                        : item.Name))));
            choices.AddRange(state.DistanceMeasures.Select(item => new AnnotationChoice(
                "distance",
                item.Id,
                "Зайн хэмжээс")));
            choices.AddRange(state.RadiusMeasures.Select(item => new AnnotationChoice(
                "radius",
                item.Id,
                $"Цагираг · {Math.Max(1, item.RadiiMeters.Count)}")));

            annotationBox.ItemsSource = choices;
            AnnotationChoice? selected = choices.FirstOrDefault(item =>
                item.Kind.Equals(state.SelectedAnnotationKind, StringComparison.Ordinal) &&
                item.Id.Equals(state.SelectedAnnotationId, StringComparison.Ordinal));
            annotationBox.SelectedItem = selected;
            deleteAnnotationButton.IsEnabled = selected is not null;
            if (!string.IsNullOrWhiteSpace(state.AnnotationStatusMessage))
                ShowAnnotationFeedback(state.AnnotationStatusMessage, confirmed: true);

            if (selected is null)
            {
                radiusRingBox.ItemsSource = null;
                if (activeToolMode == "landmark")
                    annotationNameBox.Text = state.AnnotationToolName;
                return;
            }

            if (selected.Kind == "landmark")
            {
                radiusRingBox.ItemsSource = null;
                ProjectMapLandmark item = state.Landmarks.First(value => value.Id == selected.Id);
                annotationNameBox.Text = item.Name;
                SelectAnnotationColor(item.Color);
                annotationScaleSlider.Maximum = 1.8d;
                annotationScaleSlider.Value = item.Size;
                projectSiteBox.IsChecked = item.IsProjectSite;
            }
            else if (selected.Kind == "distance")
            {
                radiusRingBox.ItemsSource = null;
                ProjectMapDistanceMeasure item =
                    state.DistanceMeasures.First(value => value.Id == selected.Id);
                annotationNameBox.Text = "";
                SelectAnnotationColor(item.Color);
                projectSiteBox.IsChecked = false;
            }
            else
            {
                ProjectMapRadiusMeasure item =
                    state.RadiusMeasures.First(value => value.Id == selected.Id);
                annotationNameBox.Text = "";
                List<RadiusChoice> radiusChoices = item.RadiiMeters
                    .Select((value, index) => new RadiusChoice(
                        index,
                        value,
                        index < item.RingColors.Count
                            ? item.RingColors[index]
                            : item.Color))
                    .Where(choice =>
                        double.IsFinite(choice.RadiusMeters) &&
                        choice.RadiusMeters > 0)
                    .ToList();
                if (radiusChoices.Count == 0)
                {
                    radiusChoices.Add(new RadiusChoice(
                        0,
                        Math.Clamp(item.RadiusMeters, 1d, 1_000_000d),
                        item.Color));
                }
                radiusRingBox.ItemsSource = radiusChoices;
                int selectedRadiusIndex = Math.Clamp(
                    state.SelectedRadiusIndex,
                    0,
                    radiusChoices.Count - 1);
                radiusRingBox.SelectedItem = radiusChoices[selectedRadiusIndex];
                radiusMetersBox.Text = radiusChoices[selectedRadiusIndex]
                    .RadiusMeters
                    .ToString("0.##", CultureInfo.CurrentCulture);
                SelectAnnotationColor(radiusChoices[selectedRadiusIndex].Color);
                projectSiteBox.IsChecked = false;
            }
        }
        finally
        {
            bindingAnnotations = false;
            UpdateScaleText();
            UpdateAnnotationToolUi();
        }
    }

    private void UpdateScaleText() =>
        annotationScaleValue.Text = $"{annotationScaleSlider.Value:0.00}×";

    private void UpdateAnnotationToolUi()
    {
        SetToolButtonState(panToolButton, activeToolMode == "pan");
        SetToolButtonState(landmarkToolButton, activeToolMode == "landmark");
        SetToolButtonState(distanceToolButton, activeToolMode == "distance");
        SetToolButtonState(radiusToolButton, activeToolMode == "radius");
        finishDrawingButton.Visibility = activeToolMode == "distance"
            ? Visibility.Visible
            : Visibility.Collapsed;

        string effectiveKind = (annotationBox.SelectedItem as AnnotationChoice)?.Kind
            ?? activeToolMode;
        bool marker = effectiveKind == "landmark";
        bool distance = effectiveKind == "distance";
        bool radius = effectiveKind == "radius";
        bool annotationKind = marker || distance || radius;
        bool selectedRadius =
            annotationBox.SelectedItem is AnnotationChoice { Kind: "radius" };

        annotationNameLabel.Visibility = marker ? Visibility.Visible : Visibility.Collapsed;
        annotationNameBox.Visibility = marker ? Visibility.Visible : Visibility.Collapsed;
        annotationColorLabel.Visibility = annotationKind
            ? Visibility.Visible
            : Visibility.Collapsed;
        annotationColorBox.Visibility = annotationKind
            ? Visibility.Visible
            : Visibility.Collapsed;
        annotationScaleLabel.Visibility = marker ? Visibility.Visible : Visibility.Collapsed;
        annotationScaleSlider.Visibility = marker ? Visibility.Visible : Visibility.Collapsed;
        annotationScaleValue.Visibility = marker ? Visibility.Visible : Visibility.Collapsed;
        projectSiteBox.Visibility = marker ? Visibility.Visible : Visibility.Collapsed;

        radiusRingLabel.Visibility = selectedRadius
            ? Visibility.Visible
            : Visibility.Collapsed;
        radiusRingBox.Visibility = selectedRadius
            ? Visibility.Visible
            : Visibility.Collapsed;
        radiusMetersLabel.Visibility = radius
            ? Visibility.Visible
            : Visibility.Collapsed;
        radiusMetersBox.Visibility = radius
            ? Visibility.Visible
            : Visibility.Collapsed;
        radiusRingBox.IsEnabled = selectedRadius;
        addRadiusButton.IsEnabled = selectedRadius;
        addRadiusButton.Visibility = selectedRadius
            ? Visibility.Visible
            : Visibility.Collapsed;
        removeRadiusButton.IsEnabled = selectedRadius;
        removeRadiusButton.Visibility = selectedRadius
            ? Visibility.Visible
            : Visibility.Collapsed;
        deleteAnnotationButton.IsEnabled =
            annotationBox.SelectedItem is AnnotationChoice;
        UpdateScaleText();
    }

    private static void SetToolButtonState(Button button, bool selected)
    {
        button.Background = selected ? StudioTheme.AccentBrush : StudioTheme.PanelAltBrush;
        button.BorderBrush = selected ? StudioTheme.AccentBrush : StudioTheme.BorderBrush;
    }

    private async Task SaveSelectedMapAsync()
    {
        if (editingPane is null)
            return;
        try
        {
            saveEditButton.IsEnabled = false;
            cancelEditButton.IsEnabled = false;
            statusText.Text = "Газрын зургийн өндөр чанартай preview үүсгэж байна...";
            await editingPane.WebView.CoreWebView2.ExecuteScriptAsync(
                "window.erks.finishDrawing(); window.erks.selectAnnotation('', '', false);");
            MapState mapState = await GetMapStateAsync(editingPane)
                ?? throw new InvalidOperationException("Газрын зургийн хамрах хүрээг уншиж чадсангүй.");
            if (!mapState.Ready)
                throw new InvalidOperationException("Газрын зураг бүрэн ачаалагдаагүй байна.");

            ApplyMapState(editingPane.Viewport, mapState);
            int resolutionOffset = SelectedResolutionOffset();
            editingPane.Viewport.DetailZoom = Math.Min(
                MaxZoomForProvider(editingPane.Viewport.ProviderId),
                editingPane.Viewport.Zoom + resolutionOffset);
            editingPane.Viewport.Normalize(editingPane.Kind);
            await CaptureSnapshotAsync(editingPane, mapState);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            editingPane.Viewport.UpdatedAtUtc = now;
            workingCopy.UpdatedAtUtc = now;
            HasSavedChanges = true;
            SiteContextSaved?.Invoke(Result);
            statusText.Text =
                $"{editingPane.Title} хадгалагдлаа · хамрах хүрээ Z{editingPane.Viewport.Zoom:0.#} · " +
                $"нарийвчлал Z{editingPane.Viewport.DetailZoom:0.#} · " +
                $"{editingPane.Viewport.SnapshotPixelWidth}×{editingPane.Viewport.SnapshotPixelHeight} px.";
            await EndEditAsync();
        }
        catch (Exception exception)
        {
            statusText.Text = $"Газрын зураг хадгалагдсангүй: {exception.Message}";
        }
        finally
        {
            saveEditButton.IsEnabled = true;
            cancelEditButton.IsEnabled = true;
        }
    }

    private async Task CancelEditAsync()
    {
        if (editingPane is null || editBaseline is null)
            return;
        try
        {
            if (!editingPane.Viewport.ProviderId.Equals(
                    editBaseline.ProviderId,
                    StringComparison.OrdinalIgnoreCase))
            {
                string provider = JsonSerializer.Serialize(editBaseline.ProviderId);
                await editingPane.WebView.CoreWebView2.ExecuteScriptAsync(
                    $"window.erks.setProvider({provider});");
                await WaitForReadyAsync(editingPane);
            }
            string state = JsonSerializer.Serialize(MapState.FromViewport(editBaseline), JsonOptions);
            await editingPane.WebView.CoreWebView2.ExecuteScriptAsync($"window.erks.setState({state});");
            CopyViewport(editBaseline, editingPane.Viewport);
            RefreshAnnotationControls(MapState.FromViewport(editBaseline));
            statusText.Text = "Засварыг болилоо.";
        }
        finally
        {
            await EndEditAsync();
        }
    }

    private async Task EndEditAsync()
    {
        if (editingPane is not null)
        {
            await SetMapEditingAsync(editingPane, false);
            editingPane.LockState.Text = "Түгжээтэй";
            editingPane.LockState.Foreground = StudioTheme.MutedTextBrush;
        }
        editingPane = null;
        editBaseline = null;
        foreach (MapPane pane in AllPanes())
            pane.Shield.Visibility = Visibility.Visible;
        editingTools.Visibility = Visibility.Collapsed;
        annotationTools.Visibility = Visibility.Collapsed;
        editButton.Visibility = Visibility.Visible;
        doneButton.IsEnabled = true;
    }

    private async Task CaptureSnapshotAsync(MapPane pane, MapState selectedState)
    {
        string directory = Path.Combine(projectFolder, "assets", "site-context");
        Directory.CreateDirectory(directory);
        string fileName = pane.Kind.Equals(
            ProjectMapViewportKinds.LocationScheme,
            StringComparison.OrdinalIgnoreCase)
            ? "location-scheme.png"
            : "surroundings-overview.png";
        string targetPath = Path.Combine(directory, fileName);
        string tempPath = targetPath + ".tmp";
        CoreWebView2 core = pane.WebView.CoreWebView2;
        double detailDelta = Math.Clamp(
            pane.Viewport.DetailZoom - selectedState.Zoom,
            0,
            MaximumRasterDetailOffset);

        try
        {
            try
            {
                (int captureCssWidth, int captureCssHeight, double deviceScaleFactor) =
                    CalculateCaptureMetrics(detailDelta);
                string metrics = JsonSerializer.Serialize(new
                {
                    width = captureCssWidth,
                    height = captureCssHeight,
                    deviceScaleFactor,
                    mobile = false,
                });
                await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", metrics);
                string bounds = selectedState.Bounds is null
                    ? "null"
                    : JsonSerializer.Serialize(selectedState.Bounds, JsonOptions);
                await AwaitBrowserPromiseAsync(
                    core,
                    $"window.erks.prepareCapture({bounds})");
                string response = await core.CallDevToolsProtocolMethodAsync(
                    "Page.captureScreenshot",
                    "{\"format\":\"png\",\"fromSurface\":true,\"captureBeyondViewport\":false}");
                using JsonDocument document = JsonDocument.Parse(response);
                string data = document.RootElement.GetProperty("data").GetString()
                    ?? throw new InvalidDataException("Map screenshot returned no image data.");
                await File.WriteAllBytesAsync(tempPath, Convert.FromBase64String(data));
            }
            catch when (detailDelta < 0.01d)
            {
                await using FileStream stream = new(tempPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await core.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
            }
            finally
            {
                try
                {
                    await core.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}");
                    await core.ExecuteScriptAsync("window.erks.resize();");
                    string selectedMapState = JsonSerializer.Serialize(selectedState, JsonOptions);
                    await core.ExecuteScriptAsync($"window.erks.setState({selectedMapState});");
                }
                catch
                {
                }
            }

            File.Move(tempPath, targetPath, overwrite: true);
            (int width, int height) = ReadPngDimensions(targetPath);
            pane.Viewport.SnapshotRelativePath = Path.GetRelativePath(projectFolder, targetPath)
                .Replace(Path.DirectorySeparatorChar, '/');
            pane.Viewport.SnapshotSha256 = ComputeSha256(targetPath);
            pane.Viewport.SnapshotPixelWidth = width;
            pane.Viewport.SnapshotPixelHeight = height;
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch
            {
            }
        }
    }

    internal static (int CssWidth, int CssHeight, double DeviceScaleFactor)
        CalculateCaptureMetrics(double detailDelta)
    {
        double detailScale = Math.Pow(
            2,
            Math.Clamp(detailDelta, 0, MaximumRasterDetailOffset));

        // Render at the final pixel size so "high quality" requests finer source
        // tiles instead of enlarging a lower-resolution browser screenshot.
        return (
            Math.Max(1, (int)Math.Round(CaptureBasePixelWidth * detailScale)),
            Math.Max(1, (int)Math.Round(CaptureBasePixelHeight * detailScale)),
            CaptureDeviceScale);
    }

    private static async Task AwaitBrowserPromiseAsync(CoreWebView2 core, string expression)
    {
        string arguments = JsonSerializer.Serialize(new
        {
            expression,
            awaitPromise = true,
            returnByValue = true,
        });
        string response = await core.CallDevToolsProtocolMethodAsync("Runtime.evaluate", arguments);
        using JsonDocument document = JsonDocument.Parse(response);
        JsonElement root = document.RootElement;
        if (!root.TryGetProperty("exceptionDetails", out JsonElement exceptionDetails))
            return;

        string message = exceptionDetails.TryGetProperty("exception", out JsonElement exception) &&
                         exception.TryGetProperty("description", out JsonElement description)
            ? description.GetString() ?? "Map capture preparation failed."
            : exceptionDetails.TryGetProperty("text", out JsonElement text)
                ? text.GetString() ?? "Map capture preparation failed."
                : "Map capture preparation failed.";
        throw new InvalidOperationException(message);
    }

    private static (int Width, int Height) ReadPngDimensions(string path)
    {
        using FileStream stream = File.OpenRead(path);
        BitmapDecoder decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        BitmapFrame frame = decoder.Frames[0];
        return (frame.PixelWidth, frame.PixelHeight);
    }

    private static string ComputeSha256(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static async Task SetMapEditingAsync(MapPane pane, bool editing) =>
        await pane.WebView.CoreWebView2.ExecuteScriptAsync(
            $"window.erks.setEditing({editing.ToString().ToLowerInvariant()});");

    private static async Task<MapState?> GetMapStateAsync(MapPane pane)
    {
        string json = await pane.WebView.CoreWebView2.ExecuteScriptAsync("window.erks.getState();");
        return string.IsNullOrWhiteSpace(json) || json == "null"
            ? null
            : JsonSerializer.Deserialize<MapState>(json, JsonOptions);
    }

    private void BindProvider(string providerId)
    {
        bindingProvider = true;
        providerBox.SelectedItem = providers.FirstOrDefault(choice =>
            choice.Id.Equals(providerId, StringComparison.OrdinalIgnoreCase)) ?? providers[0];
        bindingProvider = false;
    }

    private void BindResolution(ProjectMapViewport viewport)
    {
        int offset = (int)Math.Clamp(
            Math.Round(viewport.DetailZoom - viewport.Zoom),
            0,
            MaximumRasterDetailOffset);
        BindResolution(viewport.Zoom, viewport.ProviderId, offset);
    }

    private void BindResolution(double coverageZoom, string providerId, int preferredOffset)
    {
        int maximumOffset = (int)Math.Clamp(
            Math.Floor(MaxZoomForProvider(providerId) - coverageZoom),
            0,
            MaximumRasterDetailOffset);
        int selectedOffset = Math.Clamp(preferredOffset, 0, maximumOffset);
        resolutionBox.ItemsSource = Enumerable.Range(0, maximumOffset + 1)
            .Select(offset => new MapResolutionChoice(
                offset,
                offset switch
                {
                    0 => $"Z{coverageZoom:0.#} · Стандарт",
                    _ => $"Z{coverageZoom + 1:0.#} · Өндөр чанар · 3000 px",
                }))
            .ToList();
        resolutionBox.SelectedItem = resolutionBox.Items
            .OfType<MapResolutionChoice>()
            .First(choice => choice.ZoomOffset == selectedOffset);
    }

    private int SelectedResolutionOffset() =>
        (resolutionBox.SelectedItem as MapResolutionChoice)?.ZoomOffset ?? 0;

    private static double MaxZoomForProvider(string providerId) =>
        ProjectMapProviderIds.Normalize(providerId) switch
        {
            ProjectMapProviderIds.OpenTopoMap => 17,
            ProjectMapProviderIds.OpenStreetMap => 19,
            _ => 22,
        };

    private static void ApplyMapState(ProjectMapViewport viewport, MapState state)
    {
        viewport.ProviderId = state.ProviderId;
        viewport.CenterLatitude = state.CenterLatitude;
        viewport.CenterLongitude = state.CenterLongitude;
        viewport.Zoom = state.Zoom;
        viewport.Bearing = state.Bearing;
        viewport.Attribution = state.Attribution;
        viewport.Landmarks = state.Landmarks.Select(item => item.Clone()).ToList();
        viewport.DistanceMeasures = state.DistanceMeasures.Select(item => item.Clone()).ToList();
        viewport.RadiusMeasures = state.RadiusMeasures.Select(item => item.Clone()).ToList();
        viewport.Normalize(viewport.Kind);
    }

    private static void CopyViewport(ProjectMapViewport source, ProjectMapViewport target)
    {
        target.Kind = source.Kind;
        target.ProviderId = source.ProviderId;
        target.CenterLatitude = source.CenterLatitude;
        target.CenterLongitude = source.CenterLongitude;
        target.Zoom = source.Zoom;
        target.DetailZoom = source.DetailZoom;
        target.Bearing = source.Bearing;
        target.Landmarks = source.Landmarks.Select(item => item.Clone()).ToList();
        target.DistanceMeasures = source.DistanceMeasures.Select(item => item.Clone()).ToList();
        target.RadiusMeasures = source.RadiusMeasures.Select(item => item.Clone()).ToList();
        target.SnapshotRelativePath = source.SnapshotRelativePath;
        target.SnapshotSha256 = source.SnapshotSha256;
        target.SnapshotPixelWidth = source.SnapshotPixelWidth;
        target.SnapshotPixelHeight = source.SnapshotPixelHeight;
        target.Attribution = source.Attribution;
        target.UpdatedAtUtc = source.UpdatedAtUtc;
    }

    private IEnumerable<MapPane> AllPanes()
    {
        yield return locationPane;
        yield return overviewPane;
    }

    private sealed record MapProviderChoice(
        string Id,
        string DisplayName,
        bool IsAvailable,
        string UnavailableReason)
    {
        public override string ToString() => IsAvailable ? DisplayName : $"{DisplayName} · эрх шаардлагатай";
    }

    private sealed record MapResolutionChoice(int ZoomOffset, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record AnnotationChoice(string Kind, string Id, string DisplayName)
    {
        public override string ToString() => DisplayName;
    }

    private sealed record RadiusChoice(int Index, double RadiusMeters, string Color)
    {
        public override string ToString() => RadiusMeters >= 1000d
            ? $"{Index + 1}. {RadiusMeters / 1000d:0.##} км"
            : $"{Index + 1}. {RadiusMeters:0.##} м";
    }

    private sealed record MapColorChoice(string Hex, string Name, Color Color)
    {
        public static IReadOnlyList<MapColorChoice> Defaults { get; } =
        [
            new("#e5484d", "Улаан", Color.FromRgb(229, 72, 77)),
            new("#1668dc", "Цэнхэр", Color.FromRgb(22, 104, 220)),
            new("#087f8c", "Ногоовтор", Color.FromRgb(8, 127, 140)),
            new("#f0a51a", "Шар", Color.FromRgb(240, 165, 26)),
            new("#7b61ff", "Нил ягаан", Color.FromRgb(123, 97, 255)),
            new("#15181d", "Хар", Color.FromRgb(21, 24, 29)),
        ];
    }

    private sealed class AnnotationToolOptions
    {
        public string Name { get; init; } = "";
        public string Color { get; init; } = "#e5484d";
        public double Size { get; init; } = 1d;
        public bool IsProjectSite { get; init; }
        public double RadiusMeters { get; init; } = 500d;
        public double StrokeWidth { get; init; } = 1d;
    }

    private sealed class MapPane(
        string kind,
        string title,
        ProjectMapViewport viewport,
        Border container,
        DockPanel header,
        TextBlock headerTitle,
        WebView2 webView,
        Border shield,
        TextBlock lockState)
    {
        public string Kind { get; } = kind;
        public string Title { get; } = title;
        public ProjectMapViewport Viewport { get; } = viewport;
        public Border Container { get; } = container;
        public DockPanel Header { get; } = header;
        public TextBlock HeaderTitle { get; } = headerTitle;
        public WebView2 WebView { get; } = webView;
        public Border Shield { get; } = shield;
        public TextBlock LockState { get; } = lockState;
        public bool IsInitialized { get; set; }
    }

    private sealed class MapInitialization
    {
        public required MapState Viewport { get; init; }
        public required ProjectSiteBoundary Boundary { get; init; }
        public required ProjectSitePlanFeatures PlanFeatures { get; init; }
        public bool Editing { get; init; }
        public string GoogleApiKey { get; init; } = "";
        public string AzureMapsKey { get; init; } = "";
    }

    private sealed class MapState
    {
        public string ProviderId { get; set; } = ProjectMapProviderIds.OpenStreetMap;
        public double CenterLatitude { get; set; }
        public double CenterLongitude { get; set; }
        public double Zoom { get; set; }
        public double Bearing { get; set; }
        public string Attribution { get; set; } = "";
        public MapBounds? Bounds { get; set; }
        public bool Ready { get; set; }
        public string SelectedAnnotationKind { get; set; } = "";
        public string SelectedAnnotationId { get; set; } = "";
        public int SelectedDistanceVertexIndex { get; set; } = -1;
        public int SelectedRadiusIndex { get; set; }
        public string AnnotationStatusMessage { get; set; } = "";
        public string AnnotationToolName { get; set; } = "";
        public List<ProjectMapLandmark> Landmarks { get; set; } = [];
        public List<ProjectMapDistanceMeasure> DistanceMeasures { get; set; } = [];
        public List<ProjectMapRadiusMeasure> RadiusMeasures { get; set; } = [];

        public static MapState FromViewport(ProjectMapViewport viewport) => new()
        {
            ProviderId = viewport.ProviderId,
            CenterLatitude = viewport.CenterLatitude,
            CenterLongitude = viewport.CenterLongitude,
            Zoom = viewport.Zoom,
            Bearing = viewport.Bearing,
            Attribution = viewport.Attribution,
            Landmarks = viewport.Landmarks.Select(item => item.Clone()).ToList(),
            DistanceMeasures = viewport.DistanceMeasures.Select(item => item.Clone()).ToList(),
            RadiusMeasures = viewport.RadiusMeasures.Select(item => item.Clone()).ToList(),
        };
    }

    private sealed class MapBounds
    {
        public double West { get; set; }
        public double South { get; set; }
        public double East { get; set; }
        public double North { get; set; }
    }
}
