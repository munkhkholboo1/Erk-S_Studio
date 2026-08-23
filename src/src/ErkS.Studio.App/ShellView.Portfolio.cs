using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

/// <summary>
/// Портфолио: the project shown rather than recorded. It assembles pages of the
/// project's album, its imagery and files added straight to it, and prints them
/// without the sheet frame and title block the album is bound by.
/// </summary>
internal sealed partial class ShellView
{
    private readonly ListView portfolioItemsList = new() { MinHeight = 260 };
    private readonly ComboBox portfolioPageSizeModeBox = new();
    private readonly TextBox portfolioPageWidthBox = new() { Width = 90 };
    private readonly TextBox portfolioPageHeightBox = new() { Width = 90 };
    private readonly TextBox portfolioTitleBox = new();
    private readonly TextBox portfolioCaptionBox = new();
    private readonly ComboBox portfolioLayoutBox = new();
    private readonly TextBlock portfolioSummary = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        FontSize = StudioTheme.HintFontSize,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly Button portfolioAddImageButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Төслийн зураг",
        "Төсөлд бүртгэлтэй визуал зургуудаас сонгож нэмэх");
    private readonly Button portfolioAddFileButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Файл нэмэх",
        "Зураг эсвэл PDF файлыг портфолиод шууд нэмэх");
    private readonly Button portfolioAddAlbumPageButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Альбомын хуудас",
        "Төслийн альбомын хуудсуудаас сонгож нэмэх");
    private readonly Button portfolioMoveUpButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Дээш",
        "Сонгосон хуудсыг нэг дээш зөөх");
    private readonly Button portfolioMoveDownButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Доош",
        "Сонгосон хуудсыг нэг доош зөөх");
    private readonly Button portfolioRemoveButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Хасах",
        "Сонгосон хуудсыг портфолиогоос хасах");
    private readonly Button portfolioRestoreButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Сэргээх",
        "Хассан хуудсыг портфолиод буцааж оруулах");
    private readonly CheckBox portfolioShowRemovedCheck = new()
    {
        Content = "Хассаныг харуулах",
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0),
    };
    private readonly Button portfolioBuildButton = StudioWidgets.CreatePrimaryButton("PDF үүсгэх");
    private readonly Button portfolioOpenButton = StudioWidgets.CreateButton("PDF нээх");
    private bool portfolioInspectorSuspended;

    private UIElement BuildPortfolioPage()
    {
        ConfigurePortfolioList();
        portfolioLayoutBox.ItemsSource = new[]
        {
            new PortfolioLayoutChoice(ProjectPortfolioLayouts.Contain, "Захтай, бүтнээр"),
            new PortfolioLayoutChoice(ProjectPortfolioLayouts.FitPage, "Захгүй, бүтнээр"),
            new PortfolioLayoutChoice(ProjectPortfolioLayouts.FullBleed, "Хуудас дүүрэн (тайрна)"),
        };
        portfolioLayoutBox.DisplayMemberPath = nameof(PortfolioLayoutChoice.Label);
        portfolioPageSizeModeBox.ItemsSource = new[]
        {
            new PortfolioLayoutChoice(ProjectPortfolioPageSizeModes.Fixed, "Тогтмол хэмжээ"),
            new PortfolioLayoutChoice(ProjectPortfolioPageSizeModes.SourcePage, "Эх хуудасны хэмжээгээр"),
        };
        portfolioPageSizeModeBox.DisplayMemberPath = nameof(PortfolioLayoutChoice.Label);
        portfolioPageSizeModeBox.SelectionChanged += (_, _) => ApplyPortfolioPageSetup();
        portfolioPageWidthBox.LostFocus += (_, _) => ApplyPortfolioPageSetup();
        portfolioPageHeightBox.LostFocus += (_, _) => ApplyPortfolioPageSetup();

        portfolioAddImageButton.Click += (_, _) => AddPortfolioVisualizations();
        portfolioAddFileButton.Click += (_, _) => AddPortfolioFiles();
        portfolioAddAlbumPageButton.Click += (_, _) => AddPortfolioAlbumPages();
        portfolioMoveUpButton.Click += (_, _) => MovePortfolioItem(-1);
        portfolioMoveDownButton.Click += (_, _) => MovePortfolioItem(1);
        portfolioRemoveButton.Click += (_, _) => RemovePortfolioItem();
        portfolioRestoreButton.Click += (_, _) => RestorePortfolioItem();
        portfolioShowRemovedCheck.Checked += (_, _) => RefreshPortfolio();
        portfolioShowRemovedCheck.Unchecked += (_, _) => RefreshPortfolio();
        portfolioBuildButton.Click += (_, _) => BuildPortfolioPdf();
        portfolioOpenButton.Click += (_, _) => OpenPortfolioPdf();
        portfolioItemsList.SelectionChanged += (_, _) => RefreshPortfolioInspector();
        portfolioTitleBox.LostFocus += (_, _) => ApplyPortfolioInspector();
        portfolioCaptionBox.LostFocus += (_, _) => ApplyPortfolioInspector();
        portfolioLayoutBox.SelectionChanged += (_, _) => ApplyPortfolioInspector();

        var panel = new StackPanel
        {
            Margin = new Thickness(18),
            MaxWidth = 1080,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.Children.Add(StudioWidgets.CreateTitle("Портфолио"));
        panel.Children.Add(StudioWidgets.CreateHint(
            "Төслийн танилцуулга. Альбомын хуудас, төслийн зураг, нэмсэн файлуудыг дараалуулж " +
            "нэг баримт болгоно. Албан ёсны альбомоос ялгаатай нь хүрээ, булангийн хүснэгтгүй, " +
            "чөлөөт форматтай."));

        var actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 8) };
        actions.Children.Add(portfolioAddAlbumPageButton);
        actions.Children.Add(portfolioAddImageButton);
        actions.Children.Add(portfolioAddFileButton);
        actions.Children.Add(portfolioMoveUpButton);
        actions.Children.Add(portfolioMoveDownButton);
        actions.Children.Add(portfolioRemoveButton);
        actions.Children.Add(portfolioRestoreButton);
        actions.Children.Add(portfolioShowRemovedCheck);
        panel.Children.Add(actions);
        panel.Children.Add(portfolioItemsList);

        panel.Children.Add(StudioWidgets.CreateSectionHeader("Хуудасны хэмжээ"));
        panel.Children.Add(StudioWidgets.CreateFormRow("Горим", portfolioPageSizeModeBox));
        var sizeRow = new WrapPanel();
        sizeRow.Children.Add(portfolioPageWidthBox);
        sizeRow.Children.Add(new TextBlock
        {
            Text = " × ",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StudioTheme.MutedTextBrush,
        });
        sizeRow.Children.Add(portfolioPageHeightBox);
        sizeRow.Children.Add(new TextBlock
        {
            Text = " мм",
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = StudioTheme.MutedTextBrush,
        });
        panel.Children.Add(StudioWidgets.CreateFormRow("Хэмжээ", sizeRow));
        panel.Children.Add(StudioWidgets.CreateHint(
            "«Эх хуудасны хэмжээгээр» горимд хуудас бүр өөрийн зургийн хэмжээгээр гарна — " +
            "танилцуулга холимог хэмжээтэй болно. Том форматаар зурсан хуудсыг жижигрүүлэхгүй."));

        panel.Children.Add(StudioWidgets.CreateSectionHeader("Сонгосон хуудас"));
        panel.Children.Add(StudioWidgets.CreateFormRow("Нэр", portfolioTitleBox));
        panel.Children.Add(StudioWidgets.CreateFormRow("Тайлбар", portfolioCaptionBox));
        panel.Children.Add(StudioWidgets.CreateFormRow("Байрлал", portfolioLayoutBox));

        var output = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        output.Children.Add(portfolioBuildButton);
        output.Children.Add(portfolioOpenButton);
        panel.Children.Add(output);
        panel.Children.Add(portfolioSummary);
        return StudioWidgets.CreateScrollHost(panel);
    }

    private void ConfigurePortfolioList()
    {
        var view = new GridView();
        view.Columns.Add(new GridViewColumn
        {
            Header = "№",
            Width = 46,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(PortfolioItemRow.Position)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Төрөл",
            Width = 132,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(PortfolioItemRow.Kind)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Нэр",
            Width = 320,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(PortfolioItemRow.Title)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Тайлбар",
            Width = 300,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(PortfolioItemRow.Caption)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Байрлал",
            Width = 150,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(PortfolioItemRow.Layout)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Төлөв",
            Width = 150,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(PortfolioItemRow.State)),
        });
        portfolioItemsList.View = view;
    }

    private ProjectPortfolio Portfolio => state.Project.Portfolio;

    private void AddPortfolioVisualizations()
    {
        if (!state.HasOpenProject)
            return;

        IReadOnlyList<ProjectVisualizationImage> images =
            state.Project.Visualizations.ImagesForProject(state.Project.ProjectId);
        if (images.Count == 0)
        {
            SetStatus("Төсөлд бүртгэлтэй визуал зураг алга байна.");
            return;
        }

        var picker = new StudioListPickerDialog(
            "Төслийн зураг сонгох",
            images.Select(image => new StudioListPickerRow(
                image.Id,
                Path.GetFileName(image.RelativePath),
                image.RelativePath)).ToList())
        {
            Owner = Window.GetWindow(Root),
        };
        if (picker.ShowDialog() != true || picker.SelectedKeys.Count == 0)
            return;

        foreach (string id in picker.SelectedKeys)
        {
            ProjectVisualizationImage? image = images.FirstOrDefault(candidate =>
                candidate.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (image is null)
                continue;

            Portfolio.Items.Add(new ProjectPortfolioItem
            {
                Order = Portfolio.Items.Count + 1,
                Kind = ProjectPortfolioItemKinds.Image,
                Layout = ProjectPortfolioLayouts.FullBleed,
                Title = Path.GetFileNameWithoutExtension(image.RelativePath),
                RelativePath = image.RelativePath,
                FocalPointX = image.FocalPointX,
                FocalPointY = image.FocalPointY,
            });
        }
        CommitPortfolio($"{picker.SelectedKeys.Count} зураг нэмэгдлээ.");
    }

    private void AddPortfolioFiles()
    {
        if (!state.HasOpenProject || state.ProjectPath is null)
            return;

        int added = 0;
        foreach (string sourcePath in ChooseDocumentFiles("Портфолиод нэмэх файл сонгох"))
        {
            try
            {
                // Inspected so an unreadable or unsupported file is refused
                // before it is copied in, the same as any other project asset.
                ProjectDocumentAssetInspection inspection =
                    ProjectDocumentAssetInspector.Inspect(sourcePath);
                string relativePath = ProjectDocumentFileStore.StoreInsideProject(
                    state.ProjectPath,
                    ProjectDocumentCategories.Portfolio,
                    sourcePath);
                // The portfolio item is the only record of this file. It was
                // also written into a project document list that nothing ever
                // read, which meant a file could be listed there and referenced
                // nowhere - the item, and the storage tidy-up that follows it,
                // are now the single account of what the portfolio holds.
                Portfolio.Items.Add(new ProjectPortfolioItem
                {
                    Order = Portfolio.Items.Count + 1,
                    Kind = ProjectPortfolioItemKinds.Document,
                    Layout = inspection.ContentType.Equals(
                        "application/pdf",
                        StringComparison.OrdinalIgnoreCase)
                            ? ProjectPortfolioLayouts.Contain
                            : ProjectPortfolioLayouts.FullBleed,
                    Title = Path.GetFileNameWithoutExtension(sourcePath),
                    RelativePath = relativePath,
                });
                added++;
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or
                    UnauthorizedAccessException or InvalidOperationException)
            {
                SetStatus($"Файл нэмсэнгүй: {exception.Message}");
            }
        }

        if (added > 0)
            CommitPortfolio($"{added} файл нэмэгдлээ.");
    }

    private void AddPortfolioAlbumPages()
    {
        if (!state.HasOpenProject)
            return;

        string? albumPath = ResolveAlbumPreviewPath();
        if (albumPath is null || !File.Exists(albumPath))
        {
            SetStatus("Эхлээд альбомаа үүсгэнэ үү. Портфолио альбомын хуудсыг ашиглана.");
            return;
        }

        List<AlbumPageWorkspaceItem> pages = BuildAlbumWorkspaceItems()
            .Where(item => item.Kind == AlbumWorkspaceNodeKind.Page)
            .Where(item => (item.BuiltPageNumber ?? ResolveBuiltAlbumPage(item)) is not null)
            .ToList();
        if (pages.Count == 0)
        {
            SetStatus("Альбомд нэмэх хуудас олдсонгүй.");
            return;
        }

        var picker = new StudioListPickerDialog(
            "Альбомын хуудас сонгох",
            pages.Select(page => new StudioListPickerRow(
                (page.BuiltPageNumber ?? ResolveBuiltAlbumPage(page) ?? 0)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture),
                $"{page.Number} · {page.Title}",
                page.GroupLabel)).ToList())
        {
            Owner = Window.GetWindow(Root),
        };
        if (picker.ShowDialog() != true || picker.SelectedKeys.Count == 0)
            return;

        foreach (string key in picker.SelectedKeys)
        {
            if (!int.TryParse(key, out int pageNumber) || pageNumber <= 0)
                continue;

            AlbumPageWorkspaceItem? page = pages.FirstOrDefault(candidate =>
                (candidate.BuiltPageNumber ?? ResolveBuiltAlbumPage(candidate)) == pageNumber);
            Portfolio.Items.Add(new ProjectPortfolioItem
            {
                Order = Portfolio.Items.Count + 1,
                Kind = ProjectPortfolioItemKinds.AlbumPage,
                Layout = ProjectPortfolioLayouts.Contain,
                Title = page is null ? $"Альбом · {pageNumber}" : $"{page.Number} · {page.Title}",
                SourcePageNumber = pageNumber,
                AlbumPageId = page?.Page?.Id.ToString() ?? "",
            });
        }
        CommitPortfolio($"{picker.SelectedKeys.Count} хуудас нэмэгдлээ.");
    }

    private void MovePortfolioItem(int offset)
    {
        if (portfolioItemsList.SelectedItem is not PortfolioItemRow selected)
            return;

        List<ProjectPortfolioItem> ordered = Portfolio.OrderedItems().ToList();
        int index = ordered.FindIndex(item =>
            item.Id.Equals(selected.Item.Id, StringComparison.OrdinalIgnoreCase));
        int target = index + offset;
        if (index < 0 || target < 0 || target >= ordered.Count)
            return;

        (ordered[index], ordered[target]) = (ordered[target], ordered[index]);
        for (int position = 0; position < ordered.Count; position++)
            ordered[position].Order = position + 1;
        Portfolio.Items = ordered;
        CommitPortfolio("", selected.Item.Id);
    }

    private void RemovePortfolioItem()
    {
        if (portfolioItemsList.SelectedItem is not PortfolioItemRow selected || selected.Item.IsRemoved)
            return;

        // An imported page is hidden rather than deleted: the next export from
        // the same drawing would otherwise put it straight back, and taking one
        // out by accident would leave no way back.
        if (selected.Item.Kind.Equals(
                ProjectPortfolioItemKinds.CadPage,
                StringComparison.OrdinalIgnoreCase))
        {
            selected.Item.RemovedAtUtc = DateTimeOffset.UtcNow;
            CommitPortfolio(
                "Портфолиогоос хаслаа. «Хассаныг харуулах»-аар сэргээж болно.",
                selected.Item.Id);
            return;
        }

        Portfolio.Items.RemoveAll(item =>
            item.Id.Equals(selected.Item.Id, StringComparison.OrdinalIgnoreCase));
        CommitPortfolio("Портфолиогоос хаслаа.");
    }

    private void RestorePortfolioItem()
    {
        if (portfolioItemsList.SelectedItem is not PortfolioItemRow selected ||
            !selected.Item.IsRemoved)
        {
            return;
        }

        selected.Item.RemovedAtUtc = null;
        CommitPortfolio("Хуудсыг портфолиод буцаалаа.", selected.Item.Id);
    }

    private void ApplyPortfolioInspector()
    {
        if (portfolioInspectorSuspended ||
            portfolioItemsList.SelectedItem is not PortfolioItemRow selected)
        {
            return;
        }

        string title = portfolioTitleBox.Text.Trim();
        string caption = portfolioCaptionBox.Text.Trim();
        string layout = (portfolioLayoutBox.SelectedItem as PortfolioLayoutChoice)?.Value
            ?? selected.Item.Layout;
        if (selected.Item.Title.Equals(title, StringComparison.Ordinal) &&
            selected.Item.Caption.Equals(caption, StringComparison.Ordinal) &&
            selected.Item.Layout.Equals(layout, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // A name the user typed is theirs from now on: the next import updates
        // the drawing behind this page but leaves the wording alone.
        selected.Item.Title = title.Length > 0 ? title : selected.Item.SourceTitle;
        selected.Item.Caption = caption;
        selected.Item.Layout = layout;
        CommitPortfolio("", selected.Item.Id);
    }

    private void RefreshPortfolioInspector()
    {
        portfolioInspectorSuspended = true;
        try
        {
            var selected = portfolioItemsList.SelectedItem as PortfolioItemRow;
            portfolioTitleBox.Text = selected?.Item.Title ?? "";
            portfolioTitleBox.IsEnabled = selected is not null;
            portfolioCaptionBox.Text = selected?.Item.Caption ?? "";
            portfolioCaptionBox.IsEnabled = selected is not null;
            portfolioLayoutBox.IsEnabled = selected is not null;
            portfolioRemoveButton.IsEnabled = selected is not null && !selected.Item.IsRemoved;
            portfolioRestoreButton.IsEnabled = selected?.Item.IsRemoved == true;
            portfolioLayoutBox.SelectedItem = portfolioLayoutBox.Items
                .OfType<PortfolioLayoutChoice>()
                .FirstOrDefault(choice => choice.Value.Equals(
                    selected?.Item.Layout,
                    StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            portfolioInspectorSuspended = false;
        }
    }

    /// <summary>
    /// Says what a received package brought the portfolio. Imported pages are
    /// otherwise silent: they are not album sheets, so no other message
    /// mentions them.
    /// </summary>
    private static string DescribePortfolioArrival(PackageRecordResult recorded)
    {
        int created = recorded.CreatedPortfolioItemCount;
        int updated = recorded.UpdatedPortfolioItemCount;
        if (created > 0 && updated > 0)
            return $"Портфолиод {created} хуудас нэмэгдэж, {updated} хуудас шинэчлэгдлээ.";
        return created > 0
            ? $"Портфолиод {created} хуудас нэмэгдлээ."
            : $"Портфолиогийн {updated} хуудас шинэчлэгдлээ.";
    }

    private void CommitPortfolio(string status, string? selectItemId = null)
    {
        Portfolio.Normalize();
        state.SaveProject();
        RefreshPortfolio(selectItemId);
        if (!string.IsNullOrWhiteSpace(status))
            SetStatus(status);
    }

    /// <summary>
    /// Applies the page setup. Only the portfolio's own page settings change
    /// here - the pages in it, their order and their wording are untouched, so
    /// changing the size never costs the user the arrangement they built.
    /// </summary>
    private void ApplyPortfolioPageSetup()
    {
        if (portfolioInspectorSuspended || !state.HasOpenProject)
            return;

        string mode = (portfolioPageSizeModeBox.SelectedItem as PortfolioLayoutChoice)?.Value
            ?? Portfolio.PageSizeMode;
        double width = ParsePageSize(portfolioPageWidthBox.Text, Portfolio.PageWidthMm);
        double height = ParsePageSize(portfolioPageHeightBox.Text, Portfolio.PageHeightMm);
        if (Portfolio.PageSizeMode.Equals(mode, StringComparison.OrdinalIgnoreCase) &&
            Math.Abs(Portfolio.PageWidthMm - width) < 0.001 &&
            Math.Abs(Portfolio.PageHeightMm - height) < 0.001)
        {
            return;
        }

        Portfolio.PageSizeMode = mode;
        Portfolio.PageWidthMm = width;
        Portfolio.PageHeightMm = height;
        CommitPortfolio(
            Portfolio.UsesSourcePageSize
                ? "Хуудас бүр эх зургийнхаа хэмжээгээр гарна."
                : $"Хуудасны хэмжээ {Portfolio.PageWidthMm:0.#} × {Portfolio.PageHeightMm:0.#} мм боллоо.",
            (portfolioItemsList.SelectedItem as PortfolioItemRow)?.Item.Id);
    }

    private static double ParsePageSize(string text, double fallback) =>
        double.TryParse(
            (text ?? "").Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.CurrentCulture,
            out double value) && double.IsFinite(value) && value >= 50
            ? value
            : fallback;

    private void RefreshPortfolio(string? selectItemId = null)
    {
        if (!state.HasOpenProject)
            return;

        string? keepId = selectItemId ??
            (portfolioItemsList.SelectedItem as PortfolioItemRow)?.Item.Id;
        portfolioItemsList.ItemsSource = (portfolioShowRemovedCheck.IsChecked == true
                ? Portfolio.OrderedItems()
                : Portfolio.OrderedVisibleItems())
            .Select(item => new PortfolioItemRow(item))
            .ToList();
        if (keepId is not null)
        {
            portfolioItemsList.SelectedItem = portfolioItemsList.Items
                .OfType<PortfolioItemRow>()
                .FirstOrDefault(row => row.Item.Id.Equals(keepId, StringComparison.OrdinalIgnoreCase));
        }

        int visibleCount = Portfolio.OrderedVisibleItems().Count;
        int removedCount = Portfolio.Items.Count - visibleCount;
        string removedNote = removedCount > 0 ? $" {removedCount} хуудас хасагдсан." : "";
        portfolioSummary.Text = Portfolio.LastBuiltAtUtc is null
            ? $"{visibleCount} хуудас. PDF үүсгээгүй байна.{removedNote}"
            : $"{visibleCount} хуудас. Сүүлд {Portfolio.LastPageCount} хуудсаар " +
              $"{Portfolio.LastBuiltAtUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}-д үүсгэсэн.{removedNote}";
        portfolioOpenButton.IsEnabled = ResolvePortfolioPdfPath() is { } path && File.Exists(path);
        portfolioInspectorSuspended = true;
        try
        {
            portfolioPageSizeModeBox.SelectedItem = portfolioPageSizeModeBox.Items
                .OfType<PortfolioLayoutChoice>()
                .FirstOrDefault(choice => choice.Value.Equals(
                    Portfolio.PageSizeMode,
                    StringComparison.OrdinalIgnoreCase));
            portfolioPageWidthBox.Text = Portfolio.PageWidthMm.ToString("0.#");
            portfolioPageHeightBox.Text = Portfolio.PageHeightMm.ToString("0.#");
            portfolioPageWidthBox.IsEnabled = !Portfolio.UsesSourcePageSize;
            portfolioPageHeightBox.IsEnabled = !Portfolio.UsesSourcePageSize;
        }
        finally
        {
            portfolioInspectorSuspended = false;
        }
        RefreshPortfolioInspector();
    }

    private string? ResolvePortfolioPdfPath()
    {
        if (!state.HasOpenProject || state.ProjectPath is null ||
            string.IsNullOrWhiteSpace(Portfolio.LastPdfPath))
        {
            return null;
        }

        try
        {
            return ProjectWorkspacePaths.ResolveInsideProject(
                state.ProjectPath,
                Portfolio.LastPdfPath);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private void BuildPortfolioPdf()
    {
        if (!state.HasOpenProject || state.ProjectPath is null)
            return;
        if (Portfolio.Items.Count == 0)
        {
            SetStatus("Портфолиод эхлээд хуудас нэмнэ үү.");
            return;
        }

        try
        {
            string projectFolder = state.ResolveProjectFolder();
            string outputFolder = Path.Combine(projectFolder, "portfolio");
            string outputPath = Path.Combine(
                outputFolder,
                SafeFileName(Portfolio.Title) + ".pdf");
            var request = new PortfolioBuildRequest(
                Portfolio.Title,
                outputPath,
                Portfolio.PageWidthMm,
                Portfolio.PageHeightMm,
                Portfolio.OrderedVisibleItems().Select(ResolveBuildItem).ToList(),
                Portfolio.UsesSourcePageSize);

            PortfolioBuildResult result = PortfolioPdfWriter.Build(request);
            Portfolio.LastPdfPath = Path.GetRelativePath(projectFolder, result.OutputPath);
            Portfolio.LastPageCount = result.PageCount;
            Portfolio.LastPdfSha256 = Convert
                .ToHexString(SHA256.HashData(File.ReadAllBytes(result.OutputPath)))
                .ToLowerInvariant();
            Portfolio.LastBuiltAtUtc = DateTimeOffset.UtcNow;
            state.SaveProject();
            RefreshPortfolio();
            SetStatus(result.Warnings.Count == 0
                ? $"Портфолио {result.PageCount} хуудсаар үүслээ."
                : $"Портфолио {result.PageCount} хуудсаар үүслээ. " +
                  string.Join(" ", result.Warnings.Take(3)));
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                InvalidDataException or InvalidOperationException)
        {
            SetStatus($"Портфолио үүсгэсэнгүй: {exception.Message}");
        }
    }

    private PortfolioBuildItem ResolveBuildItem(ProjectPortfolioItem item)
    {
        string path = "";
        if (item.Kind.Equals(ProjectPortfolioItemKinds.AlbumPage, StringComparison.OrdinalIgnoreCase))
        {
            path = ResolveAlbumPreviewPath() ?? "";
        }
        else if (!string.IsNullOrWhiteSpace(item.RelativePath) && state.ProjectPath is not null)
        {
            try
            {
                path = ProjectWorkspacePaths.ResolveInsideProject(
                    state.ProjectPath,
                    item.RelativePath);
            }
            catch (InvalidDataException)
            {
                path = "";
            }
        }

        return new PortfolioBuildItem(
            item.Kind,
            item.Layout,
            item.Caption,
            path,
            item.SourcePageNumber,
            item.FocalPointX,
            item.FocalPointY);
    }

    private void OpenPortfolioPdf()
    {
        if (ResolvePortfolioPdfPath() is not { } path || !File.Exists(path))
        {
            SetStatus("Портфолиогийн PDF хараахан үүсээгүй байна.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is IOException or InvalidOperationException or
                System.ComponentModel.Win32Exception)
        {
            SetStatus($"PDF нээсэнгүй: {exception.Message}");
        }
    }

    private sealed record PortfolioLayoutChoice(string Value, string Label);

    private sealed record PortfolioItemRow(ProjectPortfolioItem Item)
    {
        public string Position => Item.Order.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public string Kind => Item.Kind switch
        {
            ProjectPortfolioItemKinds.AlbumPage => "Альбомын хуудас",
            ProjectPortfolioItemKinds.Document => "Нэмсэн файл",
            ProjectPortfolioItemKinds.CadPage => "CAD хуудас",
            _ => "Төслийн зураг",
        };

        public string Title => Item.Title;

        public string Caption => Item.Caption;

        /// <summary>
        /// Why a page looks the way it does: taken out by the user, or still
        /// here although the drawing it came from no longer offers it.
        /// </summary>
        public string State => Item.IsRemoved
            ? "Хасагдсан"
            : Item.MissingFromSourceSinceUtc is not null
                ? "Эх багцад алга"
                : "";

        public string Layout => Item.Layout switch
        {
            ProjectPortfolioLayouts.FullBleed => "Хуудас дүүрэн (тайрна)",
            ProjectPortfolioLayouts.FitPage => "Захгүй, бүтнээр",
            _ => "Захтай, бүтнээр",
        };
    }
}
