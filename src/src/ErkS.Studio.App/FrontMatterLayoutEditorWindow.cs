using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// The working drawings' 01 «ЕРӨНХИЙ ХЭСЭГ» laid out by hand: the page is shown,
/// the front-matter blocks stand on it, and a selected block is moved and
/// resized by its dot grips; its columns are added, removed and spaced on the
/// page. Edges snap to guides - the corner table, the drawing frame, the other
/// blocks, the page centre - and the guide shows while it holds.
/// </summary>
/// <remarks>
/// Owner, 2026-10-20: «бичих биш өөрийг нь идэвхжүүлээд хөөрхөн цэгэн болон
/// тохирсон гоё guide уудаар удирддаг байна». The geometry lives in
/// <see cref="FrontMatterLayoutEditing"/>; this window only draws and listens.
/// </remarks>
internal sealed class FrontMatterLayoutEditorWindow : Window
{
    private const double Unit = 2.0; // canvas units per millimetre
    private const double GripRadius = 4.5;

    private static readonly Color BlockColor = Color.FromRgb(0x2F, 0x80, 0xED);
    private static readonly Color GuideColor = Color.FromRgb(0xE0, 0x3E, 0x8C);
    private static readonly Color MoveColor = Color.FromRgb(0xF2, 0x8C, 0x28);

    private readonly PdfPageImageCache imageCache;
    private readonly SheetRecord sheet;
    private readonly double pageWidthMm;
    private readonly double pageHeightMm;
    private readonly PageRectMm corner;
    private readonly PageRectMm innerFrame;
    private readonly IReadOnlyDictionary<string, PageRectSpec> reserved;
    private readonly IReadOnlyList<FrontMatterBlockLayout> defaults;
    private readonly List<FrontMatterBlockLayout> blocks;
    private readonly HashSet<string> placedByOwner;
    private readonly Canvas canvas = new() { Background = Brushes.Transparent, ClipToBounds = true };
    private readonly Image pageImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly TextBlock statusText = StudioWidgets.CreateHint("");
    private string? selectedKind;
    private FrontMatterGrip activeGrip;
    private Point dragOrigin;
    private FrontMatterBlockLayout? dragStart;
    private double? guideX;
    private double? guideY;
    private readonly List<SourceRegionMove> moves;
    private int? selectedMove;
    private SourceRegionMove? moveStart;
    private bool selectingRegion;
    private Point? regionStart;
    private Point? regionCurrent;
    private BitmapSource? pageBitmap;

    public IReadOnlyList<SourceRegionMove> MovesResult => moves.Select(move => move.Clone()).ToList();

    public IReadOnlyList<FrontMatterBlockLayout> Result =>
        blocks.Where(block => placedByOwner.Contains(block.Kind)).Select(block => block.Normalized()).ToList();

