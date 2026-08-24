using ErkS.Platform.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Pdf;

/// <summary>
/// One card on a board: where it sits, what it shows, and how much of it.
/// The source path is already resolved, so the writer never has to know where
/// a project keeps its files.
/// </summary>
public sealed record BoardBuildCard(
    string Layout,
    string Caption,
    string SourcePath,
    int SourcePageNumber,
    int Column,
    int ColumnSpan,
    int Row,
    int RowSpan,
    double CropX = 0,
    double CropY = 0,
    double CropWidth = 1,
    double CropHeight = 1,
    double FocalPointX = 0.5,
    double FocalPointY = 0.5,
    /// <summary>
    /// A CityGen board export to draw from its classification, instead of a
    /// file to place. This is the general plan arriving as what it means rather
    /// than as a picture of itself.
    /// </summary>
    string PlanPath = "",
    string Id = "",
    /// <summary><see cref="BoardBuildCardKinds"/>.</summary>
    string Kind = BoardBuildCardKinds.Content,
    /// <summary>
    /// For an annotation, the plan card it describes. A legend, an arrow and a
    /// scale bar are all statements about one particular drawing, and saying
    /// which one is what keeps them from drifting away from it.
    /// </summary>
    string PlanCardId = "",
    /// <summary>
    /// The user has looked at a value the source only assumed and agreed it.
    /// Without this an assumed north or an inferred unit is not drawn at all.
    /// </summary>
    bool IsConfirmed = false,
    /// <summary>
    /// An exact size in millimetres, overriding the grid's. Zero follows the
    /// grid. The grid still places the card's corner; only its reach changes.
    /// </summary>
    double WidthMm = 0,
    double HeightMm = 0);

public static class BoardBuildCardKinds
{
    /// <summary>Material placed on the board: a page, a render, a plan.</summary>
    public const string Content = "Content";

    /// <summary>What the plan's surfaces mean, taken from the plan itself.</summary>
    public const string Legend = "Legend";

    public const string NorthArrow = "NorthArrow";

    public const string ScaleBar = "ScaleBar";
}

public sealed record BoardBuildBoard(
    string Code,
    string Title,
    IReadOnlyList<BoardBuildCard> Cards);

public sealed record BoardBuildRequest(
    string Title,
    string OutputPath,
    double BoardWidthMm,
    double BoardHeightMm,
    BoardGrid Grid,
    IReadOnlyList<BoardBuildBoard> Boards,
    /// <summary>
    /// Draw an outline where a card has no content yet. True while composing,
    /// so the layout can be seen before the material arrives; false for a
    /// submission, where an empty card must leave no mark on the paper.
    /// </summary>
    bool ShowPlaceholders = true);

