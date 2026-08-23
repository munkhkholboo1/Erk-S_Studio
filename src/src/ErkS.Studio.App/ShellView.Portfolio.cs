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

        portfolioAddImageButton.Click += (_, _) => AddPortfolioVisualizations();
        portfolioAddFileButton.Click += (_, _) => AddPortfolioFiles();
        portfolioAddAlbumPageButton.Click += (_, _) => AddPortfolioAlbumPages();
        portfolioMoveUpButton.Click += (_, _) => MovePortfolioItem(-1);
        portfolioMoveDownButton.Click += (_, _) => MovePortfolioItem(1);
        portfolioRemoveButton.Click += (_, _) => RemovePortfolioItem();
        portfolioBuildButton.Click += (_, _) => BuildPortfolioPdf();
        portfolioOpenButton.Click += (_, _) => OpenPortfolioPdf();
        portfolioItemsList.SelectionChanged += (_, _) => RefreshPortfolioInspector();
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
        panel.Children.Add(actions);
        panel.Children.Add(portfolioItemsList);

        panel.Children.Add(StudioWidgets.CreateSectionHeader("Сонгосон хуудас"));
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
            Width = 110,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(PortfolioItemRow.Layout)),
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
                ProjectDocumentAssetInspection inspection =
                    ProjectDocumentAssetInspector.Inspect(sourcePath);
                string relativePath = ProjectDocumentFileStore.StoreInsideProject(
                    state.ProjectPath,
                    ProjectDocumentCategories.Portfolio,
                    sourcePath);
                ProjectFileReference document = CreateDocumentReference(
                    sourcePath,
                    relativePath,
                    ProjectDocumentCategories.Portfolio,
                    Path.GetFileNameWithoutExtension(sourcePath),
                    inspection);
                // A presentation asset never enters an upload queue.
                document.CloudSyncStatus = ProjectDocumentCloudSyncStatuses.Local;
                AddDocumentIfMissing(state.Project.PortfolioDocuments, document);
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
        if (portfolioItemsList.SelectedItem is not PortfolioItemRow selected)
            return;

        Portfolio.Items.RemoveAll(item =>
            item.Id.Equals(selected.Item.Id, StringComparison.OrdinalIgnoreCase));
        CommitPortfolio("Портфолиогоос хаслаа.");
    }

    private void ApplyPortfolioInspector()
    {
        if (portfolioInspectorSuspended ||
            portfolioItemsList.SelectedItem is not PortfolioItemRow selected)
        {
            return;
        }

        string caption = portfolioCaptionBox.Text.Trim();
        string layout = (portfolioLayoutBox.SelectedItem as PortfolioLayoutChoice)?.Value
            ?? selected.Item.Layout;
        if (selected.Item.Caption.Equals(caption, StringComparison.Ordinal) &&
            selected.Item.Layout.Equals(layout, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

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
            portfolioCaptionBox.Text = selected?.Item.Caption ?? "";
            portfolioCaptionBox.IsEnabled = selected is not null;
            portfolioLayoutBox.IsEnabled = selected is not null;
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

    private void RefreshPortfolio(string? selectItemId = null)
    {
        if (!state.HasOpenProject)
            return;

        string? keepId = selectItemId ??
            (portfolioItemsList.SelectedItem as PortfolioItemRow)?.Item.Id;
        portfolioItemsList.ItemsSource = Portfolio.OrderedItems()
            .Select(item => new PortfolioItemRow(item))
            .ToList();
        if (keepId is not null)
        {
            portfolioItemsList.SelectedItem = portfolioItemsList.Items
                .OfType<PortfolioItemRow>()
                .FirstOrDefault(row => row.Item.Id.Equals(keepId, StringComparison.OrdinalIgnoreCase));
        }

        portfolioSummary.Text = Portfolio.LastBuiltAtUtc is null
            ? $"{Portfolio.Items.Count} хуудас. PDF үүсгээгүй байна."
            : $"{Portfolio.Items.Count} хуудас. Сүүлд {Portfolio.LastPageCount} хуудсаар " +
              $"{Portfolio.LastBuiltAtUtc.Value.ToLocalTime():yyyy-MM-dd HH:mm}-д үүсгэсэн.";
        portfolioOpenButton.IsEnabled = ResolvePortfolioPdfPath() is { } path && File.Exists(path);
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
                Portfolio.OrderedItems().Select(ResolveBuildItem).ToList());

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

        public string Layout => Item.Layout switch
        {
            ProjectPortfolioLayouts.FullBleed => "Хуудас дүүрэн (тайрна)",
            ProjectPortfolioLayouts.FitPage => "Захгүй, бүтнээр",
            _ => "Захтай, бүтнээр",
        };
    }
}
