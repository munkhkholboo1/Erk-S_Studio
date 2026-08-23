using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

/// <summary>
/// Самбар: the project composed rather than listed. Many pieces of its own
/// material on one large sheet, where a grid puts them - which is what a
/// competition submission is, and what a page-per-item portfolio cannot say.
///
/// The portfolio remains the pool this draws from. An item there records what
/// arrived and keeps its link to the source that sent it; a card here decides
/// what appears where. That is why one render can be on three boards, and why
/// nothing about anyone's existing portfolio had to change.
/// </summary>
internal sealed partial class ShellView
{
    private readonly ListBox boardList = new() { MinWidth = 190, MaxHeight = 190 };
    private readonly BoardCanvasSurface boardCanvas = new() { MinHeight = 420 };
    private readonly TextBox boardWidthBox = new() { Width = 80 };
    private readonly TextBox boardHeightBox = new() { Width = 80 };
    private readonly TextBox boardColumnsBox = new() { Width = 56 };
    private readonly TextBox boardRowsBox = new() { Width = 56 };
    private readonly TextBox boardGutterBox = new() { Width = 56 };
    private readonly TextBox boardMarginBox = new() { Width = 56 };
    private readonly ComboBox boardAssetBox = new() { MinWidth = 260 };
    private readonly ComboBox boardCardLayoutBox = new() { MinWidth = 180 };
    private readonly TextBox boardCardCaptionBox = new();
    private readonly ComboBox boardDpiBox = new() { Width = 90 };
    private readonly TextBlock boardCardSizeText = new()
    {
        Foreground = StudioTheme.TextBrush,
        FontSize = 15,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock boardSummary = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        FontSize = StudioTheme.HintFontSize,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly Button boardAddButton = StudioWidgets.CreateGlyphTextButton(
        "", "Самбар нэмэх", "Цувралд шинэ самбар нэмэх");
    private readonly Button boardRemoveButton = StudioWidgets.CreateGlyphTextButton(
        "", "Самбар устгах", "Сонгосон самбарыг цувралаас хасах");
    private readonly Button boardAddCardButton = StudioWidgets.CreateGlyphTextButton(
        "", "Карт нэмэх", "Самбар дээр шинэ байрлуулагч карт нэмэх");
    private readonly Button boardRemoveCardButton = StudioWidgets.CreateGlyphTextButton(
        "", "Карт устгах", "Сонгосон картыг самбараас хасах");
    private readonly Button boardCopySizeButton = StudioWidgets.CreateButton("Хэмжээг хуулах");
    private readonly Button boardZoomFitButton = StudioWidgets.CreateButton("Багтаах");
    private readonly Button boardBuildButton = StudioWidgets.CreatePrimaryButton("PDF үүсгэх");
    private readonly Button boardOpenButton = StudioWidgets.CreateButton("PDF нээх");
    private bool boardInspectorSuspended;

    private ProjectBoardSeries Boards => state.Project.Boards;

    private ProjectBoard? SelectedBoard => boardList.SelectedItem is BoardRow row
        ? Boards.Boards.FirstOrDefault(item => item.Id == row.Id)
        : null;

    private UIElement BuildBoardsPage()
    {
        boardCardLayoutBox.ItemsSource = new[]
        {
            new PortfolioLayoutChoice(ProjectPortfolioLayouts.FitPage, "Картад бүтнээр"),
            new PortfolioLayoutChoice(ProjectPortfolioLayouts.Contain, "Захтай, бүтнээр"),
            new PortfolioLayoutChoice(ProjectPortfolioLayouts.FullBleed, "Карт дүүрэн (тайрна)"),
        };
        boardCardLayoutBox.DisplayMemberPath = nameof(PortfolioLayoutChoice.Label);
        boardDpiBox.ItemsSource = new[] { 150, 200, 300, 600 };
        boardDpiBox.SelectedItem = 300;
        boardDpiBox.SelectionChanged += (_, _) => RefreshBoardInspector();

        boardCanvas.DescribeCard = DescribeBoardCard;
        boardCanvas.SelectionChanged += (_, _) => RefreshBoardInspector();
        boardCanvas.CardChanged += (_, _) =>
        {
            RefreshBoardInspector();
            state.SaveProject();
        };

        boardList.SelectionChanged += (_, _) => ShowSelectedBoard();
        boardAddButton.Click += (_, _) => AddBoard();
        boardRemoveButton.Click += (_, _) => RemoveBoard();
        boardAddCardButton.Click += (_, _) => AddBoardCard();
        boardRemoveCardButton.Click += (_, _) => RemoveBoardCard();
        boardCopySizeButton.Click += (_, _) => CopyBoardCardSize();
        boardZoomFitButton.Click += (_, _) => boardCanvas.ZoomToFit();
        boardBuildButton.Click += (_, _) => BuildBoardPdf();
        boardOpenButton.Click += (_, _) => OpenBoardPdf();
        boardAssetBox.SelectionChanged += (_, _) => ApplyBoardInspector();
        boardCardLayoutBox.SelectionChanged += (_, _) => ApplyBoardInspector();
        boardCardCaptionBox.LostFocus += (_, _) => ApplyBoardInspector();
        boardWidthBox.LostFocus += (_, _) => ApplyBoardSetup();
        boardHeightBox.LostFocus += (_, _) => ApplyBoardSetup();
        boardColumnsBox.LostFocus += (_, _) => ApplyBoardSetup();
        boardRowsBox.LostFocus += (_, _) => ApplyBoardSetup();
        boardGutterBox.LostFocus += (_, _) => ApplyBoardSetup();
        boardMarginBox.LostFocus += (_, _) => ApplyBoardSetup();

        var page = new Grid { Margin = new Thickness(18) };
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        page.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new StackPanel();
        header.Children.Add(StudioWidgets.CreateTitle("Самбар"));
        header.Children.Add(StudioWidgets.CreateHint(
            "Уралдааны график самбар. Портфолиод ирсэн материалаас карт байрлуулж, " +
            "нэг том хуудсан дээр зохион байгуулна. Карт бүр торонд наалддаг — " +
            "самбарын цэвэрхэн байдал тэгшилгээнээс гардаг."));
        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
        actions.Children.Add(boardAddButton);
        actions.Children.Add(boardRemoveButton);
        actions.Children.Add(boardAddCardButton);
        actions.Children.Add(boardRemoveCardButton);
        actions.Children.Add(boardZoomFitButton);
        actions.Children.Add(boardBuildButton);
        actions.Children.Add(boardOpenButton);
        header.Children.Add(actions);
        Grid.SetColumnSpan(header, 2);
        page.Children.Add(header);

        var canvasHost = new Grid { Margin = new Thickness(0, 8, 12, 0) };
        canvasHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        canvasHost.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var boardPicker = new StackPanel { Orientation = Orientation.Horizontal };
        boardPicker.Children.Add(new TextBlock
        {
            Text = "Самбар:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 6),
            Foreground = StudioTheme.MutedTextBrush,
        });
        boardList.MaxHeight = 34;
        boardPicker.Children.Add(boardList);
        canvasHost.Children.Add(boardPicker);
        Grid.SetRow(boardCanvas, 1);
        canvasHost.Children.Add(boardCanvas);
        Grid.SetRow(canvasHost, 1);
        page.Children.Add(canvasHost);

        var inspector = new StackPanel { Width = 340 };
        inspector.Children.Add(StudioWidgets.CreateSectionHeader("Сонгосон карт"));
        inspector.Children.Add(boardCardSizeText);
        inspector.Children.Add(boardCopySizeButton);
        inspector.Children.Add(StudioWidgets.CreateHint(
            "Энэ хэмжээг PFA эсвэл PFR дээрээ тохируулж эх бэлдээрэй — " +
            "картын харьцаанд тохирсон эх хамгийн цэвэр гарна."));
        inspector.Children.Add(StudioWidgets.CreateFormRow("Нягтрал", boardDpiBox, 90));
        inspector.Children.Add(StudioWidgets.CreateFormRow("Агуулга", boardAssetBox, 90));
        inspector.Children.Add(StudioWidgets.CreateFormRow("Байрлал", boardCardLayoutBox, 90));
        inspector.Children.Add(StudioWidgets.CreateFormRow("Тайлбар", boardCardCaptionBox, 90));

        inspector.Children.Add(StudioWidgets.CreateSectionHeader("Цуврал"));
        var sizeRow = new WrapPanel();
        sizeRow.Children.Add(boardWidthBox);
        sizeRow.Children.Add(new TextBlock
        {
            Text = " × ",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StudioTheme.MutedTextBrush,
        });
        sizeRow.Children.Add(boardHeightBox);
        sizeRow.Children.Add(new TextBlock
        {
            Text = " мм",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StudioTheme.MutedTextBrush,
        });
        inspector.Children.Add(StudioWidgets.CreateFormRow("Хэмжээ", sizeRow, 90));
        var gridRow = new WrapPanel();
        gridRow.Children.Add(boardColumnsBox);
        gridRow.Children.Add(new TextBlock
        {
            Text = " × ",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StudioTheme.MutedTextBrush,
        });
        gridRow.Children.Add(boardRowsBox);
        inspector.Children.Add(StudioWidgets.CreateFormRow("Тор", gridRow, 90));
        inspector.Children.Add(StudioWidgets.CreateFormRow("Зай", boardGutterBox, 90));
        inspector.Children.Add(StudioWidgets.CreateFormRow("Зах", boardMarginBox, 90));
        inspector.Children.Add(StudioWidgets.CreateHint(
            "Тор, хэмжээ, зах нь цувралын шинж — бүх самбарт нэгэн адил үйлчилнэ. " +
            "Уралдааны самбарууд ижил харагдах ёстой."));
        inspector.Children.Add(boardSummary);
        Grid.SetRow(inspector, 1);
        Grid.SetColumn(inspector, 1);
        page.Children.Add(StudioWidgets.CreateScrollHost(inspector));
        UIElement inspectorHost = page.Children[^1];
        Grid.SetRow(inspectorHost, 1);
        Grid.SetColumn(inspectorHost, 1);

        return page;
    }

    private void RefreshBoards()
    {
        Boards.Normalize();
        if (Boards.Boards.Count == 0)
            Boards.Boards.Add(NewBoard());

        string? selectedId = (boardList.SelectedItem as BoardRow)?.Id;
        boardList.ItemsSource = Boards.OrderedBoards()
            .Select(item => new BoardRow(item.Id, DescribeBoard(item)))
            .ToList();
        boardList.DisplayMemberPath = nameof(BoardRow.Label);
        boardList.SelectedItem = (boardList.ItemsSource as IEnumerable<BoardRow>)?
            .FirstOrDefault(row => row.Id == selectedId)
            ?? (boardList.ItemsSource as IEnumerable<BoardRow>)?.FirstOrDefault();

        boardInspectorSuspended = true;
        boardWidthBox.Text = Boards.BoardWidthMm.ToString("0.#");
        boardHeightBox.Text = Boards.BoardHeightMm.ToString("0.#");
        boardColumnsBox.Text = Boards.Grid.Columns.ToString();
        boardRowsBox.Text = Boards.Grid.Rows.ToString();
        boardGutterBox.Text = Boards.Grid.ColumnGutterMm.ToString("0.#");
        boardMarginBox.Text = Boards.Grid.MarginLeftMm.ToString("0.#");
        boardInspectorSuspended = false;

        ShowSelectedBoard();
    }

    private void ShowSelectedBoard()
    {
        boardCanvas.Show(Boards, SelectedBoard);
        RefreshBoardInspector();
    }

    private void RefreshBoardInspector()
    {
        boardInspectorSuspended = true;
        BoardElement? card = boardCanvas.Selected;
        bool hasCard = card is not null;
        boardRemoveCardButton.IsEnabled = hasCard;
        boardCopySizeButton.IsEnabled = hasCard;
        boardAssetBox.IsEnabled = hasCard;
        boardCardLayoutBox.IsEnabled = hasCard;
        boardCardCaptionBox.IsEnabled = hasCard;

        boardAssetBox.ItemsSource = BoardAssetChoices();
        boardAssetBox.DisplayMemberPath = nameof(BoardAssetChoice.Label);

        if (card is null)
        {
            boardCardSizeText.Text = "Карт сонгоогүй байна.";
            boardCardCaptionBox.Text = "";
            boardAssetBox.SelectedItem = null;
            boardInspectorSuspended = false;
            RefreshBoardSummary();
            return;
        }

        boardCardSizeText.Text = DescribeCardSize(card);
        boardCardCaptionBox.Text = card.Caption;
        boardAssetBox.SelectedItem = (boardAssetBox.ItemsSource as IEnumerable<BoardAssetChoice>)?
            .FirstOrDefault(choice => choice.ItemId == card.AssetItemId);
        boardCardLayoutBox.SelectedItem = (boardCardLayoutBox.ItemsSource as IEnumerable<PortfolioLayoutChoice>)?
            .FirstOrDefault(choice => choice.Value == card.Layout);
        boardInspectorSuspended = false;
        RefreshBoardSummary();
    }

    /// <summary>
    /// What the card is, in millimetres and in pixels.
    ///
    /// This is the whole of the arrangement with the drawing programs. Studio
    /// does not send them a task; it says plainly how big the card is and what
    /// that needs, and the artwork is prepared to match by hand.
    /// </summary>
    private string DescribeCardSize(BoardElement card)
    {
        int dpi = boardDpiBox.SelectedItem is int chosen ? chosen : BoardCardMeasurements.PrintDpi;
        if (BoardCardMeasurements.Measure(Boards.Resolve(card), dpi) is not { } measured)
            return "Карт торонд багтсангүй.";

        return
            measured.WidthMm.ToString("0.#") + " x " + measured.HeightMm.ToString("0.#") + " мм" +
            Environment.NewLine +
            "харьцаа " + measured.AspectRatio.ToString("0.###") + Environment.NewLine +
            measured.Dpi + " dpi-д " + measured.WidthPixels + " x " + measured.HeightPixels + " пиксел";
    }

    private string DescribeBoardCard(BoardElement card)
    {
        if (!string.IsNullOrWhiteSpace(card.Caption))
            return card.Caption;
        ProjectPortfolioItem? asset = FindBoardAsset(card);
        return asset is null ? "Хоосон карт" : asset.Title;
    }

    private ProjectPortfolioItem? FindBoardAsset(BoardElement card) =>
        string.IsNullOrWhiteSpace(card.AssetItemId)
            ? null
            : Portfolio.Items.FirstOrDefault(item => item.Id == card.AssetItemId);

    private List<BoardAssetChoice> BoardAssetChoices()
    {
        var choices = new List<BoardAssetChoice> { new("", "— хоосон байрлуулагч —") };
        choices.AddRange(Portfolio.OrderedVisibleItems()
            .Select(item => new BoardAssetChoice(
                item.Id,
                string.IsNullOrWhiteSpace(item.Title) ? item.RelativePath : item.Title)));
        return choices;
    }

    private void RefreshBoardSummary()
    {
        ProjectBoard? board = SelectedBoard;
        int cards = board?.Elements.Count ?? 0;
        int placeholders = board?.Elements.Count(element => element.IsPlaceholder) ?? 0;
        boardSummary.Text =
            $"Цувралд {Boards.Boards.Count} самбар. Энэ самбарт {cards} карт" +
            (placeholders > 0 ? $", тэдгээрийн {placeholders} нь хоосон байрлуулагч." : ".") +
            (Portfolio.OrderedVisibleItems().Count == 0
                ? " Портфолиод материал алга — эх үүсвэрээс хуудас ирэхэд картад холбогдоно."
                : "");
    }

    private void ApplyBoardSetup()
    {
        if (boardInspectorSuspended)
            return;
        if (double.TryParse(boardWidthBox.Text, out double width) && width > 0)
            Boards.BoardWidthMm = width;
        if (double.TryParse(boardHeightBox.Text, out double height) && height > 0)
            Boards.BoardHeightMm = height;
        if (int.TryParse(boardColumnsBox.Text, out int columns) && columns > 0)
            Boards.Grid.Columns = columns;
        if (int.TryParse(boardRowsBox.Text, out int rows) && rows > 0)
            Boards.Grid.Rows = rows;
        if (double.TryParse(boardGutterBox.Text, out double gutter) && gutter >= 0)
        {
            Boards.Grid.ColumnGutterMm = gutter;
            Boards.Grid.RowGutterMm = gutter;
        }
        if (double.TryParse(boardMarginBox.Text, out double margin) && margin >= 0)
        {
            Boards.Grid.MarginLeftMm = margin;
            Boards.Grid.MarginTopMm = margin;
            Boards.Grid.MarginRightMm = margin;
            Boards.Grid.MarginBottomMm = margin;
        }

        Boards.Normalize();
        HoldCardsInsideTheGrid();
        state.SaveProject();
        RefreshBoards();
    }

    /// <summary>
    /// A grid made smaller can leave a card reaching past its last column. The
    /// card is pulled back rather than left in a place the board cannot draw,
    /// because the writer would then refuse it and the user would see a card
    /// vanish from the printed sheet without being told why.
    /// </summary>
    private void HoldCardsInsideTheGrid()
    {
        foreach (ProjectBoard board in Boards.Boards)
            BoardGridFitting.HoldInside(Boards.Grid, board.Elements);
    }

    private void ApplyBoardInspector()
    {
        if (boardInspectorSuspended || boardCanvas.Selected is not { } card)
            return;

        if (boardAssetBox.SelectedItem is BoardAssetChoice asset)
            card.AssetItemId = asset.ItemId;
        if (boardCardLayoutBox.SelectedItem is PortfolioLayoutChoice layout)
            card.Layout = layout.Value;
        card.Caption = boardCardCaptionBox.Text.Trim();
        card.Normalize();
        state.SaveProject();
        boardCanvas.Redraw();
        RefreshBoardSummary();
    }

    private void AddBoard()
    {
        Boards.Boards.Add(NewBoard());
        Boards.Normalize();
        state.SaveProject();
        RefreshBoards();
        boardList.SelectedIndex = boardList.Items.Count - 1;
    }

    private ProjectBoard NewBoard() => new()
    {
        Code = "A" + (Boards.Boards.Count + 1),
        Order = Boards.Boards.Count + 1,
    };

    private void RemoveBoard()
    {
        if (SelectedBoard is not { } board)
            return;
        if (MessageBox.Show(
                $"«{DescribeBoard(board)}» самбарыг устгах уу?",
                "Самбар устгах",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        Boards.Boards.Remove(board);
        Boards.Normalize();
        state.SaveProject();
        RefreshBoards();
    }

    private void AddBoardCard()
    {
        if (SelectedBoard is not { } board)
            return;

        // Placed at the first row, a third of the grid wide: a starting shape
        // to drag from rather than a guess at what the card is for.
        var element = new BoardElement
        {
            Column = 0,
            Row = 0,
            ColumnSpan = Math.Max(1, Boards.Grid.Columns / 3),
            RowSpan = Math.Max(1, Boards.Grid.Rows / 3),
            ZOrder = board.Elements.Count,
        };
        element.Normalize();
        board.Elements.Add(element);
        state.SaveProject();
        boardCanvas.Show(Boards, board);
        boardCanvas.Select(element);
    }

    private void RemoveBoardCard()
    {
        if (SelectedBoard is not { } board || boardCanvas.Selected is not { } card)
            return;
        board.Elements.Remove(card);
        state.SaveProject();
        boardCanvas.Select(null);
        boardCanvas.Show(Boards, board);
        RefreshBoardInspector();
    }

    private void CopyBoardCardSize()
    {
        if (boardCanvas.Selected is not { } card)
            return;
        try
        {
            Clipboard.SetText(DescribeCardSize(card).Replace("\n", "  ·  "));
            SetStatus("Картын хэмжээ хуулагдлаа.");
        }
        catch (Exception exception)
        {
            SetStatus("Хуулж чадсангүй: " + exception.Message);
        }
    }

    private void BuildBoardPdf()
    {
        if (!state.HasOpenProject)
            return;
        Boards.Normalize();
        var boards = new List<BoardBuildBoard>();
        foreach (ProjectBoard board in Boards.OrderedBoards())
        {
            boards.Add(new BoardBuildBoard(
                board.Code,
                board.Title,
                board.OrderedVisibleElements().Select(ToBuildCard).ToList()));
        }

        string outputPath = Path.Combine(
            Path.GetDirectoryName(state.ProjectPath) ?? Directory.GetCurrentDirectory(),
            "sambar.pdf");
        try
        {
            BoardBuildResult result = BoardPdfWriter.Build(new BoardBuildRequest(
                string.IsNullOrWhiteSpace(Boards.Title) ? "Самбар" : Boards.Title,
                outputPath,
                Boards.BoardWidthMm,
                Boards.BoardHeightMm,
                Boards.Grid,
                boards));
            Boards.LastPdfPath = result.OutputPath;
            Boards.LastBuiltAtUtc = DateTimeOffset.UtcNow;
            state.SaveProject();
            SetStatus(result.Warnings.Count == 0
                ? $"Самбар үүслээ: {result.PageCount} хуудас."
                : $"Самбар үүслээ ({result.PageCount} хуудас), " +
                  $"{result.Warnings.Count} анхааруулгатай: {result.Warnings[0]}");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus("Самбар үүсгэсэнгүй: " + exception.Message);
        }
    }

    private BoardBuildCard ToBuildCard(BoardElement element)
    {
        ProjectPortfolioItem? asset = FindBoardAsset(element);
        return new BoardBuildCard(
            element.Layout,
            element.Caption,
            asset is null
                ? ""
                : ProjectWorkspacePaths.ResolveInsideProject(state.ProjectPath, asset.RelativePath),
            asset?.SourcePageNumber ?? 1,
            element.Column,
            element.ColumnSpan,
            element.Row,
            element.RowSpan,
            element.CropX,
            element.CropY,
            element.CropWidth,
            element.CropHeight,
            element.FocalPointX,
            element.FocalPointY);
    }

    private void OpenBoardPdf()
    {
        string path = Boards.LastPdfPath;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            SetStatus("Самбарын PDF хараахан үүсээгүй байна.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception)
        {
            SetStatus("Нээж чадсангүй: " + exception.Message);
        }
    }

    private static string DescribeBoard(ProjectBoard board) =>
        string.IsNullOrWhiteSpace(board.Title)
            ? (string.IsNullOrWhiteSpace(board.Code) ? "Самбар" : board.Code)
            : $"{board.Code} {board.Title}".Trim();

    private sealed record BoardRow(string Id, string Label);

    private sealed record BoardAssetChoice(string ItemId, string Label);
}
