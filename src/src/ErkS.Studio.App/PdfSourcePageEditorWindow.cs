using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Non-destructive PDF source-page editor. Crop and whiteout geometry is stored
/// against the album page; the original PDF remains untouched and vector.
/// </summary>
internal sealed class PdfSourcePageEditorWindow : Window
{
    private const double LogicalPreviewWidth = 1000;
    private readonly PdfPageImageCache imageCache;
    private readonly SheetRecord sheet;
    private readonly SourcePageCropDefinition working;
    private readonly PageFormatDefinition studioFormat;
    private readonly Grid previewSurface = new();
    private readonly Image pageImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas overlay = new() { Background = Brushes.Transparent };
    private readonly Grid previewHost = new();
    private readonly Border sourcePreviewBorder = new();
    private readonly Border studioPreviewBorder = new();
    private readonly Canvas studioSurface = new() { Background = Brushes.Transparent };
    private readonly TextBox leftBox = new();
    private readonly TextBox topBox = new();
    private readonly TextBox rightBox = new();
    private readonly TextBox bottomBox = new();
    private readonly TextBox offsetXBox = new();
    private readonly TextBox offsetYBox = new();
    private readonly TextBox rotationBox = new();
    private readonly TextBox titleBlockScaleBox = new();
    private readonly CheckBox inheritTitleBlockScaleCheck = new()
    {
        Content = "Эх PDF-ийн масштабыг дагах",
        Foreground = StudioTheme.TextBrush,
        Margin = new Thickness(0, 0, 0, 5),
    };
    private readonly Button sourceViewButton = StudioWidgets.CreateButton("Эх PDF / crop");
    private readonly Button studioViewButton =
        StudioWidgets.CreatePrimaryButton("Studio хуудсан дээр байрлуулах");
    private readonly TextBlock statusText = StudioWidgets.CreateHint("");
    private readonly List<SourcePagePointDefinition> polygonPoints = [];
    private EditorTool activeTool;
    private EditorView activeView = EditorView.Source;
    private Point? dragStart;
    private Point? dragCurrent;
    private Point? studioDragStart;
    private Guid? selectedMaskId;
    private double sourceWidthMm;
    private double sourceHeightMm;
    private double studioPixelsPerMillimeter;
    private double studioDragStartOffsetXmm;
    private double studioDragStartOffsetYmm;
    private Rect studioDrawingArea;
    private Rect studioPlacementArea;
    private BitmapSource? previewBitmap;
    private bool imageReady;

    public SourcePageCropDefinition Result => working.DeepClone();

    /// <summary>
    /// The title-block scale is metadata only. It never resizes the source PDF.
    /// </summary>
    public string? ScaleTextOverride => inheritTitleBlockScaleCheck.IsChecked == true
        ? null
        : DrawingScaleText.Normalize(titleBlockScaleBox.Text);

