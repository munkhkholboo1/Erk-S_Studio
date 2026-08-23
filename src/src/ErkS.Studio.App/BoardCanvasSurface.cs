using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// The board itself, at a size worth looking at, with the cards where the grid
/// puts them.
///
/// A board is composed rather than listed, so this is the working surface
/// rather than a preview of one. Cards are dragged and stretched here, and
/// everything they do lands on the grid: a competition board's tidiness is
/// almost entirely alignment, and forty hand-placed elements never align.
///
/// The grid is drawn faintly and always. It is the thing the layout is being
/// made against, and hiding it would leave the person composing guessing at
/// where the next card can go.
/// </summary>
internal sealed class BoardCanvasSurface : Grid
{
    private const double MinimumZoom = 0.05;
    private const double MaximumZoom = 2.0;
    private const double HandleSize = 12;

    private static readonly SolidColorBrush PaperBrush = Frozen(Color.FromRgb(250, 250, 248));
    private static readonly SolidColorBrush GridBrush = Frozen(Color.FromArgb(52, 90, 100, 120));
    private static readonly SolidColorBrush MarginBrush = Frozen(Color.FromArgb(110, 90, 100, 120));
    private static readonly SolidColorBrush CardBrush = Frozen(Color.FromArgb(30, 60, 90, 140));
    private static readonly SolidColorBrush CardEdgeBrush = Frozen(Color.FromRgb(120, 132, 150));
    private static readonly SolidColorBrush PlaceholderEdgeBrush = Frozen(Color.FromRgb(168, 174, 184));
    private static readonly SolidColorBrush SelectedEdgeBrush = Frozen(Color.FromRgb(64, 132, 214));
    private static readonly SolidColorBrush SelectedFillBrush = Frozen(Color.FromArgb(46, 64, 132, 214));
    private static readonly SolidColorBrush LabelBrush = Frozen(Color.FromRgb(70, 78, 92));