    public FrontMatterLayoutEditorWindow(
        PdfPageImageCache imageCache,
        SheetRecord sheet,
        PageRectMm corner,
        PageRectMm innerFrame,
        IReadOnlyDictionary<string, PageRectSpec> reserved,
        IReadOnlyList<FrontMatterBlockLayout> current,
        IReadOnlyList<FrontMatterBlockLayout> defaults,
        IReadOnlyCollection<string> placedByOwner,
        IReadOnlyList<SourceRegionMove>? currentMoves = null)
    {
        moves = (currentMoves ?? []).Select(move => move.Clone()).ToList();
        this.imageCache = imageCache;
        this.sheet = sheet;
        this.corner = corner;
        this.innerFrame = innerFrame;
        this.reserved = reserved;
        this.defaults = defaults;
        this.placedByOwner = new HashSet<string>(placedByOwner, StringComparer.Ordinal);
        blocks = current.Select(block => block.Clone()).ToList();
        pageWidthMm = sheet.Entry.WidthMm > 0 ? sheet.Entry.WidthMm : 420;
        pageHeightMm = sheet.Entry.HeightMm > 0 ? sheet.Entry.HeightMm : 297;

        Title = "ЕХ байрлал — 01 «ЕРӨНХИЙ ХЭСЭГ»";
        Width = 1400;
        Height = 960;
        MinWidth = 900;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        StudioTheme.Apply(this);
        Content = BuildContent();
        Loaded += async (_, _) => await LoadPageAsync();
        PreviewKeyDown += HandleKey;
        Redraw();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        Button pickRegion = StudioWidgets.CreateButton("+ Зургийн хэсэг зөөх");
        pickRegion.Margin = new Thickness(0, 0, 8, 0);
        pickRegion.ToolTip = "Хуудасны зургийн хэсгийг (байршлын зураг, фасад г.м.) тэгш өнцөгтөөр сонгоод чирж зөөнө.";
        pickRegion.Click += (_, _) =>
        {
            selectingRegion = true;
            selectedKind = null;
            selectedMove = null;
            canvas.Cursor = Cursors.Cross;
            statusText.Text = "Зөөх хэсгээ хулганаар тэгш өнцөгтөөр татаж сонгоно уу.";
            Redraw();
        };
        actions.Children.Add(pickRegion);
        Button resetAll = StudioWidgets.CreateButton("Бүгдийг анхдагч руу");
        resetAll.Margin = new Thickness(0, 0, 8, 0);
        resetAll.Click += (_, _) =>
        {
            placedByOwner.Clear();
            blocks.Clear();
            blocks.AddRange(defaults.Select(block => block.Clone()));
            Redraw();
        };
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => DialogResult = false;
        Button save = StudioWidgets.CreatePrimaryButton("Хадгалах");
        save.Click += (_, _) => DialogResult = true;
        actions.Children.Add(resetAll);
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        statusText.Margin = new Thickness(0, 0, 0, 8);
        statusText.Text = "Блок дээр дарж идэвхжүүлнэ. Цэгээр хэмжээг, дотроос нь чирж байрлалыг, " +
                          "дунд цэгээр баганын зайг өөрчилнө. «−» «+» баганын тоо. Сум товч 1 мм, Shift+сум 5 мм.";
        DockPanel.SetDock(statusText, Dock.Top);
        root.Children.Add(statusText);

        var surface = new Grid
        {
            Width = pageWidthMm * Unit,
            Height = pageHeightMm * Unit,
            Background = Brushes.White,
        };
        surface.Children.Add(pageImage);
        canvas.Width = surface.Width;
        canvas.Height = surface.Height;
        surface.Children.Add(canvas);
        canvas.MouseLeftButtonDown += HandleMouseDown;
        canvas.MouseMove += HandleMouseMove;
        canvas.MouseLeftButtonUp += HandleMouseUp;

        root.Children.Add(new Border
        {
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            Background = StudioTheme.PanelAltBrush,
            Padding = new Thickness(8),
            Child = new Viewbox { Stretch = Stretch.Uniform, Child = surface },
        });
        return root;
    }

    private async Task LoadPageAsync()
    {
        try
        {
            BitmapSource? bitmap = await imageCache.GetPageAsync(
                sheet.PdfPath,
                Math.Max(1, sheet.Entry.PdfPageNumber),
                2400,
                CancellationToken.None);
            if (bitmap is not null)
            {
                pageImage.Source = bitmap;
                pageBitmap = bitmap;
                Redraw();
            }
        }
        catch
        {
            // The blocks can still be arranged on a blank page.
        }
    }

    // ---- drawing ----------------------------------------------------------