    public PdfSourcePageEditorWindow(
        PdfPageImageCache imageCache,
        SheetRecord sheet,
        SourcePageCropDefinition? current,
        PageFormatDefinition studioFormat,
        string? scaleTextOverride)
    {
        this.imageCache = imageCache;
        this.sheet = sheet;
        this.studioFormat = studioFormat;
        working = current?.DeepClone() ?? new SourcePageCropDefinition();
        working.ScalePercent = 100;
        inheritTitleBlockScaleCheck.IsChecked = scaleTextOverride is null;
        titleBlockScaleBox.Text = DrawingScaleText.Normalize(
            scaleTextOverride ?? sheet.Entry.ScaleText);
        sourceWidthMm = sheet.Entry.WidthMm > 0 ? sheet.Entry.WidthMm : 420;
        sourceHeightMm = sheet.Entry.HeightMm > 0 ? sheet.Entry.HeightMm : 297;

        Title = "PDF эх хуудасны хэсэг засах";
        Width = 1420;
        Height = 900;
        MinWidth = 980;
        MinHeight = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        StudioTheme.Apply(this);

        Content = BuildContent();
        Loaded += async (_, _) => await LoadPreviewAsync();
        PreviewKeyDown += HandlePreviewKeyDown;
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        root.Children.Add(BuildActions());
        root.Children.Add(BuildToolbar());

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(286) });

        previewSurface.Children.Add(pageImage);
        previewSurface.Children.Add(overlay);
        previewSurface.Width = LogicalPreviewWidth;
        previewSurface.Height = 707;
        overlay.Width = previewSurface.Width;
        overlay.Height = previewSurface.Height;
        overlay.MouseLeftButtonDown += HandleCanvasMouseDown;
        overlay.MouseMove += HandleCanvasMouseMove;
        overlay.MouseLeftButtonUp += HandleCanvasMouseUp;
        overlay.MouseRightButtonDown += (_, eventArgs) =>
        {
            if (activeTool == EditorTool.PolygonMask)
            {
                CompletePolygon();
                eventArgs.Handled = true;
            }
        };

        var sourceViewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            Child = previewSurface,
        };
        sourcePreviewBorder.Background = new SolidColorBrush(Color.FromRgb(53, 57, 63));
        sourcePreviewBorder.BorderBrush = StudioTheme.BorderBrush;
        sourcePreviewBorder.BorderThickness = new Thickness(1);
        sourcePreviewBorder.Padding = new Thickness(12);
        sourcePreviewBorder.Child = sourceViewbox;

        double studioWidthMm = Math.Max(1, studioFormat.WidthMm);
        double studioHeightMm = Math.Max(1, studioFormat.HeightMm);
        studioSurface.Width = LogicalPreviewWidth;
        studioSurface.Height = LogicalPreviewWidth * studioHeightMm / studioWidthMm;
        studioSurface.ClipToBounds = true;
        studioSurface.Cursor = Cursors.Arrow;
        studioSurface.MouseLeftButtonDown += HandleStudioMouseDown;
        studioSurface.MouseMove += HandleStudioMouseMove;
        studioSurface.MouseLeftButtonUp += HandleStudioMouseUp;
        var studioViewbox = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.Both,
            Child = studioSurface,
        };
        studioPreviewBorder.Background = new SolidColorBrush(Color.FromRgb(53, 57, 63));
        studioPreviewBorder.BorderBrush = StudioTheme.BorderBrush;
        studioPreviewBorder.BorderThickness = new Thickness(1);
        studioPreviewBorder.Padding = new Thickness(12);
        studioPreviewBorder.Child = studioViewbox;
        studioPreviewBorder.Visibility = Visibility.Collapsed;

        previewHost.Children.Add(sourcePreviewBorder);
        previewHost.Children.Add(studioPreviewBorder);
        var previewFrame = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(53, 57, 63)),
            Child = previewHost,
        };
        Grid.SetColumn(previewFrame, 0);
        workspace.Children.Add(previewFrame);

        var properties = BuildProperties();
        Grid.SetColumn(properties, 1);
        workspace.Children.Add(properties);
        root.Children.Add(workspace);
        return root;
    }

    private UIElement BuildActions()
    {
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.Click += (_, _) => DialogResult = false;
        Button save = StudioWidgets.CreatePrimaryButton("Хадгалах");
        save.IsDefault = true;
        save.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        DockPanel.SetDock(actions, Dock.Bottom);
        return actions;
    }

    private UIElement BuildToolbar()
    {
        var area = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        var titleRow = new DockPanel();
        titleRow.Children.Add(StudioWidgets.CreateTitle("PDF эх хуудасны хэсэг засах"));
        area.Children.Add(titleRow);
        area.Children.Add(StudioWidgets.CreateHint(
            "1. Эх PDF дээр хэрэгтэй хэсгээ crop хийнэ. 2. Studio хуудсан дээр " +
            "байрлуулах горимд орж тайрсан зургаа хараад чирж байрлуулна. Эх PDF өөрчлөгдөхгүй."));

        var views = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        sourceViewButton.Click += (_, _) => SetEditorView(EditorView.Source);
        studioViewButton.Click += (_, _) => SetEditorView(EditorView.Studio);
        views.Children.Add(sourceViewButton);
        views.Children.Add(studioViewButton);
        area.Children.Add(views);

        var tools = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        tools.Children.Add(CreateToolButton("Сонгох", EditorTool.Select));
        tools.Children.Add(CreateToolButton("Хэрэгтэй хүрээ", EditorTool.Crop));
        tools.Children.Add(CreateToolButton("Тэгш өнцөгт маск", EditorTool.RectangleMask));
        tools.Children.Add(CreateToolButton("Чөлөөт маск", EditorTool.PolygonMask));
        Button delete = StudioWidgets.CreateButton("Сонгосныг хасах");
        delete.Click += (_, _) => DeleteSelectedMask();
        tools.Children.Add(delete);
        Button reset = StudioWidgets.CreateButton("Бүгдийг цэвэрлэх");
        reset.Click += (_, _) => ResetEdits();
        tools.Children.Add(reset);
        area.Children.Add(tools);
        area.Children.Add(statusText);
        DockPanel.SetDock(area, Dock.Top);
        return area;
    }

    private Button CreateToolButton(string text, EditorTool tool)
    {
        Button button = StudioWidgets.CreateButton(text);
        button.Click += (_, _) =>
        {
            SetEditorView(EditorView.Source);
            ActivateTool(tool);
        };
        return button;
    }

    private UIElement BuildProperties()
    {
        BindPropertyValues();
        var panel = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        panel.Children.Add(StudioWidgets.CreateSectionHeader("Crop хүрээ"));
        panel.Children.Add(StudioWidgets.CreateFormRow("Зүүн (мм)", leftBox, 92));
        panel.Children.Add(StudioWidgets.CreateFormRow("Дээд (мм)", topBox, 92));
        panel.Children.Add(StudioWidgets.CreateFormRow("Баруун (мм)", rightBox, 92));
        panel.Children.Add(StudioWidgets.CreateFormRow("Доод (мм)", bottomBox, 92));
        Button applyCrop = StudioWidgets.CreateButton("Тоон утга хэрэглэх");
        applyCrop.Click += (_, _) =>
        {
            ApplyPropertyValues();
            RedrawOverlay();
            SetEditorView(EditorView.Studio);
            ShowStatus("Crop баталгаажлаа. Studio хуудсан дээр тайрсан зургаа чирж байрлуулна уу.");
        };
        panel.Children.Add(applyCrop);

        panel.Children.Add(StudioWidgets.CreateSectionHeader("Байршуулалт"));
        panel.Children.Add(StudioWidgets.CreateFormRow("X шилжилт (мм)", offsetXBox, 92));
        panel.Children.Add(StudioWidgets.CreateFormRow("Y шилжилт (мм)", offsetYBox, 92));
        panel.Children.Add(StudioWidgets.CreateFormRow("Эргэлт °", rotationBox, 92));
        Button centerPlacement = StudioWidgets.CreateButton("Studio талбайн голд");
        centerPlacement.Click += (_, _) =>
        {
            working.OffsetXmm = 0;
            working.OffsetYmm = 0;
            BindPropertyValues();
            RedrawStudioPreview();
            ShowStatus("Тайрсан зураг Studio зургийн талбайн голд байрлалаа.");
        };
        panel.Children.Add(centerPlacement);
        panel.Children.Add(StudioWidgets.CreateHint(
            "Studio preview дээр тайрсан зургийг 1:1 бодит хэмжээгээр шууд чирнэ. " +
            "Crop хийхэд зургийн хэмжээ өөрчлөгдөхгүй; X/Y нь зөвхөн байрлалыг хадгална."));

        panel.Children.Add(StudioWidgets.CreateSectionHeader("Булангийн хүснэгт"));
        inheritTitleBlockScaleCheck.Checked += (_, _) =>
        {
            RefreshTitleBlockScaleControls();
            RedrawStudioPreview();
        };
        inheritTitleBlockScaleCheck.Unchecked += (_, _) =>
        {
            RefreshTitleBlockScaleControls();
            RedrawStudioPreview();
        };
        titleBlockScaleBox.TextChanged += (_, _) => RedrawStudioPreview();
        panel.Children.Add(inheritTitleBlockScaleCheck);
        panel.Children.Add(
            StudioWidgets.CreateFormRow("Зургийн масштаб", titleBlockScaleBox, 92));
        panel.Children.Add(StudioWidgets.CreateHint(
            "100 гэж оруулбал булангийн хүснэгтэд 1:100 гэж бичигдэнэ. " +
            "Энэ утга зургийн хэмжээг өөрчлөхгүй."));
        RefreshTitleBlockScaleControls();

        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private async Task LoadPreviewAsync()
    {
        try
        {
            BitmapSource? bitmap = await imageCache.GetPageAsync(
                sheet.PdfPath,
                Math.Max(1, sheet.Entry.PdfPageNumber),
                2200,
                CancellationToken.None);
            if (bitmap is null)
            {
                ShowStatus("PDF хуудсыг урьдчилж харах боломжгүй байна.", isError: true);
                return;
            }

            previewBitmap = bitmap;
            pageImage.Source = bitmap;
            double aspect = bitmap.PixelWidth > 0
                ? (double)bitmap.PixelHeight / bitmap.PixelWidth
                : sourceHeightMm / sourceWidthMm;
            previewSurface.Width = LogicalPreviewWidth;
            previewSurface.Height = Math.Clamp(LogicalPreviewWidth * aspect, 250, 1600);
            overlay.Width = previewSurface.Width;
            overlay.Height = previewSurface.Height;
            if (sheet.Entry.WidthMm <= 0 || sheet.Entry.HeightMm <= 0)
            {
                sourceWidthMm = 420;
                sourceHeightMm = sourceWidthMm * aspect;
            }
            imageReady = true;
            RedrawOverlay();
            ActivateTool(EditorTool.Select);
        }
        catch (Exception exception)
        {
            ShowStatus($"PDF preview алдаа: {exception.Message}", isError: true);
        }
    }

    private void SetEditorView(EditorView view)
    {
        if (view == EditorView.Studio && !imageReady)
        {
            ShowStatus("Studio preview нээгдэхийн тулд PDF preview ачаалагдахыг хүлээнэ үү.");
            return;
        }

        activeView = view;
        sourcePreviewBorder.Visibility =
            view == EditorView.Source ? Visibility.Visible : Visibility.Collapsed;
        studioPreviewBorder.Visibility =
            view == EditorView.Studio ? Visibility.Visible : Visibility.Collapsed;
        sourceViewButton.FontWeight =
            view == EditorView.Source ? FontWeights.SemiBold : FontWeights.Normal;
        studioViewButton.FontWeight =
            view == EditorView.Studio ? FontWeights.SemiBold : FontWeights.Normal;

        if (view == EditorView.Studio)
        {
            ApplyPropertyValues();
            ClampStudioOffsets();
            BindPropertyValues();
            RedrawStudioPreview();
            ShowStatus(
                "Studio хуудасны зургийн талбай дээр тайрсан зургаа чирж байрлуулна уу.");
        }
        else
        {
            ShowStatus("Эх PDF дээр хэрэгтэй хэсгээ хүрээлж crop хийнэ үү.");
        }
    }

    private void RefreshTitleBlockScaleControls()
    {
        bool inheritsSource = inheritTitleBlockScaleCheck.IsChecked == true;
        titleBlockScaleBox.IsEnabled = !inheritsSource;
        titleBlockScaleBox.Opacity = inheritsSource ? 0.6 : 1;
    }

    private void HandleStudioMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!imageReady || activeView != EditorView.Studio)
            return;

        Point point = eventArgs.GetPosition(studioSurface);
        Rect visiblePlacement = Rect.Intersect(studioPlacementArea, studioDrawingArea);
        if (visiblePlacement.IsEmpty || !visiblePlacement.Contains(point))
            return;

        studioDragStart = point;
        studioDragStartOffsetXmm = working.OffsetXmm;
        studioDragStartOffsetYmm = working.OffsetYmm;
        studioSurface.Cursor = Cursors.SizeAll;
        studioSurface.CaptureMouse();
        eventArgs.Handled = true;
    }

    private void HandleStudioMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (studioDragStart is not Point start ||
            !studioSurface.IsMouseCaptured ||
            studioPixelsPerMillimeter <= 0)
        {
            return;
        }

        Point current = eventArgs.GetPosition(studioSurface);
        working.OffsetXmm =
            studioDragStartOffsetXmm + (current.X - start.X) / studioPixelsPerMillimeter;
        working.OffsetYmm =
            studioDragStartOffsetYmm + (current.Y - start.Y) / studioPixelsPerMillimeter;
        ClampStudioOffsets();
        BindPropertyValues();
        RedrawStudioPreview();
    }

    private void HandleStudioMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!studioSurface.IsMouseCaptured)
            return;

        studioSurface.ReleaseMouseCapture();
        studioDragStart = null;
        studioSurface.Cursor = Cursors.Arrow;
        ShowStatus(
            $"Studio байрлал хадгалагдлаа: X {working.OffsetXmm:0.##} мм, " +
            $"Y {working.OffsetYmm:0.##} мм.");
        eventArgs.Handled = true;
    }

    private void ClampStudioOffsets()
    {
        (working.OffsetXmm, working.OffsetYmm) =
            PdfSourcePagePlacementGeometry.ClampOffsetsToTarget(
                sourceWidthMm,
                sourceHeightMm,
                ResolveStudioDrawingArea(),
                PagePlacementMode.PreservePhysicalSize,
                working,
                studioFormat.Id);
    }

    private void RedrawStudioPreview()
    {
        studioSurface.Children.Clear();
        if (!imageReady || previewBitmap is null)
            return;

        double pageWidthMm = Math.Max(1, studioFormat.WidthMm);
        double pageHeightMm = Math.Max(1, studioFormat.HeightMm);
        studioPixelsPerMillimeter = LogicalPreviewWidth / pageWidthMm;
        studioSurface.Width = LogicalPreviewWidth;
        studioSurface.Height = pageHeightMm * studioPixelsPerMillimeter;

        AddStudioRectangle(
            new Rect(0, 0, studioSurface.Width, studioSurface.Height),
            Brushes.White,
            Brushes.Black,
            1.5);

        PageRectMm drawingMm = ResolveStudioDrawingArea();
        Rect drawing = ToStudioRect(drawingMm);
        studioDrawingArea = drawing;
        var drawingContent = new Canvas
        {
            Width = drawing.Width,
            Height = drawing.Height,
            ClipToBounds = true,
            Background = Brushes.White,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(drawingContent, drawing.Left);
        Canvas.SetTop(drawingContent, drawing.Top);
        studioSurface.Children.Add(drawingContent);

        PdfSourcePagePlacementMm placement = ResolveStudioPlacement();
        PageRectMm sourceCrop = placement.SourceRectangle;
        PageRectMm destination = placement.DestinationRectangle;
        double placedWidth = destination.Width * studioPixelsPerMillimeter;
        double placedHeight = destination.Height * studioPixelsPerMillimeter;
        double placedX = destination.X * studioPixelsPerMillimeter;
        double placedY = destination.Y * studioPixelsPerMillimeter;
        studioPlacementArea = new Rect(placedX, placedY, placedWidth, placedHeight);

        var croppedSource = new Canvas
        {
            Width = placedWidth,
            Height = placedHeight,
            ClipToBounds = true,
            Background = Brushes.White,
        };
        double sourceScale =
            destination.Width / Math.Max(0.01, sourceCrop.Width) *
            studioPixelsPerMillimeter;
        var sourceImage = new Image
        {
            Source = previewBitmap,
            Width = sourceWidthMm * sourceScale,
            Height = sourceHeightMm * sourceScale,
            Stretch = Stretch.Fill,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(sourceImage, -sourceCrop.X * sourceScale);
        Canvas.SetTop(sourceImage, -sourceCrop.Y * sourceScale);
        croppedSource.Children.Add(sourceImage);
        DrawStudioMasks(croppedSource, sourceCrop.X, sourceCrop.Y, sourceScale);

        var croppedBorder = new Border
        {
            Width = placedWidth,
            Height = placedHeight,
            Child = croppedSource,
            BorderBrush = StudioTheme.AccentBrush,
            BorderThickness = new Thickness(2),
            IsHitTestVisible = false,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new RotateTransform(working.RotationDegrees),
        };
        Canvas.SetLeft(croppedBorder, placedX - drawing.Left);
        Canvas.SetTop(croppedBorder, placedY - drawing.Top);
        drawingContent.Children.Add(croppedBorder);

        DrawStudioChrome();
    }

    private void DrawStudioMasks(
        Canvas croppedSource,
        double cropLeftMm,
        double cropTopMm,
        double sourceScale)
    {
        foreach (SourcePageMaskDefinition mask in working.Masks ?? [])
        {
            IReadOnlyList<SourcePagePointDefinition> points =
                mask.Shape == SourcePageMaskShape.Rectangle
                    ? RectangleMaskPoints(mask.Points)
                    : mask.Points;
            if (points.Count < 3)
                continue;

            var polygon = new Polygon
            {
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(205, 210, 216)),
                StrokeThickness = 1,
                IsHitTestVisible = false,
            };
            foreach (SourcePagePointDefinition point in points)
            {
                polygon.Points.Add(new Point(
                    (Math.Clamp(point.X, 0, 1) * sourceWidthMm - cropLeftMm) *
                    sourceScale,
                    (Math.Clamp(point.Y, 0, 1) * sourceHeightMm - cropTopMm) *
                    sourceScale));
            }
            croppedSource.Children.Add(polygon);
        }
    }

    private void DrawStudioChrome()
    {
        BuildingArchitectureConceptPageRegions regions =
            BuildingArchitectureConceptPageLayout.ResolveRegions(
                studioFormat,
                includeInformationHeader: false);
        Rect frame = ToStudioRect(regions.Frame);
        Rect header = ToStudioRect(regions.SheetTitleArea);
        Rect titleBlock = ToStudioRect(regions.TitleBlockArea);

        AddStudioRectangle(header, Brushes.White, Brushes.Black, 1.2);
        AddStudioRectangle(titleBlock, Brushes.White, Brushes.Black, 1.2);
        AddStudioRectangle(frame, Brushes.Transparent, Brushes.Black, 1.6);

        var drawingOutline = AddStudioRectangle(
            studioDrawingArea,
            Brushes.Transparent,
            StudioTheme.AccentBrush,
            1.4);
        drawingOutline.StrokeDashArray = new DoubleCollection([7, 5]);

        if (titleBlock.Width > 0 && titleBlock.Height > 0)
        {
            BuildingArchitectureConceptCornerGrid grid =
                BuildingArchitectureConceptPageLayout.ResolveCornerGrid(
                    regions.TitleBlockArea);
            foreach (double xMm in new[] { grid.X1, grid.X2, grid.X3, grid.X4 })
            {
                AddStudioLine(
                    xMm * studioPixelsPerMillimeter,
                    titleBlock.Top,
                    xMm * studioPixelsPerMillimeter,
                    titleBlock.Bottom);
            }
            foreach (double yMm in new[] { grid.Y1, grid.Y2, grid.Y3 })
            {
                AddStudioLine(
                    titleBlock.Left,
                    yMm * studioPixelsPerMillimeter,
                    titleBlock.Right,
                    yMm * studioPixelsPerMillimeter);
            }

            string scale = inheritTitleBlockScaleCheck.IsChecked == true
                ? DrawingScaleText.Normalize(sheet.Entry.ScaleText)
                : DrawingScaleText.Normalize(titleBlockScaleBox.Text);
            var scaleText = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(scale) ? "Масштаб —" : $"Масштаб {scale}",
                Foreground = Brushes.Black,
                FontSize = Math.Clamp(titleBlock.Height * 0.23, 7, 14),
                TextAlignment = TextAlignment.Center,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(scaleText, grid.X4 * studioPixelsPerMillimeter);
            Canvas.SetTop(scaleText, grid.Y1 * studioPixelsPerMillimeter);
            scaleText.Width = Math.Max(
                0,
                (grid.X5 - grid.X4) * studioPixelsPerMillimeter);
            scaleText.Height = Math.Max(
                0,
                (grid.Y2 - grid.Y1) * studioPixelsPerMillimeter);
            studioSurface.Children.Add(scaleText);
        }

        var placementHint = new TextBlock
        {
            Text = "Тайрсан зургийг чирж байрлуулна",
            Foreground = StudioTheme.AccentBrush,
            Background = new SolidColorBrush(Color.FromArgb(205, 255, 255, 255)),
            Padding = new Thickness(5, 2, 5, 2),
            FontWeight = FontWeights.SemiBold,
            FontSize = 11,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(placementHint, studioDrawingArea.Left + 8);
        Canvas.SetTop(placementHint, studioDrawingArea.Top + 8);
        studioSurface.Children.Add(placementHint);
    }

    private PageRectMm ResolveStudioDrawingArea()
    {
        if (studioFormat.DrawingArea.Width > 0 && studioFormat.DrawingArea.Height > 0)
            return studioFormat.DrawingArea;

        return BuildingArchitectureConceptPageLayout.Calculate(
            Math.Max(1, studioFormat.WidthMm),
            Math.Max(1, studioFormat.HeightMm),
            studioFormat.BindEdge).DrawingArea;
    }

    private PdfSourcePagePlacementMm ResolveStudioPlacement() =>
        PdfSourcePagePlacementGeometry.Calculate(
            sourceWidthMm,
            sourceHeightMm,
            ResolveStudioDrawingArea(),
            PagePlacementMode.PreservePhysicalSize,
            working,
            studioFormat.Id);

    private Rect ToStudioRect(PageRectMm rectangle) => new(
        rectangle.X * studioPixelsPerMillimeter,
        rectangle.Y * studioPixelsPerMillimeter,
        Math.Max(0, rectangle.Width * studioPixelsPerMillimeter),
        Math.Max(0, rectangle.Height * studioPixelsPerMillimeter));

    private Rectangle AddStudioRectangle(
        Rect rect,
        Brush fill,
        Brush stroke,
        double strokeThickness)
    {
        var rectangle = new Rectangle
        {
            Width = Math.Max(0, rect.Width),
            Height = Math.Max(0, rect.Height),
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = strokeThickness,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(rectangle, rect.Left);
        Canvas.SetTop(rectangle, rect.Top);
        studioSurface.Children.Add(rectangle);
        return rectangle;
    }

    private void AddStudioLine(double x1, double y1, double x2, double y2)
    {
        studioSurface.Children.Add(new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = Brushes.Black,
            StrokeThickness = 0.8,
            IsHitTestVisible = false,
        });
    }

    private void HandleCanvasMouseDown(object sender, MouseButtonEventArgs eventArgs)
    {
        if (!imageReady)
            return;

        Point point = ClampPoint(eventArgs.GetPosition(overlay));
        if (activeTool is EditorTool.Crop or EditorTool.RectangleMask)
        {
            dragStart = point;
            dragCurrent = point;
            overlay.CaptureMouse();
            RedrawOverlay();
            eventArgs.Handled = true;
            return;
        }

        if (activeTool == EditorTool.PolygonMask)
        {
            polygonPoints.Add(ToNormalized(point));
            if (eventArgs.ClickCount > 1)
                CompletePolygon();
            else
                RedrawOverlay();
            eventArgs.Handled = true;
            return;
        }

        selectedMaskId = null;
        RedrawOverlay();
    }

    private void HandleCanvasMouseMove(object sender, MouseEventArgs eventArgs)
    {
        if (dragStart is null || !overlay.IsMouseCaptured)
            return;

        dragCurrent = ClampPoint(eventArgs.GetPosition(overlay));
        RedrawOverlay();
    }

    private void HandleCanvasMouseUp(object sender, MouseButtonEventArgs eventArgs)
    {
        if (dragStart is not Point start || activeTool is not (EditorTool.Crop or EditorTool.RectangleMask))
            return;

        Point end = ClampPoint(eventArgs.GetPosition(overlay));
        overlay.ReleaseMouseCapture();
        dragStart = null;
        dragCurrent = null;
        Rect rect = NormalizeRect(start, end);
        if (rect.Width < 6 || rect.Height < 6)
        {
            ShowStatus("Хүрээ хэт жижиг байна.", isError: true);
            RedrawOverlay();
            return;
        }

        bool completedCrop = activeTool == EditorTool.Crop;
        if (completedCrop)
        {
            working.Enabled = true;
            working.LeftMm = rect.Left / overlay.Width * sourceWidthMm;
            working.TopMm = rect.Top / overlay.Height * sourceHeightMm;
            working.RightMm = (overlay.Width - rect.Right) / overlay.Width * sourceWidthMm;
            working.BottomMm = (overlay.Height - rect.Bottom) / overlay.Height * sourceHeightMm;
            BindPropertyValues();
            ShowStatus("Хэрэгтэй хэсгийн хүрээ тохируулагдлаа.");
        }
        else
        {
            working.Masks ??= [];
            working.Masks.Add(new SourcePageMaskDefinition
            {
                Shape = SourcePageMaskShape.Rectangle,
                Points = [ToNormalized(rect.TopLeft), ToNormalized(rect.BottomRight)],
            });
            selectedMaskId = working.Masks[^1].Id;
            ShowStatus("Тэгш өнцөгт маск нэмэгдлээ.");
        }

        ActivateTool(EditorTool.Select);
        RedrawOverlay();
        if (completedCrop)
        {
            SetEditorView(EditorView.Studio);
            ShowStatus(
                "Crop баталгаажлаа. Studio хуудсан дээр тайрсан зургаа чирж байрлуулна уу.");
        }
    }

    private void HandlePreviewKeyDown(object sender, KeyEventArgs eventArgs)
    {
        if (eventArgs.Key == Key.Escape)
        {
            CancelCurrentDrawing();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Delete)
        {
            DeleteSelectedMask();
            eventArgs.Handled = true;
        }
        else if (eventArgs.Key == Key.Enter && activeTool == EditorTool.PolygonMask)
        {
            CompletePolygon();
            eventArgs.Handled = true;
        }
    }

    private void ActivateTool(EditorTool tool)
    {
        CancelCurrentDrawing(redraw: false);
        activeTool = tool;
        overlay.Cursor = tool == EditorTool.Select ? Cursors.Arrow : Cursors.Cross;
        string hint = tool switch
        {
            EditorTool.Crop => "Хадгалах хэрэгтэй хэсгийг хүрээлж чирнэ үү.",
            EditorTool.RectangleMask => "Арилгах хэсгийг хүрээлж чирнэ үү.",
            EditorTool.PolygonMask => "Олон цэг тавина. Enter, давхар товшилт эсвэл баруун товчоор дуусгана.",
            _ => "Маск сонгоод Delete дарж хасаж болно.",
        };
        ShowStatus(hint);
        RedrawOverlay();
    }

    private void CompletePolygon()
    {
        if (polygonPoints.Count >= 3)
        {
            working.Masks ??= [];
            working.Masks.Add(new SourcePageMaskDefinition
            {
                Shape = SourcePageMaskShape.Polygon,
                Points = polygonPoints
                    .Select(point => new SourcePagePointDefinition { X = point.X, Y = point.Y })
                    .ToList(),
            });
            selectedMaskId = working.Masks[^1].Id;
            ShowStatus("Чөлөөт маск нэмэгдлээ.");
        }
        else if (polygonPoints.Count > 0)
        {
            ShowStatus("Чөлөөт маск дор хаяж 3 цэгтэй байна.", isError: true);
        }

        polygonPoints.Clear();
        activeTool = EditorTool.Select;
        overlay.Cursor = Cursors.Arrow;
        RedrawOverlay();
    }

    private void CancelCurrentDrawing(bool redraw = true)
    {
        if (overlay.IsMouseCaptured)
            overlay.ReleaseMouseCapture();
        dragStart = null;
        dragCurrent = null;
        polygonPoints.Clear();
        activeTool = EditorTool.Select;
        overlay.Cursor = Cursors.Arrow;
        if (redraw)
        {
            ShowStatus("Үйлдэл цуцлагдлаа.");
            RedrawOverlay();
        }
    }

    private void DeleteSelectedMask()
    {
        if (selectedMaskId is not Guid id || working.Masks is null)
        {
            ShowStatus("Хасах маскаа эхлээд сонгоно уу.", isError: true);
            return;
        }

        working.Masks.RemoveAll(mask => mask.Id == id);
        selectedMaskId = null;
        ShowStatus("Сонгосон маск хасагдлаа.");
        RedrawOverlay();
    }

    private void ResetEdits()
    {
        working.Enabled = false;
        working.LeftMm = 0;
        working.TopMm = 0;
        working.RightMm = 0;
        working.BottomMm = 0;
        working.OffsetXmm = 0;
        working.OffsetYmm = 0;
        working.ScalePercent = 100;
        working.RotationDegrees = 0;
        working.Masks?.Clear();
        selectedMaskId = null;
        BindPropertyValues();
        ActivateTool(EditorTool.Select);
        ShowStatus("PDF хуудасны бүх таслалт, маск цэвэрлэгдлээ.");
    }

    private void RedrawOverlay()
    {
        overlay.Children.Clear();
        if (!imageReady)
            return;

        DrawCrop();
        foreach (SourcePageMaskDefinition mask in working.Masks ?? [])
            DrawMask(mask);
        DrawWorkingGeometry();
        if (activeView == EditorView.Studio)
            RedrawStudioPreview();
    }

    private void DrawCrop()
    {
        if (!working.Enabled)
            return;

        Rect crop = ResolveCropRect();
        Brush dim = new SolidColorBrush(Color.FromArgb(115, 18, 20, 24));
        AddFilledRectangle(new Rect(0, 0, overlay.Width, Math.Max(0, crop.Top)), dim);
        AddFilledRectangle(
            new Rect(0, crop.Bottom, overlay.Width, Math.Max(0, overlay.Height - crop.Bottom)),
            dim);
        AddFilledRectangle(new Rect(0, crop.Top, Math.Max(0, crop.Left), crop.Height), dim);
        AddFilledRectangle(
            new Rect(crop.Right, crop.Top, Math.Max(0, overlay.Width - crop.Right), crop.Height),
            dim);
        AddOutlineRectangle(crop, StudioTheme.AccentBrush, 3);
    }

    private void DrawMask(SourcePageMaskDefinition mask)
    {
        IReadOnlyList<SourcePagePointDefinition> points = mask.Shape == SourcePageMaskShape.Rectangle
            ? RectangleMaskPoints(mask.Points)
            : mask.Points;
        if (points.Count < 3)
            return;

        var polygon = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromArgb(145, 232, 91, 70)),
            Stroke = selectedMaskId == mask.Id ? StudioTheme.AccentBrush : Brushes.White,
            StrokeThickness = selectedMaskId == mask.Id ? 3 : 1.5,
            Tag = mask.Id,
            Cursor = Cursors.Hand,
        };
        foreach (SourcePagePointDefinition point in points)
            polygon.Points.Add(FromNormalized(point));
        polygon.MouseLeftButtonDown += (_, eventArgs) =>
        {
            if (activeTool != EditorTool.Select)
                return;
            selectedMaskId = mask.Id;
            ShowStatus("Маск сонгогдлоо. Delete дарж хасаж болно.");
            RedrawOverlay();
            eventArgs.Handled = true;
        };
        overlay.Children.Add(polygon);
    }

    private void DrawWorkingGeometry()
    {
        if (dragStart is Point start && dragCurrent is Point current)
        {
            Rect rect = NormalizeRect(start, current);
            AddOutlineRectangle(
                rect,
                activeTool == EditorTool.Crop ? StudioTheme.AccentBrush : Brushes.OrangeRed,
                3);
        }

        if (polygonPoints.Count == 0)
            return;

        var polyline = new Polyline
        {
            Stroke = Brushes.OrangeRed,
            StrokeThickness = 3,
        };
        foreach (SourcePagePointDefinition point in polygonPoints)
            polyline.Points.Add(FromNormalized(point));
        overlay.Children.Add(polyline);
        foreach (SourcePagePointDefinition point in polygonPoints)
        {
            Point canvasPoint = FromNormalized(point);
            var marker = new Ellipse
            {
                Width = 8,
                Height = 8,
                Fill = Brushes.OrangeRed,
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(marker, canvasPoint.X - 4);
            Canvas.SetTop(marker, canvasPoint.Y - 4);
            overlay.Children.Add(marker);
        }
    }

    private void AddFilledRectangle(Rect rect, Brush fill)
    {
        if (rect.Width <= 0 || rect.Height <= 0)
            return;
        var shape = new Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = fill,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(shape, rect.Left);
        Canvas.SetTop(shape, rect.Top);
        overlay.Children.Add(shape);
    }

    private void AddOutlineRectangle(Rect rect, Brush stroke, double thickness)
    {
        var shape = new Rectangle
        {
            Width = Math.Max(0, rect.Width),
            Height = Math.Max(0, rect.Height),
            Stroke = stroke,
            StrokeThickness = thickness,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(shape, rect.Left);
        Canvas.SetTop(shape, rect.Top);
        overlay.Children.Add(shape);
    }

    private Rect ResolveCropRect()
    {
        double left = Math.Clamp(working.LeftMm / sourceWidthMm * overlay.Width, 0, overlay.Width);
        double top = Math.Clamp(working.TopMm / sourceHeightMm * overlay.Height, 0, overlay.Height);
        double right = Math.Clamp(
            overlay.Width - working.RightMm / sourceWidthMm * overlay.Width,
            left,
            overlay.Width);
        double bottom = Math.Clamp(
            overlay.Height - working.BottomMm / sourceHeightMm * overlay.Height,
            top,
            overlay.Height);
        return new Rect(new Point(left, top), new Point(right, bottom));
    }

    private void BindPropertyValues()
    {
        leftBox.Text = FormatNumber(working.LeftMm);
        topBox.Text = FormatNumber(working.TopMm);
        rightBox.Text = FormatNumber(working.RightMm);
        bottomBox.Text = FormatNumber(working.BottomMm);
        offsetXBox.Text = FormatNumber(working.OffsetXmm);
        offsetYBox.Text = FormatNumber(working.OffsetYmm);
        rotationBox.Text = FormatNumber(working.RotationDegrees);
    }

    private void ApplyPropertyValues()
    {
        working.LeftMm = Math.Max(0, ParseNumber(leftBox.Text, working.LeftMm));
        working.TopMm = Math.Max(0, ParseNumber(topBox.Text, working.TopMm));
        working.RightMm = Math.Max(0, ParseNumber(rightBox.Text, working.RightMm));
        working.BottomMm = Math.Max(0, ParseNumber(bottomBox.Text, working.BottomMm));
        working.OffsetXmm = ParseNumber(offsetXBox.Text, working.OffsetXmm);
        working.OffsetYmm = ParseNumber(offsetYBox.Text, working.OffsetYmm);
        working.ScalePercent = 100;
        working.RotationDegrees = ParseNumber(rotationBox.Text, working.RotationDegrees);
        working.Enabled =
            working.LeftMm > 0 ||
            working.TopMm > 0 ||
            working.RightMm > 0 ||
            working.BottomMm > 0;
    }

    private void Accept()
    {
        ApplyPropertyValues();
        if (working.Enabled &&
            (working.LeftMm + working.RightMm >= sourceWidthMm ||
             working.TopMm + working.BottomMm >= sourceHeightMm))
        {
            ShowStatus("Crop хүрээ эх хуудсыг бүтнээр нь арилгаж байна.", isError: true);
            return;
        }
        if (working.Enabled)
            ClampStudioOffsets();
        DialogResult = true;
    }

    private Point ClampPoint(Point point) => new(
        Math.Clamp(point.X, 0, overlay.Width),
        Math.Clamp(point.Y, 0, overlay.Height));

    private SourcePagePointDefinition ToNormalized(Point point) => new()
    {
        X = overlay.Width <= 0 ? 0 : Math.Clamp(point.X / overlay.Width, 0, 1),
        Y = overlay.Height <= 0 ? 0 : Math.Clamp(point.Y / overlay.Height, 0, 1),
    };

    private Point FromNormalized(SourcePagePointDefinition point) => new(
        Math.Clamp(point.X, 0, 1) * overlay.Width,
        Math.Clamp(point.Y, 0, 1) * overlay.Height);

    private static Rect NormalizeRect(Point first, Point second) => new(
        new Point(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)),
        new Point(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)));

    private static IReadOnlyList<SourcePagePointDefinition> RectangleMaskPoints(
        IReadOnlyList<SourcePagePointDefinition> points)
    {
        if (points.Count < 2)
            return [];
        double left = Math.Min(points[0].X, points[1].X);
        double top = Math.Min(points[0].Y, points[1].Y);
        double right = Math.Max(points[0].X, points[1].X);
        double bottom = Math.Max(points[0].Y, points[1].Y);
        return
        [
            new SourcePagePointDefinition { X = left, Y = top },
            new SourcePagePointDefinition { X = right, Y = top },
            new SourcePagePointDefinition { X = right, Y = bottom },
            new SourcePagePointDefinition { X = left, Y = bottom },
        ];
    }

    private void ShowStatus(string text, bool isError = false)
    {
        statusText.Text = text;
        statusText.Foreground = isError ? StudioTheme.DangerBrush : StudioTheme.MutedTextBrush;
    }

    private static string FormatNumber(double value) =>
        value.ToString("0.##", CultureInfo.CurrentCulture);

    private static double ParseNumber(string text, double fallback)
    {
        const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
        if (double.TryParse(text.Trim(), styles, CultureInfo.CurrentCulture, out double value) ||
            double.TryParse(text.Trim(), styles, CultureInfo.InvariantCulture, out value))
        {
            return double.IsFinite(value) ? value : fallback;
        }
        return fallback;
    }

    private enum EditorTool
    {
        Select,
        Crop,
        RectangleMask,
        PolygonMask,
    }

    private enum EditorView
    {
        Source,
        Studio,
    }
}