    private readonly ScrollViewer scroll = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        Background = new SolidColorBrush(Color.FromRgb(48, 52, 58)),
        Padding = new Thickness(20),
    };
    private readonly Canvas surface = new()
    {
        Background = Brushes.Transparent,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private ProjectBoardSeries series = new();
    private ProjectBoard? board;
    private double zoom = 0.25;

    private BoardElement? dragged;
    private bool draggingSize;
    private Point dragOrigin;
    private BoardGridSpan dragStartSpan;

    public BoardCanvasSurface()
    {
        scroll.Content = surface;
        Children.Add(scroll);
        surface.MouseLeftButtonDown += OnSurfaceDown;
        surface.MouseMove += OnSurfaceMove;
        surface.MouseLeftButtonUp += OnSurfaceUp;
        surface.MouseLeave += (_, _) => EndDrag();
    }

    /// <summary>The card the inspector is showing, or null.</summary>
    public BoardElement? Selected { get; private set; }

    public event EventHandler? SelectionChanged;

    /// <summary>A card was moved or stretched, so the project has changed.</summary>
    public event EventHandler? CardChanged;

    /// <summary>How a card names itself on the surface. Supplied by the shell.</summary>
    public Func<BoardElement, string>? DescribeCard { get; set; }

    public double Zoom
    {
        get => zoom;
        set
        {
            double clamped = Math.Clamp(value, MinimumZoom, MaximumZoom);
            if (Math.Abs(clamped - zoom) < 0.0001)
                return;
            zoom = clamped;
            Redraw();
        }
    }

    public void Show(ProjectBoardSeries showSeries, ProjectBoard? showBoard)
    {
        series = showSeries ?? new ProjectBoardSeries();
        board = showBoard;
        if (Selected is not null && board?.Elements.Contains(Selected) != true)
            Select(null);
        Redraw();
    }

    public void Select(BoardElement? element)
    {
        if (ReferenceEquals(Selected, element))
            return;
        Selected = element;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        Redraw();
    }

    /// <summary>The zoom that shows the whole board in the space available.</summary>
    public void ZoomToFit()
    {
        double available = Math.Min(
            scroll.ViewportWidth - 48,
            scroll.ViewportHeight * (series.BoardWidthMm / Math.Max(1, series.BoardHeightMm)) - 48);
        if (available <= 0 || !double.IsFinite(available))
            return;
        Zoom = available / series.BoardWidthMm;
    }

    public void Redraw()
    {
        surface.Children.Clear();
        double width = series.BoardWidthMm * zoom;
        double height = series.BoardHeightMm * zoom;
        surface.Width = Math.Max(1, width);
        surface.Height = Math.Max(1, height);

        surface.Children.Add(new Rectangle
        {
            Width = surface.Width,
            Height = surface.Height,
            Fill = PaperBrush,
            Stroke = CardEdgeBrush,
            StrokeThickness = 1,
        });

        DrawGrid();
        if (board is null)
            return;
        foreach (BoardElement element in board.OrderedVisibleElements())
            DrawCard(element);
    }

    private void DrawGrid()
    {
        BoardGrid grid = series.Grid;
        if (BoardGridGeometry.Content(grid, series.BoardWidthMm, series.BoardHeightMm)
            is not { } content)
        {
            return;
        }

        surface.Children.Add(Place(new Rectangle
        {
            Width = Math.Max(1, content.WidthMm * zoom),
            Height = Math.Max(1, content.HeightMm * zoom),
            Stroke = MarginBrush,
            StrokeThickness = 1,
            StrokeDashArray = [4, 4],
        }, content.LeftMm, content.TopMm));

        for (int column = 0; column < grid.Columns; column++)
        {
            for (int row = 0; row < grid.Rows; row++)
            {
                if (BoardGridGeometry.Resolve(
                        grid,
                        series.BoardWidthMm,
                        series.BoardHeightMm,
                        new BoardGridSpan(column, 1, row, 1)) is not { } cell)
                {
                    continue;
                }

                surface.Children.Add(Place(new Rectangle
                {
                    Width = Math.Max(1, cell.WidthMm * zoom),
                    Height = Math.Max(1, cell.HeightMm * zoom),
                    Stroke = GridBrush,
                    StrokeThickness = 0.7,
                    IsHitTestVisible = false,
                }, cell.LeftMm, cell.TopMm));
            }
        }
    }

    private void DrawCard(BoardElement element)
    {
        if (series.Resolve(element) is not { } rect)
            return;

        bool selected = ReferenceEquals(element, Selected);
        var body = new Rectangle
        {
            Width = Math.Max(1, rect.WidthMm * zoom),
            Height = Math.Max(1, rect.HeightMm * zoom),
            Fill = selected ? SelectedFillBrush : CardBrush,
            Stroke = selected
                ? SelectedEdgeBrush
                : element.IsPlaceholder ? PlaceholderEdgeBrush : CardEdgeBrush,
            StrokeThickness = selected ? 2 : 1,
            // A card with nothing in it yet is a state of the layout, so it is
            // drawn as an outline rather than as a fault.
            StrokeDashArray = element.IsPlaceholder ? [5, 4] : null,
            Tag = element,
            Cursor = Cursors.SizeAll,
        };
        surface.Children.Add(Place(body, rect.LeftMm, rect.TopMm));

        string label = DescribeCard?.Invoke(element) ?? "";
        if (label.Length > 0 && rect.WidthMm * zoom > 40)
        {
            surface.Children.Add(Place(new TextBlock
            {
                Text = label,
                Foreground = LabelBrush,
                FontSize = 11,
                MaxWidth = Math.Max(20, rect.WidthMm * zoom - 10),
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false,
            }, rect.LeftMm + 3 / zoom, rect.TopMm + 3 / zoom));
        }

        if (!selected)
            return;

        // The corner that stretches the card. Placed inside the edge so it is
        // reachable even when the card sits against the board's own margin.
        surface.Children.Add(Place(
            new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = SelectedEdgeBrush,
                Tag = new ResizeHandle(element),
                Cursor = Cursors.SizeNWSE,
            },
            rect.RightMm - HandleSize / zoom,
            rect.BottomMm - HandleSize / zoom));
    }

    private UIElement Place(UIElement element, double leftMm, double topMm)
    {
        Canvas.SetLeft(element, leftMm * zoom);
        Canvas.SetTop(element, topMm * zoom);
        return element;
    }

    private void OnSurfaceDown(object sender, MouseButtonEventArgs e)
    {
        Point point = e.GetPosition(surface);
        object? tag = (e.OriginalSource as FrameworkElement)?.Tag;
        switch (tag)
        {
            case ResizeHandle handle:
                Select(handle.Element);
                BeginDrag(handle.Element, point, sizing: true);
                break;
            case BoardElement element:
                Select(element);
                BeginDrag(element, point, sizing: false);
                break;
            default:
                Select(null);
                break;
        }
        surface.CaptureMouse();
        e.Handled = true;
    }

    private void BeginDrag(BoardElement element, Point point, bool sizing)
    {
        if (element.IsLocked)
            return;
        dragged = element;
        draggingSize = sizing;
        dragOrigin = point;
        dragStartSpan = element.Span;
    }

    private void OnSurfaceMove(object sender, MouseEventArgs e)
    {
        if (dragged is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        Point point = e.GetPosition(surface);
        if (BoardGridGeometry.CellAt(
                series.Grid,
                series.BoardWidthMm,
                series.BoardHeightMm,
                point.X / zoom,
                point.Y / zoom) is not { } cell)
        {
            return;
        }

        if (draggingSize)
        {
            // Stretched from the top-left corner it was already anchored at.
            int columnSpan = Math.Max(1, cell.Column - dragged.Column + 1);
            int rowSpan = Math.Max(1, cell.Row - dragged.Row + 1);
            if (columnSpan == dragged.ColumnSpan && rowSpan == dragged.RowSpan)
                return;
            dragged.ColumnSpan = Math.Min(columnSpan, series.Grid.Columns - dragged.Column);
            dragged.RowSpan = Math.Min(rowSpan, series.Grid.Rows - dragged.Row);
        }
        else
        {
            if (BoardGridGeometry.CellAt(
                    series.Grid,
                    series.BoardWidthMm,
                    series.BoardHeightMm,
                    dragOrigin.X / zoom,
                    dragOrigin.Y / zoom) is not { } from)
            {
                return;
            }

            int column = dragStartSpan.Column + (cell.Column - from.Column);
            int row = dragStartSpan.Row + (cell.Row - from.Row);
            // Held inside the grid: a card dragged off the edge stops at it
            // rather than being placed where it cannot be drawn.
            column = Math.Clamp(column, 0, series.Grid.Columns - dragged.ColumnSpan);
            row = Math.Clamp(row, 0, series.Grid.Rows - dragged.RowSpan);
            if (column == dragged.Column && row == dragged.Row)
                return;
            dragged.Column = column;
            dragged.Row = row;
        }

        Redraw();
    }

    private void OnSurfaceUp(object sender, MouseButtonEventArgs e)
    {
        bool moved = dragged is not null && !dragged.Span.Equals(dragStartSpan);
        EndDrag();
        surface.ReleaseMouseCapture();
        if (moved)
            CardChanged?.Invoke(this, EventArgs.Empty);
    }

    private void EndDrag()
    {
        dragged = null;
        draggingSize = false;
    }

    private static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private sealed record ResizeHandle(BoardElement Element);
}
