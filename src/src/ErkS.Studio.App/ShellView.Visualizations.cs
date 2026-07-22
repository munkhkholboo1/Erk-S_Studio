using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using Microsoft.Win32;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private const string VisualizationSourceSelectionKey = "studio-visualizations";

    private readonly ListView visualizationImagesWorkspaceList = new()
    {
        SelectionMode = SelectionMode.Extended,
        Visibility = Visibility.Collapsed,
    };
    private readonly StackPanel visualizationSourceControls = new()
    {
        Visibility = Visibility.Collapsed,
        Margin = new Thickness(0, 8, 0, 0),
    };
    private readonly ComboBox visualizationImagesPerPageBox = new()
    {
        Width = 150,
        HorizontalAlignment = HorizontalAlignment.Left,
    };
    private readonly Button addVisualizationImagesButton = StudioWidgets.CreateGlyphTextButton(
        "\uE8E5",
        "Зураг нэмэх",
        "PNG болон JPEG зураг олноор сонгоно");
    private readonly Button relinkVisualizationImageButton = StudioWidgets.CreateGlyphTextButton(
        "\uE71B",
        "Эх файлыг дахин заах",
        "Сонгосон зургийн source link-ийг page ID болон байрлалыг нь хадгалан шинэчлэх");
    private readonly Button excludeVisualizationImagesButton = StudioWidgets.CreateGlyphTextButton(
        "\uE74D",
        "Хуудаснаас хасах",
        "Сонгосон зургуудыг эх үүсвэрт нь хадгалж, альбумд идэвхгүй болгоно");
    private readonly Button includeVisualizationImagesButton = StudioWidgets.CreateGlyphTextButton(
        "\uE72A",
        "Хуудсанд оруулах",
        "Сонгосон идэвхгүй зургуудыг альбумын хуудаслалтад буцаан оруулна");
    private bool bindingVisualizationSource;
    private CancellationTokenSource? visualizationThumbnailLoadCancellation;

    private ProjectVisualizationSource CurrentProjectVisualizationSource()
    {
        ProjectVisualizationSource source = state.Project.Visualizations;
        source.Normalize(state.Project.ProjectId);
        return source;
    }

    private IReadOnlyList<ProjectVisualizationImage> CurrentProjectVisualizationImages() =>
        CurrentProjectVisualizationSource().ImagesForProject(state.Project.ProjectId);

    private void ConfigureVisualizationSourceForCurrentProject()
    {
        if (!EnsureProjectContentPermission())
            return;

        ProjectVisualizationSource source = state.Project.Visualizations;
        source.ConfigureForProject(state.Project.ProjectId);
        state.SaveProject();
        RefreshSourceWorkspace(VisualizationSourceSelectionKey);
        SetStatus("Харагдах байдлын эх үүсвэр энэ төсөлд үүслээ. PNG/JPEG зургаа нэмнэ үү.");
    }

    private void ConfigureVisualizationImagesList()
    {
        visualizationImagesWorkspaceList.BorderThickness = new Thickness(0);
        visualizationImagesWorkspaceList.Background = StudioTheme.InputBrush;
        visualizationImagesWorkspaceList.Foreground = StudioTheme.TextBrush;
        visualizationImagesWorkspaceList.SelectionChanged += (_, _) =>
        {
            UpdateVisualizationActionState();
        };

        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.TextBrush));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 5, 5, 5)));
        visualizationImagesWorkspaceList.ItemContainerStyle = itemStyle;

        var view = new GridView();
        var headerStyle = new Style(typeof(GridViewColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, StudioTheme.PanelAltBrush));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.MutedTextBrush));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
        view.ColumnHeaderContainerStyle = headerStyle;
        view.Columns.Add(new GridViewColumn
        {
            Header = "Preview",
            Width = 104,
            CellTemplate = CreateVisualizationThumbnailTemplate(),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Файл",
            Width = 230,
            DisplayMemberBinding = new Binding(nameof(VisualizationImageWorkspaceItem.FileName)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Чиглэл",
            Width = 90,
            DisplayMemberBinding = new Binding(nameof(VisualizationImageWorkspaceItem.Orientation)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Хэмжээ",
            Width = 120,
            DisplayMemberBinding = new Binding(nameof(VisualizationImageWorkspaceItem.Dimensions)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Төлөв",
            Width = 128,
            DisplayMemberBinding = new Binding(nameof(VisualizationImageWorkspaceItem.Status)),
        });
        visualizationImagesWorkspaceList.View = view;
    }

    private static DataTemplate CreateVisualizationThumbnailTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.WidthProperty, 88d);
        border.SetValue(Border.HeightProperty, 60d);
        border.SetValue(Border.BackgroundProperty, StudioTheme.PanelAltBrush);
        border.SetValue(Border.PaddingProperty, new Thickness(2));

        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new Binding(nameof(VisualizationImageWorkspaceItem.Thumbnail)));
        image.SetValue(Image.StretchProperty, Stretch.Uniform);
        image.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
        border.AppendChild(image);
        return new DataTemplate(typeof(VisualizationImageWorkspaceItem)) { VisualTree = border };
    }

    private UIElement BuildVisualizationSourceControls()
    {
        visualizationSourceControls.Children.Add(StudioWidgets.CreateSectionHeader("Хуудаслалт"));
        visualizationImagesPerPageBox.ItemsSource = Enumerable.Range(
                1,
                VisualizationPageLayoutPlanner.MaximumImagesPerPage)
            .Select(value => new ImagesPerPageChoice(value))
            .ToList();
        visualizationImagesPerPageBox.SelectionChanged += (_, _) => ChangeVisualizationImagesPerPage();
        visualizationSourceControls.Children.Add(StudioWidgets.CreateFormRow(
            "Нэг хуудсанд",
            visualizationImagesPerPageBox));

        var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 4) };
        addVisualizationImagesButton.Click += (_, _) => AddVisualizationImages();
        relinkVisualizationImageButton.Click += (_, _) => RelinkSelectedVisualizationImage();
        excludeVisualizationImagesButton.Click += (_, _) => ExcludeSelectedVisualizationImages();
        includeVisualizationImagesButton.Click += (_, _) => IncludeSelectedVisualizationImages();
        actions.Children.Add(addVisualizationImagesButton);
        actions.Children.Add(relinkVisualizationImageButton);
        actions.Children.Add(excludeVisualizationImagesButton);
        actions.Children.Add(includeVisualizationImagesButton);
        visualizationSourceControls.Children.Add(actions);
        visualizationSourceControls.Children.Add(StudioWidgets.CreateHint(
            "Зургийн харьцааг хадгална. Бага төвийн тайралт боломжтой; их тайралт шаардвал зураг бүтнээрээ багтана."));
        return visualizationSourceControls;
    }

    private void RefreshVisualizationSourceDetails()
    {
        ProjectVisualizationSource source = CurrentProjectVisualizationSource();
        IReadOnlyList<ProjectVisualizationImage> images = CurrentProjectVisualizationImages();
        int includedImageCount = images.Count(image => image.IsAvailable && image.IsIncludedInAlbum);
        int inactiveImageCount = images.Count(image => !image.IsIncludedInAlbum);
        int missingImageCount = images.Count(image => !image.IsAvailable);
        int pageCount = includedImageCount == 0
            ? 0
            : (int)Math.Ceiling(includedImageCount / (double)source.ImagesPerPage);
        sourceDetailsText.Text =
            "Төрөл: Studio зураг\n" +
            "Төлөв: Төсөлд хадгалагдсан\n" +
            $"Үе шат: {state.Project.Identity.StageName}\n" +
            "Бүлэг: Харагдах байдал\n" +
            $"Зураг: {images.Count} · Альбумд: {includedImageCount}" +
            (inactiveImageCount > 0 ? $" · Идэвхгүй: {inactiveImageCount}" : "") +
            (missingImageCount > 0 ? $" · Эх файл олдоогүй: {missingImageCount}" : "") + "\n" +
            $"Автомат хуудас: {pageCount}\n" +
            $"Нэг хуудсанд: {source.ImagesPerPage} зураг";
        sourceWorkflowText.Text =
            "PNG/JPEG зургуудыг олон сонголтоор нэмнэ. Studio босоо, хөндлөн харьцааг таньж " +
            "A3 хуудсанд сунгалтгүйгээр автоматаар зохиомжлоно.";
        openNativeSourceButton.Visibility = Visibility.Collapsed;
        openSourceFolderButton.Visibility = Visibility.Visible;
        visualizationSourceControls.Visibility = Visibility.Visible;

        bindingVisualizationSource = true;
        visualizationImagesPerPageBox.SelectedItem = visualizationImagesPerPageBox.Items
            .Cast<ImagesPerPageChoice>()
            .FirstOrDefault(choice => choice.Value == source.ImagesPerPage);
        bindingVisualizationSource = false;
        addVisualizationImagesButton.IsEnabled = CanEditProjectContent();
        UpdateVisualizationActionState();
    }

    private void RefreshVisualizationImagesList()
    {
        CancelVisualizationThumbnailLoading();
        List<VisualizationImageWorkspaceItem> items = CurrentProjectVisualizationImages()
            .Select(image => new VisualizationImageWorkspaceItem(
                image,
                null))
            .ToList();
        visualizationImagesWorkspaceList.ItemsSource = items;
        if (items.Count > 0)
        {
            var cancellation = new CancellationTokenSource();
            visualizationThumbnailLoadCancellation = cancellation;
            _ = LoadVisualizationThumbnailsAsync(
                items,
                state.Project.ProjectId,
                cancellation.Token);
        }
        UpdateVisualizationActionState();
    }

    private async Task LoadVisualizationThumbnailsAsync(
        IReadOnlyList<VisualizationImageWorkspaceItem> items,
        string projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            foreach (VisualizationImageWorkspaceItem item in items)
            {
                ImageSource? thumbnail = await Task.Run(
                    () => TryLoadVisualizationThumbnail(item.Image),
                    cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (!state.HasOpenProject ||
                    !state.Project.ProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                item.SetThumbnail(thumbnail);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void CancelVisualizationThumbnailLoading()
    {
        visualizationThumbnailLoadCancellation?.Cancel();
        visualizationThumbnailLoadCancellation?.Dispose();
        visualizationThumbnailLoadCancellation = null;
    }

    private void AddVisualizationImages()
    {
        if (!EnsureProjectContentPermission() || state.ProjectPath is null)
            return;

        ProjectVisualizationSource visualizationSource = CurrentProjectVisualizationSource();
        visualizationSource.ConfigureForProject(state.Project.ProjectId);

        var dialog = new OpenFileDialog
        {
            Title = "Харагдах байдлын зураг сонгох",
            Filter = "Зураг|*.png;*.jpg;*.jpeg|PNG|*.png|JPEG|*.jpg;*.jpeg",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog(Window.GetWindow(Root)) != true)
            return;

        int added = 0;
        int updated = 0;
        int skipped = 0;
        foreach (string sourcePath in dialog.FileNames)
        {
            try
            {
                string linkedSourcePath = Path.GetFullPath(sourcePath);
                ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
                if (inspection.PixelWidth <= 0 || inspection.PixelHeight <= 0)
                    throw new InvalidDataException("Зургийн хэмжээ уншигдсангүй.");
                ProjectVisualizationImage? linkedImage = visualizationSource
                    .ImagesForProject(state.Project.ProjectId)
                    .FirstOrDefault(image => PathsEqual(image.LinkedSourcePath, linkedSourcePath));
                ProjectVisualizationImage? sameContent = visualizationSource
                    .ImagesForProject(state.Project.ProjectId)
                    .FirstOrDefault(image => image.Sha256.Equals(
                        inspection.Sha256,
                        StringComparison.OrdinalIgnoreCase));
                if (linkedImage is null && sameContent is not null)
                {
                    if (string.IsNullOrWhiteSpace(sameContent.LinkedSourcePath))
                    {
                        sameContent.LinkedSourcePath = linkedSourcePath;
                        sameContent.LinkedSourceLastWriteTimeUtc = new DateTimeOffset(
                            File.GetLastWriteTimeUtc(linkedSourcePath),
                            TimeSpan.Zero);
                        sameContent.OriginalFileName = Path.GetFileName(sourcePath);
                        sameContent.IsAvailable = true;
                        updated++;
                        continue;
                    }
                    skipped++;
                    continue;
                }

                string relativePath = ProjectVisualizationFileStore.StoreInsideProject(
                    state.ProjectPath,
                    sourcePath);
                DateTimeOffset sourceWriteTime = new(
                    File.GetLastWriteTimeUtc(linkedSourcePath),
                    TimeSpan.Zero);
                if (linkedImage is not null)
                {
                    bool contentChanged = !linkedImage.Sha256.Equals(
                        inspection.Sha256,
                        StringComparison.OrdinalIgnoreCase);
                    linkedImage.OriginalFileName = Path.GetFileName(sourcePath);
                    linkedImage.LinkedSourcePath = linkedSourcePath;
                    linkedImage.LinkedSourceLastWriteTimeUtc = sourceWriteTime;
                    linkedImage.RelativePath = relativePath;
                    linkedImage.ContentType = inspection.ContentType;
                    linkedImage.SizeBytes = inspection.SizeBytes;
                    linkedImage.PixelWidth = inspection.PixelWidth;
                    linkedImage.PixelHeight = inspection.PixelHeight;
                    linkedImage.Sha256 = inspection.Sha256;
                    linkedImage.IsAvailable = true;
                    if (contentChanged)
                        linkedImage.Version = Math.Max(1, linkedImage.Version) + 1;
                    updated++;
                    continue;
                }

                visualizationSource.Images.Add(new ProjectVisualizationImage
                {
                    OwnerProjectId = state.Project.ProjectId,
                    OriginalFileName = Path.GetFileName(sourcePath),
                    LinkedSourcePath = linkedSourcePath,
                    LinkedSourceLastWriteTimeUtc = sourceWriteTime,
                    IsAvailable = true,
                    RelativePath = relativePath,
                    ContentType = inspection.ContentType,
                    SizeBytes = inspection.SizeBytes,
                    PixelWidth = inspection.PixelWidth,
                    PixelHeight = inspection.PixelHeight,
                    Sha256 = inspection.Sha256,
                    AddedAtUtc = DateTimeOffset.UtcNow,
                });
                added++;
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                SetStatus($"{Path.GetFileName(sourcePath)} зураг нэмэгдсэнгүй: {exception.Message}");
            }
        }

        if (added == 0 && updated == 0)
        {
            if (skipped > 0)
                SetStatus($"Сонгосон {skipped} зураг өмнө нь нэмэгдсэн байна.");
            return;
        }

        visualizationSource.Normalize(state.Project.ProjectId);
        state.SaveProject();
        RefreshSourceWorkspace(VisualizationSourceSelectionKey);
        UpdateAlbum(
            silent: false,
            statusPrefix: $"Харагдах байдалд {added} зураг нэмэгдэж, {updated} зураг шинэчлэгдлээ" +
                          (skipped > 0 ? $", {skipped} давхардлыг алгасав" : ""));
    }

    private void RelinkSelectedVisualizationImage()
    {
        if (!EnsureProjectContentPermission() || state.ProjectPath is null ||
            visualizationImagesWorkspaceList.SelectedItems.Count != 1 ||
            visualizationImagesWorkspaceList.SelectedItem is not VisualizationImageWorkspaceItem selected)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Харагдах байдлын шинэ эх зургийг заах",
            Filter = "Зураг|*.png;*.jpg;*.jpeg|PNG|*.png|JPEG|*.jpg;*.jpeg",
            Multiselect = false,
            CheckFileExists = true,
            FileName = string.IsNullOrWhiteSpace(selected.Image.LinkedSourcePath)
                ? selected.Image.OriginalFileName
                : selected.Image.LinkedSourcePath,
        };
        if (dialog.ShowDialog(Window.GetWindow(Root)) != true)
            return;

        try
        {
            string sourcePath = Path.GetFullPath(dialog.FileName);
            ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
            if (inspection.PixelWidth <= 0 || inspection.PixelHeight <= 0)
                throw new InvalidDataException("Зургийн хэмжээ уншигдсангүй.");
            string relativePath = ProjectVisualizationFileStore.StoreInsideProject(
                state.ProjectPath,
                sourcePath);
            ProjectVisualizationImage image = selected.Image;
            bool contentChanged = !image.Sha256.Equals(
                inspection.Sha256,
                StringComparison.OrdinalIgnoreCase);
            image.OriginalFileName = Path.GetFileName(sourcePath);
            image.LinkedSourcePath = sourcePath;
            image.LinkedSourceLastWriteTimeUtc = new DateTimeOffset(
                File.GetLastWriteTimeUtc(sourcePath),
                TimeSpan.Zero);
            image.RelativePath = relativePath;
            image.ContentType = inspection.ContentType;
            image.SizeBytes = inspection.SizeBytes;
            image.PixelWidth = inspection.PixelWidth;
            image.PixelHeight = inspection.PixelHeight;
            image.Sha256 = inspection.Sha256;
            image.IsAvailable = true;
            if (contentChanged)
                image.Version = Math.Max(1, image.Version) + 1;

            state.SaveProject();
            RefreshSourceWorkspace(VisualizationSourceSelectionKey);
            UpdateAlbum(
                silent: false,
                statusPrefix: $"{image.OriginalFileName} эх зураг дахин холбогдлоо");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SetStatus($"Харагдах байдлын эх зураг холбогдсонгүй: {exception.Message}");
        }
    }

    private void ExcludeSelectedVisualizationImages()
    {
        if (!EnsureProjectContentPermission())
            return;
        ProjectVisualizationImage[] selectedImages = visualizationImagesWorkspaceList.SelectedItems
            .Cast<VisualizationImageWorkspaceItem>()
            .Select(item => item.Image)
            .Where(image => image.IsIncludedInAlbum)
            .ToArray();
        if (selectedImages.Length == 0)
            return;

        foreach (ProjectVisualizationImage image in selectedImages)
            image.IsIncludedInAlbum = false;
        state.MarkAlbumComponentChanged(ProjectCloudSyncMetadata.VisualizationsComponentCode);
        state.MarkFoundationContentChanged();
        RefreshSourceWorkspace(VisualizationSourceSelectionKey);
        UpdateAlbum(
            silent: false,
            statusPrefix: $"{selectedImages.Length} зураг хуудаснаас хасагдлаа. Эх үүсвэрт идэвхгүй төлөвөөр хадгалагдсан");
    }

    private void IncludeSelectedVisualizationImages()
    {
        if (!EnsureProjectContentPermission())
            return;
        ProjectVisualizationImage[] selectedImages = visualizationImagesWorkspaceList.SelectedItems
            .Cast<VisualizationImageWorkspaceItem>()
            .Select(item => item.Image)
            .Where(image => !image.IsIncludedInAlbum)
            .ToArray();
        if (selectedImages.Length == 0)
            return;

        foreach (ProjectVisualizationImage image in selectedImages)
            image.IsIncludedInAlbum = true;
        state.MarkAlbumComponentChanged(ProjectCloudSyncMetadata.VisualizationsComponentCode);
        state.MarkFoundationContentChanged();
        RefreshSourceWorkspace(VisualizationSourceSelectionKey);
        UpdateAlbum(
            silent: false,
            statusPrefix: $"{selectedImages.Length} зураг альбумын хуудаслалтад буцаан орлоо");
    }

    private void EditVisualizationAlbumPages()
    {
        if (!EnsureProjectContentPermission())
            return;

        ProjectVisualizationSource source = CurrentProjectVisualizationSource();
        if (CurrentProjectVisualizationImages().Count == 0)
        {
            SetStatus("Харагдах байдлын эх үүсвэрт зураг нэмээгүй байна.");
            return;
        }

        int firstPageNumber = ResolveFirstVisualizationPageNumber();
        var dialog = new VisualizationAlbumEditorDialog(
            source,
            state.Project.ProjectId,
            firstPageNumber,
            ResolveVisualizationImagePath)
        {
            Owner = Window.GetWindow(Root),
        };
        if (dialog.ShowDialog() != true || dialog.ChangedCount == 0)
            return;

        int changed = 0;
        foreach (ProjectVisualizationImage image in CurrentProjectVisualizationImages())
        {
            if (!dialog.InclusionByImageId.TryGetValue(image.Id, out bool included) ||
                image.IsIncludedInAlbum == included)
            {
                continue;
            }

            image.IsIncludedInAlbum = included;
            changed++;
        }
        if (changed == 0)
            return;

        state.MarkAlbumComponentChanged(ProjectCloudSyncMetadata.VisualizationsComponentCode);
        state.MarkFoundationContentChanged();
        RefreshSourceWorkspace(VisualizationSourceSelectionKey);
        UpdateAlbum(
            silent: false,
            statusPrefix: $"Харагдах байдлын {changed} зургийн төлөв шинэчлэгдэж, хуудсууд дахин зохион байгуулагдлаа");
    }

    private int ResolveFirstVisualizationPageNumber()
    {
        AlbumProject buildProject = state.CreateAlbumBuildProject();
        IReadOnlyList<ConceptGeneratedPagePlan> generatedPlans =
            BuildingArchitectureConceptGeneratedPagePlanner.Create(buildProject);
        IReadOnlyList<ConceptAlbumSourcePage> sequence =
            BuildingArchitectureConceptAlbumSequencer.Create(
                state.Album,
                state.Album.Pages,
                state.Library,
                state.Project.Sources,
                generatedPlans.Count);
        return BuildingArchitectureConceptAlbumSequencer.NextAutomaticNumber(
            state.Album,
            sequence,
            generatedPlans.Count);
    }

    private void UpdateVisualizationActionState()
    {
        bool canEdit = CanEditProjectContent();
        ProjectVisualizationImage[] selected = visualizationImagesWorkspaceList.SelectedItems
            .Cast<VisualizationImageWorkspaceItem>()
            .Select(item => item.Image)
            .ToArray();
        relinkVisualizationImageButton.IsEnabled = canEdit && selected.Length == 1;
        excludeVisualizationImagesButton.IsEnabled =
            canEdit && selected.Any(image => image.IsIncludedInAlbum);
        includeVisualizationImagesButton.IsEnabled =
            canEdit && selected.Any(image => !image.IsIncludedInAlbum);
    }

    private void ChangeVisualizationImagesPerPage()
    {
        if (bindingVisualizationSource ||
            visualizationImagesPerPageBox.SelectedItem is not ImagesPerPageChoice choice ||
            !state.HasOpenProject ||
            !CanEditProjectContent() ||
            CurrentProjectVisualizationSource().ImagesPerPage == choice.Value)
        {
            return;
        }

        ProjectVisualizationSource source = CurrentProjectVisualizationSource();
        source.ConfigureForProject(state.Project.ProjectId);
        source.ImagesPerPage = choice.Value;
        source.Normalize(state.Project.ProjectId);
        state.SaveProject();
        RefreshSourceWorkspace(VisualizationSourceSelectionKey);
        UpdateAlbum(
            silent: false,
            statusPrefix: $"Харагдах байдлын нэг хуудсанд {choice.Value} зураг байрлуулна");
    }

    private string ResolveVisualizationImageFolder() => Path.Combine(
        state.ResolveProjectFolder(),
        "sources",
        "visualizations",
        "images");

    private string? ResolveVisualizationImagePath(ProjectVisualizationImage image)
    {
        if (state.ProjectPath is null || string.IsNullOrWhiteSpace(image.RelativePath))
            return null;
        if (!string.IsNullOrWhiteSpace(image.OwnerProjectId) &&
            !image.OwnerProjectId.Equals(state.Project.ProjectId, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        try
        {
            string path = ProjectWorkspacePaths.ResolveInsideProject(
                state.ProjectPath,
                image.RelativePath);
            return File.Exists(path) ? path : null;
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private ImageSource? TryLoadVisualizationThumbnail(ProjectVisualizationImage image)
    {
        if (!image.IsAvailable)
            return null;
        string? path = ResolveVisualizationImagePath(image);
        if (path is null)
            return null;
        try
        {
            using FileStream stream = File.OpenRead(path);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 180;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception exception) when (
            exception is IOException or NotSupportedException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private void AddVisualizationPagePreview(
        Canvas canvas,
        VisualizationAlbumPagePlan plan)
    {
        foreach (VisualizationImageTilePlan tile in plan.Tiles)
        {
            PageRectMm frame = tile.Frame;
            var background = AddPreviewRectangle(canvas, frame, Brushes.White, Brushes.Transparent);
            background.StrokeThickness = 0;
            ImageSource? source = TryLoadVisualizationThumbnail(tile.Image);
            if (source is not null)
            {
                var image = new Image
                {
                    Source = source,
                    Width = frame.Width,
                    Height = frame.Height,
                    Stretch = tile.FitMode == VisualizationImageFitMode.CenterCrop
                        ? Stretch.UniformToFill
                        : Stretch.Uniform,
                    Clip = new RectangleGeometry(new Rect(0, 0, frame.Width, frame.Height)),
                };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                Canvas.SetLeft(image, frame.X);
                Canvas.SetTop(image, frame.Y);
                canvas.Children.Add(image);
            }
            else
            {
                AddPreviewText(
                    canvas,
                    "Зураг олдсонгүй",
                    frame.X + 4,
                    frame.Y + frame.Height * 0.42,
                    frame.Width - 8,
                    20,
                    8,
                    FontWeights.Normal,
                    Brushes.DimGray);
            }
            var border = AddPreviewRectangle(canvas, frame, Brushes.Transparent, Brushes.LightGray);
            border.StrokeThickness = 0.35;
        }
        AddConceptSheetPreviewChrome(canvas, plan.Title, plan.Number);
    }

    private sealed class VisualizationImageWorkspaceItem : INotifyPropertyChanged
    {
        private ImageSource? thumbnail;

        public VisualizationImageWorkspaceItem(
            ProjectVisualizationImage image,
            ImageSource? thumbnail)
        {
            Image = image;
            this.thumbnail = thumbnail;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ProjectVisualizationImage Image { get; }

        public ImageSource? Thumbnail => thumbnail;

        public void SetThumbnail(ImageSource? value)
        {
            if (ReferenceEquals(thumbnail, value))
                return;
            thumbnail = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Thumbnail)));
        }

        public string FileName => Image.OriginalFileName;
        public string Orientation => Image.PixelWidth > Image.PixelHeight
            ? "Хөндлөн"
            : Image.PixelHeight > Image.PixelWidth
                ? "Босоо"
                : "Дөрвөлжин";
        public string Dimensions => $"{Image.PixelWidth} × {Image.PixelHeight} px";
        public string Status => !Image.IsAvailable
            ? "Эх файл олдсонгүй"
            : Image.IsIncludedInAlbum
                ? "Альбумд орсон"
                : "Идэвхгүй";
    }

    private sealed record ImagesPerPageChoice(int Value)
    {
        public override string ToString() => $"{Value} зураг";
    }
}