    private void Redraw()
    {
        canvas.Children.Clear();

        // The drawing frame and the corner table: fixed, shown as references.
        AddRect(innerFrame, null, new SolidColorBrush(Color.FromArgb(90, 120, 120, 120)), 1, [4, 4]);
        AddRect(corner, new SolidColorBrush(Color.FromArgb(40, 90, 90, 90)), new SolidColorBrush(Color.FromArgb(160, 90, 90, 90)), 1.2, null);
        AddLabel(corner.X, corner.Y + corner.Height / 2 - 4, corner.Width, "Булангийн хүснэгт", Color.FromRgb(90, 90, 90), center: true);

        // Where the source reserved the frames - faint, for orientation.
        foreach (PageRectSpec frame in reserved.Values)
        {
            AddRect(
                new PageRectMm { X = frame.X, Y = frame.Y, Width = frame.Width, Height = frame.Height },
                null,
                new SolidColorBrush(Color.FromArgb(70, 150, 150, 150)),
                0.8,
                [2, 3]);
        }

        for (int index = 0; index < moves.Count; index++)
            DrawMove(moves[index], index == selectedMove);

        foreach (FrontMatterBlockLayout block in blocks)
            DrawBlock(block, block.Kind == selectedKind);

        if (regionStart is Point a && regionCurrent is Point b)
        {
            var selection = new Rectangle
            {
                Width = Math.Abs(b.X - a.X),
                Height = Math.Abs(b.Y - a.Y),
                Stroke = new SolidColorBrush(MoveColor),
                StrokeThickness = 1.5,
                StrokeDashArray = new DoubleCollection([5, 3]),
                Fill = new SolidColorBrush(Color.FromArgb(30, MoveColor.R, MoveColor.G, MoveColor.B)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(selection, Math.Min(a.X, b.X));
            Canvas.SetTop(selection, Math.Min(a.Y, b.Y));
            canvas.Children.Add(selection);
        }

        if (guideX is double gx)
            AddGuide(gx * Unit, 0, gx * Unit, canvas.Height);
        if (guideY is double gy)
            AddGuide(0, gy * Unit, canvas.Width, gy * Unit);
    }

    private void DrawBlock(FrontMatterBlockLayout block, bool selected)
    {
        var rect = new PageRectMm { X = block.X, Y = block.Y, Width = block.Width, Height = block.Height };
        byte fill = selected ? (byte)46 : (byte)26;
        AddRect(
            rect,
            new SolidColorBrush(Color.FromArgb(fill, BlockColor.R, BlockColor.G, BlockColor.B)),
            new SolidColorBrush(Color.FromArgb(selected ? (byte)255 : (byte)170, BlockColor.R, BlockColor.G, BlockColor.B)),
            selected ? 2 : 1.2,
            null);

        // Columns: each one shows the run of its lines.
        int columns = Math.Max(1, block.Columns);
        double columnWidth = block.ColumnWidthMm;
        for (int column = 0; column < columns; column++)
        {
            double x = block.X + column * (columnWidth + block.ColumnGapMm);
            if (column > 0)
            {
                AddLine(x * Unit, block.Y * Unit, x * Unit, (block.Y + block.Height) * Unit,
                    Color.FromArgb(140, BlockColor.R, BlockColor.G, BlockColor.B), 1, [3, 3]);
                double gapLeft = x - block.ColumnGapMm;
                AddLine(gapLeft * Unit, block.Y * Unit, gapLeft * Unit, (block.Y + block.Height) * Unit,
                    Color.FromArgb(140, BlockColor.R, BlockColor.G, BlockColor.B), 1, [3, 3]);
            }
            for (double y = block.Y + 12; y < block.Y + block.Height - 4; y += 6)
            {
                AddLine((x + 2) * Unit, y * Unit, (x + columnWidth - 2) * Unit, y * Unit,
                    Color.FromArgb(55, BlockColor.R, BlockColor.G, BlockColor.B), 1.6, null);
            }
        }

        string title = KindTitle(block.Kind) + (columns > 1 ? $" · {columns} багана" : "");
        AddLabel(block.X + 2, block.Y + 2, block.Width - 4, title, BlockColor, center: false);

        if (!selected)
            return;

        foreach ((FrontMatterGrip grip, double gx, double gy) in Grips(block))
            AddGrip(gx, gy, Brushes.White, new SolidColorBrush(BlockColor));
        if (columns > 1)
        {
            double gapCenter = block.X + columnWidth + block.ColumnGapMm / 2;
            AddGrip(gapCenter, block.Y + block.Height / 2, new SolidColorBrush(GuideColor), Brushes.White);
        }
        AddColumnBar(block);
    }

    /// <summary>
    /// A moved part of the drawing: its old place as a dashed outline, the
    /// part itself - a crop of the page - where it now stands.
    /// </summary>
    private void DrawMove(SourceRegionMove move, bool selected)
    {
        AddRect(
            new PageRectMm { X = move.X, Y = move.Y, Width = move.Width, Height = move.Height },
            new SolidColorBrush(Color.FromArgb(200, 255, 255, 255)),
            new SolidColorBrush(Color.FromArgb(150, 140, 140, 140)),
            1,
            [3, 3]);
        var destination = new PageRectMm
        {
            X = move.X + move.OffsetX,
            Y = move.Y + move.OffsetY,
            Width = move.Width,
            Height = move.Height,
        };
        if (pageBitmap is not null)
        {
            double scale = pageBitmap.PixelWidth / pageWidthMm;
            var crop = new Int32Rect(
                (int)Math.Clamp(Math.Round(move.X * scale), 0, pageBitmap.PixelWidth - 1),
                (int)Math.Clamp(Math.Round(move.Y * scale), 0, pageBitmap.PixelHeight - 1),
                0,
                0);
            crop.Width = (int)Math.Clamp(Math.Round(move.Width * scale), 1, pageBitmap.PixelWidth - crop.X);
            crop.Height = (int)Math.Clamp(Math.Round(move.Height * scale), 1, pageBitmap.PixelHeight - crop.Y);
            var image = new Image
            {
                Source = new CroppedBitmap(pageBitmap, crop),
                Width = destination.Width * Unit,
                Height = destination.Height * Unit,
                Stretch = Stretch.Fill,
                IsHitTestVisible = false,
            };
            var paper = new Rectangle { Width = image.Width, Height = image.Height, Fill = Brushes.White, IsHitTestVisible = false };
            Canvas.SetLeft(paper, destination.X * Unit);
            Canvas.SetTop(paper, destination.Y * Unit);
            canvas.Children.Add(paper);
            Canvas.SetLeft(image, destination.X * Unit);
            Canvas.SetTop(image, destination.Y * Unit);
            canvas.Children.Add(image);
        }
        AddRect(
            destination,
            new SolidColorBrush(Color.FromArgb(selected ? (byte)28 : (byte)12, MoveColor.R, MoveColor.G, MoveColor.B)),
            new SolidColorBrush(Color.FromArgb(selected ? (byte)255 : (byte)180, MoveColor.R, MoveColor.G, MoveColor.B)),
            selected ? 2 : 1.2,
            null);
        AddLabel(destination.X + 2, destination.Y + 2, destination.Width - 4, "Зургийн хэсэг", MoveColor, center: false);
        if (!selected)
            return;

        foreach ((double gx, double gy) in new[]
                 {
                     (destination.X, destination.Y),
                     (destination.X + destination.Width, destination.Y),
                     (destination.X, destination.Y + destination.Height),
                     (destination.X + destination.Width, destination.Y + destination.Height),
                 })
        {
            AddGrip(gx, gy, Brushes.White, new SolidColorBrush(MoveColor));
        }

        var bar = new StackPanel { Orientation = Orientation.Horizontal, Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)) };
        Button back = SmallButton("Буцааж байранд нь");
        back.Click += (_, _) =>
        {
            if (selectedMove is int index)
            {
                moves[index].OffsetX = 0;
                moves[index].OffsetY = 0;
                Redraw();
            }
        };
        Button remove = SmallButton("Устгах");
        remove.Margin = new Thickness(6, 0, 0, 0);
        remove.Click += (_, _) =>
        {
            if (selectedMove is int index)
            {
                moves.RemoveAt(index);
                selectedMove = null;
                Redraw();
            }
        };
        bar.Children.Add(back);
        bar.Children.Add(remove);
        var border = new Border
        {
            Child = bar,
            BorderBrush = new SolidColorBrush(MoveColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 2, 4, 2),
        };
        Canvas.SetLeft(border, destination.X * Unit);
        Canvas.SetTop(border, Math.Max(0, destination.Y * Unit - 36));
        canvas.Children.Add(border);
    }