public sealed record BoardBuildResult(
    string OutputPath,
    int PageCount,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Where a card's content lands, when the card shows only part of its source.
///
/// This is the geometry alone, kept beside <see cref="PortfolioPlacement"/> and
/// for the same reason: a board is printed at a metre across, and a placement
/// that is slightly wrong there is obvious to everyone looking at it.
/// </summary>
public static class BoardPlacement
{
    /// <summary>
    /// Places the source so that the cropped part of it sits wholly inside the
    /// area, centred. The returned rectangle is where the <em>whole</em> source
    /// is drawn - it reaches outside the area by design, and the caller clips
    /// to the area so only the cropped part shows.
    ///
    /// With the whole source selected this reduces exactly to
    /// <see cref="PortfolioPlacement.Fit"/>, which is the guarantee that keeps
    /// cropping from being a second, divergent way of placing things.
    /// </summary>
    public static PortfolioPlacementRect? FitCropped(
        double sourceWidth,
        double sourceHeight,
        double cropX,
        double cropY,
        double cropWidth,
        double cropHeight,
        double areaLeft,
        double areaTop,
        double areaWidth,
        double areaHeight)
    {
        double visibleWidth = sourceWidth * cropWidth;
        double visibleHeight = sourceHeight * cropHeight;
        if (!IsUsable(visibleWidth) || !IsUsable(visibleHeight))
            return null;

        double scale = Math.Min(areaWidth / visibleWidth, areaHeight / visibleHeight);
        return Place(
            sourceWidth, sourceHeight, cropX, cropY, cropWidth, cropHeight,
            areaLeft, areaTop, areaWidth, areaHeight, scale);
    }

    /// <summary>
    /// Places the source so that the cropped part of it covers the area,
    /// cutting away whatever still falls outside.
    /// </summary>
    public static PortfolioPlacementRect? CoverCropped(
        double sourceWidth,
        double sourceHeight,
        double cropX,
        double cropY,
        double cropWidth,
        double cropHeight,
        double areaLeft,
        double areaTop,
        double areaWidth,
        double areaHeight,
        double focalPointX,
        double focalPointY)
    {
        double visibleWidth = sourceWidth * cropWidth;
        double visibleHeight = sourceHeight * cropHeight;
        if (!IsUsable(visibleWidth) || !IsUsable(visibleHeight))
            return null;

        double scale = Math.Max(areaWidth / visibleWidth, areaHeight / visibleHeight);
        if (!double.IsFinite(scale) || scale <= 0)
            return null;

        double drawnVisibleWidth = visibleWidth * scale;
        double drawnVisibleHeight = visibleHeight * scale;
        // The crop's own origin, in the coordinates the whole source is drawn in.
        double offsetX = sourceWidth * cropX * scale;
        double offsetY = sourceHeight * cropY * scale;
        return new PortfolioPlacementRect(
            areaLeft - (drawnVisibleWidth - areaWidth) * Math.Clamp(focalPointX, 0, 1) - offsetX,
            areaTop - (drawnVisibleHeight - areaHeight) * Math.Clamp(focalPointY, 0, 1) - offsetY,
            sourceWidth * scale,
            sourceHeight * scale);
    }

    private static PortfolioPlacementRect? Place(
        double sourceWidth,
        double sourceHeight,
        double cropX,
        double cropY,
        double cropWidth,
        double cropHeight,
        double areaLeft,
        double areaTop,
        double areaWidth,
        double areaHeight,
        double scale)
    {
        if (!double.IsFinite(scale) || scale <= 0)
            return null;

        double drawnVisibleWidth = sourceWidth * cropWidth * scale;
        double drawnVisibleHeight = sourceHeight * cropHeight * scale;
        double offsetX = sourceWidth * cropX * scale;
        double offsetY = sourceHeight * cropY * scale;
        return new PortfolioPlacementRect(
            areaLeft + (areaWidth - drawnVisibleWidth) / 2 - offsetX,
            areaTop + (areaHeight - drawnVisibleHeight) / 2 - offsetY,
            sourceWidth * scale,
            sourceHeight * scale);
    }

    private static bool IsUsable(double value) => double.IsFinite(value) && value > 0;
}

/// <summary>
/// Writes a series of boards as one document: a page per board, each holding
/// its cards where the grid puts them.
///
/// This is the portfolio writer's sibling rather than its replacement. That one
/// gives a page to every item in turn; this one composes many items onto one
/// large sheet. A board carrying a single card fitted to the page is the same
/// output the portfolio has always produced, which is what lets the two exist
/// side by side without either changing.
/// </summary>
public static class BoardPdfWriter
{
    private const double PointsPerMm = 72.0 / 25.4;
    private const double CaptionBandMm = 8;
    private const double CaptionGapMm = 2;

    private static readonly XSolidBrush PaperBrush = new(XColors.White);
    private static readonly XSolidBrush BleedBrush = new(XColor.FromArgb(24, 26, 30));
    private static readonly XSolidBrush CaptionBrush = new(XColor.FromArgb(28, 30, 34));
    private static readonly XPen PlaceholderPen = new(XColor.FromArgb(168, 174, 184), 0.6)
    {
        DashStyle = XDashStyle.Dash,
    };

    public static BoardBuildResult Build(BoardBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Grid);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OutputPath);
        WindowsFontResolver.Register();

        var warnings = new List<string>();
        using var document = new PdfDocument();
        document.Info.Title = string.IsNullOrWhiteSpace(request.Title)
            ? "Самбар"
            : request.Title.Trim();

        foreach (BoardBuildBoard board in request.Boards)
            DrawBoard(document, request, board, warnings);

        if (document.PageCount == 0)
        {
            AddPage(document, request.BoardWidthMm, request.BoardHeightMm);
            warnings.Add("Цувралд самбар нэмээгүй тул хоосон баримт үүсгэлээ.");
        }

        // Asked before saving: a saved document refuses every question about
        // itself afterwards, including how many pages it just wrote.
        int pageCount = document.PageCount;
        string outputPath = SaveAtomically(document, request.OutputPath);
        return new BoardBuildResult(outputPath, pageCount, warnings);
    }

    private static void DrawBoard(
        PdfDocument document,
        BoardBuildRequest request,
        BoardBuildBoard board,
        List<string> warnings)
    {
        PdfPage page = AddPage(document, request.BoardWidthMm, request.BoardHeightMm);
        using XGraphics gfx = XGraphics.FromPdfPage(page);
        gfx.DrawRectangle(PaperBrush, 0, 0, page.Width.Point, page.Height.Point);

        string label = string.IsNullOrWhiteSpace(board.Code)
            ? board.Title
            : $"{board.Code} {board.Title}".Trim();

        // The content first, then what describes it. An annotation is drawn
        // from what the plan card actually turned out to be - the scale it came
        // out at, the surfaces it really used - so it cannot be drawn until
        // that card has been.
        var plans = new Dictionary<string, DrawnPlan>(StringComparer.Ordinal);
        foreach (BoardBuildCard card in board.Cards)
        {
            if (!card.Kind.Equals(BoardBuildCardKinds.Content, StringComparison.Ordinal))
                continue;
            if (Resolve(request, card, label, warnings) is not { } rect)
                continue;
            DrawCard(gfx, request, card, rect, label, warnings, plans);
        }

        foreach (BoardBuildCard card in board.Cards)
        {
            if (card.Kind.Equals(BoardBuildCardKinds.Content, StringComparison.Ordinal))
                continue;
            if (Resolve(request, card, label, warnings) is not { } rect)
                continue;
            DrawAnnotation(gfx, card, rect, label, plans, warnings);
        }
    }

    private static XRect? Resolve(
        BoardBuildRequest request,
        BoardBuildCard card,
        string boardLabel,
        List<string> warnings)
    {
        BoardRectMm? cell = BoardCardGeometry.Resolve(
            request.Grid,
            request.BoardWidthMm,
            request.BoardHeightMm,
            new BoardElement
            {
                Column = card.Column,
                ColumnSpan = card.ColumnSpan,
                Row = card.Row,
                RowSpan = card.RowSpan,
                WidthMm = card.WidthMm,
                HeightMm = card.HeightMm,
            });
        if (cell is { } rect)
            return ToPoints(rect);

        warnings.Add(
            $"Самбар {boardLabel}: карт торонд багтсангүй " +
            $"({card.Column},{card.Row} {card.ColumnSpan}x{card.RowSpan}).");
        return null;
    }

    /// <summary>
    /// A legend, a north arrow or a scale bar, drawn from the plan it names.
    ///
    /// Two of the three refuse to appear when the plan only assumed the answer.
    /// A missing north arrow is visible and gets fixed; one pointing the wrong
    /// way looks exactly like one pointing the right way, and is discovered by
    /// the jury.
    /// </summary>
    private static void DrawAnnotation(
        XGraphics gfx,
        BoardBuildCard card,
        XRect rect,
        string boardLabel,
        Dictionary<string, DrawnPlan> plans,
        List<string> warnings)
    {
        if (!plans.TryGetValue(card.PlanCardId ?? "", out DrawnPlan? plan))
        {
            warnings.Add(
                $"Самбар {boardLabel}: {DescribeAnnotation(card.Kind)} нь ямар " +
                "төлөвлөгөөг тайлбарлахаа заагаагүй тул зурагдсангүй.");
            return;
        }

        switch (card.Kind)
        {
            case BoardBuildCardKinds.Legend:
                if (BoardPlanAnnotations.DrawLegend(gfx, plan.Result.Legend, rect, card.Caption) <
                    plan.Result.Legend.Count)
                {
                    warnings.Add(
                        $"Самбар {boardLabel}: легенд бүхэлдээ багтсангүй, " +
                        "картыг өндөр болгоно уу.");
                }
                break;

            case BoardBuildCardKinds.NorthArrow:
                if (!BoardPlanAnnotations.DrawNorthArrow(
                        gfx,
                        plan.Manifest.NorthAngleDegrees,
                        plan.Manifest.NorthIsAssumed,
                        card.IsConfirmed,
                        rect))
                {
                    warnings.Add(
                        $"Самбар {boardLabel}: хойд зүг зурганд зарлагдаагүй тул сум " +
                        "зурагдсангүй — хэрэглэгч баталсны дараа зурагдана.");
                }
                break;

            case BoardBuildCardKinds.ScaleBar:
                if (!BoardPlanAnnotations.DrawScaleBar(
                        gfx,
                        plan.Result.ScaleDenominator,
                        unitsAreAssumed: false,
                        card.IsConfirmed,
                        rect))
                {
                    warnings.Add(
                        $"Самбар {boardLabel}: масштабын шугам энэ картад багтсангүй.");
                }
                break;

            default:
                warnings.Add($"Самбар {boardLabel}: '{card.Kind}' төрлийн карт танигдсангүй.");
                break;
        }
    }

    private static string DescribeAnnotation(string kind) => kind switch
    {
        BoardBuildCardKinds.Legend => "легенд",
        BoardBuildCardKinds.NorthArrow => "хойд сум",
        BoardBuildCardKinds.ScaleBar => "масштабын шугам",
        _ => kind,
    };

    private sealed record DrawnPlan(CityGenBoardManifest Manifest, BoardPlanDrawResult Result);

    private static void DrawCard(
        XGraphics gfx,
        BoardBuildRequest request,
        BoardBuildCard card,
        XRect cell,
        string boardLabel,
        List<string> warnings,
        Dictionary<string, DrawnPlan> plans)
    {
        string path = (card.SourcePath ?? "").Trim();
        string planPath = (card.PlanPath ?? "").Trim();
        bool hasCaption = !string.IsNullOrWhiteSpace(card.Caption);
        XRect content = hasCaption ? ContentAbove(cell) : cell;

        if (planPath.Length > 0)
        {
            DrawPlan(gfx, planPath, content, boardLabel, warnings, card, plans);
            DrawCaption(gfx, cell, card.Caption);
            return;
        }

        if (path.Length == 0)
        {
            // An empty card is a placeholder, not a failure: the layout is made
            // before the material arrives. It is drawn only while composing.
            if (request.ShowPlaceholders)
                gfx.DrawRectangle(PlaceholderPen, cell);
            DrawCaption(gfx, cell, card.Caption);
            return;
        }

        if (!File.Exists(path))
        {
            warnings.Add(
                $"Самбар {boardLabel}: файл олдсонгүй, карт хоосон үлдлээ - {Path.GetFileName(path)}");
            if (request.ShowPlaceholders)
                gfx.DrawRectangle(PlaceholderPen, cell);
            DrawCaption(gfx, cell, card.Caption);
            return;
        }

        bool fullBleed = card.Layout.Equals(
            ProjectPortfolioLayouts.FullBleed,
            StringComparison.OrdinalIgnoreCase);
        if (fullBleed)
            gfx.DrawRectangle(BleedBrush, content);

        try
        {
            using XImage image = OpenSource(card);
            PortfolioPlacementRect? placement = fullBleed
                ? BoardPlacement.CoverCropped(
                    image.PointWidth, image.PointHeight,
                    card.CropX, card.CropY, card.CropWidth, card.CropHeight,
                    content.Left, content.Top, content.Width, content.Height,
                    card.FocalPointX, card.FocalPointY)
                : BoardPlacement.FitCropped(
                    image.PointWidth, image.PointHeight,
                    card.CropX, card.CropY, card.CropWidth, card.CropHeight,
                    content.Left, content.Top, content.Width, content.Height);
            if (placement is not { } placed)
            {
                warnings.Add($"Самбар {boardLabel}: картын хэмжээг тооцож чадсангүй.");
                return;
            }

            // Clipped whenever the drawn source reaches past the card: a crop
            // is drawn by placing the whole source and showing only the part
            // that falls inside, so the clip is what makes the crop a crop.
            XGraphicsState state = gfx.Save();
            gfx.IntersectClip(content);
            gfx.DrawImage(image, placed.Left, placed.Top, placed.Width, placed.Height);
            gfx.Restore(state);
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or
                NotSupportedException or ArgumentException)
        {
            warnings.Add(
                $"Самбар {boardLabel}: файлыг уншиж чадсангүй - " +
                $"{Path.GetFileName(path)} - {exception.Message}");
            return;
        }

        DrawCaption(gfx, cell, card.Caption);
    }

    /// <summary>
    /// Draws the general plan from its classification. A plan that cannot be
    /// read leaves the board standing and says why: half a masterplan drawn
    /// silently would be worse than none.
    /// </summary>
    private static void DrawPlan(
        XGraphics gfx,
        string planPath,
        XRect content,
        string boardLabel,
        List<string> warnings,
        BoardBuildCard card,
        Dictionary<string, DrawnPlan> plans)
    {
        CityGenBoardLoadResult loaded = CityGenGraphicBoardReader.Load(planPath);
        if (!loaded.IsLoaded)
        {
            warnings.Add(
                $"Самбар {boardLabel}: ерөнхий төлөвлөгөөг уншиж чадсангүй - " +
                string.Join("; ", loaded.Issues));
            return;
        }

        BoardPlanDrawResult drawn = BoardPlanRenderer.Draw(gfx, loaded.Manifest!, content);
        // Recorded under the card that drew it, so a legend or a scale bar can
        // be drawn from what this plan actually turned out to be.
        if (!string.IsNullOrWhiteSpace(card.Id))
            plans[card.Id] = new DrawnPlan(loaded.Manifest!, drawn);
        if (drawn.ShapesDrawn == 0)
        {
            warnings.Add($"Самбар {boardLabel}: ерөнхий төлөвлөгөөнөөс зурах дүрс олдсонгүй.");
            return;
        }

        foreach (string skipped in loaded.SkippedObjects)
            warnings.Add($"Самбар {boardLabel}: {skipped}");
        if (drawn.UnrecognisedShapes > 0)
        {
            // Drawn, but in the neutral style: the plan came in carrying a
            // classification Studio has no entry for, and that is worth saying
            // rather than leaving the user to notice the grey patches.
            warnings.Add(
                $"Самбар {boardLabel}: {drawn.UnrecognisedShapes} дүрсийн ангилал танигдсангүй.");
        }
        // An assumed north is not reported here. Nothing on a plan without an
        // arrow depends on it, and a warning with no consequence is noise -
        // which is exactly what buried a hundred and ninety-one duplicated
        // identifiers behind a hundred and ninety-one identical lines. It is
        // said where it matters instead: when an arrow refuses to be drawn.
    }

    private static XImage OpenSource(BoardBuildCard card)
    {
        // A PDF page is placed as a form, so it stays vector at any board size.
        if (Path.GetExtension(card.SourcePath).Equals(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            var form = XPdfForm.FromFile(card.SourcePath);
            form.PageNumber = Math.Max(1, card.SourcePageNumber);
            return form;
        }
        return XImage.FromFile(card.SourcePath);
    }

    /// <summary>The card area above its caption band.</summary>
    private static XRect ContentAbove(XRect cell)
    {
        double band = (CaptionBandMm + CaptionGapMm) * PointsPerMm;
        return new XRect(
            cell.Left,
            cell.Top,
            cell.Width,
            Math.Max(1, cell.Height - band));
    }

    private static void DrawCaption(XGraphics gfx, XRect cell, string caption)
    {
        string text = (caption ?? "").Trim();
        if (text.Length == 0)
            return;

        double band = CaptionBandMm * PointsPerMm;
        var rect = new XRect(cell.Left, cell.Bottom - band, Math.Max(1, cell.Width), band);
        var font = new XFont("Segoe UI", 8, XFontStyleEx.Regular);
        gfx.DrawString(text, font, CaptionBrush, rect, XStringFormats.CenterLeft);
    }

    private static PdfPage AddPage(PdfDocument document, double widthMm, double heightMm)
    {
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(Math.Max(50, widthMm));
        page.Height = XUnit.FromMillimeter(Math.Max(50, heightMm));
        return page;
    }

    private static XRect ToPoints(BoardRectMm rect) => new(
        rect.LeftMm * PointsPerMm,
        rect.TopMm * PointsPerMm,
        Math.Max(1, rect.WidthMm * PointsPerMm),
        Math.Max(1, rect.HeightMm * PointsPerMm));

    private static string SaveAtomically(PdfDocument document, string requestedPath)
    {
        string outputPath = Path.GetFullPath(requestedPath);
        string directory = Path.GetDirectoryName(outputPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        string temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(outputPath)}.{Guid.NewGuid():N}.tmp.pdf");
        try
        {
            document.Save(temporaryPath);
            File.Move(temporaryPath, outputPath, overwrite: true);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
            catch (IOException)
            {
                // Best-effort cleanup must not mask the build result.
            }
        }

        return outputPath;
    }
}
