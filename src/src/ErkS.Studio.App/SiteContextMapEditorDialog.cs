using System.IO;
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
    private const int CaptureCssWidth = 1000;
    private const int CaptureCssHeight = 1206;
    private const double CaptureDeviceScale = 1.5d;

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
    private readonly StackPanel editingTools = new() { Orientation = Orientation.Horizontal };
    private readonly ComboBox providerBox = new() { Width = 210, Margin = new Thickness(8, 0, 8, 0) };
    private readonly ComboBox resolutionBox = new() { Width = 160, Margin = new Thickness(8, 0, 8, 0) };
    private readonly Button editButton = StudioWidgets.CreateIconTextButton("icon-project.svg", "Засварлах");
    private readonly Button saveEditButton = StudioWidgets.CreateIconTextButton("icon-project.svg", "Хадгалах");
    private readonly Button cancelEditButton = StudioWidgets.CreateButton("Болих");
    private readonly Button doneButton = StudioWidgets.CreateButton("Дуусгах");
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
    private bool mapsInitialized;

    public SiteContextMapEditorDialog(
        string projectFolder,
        string projectId,
        ProjectSiteContextMap source)
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

        Title = "Байршлын схем / Орчны тойм";
        Width = Math.Min(1260, SystemParameters.WorkArea.Width * 0.94);
        Height = Math.Min(820, SystemParameters.WorkArea.Height * 0.92);
        MinWidth = 940;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        StudioTheme.Apply(this);

        locationPane = CreateMapPane(
            ProjectMapViewportKinds.LocationScheme,
            "БАЙРШЛЫН СХЕМ",
            workingCopy.LocationScheme);
        overviewPane = CreateMapPane(
            ProjectMapViewportKinds.SurroundingsOverview,
            "ОРЧНЫ ТОЙМ",
            workingCopy.SurroundingsOverview);

        Content = BuildContent();
        SelectPane(locationPane);
        Loaded += async (_, _) => await InitializeMapsAsync();
    }

    public bool HasSavedChanges { get; private set; }

    public ProjectSiteContextMap Result => workingCopy.CreateProjectSnapshot(projectId);

    public event Action<ProjectSiteContextMap>? SiteContextSaved;

    private UIElement BuildContent()
    {
        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new TextBlock
        {
            Text = "Байршлын схем / Орчны тойм",
            FontSize = StudioTheme.TitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 14),
        };
        root.Children.Add(heading);

        var commandBar = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 12),
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
        Grid.SetRow(commandBar, 1);
        root.Children.Add(commandBar);

        mapsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mapsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        mapsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        mapsGrid.Children.Add(locationPane.Container);
        Grid.SetColumn(overviewPane.Container, 2);
        mapsGrid.Children.Add(overviewPane.Container);
        Grid.SetRow(mapsGrid, 2);
        root.Children.Add(mapsGrid);

        var footer = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        doneButton.Click += (_, _) =>
        {
            if (editingPane is not null)
                return;
            DialogResult = HasSavedChanges;
            Close();
        };
        DockPanel.SetDock(doneButton, Dock.Right);
        footer.Children.Add(doneButton);
        footer.Children.Add(statusText);
        Grid.SetRow(footer, 3);
        root.Children.Add(footer);
        return root;
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
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Child = content,
        };
        var pane = new MapPane(kind, title, viewport, container, webView, shield, lockState);
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
            item.Container.BorderBrush = selected ? StudioTheme.AccentBrush : StudioTheme.BorderBrush;
            item.Container.BorderThickness = new Thickness(selected ? 2 : 1);
        }
        editButton.IsEnabled = mapsInitialized && pane.IsInitialized;
        statusText.Text = pane.IsInitialized ? "" : "Газрын зураг ачаалж байна...";
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
        doneButton.IsEnabled = false;
        editingPane.LockState.Text = "Засварлаж байна";
        editingPane.LockState.Foreground = StudioTheme.AccentSoftBrush;
        editingPane.Shield.Visibility = Visibility.Collapsed;
        foreach (MapPane pane in AllPanes().Where(pane => !ReferenceEquals(pane, editingPane)))
            pane.Shield.Visibility = Visibility.Visible;
        await SetMapEditingAsync(editingPane, true);
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

    private async Task SaveSelectedMapAsync()
    {
        if (editingPane is null)
            return;
        try
        {
            saveEditButton.IsEnabled = false;
            cancelEditButton.IsEnabled = false;
            statusText.Text = "Газрын зургийн өндөр чанартай preview үүсгэж байна...";
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
                $"нарийвчлал Z{editingPane.Viewport.DetailZoom:0.#}.";
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

        try
        {
            try
            {
                double detailDelta = Math.Clamp(
                    pane.Viewport.DetailZoom - selectedState.Zoom,
                    0,
                    2);
                double detailScale = Math.Pow(2, detailDelta);
                string metrics = JsonSerializer.Serialize(new
                {
                    width = (int)Math.Round(CaptureCssWidth * detailScale),
                    height = (int)Math.Round(CaptureCssHeight * detailScale),
                    deviceScaleFactor = CaptureDeviceScale,
                    mobile = false,
                });
                await core.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", metrics);
                await core.ExecuteScriptAsync("window.erks.resize();");
                if (selectedState.Bounds is not null)
                {
                    string bounds = JsonSerializer.Serialize(selectedState.Bounds, JsonOptions);
                    await core.ExecuteScriptAsync($"window.erks.fitBounds({bounds});");
                }
                else
                {
                    string selectedMapState = JsonSerializer.Serialize(selectedState, JsonOptions);
                    await core.ExecuteScriptAsync($"window.erks.setState({selectedMapState});");
                }
                await Task.Delay(1200);
                string response = await core.CallDevToolsProtocolMethodAsync(
                    "Page.captureScreenshot",
                    "{\"format\":\"png\",\"fromSurface\":true,\"captureBeyondViewport\":false}");
                using JsonDocument document = JsonDocument.Parse(response);
                string data = document.RootElement.GetProperty("data").GetString()
                    ?? throw new InvalidDataException("Map screenshot returned no image data.");
                await File.WriteAllBytesAsync(tempPath, Convert.FromBase64String(data));
            }
            catch
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
            2);
        BindResolution(viewport.Zoom, viewport.ProviderId, offset);
    }

    private void BindResolution(double coverageZoom, string providerId, int preferredOffset)
    {
        int maximumOffset = (int)Math.Clamp(
            Math.Floor(MaxZoomForProvider(providerId) - coverageZoom),
            0,
            2);
        int selectedOffset = Math.Clamp(preferredOffset, 0, maximumOffset);
        resolutionBox.ItemsSource = Enumerable.Range(0, maximumOffset + 1)
            .Select(offset => new MapResolutionChoice(
                offset,
                offset switch
                {
                    0 => $"Z{coverageZoom:0.#} · Стандарт",
                    1 => $"Z{coverageZoom + 1:0.#} · 2× нарийвчлал",
                    _ => $"Z{coverageZoom + 2:0.#} · 4× нарийвчлал",
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

    private sealed class MapPane(
        string kind,
        string title,
        ProjectMapViewport viewport,
        Border container,
        WebView2 webView,
        Border shield,
        TextBlock lockState)
    {
        public string Kind { get; } = kind;
        public string Title { get; } = title;
        public ProjectMapViewport Viewport { get; } = viewport;
        public Border Container { get; } = container;
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

        public static MapState FromViewport(ProjectMapViewport viewport) => new()
        {
            ProviderId = viewport.ProviderId,
            CenterLatitude = viewport.CenterLatitude,
            CenterLongitude = viewport.CenterLongitude,
            Zoom = viewport.Zoom,
            Bearing = viewport.Bearing,
            Attribution = viewport.Attribution,
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
