using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Reviewing a sheet of the album: the drawing at a size worth looking at,
/// marks drawn straight onto it, and the comment written where the mark was
/// made.
///
/// This is a surface the album view puts in place of its reader, not a window
/// of its own. A reviewer marks a fault and says what is wrong in one movement,
/// on the drawing, without a dialog coming between them and the sheet.
///
/// The drawing is rendered by Studio's own engine rather than shown through the
/// browser's viewer, because a reviewer has to be able to point at a place on
/// it - cloud what must change, box an area, aim an arrow at one line - and the
/// browser's viewer hands over neither its surface nor the point that was
/// clicked.
///
/// A mark is stored as fractions of the page. It therefore stays on the same
/// part of the same drawing at any zoom, after the album is rebuilt, and after
/// the sheet is re-issued at another size.
/// </summary>
internal sealed class SheetMarkupSurface : Grid
{
    private const double MinimumZoom = 0.25;
    private const double MaximumZoom = 6d;
    private double pageWidthPoints;
    private int renderedWidthPx;
    private int renderGeneration;
    private const double ComposerWidth = 330;

    private readonly StudioAccountService account;
    private readonly string projectId;
    private readonly bool canWrite;
    private readonly Action? onExit;

    private readonly ScrollViewer surfaceScroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = new SolidColorBrush(Color.FromRgb(48, 52, 58)),
        Padding = new Thickness(16),
    };
    private readonly Grid pageHost = new()
    {
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Image pageImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas markupLayer = new() { Background = Brushes.Transparent };

    /// <summary>
    /// Where the comment being written sits. It is a layer of its own because
    /// the marks below it are cleared and redrawn on every stroke, and text
    /// somebody is part-way through typing must survive that.
    /// </summary>
    private readonly Canvas overlayLayer = new() { Background = null };

    private readonly StackPanel threadPanel = new();
    private readonly TextBlock titleText = new()
    {
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Foreground = StudioTheme.TextBrush,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly TextBlock summaryText = new()
    {
        FontSize = 11.5,
        Foreground = StudioTheme.MutedTextBrush,
        Margin = new Thickness(0, 3, 0, 0),
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock zoomText = new()
    {
        FontSize = 11.5,
        Foreground = StudioTheme.MutedTextBrush,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 0, 0),
    };
    private readonly ComboBox kindBox = new() { Width = 176, Margin = new Thickness(0, 0, 10, 0) };
    private readonly Dictionary<string, ToggleButton> toolButtons = new(StringComparer.Ordinal);

    private string albumPdfPath = "";
    private int albumPageNumber = 1;
    private string pageIdentity = "";
    private string pageLabel = "";

    private PdfiumDocument? document;
    private double pageAspect = 297d / 420d;
    private double zoom = 1d;
    private bool hasFitted;
    private string activeTool = StudioSheetCommentRules.ShapeCloud;
    private IReadOnlyList<StudioSheetComment> comments = [];
    private string selectedCommentId = "";
    private readonly List<Point> drawing = [];
    private bool isDrawing;
    private bool busy;

    public SheetMarkupSurface(
        StudioAccountService account,
        string projectId,
        bool canWrite,
        Action? onExit = null)
    {
        this.account = account ?? throw new ArgumentNullException(nameof(account));
        this.projectId = (projectId ?? "").Trim();
        this.canWrite = canWrite;
        this.onExit = onExit;
        Children.Add(BuildLayout());
    }

    /// <summary>
    /// Shows one page of the album and the conversation already on it. The
    /// document is kept open across pages of the same album: a reviewer walks
    /// through the sheets, and re-reading tens of megabytes per step would be
    /// felt.
    /// </summary>
    public async Task ShowPageAsync(
        string albumPath,
        int pageNumber,
        string identity,
        string pageNumberText,
        string pageTitle)
    {
        albumPageNumber = Math.Max(1, pageNumber);
        pageIdentity = (identity ?? "").Trim();
        pageLabel = StudioSheetCommentRules.CleanPageLabel(
            StudioSheetCommentRules.PageLabel(pageNumberText, pageTitle));
        titleText.Text = pageLabel.Length == 0
            ? $"{albumPageNumber}-р хуудас"
            : $"{albumPageNumber}-р хуудас  ·  {pageLabel}";

        string path = (albumPath ?? "").Trim();
        if (!string.Equals(path, albumPdfPath, StringComparison.OrdinalIgnoreCase))
        {
            document?.Dispose();
            document = null;
            albumPdfPath = path;
        }

        comments = [];
        selectedCommentId = "";
        CancelDrawing();
        LoadPage();
        RenderThreads();
        await ReloadAsync();
    }

    /// <summary>Lets go of the album. The surface can be shown again after.</summary>
    public void Release()
    {
        document?.Dispose();
        document = null;
        albumPdfPath = "";
        pageImage.Source = null;
        hasFitted = false;
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel();

        UIElement header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var threads = new Border
        {
            Width = 340,
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Margin = new Thickness(10, 0, 0, 0),
            Padding = new Thickness(12, 10, 6, 10),
            Child = new ScrollViewer
            {
                Content = threadPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
        DockPanel.SetDock(threads, Dock.Right);
        root.Children.Add(threads);

        pageHost.Children.Add(pageImage);
        pageHost.Children.Add(markupLayer);
        pageHost.Children.Add(overlayLayer);
        markupLayer.MouseLeftButtonDown += OnSurfacePressed;
        markupLayer.MouseMove += OnSurfaceMoved;
        markupLayer.MouseLeftButtonUp += OnSurfaceReleased;
        surfaceScroll.Content = pageHost;
        surfaceScroll.PreviewMouseWheel += OnWheel;
        // The page is fitted the first time the surface has a real width. Until
        // then there is nothing to fit it to, and opening at an arbitrary zoom
        // would leave the reviewer hunting for the drawing.
        surfaceScroll.SizeChanged += (_, _) =>
        {
            if (!hasFitted && surfaceScroll.ViewportWidth > 80)
                FitToWindow();
        };

        root.Children.Add(new Border
        {
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = surfaceScroll,
        });
        return root;
    }

    private UIElement BuildHeader()
    {
        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

        var titleRow = new DockPanel();
        if (onExit is not null)
        {
            Button close = StudioWidgets.CreateButton("Тэмдэглэгээ хаах");
            close.ToolTip = "Тэмдэглэгээний горимоос гарч, уншиж харах горимд буцах";
            DockPanel.SetDock(close, Dock.Right);
            close.Click += (_, _) => onExit();
            titleRow.Children.Add(close);
        }
        titleRow.Children.Add(titleText);
        header.Children.Add(titleRow);
        header.Children.Add(summaryText);

        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
        };
        foreach (string shape in StudioSheetCommentRules.Shapes)
            tools.Children.Add(BuildToolButton(shape));

        tools.Children.Add(new Border
        {
            Width = 1,
            Background = StudioTheme.BorderBrush,
            Margin = new Thickness(10, 4, 12, 4),
        });
        kindBox.ItemsSource = StudioSheetCommentRules.Kinds
            .Select(kind => new KindChoice(kind, StudioSheetCommentRules.KindLabel(kind)))
            .ToList();
        kindBox.DisplayMemberPath = nameof(KindChoice.Label);
        kindBox.SelectedIndex = 0;
        kindBox.VerticalAlignment = VerticalAlignment.Center;
        kindBox.IsEnabled = canWrite;
        tools.Children.Add(kindBox);

        tools.Children.Add(BuildTextButton("−", "Багасгах", () => SetZoom(zoom / 1.25)));
        tools.Children.Add(BuildTextButton("+", "Томсгох", () => SetZoom(zoom * 1.25)));
        tools.Children.Add(BuildTextButton("Багтаах", "Хуудсыг дэлгэцэд багтаах", FitToWindow));
        tools.Children.Add(zoomText);
        header.Children.Add(tools);
        return header;
    }

    private ToggleButton BuildToolButton(string shape)
    {
        var button = new ToggleButton
        {
            Content = StudioSheetCommentRules.ShapeLabel(shape),
            ToolTip = StudioSheetCommentRules.ShapeLabel(shape) + " зурах",
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 0, 6, 0),
            IsChecked = shape == activeTool,
            IsEnabled = canWrite,
        };
        button.Checked += (_, _) => SelectTool(shape);
        button.Click += (_, _) => button.IsChecked = true;
        toolButtons[shape] = button;
        return button;
    }

    private static Button BuildTextButton(string text, string tooltip, Action action)
    {
        var button = new Button
        {
            Content = text,
            ToolTip = tooltip,
            MinWidth = 34,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(6, 0, 0, 0),
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void SelectTool(string shape)
    {
        activeTool = shape;
        foreach ((string key, ToggleButton button) in toolButtons)
            button.IsChecked = key == shape;
    }

    private void LoadPage()
    {
        document ??= PdfiumDocument.Open(albumPdfPath);
        if (document is null)
        {
            pageImage.Source = null;
            summaryText.Text = "Хуудсыг нээж чадсангүй.";
            return;
        }

        pageAspect = document.GetPageAspect(albumPageNumber);
        pageWidthPoints = document.GetPageWidthPoints(albumPageNumber);
        renderedWidthPx = 0;
        pageImage.Source = document.RenderPage(
            albumPageNumber,
            PreviewRenderResolution.FirstPassWidthPx);
        renderedWidthPx = PreviewRenderResolution.FirstPassWidthPx;
        FitToWindow();
    }

    /// <summary>
    /// Rasterises the page again when the zoom has outgrown the image on
    /// screen, up to 300 DPI on the paper.
    /// </summary>
    /// <remarks>
    /// Off the UI thread and without clearing what is displayed: a full-
    /// resolution A1 takes a noticeable moment, and a blank sheet while it
    /// renders reads as a bug. The old image stays, slightly soft, until the
    /// sharper one is ready.
    ///
    /// The generation counter is what makes a zoom drag safe. Each change
    /// starts a render; they finish in whatever order they finish, and without
    /// it a slow early one could land after a fast later one and put a coarse
    /// image back on a sheet the user has already zoomed past.
    /// </remarks>
    private async void RefreshPageResolution()
    {
        if (document is null)
            return;

        // pageHost.Width is in device-independent units. On a 150% display the
        // sheet covers half again as many real pixels, and rendering to the
        // DIP count would leave it soft on exactly the screens people run.
        double dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        int wanted = PreviewRenderResolution.ForDisplay(
            pageWidthPoints,
            pageHost.Width * (dpiScale > 0 ? dpiScale : 1d));
        if (wanted <= renderedWidthPx)
            return;

        int generation = ++renderGeneration;
        PdfiumDocument open = document;
        int pageNumber = albumPageNumber;

        BitmapSource? sharper = await Task.Run(() => open.RenderPage(pageNumber, wanted));
        if (sharper is null || generation != renderGeneration)
            return;

        pageImage.Source = sharper;
        renderedWidthPx = wanted;
    }

    /// <summary>
    /// Sizes the sheet so all of it is on screen. A drawing is read whole - a
    /// reviewer looks over the sheet for what is wrong before deciding where to
    /// zoom - so the fit is to both sides of the page, not just its width.
    /// </summary>
    private void FitToWindow()
    {
        double availableWidth = surfaceScroll.ViewportWidth - 48;
        double availableHeight = surfaceScroll.ViewportHeight - 48;
        if (availableWidth <= 80 || availableHeight <= 80)
        {
            hasFitted = false;
            SetZoom(zoom);
            return;
        }

        hasFitted = true;
        SetZoom(Math.Min(
            availableWidth / 1000d,
            availableHeight / Math.Max(1d, 1000d * pageAspect)));
    }

    private void SetZoom(double value)
    {
        zoom = Math.Clamp(value, MinimumZoom, MaximumZoom);
        double width = Math.Max(200d, 1000d * zoom);
        pageHost.Width = width;
        pageHost.Height = Math.Max(120d, width * pageAspect);
        zoomText.Text = $"{Math.Round(zoom * 100)}%";
        RefreshPageResolution();
        RenderMarkups();
        RepositionComposer();
    }

    private void OnWheel(object sender, MouseWheelEventArgs args)
    {
        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        args.Handled = true;
        SetZoom(args.Delta > 0 ? zoom * 1.15 : zoom / 1.15);
    }

    // Drawing. A mark is collected in fractions from the first press, so the
    // zoom it was drawn at never enters what is stored.

    private void OnSurfacePressed(object sender, MouseButtonEventArgs args)
    {
        if (!canWrite || pageHost.Width <= 0)
            return;

        overlayLayer.Children.Clear();
        isDrawing = true;
        drawing.Clear();
        drawing.Add(ToFraction(args.GetPosition(markupLayer)));
        markupLayer.CaptureMouse();
    }

    private void OnSurfaceMoved(object sender, MouseEventArgs args)
    {
        if (!isDrawing)
            return;

        Point point = ToFraction(args.GetPosition(markupLayer));
        if (activeTool is StudioSheetCommentRules.ShapeRectangle or
            StudioSheetCommentRules.ShapeArrow)
        {
            if (drawing.Count == 1)
                drawing.Add(point);
            else
                drawing[^1] = point;
        }
        else if (activeTool == StudioSheetCommentRules.ShapePin)
        {
            drawing[0] = point;
        }
        else
        {
            drawing.Add(point);
        }

        RenderMarkups();
    }

    private void OnSurfaceReleased(object sender, MouseButtonEventArgs args)
    {
        if (!isDrawing)
            return;

        isDrawing = false;
        markupLayer.ReleaseMouseCapture();
        if (drawing.Count < StudioSheetCommentRules.MinimumPointsFor(activeTool))
        {
            CancelDrawing();
            return;
        }

        RenderMarkups();
        ShowComposer();
    }

    private Point ToFraction(Point position) => new(
        Math.Clamp(position.X / Math.Max(1d, pageHost.Width), 0d, 1d),
        Math.Clamp(position.Y / Math.Max(1d, pageHost.Height), 0d, 1d));

    private void CancelDrawing()
    {
        isDrawing = false;
        drawing.Clear();
        overlayLayer.Children.Clear();
        RenderMarkups();
    }

    private void RenderMarkups()
    {
        markupLayer.Children.Clear();
        if (pageHost.Width <= 0 || pageHost.Height <= 0)
            return;

        int index = 1;
        foreach (StudioSheetComment comment in comments)
        {
            DrawMark(
                comment.Shape,
                comment.ShapePoints.Select(point => new Point(point.X, point.Y)).ToList(),
                StudioSheetCommentRules.KindBrush(comment.Kind),
                StudioSheetCommentRules.IsResolved(comment.Status) ? 0.4 : 1d,
                comment.CommentId.Equals(selectedCommentId, StringComparison.OrdinalIgnoreCase),
                index,
                comment.CommentId);
            index++;
        }

        if (drawing.Count > 0)
        {
            DrawMark(
                activeTool,
                drawing,
                StudioSheetCommentRules.KindBrush(SelectedKind),
                0.85,
                selected: true,
                number: null,
                commentId: null);
        }
    }

    private void DrawMark(
        string? shape,
        IReadOnlyList<Point> points,
        Brush brush,
        double opacity,
        bool selected,
        int? number,
        string? commentId)
    {
        Geometry? geometry = SheetMarkupGeometry.Build(
            shape,
            points,
            pageHost.Width,
            pageHost.Height);
        if (geometry is null)
            return;

        var path = new Path
        {
            Data = geometry,
            Stroke = brush,
            StrokeThickness = selected ? 3.5 : 2.5,
            Opacity = opacity,
            Fill = StudioSheetCommentRules.NormalizeShape(shape) == StudioSheetCommentRules.ShapePin
                ? brush
                : null,
            Cursor = commentId is null ? Cursors.Arrow : Cursors.Hand,
        };
        if (commentId is not null)
        {
            path.MouseLeftButtonDown += (_, args) =>
            {
                args.Handled = true;
                selectedCommentId = commentId;
                RenderMarkups();
                RenderThreads();
            };
        }
        markupLayer.Children.Add(path);

        if (number is null)
            return;

        Point anchor = SheetMarkupGeometry.ResolveLabelAnchor(shape, points);
        var badge = new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(11),
            Width = 22,
            Height = 22,
            Opacity = opacity,
            Child = new TextBlock
            {
                Text = number.Value.ToString(),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
        Canvas.SetLeft(badge, (anchor.X * pageHost.Width) - 11);
        Canvas.SetTop(badge, (anchor.Y * pageHost.Height) - 26);
        markupLayer.Children.Add(badge);
    }

    private string SelectedKind => kindBox.SelectedItem is KindChoice choice
        ? choice.Kind
        : StudioSheetCommentRules.KindChangeRequired;

    private async Task ReloadAsync()
    {
        if (pageIdentity.Length == 0)
        {
            summaryText.Text = "Энэ хуудсанд тэмдэглэгээ бэхлэх тогтвортой дугаар алга.";
            return;
        }

        try
        {
            Apply(await account.ListSheetCommentsAsync(projectId, pageIdentity));
        }
        catch (Exception exception) when (
            exception is StudioAccountException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            summaryText.Text = "Тэмдэглэгээг уншиж чадсангүй: " + exception.Message;
        }
    }

    private void Apply(StudioSheetCommentList list)
    {
        comments = StudioSheetCommentRules.InReadingOrder(list.Comments).ToList();
        int open = comments.Count(item => !StudioSheetCommentRules.IsResolved(item.Status));
        int change = comments.Count(item =>
            !StudioSheetCommentRules.IsResolved(item.Status) &&
            StudioSheetCommentRules.Normalize(item.Kind) == StudioSheetCommentRules.KindChangeRequired);
        summaryText.Text = comments.Count == 0
            ? "Энэ хуудсанд тэмдэглэгээ алга. Багажаа сонгоод зураг дээрээ зурна уу."
            : $"{comments.Count} тэмдэглэгээ · {open} нээлттэй · {change} засах шаардлагатай";

        // A comment whose drawn mark did not come back has nothing to show on
        // the page. That is worth saying plainly rather than leaving a reviewer
        // to wonder where their cloud went: it means the project is talking to a
        // server that predates marks.
        int unmarked = comments.Count(item => item.ShapePoints.Count == 0);
        if (unmarked > 0)
        {
            summaryText.Text +=
                $" · {unmarked} тэмдэглэгээний зураас серверээс ирсэнгүй " +
                "(сервер хуучин хувилбар байна)";
        }

        CancelDrawing();
        RenderThreads();
    }

    private void RenderThreads()
    {
        threadPanel.Children.Clear();
        if (comments.Count == 0)
        {
            threadPanel.Children.Add(StudioWidgets.CreateHint(
                canWrite
                    ? "Дээрээс багажаа сонгоод зураг дээр зурна уу. Зурж дуусахад тэр газартаа коммент бичих талбар нээгдэнэ."
                    : "Энэ хуудсанд тэмдэглэгээ алга."));
            return;
        }

        int index = 1;
        foreach (StudioSheetComment comment in comments)
        {
            threadPanel.Children.Add(BuildThreadCard(comment, index));
            index++;
        }
    }

    /// <summary>
    /// Opens the comment box on the page, beside the mark that was just drawn.
    /// The reviewer marks the fault and says what is wrong without leaving the
    /// drawing, which is the whole point of marking it there.
    /// </summary>
    private void ShowComposer()
    {
        overlayLayer.Children.Clear();
        if (!canWrite || drawing.Count == 0)
            return;

        var card = new Border
        {
            Width = ComposerWidth,
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioSheetCommentRules.KindBrush(SelectedKind),
            BorderThickness = new Thickness(2),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 18,
                ShadowDepth = 3,
                Opacity = 0.5,
                Color = Colors.Black,
            },
        };
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = StudioSheetCommentRules.ShapeLabel(activeTool) + " · " +
                StudioSheetCommentRules.KindLabel(SelectedKind),
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioSheetCommentRules.KindBrush(SelectedKind),
            Margin = new Thickness(0, 0, 0, 8),
        });
        var input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 82,
            MaxLength = StudioSheetCommentRules.MaximumBodyLength,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        body.Children.Add(input);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => CancelDrawing();
        actions.Children.Add(cancel);
        Button save = StudioWidgets.CreatePrimaryButton("Хадгалах");
        save.Click += async (_, _) => await AddAsync(input.Text);
        actions.Children.Add(save);
        body.Children.Add(actions);

        // Enter sends, Shift+Enter breaks the line, Esc drops the mark. A
        // reviewer works through a sheet quickly and should not have to reach
        // for the mouse between marks.
        input.KeyDown += async (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                args.Handled = true;
                CancelDrawing();
            }
            else if (args.Key == Key.Enter && Keyboard.Modifiers != ModifierKeys.Shift)
            {
                args.Handled = true;
                await AddAsync(input.Text);
            }
        };

        card.Child = body;
        overlayLayer.Children.Add(card);
        RepositionComposer();
        input.Focus();
        Keyboard.Focus(input);
    }

    /// <summary>
    /// Keeps the open comment box beside its mark and inside the page, so a
    /// mark drawn at the right edge does not push the box out of sight.
    /// </summary>
    private void RepositionComposer()
    {
        if (overlayLayer.Children.Count == 0 ||
            overlayLayer.Children[0] is not FrameworkElement card ||
            drawing.Count == 0)
        {
            return;
        }

        card.Measure(new Size(ComposerWidth, double.PositiveInfinity));
        Point place = SheetMarkupGeometry.PlaceComposer(
            drawing,
            pageHost.Width,
            pageHost.Height,
            ComposerWidth,
            card.DesiredSize.Height);
        Canvas.SetLeft(card, place.X);
        Canvas.SetTop(card, place.Y);
    }

    private UIElement BuildThreadCard(StudioSheetComment comment, int index)
    {
        bool resolved = StudioSheetCommentRules.IsResolved(comment.Status);
        bool selected = comment.CommentId.Equals(selectedCommentId, StringComparison.OrdinalIgnoreCase);
        var card = new Border
        {
            Background = selected ? StudioTheme.InputBrush : StudioTheme.PanelAltBrush,
            BorderBrush = selected
                ? StudioSheetCommentRules.KindBrush(comment.Kind)
                : StudioTheme.BorderBrush,
            BorderThickness = new Thickness(selected ? 2 : 1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 6, 10),
            Opacity = resolved ? 0.75 : 1d,
            Cursor = Cursors.Hand,
        };
        string commentId = comment.CommentId;
        card.MouseLeftButtonUp += (_, _) =>
        {
            selectedCommentId = commentId;
            RenderMarkups();
            RenderThreads();
        };

        var body = new StackPanel();
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = StudioSheetCommentRules.KindBrush(comment.Kind),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock
            {
                Text = index.ToString(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        top.Children.Add(new TextBlock
        {
            Text = StudioSheetCommentRules.KindLabel(comment.Kind) + "  ·  " +
                StudioSheetCommentRules.ShapeLabel(comment.Shape) + "  ·  " +
                StudioSheetCommentRules.StatusLabel(comment.Status),
            FontSize = 11,
            Foreground = resolved ? StudioTheme.SuccessBrush : StudioTheme.MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(top);

        body.Children.Add(new TextBlock
        {
            Text = comment.Body,
            FontSize = 13,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0),
        });
        body.Children.Add(new TextBlock
        {
            Text = comment.AuthorDisplayName + " · " +
                comment.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            FontSize = 10.5,
            Foreground = StudioTheme.FaintTextBrush,
            Margin = new Thickness(0, 7, 0, 0),
        });

        foreach (StudioSheetCommentReply reply in comment.Replies)
            body.Children.Add(BuildReply(reply));

        if (selected && canWrite)
            body.Children.Add(BuildReplyBox(comment));
        if (selected && comment.CanManage)
            body.Children.Add(BuildManageRow(comment, resolved));

        card.Child = body;
        return card;
    }

    private static UIElement BuildReply(StudioSheetCommentReply reply)
    {
        var card = new Border
        {
            Background = StudioTheme.PanelBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(14, 8, 0, 0),
        };
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = reply.Body,
            FontSize = 12,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = reply.AuthorDisplayName + " · " +
                reply.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            FontSize = 10,
            Foreground = StudioTheme.FaintTextBrush,
            Margin = new Thickness(0, 5, 0, 0),
        });
        card.Child = body;
        return card;
    }

    private UIElement BuildReplyBox(StudioSheetComment comment)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 10, 0, 0) };
        var input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 54,
            MaxLength = StudioSheetCommentRules.MaximumBodyLength,
        };
        panel.Children.Add(input);
        Button send = StudioWidgets.CreateButton("Хариулах");
        send.HorizontalAlignment = HorizontalAlignment.Right;
        send.Margin = new Thickness(0, 8, 0, 0);
        send.Click += async (_, _) => await RunAsync(() =>
            account.ReplyToSheetCommentAsync(projectId, comment.CommentId, input.Text));
        panel.Children.Add(send);
        return panel;
    }

    private UIElement BuildManageRow(StudioSheetComment comment, bool resolved)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        Button toggle = StudioWidgets.CreateButton(resolved ? "Дахин нээх" : "Шийдэгдсэн болгох");
        toggle.Margin = new Thickness(0, 0, 8, 0);
        toggle.Click += async (_, _) => await RunAsync(() => account.SetSheetCommentStatusAsync(
            projectId,
            comment.CommentId,
            resolved ? StudioSheetCommentRules.StatusOpen : StudioSheetCommentRules.StatusResolved));
        row.Children.Add(toggle);
        Button remove = StudioWidgets.CreateButton("Устгах");
        remove.Click += async (_, _) =>
        {
            if (MessageBox.Show(
                    Window.GetWindow(this),
                    "Энэ тэмдэглэгээг устгах уу? Хариултууд нь хамт устана.",
                    "Тэмдэглэгээ устгах",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) != MessageBoxResult.OK)
            {
                return;
            }

            selectedCommentId = "";
            await RunAsync(() => account.DeleteSheetCommentAsync(projectId, comment.CommentId));
        };
        row.Children.Add(remove);
        return row;
    }

    private async Task AddAsync(string body)
    {
        string text = StudioSheetCommentRules.CleanBody(body);
        if (text.Length == 0 || busy || drawing.Count == 0)
            return;

        Point anchor = SheetMarkupGeometry.ResolveLabelAnchor(activeTool, drawing);
        var request = new StudioSheetCommentCreateRequest
        {
            PageIdentity = pageIdentity,
            PageLabel = pageLabel,
            PageNumber = albumPageNumber,
            AnchorX = anchor.X,
            AnchorY = anchor.Y,
            Shape = activeTool,
            // Thinned here rather than left to the server, so the mark that is
            // stored is the mark that was just drawn on screen.
            ShapePoints = StudioSheetCommentRules.Thin(drawing)
                .Select(point => new StudioSheetCommentPoint { X = point.X, Y = point.Y })
                .ToList(),
            Kind = SelectedKind,
            Body = text,
        };
        await RunAsync(() => account.AddSheetCommentAsync(projectId, request));
    }

    private async Task RunAsync(Func<Task<StudioSheetCommentList>> operation)
    {
        busy = true;
        try
        {
            Apply(await operation());
        }
        catch (Exception exception) when (
            exception is StudioAccountException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            summaryText.Text = "Үйлдэл гүйцэтгэгдсэнгүй: " + exception.Message;
        }
        finally
        {
            busy = false;
        }
    }

    private sealed record KindChoice(string Kind, string Label);
}