    private void AddColumnBar(FrontMatterBlockLayout block)
    {
        var bar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromArgb(235, 255, 255, 255)),
        };
        Button less = SmallButton("−");
        less.Click += (_, _) => ChangeColumns(-1);
        var count = new TextBlock
        {
            Text = $"{block.Columns} багана · зай {block.ColumnGapMm.ToString("0.#", CultureInfo.InvariantCulture)} мм",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 6, 0),
            Foreground = new SolidColorBrush(Color.FromRgb(30, 30, 30)),
            FontSize = 13,
        };
        Button more = SmallButton("+");
        more.Click += (_, _) => ChangeColumns(1);
        Button reset = SmallButton("Анхдагч");
        reset.Margin = new Thickness(8, 0, 0, 0);
        reset.Click += (_, _) => ResetSelected();
        bar.Children.Add(less);
        bar.Children.Add(count);
        bar.Children.Add(more);
        bar.Children.Add(reset);
        var border = new Border
        {
            Child = bar,
            BorderBrush = new SolidColorBrush(BlockColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(4, 2, 4, 2),
        };
        Canvas.SetLeft(border, block.X * Unit);
        Canvas.SetTop(border, Math.Max(0, block.Y * Unit - 36));
        canvas.Children.Add(border);
    }

    private static Button SmallButton(string text) => new()
    {
        Content = text,
        MinWidth = 28,
        Height = 26,
        FontSize = 14,
        Padding = new Thickness(6, 0, 6, 0),
        Cursor = Cursors.Hand,
    };

    private static IEnumerable<(FrontMatterGrip Grip, double X, double Y)> Grips(FrontMatterBlockLayout block)
    {
        double l = block.X, t = block.Y, r = block.X + block.Width, b = block.Y + block.Height;
        double cx = (l + r) / 2, cy = (t + b) / 2;
        yield return (FrontMatterGrip.NorthWest, l, t);
        yield return (FrontMatterGrip.North, cx, t);
        yield return (FrontMatterGrip.NorthEast, r, t);
        yield return (FrontMatterGrip.East, r, cy);
        yield return (FrontMatterGrip.SouthEast, r, b);
        yield return (FrontMatterGrip.South, cx, b);
        yield return (FrontMatterGrip.SouthWest, l, b);
        yield return (FrontMatterGrip.West, l, cy);
    }

    private void AddRect(PageRectMm rect, Brush? fill, Brush stroke, double thickness, double[]? dash)
    {
        var shape = new Rectangle
        {
            Width = Math.Max(1, rect.Width * Unit),
            Height = Math.Max(1, rect.Height * Unit),
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = thickness,
            IsHitTestVisible = false,
            RadiusX = 2,
            RadiusY = 2,
        };
        if (dash is not null)
            shape.StrokeDashArray = new DoubleCollection(dash);
        Canvas.SetLeft(shape, rect.X * Unit);
        Canvas.SetTop(shape, rect.Y * Unit);
        canvas.Children.Add(shape);
    }

    private void AddLine(double x1, double y1, double x2, double y2, Color color, double thickness, double[]? dash)
    {
        var line = new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = new SolidColorBrush(color),
            StrokeThickness = thickness,
            IsHitTestVisible = false,
        };
        if (dash is not null)
            line.StrokeDashArray = new DoubleCollection(dash);
        canvas.Children.Add(line);
    }

    private void AddGuide(double x1, double y1, double x2, double y2) =>
        AddLine(x1, y1, x2, y2, GuideColor, 1.4, [6, 4]);

    private void AddGrip(double xMm, double yMm, Brush fill, Brush stroke)
    {
        var dot = new Ellipse
        {
            Width = GripRadius * 2,
            Height = GripRadius * 2,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 2,
            IsHitTestVisible = false,
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 4,
                ShadowDepth = 1,
                Opacity = 0.35,
            },
        };
        Canvas.SetLeft(dot, xMm * Unit - GripRadius);
        Canvas.SetTop(dot, yMm * Unit - GripRadius);
        canvas.Children.Add(dot);
    }

    private void AddLabel(double xMm, double yMm, double widthMm, string text, Color color, bool center)
    {
        var label = new TextBlock
        {
            Text = text,
            Width = Math.Max(10, widthMm * Unit),
            Foreground = new SolidColorBrush(color),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(label, xMm * Unit);
        Canvas.SetTop(label, yMm * Unit);
        canvas.Children.Add(label);
    }

    private static string KindTitle(string kind) => kind switch
    {
        SheetFrontMatterKinds.DrawingList => "Зургийн жагсаалт",
        SheetFrontMatterKinds.ExplanatoryNote => "Тайлбар бичиг",
        SheetFrontMatterKinds.AlbumComposition => "Иж бүрдлийн жагсаалт",
        SheetFrontMatterKinds.TechnicalIndicators => "ТЭЗҮ",
        _ => kind,
    };

    // ---- interaction --------------------------------------------------------

    private void HandleMouseDown(object sender, MouseButtonEventArgs e)
    {
        Point at = e.GetPosition(canvas);
        double xMm = at.X / Unit, yMm = at.Y / Unit;
        double reach = (GripRadius + 3) / Unit;

        if (selectingRegion)
        {
            regionStart = at;
            regionCurrent = at;
            canvas.CaptureMouse();
            return;
        }

        FrontMatterBlockLayout? selected = Selected();
        if (selected is not null)
        {
            if (selected.Columns > 1)
            {
                double gapCenter = selected.X + selected.ColumnWidthMm + selected.ColumnGapMm / 2;
                if (Near(xMm, yMm, gapCenter, selected.Y + selected.Height / 2, reach))
                {
                    BeginDrag(selected, FrontMatterGrip.ColumnGap, at);
                    return;
                }
            }
            foreach ((FrontMatterGrip grip, double gx, double gy) in Grips(selected))
            {
                if (Near(xMm, yMm, gx, gy, reach))
                {
                    BeginDrag(selected, grip, at);
                    return;
                }
            }
        }

        FrontMatterBlockLayout? hit = blocks.LastOrDefault(block =>
            xMm >= block.X && xMm <= block.X + block.Width &&
            yMm >= block.Y && yMm <= block.Y + block.Height);
        selectedKind = hit?.Kind;
        selectedMove = null;
        if (hit is not null)
        {
            BeginDrag(hit, FrontMatterGrip.Move, at);
        }
        else
        {
            int moveIndex = moves.FindLastIndex(move =>
                xMm >= move.X + move.OffsetX && xMm <= move.X + move.OffsetX + move.Width &&
                yMm >= move.Y + move.OffsetY && yMm <= move.Y + move.OffsetY + move.Height);
            if (moveIndex >= 0)
            {
                selectedMove = moveIndex;
                moveStart = moves[moveIndex].Clone();
                dragOrigin = at;
                canvas.CaptureMouse();
            }
        }
        Redraw();
    }

    private void BeginDrag(FrontMatterBlockLayout block, FrontMatterGrip grip, Point at)
    {
        activeGrip = grip;
        dragOrigin = at;
        dragStart = block.Clone();
        canvas.CaptureMouse();
    }

    private void HandleMouseMove(object sender, MouseEventArgs e)
    {
        if (selectingRegion && regionStart is not null && e.LeftButton == MouseButtonState.Pressed)
        {
            regionCurrent = e.GetPosition(canvas);
            Redraw();
            return;
        }
        if (moveStart is not null && selectedMove is int moving && e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove(moving, e.GetPosition(canvas));
            return;
        }
        if (dragStart is null || e.LeftButton != MouseButtonState.Pressed)
        {
            UpdateCursor(e.GetPosition(canvas));
            return;
        }

        Point at = e.GetPosition(canvas);
        double dx = (at.X - dragOrigin.X) / Unit;
        double dy = (at.Y - dragOrigin.Y) / Unit;
        (List<double> xs, List<double> ys) = FrontMatterLayoutEditing.Guides(
            pageWidthMm,
            pageHeightMm,
            corner,
            innerFrame,
            blocks.Where(block => block.Kind != dragStart.Kind));
        bool freehand = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        FrontMatterDragResult result = FrontMatterLayoutEditing.Drag(
            dragStart,
            activeGrip,
            dx,
            dy,
            freehand ? [] : xs,
            freehand ? [] : ys);
        Replace(result.Block);
        guideX = result.GuideX;
        guideY = result.GuideY;
        Redraw();
    }

    private void HandleMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (selectingRegion && regionStart is Point a && regionCurrent is Point b)
        {
            selectingRegion = false;
            regionStart = null;
            regionCurrent = null;
            canvas.ReleaseMouseCapture();
            canvas.Cursor = Cursors.Arrow;
            var region = new SourceRegionMove
            {
                X = Math.Min(a.X, b.X) / Unit,
                Y = Math.Min(a.Y, b.Y) / Unit,
                Width = Math.Abs(b.X - a.X) / Unit,
                Height = Math.Abs(b.Y - a.Y) / Unit,
            };
            if (region.Width >= FrontMatterBlockLayout.MinSizeMm && region.Height >= FrontMatterBlockLayout.MinSizeMm)
            {
                moves.Add(region);
                selectedMove = moves.Count - 1;
                statusText.Text = "Сонгосон хэсгээ чирж шинэ байранд нь тавина. «Буцааж байранд нь», «Устгах» дээд самбарт.";
            }
            Redraw();
            return;
        }
        if (moveStart is not null)
        {
            moveStart = null;
            guideX = null;
            guideY = null;
            canvas.ReleaseMouseCapture();
            Redraw();
            return;
        }
        if (dragStart is null)
            return;
        dragStart = null;
        guideX = null;
        guideY = null;
        canvas.ReleaseMouseCapture();
        Redraw();
    }

    private void UpdateCursor(Point at)
    {
        FrontMatterBlockLayout? selected = Selected();
        double xMm = at.X / Unit, yMm = at.Y / Unit, reach = (GripRadius + 3) / Unit;
        Cursor cursor = Cursors.Arrow;
        if (selected is not null)
        {
            foreach ((FrontMatterGrip grip, double gx, double gy) in Grips(selected))
            {
                if (!Near(xMm, yMm, gx, gy, reach))
                    continue;
                cursor = grip switch
                {
                    FrontMatterGrip.North or FrontMatterGrip.South => Cursors.SizeNS,
                    FrontMatterGrip.East or FrontMatterGrip.West => Cursors.SizeWE,
                    FrontMatterGrip.NorthWest or FrontMatterGrip.SouthEast => Cursors.SizeNWSE,
                    _ => Cursors.SizeNESW,
                };
            }
            if (selected.Columns > 1 &&
                Near(xMm, yMm, selected.X + selected.ColumnWidthMm + selected.ColumnGapMm / 2, selected.Y + selected.Height / 2, reach))
            {
                cursor = Cursors.SizeWE;
            }
        }
        if (cursor == Cursors.Arrow && blocks.Any(block =>
                xMm >= block.X && xMm <= block.X + block.Width && yMm >= block.Y && yMm <= block.Y + block.Height))
        {
            cursor = Cursors.SizeAll;
        }
        canvas.Cursor = cursor;
    }

    /// <summary>A moved part follows the pointer; its new edges snap like a block's.</summary>
    private void DragMove(int index, Point at)
    {
        SourceRegionMove start = moveStart!;
        var asBlock = new FrontMatterBlockLayout
        {
            Kind = "source-move",
            X = start.X + start.OffsetX,
            Y = start.Y + start.OffsetY,
            Width = start.Width,
            Height = start.Height,
        };
        (List<double> xs, List<double> ys) = FrontMatterLayoutEditing.Guides(pageWidthMm, pageHeightMm, corner, innerFrame, blocks);
        bool freehand = Keyboard.Modifiers.HasFlag(ModifierKeys.Alt);
        FrontMatterDragResult result = FrontMatterLayoutEditing.Drag(
            asBlock,
            FrontMatterGrip.Move,
            (at.X - dragOrigin.X) / Unit,
            (at.Y - dragOrigin.Y) / Unit,
            freehand ? [] : xs,
            freehand ? [] : ys);
        moves[index].OffsetX = result.Block.X - start.X;
        moves[index].OffsetY = result.Block.Y - start.Y;
        guideX = result.GuideX;
        guideY = result.GuideY;
        Redraw();
    }

    private void HandleKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && selectingRegion)
        {
            selectingRegion = false;
            regionStart = null;
            regionCurrent = null;
            canvas.Cursor = Cursors.Arrow;
            Redraw();
            e.Handled = true;
            return;
        }
        if (selectedMove is int moving)
        {
            double moveStep = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1;
            (double mx, double my) = e.Key switch
            {
                Key.Left => (-moveStep, 0.0),
                Key.Right => (moveStep, 0.0),
                Key.Up => (0.0, -moveStep),
                Key.Down => (0.0, moveStep),
                _ => (0.0, 0.0),
            };
            if (mx != 0 || my != 0)
            {
                moves[moving].OffsetX += mx;
                moves[moving].OffsetY += my;
                Redraw();
                e.Handled = true;
            }
            return;
        }
        FrontMatterBlockLayout? selected = Selected();
        if (selected is null)
            return;
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 5 : 1;
        (double dx, double dy) = e.Key switch
        {
            Key.Left => (-step, 0.0),
            Key.Right => (step, 0.0),
            Key.Up => (0.0, -step),
            Key.Down => (0.0, step),
            _ => (0.0, 0.0),
        };
        if (dx == 0 && dy == 0)
            return;
        FrontMatterBlockLayout moved = selected.Clone();
        moved.X += dx;
        moved.Y += dy;
        Replace(moved);
        Redraw();
        e.Handled = true;
    }

    private void ChangeColumns(int delta)
    {
        FrontMatterBlockLayout? selected = Selected();
        if (selected is null)
            return;
        Replace(FrontMatterLayoutEditing.WithColumns(selected, selected.Columns + delta));
        Redraw();
    }

    private void ResetSelected()
    {
        FrontMatterBlockLayout? selected = Selected();
        FrontMatterBlockLayout? standard = defaults.FirstOrDefault(block => block.Kind == selected?.Kind);
        if (selected is null || standard is null)
            return;
        int index = blocks.FindIndex(block => block.Kind == selected.Kind);
        blocks[index] = standard.Clone();
        placedByOwner.Remove(selected.Kind);
        Redraw();
    }

    private FrontMatterBlockLayout? Selected() =>
        selectedKind is null ? null : blocks.FirstOrDefault(block => block.Kind == selectedKind);

    private void Replace(FrontMatterBlockLayout block)
    {
        int index = blocks.FindIndex(item => item.Kind == block.Kind);
        if (index < 0)
            return;
        blocks[index] = block;
        placedByOwner.Add(block.Kind);
    }

    private static bool Near(double x, double y, double tx, double ty, double reach) =>
        Math.Abs(x - tx) <= reach && Math.Abs(y - ty) <= reach;
}
