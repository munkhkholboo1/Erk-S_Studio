using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ErkS.Platform.Core;
using Microsoft.Web.WebView2.Wpf;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private readonly ListBox designSourcesWorkspaceList = new();
    private readonly ListView receivedSheetsWorkspaceList = new();
    private readonly TextBlock sourceDetailsText = new() { TextWrapping = TextWrapping.Wrap };

    private readonly ListBox albumPagesWorkspaceList = new();
    private readonly ToggleButton albumListViewToggle = new();
    private readonly ToggleButton albumThumbnailViewToggle = new();
    private readonly PdfPageImageCache albumPageImages = new();
    private readonly HashSet<string> collapsedAlbumWorkspaceNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Grid albumPreviewHost = new();
    private readonly WebView2 albumPdfViewer = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly ComboBox albumPageFormatBox = new();
    private readonly ComboBox albumPlacementBox = new();
    private readonly ComboBox albumSectionBox = new();
    private readonly TextBox albumPageNumberBox = new();
    private readonly TextBox albumPageTitleBox = new();
    private readonly CheckBox includeCoverCheck = new() { Content = "Нүүр хуудас" };
    private readonly CheckBox includeTocCheck = new() { Content = "Зургийн жагсаалт" };
    private bool bindingAlbumPage;
    private bool albumThumbnailMode;
    private bool albumPdfViewerConfigured;
    private bool sourceRefreshInProgress;
    private string? loadedAlbumPdfDocumentKey;
    private long albumPdfNavigationSerial;
    private CancellationTokenSource? albumThumbnailLoadCancellation;
    private string? selectedAlbumWorkspaceKey;

    private UIElement BuildSourcesPage()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildSourceRibbon());

        var workspace = new Grid { Background = StudioTheme.WindowBackgroundBrush };
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 360 });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        Grid.SetRow(workspace, 1);
        root.Children.Add(workspace);

        designSourcesWorkspaceList.BorderThickness = new Thickness(0);
        designSourcesWorkspaceList.SelectionChanged += (_, _) =>
        {
            RefreshReceivedSheetWorkspace();
            RefreshSourceDetails();
        };
        workspace.Children.Add(BuildPane("Эх үүсвэрүүд", designSourcesWorkspaceList, new Thickness(0, 0, 1, 0)));

        ConfigureReceivedSheetsList();
        var sheetsPane = BuildPane("Хүлээн авсан sheets", receivedSheetsWorkspaceList, new Thickness(0, 0, 1, 0));
        Grid.SetColumn(sheetsPane, 1);
        workspace.Children.Add(sheetsPane);

        sourceDetailsText.Foreground = StudioTheme.MutedTextBrush;
        sourceDetailsText.Margin = new Thickness(2, 4, 2, 10);
        var details = new StackPanel();
        details.Children.Add(sourceDetailsText);
        var openFolder = StudioWidgets.CreateIconTextButton("icon-sources.svg", "Inbox нээх");
        openFolder.Click += (_, _) => OpenSelectedSourceFolder();
        details.Children.Add(openFolder);
        var detailPane = BuildPane(
            "Эх үүсвэрийн мэдээлэл",
            new ScrollViewer { Content = details, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            new Thickness(0));
        Grid.SetColumn(detailPane, 2);
        workspace.Children.Add(detailPane);
        return root;
    }

    private UIElement BuildSourceRibbon()
    {
        var ribbon = CreateRibbon();
        var sourceGroup = CreateRibbonGroup("SOURCE");
        var addSource = StudioWidgets.CreateIconTextButton("icon-sources.svg", "Эх үүсвэр нэмэх");
        addSource.Background = StudioTheme.AccentBrush;
        addSource.BorderBrush = StudioTheme.AccentBrush;
        addSource.Click += (_, _) => AddDesignSourceFromDialog();
        var removeSource = StudioWidgets.CreateButton("Хасах");
        removeSource.ToolTip = "Бүртгэлээс хасна. Файл устгахгүй.";
        removeSource.Click += (_, _) => RemoveSelectedDesignSource();
        var rescan = StudioWidgets.CreateButton("Шинэчлэлт шалгах");
        rescan.ToolTip = "Source package-уудыг шалгаж, альбум болон PDF харагдацыг бүрэн шинэчилнэ.";
        rescan.Click += (_, _) => CheckForSourceUpdates();
        sourceGroup.Children.Add(addSource);
        sourceGroup.Children.Add(removeSource);
        sourceGroup.Children.Add(rescan);
        ribbon.Children.Add(sourceGroup);

        var albumGroup = CreateRibbonGroup("ALBUM");
        var addSelected = StudioWidgets.CreateIconTextButton("icon-album.svg", "Сонгосныг нэмэх");
        addSelected.Click += (_, _) => AddSelectedSheetsToAlbum();
        var addVisible = StudioWidgets.CreateButton("Харагдаж буй бүгд");
        addVisible.Click += (_, _) => AddVisibleSheetsToAlbum();
        albumGroup.Children.Add(addSelected);
        albumGroup.Children.Add(addVisible);
        ribbon.Children.Add(albumGroup);
        return ribbon;
    }

    private void CheckForSourceUpdates()
    {
        if (!state.HasOpenProject || sourceRefreshInProgress || !EnsureProjectContentPermission())
        {
            return;
        }

        sourceRefreshInProgress = true;
        var selectedSourceId = (designSourcesWorkspaceList.SelectedItem as SourceWorkspaceItem)?.Source.Id;
        SheetIntakeScanResult scan;
        try
        {
            SetStatus("Шинэчлэлт шалгаж байна...");
            scan = state.Intake.Rescan();
        }
        catch (Exception exception)
        {
            sourceRefreshInProgress = false;
            SetStatus($"Шинэчлэлт шалгахад алдаа: {exception.Message}");
            return;
        }

        // Package callbacks reconcile authoritative snapshots on the UI dispatcher.
        // Queue the album rebuild after those callbacks so deletion and addition are atomic to the user.
        dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                autoRebuildTimer.Stop();
                RefreshSourceWorkspace(selectedSourceId);
                RefreshAlbumWorkspace();
                UpdateAlbum(silent: false, statusPrefix: BuildSourceRefreshSummary(scan));
            }
            finally
            {
                sourceRefreshInProgress = false;
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static string BuildSourceRefreshSummary(SheetIntakeScanResult scan)
    {
        var summary = scan.ChangedPackageCount == 0
            ? $"{scan.ManifestCount} package шалгав, шинэ source өөрчлөлтгүй"
            : $"{scan.ChangedPackageCount} package шинэчлэгдэж, " +
              $"{scan.UpdatedSheetCount} sheet шинэчлэгдэн, {scan.RemovedSheetCount} sheet хасагдав";
        return scan.ErrorCount == 0 ? summary : $"{summary}, {scan.ErrorCount} алдаа";
    }

    private void ConfigureReceivedSheetsList()
    {
        receivedSheetsWorkspaceList.SelectionMode = SelectionMode.Extended;
        receivedSheetsWorkspaceList.BorderThickness = new Thickness(0);
        receivedSheetsWorkspaceList.Background = StudioTheme.InputBrush;
        receivedSheetsWorkspaceList.Foreground = StudioTheme.TextBrush;

        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.TextBrush));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 4, 5, 4)));
        receivedSheetsWorkspaceList.ItemContainerStyle = itemStyle;

        var view = new GridView();
        var headerStyle = new Style(typeof(GridViewColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, StudioTheme.PanelAltBrush));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.MutedTextBrush));
        headerStyle.Setters.Add(new Setter(Control.BorderBrushProperty, StudioTheme.BorderBrush));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 1, 1)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
        view.ColumnHeaderContainerStyle = headerStyle;
        view.Columns.Add(new GridViewColumn { Header = "Дугаар", Width = 90, DisplayMemberBinding = new Binding(nameof(SheetWorkspaceItem.Number)) });
        view.Columns.Add(new GridViewColumn { Header = "Нэр", Width = 200, DisplayMemberBinding = new Binding(nameof(SheetWorkspaceItem.Name)) });
        view.Columns.Add(new GridViewColumn { Header = "Эх файл", Width = 150, DisplayMemberBinding = new Binding(nameof(SheetWorkspaceItem.Application)) });
        view.Columns.Add(new GridViewColumn { Header = "Format", Width = 90, DisplayMemberBinding = new Binding(nameof(SheetWorkspaceItem.Size)) });
        view.Columns.Add(new GridViewColumn { Header = "Төлөв", Width = 70, DisplayMemberBinding = new Binding(nameof(SheetWorkspaceItem.Status)) });
        receivedSheetsWorkspaceList.View = view;
    }

    private void AddDesignSourceFromDialog()
    {
        if (!EnsureProjectContentPermission())
            return;
        var dialog = new DesignSourceDialog(state.Project, state.ResolveDefaultSourceFolder)
        {
            Owner = Window.GetWindow(Root),
        };
        if (dialog.ShowDialog() != true || dialog.ResultSource is null)
        {
            return;
        }

        state.AddDesignSource(dialog.ResultSource);
        RefreshSourceWorkspace(dialog.ResultSource.Id);
        SetStatus($"Эх үүсвэр нэмэгдлээ: {dialog.ResultSource.DisplayName}");
    }

    private void RemoveSelectedDesignSource()
    {
        if (!EnsureProjectContentPermission())
            return;
        if (designSourcesWorkspaceList.SelectedItem is not SourceWorkspaceItem selected)
        {
            return;
        }

        state.RemoveDesignSource(selected.Source);
        RefreshSourceWorkspace();
        SetStatus($"Эх үүсвэрийн бүртгэлийг хаслаа: {selected.Source.DisplayName}. Файлууд хэвээр үлдсэн.");
    }

    private void OpenSelectedSourceFolder()
    {
        if (designSourcesWorkspaceList.SelectedItem is not SourceWorkspaceItem selected ||
            string.IsNullOrWhiteSpace(selected.Source.InboxFolder))
        {
            return;
        }

        Directory.CreateDirectory(selected.Source.InboxFolder);
        Process.Start(new ProcessStartInfo(selected.Source.InboxFolder) { UseShellExecute = true });
    }

    private void RefreshSourceWorkspace(string? selectSourceId = null)
    {
        if (selectSourceId is null && designSourcesWorkspaceList.SelectedItem is SourceWorkspaceItem current)
        {
            selectSourceId = current.Source.Id;
        }

        var items = state.Project.Sources
            .Select(source => new SourceWorkspaceItem(
                source,
                SourceDocumentLabel(source),
                $"{source.DisplayName}  |  {SourceStatusLabel(source.Status)}"))
            .ToList();
        designSourcesWorkspaceList.ItemsSource = items;
        designSourcesWorkspaceList.SelectedItem = items.FirstOrDefault(item =>
            string.Equals(item.Source.Id, selectSourceId, StringComparison.OrdinalIgnoreCase));
        if (designSourcesWorkspaceList.SelectedItem is null && items.Count > 0)
        {
            designSourcesWorkspaceList.SelectedIndex = 0;
        }

        RefreshReceivedSheetWorkspace();
        RefreshSourceDetails();
    }

    private void RefreshReceivedSheetWorkspace()
    {
        var records = state.Library.Snapshot().AsEnumerable();
        if (designSourcesWorkspaceList.SelectedItem is SourceWorkspaceItem selected)
        {
            records = selected.Source.UseLegacySheetKeys
                ? records.Where(record => string.IsNullOrWhiteSpace(record.SourceId))
                : records.Where(record => string.Equals(
                    record.SourceId,
                    selected.Source.Id,
                    StringComparison.OrdinalIgnoreCase));
        }

        receivedSheetsWorkspaceList.ItemsSource = records
            .Select(record => new SheetWorkspaceItem(
                record,
                record.Entry.Number,
                record.Entry.Name,
                ResolveSheetSourceLabel(record),
                FormatSize(record.Entry.WidthMm, record.Entry.HeightMm),
                record.IsVerified ? "OK" : "Алдаа"))
            .ToList();
    }

    private void RefreshSourceDetails()
    {
        if (designSourcesWorkspaceList.SelectedItem is not SourceWorkspaceItem selected)
        {
            sourceDetailsText.Text = "Эх үүсвэр сонгоно уу.";
            return;
        }

        var source = selected.Source;
        var sheetCount = state.Library.Snapshot().Count(record =>
            source.UseLegacySheetKeys
                ? string.IsNullOrWhiteSpace(record.SourceId)
                : string.Equals(record.SourceId, source.Id, StringComparison.OrdinalIgnoreCase));
        sourceDetailsText.Text =
            $"Төрөл: {source.Kind}\n" +
            $"Төлөв: {SourceStatusLabel(source.Status)}\n" +
            $"Үе шат: {state.Project.Identity.StageName}\n" +
            "Багц: Барилга архитектурын загвар зураг\n" +
            $"Хариуцагч: {(string.IsNullOrWhiteSpace(source.OwnerOrganizationName) ? "-" : source.OwnerOrganizationName)}\n" +
            $"Хүлээн авсан: {sheetCount} sheet\n\n" +
            $"Inbox\n{source.InboxFolder}\n\n" +
            $"Native файл\n{(string.IsNullOrWhiteSpace(source.NativeDocumentPath) ? "Локал эх файл холбогдоогүй" : source.NativeDocumentPath)}\n\n" +
            $"Source ID\n{source.Id}";
    }

    private void AddSelectedSheetsToAlbum()
    {
        if (!EnsureProjectContentPermission())
            return;
        var records = receivedSheetsWorkspaceList.SelectedItems
            .Cast<SheetWorkspaceItem>()
            .Select(item => item.Record)
            .ToList();
        AddSheetsToAlbum(records);
    }

    private void AddVisibleSheetsToAlbum()
    {
        if (!EnsureProjectContentPermission())
            return;
        var records = receivedSheetsWorkspaceList.Items
            .Cast<SheetWorkspaceItem>()
            .Select(item => item.Record)
            .ToList();
        AddSheetsToAlbum(records);
    }

    private void AddSheetsToAlbum(IReadOnlyList<SheetRecord> records)
    {
        var added = 0;
        var updated = 0;
        Guid? lastAddedId = null;
        foreach (var record in records)
        {
            var slot = string.Equals(
                state.Album.TemplateId,
                BuildingArchitectureConceptAlbumTemplate.TemplateId,
                StringComparison.OrdinalIgnoreCase)
                ? BuildingArchitectureConceptAlbumTemplate.FindSourceSlot(state.Album, record.Entry)
                : null;
            var existingPage = state.Album.Pages.FirstOrDefault(page =>
                string.Equals(page.SheetKey, record.Key, StringComparison.Ordinal));
            if (existingPage is not null)
            {
                if (slot is not null && !string.Equals(
                        existingPage.TemplateSlotId,
                        slot.Id,
                        StringComparison.OrdinalIgnoreCase))
                {
                    existingPage.TemplateSlotId = slot.Id;
                    existingPage.SectionId = BuildingArchitectureConceptAlbumTemplate.ResolveSectionId(state.Album, slot);
                    updated++;
                }
                if (PageFormatResolver.ApplySourceFormat(existingPage, record.Entry))
                {
                    updated++;
                    lastAddedId = existingPage.Id;
                }
                continue;
            }

            var source = state.Project.Sources.FirstOrDefault(item =>
                string.Equals(item.Id, record.SourceId, StringComparison.OrdinalIgnoreCase));
            var page = new AlbumPageDefinition
            {
                SheetKey = record.Key,
                TemplateSlotId = slot?.Id ?? "",
                SectionId = slot is null
                    ? ResolveDefaultSection(record, source)
                    : BuildingArchitectureConceptAlbumTemplate.ResolveSectionId(state.Album, slot),
                PageFormatId = ResolveDefaultPageFormat(source),
                PlacementMode = PagePlacementMode.FitDrawingArea,
            };
            PageFormatResolver.ApplySourceFormat(page, record.Entry);
            state.Album.Pages.Add(page);
            lastAddedId = page.Id;
            added++;
        }

        if (string.Equals(
                state.Album.TemplateId,
                BuildingArchitectureConceptAlbumTemplate.TemplateId,
                StringComparison.OrdinalIgnoreCase))
        {
            var ordered = BuildingArchitectureConceptAlbumSequencer.OrderPages(
                state.Album,
                state.Album.Pages,
                state.Library,
                state.Project.Sources);
            state.Album.Pages.Clear();
            state.Album.Pages.AddRange(ordered);
        }

        RefreshAlbumWorkspace(lastAddedId);
        SetStatus(added == 0 && updated == 0
            ? "Сонгосон sheets альбумд аль хэдийн орсон байна."
            : $"Альбум: {added} sheet нэмэгдэж, {updated} format шинэчлэгдлээ.");
    }

    private string ResolveDefaultPageFormat(ProjectDesignSource? source)
    {
        if (state.Project.Identity.StageCode.Contains("Concept", StringComparison.OrdinalIgnoreCase))
        {
            return PageFormatCatalog.ConceptA3LandscapeId;
        }

        return source?.Kind is DesignSourceKind.Pdf or DesignSourceKind.Folder
            ? PageFormatCatalog.SourceAsIsId
            : PageFormatCatalog.WorkingDrawingA3LandscapeId;
    }

    private Guid? ResolveDefaultSection(SheetRecord record, ProjectDesignSource? source)
    {
        var hints = new[] { record.Entry.Discipline, source?.Name }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();
        var match = state.Album.Sections.FirstOrDefault(section =>
            hints.Any(hint => section.Title.Contains(hint!, StringComparison.OrdinalIgnoreCase) ||
                              hint!.Contains(section.Title, StringComparison.OrdinalIgnoreCase)));
        if (match is not null)
        {
            return match.Id;
        }

        return state.Album.Sections.Count > 1
            ? state.Album.Sections[1].Id
            : state.Album.Sections.FirstOrDefault()?.Id;
    }

    private static string SourceStatusLabel(string status) => status switch
    {
        DesignSourceStatuses.Connected => "Холбогдсон",
        DesignSourceStatuses.Receiving => "Хүлээн авч байна",
        DesignSourceStatuses.Error => "Алдаатай",
        _ => "Холболт хүлээж байна",
    };

    private string ResolveSheetSourceLabel(SheetRecord record)
    {
        var source = state.Project.Sources.FirstOrDefault(item =>
            string.Equals(item.Id, record.SourceId, StringComparison.OrdinalIgnoreCase));
        if (source is not null)
        {
            return SourceDocumentLabel(source);
        }

        if (!string.IsNullOrWhiteSpace(record.Source.DocumentTitle))
        {
            return record.Source.DocumentTitle;
        }

        return string.IsNullOrWhiteSpace(record.Source.DocumentPath)
            ? record.Source.Application.ToString()
            : Path.GetFileName(record.Source.DocumentPath);
    }

    private static string SourceDocumentLabel(ProjectDesignSource source)
    {
        if (!string.IsNullOrWhiteSpace(source.NativeDocumentTitle))
        {
            return source.NativeDocumentTitle;
        }

        return string.IsNullOrWhiteSpace(source.NativeDocumentPath)
            ? source.DisplayName
            : Path.GetFileName(source.NativeDocumentPath);
    }

    private static string FormatSize(double width, double height) =>
        width > 0 && height > 0 ? $"{width:0} x {height:0}" : "PDF";

    private UIElement BuildAlbumPage()
    {
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(BuildAlbumRibbon());

        var workspace = new Grid { Background = StudioTheme.WindowBackgroundBrush };
        workspace.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
            MinWidth = 360,
        });
        Grid.SetRow(workspace, 1);
        root.Children.Add(workspace);

        albumPagesWorkspaceList.BorderThickness = new Thickness(0);
        albumPagesWorkspaceList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        albumPagesWorkspaceList.SetValue(
            ScrollViewer.HorizontalScrollBarVisibilityProperty,
            ScrollBarVisibility.Disabled);
        albumPagesWorkspaceList.ItemTemplate = CreateAlbumPageItemTemplate(thumbnailMode: false);
        albumPagesWorkspaceList.SelectionChanged += (_, _) => HandleAlbumWorkspaceSelection();
        albumPagesWorkspaceList.PreviewMouseLeftButtonDown += HandleAlbumNavigatorMouseDown;
        albumPagesWorkspaceList.KeyDown += HandleAlbumNavigatorKeyDown;
        albumPreviewHost.Background = new SolidColorBrush(Color.FromRgb(54, 58, 64));
        var previewPane = BuildPane("Альбумын бодит харагдац", albumPreviewHost, new Thickness(0));
        workspace.Children.Add(previewPane);
        return root;
    }

    private Border BuildAlbumNavigatorPane()
    {
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = "Альбумын хуудас",
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });

        ConfigureAlbumViewToggle(albumListViewToggle, "\uE8FD", "Жагсаалтаар харах");
        ConfigureAlbumViewToggle(albumThumbnailViewToggle, "\uE80A", "Thumbnail-аар харах");
        albumListViewToggle.IsChecked = true;
        albumThumbnailViewToggle.IsChecked = false;
        albumListViewToggle.Click += (_, _) => SetAlbumPageViewMode(thumbnailMode: false);
        albumThumbnailViewToggle.Click += (_, _) => SetAlbumPageViewMode(thumbnailMode: true);

        var viewModes = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        viewModes.Children.Add(albumListViewToggle);
        viewModes.Children.Add(albumThumbnailViewToggle);
        Grid.SetColumn(viewModes, 1);
        header.Children.Add(viewModes);
        return BuildPane(header, albumPagesWorkspaceList, new Thickness(0, 0, 1, 0));
    }

    private static void ConfigureAlbumViewToggle(ToggleButton button, string glyph, string tooltip)
    {
        button.ToolTip = tooltip;
        button.Width = 30;
        button.Height = 25;
        button.Padding = new Thickness(0);
        button.Margin = new Thickness(3, 0, 0, 0);
        button.Content = new TextBlock
        {
            Text = glyph,
            FontFamily = new FontFamily("Segoe MDL2 Assets"),
            FontSize = 14,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
    }

    private void SetAlbumPageViewMode(bool thumbnailMode)
    {
        albumThumbnailMode = thumbnailMode;
        albumListViewToggle.IsChecked = !thumbnailMode;
        albumThumbnailViewToggle.IsChecked = thumbnailMode;
        albumPagesWorkspaceList.ItemTemplate = CreateAlbumPageItemTemplate(thumbnailMode);
        RefreshAlbumWorkspace(selectItemKey: selectedAlbumWorkspaceKey);
    }

    private void HandleAlbumNavigatorMouseDown(object sender, MouseButtonEventArgs e)
    {
        var current = e.OriginalSource as DependencyObject;
        while (current is not null && current is not ListBoxItem)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        if (current is ListBoxItem { DataContext: AlbumPageWorkspaceItem { IsGroup: true } item })
        {
            e.Handled = true;
            ToggleAlbumWorkspaceGroup(item);
        }
    }

    private void HandleAlbumNavigatorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) ||
            albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem { IsGroup: true } item)
        {
            return;
        }

        e.Handled = true;
        ToggleAlbumWorkspaceGroup(item);
    }

    private void ToggleAlbumWorkspaceGroup(AlbumPageWorkspaceItem item)
    {
        if (!collapsedAlbumWorkspaceNodes.Add(item.NodeKey))
        {
            collapsedAlbumWorkspaceNodes.Remove(item.NodeKey);
        }
        RefreshAlbumWorkspace(selectItemKey: item.SelectionKey);
    }

    private void HandleAlbumWorkspaceSelection()
    {
        if (bindingAlbumPage ||
            albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem selected ||
            selected.IsGroup)
        {
            return;
        }

        selectedAlbumWorkspaceKey = selected.SelectionKey;
        BindSelectedAlbumPage();
    }

    private static DataTemplate CreateAlbumPageItemTemplate(bool thumbnailMode)
    {
        var root = new FrameworkElementFactory(typeof(Grid));
        root.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 2));
        root.AppendChild(CreateAlbumGroupTemplate());
        root.AppendChild(thumbnailMode ? CreateAlbumThumbnailTemplate() : CreateAlbumListTemplate());
        return new DataTemplate(typeof(AlbumPageWorkspaceItem)) { VisualTree = root };
    }

    private static FrameworkElementFactory CreateAlbumGroupTemplate()
    {
        var group = new FrameworkElementFactory(typeof(DockPanel));
        group.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(AlbumPageWorkspaceItem.Indent)));
        group.SetValue(FrameworkElement.MinHeightProperty, 28.0);
        group.SetValue(FrameworkElement.StyleProperty, CreateAlbumItemVisibilityStyle(typeof(DockPanel), showForGroups: true));

        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.SetBinding(TextBlock.TextProperty, new Binding(nameof(AlbumPageWorkspaceItem.ExpansionGlyph)));
        glyph.SetValue(FrameworkElement.WidthProperty, 18.0);
        glyph.SetValue(TextBlock.FontSizeProperty, 12.0);
        glyph.SetValue(TextBlock.ForegroundProperty, StudioTheme.AccentSoftBrush);
        glyph.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        glyph.SetValue(DockPanel.DockProperty, Dock.Left);
        group.AppendChild(glyph);

        var count = new FrameworkElementFactory(typeof(TextBlock));
        count.SetBinding(TextBlock.TextProperty, new Binding(nameof(AlbumPageWorkspaceItem.ChildCountLabel)));
        count.SetValue(TextBlock.ForegroundProperty, StudioTheme.FaintTextBrush);
        count.SetValue(TextBlock.MarginProperty, new Thickness(6, 0, 2, 0));
        count.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        count.SetValue(DockPanel.DockProperty, Dock.Right);
        group.AppendChild(count);

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(AlbumPageWorkspaceItem.Title)));
        title.SetValue(TextBlock.ForegroundProperty, StudioTheme.TextBrush);
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        title.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        group.AppendChild(title);
        return group;
    }

    private static FrameworkElementFactory CreateAlbumListTemplate()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(AlbumPageWorkspaceItem.Indent)));
        row.SetValue(FrameworkElement.MinHeightProperty, 34.0);
        row.SetValue(FrameworkElement.StyleProperty, CreateAlbumItemVisibilityStyle(typeof(DockPanel), showForGroups: false));

        var number = new FrameworkElementFactory(typeof(TextBlock));
        number.SetBinding(TextBlock.TextProperty, new Binding(nameof(AlbumPageWorkspaceItem.Number)));
        number.SetValue(FrameworkElement.WidthProperty, 40.0);
        number.SetValue(TextBlock.ForegroundProperty, StudioTheme.AccentSoftBrush);
        number.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        number.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        number.SetValue(DockPanel.DockProperty, Dock.Left);
        row.AppendChild(number);

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(AlbumPageWorkspaceItem.Title)));
        title.SetValue(TextBlock.ForegroundProperty, StudioTheme.TextBrush);
        title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        title.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(title);
        return row;
    }

    private static FrameworkElementFactory CreateAlbumThumbnailTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetBinding(FrameworkElement.MarginProperty, new Binding(nameof(AlbumPageWorkspaceItem.Indent)));
        panel.SetValue(FrameworkElement.WidthProperty, 207.0);
        panel.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        panel.SetValue(FrameworkElement.StyleProperty, CreateAlbumItemVisibilityStyle(typeof(StackPanel), showForGroups: false));

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(AlbumPageWorkspaceItem.Title)));
        title.SetValue(TextBlock.ForegroundProperty, StudioTheme.TextBrush);
        title.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        title.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        title.SetValue(TextBlock.MarginProperty, new Thickness(0, 1, 0, 5));
        panel.AppendChild(title);

        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetValue(FrameworkElement.WidthProperty, 207.0);
        var number = new FrameworkElementFactory(typeof(TextBlock));
        number.SetBinding(TextBlock.TextProperty, new Binding(nameof(AlbumPageWorkspaceItem.Number)));
        number.SetValue(FrameworkElement.WidthProperty, 34.0);
        number.SetValue(TextBlock.ForegroundProperty, StudioTheme.AccentSoftBrush);
        number.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        number.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Right);
        number.SetValue(TextBlock.MarginProperty, new Thickness(0, 4, 8, 0));
        number.SetValue(DockPanel.DockProperty, Dock.Left);
        row.AppendChild(number);

        var pageHost = new FrameworkElementFactory(typeof(Border));
        pageHost.SetValue(FrameworkElement.WidthProperty, 165.0);
        pageHost.SetValue(FrameworkElement.HeightProperty, 117.0);
        pageHost.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(238, 239, 241)));
        pageHost.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(105, 112, 122)));
        pageHost.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        pageHost.SetValue(Border.CornerRadiusProperty, new CornerRadius(1));
        pageHost.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);

        var pageVisual = new FrameworkElementFactory(typeof(Grid));
        var loading = new FrameworkElementFactory(typeof(TextBlock));
        loading.SetBinding(TextBlock.TextProperty, new Binding(nameof(AlbumPageWorkspaceItem.ThumbnailMessage)));
        loading.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(112, 118, 128)));
        loading.SetValue(TextBlock.FontSizeProperty, 8.0);
        loading.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        loading.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        loading.SetValue(FrameworkElement.MarginProperty, new Thickness(10));
        loading.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        loading.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        pageVisual.AppendChild(loading);

        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new Binding(nameof(AlbumPageWorkspaceItem.ThumbnailSource)));
        image.SetValue(Image.StretchProperty, Stretch.Uniform);
        image.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
        image.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
        pageVisual.AppendChild(image);

        pageHost.AppendChild(pageVisual);
        row.AppendChild(pageHost);
        panel.AppendChild(row);
        return panel;
    }

    private static Style CreateAlbumItemVisibilityStyle(Type targetType, bool showForGroups)
    {
        var style = new Style(targetType);
        style.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        var trigger = new DataTrigger
        {
            Binding = new Binding(nameof(AlbumPageWorkspaceItem.IsGroup)),
            Value = showForGroups,
        };
        trigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible));
        style.Triggers.Add(trigger);
        return style;
    }

    private UIElement BuildAlbumRibbon()
    {
        var ribbon = CreateRibbon();
        var documentGroup = CreateRibbonGroup("ALBUM");
        albumTitleBox.MinWidth = 220;
        albumTitleBox.Margin = new Thickness(0, 0, 8, 4);
        albumTitleBox.TextChanged += (_, _) =>
        {
            if (!bindingAlbumPage)
            {
                state.Album.Title = string.IsNullOrWhiteSpace(albumTitleBox.Text)
                    ? "Project album"
                    : albumTitleBox.Text.Trim();
            }
        };
        documentGroup.Children.Add(albumTitleBox);
        var save = StudioWidgets.CreateIconTextButton("icon-project.svg", "Хадгалах");
        save.Click += (_, _) => SaveProject();
        var updateAlbum = StudioWidgets.CreateIconTextButton("icon-album.svg", "Альбум шинэчлэх");
        updateAlbum.ToolTip = "Төслийн мэдээлэл, формат болон хамгийн сүүлийн source sheet-үүдээр альбумыг дахин бүрдүүлнэ.";
        updateAlbum.Background = StudioTheme.AccentBrush;
        updateAlbum.BorderBrush = StudioTheme.AccentBrush;
        updateAlbum.Click += (_, _) => CheckForSourceUpdates();
        var open = StudioWidgets.CreateButton("PDF нээх");
        open.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(lastAlbumPath) && File.Exists(lastAlbumPath))
            {
                Process.Start(new ProcessStartInfo(lastAlbumPath) { UseShellExecute = true });
            }
        };
        documentGroup.Children.Add(save);
        documentGroup.Children.Add(updateAlbum);
        documentGroup.Children.Add(open);
        autoRebuildCheck.Content = "Auto шинэчлэлт";
        autoRebuildCheck.ToolTip = "Эх үүсвэр өөрчлөгдөхөд альбумыг автоматаар шинэчилнэ.";
        autoRebuildCheck.Margin = new Thickness(8, 0, 0, 0);
        autoRebuildCheck.VerticalAlignment = VerticalAlignment.Center;
        documentGroup.Children.Add(autoRebuildCheck);
        ribbon.Children.Add(documentGroup);
        return ribbon;
    }

    private UIElement BuildAlbumProperties()
    {
        albumPageFormatBox.ItemsSource = PageFormatCatalog.All;
        albumPlacementBox.ItemsSource = new[]
        {
            new PlacementChoice(PagePlacementMode.PreserveDrawingSpace, "1:1 цэвэр зургийн талбай"),
            new PlacementChoice(PagePlacementMode.FitDrawingArea, "Зургийн талбайд багтаах"),
            new PlacementChoice(PagePlacementMode.FillCrop, "Талбайг дүүргэж тайрах"),
            new PlacementChoice(PagePlacementMode.FullPage, "Хуудсыг бүтэн дүүргэх"),
        };

        albumPageFormatBox.SelectionChanged += (_, _) => ApplyAlbumPageProperties();
        albumPlacementBox.SelectionChanged += (_, _) => ApplyAlbumPageProperties();
        albumSectionBox.SelectionChanged += (_, _) => ApplyAlbumPageProperties();
        albumPageNumberBox.TextChanged += (_, _) => ApplyAlbumPageProperties();
        albumPageTitleBox.TextChanged += (_, _) => ApplyAlbumPageProperties();
        includeCoverCheck.Checked += (_, _) => ApplyAlbumOptions();
        includeCoverCheck.Unchecked += (_, _) => ApplyAlbumOptions();
        includeTocCheck.Checked += (_, _) => ApplyAlbumOptions();
        includeTocCheck.Unchecked += (_, _) => ApplyAlbumOptions();

        var panel = new StackPanel { Margin = new Thickness(0, 0, 2, 0) };
        panel.Children.Add(StudioWidgets.CreateFormRow("Дугаар", albumPageNumberBox, 76));
        panel.Children.Add(StudioWidgets.CreateFormRow("Нэр", albumPageTitleBox, 76));
        panel.Children.Add(StudioWidgets.CreateFormRow("Format", albumPageFormatBox, 76));
        panel.Children.Add(StudioWidgets.CreateFormRow("Placement", albumPlacementBox, 76));
        panel.Children.Add(StudioWidgets.CreateFormRow("Бүлэг", albumSectionBox, 76));
        panel.Children.Add(StudioWidgets.CreateSectionHeader("Альбум"));
        panel.Children.Add(includeCoverCheck);
        panel.Children.Add(includeTocCheck);
        autoRebuildCheck.Content = "Эх үүсвэр шинэчлэгдэхэд альбум автоматаар шинэчлэх";
        autoRebuildCheck.Margin = new Thickness(0, 6, 0, 0);
        panel.Children.Add(autoRebuildCheck);
        albumInfoText.Foreground = StudioTheme.MutedTextBrush;
        albumInfoText.Margin = new Thickness(0, 10, 0, 0);
        panel.Children.Add(albumInfoText);
        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }

    private void RefreshAlbumWorkspace(Guid? selectPageId = null, string? selectItemKey = null)
    {
        var requestedSelectionKey = selectPageId is Guid pageId
            ? $"page:{pageId:N}"
            : selectItemKey;
        if (string.IsNullOrWhiteSpace(requestedSelectionKey) &&
            albumPagesWorkspaceList.SelectedItem is AlbumPageWorkspaceItem current)
        {
            requestedSelectionKey = current.SelectionKey;
        }
        requestedSelectionKey ??= selectedAlbumWorkspaceKey;

        var items = BuildAlbumWorkspaceItems();

        bindingAlbumPage = true;
        albumPagesWorkspaceList.ItemsSource = items;
        albumPagesWorkspaceList.SelectedItem = items.FirstOrDefault(item => string.Equals(
            item.SelectionKey,
            requestedSelectionKey,
            StringComparison.OrdinalIgnoreCase));
        if (albumPagesWorkspaceList.SelectedItem is null)
        {
            albumPagesWorkspaceList.SelectedItem = items.FirstOrDefault(item => !item.IsGroup)
                                                   ?? items.FirstOrDefault();
        }

        albumTitleBox.Text = state.Album.Title;
        includeCoverCheck.IsChecked = state.Album.IncludeCover;
        includeTocCheck.IsChecked = state.Album.IncludeTableOfContents;
        var hasComposition = state.Album.Composition.Count > 0;
        includeCoverCheck.Visibility = hasComposition ? Visibility.Collapsed : Visibility.Visible;
        if (hasComposition)
        {
            var ready = state.Album.Composition.Count(item =>
                item.Kind == AlbumCompositionKind.Generated ||
                state.Album.Pages.Any(page => string.Equals(
                    page.TemplateSlotId,
                    item.Id,
                    StringComparison.OrdinalIgnoreCase)));
            albumInfoText.Text = $"Бүрдэл {ready}/{state.Album.Composition.Count} · {state.Album.Pages.Count} source sheet";
        }
        else
        {
            albumInfoText.Text = $"{state.Album.Pages.Count} sheet | PDF output";
        }
        bindingAlbumPage = false;
        StartAlbumThumbnailLoading(items);
        if (albumPagesWorkspaceList.SelectedItem is AlbumPageWorkspaceItem { IsGroup: false } selected)
        {
            selectedAlbumWorkspaceKey = selected.SelectionKey;
            BindSelectedAlbumPage();
        }
    }

    private void StartAlbumThumbnailLoading(IReadOnlyList<AlbumPageWorkspaceItem> items)
    {
        albumThumbnailLoadCancellation?.Cancel();
        albumThumbnailLoadCancellation?.Dispose();
        albumThumbnailLoadCancellation = null;

        if (!albumThumbnailMode || string.IsNullOrWhiteSpace(lastAlbumPath) || !File.Exists(lastAlbumPath))
        {
            return;
        }

        foreach (var item in items.Where(item => !item.IsGroup))
        {
            item.BuiltPageNumber = ResolveBuiltAlbumPage(item);
            item.SetThumbnail(null, item.BuiltPageNumber.HasValue
                ? "Уншиж байна"
                : "Эх үүсвэр хүлээж байна");
        }

        var cancellation = new CancellationTokenSource();
        albumThumbnailLoadCancellation = cancellation;
        _ = LoadAlbumThumbnailsAsync(items, lastAlbumPath, cancellation.Token);
    }

    private async Task LoadAlbumThumbnailsAsync(
        IReadOnlyList<AlbumPageWorkspaceItem> items,
        string pdfPath,
        CancellationToken cancellationToken)
    {
        foreach (var item in items.Where(item => !item.IsGroup && item.BuiltPageNumber.HasValue))
        {
            try
            {
                var thumbnail = await albumPageImages.GetPageAsync(
                    pdfPath,
                    item.BuiltPageNumber!.Value,
                    pixelWidth: 400,
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                item.SetThumbnail(thumbnail, thumbnail is null ? "Thumbnail уншсангүй" : "");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                item.SetThumbnail(null, "Thumbnail уншсангүй");
            }
        }
    }

    private List<AlbumPageWorkspaceItem> BuildAlbumWorkspaceItems()
    {
        var albumNodeKey = $"album:{state.Project.ProjectId}";
        var root = CreateAlbumWorkspaceGroup(
            albumNodeKey,
            AlbumWorkspaceNodeKind.Album,
            $"Альбум · {state.Album.Title}");

        if (state.Album.Composition.Count == 0)
        {
            var sourcePages = CreateAlbumWorkspaceGroup(
                $"{albumNodeKey}:legacy-source-pages",
                AlbumWorkspaceNodeKind.Source,
                "Эх үүсвэрийн хуудас");
            foreach (var (page, index) in state.Album.Pages.Select((page, index) => (page, index)))
            {
                var record = state.Library.Find(page.SheetKey);
                var number = string.IsNullOrWhiteSpace(page.NumberOverride)
                    ? record?.Entry.Number ?? $"{index + 1:00}"
                    : page.NumberOverride;
                var title = string.IsNullOrWhiteSpace(page.TitleOverride)
                    ? record?.Entry.Name ?? "Source олдсонгүй"
                    : page.TitleOverride;
                sourcePages.Children.Add(CreateAlbumWorkspacePage(new AlbumPageWorkspaceItem(
                    page,
                    null,
                    number,
                    number,
                    title,
                    "",
                    "")));
            }
            root.Children.Add(sourcePages);
            return FlattenAlbumWorkspace(root);
        }

        var sequence = BuildingArchitectureConceptAlbumSequencer.Create(
            state.Album,
            state.Album.Pages,
            state.Library,
            state.Project.Sources);

        var studioPages = CreateAlbumWorkspaceGroup(
            $"{albumNodeKey}:studio-pages",
            AlbumWorkspaceNodeKind.Studio,
            "Studio хуудас");
        foreach (var component in state.Album.Composition
                     .Where(item => item.Kind == AlbumCompositionKind.Generated)
                     .OrderBy(item => item.Order))
        {
            studioPages.Children.Add(CreateAlbumWorkspacePage(new AlbumPageWorkspaceItem(
                null,
                component,
                component.Number,
                component.Number,
                component.Title,
                "Studio",
                "")));
        }
        root.Children.Add(studioPages);

        var generalPlanPages = CreateAlbumWorkspaceGroup(
            $"{albumNodeKey}:general-plan",
            AlbumWorkspaceNodeKind.GeneralPlan,
            "Ерөнхий төлөвлөгөө");
        foreach (var component in state.Album.Composition
                     .Where(item => item.Kind == AlbumCompositionKind.SourceSlot && !item.AllowMultiple)
                     .OrderBy(item => item.Order))
        {
            var linkedPages = sequence.Where(item =>
                    item.IsFixedTemplatePage &&
                    string.Equals(item.Slot?.Id, component.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (linkedPages.Count == 0)
            {
                generalPlanPages.Children.Add(CreateAlbumWorkspacePage(new AlbumPageWorkspaceItem(
                    null,
                    component,
                    component.Number,
                    component.Number,
                    component.Title,
                    "Эх үүсвэр хүлээж байна",
                    "")));
                continue;
            }

            foreach (var linkedPage in linkedPages)
            {
                generalPlanPages.Children.Add(CreateAlbumWorkspacePage(
                    CreateSourcePageWorkspaceItem(linkedPage)));
            }
        }
        root.Children.Add(generalPlanPages);

        var drawingPages = sequence.Where(item => !item.IsFixedTemplatePage).ToList();
        foreach (var sourceGroup in drawingPages.GroupBy(
                     ResolveAlbumWorkspaceSourceKey,
                     StringComparer.OrdinalIgnoreCase))
        {
            var firstSourcePage = sourceGroup.First();
            var sourceNode = CreateAlbumWorkspaceGroup(
                $"{albumNodeKey}:source:{sourceGroup.Key}",
                AlbumWorkspaceNodeKind.Source,
                $"Эх үүсвэр · {ResolveAlbumWorkspaceSourceTitle(firstSourcePage)}");

            foreach (var drawingTypeGroup in sourceGroup.GroupBy(
                         ResolveAlbumWorkspaceDrawingTypeKey,
                         StringComparer.OrdinalIgnoreCase))
            {
                var firstDrawingPage = drawingTypeGroup.First();
                var drawingTypeNode = CreateAlbumWorkspaceGroup(
                    $"{sourceNode.Key}:type:{drawingTypeGroup.Key}",
                    AlbumWorkspaceNodeKind.DrawingType,
                    ResolveAlbumWorkspaceDrawingTypeTitle(firstDrawingPage));
                foreach (var linkedPage in drawingTypeGroup)
                {
                    drawingTypeNode.Children.Add(CreateAlbumWorkspacePage(
                        CreateSourcePageWorkspaceItem(linkedPage)));
                }
                sourceNode.Children.Add(drawingTypeNode);
            }
            root.Children.Add(sourceNode);
        }

        return FlattenAlbumWorkspace(root);
    }

    private List<AlbumPageWorkspaceItem> FlattenAlbumWorkspace(AlbumWorkspaceNode root)
    {
        var items = new List<AlbumPageWorkspaceItem>();
        Append(root, 0);
        return items;

        void Append(AlbumWorkspaceNode node, int depth)
        {
            if (node.PageItem is AlbumPageWorkspaceItem pageItem)
            {
                items.Add(pageItem with
                {
                    NodeKey = node.Key,
                    Depth = depth,
                    Kind = AlbumWorkspaceNodeKind.Page,
                });
                return;
            }

            var expanded = !collapsedAlbumWorkspaceNodes.Contains(node.Key);
            items.Add(new AlbumPageWorkspaceItem(null, null, "", "", node.Title, "", "")
            {
                NodeKey = node.Key,
                Depth = depth,
                Kind = node.Kind,
                ChildCount = CountAlbumWorkspacePages(node),
                IsExpanded = expanded,
            });
            if (!expanded)
            {
                return;
            }

            foreach (var child in node.Children)
            {
                Append(child, depth + 1);
            }
        }
    }

    private static AlbumWorkspaceNode CreateAlbumWorkspaceGroup(
        string key,
        AlbumWorkspaceNodeKind kind,
        string title) => new()
        {
            Key = key,
            Kind = kind,
            Title = title,
        };

    private static AlbumWorkspaceNode CreateAlbumWorkspacePage(AlbumPageWorkspaceItem item) => new()
    {
        Key = item.Page is AlbumPageDefinition page
            ? $"page:{page.Id:N}"
            : $"component:{item.Component?.Id ?? Guid.NewGuid().ToString("N")}",
        Kind = AlbumWorkspaceNodeKind.Page,
        Title = item.Title,
        PageItem = item,
    };

    private static int CountAlbumWorkspacePages(AlbumWorkspaceNode node) =>
        node.PageItem is AlbumPageWorkspaceItem item
            ? (item.Page is not null || item.Component?.Kind == AlbumCompositionKind.Generated ? 1 : 0)
            : node.Children.Sum(CountAlbumWorkspacePages);

    private static AlbumPageWorkspaceItem CreateSourcePageWorkspaceItem(ConceptAlbumSourcePage item)
    {
        var title = string.IsNullOrWhiteSpace(item.Page.TitleOverride)
            ? item.Sheet?.Entry.Name ?? item.Slot?.Title ?? "Source олдсонгүй"
            : item.Page.TitleOverride;
        return new AlbumPageWorkspaceItem(
            item.Page,
            item.Slot,
            item.Number,
            item.AutomaticNumber,
            title,
            "",
            "");
    }

    private static string ResolveAlbumWorkspaceSourceKey(ConceptAlbumSourcePage item)
    {
        if (!string.IsNullOrWhiteSpace(item.Source?.Id))
        {
            return item.Source.Id;
        }

        if (!string.IsNullOrWhiteSpace(item.Sheet?.SourceId))
        {
            return item.Sheet.SourceId;
        }

        var separator = item.Page.SheetKey.IndexOf('|');
        return separator > 0 ? item.Page.SheetKey[..separator] : item.Page.SheetKey;
    }

    private static string ResolveAlbumWorkspaceSourceTitle(ConceptAlbumSourcePage item)
    {
        if (!string.IsNullOrWhiteSpace(item.Source?.NativeDocumentTitle))
        {
            return item.Source.NativeDocumentTitle.Trim();
        }
        if (!string.IsNullOrWhiteSpace(item.Source?.NativeDocumentPath))
        {
            return Path.GetFileName(item.Source.NativeDocumentPath.Trim());
        }
        if (!string.IsNullOrWhiteSpace(item.Source?.Name))
        {
            return item.Source.Name.Trim();
        }
        if (!string.IsNullOrWhiteSpace(item.Sheet?.Source.DocumentTitle))
        {
            return item.Sheet.Source.DocumentTitle.Trim();
        }
        if (!string.IsNullOrWhiteSpace(item.Sheet?.Source.DocumentPath))
        {
            return Path.GetFileName(item.Sheet.Source.DocumentPath.Trim());
        }
        return "Эх үүсвэр";
    }

    private static string ResolveAlbumWorkspaceDrawingTypeKey(ConceptAlbumSourcePage item) =>
        !string.IsNullOrWhiteSpace(item.Slot?.Id)
            ? item.Slot.Id
            : !string.IsNullOrWhiteSpace(item.Sheet?.Entry.ContentKind)
                ? item.Sheet.Entry.ContentKind.Trim()
                : "drawing-pages";

    private static string ResolveAlbumWorkspaceDrawingTypeTitle(ConceptAlbumSourcePage item) =>
        !string.IsNullOrWhiteSpace(item.Slot?.SectionTitle)
            ? item.Slot.SectionTitle.Trim()
            : !string.IsNullOrWhiteSpace(item.Sheet?.Entry.ContentKind)
                ? item.Sheet.Entry.ContentKind.Trim()
                : "Зургийн хуудас";

    private void BindSelectedAlbumPage()
    {
        bindingAlbumPage = true;
        albumSectionBox.ItemsSource = new[] { new SectionChoice(null, "Бүлэггүй") }
            .Concat(state.Album.Sections.Select(section => new SectionChoice(section.Id, section.Title)))
            .ToList();

        if (albumPagesWorkspaceList.SelectedItem is AlbumPageWorkspaceItem selected &&
            selected.Page is AlbumPageDefinition page)
        {
            SetAlbumPagePropertiesEnabled(true);
            var sheet = state.Library.Find(page.SheetKey);
            albumPageNumberBox.Text = selected.Number;
            albumPageTitleBox.Text = string.IsNullOrWhiteSpace(page.TitleOverride)
                ? sheet?.Entry.Name ?? ""
                : page.TitleOverride;
            var formatChoices = page.PageFormatSnapshot is not null &&
                                PageFormatCatalog.IsUsable(page.PageFormatSnapshot)
                ? new[] { page.PageFormatSnapshot }
                    .Concat(PageFormatCatalog.All.Where(format =>
                        !string.Equals(format.Id, page.PageFormatSnapshot.Id, StringComparison.OrdinalIgnoreCase)))
                    .ToList()
                : PageFormatCatalog.All.ToList();
            albumPageFormatBox.ItemsSource = formatChoices;
            albumPageFormatBox.SelectedItem = page.PageFormatSnapshot is not null &&
                                              PageFormatCatalog.IsUsable(page.PageFormatSnapshot)
                ? page.PageFormatSnapshot
                : formatChoices.FirstOrDefault(format =>
                    string.Equals(format.Id, page.PageFormatId, StringComparison.OrdinalIgnoreCase));
            albumPlacementBox.SelectedItem = albumPlacementBox.Items
                .Cast<PlacementChoice>()
                .FirstOrDefault(choice => choice.Value == page.PlacementMode);
            albumSectionBox.SelectedItem = albumSectionBox.Items
                .Cast<SectionChoice>()
                .FirstOrDefault(choice => choice.Id == page.SectionId);
        }
        else if (albumPagesWorkspaceList.SelectedItem is AlbumPageWorkspaceItem compositionItem)
        {
            SetAlbumPagePropertiesEnabled(false);
            albumPageNumberBox.Text = compositionItem.Component?.Number ?? compositionItem.Number;
            albumPageTitleBox.Text = compositionItem.Component?.Title ?? compositionItem.Title;
            albumPageFormatBox.ItemsSource = PageFormatCatalog.All;
            albumPageFormatBox.SelectedItem = PageFormatCatalog.Resolve(PageFormatCatalog.ConceptA3LandscapeId);
            albumPlacementBox.SelectedItem = null;
            albumSectionBox.SelectedItem = albumSectionBox.Items
                .Cast<SectionChoice>()
                .FirstOrDefault(choice => string.Equals(
                    choice.Label,
                    compositionItem.Component?.SectionTitle,
                    StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            SetAlbumPagePropertiesEnabled(false);
            albumPageFormatBox.ItemsSource = PageFormatCatalog.All;
            albumPageNumberBox.Text = "";
            albumPageTitleBox.Text = "";
            albumPageFormatBox.SelectedItem = null;
            albumPlacementBox.SelectedItem = null;
            albumSectionBox.SelectedItem = null;
        }

        bindingAlbumPage = false;
        RefreshAlbumPagePreview();
    }

    private void SetAlbumPagePropertiesEnabled(bool enabled)
    {
        albumPageNumberBox.IsEnabled = enabled;
        albumPageTitleBox.IsEnabled = enabled;
        albumPageFormatBox.IsEnabled = enabled;
        albumPlacementBox.IsEnabled = enabled;
        albumSectionBox.IsEnabled = enabled;
    }

    private void ApplyAlbumPageProperties()
    {
        if (bindingAlbumPage ||
            albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem selected ||
            selected.Page is not AlbumPageDefinition page)
        {
            return;
        }

        var sheet = state.Library.Find(page.SheetKey);
        page.NumberOverride = string.Equals(
            albumPageNumberBox.Text.Trim(),
            selected.AutomaticNumber,
            StringComparison.Ordinal)
            ? ""
            : albumPageNumberBox.Text.Trim();
        page.TitleOverride = string.Equals(albumPageTitleBox.Text.Trim(), sheet?.Entry.Name, StringComparison.Ordinal)
            ? ""
            : albumPageTitleBox.Text.Trim();
        if (albumPageFormatBox.SelectedItem is PageFormatDefinition format)
        {
            var selectedSourceSnapshot = page.PageFormatSnapshot is not null &&
                string.Equals(page.PageFormatSnapshot.Id, format.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    page.PageFormatSnapshot.GeometryHash,
                    format.GeometryHash,
                    StringComparison.OrdinalIgnoreCase);
            page.PageFormatId = format.Id;
            if (!selectedSourceSnapshot)
            {
                page.PageFormatSnapshot = null;
                page.FollowSourceFormat = false;
            }
        }

        if (albumPlacementBox.SelectedItem is PlacementChoice placement)
        {
            if (page.PlacementMode != placement.Value)
            {
                page.FollowSourceFormat = false;
            }
            page.PlacementMode = placement.Value;
        }

        page.SectionId = (albumSectionBox.SelectedItem as SectionChoice)?.Id;
        RefreshAlbumPagePreview();
    }

    private void ApplyAlbumOptions()
    {
        if (bindingAlbumPage)
        {
            return;
        }

        state.Album.IncludeCover = includeCoverCheck.IsChecked == true;
        state.Album.IncludeTableOfContents = includeTocCheck.IsChecked == true;
    }

    private void RemoveSelectedAlbumPage()
    {
        if (albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem selected ||
            selected.Page is not AlbumPageDefinition page)
        {
            return;
        }

        var index = state.Album.Pages.IndexOf(page);
        state.Album.Pages.Remove(page);
        var next = state.Album.Pages.Count == 0
            ? (Guid?)null
            : state.Album.Pages[Math.Min(index, state.Album.Pages.Count - 1)].Id;
        RefreshAlbumWorkspace(next);
    }

    private void MoveSelectedAlbumPage(int offset)
    {
        if (albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem selected ||
            selected.Page is not AlbumPageDefinition page)
        {
            return;
        }

        var oldIndex = state.Album.Pages.IndexOf(page);
        var newIndex = Math.Clamp(oldIndex + offset, 0, state.Album.Pages.Count - 1);
        if (oldIndex == newIndex)
        {
            return;
        }

        state.Album.Pages.RemoveAt(oldIndex);
        state.Album.Pages.Insert(newIndex, page);
        RefreshAlbumWorkspace(page.Id);
    }

    private void RefreshAlbumPagePreview()
    {
        albumPreviewHost.Children.Clear();
        if (albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem selected)
        {
            albumPreviewHost.Children.Add(new TextBlock
            {
                Text = "Альбумд sheet нэмнэ үү",
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
            return;
        }

        if (!string.IsNullOrWhiteSpace(lastAlbumPath) &&
            File.Exists(lastAlbumPath) &&
            (selected.BuiltPageNumber ?? ResolveBuiltAlbumPage(selected)) is int builtPage)
        {
            ShowAlbumPdfPage(lastAlbumPath, builtPage);
            return;
        }

        if (selected.Page is not AlbumPageDefinition sourcePage)
        {
            ShowCompositionPreview(selected);
            return;
        }

        var sheet = state.Library.Find(sourcePage.SheetKey);
        var format = PageFormatCatalog.Resolve(sourcePage);
        var width = format.Kind == PageFormatKind.SourceAsIs
            ? Math.Max(210, sheet?.Entry.WidthMm ?? 420)
            : format.WidthMm;
        var height = format.Kind == PageFormatKind.SourceAsIs
            ? Math.Max(148, sheet?.Entry.HeightMm ?? 297)
            : format.HeightMm;
        var canvas = new Canvas { Width = width, Height = height, Background = Brushes.White };

        if (format.Kind == PageFormatKind.SourceAsIs)
        {
            AddPreviewText(canvas, "Эх PDF", 0, height * 0.42, width, 22, 14, FontWeights.SemiBold, Brushes.Black);
            AddPreviewText(canvas, sheet?.DisplayLabel ?? selected.Title, width * 0.1, height * 0.52, width * 0.8, 24, 10, FontWeights.Normal, Brushes.DimGray);
        }
        else if (BuildingArchitectureConceptPageLayout.IsCanonical(format))
        {
            var drawing = AddPreviewRectangle(canvas, format.DrawingArea, Brushes.WhiteSmoke, Brushes.LightGray);
            drawing.StrokeDashArray = new DoubleCollection { 2, 1 };
            AddPreviewText(canvas, sheet?.DisplayLabel ?? selected.Title,
                format.DrawingArea.X + format.DrawingArea.Width * 0.08,
                format.DrawingArea.Y + format.DrawingArea.Height * 0.44,
                format.DrawingArea.Width * 0.84,
                30,
                10,
                FontWeights.SemiBold,
                Brushes.DimGray);
            var pageTitle = string.IsNullOrWhiteSpace(sourcePage.TitleOverride)
                ? sheet?.Entry.Name ?? selected.Title
                : sourcePage.TitleOverride;
            var pageNumber = selected.Number;
            AddConceptSheetPreviewChrome(canvas, pageTitle, pageNumber);
        }
        else
        {
            var drawing = AddPreviewRectangle(canvas, format.DrawingArea, Brushes.WhiteSmoke, Brushes.DimGray);
            drawing.StrokeDashArray = new DoubleCollection { 2, 1 };
            AddPreviewText(canvas, sheet?.DisplayLabel ?? selected.Title,
                format.DrawingArea.X + format.DrawingArea.Width * 0.08,
                format.DrawingArea.Y + format.DrawingArea.Height * 0.44,
                format.DrawingArea.Width * 0.84,
                30,
                10,
                FontWeights.SemiBold,
                Brushes.DimGray);

            var title = AddPreviewRectangle(canvas, format.TitleBlockArea, Brushes.White, Brushes.Black);
            title.StrokeThickness = 1;
            AddPreviewTitleBlock(canvas, format.TitleBlockArea, selected.Number);
        }

        ShowAlbumPreviewCanvas(canvas);
    }

    private int? ResolveBuiltAlbumPage(AlbumPageWorkspaceItem selected)
    {
        var project = state.CreateAlbumBuildProject();
        var generated = project.Album.Composition
            .Where(item => item.Kind == AlbumCompositionKind.Generated)
            .OrderBy(item => item.Order)
            .ToList();

        if (selected.Component?.Kind == AlbumCompositionKind.Generated)
        {
            var generatedIndex = generated.FindIndex(item => string.Equals(
                item.Id,
                selected.Component.Id,
                StringComparison.OrdinalIgnoreCase));
            return generatedIndex < 0 ? null : generatedIndex + 1;
        }

        if (selected.Page is not AlbumPageDefinition selectedPage)
        {
            return null;
        }

        var pageNumber = generated.Count;
        if (generated.Count == 0 && project.Album.IncludeCover)
        {
            pageNumber++;
        }
        if (project.Album.IncludeTableOfContents)
        {
            pageNumber++;
        }

        var request = AlbumBuilder.CreateRequest(project, state.Library);
        foreach (var buildPage in request.Sections.SelectMany(section => section.Pages))
        {
            if (buildPage.Definition.Id == selectedPage.Id)
            {
                return File.Exists(buildPage.Sheet.PdfPath) ? pageNumber + 1 : null;
            }

            if (File.Exists(buildPage.Sheet.PdfPath))
            {
                pageNumber += Math.Max(1, buildPage.Sheet.Entry.PageCount);
            }
        }

        return null;
    }

    private async void ShowAlbumPdfPage(string pdfPath, int pageNumber)
    {
        var navigationSerial = ++albumPdfNavigationSerial;
        var targetPdfPath = Path.GetFullPath(pdfPath);
        var targetPage = Math.Max(1, pageNumber);

        albumPreviewHost.Children.Clear();
        if (albumPdfViewer.Parent is Panel currentParent)
        {
            currentParent.Children.Remove(albumPdfViewer);
        }
        albumPreviewHost.Children.Add(albumPdfViewer);

        try
        {
            await albumPdfViewer.EnsureCoreWebView2Async();
            if (navigationSerial != albumPdfNavigationSerial)
            {
                return;
            }
            if (!albumPdfViewerConfigured)
            {
                albumPdfViewer.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                albumPdfViewer.CoreWebView2.Settings.AreDevToolsEnabled = false;
                albumPdfViewer.CoreWebView2.Settings.IsStatusBarEnabled = false;
                albumPdfViewer.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                albumPdfViewerConfigured = true;
            }

            if (!File.Exists(targetPdfPath))
            {
                return;
            }

            var pdf = new FileInfo(targetPdfPath);
            var documentKey = $"{pdf.FullName}|{pdf.LastWriteTimeUtc.Ticks}|{pdf.Length}";
            var builder = new UriBuilder(new Uri(targetPdfPath))
            {
                Query = $"erksVersion={pdf.LastWriteTimeUtc.Ticks}-{pdf.Length}",
                Fragment = $"page={targetPage}&zoom=page-fit",
            };

            if (string.Equals(loadedAlbumPdfDocumentKey, documentKey, StringComparison.Ordinal))
            {
                var mainWindow = System.Windows.Application.Current.MainWindow;
                var windowHandle = mainWindow is null ? IntPtr.Zero : new WindowInteropHelper(mainWindow).Handle;
                for (var attempt = 0; attempt < 8; attempt++)
                {
                    if (navigationSerial != albumPdfNavigationSerial)
                    {
                        return;
                    }

                    if (windowHandle != IntPtr.Zero && await Task.Run(() =>
                            TrySelectLoadedPdfPage(windowHandle, targetPage)))
                    {
                        await Task.Delay(40);
                        if (navigationSerial == albumPdfNavigationSerial)
                        {
                            albumPagesWorkspaceList.Focus();
                        }
                        return;
                    }
                    await Task.Delay(75);
                }
            }

            loadedAlbumPdfDocumentKey = documentKey;
            albumPdfViewer.CoreWebView2.Navigate(builder.Uri.AbsoluteUri);
        }
        catch (Exception exception)
        {
            albumPreviewHost.Children.Clear();
            albumPreviewHost.Children.Add(new TextBlock
            {
                Text = $"PDF харагдацыг нээж чадсангүй.\n{exception.Message}",
                Foreground = Brushes.White,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(24),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            });
        }
    }

    private static bool TrySelectLoadedPdfPage(IntPtr windowHandle, int pageNumber)
    {
        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            var selector = root.FindFirst(
                TreeScope.Descendants,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "pageselector"));
            if (selector is null ||
                !selector.TryGetCurrentPattern(ValuePattern.Pattern, out var valuePatternObject) ||
                valuePatternObject is not ValuePattern valuePattern)
            {
                return false;
            }

            SetForegroundWindow(windowHandle);
            selector.SetFocus();
            valuePattern.SetValue(Math.Max(1, pageNumber).ToString());
            keybd_event(VirtualKeyReturn, 0, 0, UIntPtr.Zero);
            keybd_event(VirtualKeyReturn, 0, KeyEventKeyUp, UIntPtr.Zero);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private const byte VirtualKeyReturn = 0x0D;
    private const uint KeyEventKeyUp = 0x0002;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    private static extern void keybd_event(
        byte virtualKey,
        byte scanCode,
        uint flags,
        UIntPtr extraInfo);

    private void ShowCompositionPreview(AlbumPageWorkspaceItem selected)
    {
        var canvas = new Canvas
        {
            Width = BuildingArchitectureConceptPageLayout.PageWidthMm,
            Height = BuildingArchitectureConceptPageLayout.PageHeightMm,
            Background = Brushes.White,
        };
        var component = selected.Component;
        if (component?.GeneratedPageKind == AlbumGeneratedPageKind.Cover)
        {
            AddConceptCoverPreview(canvas);
            ShowAlbumPreviewCanvas(canvas);
            return;
        }

        AddConceptSheetPreviewChrome(
            canvas,
            component?.Title ?? selected.Title,
            component?.Number ?? selected.Number);

        if (component?.Kind == AlbumCompositionKind.Generated)
        {
            var primary = component.GeneratedPageKind switch
            {
                AlbumGeneratedPageKind.DesignOrganization => state.Project.DesignOrganizationName,
                AlbumGeneratedPageKind.PlanningTask => state.Project.Foundation.PlanningTask.IssuingAuthorityName,
                _ => component.Title,
            };
            var secondary = component.GeneratedPageKind switch
            {
                AlbumGeneratedPageKind.DesignOrganization => state.Project.Foundation.DesignCompany.OrganizationSnapshot.Email,
                AlbumGeneratedPageKind.PlanningTask => $"АТД {ValueOrDash(state.Project.Foundation.PlanningTask.AtdNumber)} · {ValueOrDash(state.Project.Foundation.PlanningTask.Status)}",
                _ => "Studio",
            };
            var content = AddPreviewRectangle(
                canvas,
                new PageRectMm { X = 25, Y = 30, Width = 380, Height = 218 },
                Brushes.White,
                Brushes.LightGray);
            content.StrokeDashArray = new DoubleCollection { 2, 1 };
            AddPreviewText(canvas, primary, 45, 95, 340, 36, 18, FontWeights.Bold, Brushes.Black);
            AddPreviewText(canvas, secondary, 55, 139, 320, 30, 10, FontWeights.Normal, Brushes.DimGray);
            AddPreviewText(canvas, "STUDIO-Д ҮҮСНЭ", 155, 207, 110, 14, 8, FontWeights.Bold, StudioTheme.AccentBrush);
        }
        else
        {
            var waiting = AddPreviewRectangle(
                canvas,
                new PageRectMm { X = 35, Y = 45, Width = 350, Height = 180 },
                Brushes.WhiteSmoke,
                Brushes.Gray);
            waiting.StrokeDashArray = new DoubleCollection { 4, 2 };
            AddPreviewText(canvas, component?.Title ?? selected.Title, 55, 108, 310, 28, 16, FontWeights.Bold, Brushes.DimGray);
            AddPreviewText(canvas, "Эх үүсвэр хүлээж байна", 55, 143, 310, 22, 10, FontWeights.Normal, Brushes.Gray);
        }
        ShowAlbumPreviewCanvas(canvas);
    }

    private void AddConceptSheetPreviewChrome(Canvas canvas, string title, string number)
    {
        var frame = AddPreviewRectangle(canvas, BuildingArchitectureConceptPageLayout.Frame, Brushes.Transparent, Brushes.Black);
        frame.StrokeThickness = 0.9;
        AddPreviewLine(
            canvas,
            BuildingArchitectureConceptPageLayout.FrameLeftMm,
            BuildingArchitectureConceptPageLayout.SheetHeaderBottomMm,
            BuildingArchitectureConceptPageLayout.FrameRightMm,
            BuildingArchitectureConceptPageLayout.SheetHeaderBottomMm);
        AddPreviewText(
            canvas,
            title,
            20,
            6.5,
            390,
            6.5,
            7.5,
            FontWeights.Normal,
            Brushes.Black,
            TextAlignment.Right);

        var corner = AddPreviewRectangle(
            canvas,
            BuildingArchitectureConceptPageLayout.TitleBlockArea,
            Brushes.White,
            Brushes.Black);
        corner.StrokeThickness = 0.8;

        var x0 = BuildingArchitectureConceptPageLayout.CornerX0Mm;
        var x1 = BuildingArchitectureConceptPageLayout.CornerX1Mm;
        var x2 = BuildingArchitectureConceptPageLayout.CornerX2Mm;
        var x3 = BuildingArchitectureConceptPageLayout.CornerX3Mm;
        var x4 = BuildingArchitectureConceptPageLayout.CornerX4Mm;
        var x5 = BuildingArchitectureConceptPageLayout.CornerX5Mm;
        var y0 = BuildingArchitectureConceptPageLayout.CornerY0Mm;
        var y1 = BuildingArchitectureConceptPageLayout.CornerY1Mm;
        var y2 = BuildingArchitectureConceptPageLayout.CornerY2Mm;
        var y3 = BuildingArchitectureConceptPageLayout.CornerY3Mm;
        var y4 = BuildingArchitectureConceptPageLayout.CornerY4Mm;
        foreach (var x in new[] { x1, x2, x3, x4 })
        {
            AddPreviewLine(canvas, x, y0, x, y4);
        }
        foreach (var y in new[] { y1, y2, y3 })
        {
            AddPreviewLine(canvas, x1, y, x5, y);
        }

        var company = state.Project.Foundation.DesignCompany.OrganizationSnapshot;
        var representative = company.Signers.FirstOrDefault(signer =>
                                 signer.Role.Contains("захирал", StringComparison.OrdinalIgnoreCase))
                             ?? company.Signers.FirstOrDefault();
        var companyRole = representative?.Role ?? "Захирал";
        var companyName = state.Project.DesignOrganizationName;
        if (!string.IsNullOrWhiteSpace(companyName))
        {
            companyRole = $"\"{companyName}\" {companyRole}";
        }
        var architect = state.Project.Foundation.DesignCompany.Members
            .Where(member => member.Roles.Any(role => role.Contains("architect", StringComparison.OrdinalIgnoreCase)))
            .OrderBy(member => member.Roles.Any(role => role.Contains("Major", StringComparison.OrdinalIgnoreCase)) ? 0 : 1)
            .Select(member => member.FullName)
            .FirstOrDefault() ?? "";
        var companyMark = string.IsNullOrWhiteSpace(company.ShortName) ? company.Name : company.ShortName;

        AddPreviewText(canvas, ValueOrDash(companyMark), x0 + 2, y0 + 7, x1 - x0 - 4, 14, 6.5, FontWeights.Bold, Brushes.DimGray);
        AddPreviewCornerCell(canvas, state.Project.Name, x1, y0, x2, y1, TextAlignment.Left);
        AddPreviewCornerCell(canvas, "Нэр", x2, y0, x3, y1);
        AddPreviewCornerCell(canvas, "Гарын үсэг", x3, y0, x4, y1);
        AddPreviewCornerCell(canvas, "Загвар", x4, y0, x5, y1);
        AddPreviewCornerCell(canvas, companyRole, x1, y1, x2, y2, TextAlignment.Left);
        AddPreviewCornerCell(canvas, representative?.FullName ?? "", x2, y1, x3, y2);
        AddPreviewCornerCell(canvas, "Архитектор", x1, y2, x2, y3, TextAlignment.Left);
        AddPreviewCornerCell(canvas, architect, x2, y2, x3, y3);
        AddPreviewCornerCell(canvas, $"Хуудас-{ValueOrDash(number)}", x4, y2, x5, y3);
        AddPreviewCornerCell(canvas, "Захиалагч", x1, y3, x2, y4, TextAlignment.Left);
        AddPreviewCornerCell(canvas, ValueOrDash(state.Project.Foundation.InitiationBasis.ClientName), x2, y3, x3, y4);
        AddPreviewCornerCell(canvas, $"{DateTime.Now:yyyy} он", x4, y3, x5, y4);
    }

    private static void AddPreviewCornerCell(
        Canvas canvas,
        string text,
        double x0,
        double y0,
        double x1,
        double y1,
        TextAlignment alignment = TextAlignment.Center)
    {
        AddPreviewText(canvas, text, x0 + 1, y0 + 0.8, x1 - x0 - 2, y1 - y0 - 1.6, 5.5, FontWeights.Normal, Brushes.Black, alignment);
    }

    private void AddConceptCoverPreview(Canvas canvas)
    {
        var boundary = AddPreviewRectangle(
            canvas,
            BuildingArchitectureConceptPageLayout.Frame,
            Brushes.White,
            Brushes.Black);
        boundary.StrokeThickness = 0.6;

        var members = state.Project.Foundation.PlanningTask.AuthorityMembers;
        var approved = members.FirstOrDefault(member => HasProjectRole(member, "Chief Architect"));
        var approvals = members
            .Where(member => !ReferenceEquals(member, approved))
            .Where(member => member.Roles.Count > 0 || !string.IsNullOrWhiteSpace(member.FullName))
            .Take(8)
            .ToList();
        var reviewRowCount = Math.Clamp(approvals.Count, 1, 8);
        const double bodyTextHeightMm = BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm;
        const double projectNameTextHeightMm = BuildingArchitectureConceptPageLayout.CoverProjectNameTextHeightMm;
        var approvedRole = (approved is null ? "Ерөнхий архитектор" : DisplayProjectRoles(approved)).ToUpperInvariant();
        var approvedName = (approved?.FullName ?? "").ToUpperInvariant();
        var approvedRowHeightMm = Math.Max(
            8.0,
            Math.Max(
                MeasureCoverPreviewTextHeightMm(approvedRole, 120, bodyTextHeightMm),
                MeasureCoverPreviewTextHeightMm(approvedName, 75, bodyTextHeightMm)) + 1.2);
        const double approvedRowTopMm = 262.205;

        AddCoverPreviewText(canvas, "БАТЛАВ:", BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 281.205, 50, 8), bodyTextHeightMm, FontWeights.Normal);
        AddCoverPreviewText(canvas, approvedRole,
            BuildingArchitectureConceptPageLayout.FromBottomLeft(105.8, approvedRowTopMm - approvedRowHeightMm, 225.8, approvedRowTopMm), bodyTextHeightMm, FontWeights.Normal, TextAlignment.Left);
        AddCoverPreviewText(canvas, approvedName,
            BuildingArchitectureConceptPageLayout.FromBottomLeft(277.4, approvedRowTopMm - approvedRowHeightMm, 352.4, approvedRowTopMm), bodyTextHeightMm, FontWeights.Normal, TextAlignment.Left);

        AddCoverPreviewText(canvas, ValueOrDash(state.Project.Foundation.InitiationBasis.SiteAddress),
            BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 220.510, 180, 8), bodyTextHeightMm, FontWeights.Normal);
        AddCoverPreviewText(canvas, state.Project.Name,
            BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 207.510, 220, 12), projectNameTextHeightMm, FontWeights.Normal);
        AddCoverPreviewText(canvas, "/ЗАГВАР ЗУРАГ/",
            BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 186.760, 110, 8), bodyTextHeightMm, FontWeights.Normal);
        AddCoverPreviewText(canvas, "ЗӨВШӨӨРӨЛЦСӨН:",
            BuildingArchitectureConceptPageLayout.FromBottomLeft(68.275, 162.36, 196.275, 168.86), bodyTextHeightMm, FontWeights.Normal, TextAlignment.Left);
        AddCoverPreviewText(canvas, "БОЛОВСРУУЛСАН:",
            BuildingArchitectureConceptPageLayout.FromBottomLeft(196.275, 162.36, 351.725, 168.86), bodyTextHeightMm, FontWeights.Normal, TextAlignment.Left);

        const double reviewRowsTopMm = 153.86;
        const double reviewRowsBaseHeightMm = 60.0;
        const double cellVerticalPaddingMm = 1.2;
        var reviewRows = new List<(ProjectMember? Member, double BottomMm, double TopMm)>(reviewRowCount);
        var reviewRowTopMm = reviewRowsTopMm;
        var reviewBaseRowHeightMm = reviewRowsBaseHeightMm / reviewRowCount;
        for (var index = 0; index < reviewRowCount; index++)
        {
            var member = index < approvals.Count ? approvals[index] : null;
            var roleHeightMm = MeasureCoverPreviewTextHeightMm(
                member is null ? "" : DisplayProjectRoles(member),
                66.0,
                bodyTextHeightMm);
            var nameHeightMm = MeasureCoverPreviewTextHeightMm(
                member?.FullName ?? "",
                25.6,
                bodyTextHeightMm);
            var rowHeightMm = Math.Max(
                reviewBaseRowHeightMm,
                Math.Max(roleHeightMm, nameHeightMm) + cellVerticalPaddingMm);
            var reviewRowBottomMm = reviewRowTopMm - rowHeightMm;
            reviewRows.Add((member, reviewRowBottomMm, reviewRowTopMm));
            reviewRowTopMm = reviewRowBottomMm;
        }

        var company = state.Project.Foundation.DesignCompany.OrganizationSnapshot;
        var representative = company.Signers.FirstOrDefault(signer =>
                                 signer.Role.Contains("захирал", StringComparison.OrdinalIgnoreCase))
                             ?? company.Signers.FirstOrDefault();
        var companyRole = representative?.Role ?? "Захирал";
        if (!string.IsNullOrWhiteSpace(state.Project.DesignOrganizationName))
        {
            companyRole = $"\"{state.Project.DesignOrganizationName}\" {companyRole}";
        }

        var representativeName = representative?.FullName ?? "";
        var clientName = ValueOrDash(state.Project.Foundation.InitiationBasis.ClientName);
        const double clientTitleBottomMm = 153.86;
        var clientDataHeightMm = Math.Max(
            16.0,
            Math.Max(
                MeasureCoverPreviewTextHeightMm("Иргэн", 64.3, bodyTextHeightMm),
                MeasureCoverPreviewTextHeightMm(clientName, 26.35, bodyTextHeightMm)) + cellVerticalPaddingMm);
        var clientDataBottomMm = clientTitleBottomMm - clientDataHeightMm;
        var companyTitleBottomMm = clientDataBottomMm - 8.0;
        var companyHeaderBottomMm = companyTitleBottomMm - 8.0;
        var companyDataHeightMm = Math.Max(
            20.0,
            Math.Max(
                MeasureCoverPreviewTextHeightMm(companyRole, 64.3, bodyTextHeightMm),
                MeasureCoverPreviewTextHeightMm(representativeName, 26.35, bodyTextHeightMm)) + cellVerticalPaddingMm);
        var companyColumnBottomMm = companyHeaderBottomMm - companyDataHeightMm;
        var tableBottomMm = Math.Min(reviewRows[^1].BottomMm, companyColumnBottomMm);

        var table = AddPreviewRectangle(
            canvas,
            BuildingArchitectureConceptPageLayout.FromBottomLeft(68.275, tableBottomMm, 351.725, 161.86),
            Brushes.White,
            Brushes.Black);
        table.StrokeThickness = 0.7;
        AddPreviewBottomLine(canvas, 68.275, 153.86, 351.725, 153.86);
        AddPreviewBottomLine(canvas, 196.275, tableBottomMm, 196.275, 161.86);
        AddPreviewBottomLine(canvas, 138.275, tableBottomMm, 138.275, 161.86);
        AddPreviewBottomLine(canvas, 166.275, tableBottomMm, 166.275, 161.86);
        AddPreviewBottomLine(canvas, 226.275, clientDataBottomMm, 226.275, 161.86);
        AddPreviewBottomLine(canvas, 292.975, clientDataBottomMm, 292.975, 161.86);
        AddPreviewBottomLine(canvas, 321.725, clientDataBottomMm, 321.725, 161.86);
        AddPreviewBottomLine(canvas, 226.275, tableBottomMm, 226.275, companyTitleBottomMm);
        AddPreviewBottomLine(canvas, 292.975, tableBottomMm, 292.975, companyTitleBottomMm);
        AddPreviewBottomLine(canvas, 321.725, tableBottomMm, 321.725, companyTitleBottomMm);
        AddPreviewBottomLine(canvas, 196.275, clientDataBottomMm, 351.725, clientDataBottomMm);
        AddPreviewBottomLine(canvas, 196.275, companyTitleBottomMm, 351.725, companyTitleBottomMm);
        AddPreviewBottomLine(canvas, 226.275, companyHeaderBottomMm, 351.725, companyHeaderBottomMm);

        for (var index = 0; index < reviewRows.Count - 1; index++)
        {
            AddPreviewBottomLine(canvas, 68.275, reviewRows[index].BottomMm, 196.275, reviewRows[index].BottomMm);
        }

        AddCoverPreviewCell(canvas, "Албан тушаал", 68.275, 153.86, 138.275, 161.86);
        AddCoverPreviewCell(canvas, "Нэр", 138.275, 153.86, 166.275, 161.86);
        AddCoverPreviewCell(canvas, "Гарын үсэг", 166.275, 153.86, 196.275, 161.86);
        AddCoverPreviewCell(canvas, "Албан тушаал", 226.275, 153.86, 292.975, 161.86);
        AddCoverPreviewCell(canvas, "Нэр", 292.975, 153.86, 321.725, 161.86);
        AddCoverPreviewCell(canvas, "Гарын үсэг", 321.725, 153.86, 351.725, 161.86);

        foreach (var row in reviewRows)
        {
            AddCoverPreviewCell(canvas, row.Member is null ? "" : DisplayProjectRoles(row.Member), 68.275, row.BottomMm, 138.275, row.TopMm, TextAlignment.Left);
            AddCoverPreviewCell(canvas, row.Member?.FullName ?? "", 138.275, row.BottomMm, 166.275, row.TopMm);
        }

        var companyMark = string.IsNullOrWhiteSpace(company.ShortName) ? company.Name : company.ShortName;
        AddCoverPreviewCell(canvas, "Захиалагч", 196.275, clientTitleBottomMm, 226.275, 161.86);
        AddCoverPreviewCell(canvas, "Иргэн", 226.275, clientDataBottomMm, 292.975, clientTitleBottomMm);
        AddCoverPreviewCell(canvas, clientName, 292.975, clientDataBottomMm, 321.725, clientTitleBottomMm);

        AddCoverPreviewCell(canvas, "Гүйцэтгэсэн", 196.275, companyTitleBottomMm, 351.725, clientDataBottomMm, TextAlignment.Left);
        AddCoverPreviewCell(canvas, ValueOrDash(companyMark), 196.275, tableBottomMm, 226.275, companyTitleBottomMm);
        AddCoverPreviewCell(canvas, "Албан тушаал", 226.275, companyHeaderBottomMm, 292.975, companyTitleBottomMm);
        AddCoverPreviewCell(canvas, "Нэр", 292.975, companyHeaderBottomMm, 321.725, companyTitleBottomMm);
        AddCoverPreviewCell(canvas, "Гарын үсэг", 321.725, companyHeaderBottomMm, 351.725, companyTitleBottomMm);
        AddCoverPreviewCell(canvas, companyRole, 226.275, tableBottomMm, 292.975, companyHeaderBottomMm);
        AddCoverPreviewCell(canvas, representativeName, 292.975, tableBottomMm, 321.725, companyHeaderBottomMm);

        AddCoverPreviewText(canvas, "Улаанбаатар хот",
            BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 26.125, 200, 12), bodyTextHeightMm, FontWeights.Normal);
        AddCoverPreviewText(canvas, $"{DateTime.Now:yyyy} он",
            BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 15.625, 90, 12), bodyTextHeightMm, FontWeights.Normal);
    }

    private static void AddPreviewBottomLine(Canvas canvas, double x0, double y0, double x1, double y1)
    {
        AddPreviewLine(
            canvas,
            x0,
            BuildingArchitectureConceptPageLayout.PageHeightMm - y0,
            x1,
            BuildingArchitectureConceptPageLayout.PageHeightMm - y1);
    }

    private static void AddCoverPreviewCell(
        Canvas canvas,
        string text,
        double x0,
        double y0,
        double x1,
        double y1,
        TextAlignment alignment = TextAlignment.Center)
    {
        AddCoverPreviewText(
            canvas,
            text,
            BuildingArchitectureConceptPageLayout.FromBottomLeft(x0, y0, x1, y1),
            BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm,
            FontWeights.Normal,
            alignment);
    }

    private static void AddCoverPreviewText(
        Canvas canvas,
        string text,
        PageRectMm rect,
        double printedTextHeightMm,
        FontWeight weight,
        TextAlignment alignment = TextAlignment.Center)
    {
        AddPreviewText(
            canvas,
            text,
            rect.X + 1,
            rect.Y + 0.5,
            Math.Max(1, rect.Width - 2),
            Math.Max(1, rect.Height - 1),
            CoverPreviewFontEmSizeMm(printedTextHeightMm),
            weight,
            Brushes.Black,
            alignment);
    }

    private static double MeasureCoverPreviewTextHeightMm(
        string text,
        double widthMm,
        double printedTextHeightMm)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(
                new FontFamily(BuildingArchitectureConceptPageLayout.FontFamilyName),
                FontStyles.Normal,
                FontWeights.Normal,
                FontStretches.Normal),
            CoverPreviewFontEmSizeMm(printedTextHeightMm),
            Brushes.Black,
            1.0)
        {
            MaxTextWidth = Math.Max(1, widthMm),
        };
        return formatted.Height;
    }

    private static double CoverPreviewFontEmSizeMm(double printedTextHeightMm) =>
        printedTextHeightMm / BuildingArchitectureConceptPageLayout.ArialCapHeightRatio;

    private static bool HasProjectRole(ProjectMember member, string role) =>
        member.Roles.Any(candidate => candidate.Contains(role, StringComparison.OrdinalIgnoreCase));

    private static string DisplayProjectRoles(ProjectMember member) =>
        string.Join(", ", member.Roles.Select(DisplayProjectRole).Distinct(StringComparer.OrdinalIgnoreCase));

    private static string DisplayProjectRole(string role)
    {
        if (role.Contains("Chief Architect", StringComparison.OrdinalIgnoreCase))
        {
            return "Ерөнхий архитектор";
        }
        if (role.Contains("Department Head", StringComparison.OrdinalIgnoreCase))
        {
            return "Хэлтсийн дарга";
        }
        if (role.Contains("Authority Specialist", StringComparison.OrdinalIgnoreCase))
        {
            return "Хот байгуулалтын мэргэжилтэн";
        }
        return role;
    }

    private void ShowAlbumPreviewCanvas(Canvas canvas)
    {
        albumPreviewHost.Children.Add(new Viewbox
        {
            Child = new Border
            {
                Child = canvas,
                BorderBrush = Brushes.Black,
                BorderThickness = new Thickness(0.7),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 10,
                    ShadowDepth = 3,
                    Opacity = 0.35,
                },
            },
            Stretch = Stretch.Uniform,
            Margin = new Thickness(28),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        });
    }

    private void AddPreviewTitleBlock(Canvas canvas, PageRectMm rect, string pageNumber)
    {
        var first = rect.Y + rect.Height * 0.34;
        var second = rect.Y + rect.Height * 0.66;
        AddPreviewLine(canvas, rect.X, first, rect.X + rect.Width, first);
        AddPreviewLine(canvas, rect.X, second, rect.X + rect.Width, second);
        AddPreviewText(canvas,
            state.Project.DesignOrganizationName,
            rect.X + 2,
            rect.Y + 1,
            rect.Width - 4,
            Math.Max(8, first - rect.Y - 2),
            7,
            FontWeights.SemiBold,
            Brushes.Black);
        AddPreviewText(canvas,
            pageNumber,
            rect.X + 2,
            second + 1,
            rect.Width - 4,
            Math.Max(8, rect.Y + rect.Height - second - 2),
            8,
            FontWeights.Bold,
            Brushes.Black);
    }

    private static System.Windows.Shapes.Rectangle AddPreviewRectangle(
        Canvas canvas,
        PageRectMm rect,
        Brush fill,
        Brush stroke)
    {
        var shape = new System.Windows.Shapes.Rectangle
        {
            Width = rect.Width,
            Height = rect.Height,
            Fill = fill,
            Stroke = stroke,
            StrokeThickness = 0.7,
        };
        Canvas.SetLeft(shape, rect.X);
        Canvas.SetTop(shape, rect.Y);
        canvas.Children.Add(shape);
        return shape;
    }

    private static void AddPreviewLine(Canvas canvas, double x1, double y1, double x2, double y2)
    {
        canvas.Children.Add(new System.Windows.Shapes.Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = Brushes.Black,
            StrokeThickness = 0.55,
        });
    }

    private static void AddPreviewText(
        Canvas canvas,
        string text,
        double x,
        double y,
        double width,
        double height,
        double fontSize,
        FontWeight weight,
        Brush foreground,
        TextAlignment textAlignment = TextAlignment.Center)
    {
        var block = new TextBlock
        {
            Text = text,
            Width = Math.Max(1, width),
            Height = Math.Max(1, height),
            FontFamily = new FontFamily(BuildingArchitectureConceptPageLayout.FontFamilyName),
            FontSize = fontSize,
            FontWeight = weight,
            Foreground = foreground,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextAlignment = textAlignment,
        };
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y);
        canvas.Children.Add(block);
    }

    private static Border BuildPane(string title, UIElement content, Thickness borderThickness) =>
        BuildPane(
            new TextBlock
            {
                Text = title,
                FontWeight = FontWeights.SemiBold,
                Foreground = StudioTheme.TextBrush,
            },
            content,
            borderThickness);

    private static Border BuildPane(UIElement headerContent, UIElement content, Thickness borderThickness)
    {
        var dock = new DockPanel();
        var header = new Border
        {
            Background = StudioTheme.PanelAltBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 7, 10, 7),
            Child = headerContent,
        };
        DockPanel.SetDock(header, Dock.Top);
        dock.Children.Add(header);
        var contentBorder = new Border { Padding = new Thickness(8), Child = content };
        dock.Children.Add(contentBorder);
        return new Border
        {
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = borderThickness,
            Child = dock,
        };
    }

    private static WrapPanel CreateRibbon()
    {
        return new WrapPanel
        {
            Background = StudioTheme.PanelAltBrush,
        };
    }

    private static WrapPanel CreateRibbonGroup(string label)
    {
        var group = new WrapPanel
        {
            Margin = new Thickness(10, 7, 12, 5),
            MinWidth = 120,
        };
        group.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 9,
            Foreground = StudioTheme.FaintTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 4),
        });
        return group;
    }

    private sealed record SourceWorkspaceItem(ProjectDesignSource Source, string Name, string Detail)
    {
        public override string ToString() => $"{Name}\n{Detail}";
    }

    private sealed record SheetWorkspaceItem(
        SheetRecord Record,
        string Number,
        string Name,
        string Application,
        string Size,
        string Status);

    private enum AlbumWorkspaceNodeKind
    {
        Page,
        Album,
        Studio,
        GeneralPlan,
        Source,
        DrawingType,
    }

    private sealed class AlbumWorkspaceNode
    {
        public required string Key { get; init; }
        public required AlbumWorkspaceNodeKind Kind { get; init; }
        public required string Title { get; init; }
        public AlbumPageWorkspaceItem? PageItem { get; init; }
        public List<AlbumWorkspaceNode> Children { get; } = [];
    }

    private sealed record AlbumPageWorkspaceItem(
        AlbumPageDefinition? Page,
        AlbumCompositionItem? Component,
        string Number,
        string AutomaticNumber,
        string Title,
        string Status,
        string GroupLabel) : INotifyPropertyChanged
    {
        private ImageSource? thumbnailSource;
        private string thumbnailMessage = "Уншиж байна";

        public event PropertyChangedEventHandler? PropertyChanged;

        public AlbumWorkspaceNodeKind Kind { get; init; } = AlbumWorkspaceNodeKind.Page;
        public string NodeKey { get; init; } = "";
        public int Depth { get; init; }
        public int ChildCount { get; init; }
        public bool IsExpanded { get; init; } = true;
        public int? BuiltPageNumber { get; set; }
        public ImageSource? ThumbnailSource => thumbnailSource;
        public string ThumbnailMessage => thumbnailMessage;

        public bool IsGroup => Kind != AlbumWorkspaceNodeKind.Page;
        public string SelectionKey => NodeKey;
        public string ExpansionGlyph => IsExpanded ? "▾" : "▸";
        public string ChildCountLabel => ChildCount > 0 ? $"({ChildCount})" : "";
        public Thickness Indent => new(Depth * 14.0, 0, 0, 0);
        public void SetThumbnail(ImageSource? source, string message)
        {
            thumbnailSource = source;
            thumbnailMessage = message;
            OnPropertyChanged(nameof(ThumbnailSource));
            OnPropertyChanged(nameof(ThumbnailMessage));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

        public override string ToString() => IsGroup ? Title : $"{Number}  {Title}";
    }

    private sealed record PlacementChoice(PagePlacementMode Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SectionChoice(Guid? Id, string Label)
    {
        public override string ToString() => Label;
    }
}
