using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using Microsoft.Web.WebView2.Wpf;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private readonly ListBox designSourcesWorkspaceList = new();
    private readonly ListView receivedSheetsWorkspaceList = new();
    private readonly Grid receivedSheetsWorkspaceHost = new();
    private readonly StackPanel sourceSheetActionsPanel = new()
    {
        Orientation = Orientation.Horizontal,
        Margin = new Thickness(8, 7, 8, 7),
    };
    private readonly Button includeSelectedSourceSheetsButton =
        StudioWidgets.CreateButton("Альбумд оруулах");
    private readonly Button excludeSelectedSourceSheetsButton =
        StudioWidgets.CreateButton("Альбумаас хасах");
    private readonly Button editSelectedSourcePdfPageButton =
        StudioWidgets.CreatePrimaryButton("PDF хэсэг засах");
    private readonly TextBlock sourceSheetSummaryText = new()
    {
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(12, 0, 0, 0),
    };
    private readonly Grid sourceContentHost = new();
    private readonly TextBlock sourceContentTitle = new() { FontWeight = FontWeights.SemiBold };
    private readonly TextBlock sourceDetailsText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock sourceWorkflowText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button openNativeSourceButton = StudioWidgets.CreateIconTextButton(
        "icon-sources.svg",
        "Эх файл нээх",
        "RVT/DWG эх файлыг өөрийн мэргэжлийн программ дээр нээнэ.");
    private readonly Button openSourceFolderButton = StudioWidgets.CreateIconTextButton(
        "icon-sources.svg",
        "Хавтас нээх");
    private readonly Button relinkNativeSourceButton = StudioWidgets.CreateButton("Эх файлыг солих");
    private readonly Button bindCloudSourceButton = StudioWidgets.CreateButton("Cloud source холбох");
    private readonly Button transferSourceCustodyButton = StudioWidgets.CreateButton("Хариуцагч шилжүүлэх");
    private readonly Button removeDesignSourceButton = StudioWidgets.CreateButton("Эх үүсвэр хасах");

    private readonly ListBox albumPagesWorkspaceList = new();
    private readonly ToggleButton albumListViewToggle = new();
    private readonly ToggleButton albumThumbnailViewToggle = new();
    private readonly PdfPageImageCache albumPageImages = new();
    private readonly PdfPageImageCache sourceSheetPageImages = new();
    private readonly HashSet<string> collapsedAlbumWorkspaceNodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Grid albumPreviewHost = new();
    private readonly WebView2 albumPdfViewer = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };
    private readonly ComboBox albumPageSelectorBox = new();
    private readonly TextBlock albumPageOwnerText = new();

    /// <summary>
    /// The album view in marking mode. It takes the place of the reader in the
    /// same panel rather than opening a window of its own: a reviewer marks the
    /// fault on the drawing they are already looking at.
    /// </summary>
    private SheetMarkupSurface? albumMarkupSurface;
    private bool albumMarkupMode;
    private readonly ComboBox albumPageFormatBox = new();
    private readonly ComboBox albumPlacementBox = new();
    private readonly ComboBox albumSectionBox = new();
    private readonly ComboBox albumContentKindBox = new();
    private readonly TextBox albumPageNumberBox = new();
    private readonly TextBox albumPageTitleBox = new();
    private readonly CheckBox albumSourceCropCheck = new()
    {
        Content = "Хуучин хүрээ, булангийн хүснэгтийг тайрах",
    };
    private readonly TextBox albumCropLeftBox = new();
    private readonly TextBox albumCropTopBox = new();
    private readonly TextBox albumCropRightBox = new();
    private readonly TextBox albumCropBottomBox = new();
    private readonly Button albumCropFromDrawingAreaButton =
        StudioWidgets.CreateButton("Форматын цэвэр талбайгаар");
    private readonly StackPanel albumSourceCropPanel = new();
    private readonly ComboBox albumPdfPageSizeBox = new();
    private readonly ComboBox albumPdfOrientationBox = new();
    private readonly ComboBox albumPdfBindEdgeBox = new();
    private readonly TextBox albumPdfDrawingScaleBox = new();
    private readonly TextBox albumPdfCustomWidthBox = new();
    private readonly TextBox albumPdfCustomHeightBox = new();
    private readonly Button albumPdfApplyFormatButton =
        StudioWidgets.CreateButton("Формат хэрэглэх");
    private readonly Button albumSheetCommentButton =
        StudioWidgets.CreateGlyphTextButton("", "Хуудасны коммент");
    private readonly Button albumPdfEditPageButton =
        StudioWidgets.CreatePrimaryButton("PDF хэсэг засах");
    private readonly StackPanel albumPdfFormatPanel = new();
    private readonly StackPanel albumPdfCustomSizePanel = new();
    private readonly StackPanel albumGeneratedFormatPanel = new();
    private readonly ComboBox albumGeneratedFormatColumnsBox = new();
    private readonly ComboBox albumGeneratedFormatRowsBox = new();
    private readonly TextBlock albumGeneratedFormatSummaryText = new()
    {
        TextWrapping = TextWrapping.Wrap,
        Foreground = StudioTheme.MutedTextBrush,
        Margin = new Thickness(0, 5, 0, 4),
    };
    private readonly CheckBox includeCoverCheck = new() { Content = "Нүүр хуудас" };
    private readonly CheckBox includeTocCheck = new() { Content = "Зургийн жагсаалт" };
    private bool bindingAlbumPage;
    private bool bindingSourceWorkspaceSelection;
    private bool albumThumbnailMode;
    private bool albumPdfViewerConfigured;
    private bool sourceRefreshInProgress;
    private bool sourceRemovalInProgress;
    private bool sourceRelationshipMutationInProgress;
    private CancellationTokenSource? sourceSheetThumbnailLoadCancellation;
    private long sourceSheetThumbnailLoadSerial;
    private string? boundAlbumProjectId;
    private long albumPdfNavigationSerial;
    private CancellationTokenSource? albumThumbnailLoadCancellation;
    private string? selectedAlbumWorkspaceKey;
    private SiteContextMapEditorControl? inlineSiteContextEditor;
    private bool inlineSiteContextPersisted;

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
            if (bindingSourceWorkspaceSelection)
            {
                return;
            }
            RefreshReceivedSheetWorkspace();
            RefreshSourceDetails();
        };
        workspace.Children.Add(BuildPane("Эх үүсвэрүүд", designSourcesWorkspaceList, new Thickness(0, 0, 1, 0)));

        ConfigureReceivedSheetsList();
        ConfigureReceivedSheetsWorkspace();
        ConfigureVisualizationImagesList();
        sourceContentTitle.Foreground = StudioTheme.TextBrush;
        sourceContentHost.Children.Add(receivedSheetsWorkspaceHost);
        sourceContentHost.Children.Add(visualizationImagesWorkspaceList);
        var sheetsPane = BuildPane(sourceContentTitle, sourceContentHost, new Thickness(0, 0, 1, 0));
        Grid.SetColumn(sheetsPane, 1);
        workspace.Children.Add(sheetsPane);

        sourceDetailsText.Foreground = StudioTheme.MutedTextBrush;
        sourceDetailsText.Margin = new Thickness(2, 4, 2, 10);
        var details = new StackPanel();
        details.Children.Add(sourceDetailsText);
        sourceWorkflowText.Foreground = StudioTheme.MutedTextBrush;
        sourceWorkflowText.Margin = new Thickness(2, 0, 2, 10);
        details.Children.Add(sourceWorkflowText);
        openNativeSourceButton.Margin = new Thickness(0, 0, 0, 6);
        openNativeSourceButton.Click += (_, _) => OpenSelectedNativeSource();
        details.Children.Add(openNativeSourceButton);
        openSourceFolderButton.Click += (_, _) => OpenSelectedSourceFolder();
        details.Children.Add(openSourceFolderButton);
        relinkNativeSourceButton.Margin = new Thickness(0, 6, 0, 0);
        relinkNativeSourceButton.ToolTip =
            "RVT/DWG эх файлын локал байрлалыг энэ төхөөрөмж дээр солино. Файл cloud руу дамжихгүй.";
        relinkNativeSourceButton.Click += (_, _) => RelinkSelectedNativeSource();
        details.Children.Add(relinkNativeSourceButton);
        bindCloudSourceButton.Margin = new Thickness(0, 6, 0, 0);
        bindCloudSourceButton.ToolTip =
            "Өөрт хариуцуулсан cloud source-ийг сонгосон локал эх үүсвэртэй холбоно.";
        bindCloudSourceButton.Click += async (_, _) => await BindSelectedCloudSourceAsync();
        details.Children.Add(bindCloudSourceButton);
        transferSourceCustodyButton.Margin = new Thickness(0, 6, 0, 0);
        transferSourceCustodyButton.ToolTip =
            "Cloud source-ийн хариуцагчийг төслийн edit эрхтэй гишүүнд шилжүүлнэ. Native файл дамжихгүй.";
        transferSourceCustodyButton.Click += async (_, _) => await TransferCloudSourceCustodyAsync();
        details.Children.Add(transferSourceCustodyButton);
        removeDesignSourceButton.Margin = new Thickness(0, 6, 0, 0);
        removeDesignSourceButton.ToolTip = "Төслийн бүртгэлээс хасна. Эх файл болон хүлээн авсан файлуудыг устгахгүй.";
        removeDesignSourceButton.Click += async (_, _) => await RemoveSelectedDesignSourceAsync();
        details.Children.Add(removeDesignSourceButton);
        details.Children.Add(BuildVisualizationSourceControls());
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
        var addVisualizationSource = StudioWidgets.CreateGlyphTextButton(
            "\uEB9F",
            "Харагдах байдал",
            "Одоогийн төсөлд зурагт харагдах байдлын эх үүсвэр үүсгэх");
        addVisualizationSource.Click += (_, _) => ConfigureVisualizationSourceForCurrentProject();
        var configureBuildings = StudioWidgets.CreateGlyphTextButton(
            "\uE8A9",
            "Барилгын бүлэг",
            "Олон Revit/AutoCAD эх үүсвэрийн хуудсыг нэг барилгын иж бүрдэлд оноох");
        configureBuildings.Click += (_, _) => ConfigureProjectBuildingGroups();
        var rescan = StudioWidgets.CreateButton("Эх үүсвэр шалгах");
        rescan.ToolTip =
            "Зөвхөн энэ төхөөрөмжийн Revit/AutoCAD package, АТД, гэрчилгээ, тусгай зөвшөөрөл " +
            "болон харагдах байдлын файлын өөрчлөлтийг шалгаж локал album-ыг шинэчилнэ. Cloud төслийг татахгүй.";
        rescan.Click += (_, _) => CheckForSourceUpdates();
        var openOperationLog = StudioWidgets.CreateButton("Үйлдлийн лог");
        openOperationLog.ToolTip =
            "Source Refresh болон Cloud Sync хаана, ямар reason code-оор зогссон эсвэл дууссаныг харна.\n" +
            operationDiagnosticLog.LogPath;
        openOperationLog.Click += (_, _) => OpenOperationDiagnosticLogFolder();
        sourceGroup.Children.Add(addSource);
        sourceGroup.Children.Add(addVisualizationSource);
        sourceGroup.Children.Add(configureBuildings);
        sourceGroup.Children.Add(rescan);
        sourceGroup.Children.Add(openOperationLog);
        ribbon.Children.Add(sourceGroup);
        return ribbon;
    }

    private void ConfigureProjectBuildingGroups()
    {
        if (!state.HasOpenProject || !EnsureProjectContentPermission())
        {
            return;
        }

        var dialog = new ProjectBuildingGroupsDialog(
            state.Project,
            state.Album,
            state.Library.VerifiedSnapshot())
        {
            Owner = Window.GetWindow(Root),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        state.UpdateBuildingComposition(dialog.ResultGroups, dialog.ResultAssignments);
        RefreshReceivedSheetWorkspace();
        RefreshAlbumWorkspace();
        UpdateAlbum(
            silent: false,
            statusPrefix: "Барилгын иж бүрдэл болон хуудасны дараалал шинэчлэгдлээ");
    }

    private void CheckForSourceUpdates()
    {
        string operationId = BeginDiagnosticOperation(
            "source_refresh",
            "source_refresh_started",
            "Локал эх үүсвэрийн өөрчлөлт шалгах үйлдэл эхэллээ.");
        bool canEditProjectContent = state.HasOpenProject && CanEditProjectContent();
        bool projectTransitionInProgress =
            refreshingCurrentProjectAccess || projectOpenInProgress;
        if (!StudioRefreshSyncOperationPolicy.CanStartSourceRefresh(
                state.HasOpenProject,
                canEditProjectContent,
                projectTransitionInProgress,
                sourceRefreshInProgress,
                syncInProgress || syncPreparationInProgress))
        {
            if (state.HasOpenProject &&
                !projectTransitionInProgress &&
                !sourceRefreshInProgress &&
                !syncInProgress &&
                !syncPreparationInProgress &&
                !canEditProjectContent)
            {
                _ = EnsureProjectContentPermission();
            }
            string reasonCode;
            string message;
            if (!state.HasOpenProject)
            {
                reasonCode = "source_refresh_no_open_project";
                message = "Source Refresh зогслоо: нээлттэй төсөл алга.";
            }
            else if (sourceRefreshInProgress)
            {
                reasonCode = "source_refresh_already_running";
                message = "Source Refresh аль хэдийн ажиллаж байна.";
            }
            else if (syncInProgress || syncPreparationInProgress)
            {
                reasonCode = "source_refresh_blocked_by_cloud_sync";
                message = "Source Refresh зогслоо: Cloud Sync дууссаны дараа дахин ажиллуулна уу.";
            }
            else if (projectTransitionInProgress)
            {
                reasonCode = "source_refresh_blocked_by_project_transition";
                message =
                    "Source Refresh зогслоо: төслийн access шалгалт эсвэл workspace нээлт дуусаагүй байна.";
            }
            else
            {
                reasonCode = "source_refresh_permission_denied";
                message = "Source Refresh зогслоо: таны project role альбум боловсруулах эрхгүй байна.";
            }
            SetOperationStatus(
                operationId,
                "source_refresh",
                "blocked",
                reasonCode,
                message);
            return;
        }

        StudioOperationContext operationContext = CaptureOperationContext();
        sourceRefreshInProgress = true;
        RefreshSyncUi();
        var selectedSourceId = (designSourcesWorkspaceList.SelectedItem as SourceWorkspaceItem)?.SelectionKey;
        SheetIntakeScanResult scan;
        ProjectAssetSourceReconciliationResult assetScan;
        CityGenProjectSiteReconciliationResult siteScan;
        StudioSourceMetadataUpgradeReport metadataUpgrade;
        IReadOnlyList<ProjectDesignSource> ownedSources;
        try
        {
            metadataUpgrade = state.UpgradeSourceMetadata();
            if (metadataUpgrade.ChangedCount > 0)
                state.RefreshSourceRuntimeWatchers();
            foreach (StudioSourceMetadataUpgradeDecision decision in
                     metadataUpgrade.Decisions.Where(item =>
                         item.Reason !=
                         StudioSourceMetadataUpgradeReason.SchemaCurrent))
            {
                RecordDiagnosticOperation(
                    operationId,
                    "source_refresh",
                    "progress",
                    decision.ReasonCode,
                    $"Source '{decision.SourceId}': {decision.Detail}");
            }
            ownedSources = StudioSourceRefreshScope.OwnedSources(
                state.Project,
                account.Current?.Email,
                StudioDeviceIdentity.Fingerprint);
            if (ownedSources.Count == 0)
            {
                SetOperationStatus(
                    operationId,
                    "source_refresh",
                    "progress",
                    "source_refresh_no_local_sources",
                    "Энэ бүртгэл/төхөөрөмжид баталгаатай локал эх үүсвэр алга. Cloud эх үүсвэрүүдэд хүрэхгүй.");
            }
            else
            {
                SetOperationStatus(
                    operationId,
                    "source_refresh",
                    "progress",
                    "source_refresh_scanning_local_sources",
                    $"Локал эх үүсвэрийн өөрчлөлт шалгаж байна: {ownedSources.Count} source...");
            }
            assetScan = state.ReconcileProjectAssetSources();
            siteScan = state.ReconcileCityGenProjectSite(ownedSources);
            RefreshLocalPdfSources(ownedSources);
            scan = state.Intake.RescanFolders(SourceInboxFolders(ownedSources));
        }
        catch (Exception exception)
        {
            sourceRefreshInProgress = false;
            RefreshSyncUi();
            SetOperationStatus(
                operationId,
                "source_refresh",
                "error",
                "source_refresh_scan_failed",
                $"Локал эх үүсвэр шалгахад алдаа: {exception.Message}",
                exception);
            return;
        }

        // Package callbacks reconcile authoritative snapshots on the UI dispatcher.
        // Queue the album rebuild after those callbacks so deletion and addition are atomic to the user.
        dispatcher.BeginInvoke(new Action(() =>
        {
            try
            {
                if (!CanContinueSourceRefresh(operationContext))
                {
                    SetOperationStatus(
                        operationId,
                        "source_refresh",
                        "cancelled",
                        "source_refresh_context_changed",
                        "Source Refresh-ийн үр дүнг хэрэгжүүлээгүй: бүртгэл, төсөл эсвэл access төлөв үйлдлийн явцад өөрчлөгдсөн.");
                    return;
                }

                autoRebuildTimer.Stop();
                RefreshSourceWorkspace(selectedSourceId);
                RefreshAlbumWorkspace();
                bool updated = UpdateAlbum(
                    silent: false,
                    statusPrefix: BuildSourceRefreshSummary(
                        scan,
                        assetScan,
                        siteScan,
                        metadataUpgrade),
                    origin: StudioWorkspaceOperation.SourceRefresh);
                if (updated)
                {
                    RecordDiagnosticOperation(
                        operationId,
                        "source_refresh",
                        "completed",
                        "source_refresh_completed",
                        statusText.Text);
                }
                else
                {
                    SetOperationStatus(
                        operationId,
                        "source_refresh",
                        "error",
                        "source_refresh_album_rebuild_failed",
                        statusText.Text,
                        lastAlbumUpdateException);
                }
            }
            catch (Exception exception)
            {
                SetOperationStatus(
                    operationId,
                    "source_refresh",
                    "error",
                    "source_refresh_ui_refresh_failed",
                    "Source Refresh-ийн дараах UI/альбум шинэчлэлт амжилтгүй: " + exception.Message,
                    exception);
            }
            finally
            {
                sourceRefreshInProgress = false;
                RefreshSyncUi();
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private bool CanContinueSourceRefresh(
        StudioOperationContext operationContext) =>
        StudioRefreshSyncOperationPolicy.CanContinueSourceRefresh(
            operationContext,
            refreshingCurrentProjectAccess || projectOpenInProgress,
            state.HasOpenProject,
            state.HasOpenProject ? state.Project : null,
            state.ProjectPath,
            account.Current,
            state.WorkspaceEpoch,
            account.SessionEpoch);

    private static string BuildSourceRefreshSummary(
        SheetIntakeScanResult scan,
        ProjectAssetSourceReconciliationResult assets,
        CityGenProjectSiteReconciliationResult site,
        StudioSourceMetadataUpgradeReport metadataUpgrade)
    {
        var summary = scan.ChangedPackageCount == 0
            ? $"{scan.ManifestCount} package шалгав, шинэ source өөрчлөлтгүй"
            : $"{scan.ChangedPackageCount} package шинэчлэгдэж, " +
              $"{scan.UpdatedSheetCount} sheet шинэчлэгдэн, {scan.RemovedSheetCount} sheet хасагдав";
        if (metadataUpgrade.BoundCount > 0)
        {
            summary +=
                $", хуучин локал эх үүсвэрийн binding сэргэсэн: " +
                $"{metadataUpgrade.BoundCount}";
        }
        else if (metadataUpgrade.ChangedCount > 0)
        {
            summary +=
                $", source metadata шинэчлэгдсэн: " +
                $"{metadataUpgrade.ChangedCount}";
        }
        if (scan.RejectedPackageCount > 0)
            summary += $", Rejected package: {scan.RejectedPackageCount}";
        int updatedAssets = assets.UpdatedDocumentCount + assets.UpdatedVisualizationCount;
        int missingAssets = assets.MissingDocumentCount + assets.MissingVisualizationCount;
        int restoredAssets = assets.RestoredDocumentCount + assets.RestoredVisualizationCount;
        if (updatedAssets > 0)
            summary += $", Studio source шинэчлэгдсэн: {updatedAssets}";
        if (missingAssets > 0)
            summary += $", альбумаас хасагдсан source: {missingAssets}";
        if (restoredAssets > 0)
            summary += $", сэргэсэн source: {restoredAssets}";
        if (site.Changed)
            summary += $", төслийн талбай шинэчлэгдсэн: {site.SourceDocumentName}";
        int otherErrors = Math.Max(0, scan.ErrorCount - scan.RejectedPackageCount) +
                          assets.ErrorCount +
                          site.ErrorCount;
        return otherErrors == 0 ? summary : $"{summary}, {otherErrors} алдаа";
    }

    private void ConfigureReceivedSheetsList()
    {
        receivedSheetsWorkspaceList.SelectionMode = SelectionMode.Extended;
        receivedSheetsWorkspaceList.BorderThickness = new Thickness(0);
        receivedSheetsWorkspaceList.Background = StudioTheme.InputBrush;
        receivedSheetsWorkspaceList.Foreground = StudioTheme.TextBrush;
        receivedSheetsWorkspaceList.SelectionChanged += (_, _) =>
            RefreshSourceSheetActionState();

        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.TextBrush));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 4, 5, 4)));
        itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));

        var itemTemplate = new ControlTemplate(typeof(ListViewItem));
        var rowBackground = new FrameworkElementFactory(typeof(Border), "RowBackground");
        rowBackground.SetBinding(
            Border.BackgroundProperty,
            new Binding(nameof(Control.Background))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            });
        rowBackground.SetBinding(
            Border.PaddingProperty,
            new Binding(nameof(Control.Padding))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            });
        rowBackground.SetBinding(
            System.Windows.Documents.TextElement.ForegroundProperty,
            new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            });
        rowBackground.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        var rowPresenter = new FrameworkElementFactory(typeof(GridViewRowPresenter));
        rowPresenter.SetBinding(
            GridViewRowPresenter.ContentProperty,
            new Binding(nameof(ContentControl.Content))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            });
        rowPresenter.SetBinding(
            GridViewRowPresenter.ColumnsProperty,
            new Binding($"{nameof(ListView.View)}.{nameof(GridView.Columns)}")
            {
                RelativeSource = new RelativeSource(
                    RelativeSourceMode.FindAncestor,
                    typeof(ListView),
                    1),
            });
        rowPresenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        rowPresenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        rowPresenter.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        rowBackground.AppendChild(rowPresenter);
        itemTemplate.VisualTree = rowBackground;

        var hoverTrigger = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
        };
        hoverTrigger.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(29, 40, 54)),
            "RowBackground"));
        hoverTrigger.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.TextBrush));
        itemTemplate.Triggers.Add(hoverTrigger);

        var focusedSelectionTrigger = new MultiTrigger();
        focusedSelectionTrigger.Conditions.Add(
            new System.Windows.Condition(ListBoxItem.IsSelectedProperty, true));
        focusedSelectionTrigger.Conditions.Add(
            new System.Windows.Condition(Selector.IsSelectionActiveProperty, true));
        focusedSelectionTrigger.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(25, 79, 132)),
            "RowBackground"));
        focusedSelectionTrigger.Setters.Add(
            new Setter(Control.ForegroundProperty, StudioTheme.TextBrush));
        itemTemplate.Triggers.Add(focusedSelectionTrigger);

        var unfocusedSelectionTrigger = new MultiTrigger();
        unfocusedSelectionTrigger.Conditions.Add(
            new System.Windows.Condition(ListBoxItem.IsSelectedProperty, true));
        unfocusedSelectionTrigger.Conditions.Add(
            new System.Windows.Condition(Selector.IsSelectionActiveProperty, false));
        unfocusedSelectionTrigger.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(35, 57, 82)),
            "RowBackground"));
        unfocusedSelectionTrigger.Setters.Add(
            new Setter(Control.ForegroundProperty, StudioTheme.TextBrush));
        itemTemplate.Triggers.Add(unfocusedSelectionTrigger);
        itemStyle.Setters.Add(new Setter(Control.TemplateProperty, itemTemplate));

        var inactiveTrigger = new DataTrigger
        {
            Binding = new Binding(nameof(SheetWorkspaceItem.IsActive)),
            Value = false,
        };
        inactiveTrigger.Setters.Add(new Setter(UIElement.OpacityProperty, 0.46));
        itemStyle.Triggers.Add(inactiveTrigger);
        receivedSheetsWorkspaceList.ItemContainerStyle = itemStyle;

        var view = new GridView();
        var headerStyle = new Style(typeof(GridViewColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, StudioTheme.PanelAltBrush));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.MutedTextBrush));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
        view.ColumnHeaderContainerStyle = headerStyle;
        view.Columns.Add(new GridViewColumn
        {
            Header = "Хуудас",
            Width = 174,
            CellTemplate = CreateSourceSheetThumbnailTemplate(),
        });
        view.Columns.Add(new GridViewColumn { Header = "Дугаар", Width = 72, DisplayMemberBinding = new Binding(nameof(SheetWorkspaceItem.Number)) });
        view.Columns.Add(new GridViewColumn { Header = "Нэр", Width = 230, DisplayMemberBinding = new Binding(nameof(SheetWorkspaceItem.Name)) });
        view.Columns.Add(new GridViewColumn { Header = "Барилга", Width = 150, DisplayMemberBinding = new Binding(nameof(SheetWorkspaceItem.Building)) });
        view.Columns.Add(new GridViewColumn { Header = "Эх файл", Width = 150, DisplayMemberBinding = new Binding(nameof(SheetWorkspaceItem.Application)) });
        view.Columns.Add(new GridViewColumn { Header = "Format", Width = 90, DisplayMemberBinding = new Binding(nameof(SheetWorkspaceItem.Size)) });
        view.Columns.Add(new GridViewColumn { Header = "Төлөв", Width = 100, DisplayMemberBinding = new Binding(nameof(SheetWorkspaceItem.Status)) });
        receivedSheetsWorkspaceList.View = view;
    }

    private void ConfigureReceivedSheetsWorkspace()
    {
        receivedSheetsWorkspaceHost.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        receivedSheetsWorkspaceHost.RowDefinitions.Add(new RowDefinition
        {
            Height = new GridLength(1, GridUnitType.Star),
        });

        excludeSelectedSourceSheetsButton.ToolTip =
            "Сонгосон PDF хуудсыг эх файлаас устгалгүйгээр энэ төслийн альбумд оруулахгүй.";
        excludeSelectedSourceSheetsButton.Click += (_, _) =>
            SetSelectedSourceSheetsActive(active: false);
        includeSelectedSourceSheetsButton.ToolTip =
            "Идэвхгүй болгосон PDF хуудсыг энэ төслийн альбумд буцаан оруулна.";
        includeSelectedSourceSheetsButton.Click += (_, _) =>
            SetSelectedSourceSheetsActive(active: true);
        editSelectedSourcePdfPageButton.Click += (_, _) =>
            EditSelectedSourcePdfPage();
        ToolTipService.SetShowOnDisabled(editSelectedSourcePdfPageButton, true);
        excludeSelectedSourceSheetsButton.Margin = new Thickness(0, 0, 6, 0);
        includeSelectedSourceSheetsButton.Margin = new Thickness(0, 0, 6, 0);
        editSelectedSourcePdfPageButton.Margin = new Thickness(0, 0, 6, 0);
        sourceSheetSummaryText.Foreground = StudioTheme.MutedTextBrush;

        sourceSheetActionsPanel.Children.Add(excludeSelectedSourceSheetsButton);
        sourceSheetActionsPanel.Children.Add(includeSelectedSourceSheetsButton);
        sourceSheetActionsPanel.Children.Add(editSelectedSourcePdfPageButton);
        sourceSheetActionsPanel.Children.Add(sourceSheetSummaryText);
        receivedSheetsWorkspaceHost.Children.Add(sourceSheetActionsPanel);
        Grid.SetRow(receivedSheetsWorkspaceList, 1);
        receivedSheetsWorkspaceHost.Children.Add(receivedSheetsWorkspaceList);
    }

    private static DataTemplate CreateSourceSheetThumbnailTemplate()
    {
        var host = new FrameworkElementFactory(typeof(Border));
        host.SetValue(FrameworkElement.WidthProperty, 154.0);
        host.SetValue(FrameworkElement.HeightProperty, 110.0);
        host.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromRgb(238, 239, 241)));
        host.SetValue(Border.BorderBrushProperty, new SolidColorBrush(Color.FromRgb(83, 91, 102)));
        host.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        host.SetValue(Border.CornerRadiusProperty, new CornerRadius(1));
        host.SetValue(FrameworkElement.MarginProperty, new Thickness(2));

        var content = new FrameworkElementFactory(typeof(Grid));
        var message = new FrameworkElementFactory(typeof(TextBlock));
        message.SetBinding(TextBlock.TextProperty, new Binding(nameof(SheetWorkspaceItem.ThumbnailMessage)));
        message.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(105, 112, 122)));
        message.SetValue(TextBlock.FontSizeProperty, 9.0);
        message.SetValue(TextBlock.TextAlignmentProperty, TextAlignment.Center);
        message.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        message.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        message.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        message.SetValue(FrameworkElement.MarginProperty, new Thickness(8));
        content.AppendChild(message);

        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new Binding(nameof(SheetWorkspaceItem.ThumbnailSource)));
        image.SetValue(Image.StretchProperty, Stretch.Uniform);
        image.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.HighQuality);
        image.SetValue(FrameworkElement.SnapsToDevicePixelsProperty, true);
        image.SetValue(FrameworkElement.MarginProperty, new Thickness(1));
        content.AppendChild(image);
        host.AppendChild(content);
        return new DataTemplate { VisualTree = host };
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

        string currentUserEmail = (account.Current?.Email ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(currentUserEmail))
        {
            ProjectCloudSyncMetadata.BindCloudOwner(dialog.ResultSource, currentUserEmail);
            StudioLocalSourceBindingPolicy.Bind(
                dialog.ResultSource,
                currentUserEmail,
                StudioDeviceIdentity.Fingerprint);
        }
        if (dialog.BuildingGroupsChanged)
        {
            state.UpdateBuildingComposition(
                dialog.ResultBuildingGroups,
                state.Project.SheetBuildingAssignments);
        }
        state.AddDesignSource(dialog.ResultSource);
        if (dialog.ResultSource.Kind == DesignSourceKind.Pdf)
        {
            try
            {
                LocalPdfSheetPackageImportResult imported =
                    new LocalPdfSheetPackageImporter().Import(
                        state.Project,
                        dialog.ResultSource);
                state.SaveProject();
                state.Intake.Rescan();
                SetStatus(
                    $"PDF эх үүсвэр нэмэгдлээ: {imported.PageCount} хуудас. " +
                    "Хуудас бүрийн төрөл болон тайралтыг Альбум хэсгээс тохируулна.");
            }
            catch (Exception exception)
            {
                SetStatus($"PDF эх үүсвэр импортлоход алдаа: {exception.Message}");
            }
        }
        RefreshSourceWorkspace(dialog.ResultSource.Id);
        if (dialog.ResultSource.Kind != DesignSourceKind.Pdf)
        {
            SetStatus(dialog.ResultSource.Kind == DesignSourceKind.Revit
                ? $"RVT эх үүсвэр холбогдлоо: {dialog.ResultSource.DisplayName}. Revit-ийн Альбум хэсгээс Studio руу илгээнэ."
                : $"Эх үүсвэр нэмэгдлээ: {dialog.ResultSource.DisplayName}");
        }
    }

    private static IReadOnlyList<string> SourceInboxFolders(
        IEnumerable<ProjectDesignSource> sources) =>
        (sources ?? [])
        .SelectMany(source =>
        {
            var folders = new List<string>();
            if (!string.IsNullOrWhiteSpace(source.InboxFolder))
                folders.Add(source.InboxFolder);
            if (source.Metadata.TryGetValue(
                    "LegacyInboxFolder",
                    out string? legacyInbox) &&
                !string.IsNullOrWhiteSpace(legacyInbox))
            {
                folders.Add(legacyInbox);
            }
            return folders;
        })
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private void RefreshLocalPdfSources(
        IEnumerable<ProjectDesignSource>? sources = null)
    {
        var importer = new LocalPdfSheetPackageImporter();
        bool changed = false;
        foreach (ProjectDesignSource source in (sources ?? state.Project.Sources).Where(item =>
                     item.Kind == DesignSourceKind.Pdf))
        {
            LocalPdfSheetPackageImportResult result = importer.Import(state.Project, source);
            changed |= result.Changed;
        }

        if (changed)
        {
            state.SaveProject();
        }
    }

    private async Task RemoveSelectedDesignSourceAsync()
    {
        if (!EnsureProjectContentPermission())
            return;
        if (designSourcesWorkspaceList.SelectedItem is not SourceWorkspaceItem { Source: ProjectDesignSource source })
        {
            return;
        }

        if (!CanEditLocalSource(source))
        {
            SetStatus("Энэ эх үүсвэрийг зөвхөн үүсгэсэн хэрэглэгч салгах эсвэл солих эрхтэй.");
            return;
        }

        string currentOwner = (account.Current?.Email ?? "").Trim().ToLowerInvariant();
        bool cloudLinked =
            state.Project.Cloud.Origin.Equals(
                ProjectOrigins.Cloud,
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(
                state.Project.Cloud.ServerProjectId);
        StudioSourceRegistryResolution registryResolution =
            StudioSourceRemovalOutbox.ResolveRegistrySource(
                state.Project,
                source);
        if (cloudLinked && !registryResolution.IsExact)
        {
            string reason =
                registryResolution.Status ==
                StudioSourceRegistryResolutionStatus.Ambiguous
                    ? "ижил owner + SourceKey-тэй хэд хэдэн идэвхтэй registry мөр байна"
                    : "яг тохирох идэвхтэй registry мөр локал mirror-т алга";
            SetStatus(
                $"Эх үүсвэр хасагдсангүй: {reason}. " +
                "Cloud Sync хийж registry mirror-оо шинэчлээд дахин оролдоно уу. " +
                "Локал бүртгэл, альбумын хуудас болон эх файл өөрчлөгдөөгүй. " +
                "[reason: source_removal_registry_not_exact]");
            return;
        }

        if (cloudLinked)
        {
            ProjectCloudSourceReference sharedSource =
                registryResolution.Source!;
            StudioSourceLocalRemovalCommit localCommit =
                StudioSourceRemovalOutbox.StageAndRemoveLocal(
                state.Project,
                source,
                sharedSource,
                currentOwner,
                StudioDeviceIdentity.Fingerprint,
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source),
                state.SaveProject,
                state.RemoveDesignSource);
            ProjectLocalAlbumComponentClaim removalClaim =
                localCommit.Claim;
            int cloudRemovedPageCount =
                localCommit.RemovedAlbumPageCount;
            RefreshSourceWorkspace();
            RefreshAlbumWorkspace();
            UpdateAlbum(
                silent: true,
                statusPrefix:
                    "Эх үүсвэр локалаас хасагдсан альбум шинэчлэгдлээ");
            if (!account.IsSignedIn)
            {
                SetStatus(
                    $"Эх үүсвэр болон {cloudRemovedPageCount} локал альбумын хуудас хасагдлаа. " +
                    "Cloud retire хүсэлт pending хэвээр; дараагийн Sync серверт дамжуулна. " +
                    "Эх файл, хүлээн авсан PDF-үүд хэвээр үлдсэн.");
                return;
            }

            StudioOperationContext removalContext =
                CaptureOperationContext();
            sourceRemovalInProgress = true;
            try
            {
                StudioSourceRemoteRetirementResult remoteResult =
                    await StudioSourceRemovalOutbox
                        .TryConfirmRegistryRetirementAsync(
                            state.Project,
                            removalClaim,
                            currentOwner,
                            StudioDeviceIdentity.Fingerprint,
                            canContactCloud: true,
                            sourceId => account.RetireSourcePackageAsync(
                                removalContext.ServerProjectId,
                                sourceId),
                            () =>
                            {
                                if (!IsOperationContextCurrent(removalContext))
                                {
                                    throw new
                                        StudioOperationContextChangedException(
                                            "source_retire");
                                }
                            });
                if (remoteResult.Confirmed)
                {
                    state.SaveProject();
                    SetStatus(
                        $"Эх үүсвэр болон {cloudRemovedPageCount} локал альбумын хуудас хасагдаж, " +
                        "Cloud registry retire баталгаажлаа. Эх файл, хүлээн авсан PDF-үүд хэвээр үлдсэн.");
                    return;
                }

                SetStatus(
                    $"Эх үүсвэр болон {cloudRemovedPageCount} локал альбумын хуудас хасагдлаа. " +
                    "Cloud retire баталгаажаагүй тул хүсэлт pending хэвээр; дараагийн Sync idempotent байдлаар дахин оролдоно: " +
                    remoteResult.Error?.Message);
                return;
            }
            catch (StudioOperationContextChangedException)
            {
                SetStatus(
                    "Source retire сервер дээр баталгаажсан байж болох боловч нээлттэй төсөл/бүртгэл солигдсон. " +
                    "Локал эх үүсвэр аль хэдийн хасагдсан бөгөөд анхны төсөл дээр Sync хийхэд pending хүсэлт idempotent байдлаар дуусна. " +
                    "[reason: source_removal_context_changed]");
                return;
            }
            finally
            {
                sourceRemovalInProgress = false;
            }
        }

        int removedPageCount = state.RemoveDesignSource(source);
        RefreshSourceWorkspace();
        RefreshAlbumWorkspace();
        UpdateAlbum(silent: true, statusPrefix: "Эх үүсвэр хасагдсан альбум шинэчлэгдлээ");
        SetStatus(
            $"Эх үүсвэрийн бүртгэл болон {removedPageCount} альбумын хуудасны холбоосыг хаслаа: " +
            $"{source.DisplayName}. Эх файл, хүлээн авсан PDF-үүд хэвээр үлдсэн.");
    }

    private async Task<int> ProcessPendingDesignSourceRemovalsAsync(
        string projectId,
        StudioOperationContext operationContext)
    {
        IReadOnlyList<ProjectLocalAlbumComponentClaim> pending =
            StudioSourceRemovalOutbox.Pending(
                state.Project,
                account.Current?.Email,
                StudioDeviceIdentity.Fingerprint);
        int removed = 0;
        foreach (ProjectLocalAlbumComponentClaim claim in pending)
        {
            RequireOperationContext(
                operationContext,
                "cloud_sync_source_retire_start");
            // DELETE is exact-current-source CAS and idempotent for an
            // already retired row. Thus a timeout after a committed response
            // is safely retried after restart.
            ProjectDesignSource? local =
                await StudioSourceRemovalOutbox.ConfirmRegistryRetirementAsync(
                    state.Project,
                    claim,
                    account.Current?.Email,
                    StudioDeviceIdentity.Fingerprint,
                    sourceId => account.RetireSourcePackageAsync(
                        projectId,
                        sourceId),
                    () => RequireOperationContext(
                        operationContext,
                        "cloud_sync_source_retire_acknowledgement"));
            RequireOperationContext(
                operationContext,
                "cloud_sync_source_retire_apply");
            if (local is not null)
            {
                state.RemoveDesignSource(local);
                removed++;
            }
            else
            {
                state.SaveProject();
            }
        }

        return removed;
    }

    private void RelinkSelectedNativeSource()
    {
        if (!EnsureProjectContentPermission() ||
            designSourcesWorkspaceList.SelectedItem is not SourceWorkspaceItem { Source: ProjectDesignSource source })
        {
            return;
        }
        ProjectSourceEditAuthority authority =
            ProjectCloudSyncAuthority.ResolveSource(
                state.Project,
                source,
                account.Current?.Email);
        if (!authority.CanEdit)
        {
            SetStatus("Бусдын эх үүсвэрийн локал файлыг солих боломжгүй.");
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Локал эх файлыг дахин заах",
            Filter = NativeSourceFilter(source.Kind),
            CheckFileExists = true,
            Multiselect = false,
            FileName = string.IsNullOrWhiteSpace(source.NativeDocumentPath)
                ? ""
                : source.NativeDocumentPath,
        };
        if (dialog.ShowDialog(Window.GetWindow(Root)) != true)
            return;

        source.NativeDocumentPath = Path.GetFullPath(dialog.FileName);
        source.NativeDocumentTitle = Path.GetFileName(dialog.FileName);
        if (!StudioLocalSourceBindingPolicy.TryExplicitRelink(
                source,
                authority.OwnerEmail,
                account.Current?.Email,
                StudioDeviceIdentity.Fingerprint,
                hasVerifiedPayload: File.Exists(source.NativeDocumentPath)))
        {
            SetStatus(
                "Локал эх файлыг холбосонгүй: энэ бүртгэл source-ийн баталгаажсан хариуцагч биш эсвэл файл уншигдахгүй байна.");
            return;
        }
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.Metadata["local.nativeRelinkedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
        state.SaveProject();
        state.RefreshSourceRuntimeWatchers();
        if (source.Kind == DesignSourceKind.Pdf)
        {
            try
            {
                StudioPdfSourceRelinkIntakeResult relink =
                    StudioPdfSourceRelinkIntake.ImportAndRescan(
                        state.Project,
                        source,
                        state.Intake);
                state.SaveProject();
                RefreshSourceWorkspace(source.Id);
                SetStatus(
                    $"PDF эх файл дахин холбогдож, {relink.Import.PageCount} хуудас шууд импортлогдлоо: " +
                    $"{source.NativeDocumentTitle}. Эх PDF Cloud ERA руу илгээгдэхгүй; " +
                    "баталгаажсан contribution дараагийн Cloud Sync-ээр нэгтгэгдэнэ.");
            }
            catch (Exception exception)
            {
                RefreshSourceWorkspace(source.Id);
                SetStatus(
                    $"PDF эх файл холбогдсон боловч хуудсуудыг баталгаатай импортлож чадсангүй: " +
                    exception.Message);
            }
            return;
        }

        RefreshSourceWorkspace(source.Id);
        SetStatus(
            $"Локал эх файл дахин холбогдлоо: {source.NativeDocumentTitle}. " +
            "Файл болон бүтэн зам Cloud ERA руу илгээгдэхгүй.");
    }

    private static string NativeSourceFilter(DesignSourceKind kind) => kind switch
    {
        DesignSourceKind.Revit => "Revit project (*.rvt)|*.rvt|All files (*.*)|*.*",
        DesignSourceKind.AutoCad => "AutoCAD drawing (*.dwg)|*.dwg|All files (*.*)|*.*",
        DesignSourceKind.CityGen => "CityGen source (*.json;*.geojson;*.zip)|*.json;*.geojson;*.zip|All files (*.*)|*.*",
        DesignSourceKind.Pdf => "PDF document (*.pdf)|*.pdf|All files (*.*)|*.*",
        _ => "All files (*.*)|*.*",
    };

    private async Task BindSelectedCloudSourceAsync()
    {
        if (!EnsureProjectContentPermission() ||
            designSourcesWorkspaceList.SelectedItem is not SourceWorkspaceItem { Source: ProjectDesignSource source })
        {
            return;
        }
        if (!CanEditLocalSource(source))
        {
            SetStatus("Бусдын эх үүсвэрийг энэ төхөөрөмжийн файлтай дахин холбох боломжгүй.");
            return;
        }
        if (!account.IsSignedIn ||
            !state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId))
        {
            SetStatus("Cloud source холбохын өмнө Cloud ERA project нээнэ үү.");
            return;
        }

        string projectId = state.Project.Cloud.ServerProjectId;
        string bindingAccountEmail = account.Current?.Email ?? "";
        StudioOperationContext operationContext = CaptureOperationContext();
        sourceRelationshipMutationInProgress = true;
        try
        {
            IReadOnlyList<StudioCloudDesignPackage> packages = await account.ListDesignPackagesAsync(projectId);
            if (!CanContinueCloudSourceBinding(operationContext, source))
                return;
            List<StudioCloudSourcePackage> available =
                StudioCloudSourceBindingPolicy.EligibleSources(
                    state.Project,
                    source,
                    LatestCloudSources(packages),
                    bindingAccountEmail)
                .ToList();
            if (available.Count == 0)
            {
                SetStatus(
                    "Танд хариуцуулсан, локал source-т холбогдоогүй Cloud source алга. " +
                    "Төслийн admin эхлээд Хариуцагч шилжүүлэх үйлдлээр томилно.");
                return;
            }

            var dialog = new CloudSourceBindingDialog(available) { Owner = Window.GetWindow(Root) };
            if (dialog.ShowDialog() != true || dialog.SelectedSource is null)
                return;
            if (!CanContinueCloudSourceBinding(operationContext, source))
                return;

            ProjectCloudSyncMetadata.BindToCloudSource(
                state.Project,
                source,
                dialog.SelectedSource.SourceKey);
            ProjectCloudSyncMetadata.BindCloudOwner(
                source,
                StudioCloudSourceBindingPolicy.ImmutableOwner(
                    dialog.SelectedSource,
                    bindingAccountEmail));
            StudioLocalSourceBindingPolicy.Bind(
                source,
                bindingAccountEmail,
                StudioDeviceIdentity.Fingerprint);
            state.SaveProject();
            RefreshSourceWorkspace(source.Id);
            RefreshSyncUi();
            SetStatus(
                $"{source.DisplayName} локал эх үүсвэрийг {dialog.SelectedSource.SourceDocumentReference} cloud source-т холболоо. " +
                "RVT/DWG файл болон локал зам server рүү дамжаагүй.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            SetStatus("Cloud source холбож чадсангүй: " + exception.Message);
        }
        finally
        {
            sourceRelationshipMutationInProgress = false;
        }
    }

    private bool CanContinueCloudSourceBinding(
        StudioOperationContext operationContext,
        ProjectDesignSource source) =>
        StudioCloudSourceBindingContinuationPolicy.CanApply(
            operationContext,
            source,
            state.HasOpenProject,
            state.HasOpenProject ? state.Project : null,
            state.ProjectPath,
            account.Current,
            state.WorkspaceEpoch,
            account.SessionEpoch);

    private async Task TransferCloudSourceCustodyAsync()
    {
        if (!state.HasOpenProject || !CanManageProjectTeam())
        {
            SetStatus("Cloud source-ийн хариуцагч шилжүүлэхэд төслийн баг удирдах role шаардлагатай.");
            return;
        }
        string projectId = state.Project.Cloud.ServerProjectId;
        StudioOperationContext operationContext =
            CaptureOperationContext();
        sourceRelationshipMutationInProgress = true;
        try
        {
            StudioCloudProjectDetail project = await account.GetProjectAsync(projectId);
            if (!IsOperationContextCurrent(operationContext))
                return;
            IReadOnlyList<StudioProjectRole> roleCatalog = await account.ListProjectRolesAsync();
            if (!IsOperationContextCurrent(operationContext))
                return;
            HashSet<string> editRoles = roleCatalog
                .Where(role => role.CanEditContent)
                .Select(role => role.Code)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            List<StudioCloudParticipant> participants = project.Participants
                .OfType<StudioCloudParticipant>()
                .Where(participant =>
                    string.Equals(participant.Status, "Active", StringComparison.OrdinalIgnoreCase) &&
                    (participant.Roles ?? []).Any(editRoles.Contains))
                .ToList();
            IReadOnlyList<StudioCloudDesignPackage> packages = await account.ListDesignPackagesAsync(projectId);
            if (!IsOperationContextCurrent(operationContext))
                return;
            List<StudioCloudSourcePackage> sources = LatestCloudSources(packages);
            if (sources.Count == 0 || participants.Count == 0)
            {
                SetStatus("Шилжүүлэх cloud source эсвэл concept content edit эрхтэй идэвхтэй гишүүн алга.");
                return;
            }

            var dialog = new CloudSourceCustodyDialog(sources, participants)
            {
                Owner = Window.GetWindow(Root),
            };
            if (dialog.ShowDialog() != true || dialog.Draft is null)
                return;
            if (!StudioRelationshipBoundary.Confirm(
                    Window.GetWindow(Root),
                    StudioRelationshipAction.TransferSourceCustody,
                    dialog.Draft.DisplayLabel))
            {
                return;
            }

            await account.AssignSourceCustodianAsync(
                projectId,
                dialog.Draft.SourceId,
                dialog.Draft.ParticipantId,
                project.Project.ConcurrencyToken,
                dialog.Draft.SourceId);
            if (!IsOperationContextCurrent(operationContext))
                return;
            SetStatus(
                $"Cloud source хариуцагч шилжлээ: {dialog.Draft.DisplayLabel}. " +
                "Native файлыг талууд платформоос гадуур хүлээлцэж, шинэ хариуцагч локал файлаа дахин холбоно.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            SetStatus("Cloud source хариуцагч шилжсэнгүй: " + exception.Message);
        }
        finally
        {
            sourceRelationshipMutationInProgress = false;
        }
    }

    private static List<StudioCloudSourcePackage> LatestCloudSources(
        IReadOnlyList<StudioCloudDesignPackage> packages) =>
        StudioCloudSourcePackageReconciliation.ActiveCanonical(
                packages.SelectMany(package => package.SourcePackages))
        .Where(source => !string.IsNullOrWhiteSpace(source.SourceKey))
        .OrderBy(source => source.SourceDocumentReference, StringComparer.OrdinalIgnoreCase)
        .ToList();

    private void OpenSelectedSourceFolder()
    {
        if (designSourcesWorkspaceList.SelectedItem is SourceWorkspaceItem { IsVisualization: true })
        {
            string visualizationFolder = ResolveVisualizationImageFolder();
            Directory.CreateDirectory(visualizationFolder);
            Process.Start(new ProcessStartInfo(visualizationFolder) { UseShellExecute = true });
            return;
        }

        if (designSourcesWorkspaceList.SelectedItem is not SourceWorkspaceItem { Source: ProjectDesignSource source } ||
            string.IsNullOrWhiteSpace(source.InboxFolder) ||
            !CanEditLocalSource(source))
        {
            return;
        }

        Directory.CreateDirectory(source.InboxFolder);
        Process.Start(new ProcessStartInfo(source.InboxFolder) { UseShellExecute = true });
    }

    private void OpenSelectedNativeSource()
    {
        if (designSourcesWorkspaceList.SelectedItem is not SourceWorkspaceItem { Source: ProjectDesignSource source } ||
            string.IsNullOrWhiteSpace(source.NativeDocumentPath) ||
            !CanEditLocalSource(source))
        {
            return;
        }

        string path = source.NativeDocumentPath;
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            SetStatus($"Эх файл олдсонгүй. Байршлыг дахин заана уу: {path}");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            SetStatus(source.Kind == DesignSourceKind.Revit
                ? "RVT файлыг Revit дээр нээлээ. Erk-S Platform > Альбум > Studio руу илгээх үйлдлээр sheets шинэчилнэ."
                : $"Эх файлыг нээлээ: {source.NativeDocumentTitle}");
        }
        catch (Exception exception)
        {
            SetStatus($"Эх файл нээгдсэнгүй: {exception.Message}");
        }
    }

    private void RefreshSourceWorkspace(string? selectSourceId = null)
    {
        if (selectSourceId is null && designSourcesWorkspaceList.SelectedItem is SourceWorkspaceItem current)
        {
            selectSourceId = current.SelectionKey;
        }

        ProjectVisualizationSource visualizations = CurrentProjectVisualizationSource();
        var items = new List<SourceWorkspaceItem>();
        string currentOwner = (account.Current?.Email ?? "").Trim().ToLowerInvariant();
        List<ProjectCloudAlbumComponentReference> sharedComponents =
            (state.Project.Cloud.SharedAlbumComponents ?? [])
            .OfType<ProjectCloudAlbumComponentReference>()
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.SourceKey) ||
                string.Equals(
                    item.ComponentKind,
                    StudioAlbumComponentIdentity.SourceComponentKind,
                    StringComparison.OrdinalIgnoreCase))
            .ToList();
        var representedCloudSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<ProjectVisualizationImage> currentVisualizationImages =
            CurrentProjectVisualizationImages();
        // A cloud project hid this row until it already held images - and the row
        // is the only place the first image can be added, so on a cloud project
        // the feature could never be started at all. Anyone who may edit the
        // project's content now sees it, empty or not.
        if (visualizations.IsConfiguredForProject(state.Project.ProjectId) ||
            CanEditProjectContent())
        {
            items.Add(SourceWorkspaceItem.Visualizations(
                currentVisualizationImages.Count,
                visualizations.ImagesPerPage));
            if (HasLocalVisualizationImages())
            {
                representedCloudSources.Add(CloudSourceIdentity(
                    currentOwner,
                    StudioAlbumComponentIdentity.VisualizationSourceKey));
            }
        }
        if (!string.IsNullOrWhiteSpace(currentOwner) && HasOwnedAtdDocuments(currentOwner))
        {
            representedCloudSources.Add(CloudSourceIdentity(
                currentOwner,
                StudioAlbumComponentIdentity.AtdSourceKey));
        }
        bool cloudProject =
            state.Project.Cloud.Origin.Equals(
                ProjectOrigins.Cloud,
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId);
        foreach (ProjectDesignSource source in state.Project.Sources)
        {
            string sourceKey = ProjectCloudSyncMetadata.CloudSourceKey(source);
            string immutableOwner = cloudProject
                ? StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
                    state.Project,
                    source)
                : ProjectCloudSyncMetadata.CloudOwnerEmail(source);
            if (!cloudProject && string.IsNullOrWhiteSpace(immutableOwner))
                immutableOwner = currentOwner;
            ProjectCloudSourceReference? sharedSource =
                (state.Project.Cloud.SharedSources ?? [])
                .FirstOrDefault(item =>
                    item.SourceKey.Equals(
                        sourceKey,
                        StringComparison.OrdinalIgnoreCase) &&
                    StudioSharedSourceProjection.ImmutableOwner(item).Equals(
                        immutableOwner,
                        StringComparison.OrdinalIgnoreCase));
            string identity = CloudSourceIdentity(immutableOwner, sourceKey);
            if (!string.IsNullOrWhiteSpace(immutableOwner))
                representedCloudSources.Add(identity);
            ProjectCloudAlbumComponentReference? component =
                sharedComponents.FirstOrDefault(item =>
                    CloudSourceIdentity(item.OwnerEmail, item.SourceKey).Equals(
                        identity,
                        StringComparison.OrdinalIgnoreCase));
            bool hasVerifiedPayload =
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source);
            bool isLocal =
                StudioLocalSourceBindingPolicy.IsLocal(
                    source,
                    currentOwner,
                    StudioDeviceIdentity.Fingerprint,
                    hasVerifiedPayload);
            if (isLocal)
            {
                string detail =
                    $"{source.DisplayName} | Локал | {SourceStatusLabel(source.Status)}";
                if (component is not null)
                    detail += $" | Альбум #{component.Order}";
                items.Add(new SourceWorkspaceItem(
                    source,
                    false,
                    SourceDocumentLabel(source),
                    detail,
                    CloudSource: sharedSource,
                    CloudComponent: component));
                continue;
            }

            string cloudName = sharedSource is not null &&
                !string.IsNullOrWhiteSpace(
                    sharedSource.SourceDocumentReference)
                    ? sharedSource.SourceDocumentReference
                    : SourceDocumentLabel(source);
            items.Add(SourceWorkspaceItem.CloudBinding(
                source,
                sharedSource,
                component,
                cloudName,
                $"Cloud | {immutableOwner} | Локал payload энэ бүртгэл/төхөөрөмжид баталгаажаагүй | Зөвхөн харах"));
        }
        foreach (ProjectCloudSourceReference cloudSource in
                 (state.Project.Cloud.SharedSources ?? []).OfType<ProjectCloudSourceReference>())
        {
            if (StudioSourceRemovalOutbox.IsRegistryMirrorStaged(
                    state.Project,
                    cloudSource,
                    currentOwner,
                    StudioDeviceIdentity.Fingerprint))
            {
                continue;
            }
            string identity = CloudSourceIdentity(
                StudioSharedSourceProjection.ImmutableOwner(cloudSource),
                cloudSource.SourceKey);
            if (!representedCloudSources.Add(identity))
                continue;
            string name = string.IsNullOrWhiteSpace(cloudSource.SourceDocumentReference)
                ? cloudSource.SourceKey
                : cloudSource.SourceDocumentReference;
            ProjectCloudAlbumComponentReference? component = sharedComponents.FirstOrDefault(item =>
                CloudSourceIdentity(item.OwnerEmail, item.SourceKey).Equals(
                    identity,
                    StringComparison.OrdinalIgnoreCase));
            string placement = component is null
                ? "Альбумын байрлал хүлээгдэж байна"
                : $"{component.Label} · #{component.Order}";
            items.Add(SourceWorkspaceItem.Cloud(
                cloudSource,
                component,
                name,
                $"{cloudSource.SourceApplication} | {cloudSource.OwnerEmail} | " +
                $"{cloudSource.SheetCount} sheet | {placement} | Зөвхөн харах"));
        }
        foreach (ProjectCloudAlbumComponentReference component in
                 sharedComponents)
        {
            if (StudioSourceRemovalOutbox.IsStaged(
                    state.Project,
                    component.Code,
                    currentOwner,
                    StudioDeviceIdentity.Fingerprint))
            {
                continue;
            }
            string identity = CloudSourceIdentity(component.OwnerEmail, component.SourceKey);
            if (!representedCloudSources.Add(identity))
                continue;
            items.Add(SourceWorkspaceItem.Cloud(
                component,
                string.IsNullOrWhiteSpace(component.Label) ? component.SourceKey : component.Label,
                $"Cloud album slot | {component.OwnerEmail} | " +
                $"{component.PageNumbers.Count} page | Зөвхөн харах"));
        }
        bindingSourceWorkspaceSelection = true;
        try
        {
            designSourcesWorkspaceList.ItemsSource = items;
            designSourcesWorkspaceList.SelectedItem = items.FirstOrDefault(item =>
                string.Equals(item.SelectionKey, selectSourceId, StringComparison.OrdinalIgnoreCase));
            if (designSourcesWorkspaceList.SelectedItem is null && items.Count > 0)
            {
                designSourcesWorkspaceList.SelectedIndex = 0;
            }
        }
        finally
        {
            bindingSourceWorkspaceSelection = false;
        }

        RefreshReceivedSheetWorkspace();
        RefreshSourceDetails();
    }

    private void RefreshReceivedSheetWorkspace(string? selectSheetKey = null)
    {
        if (designSourcesWorkspaceList.SelectedItem is SourceWorkspaceItem { IsCloudPlaceholder: true })
        {
            CancelSourceSheetThumbnailLoading();
            receivedSheetsWorkspaceHost.Visibility = Visibility.Visible;
            visualizationImagesWorkspaceList.Visibility = Visibility.Collapsed;
            sourceContentTitle.Text = "Cloud эх үүсвэрийн байрлал";
            receivedSheetsWorkspaceList.ItemsSource = Array.Empty<SheetWorkspaceItem>();
            sourceSheetActionsPanel.Visibility = Visibility.Collapsed;
            sourceSheetSummaryText.Text = "";
            RefreshSourceSheetActionState();
            return;
        }

        bool visualizationsSelected =
            designSourcesWorkspaceList.SelectedItem is SourceWorkspaceItem { IsVisualization: true };
        receivedSheetsWorkspaceHost.Visibility = visualizationsSelected ? Visibility.Collapsed : Visibility.Visible;
        visualizationImagesWorkspaceList.Visibility = visualizationsSelected ? Visibility.Visible : Visibility.Collapsed;
        sourceContentTitle.Text = visualizationsSelected ? "Харагдах байдлын зураг" : "Хүлээн авсан sheets";
        if (visualizationsSelected)
        {
            CancelSourceSheetThumbnailLoading();
            sourceSheetActionsPanel.Visibility = Visibility.Collapsed;
            RefreshVisualizationImagesList();
            return;
        }

        ProjectDesignSource? selectedSource =
            (designSourcesWorkspaceList.SelectedItem as SourceWorkspaceItem)?.Source;
        var records = state.Library.Snapshot().AsEnumerable();
        if (selectedSource is not null)
        {
            records = selectedSource.UseLegacySheetKeys
                ? records.Where(record => string.IsNullOrWhiteSpace(record.SourceId))
                : records.Where(record => string.Equals(
                    record.SourceId,
                    selectedSource.Id,
                    StringComparison.OrdinalIgnoreCase));
        }

        List<SheetWorkspaceItem> items = records
            .Select(record => new SheetWorkspaceItem(
                record,
                record.Entry.Number,
                record.Entry.Name,
                ResolveSheetBuildingLabel(record),
                ResolveSheetSourceLabel(record),
                FormatSize(record.Entry.WidthMm, record.Entry.HeightMm),
                selectedSource?.IsSheetActive(record.Entry.SheetId) ?? true,
                selectedSource?.IsSheetActive(record.Entry.SheetId) == false
                    ? "Идэвхгүй"
                    : record.IsVerified ? "OK" : "Алдаа"))
            .ToList();
        receivedSheetsWorkspaceList.ItemsSource = items;
        if (!string.IsNullOrWhiteSpace(selectSheetKey))
        {
            SheetWorkspaceItem? selectedItem = items.FirstOrDefault(item =>
                string.Equals(
                    item.Record.Key,
                    selectSheetKey,
                    StringComparison.Ordinal));
            if (selectedItem is not null)
            {
                receivedSheetsWorkspaceList.SelectedItem = selectedItem;
                receivedSheetsWorkspaceList.ScrollIntoView(selectedItem);
            }
        }

        bool isPdfSource = selectedSource?.Kind == DesignSourceKind.Pdf;
        sourceSheetActionsPanel.Visibility = isPdfSource ? Visibility.Visible : Visibility.Collapsed;
        int activeCount = items.Count(item => item.IsActive);
        sourceSheetSummaryText.Text = isPdfSource
            ? $"{items.Count} хуудас · {activeCount} альбумд · {items.Count - activeCount} идэвхгүй"
            : "";
        RefreshSourceSheetActionState();

        CancelSourceSheetThumbnailLoading();
        if (items.Count > 0)
        {
            sourceSheetThumbnailLoadCancellation = new CancellationTokenSource();
            long loadSerial = Interlocked.Increment(ref sourceSheetThumbnailLoadSerial);
            _ = LoadSourceSheetThumbnailsAsync(
                items,
                loadSerial,
                sourceSheetThumbnailLoadCancellation.Token);
        }
    }

    private void RefreshSourceSheetActionState()
    {
        ProjectDesignSource? source =
            (designSourcesWorkspaceList.SelectedItem as SourceWorkspaceItem)?.Source;
        var selected = receivedSheetsWorkspaceList.SelectedItems
            .OfType<SheetWorkspaceItem>()
            .ToList();
        PdfSourcePageEditResolution editResolution =
            PdfSourcePageEditResolver.Resolve(
                source,
                selected.Select(item => item.Record).ToList(),
                state.HasOpenProject
                    ? state.Album.Pages
                    : Array.Empty<AlbumPageDefinition>());
        bool isPdfSource = source?.Kind == DesignSourceKind.Pdf;
        bool canEditProjectContent = CanEditProjectContent();
        excludeSelectedSourceSheetsButton.IsEnabled =
            canEditProjectContent &&
            isPdfSource &&
            selected.Any(item => item.IsActive);
        includeSelectedSourceSheetsButton.IsEnabled =
            canEditProjectContent &&
            isPdfSource &&
            selected.Any(item => !item.IsActive);
        editSelectedSourcePdfPageButton.IsEnabled =
            canEditProjectContent &&
            editResolution.IsButtonEnabled;
        editSelectedSourcePdfPageButton.ToolTip = canEditProjectContent
            ? PdfSourcePageEditToolTip(editResolution.State)
            : "Одоогийн Studio бүртгэлийн Cloud эрх шинэчлэгдээгүй тул PDF хуудсыг засах боломжгүй.";
    }

    private static string PdfSourcePageEditToolTip(PdfSourcePageEditState state) =>
        state switch
        {
            PdfSourcePageEditState.NoSelection =>
                "Засах нэг PDF хуудсаа сонгоно уу.",
            PdfSourcePageEditState.MultipleSelection =>
                "PDF хэсгийг засахдаа зөвхөн нэг хуудас сонгоно уу.",
            PdfSourcePageEditState.NotPdf =>
                "PDF хэсэг засах багаж зөвхөн PDF эх үүсвэрийн нэг хуудсанд ажиллана.",
            PdfSourcePageEditState.Inactive =>
                "Энэ PDF хуудас альбумд идэвхгүй байна. Эхлээд “Альбумд оруулах” товчийг дарна уу.",
            PdfSourcePageEditState.AlbumPageMissing =>
                "Энэ PDF хуудасны альбумын тохиргоо олдсонгүй.",
            PdfSourcePageEditState.AmbiguousAlbumPage =>
                "Энэ SheetKey-ээр альбумд давхардсан хуудас байна. Эх үүсвэрээс шинэчилж давхардлыг арилгасны дараа засна уу.",
            PdfSourcePageEditState.Ready =>
                "Сонгосон PDF хуудасны crop, mask, offset болон rotation-ийг засна.",
            _ => "Засах нэг PDF хуудсаа сонгоно уу.",
        };

    private void EditSelectedSourcePdfPage()
    {
        if (!EnsureProjectContentPermission())
        {
            return;
        }

        ProjectDesignSource? source =
            (designSourcesWorkspaceList.SelectedItem as SourceWorkspaceItem)?.Source;
        PdfSourcePageEditResolution resolution =
            PdfSourcePageEditResolver.Resolve(
                source,
                receivedSheetsWorkspaceList.SelectedItems
                    .OfType<SheetWorkspaceItem>()
                    .Select(item => item.Record)
                    .ToList(),
                state.HasOpenProject
                    ? state.Album.Pages
                    : Array.Empty<AlbumPageDefinition>());

        switch (resolution.State)
        {
            case PdfSourcePageEditState.NoSelection:
                SetStatus("Засах нэг PDF хуудсаа сонгоно уу.");
                return;
            case PdfSourcePageEditState.MultipleSelection:
                SetStatus("PDF хэсгийг засахдаа зөвхөн нэг хуудас сонгоно уу.");
                return;
            case PdfSourcePageEditState.NotPdf:
                SetStatus("PDF хэсэг засах багаж зөвхөн PDF эх үүсвэрийн хуудсанд ажиллана.");
                return;
            case PdfSourcePageEditState.Inactive:
                SetStatus(
                    "Сонгосон PDF хуудас альбумд идэвхгүй байна. " +
                    "Эхлээд “Альбумд оруулах” товчийг дарна уу.");
                return;
            case PdfSourcePageEditState.AlbumPageMissing:
                SetStatus(
                    "Сонгосон PDF хуудасны альбумын тохиргоо олдсонгүй. " +
                    "“Эх үүсвэр шалгах” үйлдлээр альбумын хуудсыг сэргээнэ үү.");
                return;
            case PdfSourcePageEditState.AmbiguousAlbumPage:
                SetStatus(
                    "Сонгосон PDF SheetKey-ээр альбумд нэгээс олон хуудас байна. " +
                    "Санамсаргүй хуудсыг засахаас хамгаалж үйлдлийг зогсоолоо.");
                return;
            case PdfSourcePageEditState.Ready:
                EditPdfSourcePage(resolution.Sheet!, resolution.Page!);
                return;
            default:
                return;
        }
    }

    private void SetSelectedSourceSheetsActive(bool active)
    {
        if (!EnsureProjectContentPermission() ||
            designSourcesWorkspaceList.SelectedItem is not SourceWorkspaceItem
            {
                Source: ProjectDesignSource { Kind: DesignSourceKind.Pdf } source,
            })
        {
            return;
        }

        string[] sheetIds = receivedSheetsWorkspaceList.SelectedItems
            .OfType<SheetWorkspaceItem>()
            .Where(item => item.IsActive != active)
            .Select(item => item.Record.Entry.SheetId)
            .Where(sheetId => !string.IsNullOrWhiteSpace(sheetId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sheetIds.Length == 0)
        {
            return;
        }

        state.SetSourceSheetActivity(source, sheetIds, active);
        RefreshReceivedSheetWorkspace();
        RefreshAlbumWorkspace(selectItemKey: selectedAlbumWorkspaceKey);
        UpdateAlbum(
            silent: false,
            statusPrefix: active
                ? $"{sheetIds.Length} PDF хуудас альбумд буцаан орлоо"
                : $"{sheetIds.Length} PDF хуудас эх үүсвэртээ үлдэж, альбумаас идэвхгүй боллоо");
    }

    private async Task LoadSourceSheetThumbnailsAsync(
        IReadOnlyList<SheetWorkspaceItem> items,
        long loadSerial,
        CancellationToken cancellationToken)
    {
        int renderedCount = 0;
        string? firstFailure = null;
        foreach (SheetWorkspaceItem item in items)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                int pageNumber = Math.Max(1, item.Record.Entry.PdfPageNumber);
                BitmapSource? image = await RenderSourceSheetThumbnailAsync(
                    sourceSheetPageImages,
                    item.Record.PdfPath,
                    pageNumber,
                    cancellationToken);
                if (!IsCurrentSourceSheetThumbnailLoad(loadSerial, cancellationToken))
                {
                    return;
                }

                await SetSourceSheetThumbnailAsync(
                    item,
                    image,
                    image is null ? "Урьдчилж харах боломжгүй" : "");
                if (image is not null)
                {
                    renderedCount++;
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception) when (IsSourceSheetThumbnailFailure(exception))
            {
                firstFailure ??= exception.Message;
                if (!IsCurrentSourceSheetThumbnailLoad(loadSerial, cancellationToken))
                {
                    return;
                }
                await SetSourceSheetThumbnailAsync(
                    item,
                    null,
                    "Урьдчилж харах боломжгүй");
            }
        }

        if (firstFailure is not null &&
            IsCurrentSourceSheetThumbnailLoad(loadSerial, cancellationToken))
        {
            SetStatus(
                $"PDF preview: {renderedCount}/{items.Count} хуудас бэлэн. " +
                $"Эхний алдаа: {firstFailure}");
        }
    }

    private static async Task<BitmapSource?> RenderSourceSheetThumbnailAsync(
        PdfPageImageCache pageImages,
        string pdfPath,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        const int attemptCount = 2;
        for (int attempt = 1; attempt <= attemptCount; attempt++)
        {
            try
            {
                return await pageImages.GetPageAsync(
                    pdfPath,
                    pageNumber,
                    300,
                    cancellationToken);
            }
            catch (Exception exception) when (
                attempt < attemptCount &&
                IsTransientSourceSheetThumbnailFailure(exception))
            {
                await Task.Delay(120, cancellationToken);
            }
        }
        return null;
    }

    private bool IsCurrentSourceSheetThumbnailLoad(
        long loadSerial,
        CancellationToken cancellationToken) =>
        !cancellationToken.IsCancellationRequested &&
        loadSerial == Volatile.Read(ref sourceSheetThumbnailLoadSerial);

    private async Task SetSourceSheetThumbnailAsync(
        SheetWorkspaceItem item,
        ImageSource? image,
        string message)
    {
        if (Root.Dispatcher.CheckAccess())
        {
            item.SetThumbnail(image, message);
            return;
        }

        await Root.Dispatcher.InvokeAsync(() => item.SetThumbnail(image, message));
    }

    private static bool IsTransientSourceSheetThumbnailFailure(Exception exception) =>
        exception is IOException or InvalidOperationException or COMException;

    private static bool IsSourceSheetThumbnailFailure(Exception exception) =>
        exception is IOException or
        UnauthorizedAccessException or
        InvalidOperationException or
        COMException or
        ArgumentException;

    private void InvalidateSourceSheetThumbnailLoad()
    {
        Interlocked.Increment(ref sourceSheetThumbnailLoadSerial);
    }

    private void CancelSourceSheetThumbnailLoading()
    {
        InvalidateSourceSheetThumbnailLoad();
        sourceSheetThumbnailLoadCancellation?.Cancel();
        sourceSheetThumbnailLoadCancellation?.Dispose();
        sourceSheetThumbnailLoadCancellation = null;
    }

    private string ResolveSheetBuildingLabel(SheetRecord record)
    {
        string assignedName = ProjectBuildingComposition.ResolveAssignedGroupName(
            record.Key,
            state.Project.BuildingGroups,
            state.Project.SheetBuildingAssignments);
        if (!string.IsNullOrWhiteSpace(assignedName))
        {
            return assignedName;
        }
        if (!string.IsNullOrWhiteSpace(record.Entry.BuildingName))
        {
            return record.Entry.BuildingName.Trim();
        }
        if (!string.IsNullOrWhiteSpace(record.Entry.BuildingId))
        {
            return record.Entry.BuildingId.Trim();
        }
        return "Оноогоогүй";
    }

    private void RefreshSourceDetails()
    {
        SetNativeSourceActionsVisible(false);
        if (designSourcesWorkspaceList.SelectedItem is not SourceWorkspaceItem selected)
        {
            sourceDetailsText.Text = "Эх үүсвэр сонгоно уу.";
            sourceWorkflowText.Text = "";
            openNativeSourceButton.Visibility = Visibility.Collapsed;
            openSourceFolderButton.Visibility = Visibility.Collapsed;
            visualizationSourceControls.Visibility = Visibility.Collapsed;
            return;
        }

        if (selected.IsVisualization)
        {
            RefreshVisualizationSourceDetails();
            return;
        }

        if (selected.IsCloudPlaceholder)
        {
            ProjectDesignSource? localBinding = selected.Source;
            ProjectCloudSourceReference? cloudSource = selected.CloudSource;
            ProjectCloudAlbumComponentReference? component = selected.CloudComponent;
            string owner = cloudSource is null
                ? component?.OwnerEmail ??
                  (localBinding is null
                      ? ""
                      : ProjectCloudSyncMetadata.CloudOwnerEmail(localBinding))
                : StudioSharedSourceProjection.ImmutableOwner(cloudSource);
            string sourceKey = cloudSource?.SourceKey ??
                component?.SourceKey ??
                (localBinding is null
                    ? ""
                    : ProjectCloudSyncMetadata.CloudSourceKey(localBinding));
            int itemCount = component?.PageNumbers.Count ?? cloudSource?.SheetCount ?? 0;
            sourceDetailsText.Text =
                $"Төлөв: Cloud эх үүсвэр\n" +
                $"Эх үүсвэр: {selected.Name}\n" +
                $"Эзэмшигч: {(string.IsNullOrWhiteSpace(owner) ? "-" : owner)}\n" +
                $"Source key: {(string.IsNullOrWhiteSpace(sourceKey) ? "-" : sourceKey)}\n" +
                $"Альбумын дараалал: {(component?.Order.ToString() ?? "-")}\n" +
                $"Хуудас / sheet: {itemCount}";
            sourceWorkflowText.Text =
                localBinding is null
                    ? "Энэ нь Cloud ERA-аас ирсэн metadata placeholder. Эх файл дамжуулагдаагүй."
                    : "Энэ бүртгэл/төхөөрөмжид баталгаатай локал payload байхгүй. " +
                      "Баталгаажсан хариуцагч бол “Эх файлыг солих” үйлдлээр зориуд дахин холбоно.";
            openNativeSourceButton.Visibility = Visibility.Collapsed;
            openSourceFolderButton.Visibility = Visibility.Collapsed;
            visualizationSourceControls.Visibility = Visibility.Collapsed;
            if (localBinding is not null)
            {
                SetNativeSourceActionsVisible(
                    hasNativeSource: true,
                    ownsSource: CanControlSource(localBinding),
                    hasLocalPayload: false);
            }
            return;
        }

        var source = selected.Source!;
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
        openNativeSourceButton.Visibility = string.IsNullOrWhiteSpace(source.NativeDocumentPath)
            ? Visibility.Collapsed
            : Visibility.Visible;
        openSourceFolderButton.Visibility = Visibility.Visible;
        visualizationSourceControls.Visibility = Visibility.Collapsed;
        SetNativeSourceActionsVisible(true, CanEditLocalSource(source));
        string workflowHint = source.Kind switch
        {
            DesignSourceKind.Revit when sheetCount == 0 =>
                "RVT холбоос бэлэн. Revit дээр файлаа нээгээд Erk-S Platform > Альбум > Studio руу илгээхэд хуудаснууд энд автоматаар орж ирнэ.",
            DesignSourceKind.Revit =>
                "Revit дээр өөрчлөлт хийсний дараа Erk-S Platform > Альбум > Studio руу илгээхэд нэмэгдсэн, өөрчлөгдсөн, хасагдсан хуудаснууд автоматаар шинэчлэгдэнэ.",
            _ => "Native эх файл Studio болон Cloud ERA руу хуулагдахгүй; зөвхөн энэ төхөөрөмж дээрх холбоос хадгалагдана.",
        };
        // A delivery that arrived while the project was closed waits in the
        // inbox, and until something says so the project looks exactly as it
        // would if the drawing had never been sent.
        PendingSourcePackageSurvey pending = SurveyPendingDeliveries(source);
        // Visuals arrive in the same folder under their own name, and a user
        // who has been told about one waiting delivery will expect to be told
        // about the other.
        PendingVisualPackageSurvey pendingVisuals = VisualInboxScanner.Survey(
            source.InboxFolder,
            VisualInboxScanner.AbsorbedUpTo(state.Project.Portfolio, source.Id));

        var notices = new List<string>();
        if (pending.Any)
            notices.Add(DescribePendingDeliveries(pending));
        if (pendingVisuals.HasPending)
            notices.Add(DescribePendingVisuals(pendingVisuals));
        notices.Add(workflowHint);
        sourceWorkflowText.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            notices);
    }

    private static string DescribePendingVisuals(PendingVisualPackageSurvey pending)
    {
        string arrived = pending.NewestExportedAtUtc is { } newest
            ? newest.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "";
        string when = arrived.Length > 0 ? $" (сүүлийнх {arrived})" : "";
        return $"⚠ {pending.Count} визуал багц хүлээгдэж байна{when}. " +
            "Төслөө нээхэд визуалууд автоматаар орж ирнэ.";
    }

    /// <summary>
    /// What this source has waiting in its inbox that the project has not taken
    /// in. Read from manifest headers only - whether a delivery is any good is
    /// intake's question, not this one's.
    /// </summary>
    private PendingSourcePackageSurvey SurveyPendingDeliveries(ProjectDesignSource source)
    {
        ProjectSourceSyncCandidate? recorded = ProjectCloudSyncMetadata
            .SourcePackages(state.Project)
            .FirstOrDefault(candidate => candidate.Source.Id.Equals(
                source.Id,
                StringComparison.OrdinalIgnoreCase));
        return SourceInboxScanner.Survey(
            source.InboxFolder,
            recorded?.ManifestId ?? "",
            recorded?.ExportedAtUtc);
    }

    private static string DescribePendingDeliveries(PendingSourcePackageSurvey pending)
    {
        string arrived = pending.NewestExportedAtUtc is { } newest
            ? newest.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : "";
        string when = arrived.Length > 0 ? $" (сүүлийнх {arrived})" : "";
        return $"26A0 {pending.Count} шинэ багц хүлээгдэж байна{when}. " +
            "«Эх үүсвэрээс шинэчлэх» дарвал хуудаснууд орж ирнэ.";
    }

    private void SetNativeSourceActionsVisible(
        bool hasNativeSource,
        bool ownsSource = false,
        bool hasLocalPayload = true)
    {
        Visibility sourceVisibility = hasNativeSource ? Visibility.Visible : Visibility.Collapsed;
        relinkNativeSourceButton.Visibility = sourceVisibility;
        removeDesignSourceButton.Visibility = sourceVisibility;

        bool cloudProject = hasNativeSource &&
            state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId);
        bindCloudSourceButton.Visibility = cloudProject ? Visibility.Visible : Visibility.Collapsed;
        transferSourceCustodyButton.Visibility = cloudProject && CanManageProjectTeam()
            ? Visibility.Visible
            : Visibility.Collapsed;

        bool canControl = hasNativeSource && ownsSource && CanEditProjectContent();
        bool canEditLocalPayload = canControl && hasLocalPayload;
        relinkNativeSourceButton.IsEnabled = canControl;
        removeDesignSourceButton.IsEnabled = canEditLocalPayload;
        bindCloudSourceButton.IsEnabled =
            canEditLocalPayload && account.IsSignedIn;
        transferSourceCustodyButton.IsEnabled = cloudProject && CanManageProjectTeam();
    }

    private bool CanControlSource(ProjectDesignSource source)
        => StudioSourceRefreshScope.CanRefresh(
            state.Project,
            source,
            account.Current?.Email);

    private bool CanEditLocalSource(ProjectDesignSource source) =>
        CanControlSource(source) &&
        StudioLocalSourceBindingPolicy.IsLocal(
            source,
            account.Current?.Email,
            StudioDeviceIdentity.Fingerprint,
            StudioLocalSourceBindingPolicy.HasVerifiedPayload(source));

    private static string CloudSourceIdentity(string ownerEmail, string sourceKey) =>
        $"{(ownerEmail ?? "").Trim().ToLowerInvariant()}\n{(sourceKey ?? "").Trim().ToLowerInvariant()}";

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
        workspace.ColumnDefinitions.Add(albumProjectChatColumn);
        Grid.SetRow(workspace, 1);
        root.Children.Add(workspace);

        albumPagesWorkspaceList.BorderThickness = new Thickness(0);
        albumPagesWorkspaceList.SelectionMode = SelectionMode.Extended;
        albumPagesWorkspaceList.HorizontalContentAlignment = HorizontalAlignment.Stretch;
        albumPagesWorkspaceList.SetValue(
            ScrollViewer.HorizontalScrollBarVisibilityProperty,
            ScrollBarVisibility.Disabled);
        albumPagesWorkspaceList.ItemTemplate = CreateAlbumPageItemTemplate(thumbnailMode: false);
        albumPagesWorkspaceList.SelectionChanged += (_, _) => HandleAlbumWorkspaceSelection();
        albumPagesWorkspaceList.PreviewMouseLeftButtonDown += HandleAlbumNavigatorMouseDown;
        albumPagesWorkspaceList.KeyDown += HandleAlbumNavigatorKeyDown;
        albumPreviewHost.Background = new SolidColorBrush(Color.FromRgb(54, 58, 64));
        var primaryWorkspace = new Grid();
        foreach (StudioAlbumWorkspacePane pane in StudioAlbumWorkspaceLayout.PrimaryPanes)
        {
            primaryWorkspace.ColumnDefinitions.Add(pane switch
            {
                StudioAlbumWorkspacePane.Preview => new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star),
                    MinWidth = StudioAlbumWorkspaceLayout.PreviewMinimumWidth,
                },
                StudioAlbumWorkspacePane.Properties => new ColumnDefinition
                {
                    Width = new GridLength(StudioAlbumWorkspaceLayout.PropertiesWidth),
                    MinWidth = StudioAlbumWorkspaceLayout.PropertiesMinimumWidth,
                },
                _ => throw new InvalidOperationException($"Unsupported album workspace pane: {pane}"),
            });
        }

        UIElement previewPane = BuildPane(
            "Альбумын бодит харагдац",
            albumPreviewHost,
            new Thickness(0));
        primaryWorkspace.Children.Add(previewPane);
        Grid.SetColumn(previewPane, 0);

        UIElement propertiesPane = BuildPane(
            "Альбумын тохиргоо",
            BuildAlbumProperties(),
            new Thickness(1, 0, 0, 0));
        primaryWorkspace.Children.Add(propertiesPane);
        Grid.SetColumn(propertiesPane, 1);

        workspace.Children.Add(primaryWorkspace);
        Grid.SetColumn(albumProjectChatHost, 1);
        workspace.Children.Add(albumProjectChatHost);
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
            if (!bindingAlbumPage && CanEditProjectContent())
            {
                state.Album.Title = string.IsNullOrWhiteSpace(albumTitleBox.Text)
                    ? "Project album"
                    : albumTitleBox.Text.Trim();
            }
        };
        documentGroup.Children.Add(albumTitleBox);
        var save = StudioWidgets.CreateIconTextButton("icon-project.svg", "Хадгалах");
        save.Click += (_, _) => SaveProject();
        var updateAlbum = StudioWidgets.CreateIconTextButton("icon-album.svg", "Эх үүсвэрээс шинэчлэх");
        updateAlbum.ToolTip =
            "Бүх локал linked source-ийг шалгаж, өөрчлөгдсөн мэдээллээр album-ыг дахин бүрдүүлнэ. " +
            "Устсан source-ийн агуулга хуудсанд үлдэхгүй. Cloud мэдээлэл татахгүй.";
        updateAlbum.Background = StudioTheme.AccentBrush;
        updateAlbum.BorderBrush = StudioTheme.AccentBrush;
        updateAlbum.Click += (_, _) => CheckForSourceUpdates();
        var rebuildAlbum = StudioWidgets.CreateIconTextButton(
            "icon-publish.svg",
            "Бүрэн дахин байгуулах");
        rebuildAlbum.ToolTip =
            "Хуудсуудыг эх үүсвэрээс шинээр зурна. Cloud альбомын хуудас хуучин хувилбараар " +
            "зурагдсан бол ердийн шинэчлэлт түүнийг хэвээр нь авч үлддэг; энэ үйлдэл " +
            "энэ төхөөрөмжийн эзэмшдэг хэсгүүдийг дахин зурж, Sync-ээр солиход бэлдэнэ.";
        rebuildAlbum.Click += (_, _) => RebuildAlbumFromSource();
        var editVisualizations = StudioWidgets.CreateIconTextButton(
            "icon-sources.svg",
            "Харагдах байдал",
            "Альбумын хуудсан дээрх зургуудыг сонгож идэвхгүй болгох эсвэл буцаан оруулах");
        editVisualizations.Click += (_, _) => EditVisualizationAlbumPages();
        var editSiteContext = StudioWidgets.CreateIconTextButton(
            "icon-project.svg",
            "Байршлын зураг",
            "Байршлын схем болон орчны тоймын хамрах хүрээг тохируулна");
        editSiteContextButton = editSiteContext;
        RefreshSiteContextEditUi();
        editSiteContext.Click += (_, _) => EditSiteContextMaps();
        var elevationInformation = StudioWidgets.CreateIconTextButton(
            "icon-project.svg",
            "Дээд мэдээлэл");
        elevationInformation.ToolTip =
            "Сонгосон Нүүр тал эсвэл Ерөнхий төлөвлөгөөний 55 мм дээд бүсийн тайлбарыг засна. " +
            "БАТЛАВ болон ХЯНАВ нь баталгаажуулалтын мэдээллээс уншигдана.";
        elevationInformation.Click += (_, _) => EditSelectedElevationSheetInformation();
        var open = StudioWidgets.CreateButton("PDF нээх");
        open.Click += (_, _) =>
        {
            string? previewPath = ResolveAlbumPreviewPath();
            if (!string.IsNullOrWhiteSpace(previewPath))
            {
                Process.Start(new ProcessStartInfo(previewPath) { UseShellExecute = true });
            }
        };
        documentGroup.Children.Add(save);
        documentGroup.Children.Add(updateAlbum);
        documentGroup.Children.Add(rebuildAlbum);
        documentGroup.Children.Add(editSiteContext);
        documentGroup.Children.Add(editVisualizations);
        documentGroup.Children.Add(elevationInformation);
        documentGroup.Children.Add(open);
        autoRebuildCheck.Content = "Auto шинэчлэлт";
        autoRebuildCheck.ToolTip = "Эх үүсвэр өөрчлөгдөхөд альбумыг автоматаар шинэчилнэ.";
        autoRebuildCheck.Margin = new Thickness(8, 0, 0, 0);
        autoRebuildCheck.VerticalAlignment = VerticalAlignment.Center;
        documentGroup.Children.Add(autoRebuildCheck);
        ribbon.Children.Add(documentGroup);
        return ribbon;
    }

    private async void EditSiteContextMaps()
    {
        if (!EnsureSiteContextEditPermission())
            return;
        if (inlineSiteContextEditor is not null)
            return;

        const string siteContextSelectionKey = "component:site-context:None:1";
        RefreshAlbumWorkspace(selectItemKey: siteContextSelectionKey);
        AlbumPageWorkspaceItem? siteContextItem = albumPagesWorkspaceList.Items
            .OfType<AlbumPageWorkspaceItem>()
            .FirstOrDefault(item =>
                !item.IsGroup &&
                item.Component?.GeneratedPageKind == AlbumGeneratedPageKind.SiteContext);
        if (siteContextItem is null)
        {
            SetStatus("Байршлын схем / Орчны тойм хуудас альбумын бүрдэлд алга байна.");
            return;
        }

        albumPagesWorkspaceList.SelectedItem = siteContextItem;
        selectedAlbumWorkspaceKey = siteContextItem.SelectionKey;
        BindSelectedAlbumPage();

        BitmapSource? pageBackgroundSource = null;
        string? pageBackgroundPdfPath = ResolveAlbumPreviewPath();
        int? sharedPageNumber = ResolveSharedAlbumComponentPage(
            ProjectCloudSyncMetadata.SiteContextComponentCode);
        int? builtPageNumber = sharedPageNumber ??
                               siteContextItem.BuiltPageNumber ??
                               ResolveBuiltAlbumPage(siteContextItem);
        if (!string.IsNullOrWhiteSpace(pageBackgroundPdfPath) &&
            builtPageNumber.HasValue)
        {
            try
            {
                // Keep this one-page render independent from the long-lived
                // thumbnail cache. Album refreshes can switch that cache's PDF
                // while this inline editor is being prepared.
                var pageBackgroundImages = new PdfPageImageCache();
                pageBackgroundSource = await pageBackgroundImages.GetPageAsync(
                    pageBackgroundPdfPath,
                    builtPageNumber.Value,
                    1800,
                    CancellationToken.None);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidOperationException or UnauthorizedAccessException)
            {
                SetStatus($"Хуудасны фон уншсангүй: {exception.Message}");
            }
        }

        var editor = new SiteContextMapEditorControl(
            state.ResolveProjectFolder(),
            state.Project.ProjectId,
            state.Project.SiteContext,
            pageBackgroundSource);
        inlineSiteContextEditor = editor;
        UpdateProjectChatWidgetVisibility();
        inlineSiteContextPersisted = false;
        editor.SiteContextSaved += snapshot =>
        {
            if (!EnsureSiteContextEditPermission())
                return;
            state.Project.SiteContext = snapshot;
            state.MarkSiteContextChanged();
            inlineSiteContextPersisted = true;
        };
        editor.Completed += saved => CompleteInlineSiteContextEditing(editor, saved);

        albumPdfNavigationSerial++;
        albumPreviewHost.Children.Clear();
        albumPreviewHost.Children.Add(editor);
        albumPagesWorkspaceList.IsEnabled = false;
        SetStatus(pageBackgroundSource is null
            ? "Байршлын схемийн суурь хуудас уншигдсангүй. Газрын зургийг A3 талбар дээр засварлаж байна."
            : "Байршлын схемийн хуудсыг өөр дээр нь засварлаж байна.");
    }

    private void CompleteInlineSiteContextEditing(SiteContextMapEditorControl editor, bool saved)
    {
        if (!ReferenceEquals(inlineSiteContextEditor, editor))
            return;

        if (saved && !EnsureSiteContextEditPermission())
            saved = false;
        if (saved && !inlineSiteContextPersisted)
        {
            state.Project.SiteContext = editor.Result;
            state.MarkSiteContextChanged();
        }

        inlineSiteContextEditor = null;
        UpdateProjectChatWidgetVisibility();
        inlineSiteContextPersisted = false;
        albumPagesWorkspaceList.IsEnabled = true;
        albumPreviewHost.Children.Remove(editor);
        editor.Dispose();

        const string siteContextSelectionKey = "component:site-context:None:1";
        RefreshAlbumWorkspace(selectItemKey: siteContextSelectionKey);
        if (!saved)
        {
            RefreshAlbumPagePreview();
            SetStatus("Байршлын схемийн засварыг болилоо.");
            return;
        }

        UpdateAlbum(
            silent: false,
            statusPrefix: "Байршлын схем болон орчны тойм шинэчлэгдлээ");
    }

    private ProjectSiteContextEditAuthority ResolveSiteContextEditAuthority() =>
        state.HasOpenProject
            ? ProjectSiteContextEditingPolicy.Resolve(
                state.Project,
                account.Current?.Email)
            : new ProjectSiteContextEditAuthority(
                false,
                "",
                "",
                "",
                "Төсөл нээгээгүй байна.");

    private bool EnsureSiteContextEditPermission()
    {
        ProjectSiteContextEditAuthority authority = ResolveSiteContextEditAuthority();
        if (authority.CanEdit)
            return true;
        SetStatus(authority.Message);
        return false;
    }

    private void RefreshSiteContextEditUi()
    {
        if (editSiteContextButton is null)
            return;

        ProjectSiteContextEditAuthority authority = ResolveSiteContextEditAuthority();
        editSiteContextButton.IsEnabled = authority.CanEdit;
        editSiteContextButton.ToolTip = authority.CanEdit
            ? "Ерөнхий төлөвлөгөөний эх үүсвэрээр байршлын схем болон орчны тоймыг тохируулна."
            : authority.Message;
        ToolTipService.SetShowOnDisabled(editSiteContextButton, true);
    }

    private void EditSelectedElevationSheetInformation()
    {
        if (!state.HasOpenProject || !CanEditProjectContent())
            return;
        if (albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem
            {
                IsGroup: false,
                Page: AlbumPageDefinition page,
            } selected)
        {
            SetStatus("Тайлбар засах нүүр талын хуудсаа сонгоно уу.");
            return;
        }

        SheetRecord? sheet = state.Library.Find(page.SheetKey);
        if (sheet == null || !BuildingArchitectureConceptPageLayout.UsesInformationHeader(
                AlbumPageSourceMetadata.ResolveContentKind(page, sheet.Entry),
                sheet.Entry.Name,
                page.TemplateSlotId))
        {
            SetStatus("Энэ үйлдэл 55 мм дээд мэдээллийн бүстэй хуудсанд хамаарна.");
            return;
        }

        ConceptElevationHeaderSnapshot roster = ConceptElevationHeaderResolver.Resolve(
            state.Project.Foundation.ApprovalWorkflow,
            state.Project.Foundation.PlanningTask);
        var dialog = new ElevationSheetInformationDialog(
            selected.Number,
            selected.Title,
            sheet.Entry.SheetDescription,
            page.ElevationDescriptionOverride,
            roster)
        {
            Owner = Window.GetWindow(Root),
        };
        if (dialog.ShowDialog() != true)
            return;

        page.ElevationDescriptionOverride = dialog.DescriptionOverride;
        state.SaveProject();
        RefreshAlbumWorkspace(selectItemKey: selected.SelectionKey);
        UpdateAlbum(silent: false, statusPrefix: "Хуудасны дээд мэдээлэл хадгалагдлаа");
    }

    private UIElement BuildAlbumProperties()
    {
        IReadOnlyList<ModuleCountChoice> moduleCounts = Enumerable.Range(
                WorkingDrawingAlbumFormatFactory.MinimumModuleCount,
                WorkingDrawingAlbumFormatFactory.MaximumModuleCount)
            .Select(value => new ModuleCountChoice(value, value.ToString()))
            .ToList();
        albumGeneratedFormatColumnsBox.ItemsSource = moduleCounts;
        albumGeneratedFormatRowsBox.ItemsSource = moduleCounts;
        albumPageFormatBox.ItemsSource = PageFormatCatalog.All;
        albumPlacementBox.ItemsSource = new[]
        {
            new PlacementChoice(PagePlacementMode.PreserveDrawingSpace, "1:1 цэвэр зургийн талбай"),
            new PlacementChoice(PagePlacementMode.PreservePhysicalSize, "PDF бодит хэмжээ (1:1)"),
            new PlacementChoice(PagePlacementMode.FitDrawingArea, "Зургийн талбайд багтаах"),
            new PlacementChoice(PagePlacementMode.FillCrop, "Талбайг дүүргэж тайрах"),
            new PlacementChoice(PagePlacementMode.FullPage, "Хуудсыг бүтэн дүүргэх"),
        };
        albumPdfPageSizeBox.ItemsSource = new[]
        {
            new PdfPageSizeChoice(PdfSourcePageFormatFactory.SourceCode, "Эх PDF хэмжээгээр"),
            new PdfPageSizeChoice("A4", "A4"),
            new PdfPageSizeChoice("A3", "A3"),
            new PdfPageSizeChoice("A2", "A2"),
            new PdfPageSizeChoice("A1", "A1"),
            new PdfPageSizeChoice("A0", "A0"),
            new PdfPageSizeChoice(PdfSourcePageFormatFactory.CustomCode, "Тусгай хэмжээ"),
        };
        albumPdfOrientationBox.ItemsSource = new[]
        {
            new PdfFormatValueChoice("LANDSCAPE", "Хөндлөн"),
            new PdfFormatValueChoice("PORTRAIT", "Босоо"),
        };
        albumPdfBindEdgeBox.ItemsSource = new[]
        {
            new PdfFormatValueChoice("LEFT", "Зүүн"),
            new PdfFormatValueChoice("TOP", "Дээд"),
            new PdfFormatValueChoice("RIGHT", "Баруун"),
            new PdfFormatValueChoice("BOTTOM", "Доод"),
        };

        albumPageFormatBox.SelectionChanged += (_, _) => ApplyAlbumPageProperties();
        albumPlacementBox.SelectionChanged += (_, _) => ApplyAlbumPageProperties();
        albumSectionBox.SelectionChanged += (_, _) => ApplyAlbumPageProperties();
        albumContentKindBox.SelectionChanged += (_, _) => ApplyAlbumPageProperties();
        albumPageNumberBox.TextChanged += (_, _) => ApplyAlbumPageProperties();
        albumPageTitleBox.TextChanged += (_, _) => ApplyAlbumPageProperties();
        albumPdfPageSizeBox.SelectionChanged += (_, _) => RefreshPdfFormatControls();
        albumPdfApplyFormatButton.Click += (_, _) => ApplyPdfPageFormat();
        albumPdfEditPageButton.Click += (_, _) => EditPdfSourcePage();
        albumSourceCropCheck.Checked += (_, _) =>
        {
            RefreshAlbumSourceCropControls();
            ApplyAlbumPageProperties();
        };
        albumSourceCropCheck.Unchecked += (_, _) =>
        {
            RefreshAlbumSourceCropControls();
            ApplyAlbumPageProperties();
        };
        albumCropLeftBox.LostKeyboardFocus += (_, _) => ApplyAlbumPageProperties();
        albumCropTopBox.LostKeyboardFocus += (_, _) => ApplyAlbumPageProperties();
        albumCropRightBox.LostKeyboardFocus += (_, _) => ApplyAlbumPageProperties();
        albumCropBottomBox.LostKeyboardFocus += (_, _) => ApplyAlbumPageProperties();
        albumCropFromDrawingAreaButton.Click += (_, _) => ApplyDrawingAreaCropPreset();
        albumGeneratedFormatColumnsBox.SelectionChanged += (_, _) =>
            ApplyAlbumGeneratedPageFormat();
        albumGeneratedFormatRowsBox.SelectionChanged += (_, _) =>
            ApplyAlbumGeneratedPageFormat();
        includeCoverCheck.Checked += (_, _) => ApplyAlbumOptions();
        includeCoverCheck.Unchecked += (_, _) => ApplyAlbumOptions();
        includeTocCheck.Checked += (_, _) => ApplyAlbumOptions();
        includeTocCheck.Unchecked += (_, _) => ApplyAlbumOptions();

        var panel = new StackPanel { Margin = new Thickness(0, 0, 2, 0) };
        // Above the page's own properties: a comment is about the drawing, not
        // a property of it, and it is offered for every sheet rather than only
        // for the ones this account may edit.
        albumSheetCommentButton.Margin = new Thickness(0, 0, 0, 10);
        albumSheetCommentButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        albumSheetCommentButton.Click += (_, _) => ToggleSheetMarkup();
        panel.Children.Add(albumSheetCommentButton);

        // The page these settings belong to, chosen here rather than in a list
        // beside the drawing. The viewer can be told to go to a page but never
        // asked which page is on screen, so the choice has to be made on this
        // side; picking one moves the viewer to it.
        albumPageSelectorBox.Margin = new Thickness(0, 0, 0, 4);
        albumPageSelectorBox.DisplayMemberPath = nameof(AlbumPageChoice.Label);
        albumPageSelectorBox.SelectionChanged += (_, _) => ApplyAlbumPageSelection();
        panel.Children.Add(StudioWidgets.CreateFormRow("Хуудас", albumPageSelectorBox, 76));
        albumPageOwnerText.FontSize = 11;
        albumPageOwnerText.Foreground = StudioTheme.MutedTextBrush;
        albumPageOwnerText.TextWrapping = TextWrapping.Wrap;
        albumPageOwnerText.Margin = new Thickness(80, 0, 0, 12);
        panel.Children.Add(albumPageOwnerText);

        panel.Children.Add(StudioWidgets.CreateFormRow("Дугаар", albumPageNumberBox, 76));
        panel.Children.Add(StudioWidgets.CreateFormRow("Нэр", albumPageTitleBox, 76));
        panel.Children.Add(StudioWidgets.CreateFormRow("Зургийн төрөл", albumContentKindBox, 76));
        panel.Children.Add(StudioWidgets.CreateFormRow("Бүлэг", albumSectionBox, 76));
        panel.Children.Add(BuildAlbumPageRolePanel());

        albumPdfFormatPanel.Children.Add(
            StudioWidgets.CreateSectionHeader("PDF хуудасны формат"));
        albumPdfFormatPanel.Children.Add(
            StudioWidgets.CreateFormRow("Хэмжээ", albumPdfPageSizeBox, 76));
        albumPdfFormatPanel.Children.Add(
            StudioWidgets.CreateFormRow("Чиглэл", albumPdfOrientationBox, 76));
        albumPdfFormatPanel.Children.Add(
            StudioWidgets.CreateFormRow("Нуруулдах", albumPdfBindEdgeBox, 76));
        albumPdfFormatPanel.Children.Add(
            StudioWidgets.CreateFormRow("Зургийн масштаб", albumPdfDrawingScaleBox, 76));
        albumPdfCustomSizePanel.Children.Add(
            StudioWidgets.CreateFormRow("Өргөн (мм)", albumPdfCustomWidthBox, 76));
        albumPdfCustomSizePanel.Children.Add(
            StudioWidgets.CreateFormRow("Өндөр (мм)", albumPdfCustomHeightBox, 76));
        albumPdfFormatPanel.Children.Add(albumPdfCustomSizePanel);
        albumPdfApplyFormatButton.Margin = new Thickness(0, 5, 0, 8);
        albumPdfFormatPanel.Children.Add(albumPdfApplyFormatButton);
        albumPdfFormatPanel.Children.Add(StudioWidgets.CreateHint(
            "PDF зураг crop хийсний дараах мм хэмжээгээр 1:1 байрлана. " +
            "100 гэж оруулбал булангийн хүснэгтэд 1:100 гэж бичигдэх бөгөөд " +
            "зураг өөрөө жижигрэхгүй, томрохгүй."));

        albumPdfFormatPanel.Children.Add(
            StudioWidgets.CreateSectionHeader("PDF эх хуудасны хэсэг"));
        albumPdfEditPageButton.Margin = new Thickness(0, 0, 0, 8);
        albumPdfFormatPanel.Children.Add(albumPdfEditPageButton);
        albumPdfFormatPanel.Children.Add(StudioWidgets.CreateHint(
            "Бодит хуудсан дээр хэрэгтэй хүрээг сонгож, хуучин хүрээ болон " +
            "булангийн хүснэгтийг олон тэгш өнцөгт эсвэл чөлөөт маскаар халхална."));
        albumPdfFormatPanel.Children.Add(albumSourceCropCheck);
        albumSourceCropPanel.Margin = new Thickness(0, 5, 0, 8);
        albumSourceCropPanel.Children.Add(
            StudioWidgets.CreateFormRow("Зүүн (мм)", albumCropLeftBox, 76));
        albumSourceCropPanel.Children.Add(
            StudioWidgets.CreateFormRow("Дээд (мм)", albumCropTopBox, 76));
        albumSourceCropPanel.Children.Add(
            StudioWidgets.CreateFormRow("Баруун (мм)", albumCropRightBox, 76));
        albumSourceCropPanel.Children.Add(
            StudioWidgets.CreateFormRow("Доод (мм)", albumCropBottomBox, 76));
        albumCropFromDrawingAreaButton.Margin = new Thickness(0, 5, 0, 0);
        albumSourceCropPanel.Children.Add(albumCropFromDrawingAreaButton);
        albumPdfFormatPanel.Children.Add(albumSourceCropPanel);
        albumPdfFormatPanel.Visibility = Visibility.Collapsed;
        panel.Children.Add(albumPdfFormatPanel);
        panel.Children.Add(StudioWidgets.CreateSectionHeader("Альбум"));
        albumGeneratedFormatPanel.Children.Add(
            StudioWidgets.CreateSectionHeader("Нийтлэг хуудасны формат"));
        albumGeneratedFormatPanel.Children.Add(
            StudioWidgets.CreateFormRow("A3 багана", albumGeneratedFormatColumnsBox, 92));
        albumGeneratedFormatPanel.Children.Add(
            StudioWidgets.CreateFormRow("A3 мөр", albumGeneratedFormatRowsBox, 92));
        albumGeneratedFormatPanel.Children.Add(albumGeneratedFormatSummaryText);
        albumGeneratedFormatPanel.Children.Add(StudioWidgets.CreateHint(
            "Studio-оос үүсэх нүүр, зургийн жагсаалт, тайлбар бичиг, байршлын схем, " +
            "орчны тойм болон цаашид нэмэгдэх хуудсуудад үйлчилнэ. A3 модулиуд 12 мм " +
            "залгаастай; ажлын зургийн 180×36 мм хэвтээ булангийн хүснэгтийг хадгална."));
        panel.Children.Add(albumGeneratedFormatPanel);
        panel.Children.Add(includeCoverCheck);
        panel.Children.Add(includeTocCheck);
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
        RefreshSiteContextEditUi();
        bool canEditProjectContent = CanEditProjectContent();
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
        RefreshAlbumPageChoices();
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
        albumTitleBox.IsReadOnly = !canEditProjectContent;
        includeCoverCheck.IsChecked = state.Album.IncludeCover;
        includeTocCheck.IsChecked = state.Album.IncludeTableOfContents;
        includeCoverCheck.IsEnabled = canEditProjectContent;
        includeTocCheck.IsEnabled = canEditProjectContent;
        autoRebuildCheck.IsEnabled = canEditProjectContent;
        PageFormatDefinition generatedFormat =
            WorkingDrawingAlbumFormatFactory.Resolve(state.Album);
        albumGeneratedFormatColumnsBox.SelectedItem =
            albumGeneratedFormatColumnsBox.Items
                .Cast<ModuleCountChoice>()
                .FirstOrDefault(choice => choice.Value == Math.Max(
                    WorkingDrawingAlbumFormatFactory.MinimumModuleCount,
                    generatedFormat.ModuleColumns));
        albumGeneratedFormatRowsBox.SelectedItem =
            albumGeneratedFormatRowsBox.Items
                .Cast<ModuleCountChoice>()
                .FirstOrDefault(choice => choice.Value == Math.Max(
                    WorkingDrawingAlbumFormatFactory.MinimumModuleCount,
                    generatedFormat.ModuleRows));
        albumGeneratedFormatColumnsBox.IsEnabled = canEditProjectContent;
        albumGeneratedFormatRowsBox.IsEnabled = canEditProjectContent;
        albumGeneratedFormatPanel.Visibility = state.Album.GeneratedPageFormat is null
            ? Visibility.Collapsed
            : Visibility.Visible;
        RefreshAlbumGeneratedFormatSummary(generatedFormat);
        var hasComposition = state.Album.Composition.Count > 0;
        int visualizationImageCount = CurrentProjectVisualizationImages().Count;
        includeCoverCheck.Visibility = hasComposition ? Visibility.Collapsed : Visibility.Visible;
        if (hasComposition)
        {
            StudioAlbumCompositionProgress progress =
                StudioAlbumCompositionProgress.Resolve(
                    state.Album,
                    visualizationImageCount);
            albumInfoText.Text =
                $"{progress.Summary} · {state.Album.Pages.Count} source sheet · " +
                $"{visualizationImageCount} зураг";
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

        string? previewPath = ResolveAlbumPreviewPath();
        if (!albumThumbnailMode || string.IsNullOrWhiteSpace(previewPath))
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
        _ = LoadAlbumThumbnailsAsync(items, previewPath, cancellation.Token);
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

        AlbumProject buildProject = state.CreateAlbumBuildProject(
            reconcileLinkedProjectAssets: false);
        IReadOnlyList<ConceptGeneratedPagePlan> generatedPlans =
            BuildingArchitectureConceptGeneratedPagePlanner.Create(buildProject);
        var sequence = BuildingArchitectureConceptAlbumSequencer.Create(
            state.Album,
            state.Album.Pages,
            state.Library,
            state.Project.Sources,
            generatedPlans.Count,
            state.Project.BuildingGroups,
            state.Project.SheetBuildingAssignments);
        int firstVisualizationNumber = BuildingArchitectureConceptAlbumSequencer.NextAutomaticNumber(
            state.Album,
            sequence,
            generatedPlans.Count);
        IReadOnlyList<VisualizationAlbumPagePlan> visualizationPlans =
            VisualizationPageLayoutPlanner.Create(
                buildProject.Visualizations,
                buildProject.ProjectId,
                firstVisualizationNumber);

        var studioPages = CreateAlbumWorkspaceGroup(
            $"{albumNodeKey}:studio-pages",
            AlbumWorkspaceNodeKind.Studio,
            "Studio хуудас");
        foreach (ConceptGeneratedPagePlan plan in generatedPlans)
        {
            studioPages.Children.Add(CreateAlbumWorkspacePage(new AlbumPageWorkspaceItem(
                null,
                plan.Component,
                plan.Number,
                plan.Number,
                plan.Title,
                plan.DocumentLabel,
                "")
            {
                GeneratedPageIndex = plan.OutputIndex,
                GeneratedNavigationKey = plan.NavigationKey,
                CanonicalComponentCode =
                    StudioAlbumPreviewPageMap.GeneratedComponentCode(
                        plan.Component,
                        plan.DocumentKind),
                CanonicalComponentPageOffset = Math.Max(0, plan.BatchNumber - 1),
            }));
        }
        root.Children.Add(studioPages);

        bool isPartialGeneralPlan = state.Album.TemplateId.Equals(
            ErkS.Platform.Core.ProjectTypes.UrbanPlanning.UrbanPlanningAlbumTemplate.PartialPlanTemplateId,
            StringComparison.OrdinalIgnoreCase);
        if (isPartialGeneralPlan)
        {
            foreach (StudioAlbumSectionGroup group in
                     StudioAlbumSectionGrouping.ResolvePopulatedSourceSlots(
                         state.Album,
                         sequence.Select(item => item.Slot?.Id)))
            {
                AlbumWorkspaceNodeKind kind = group.Title.Contains(
                    "Инженерийн дэд бүтэц",
                    StringComparison.OrdinalIgnoreCase)
                        ? AlbumWorkspaceNodeKind.EngineeringInfrastructure
                        : AlbumWorkspaceNodeKind.GeneralPlan;
                AlbumWorkspaceNode sectionNode = CreateAlbumWorkspaceGroup(
                    $"{albumNodeKey}:{group.Key}",
                    kind,
                    group.Title);
                foreach (AlbumCompositionItem component in group.Components)
                {
                    List<ConceptAlbumSourcePage> linkedPages = sequence.Where(item =>
                            string.Equals(
                                item.Slot?.Id,
                                component.Id,
                                StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    foreach (ConceptAlbumSourcePage linkedPage in linkedPages)
                    {
                        sectionNode.Children.Add(CreateAlbumWorkspacePage(
                            CreateSourcePageWorkspaceItem(linkedPage)));
                    }
                }
                root.Children.Add(sectionNode);
            }

            List<ConceptAlbumSourcePage> unmatchedPages = sequence
                .Where(item => item.Slot is null)
                .ToList();
            if (unmatchedPages.Count > 0)
            {
                AlbumWorkspaceNode unmatchedNode = CreateAlbumWorkspaceGroup(
                    $"{albumNodeKey}:unmatched-source-pages",
                    AlbumWorkspaceNodeKind.Source,
                    "Бусад холбогдсон хуудас");
                foreach (ConceptAlbumSourcePage linkedPage in unmatchedPages)
                {
                    unmatchedNode.Children.Add(CreateAlbumWorkspacePage(
                        CreateSourcePageWorkspaceItem(linkedPage)));
                }
                root.Children.Add(unmatchedNode);
            }
        }
        else
        {
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
            foreach (var buildingGroup in drawingPages.GroupBy(
                         item => item.SourceGroupKey,
                         StringComparer.OrdinalIgnoreCase))
            {
                var firstBuildingPage = buildingGroup.First();
                var buildingNode = CreateAlbumWorkspaceGroup(
                    $"{albumNodeKey}:building:{buildingGroup.Key}",
                    AlbumWorkspaceNodeKind.Source,
                    $"Барилга · {firstBuildingPage.SourceGroupTitle}");

                foreach (var drawingTypeGroup in buildingGroup.GroupBy(
                             ResolveAlbumWorkspaceDrawingTypeKey,
                             StringComparer.OrdinalIgnoreCase))
                {
                    var firstDrawingPage = drawingTypeGroup.First();
                    var drawingTypeNode = CreateAlbumWorkspaceGroup(
                        $"{buildingNode.Key}:type:{drawingTypeGroup.Key}",
                        AlbumWorkspaceNodeKind.DrawingType,
                        ResolveAlbumWorkspaceDrawingTypeTitle(firstDrawingPage));
                    foreach (var linkedPage in drawingTypeGroup)
                    {
                        drawingTypeNode.Children.Add(CreateAlbumWorkspacePage(
                            CreateSourcePageWorkspaceItem(linkedPage)));
                    }
                    buildingNode.Children.Add(drawingTypeNode);
                }
                root.Children.Add(buildingNode);
            }
        }

        if (visualizationPlans.Count > 0)
        {
            AlbumCompositionItem? visualizationComponent = state.Album.Composition.FirstOrDefault(item =>
                item.Id.Equals("visualizations", StringComparison.OrdinalIgnoreCase));
            var visualizationSourceNode = CreateAlbumWorkspaceGroup(
                $"{albumNodeKey}:source:{VisualizationSourceSelectionKey}",
                AlbumWorkspaceNodeKind.Source,
                "Эх үүсвэр · Харагдах байдал");
            var visualizationTypeNode = CreateAlbumWorkspaceGroup(
                $"{visualizationSourceNode.Key}:type:visualizations",
                AlbumWorkspaceNodeKind.DrawingType,
                "Харагдах байдал");
            foreach (VisualizationAlbumPagePlan plan in visualizationPlans)
            {
                visualizationTypeNode.Children.Add(CreateAlbumWorkspacePage(
                    CreateVisualizationPageWorkspaceItem(plan, visualizationComponent)));
            }
            visualizationSourceNode.Children.Add(visualizationTypeNode);
            root.Children.Add(visualizationSourceNode);
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
            : !string.IsNullOrWhiteSpace(item.GeneratedNavigationKey)
                ? $"component:{item.GeneratedNavigationKey}"
                : $"component:{item.Component?.Id ?? Guid.NewGuid().ToString("N")}",
        Kind = AlbumWorkspaceNodeKind.Page,
        Title = item.Title,
        PageItem = item,
    };

    private static int CountAlbumWorkspacePages(AlbumWorkspaceNode node) =>
        node.PageItem is AlbumPageWorkspaceItem item
            ? (item.Page is not null ||
               item.Component?.Kind == AlbumCompositionKind.Generated ||
               item.VisualizationPlan is not null
                ? 1
                : 0)
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

    private static AlbumPageWorkspaceItem CreateVisualizationPageWorkspaceItem(
        VisualizationAlbumPagePlan plan,
        AlbumCompositionItem? component) => new(
            null,
            component,
            plan.Number,
            plan.Number,
            plan.Title,
            $"{plan.Tiles.Count} зураг",
            "Харагдах байдал")
        {
            GeneratedNavigationKey = plan.NavigationKey,
            VisualizationPlan = plan,
            CanonicalComponentCode =
                ProjectCloudSyncMetadata.VisualizationsComponentCode,
            CanonicalComponentPageOffset = Math.Max(0, plan.PageIndex),
        };

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

    private static string ResolveAlbumWorkspaceDrawingTypeKey(ConceptAlbumSourcePage item)
    {
        string contentKind = item.Sheet is null
            ? ""
            : AlbumPageSourceMetadata.ResolveContentKind(item.Page, item.Sheet.Entry);
        return !string.IsNullOrWhiteSpace(item.Slot?.Id)
            ? item.Slot.Id
            : !string.IsNullOrWhiteSpace(contentKind)
                ? contentKind
                : "drawing-pages";
    }

    private static string ResolveAlbumWorkspaceDrawingTypeTitle(ConceptAlbumSourcePage item)
    {
        string contentKind = item.Sheet is null
            ? ""
            : AlbumPageSourceMetadata.ResolveContentKind(item.Page, item.Sheet.Entry);
        return !string.IsNullOrWhiteSpace(item.Slot?.SectionTitle)
            ? item.Slot.SectionTitle.Trim()
            : !string.IsNullOrWhiteSpace(contentKind)
                ? contentKind
                : "Зургийн хуудас";
    }

    private void BindSelectedAlbumPage()
    {
        bindingAlbumPage = true;
        bool canEditProjectContent = CanEditProjectContent();
        int selectedPageCount = albumPagesWorkspaceList.SelectedItems
            .OfType<AlbumPageWorkspaceItem>()
            .Count(item => !item.IsGroup);
        albumSectionBox.ItemsSource = new[] { new SectionChoice(null, "Бүлэггүй") }
            .Concat(state.Album.Sections.Select(section => new SectionChoice(section.Id, section.Title)))
            .ToList();

        // A comment does not change the drawing, so it follows the chosen page
        // rather than the right to edit: a reviewer holds none of the sources
        // and may still say what they think of the sheet in front of them.
        albumSheetCommentButton.IsEnabled =
            albumPageSelectorBox.SelectedItem is AlbumPageChoice;

        if (albumPagesWorkspaceList.SelectedItem is AlbumPageWorkspaceItem selected &&
            selected.Page is AlbumPageDefinition page)
        {
            SetAlbumPagePropertiesEnabled(canEditProjectContent && selectedPageCount == 1);
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
            List<ContentKindChoice> contentKindChoices =
                BuildAlbumContentKindChoices(page.ContentKindOverride);
            albumContentKindBox.ItemsSource = contentKindChoices;
            albumContentKindBox.SelectedItem = contentKindChoices.FirstOrDefault(choice =>
                string.Equals(
                    choice.Value,
                    page.ContentKindOverride,
                    StringComparison.OrdinalIgnoreCase));
            albumSectionBox.SelectedItem = albumSectionBox.Items
                .Cast<SectionChoice>()
                .FirstOrDefault(choice => choice.Id == page.SectionId);
            bool canCrop = sheet?.Source.Application == SheetSourceApplication.Pdf;
            SourcePageCropDefinition crop = page.SourceCrop ?? new SourcePageCropDefinition();
            albumPdfFormatPanel.Visibility = canCrop ? Visibility.Visible : Visibility.Collapsed;
            albumSourceCropCheck.Visibility = canCrop ? Visibility.Visible : Visibility.Collapsed;
            albumSourceCropPanel.Visibility = canCrop ? Visibility.Visible : Visibility.Collapsed;
            albumSourceCropCheck.IsEnabled =
                canEditProjectContent &&
                canCrop;
            albumSourceCropCheck.IsChecked = canCrop && crop.Enabled;
            albumCropLeftBox.Text = FormatCropMillimeters(crop.LeftMm);
            albumCropTopBox.Text = FormatCropMillimeters(crop.TopMm);
            albumCropRightBox.Text = FormatCropMillimeters(crop.RightMm);
            albumCropBottomBox.Text = FormatCropMillimeters(crop.BottomMm);
            BindPdfFormatControls(page, sheet);
            if (canCrop)
            {
                albumPlacementBox.SelectedItem = albumPlacementBox.Items
                    .Cast<PlacementChoice>()
                    .FirstOrDefault(choice =>
                        choice.Value == PagePlacementMode.PreservePhysicalSize);
            }
            albumPlacementBox.IsEnabled =
                canEditProjectContent &&
                !canCrop;
            RefreshAlbumSourceCropControls();
        }
        else if (albumPagesWorkspaceList.SelectedItem is AlbumPageWorkspaceItem
                 {
                     VisualizationPlan: VisualizationAlbumPagePlan
                 } visualizationItem)
        {
            SetAlbumPagePropertiesEnabled(false);
            albumPageNumberBox.Text = visualizationItem.Number;
            albumPageTitleBox.Text = visualizationItem.Title;
            albumPageFormatBox.ItemsSource = PageFormatCatalog.All;
            albumPageFormatBox.SelectedItem = PageFormatCatalog.Resolve(PageFormatCatalog.ConceptA3LandscapeId);
            albumPlacementBox.SelectedItem = null;
            albumContentKindBox.ItemsSource = Array.Empty<ContentKindChoice>();
            albumContentKindBox.SelectedItem = null;
            albumSectionBox.SelectedItem = albumSectionBox.Items
                .Cast<SectionChoice>()
                .FirstOrDefault(choice => string.Equals(
                    choice.Label,
                    "Харагдах байдал",
                    StringComparison.OrdinalIgnoreCase));
        }
        else if (albumPagesWorkspaceList.SelectedItem is AlbumPageWorkspaceItem compositionItem)
        {
            SetAlbumPagePropertiesEnabled(false);
            albumPageNumberBox.Text = compositionItem.Component?.Number ?? compositionItem.Number;
            albumPageTitleBox.Text = compositionItem.Component?.Title ?? compositionItem.Title;
            albumPageFormatBox.ItemsSource = PageFormatCatalog.All;
            albumPageFormatBox.SelectedItem = PageFormatCatalog.Resolve(PageFormatCatalog.ConceptA3LandscapeId);
            albumPlacementBox.SelectedItem = null;
            albumContentKindBox.ItemsSource = Array.Empty<ContentKindChoice>();
            albumContentKindBox.SelectedItem = null;
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
            albumContentKindBox.ItemsSource = Array.Empty<ContentKindChoice>();
            albumContentKindBox.SelectedItem = null;
            albumSectionBox.SelectedItem = null;
        }

        BindAlbumPageRoleControls(canEditProjectContent);
        bindingAlbumPage = false;
        RefreshAlbumPagePreview();
    }

    /// <summary>One page of the album, as it can be chosen for settings.</summary>
    private sealed record AlbumPageChoice(
        int PageNumber,
        string Label,
        string Owner,
        string PageKey);

    /// <summary>
    /// Fills the page chooser from the album itself.
    ///
    /// The built album is the only thing that knows every page there is. The
    /// shared manifest names the pages and says who contributed each, and this
    /// device knows the titles of its own - but neither is complete, and a
    /// reviewer holds none of the sources at all. So the album is counted, and
    /// what is known about each page is laid over that count. Every page of the
    /// album can then be chosen, whoever made it.
    /// </summary>
    private void RefreshAlbumPageChoices()
    {
        if (!state.HasOpenProject)
        {
            albumPageSelectorBox.ItemsSource = Array.Empty<AlbumPageChoice>();
            albumPageOwnerText.Text = "";
            return;
        }

        var named = new Dictionary<int, AlbumPageChoice>();
        foreach (ProjectCloudAlbumComponentReference component in
            state.Project.Cloud.SharedAlbumComponents ?? [])
        {
            string label = string.IsNullOrWhiteSpace(component.Label)
                ? component.Code
                : component.Label;
            foreach (ProjectCloudAlbumComponentPageReference page in component.Pages)
            {
                if (page.PageNumber > 0 && !named.ContainsKey(page.PageNumber))
                {
                    // The page's own name first. The component's label names
                    // the whole run of pages it produced, so falling back to it
                    // gives six pages the same name - true of the run, useless
                    // for picking one page out of it.
                    named[page.PageNumber] = new AlbumPageChoice(
                        page.PageNumber,
                        string.IsNullOrWhiteSpace(page.Title) ? label : page.Title.Trim(),
                        component.OwnerEmail,
                        page.PageKey);
                }
            }
        }

        foreach (AlbumPageWorkspaceItem item in albumPagesWorkspaceList.Items
            .OfType<AlbumPageWorkspaceItem>()
            .Where(item => !item.IsGroup))
        {
            if (ResolveBuiltAlbumPage(item) is int page && page > 0 && !named.ContainsKey(page))
                named[page] = new AlbumPageChoice(page, item.Title, "", "");
        }

        int count = ResolveAlbumPageCount();
        if (count <= 0)
            count = named.Count == 0 ? 0 : named.Keys.Max();

        var choices = new List<AlbumPageChoice>(count);
        for (int page = 1; page <= count; page++)
        {
            AlbumPageChoice known = named.TryGetValue(page, out AlbumPageChoice? entry)
                ? entry
                : new AlbumPageChoice(page, "", "", "");
            choices.Add(known with
            {
                Label = known.Label.Length == 0
                    ? $"{page:00}  ·  Хуудас {page}"
                    : $"{page:00}  ·  {known.Label}",
            });
        }

        object? previous = albumPageSelectorBox.SelectedItem;
        albumPageSelectorBox.ItemsSource = choices;
        albumPageSelectorBox.SelectedItem = previous is AlbumPageChoice earlier
            ? choices.FirstOrDefault(choice => choice.PageNumber == earlier.PageNumber)
            : choices.FirstOrDefault();
        albumSheetCommentButton.IsEnabled = choices.Count > 0;
        if (choices.Count == 0)
        {
            albumPageOwnerText.Text =
                "Альбум хараахан байгуулагдаагүй байна. «Альбум байгуулах» дарснаар " +
                "хуудаснууд гарч ирнэ.";
        }
    }

    /// <summary>
    /// How many pages the built album has. This is read from the album rather
    /// than counted from the sources, because the sources of the other
    /// participants are not on this device.
    /// </summary>
    private int ResolveAlbumPageCount()
    {
        string? previewPath = ResolveAlbumPreviewPath();
        if (string.IsNullOrWhiteSpace(previewPath))
            return 0;

        if (string.Equals(previewPath, albumPageCountPath, StringComparison.OrdinalIgnoreCase))
            return albumPageCountValue;

        using PdfiumDocument? document = PdfiumDocument.Open(previewPath);
        albumPageCountPath = previewPath;
        albumPageCountValue = document?.PageCount ?? 0;
        return albumPageCountValue;
    }

    private string albumPageCountPath = "";
    private int albumPageCountValue;

    /// <summary>
    /// Moves the viewer to the chosen page and opens whatever of it this device
    /// may edit. A page contributed by somebody else has no entry here; the
    /// panel then says whose it is rather than showing empty fields.
    /// </summary>
    private void ApplyAlbumPageSelection()
    {
        if (bindingAlbumPage || albumPageSelectorBox.SelectedItem is not AlbumPageChoice choice)
            return;

        string? previewPath = ResolveAlbumPreviewPath();
        if (albumMarkupMode)
            ShowAlbumMarkup(choice);
        else if (!string.IsNullOrWhiteSpace(previewPath))
            ShowAlbumPdfPage(previewPath, choice.PageNumber);

        AlbumPageWorkspaceItem? owned = albumPagesWorkspaceList.Items
            .OfType<AlbumPageWorkspaceItem>()
            .FirstOrDefault(item =>
                !item.IsGroup &&
                (item.BuiltPageNumber ?? ResolveBuiltAlbumPage(item)) == choice.PageNumber);
        if (owned is not null)
        {
            albumPageOwnerText.Text = "";
            albumPagesWorkspaceList.SelectedItem = owned;
            return;
        }

        albumPagesWorkspaceList.SelectedItem = null;
        albumPageOwnerText.Text = choice.Owner.Length == 0
            ? "Энэ хуудас энэ төхөөрөмжийн эх үүсвэрээс гараагүй тул засах боломжгүй."
            : $"Энэ хуудсыг {choice.Owner} оруулсан. Засах эрх нь тэдэнд байна — " +
              "коммент бичиж санал хүргэнэ үү.";
    }

    private void SetAlbumPagePropertiesEnabled(bool enabled)
    {
        albumPageNumberBox.IsEnabled = enabled;
        albumPageTitleBox.IsEnabled = enabled;
        albumPageFormatBox.IsEnabled = enabled;
        albumPlacementBox.IsEnabled = enabled;
        albumContentKindBox.IsEnabled = enabled;
        albumSectionBox.IsEnabled = enabled;
        albumPdfPageSizeBox.IsEnabled = enabled;
        albumPdfOrientationBox.IsEnabled = enabled;
        albumPdfBindEdgeBox.IsEnabled = enabled;
        albumPdfDrawingScaleBox.IsEnabled = enabled;
        albumPdfCustomWidthBox.IsEnabled = enabled;
        albumPdfCustomHeightBox.IsEnabled = enabled;
        albumPdfApplyFormatButton.IsEnabled = enabled;
        albumPdfEditPageButton.IsEnabled = enabled;
        if (!enabled)
        {
            albumPdfFormatPanel.Visibility = Visibility.Collapsed;
            albumSourceCropCheck.Visibility = Visibility.Collapsed;
            albumSourceCropPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void ApplyAlbumPageProperties()
    {
        if (bindingAlbumPage ||
            !CanEditProjectContent() ||
            albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem selected ||
            selected.Page is not AlbumPageDefinition page)
        {
            return;
        }

        var sheet = state.Library.Find(page.SheetKey);
        string previousContentKind = page.ContentKindOverride;
        page.NumberOverride = string.Equals(
            albumPageNumberBox.Text.Trim(),
            selected.AutomaticNumber,
            StringComparison.Ordinal)
            ? ""
            : albumPageNumberBox.Text.Trim();
        page.TitleOverride = string.Equals(albumPageTitleBox.Text.Trim(), sheet?.Entry.Name, StringComparison.Ordinal)
            ? ""
            : albumPageTitleBox.Text.Trim();
        page.ContentKindOverride =
            (albumContentKindBox.SelectedItem as ContentKindChoice)?.Value?.Trim() ?? "";
        bool classificationChanged = !string.Equals(
            previousContentKind,
            page.ContentKindOverride,
            StringComparison.OrdinalIgnoreCase);
        if (sheet is not null)
        {
            AlbumCompositionItem? slot =
                BuildingArchitectureConceptAlbumTemplate.FindSourceSlot(
                    state.Album,
                    AlbumPageSourceMetadata.ResolveContentKind(page, sheet.Entry),
                    sheet.Entry.Discipline,
                    sheet.Entry.Name);
            page.TemplateSlotId = slot?.Id ?? "";
            page.SectionId = classificationChanged
                ? BuildingArchitectureConceptAlbumTemplate.ResolveSectionId(state.Album, slot)
                : (albumSectionBox.SelectedItem as SectionChoice)?.Id;
        }
        else
        {
            page.SectionId = (albumSectionBox.SelectedItem as SectionChoice)?.Id;
        }

        if (sheet?.Source.Application == SheetSourceApplication.Pdf)
        {
            SourcePageCropDefinition crop =
                page.SourceCrop ?? new SourcePageCropDefinition();
            crop.Enabled = albumSourceCropCheck.IsChecked == true;
            crop.LeftMm = ParseCropMillimeters(albumCropLeftBox.Text, crop.LeftMm);
            crop.TopMm = ParseCropMillimeters(albumCropTopBox.Text, crop.TopMm);
            crop.RightMm = ParseCropMillimeters(albumCropRightBox.Text, crop.RightMm);
            crop.BottomMm = ParseCropMillimeters(albumCropBottomBox.Text, crop.BottomMm);
            crop.ScalePercent = 100;
            page.SourceCrop = crop;
            page.PlacementMode = PagePlacementMode.PreservePhysicalSize;
        }

        if (classificationChanged)
        {
            IReadOnlyList<AlbumPageDefinition> ordered =
                BuildingArchitectureConceptAlbumSequencer.OrderPages(
                    state.Album,
                    state.Album.Pages,
                    state.Library,
                    state.Project.Sources,
                    state.Project.BuildingGroups,
                    state.Project.SheetBuildingAssignments);
            state.Album.Pages.Clear();
            state.Album.Pages.AddRange(ordered);
        }

        state.SaveProject();
        if (classificationChanged)
        {
            RefreshAlbumWorkspace(selectPageId: page.Id);
        }
        else
        {
            RefreshAlbumPagePreview();
        }
    }

    private List<ContentKindChoice> BuildAlbumContentKindChoices(string currentValue)
    {
        var choices = new List<ContentKindChoice>
        {
            new("", "Эх үүсвэрийн ангиллыг дагах"),
        };
        choices.AddRange(state.Album.Composition
            .Where(item => item.Kind == AlbumCompositionKind.SourceSlot)
            .OrderBy(item => item.Order)
            .Select(item => new ContentKindChoice(
                item.Id,
                string.IsNullOrWhiteSpace(item.SectionTitle)
                    ? item.Title
                    : item.SectionTitle)));
        if (!string.IsNullOrWhiteSpace(currentValue) &&
            choices.All(choice => !string.Equals(
                choice.Value,
                currentValue,
                StringComparison.OrdinalIgnoreCase)))
        {
            choices.Add(new ContentKindChoice(currentValue.Trim(), currentValue.Trim()));
        }
        return choices;
    }

    private void BindPdfFormatControls(AlbumPageDefinition page, SheetRecord? sheet)
    {
        PageFormatDefinition? configured = page.PageFormatSnapshot;
        bool hasConfiguredFormat = PdfSourcePageFormatFactory.IsPdfSourceFormat(configured);
        string sizeCode = hasConfiguredFormat
            ? configured!.Code
            : PdfSourcePageFormatFactory.SourceCode;
        string orientation = hasConfiguredFormat
            ? configured!.Orientation
            : sheet?.Entry.WidthMm >= sheet?.Entry.HeightMm
                ? "LANDSCAPE"
                : "PORTRAIT";
        string bindEdge = hasConfiguredFormat && !string.IsNullOrWhiteSpace(configured!.BindEdge)
            ? configured.BindEdge
            : "LEFT";

        albumPdfPageSizeBox.SelectedItem = albumPdfPageSizeBox.Items
            .Cast<PdfPageSizeChoice>()
            .FirstOrDefault(choice => string.Equals(
                choice.Code,
                sizeCode,
                StringComparison.OrdinalIgnoreCase));
        albumPdfOrientationBox.SelectedItem = albumPdfOrientationBox.Items
            .Cast<PdfFormatValueChoice>()
            .FirstOrDefault(choice => string.Equals(
                choice.Value,
                orientation,
                StringComparison.OrdinalIgnoreCase));
        albumPdfBindEdgeBox.SelectedItem = albumPdfBindEdgeBox.Items
            .Cast<PdfFormatValueChoice>()
            .FirstOrDefault(choice => string.Equals(
                choice.Value,
                bindEdge,
                StringComparison.OrdinalIgnoreCase));

        double width = hasConfiguredFormat
            ? configured!.WidthMm
            : sheet?.Entry.WidthMm > 0
                ? sheet.Entry.WidthMm
                : 420;
        double height = hasConfiguredFormat
            ? configured!.HeightMm
            : sheet?.Entry.HeightMm > 0
                ? sheet.Entry.HeightMm
                : 297;
        albumPdfCustomWidthBox.Text = FormatCropMillimeters(width);
        albumPdfCustomHeightBox.Text = FormatCropMillimeters(height);
        albumPdfDrawingScaleBox.Text = DrawingScaleText.Resolve(page, sheet?.Entry);
        RefreshPdfFormatControls();
    }

    private void RefreshPdfFormatControls()
    {
        string code = (albumPdfPageSizeBox.SelectedItem as PdfPageSizeChoice)?.Code ??
                      PdfSourcePageFormatFactory.SourceCode;
        bool sourceAsIs = string.Equals(
            code,
            PdfSourcePageFormatFactory.SourceCode,
            StringComparison.OrdinalIgnoreCase);
        bool custom = string.Equals(
            code,
            PdfSourcePageFormatFactory.CustomCode,
            StringComparison.OrdinalIgnoreCase);

        bool canEditProjectContent = CanEditProjectContent();
        albumPdfPageSizeBox.IsEnabled = canEditProjectContent;
        albumPdfOrientationBox.IsEnabled =
            canEditProjectContent &&
            !sourceAsIs;
        albumPdfBindEdgeBox.IsEnabled =
            canEditProjectContent &&
            !sourceAsIs;
        albumPdfDrawingScaleBox.IsEnabled = canEditProjectContent;
        albumPdfCustomWidthBox.IsEnabled = canEditProjectContent;
        albumPdfCustomHeightBox.IsEnabled = canEditProjectContent;
        albumPdfCustomSizePanel.Visibility = custom
            ? Visibility.Visible
            : Visibility.Collapsed;
        albumPdfApplyFormatButton.IsEnabled =
            canEditProjectContent &&
            albumPdfFormatPanel.Visibility == Visibility.Visible;
        albumPdfEditPageButton.IsEnabled =
            canEditProjectContent &&
            albumPdfFormatPanel.Visibility == Visibility.Visible;
    }

    private void ApplyPdfPageFormat()
    {
        if (bindingAlbumPage ||
            !EnsureProjectContentPermission() ||
            albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem selected ||
            selected.Page is not AlbumPageDefinition page)
        {
            return;
        }

        SheetRecord? sheet = state.Library.Find(page.SheetKey);
        if (sheet?.Source.Application != SheetSourceApplication.Pdf)
        {
            SetStatus("Форматыг Studio дээр зөвхөн PDF эх үүсвэрийн хуудсанд тохируулна.");
            return;
        }

        page.ScaleTextOverride = DrawingScaleText.Normalize(albumPdfDrawingScaleBox.Text);
        SourcePageCropDefinition crop =
            page.SourceCrop ?? new SourcePageCropDefinition();
        crop.ScalePercent = 100;
        page.SourceCrop = crop;

        string code = (albumPdfPageSizeBox.SelectedItem as PdfPageSizeChoice)?.Code ??
                      PdfSourcePageFormatFactory.SourceCode;
        if (string.Equals(
                code,
                PdfSourcePageFormatFactory.SourceCode,
                StringComparison.OrdinalIgnoreCase))
        {
            page.PageFormatId = PageFormatCatalog.SourceAsIsId;
            page.PageFormatSnapshot = null;
            page.FollowSourceFormat = false;
            page.PlacementMode = PagePlacementMode.PreservePhysicalSize;
        }
        else
        {
            string orientation =
                (albumPdfOrientationBox.SelectedItem as PdfFormatValueChoice)?.Value ??
                "LANDSCAPE";
            string bindEdge =
                (albumPdfBindEdgeBox.SelectedItem as PdfFormatValueChoice)?.Value ??
                "LEFT";
            double width = 420;
            double height = 297;
            if (string.Equals(
                    code,
                    PdfSourcePageFormatFactory.CustomCode,
                    StringComparison.OrdinalIgnoreCase) &&
                (!TryParsePdfDimension(albumPdfCustomWidthBox.Text, out width) ||
                 !TryParsePdfDimension(albumPdfCustomHeightBox.Text, out height)))
            {
                SetStatus("Тусгай PDF формат 100-3000 мм-ийн өргөн, өндөртэй байна.");
                return;
            }

            PageFormatDefinition format = PdfSourcePageFormatFactory.Create(
                code,
                orientation,
                bindEdge,
                width,
                height);
            page.PageFormatId = format.Id;
            page.PageFormatSnapshot = format;
            page.FollowSourceFormat = false;
            page.PlacementMode = PagePlacementMode.PreservePhysicalSize;
        }

        state.SaveProject();
        RefreshAlbumWorkspace(selectPageId: page.Id);
        UpdateAlbum(silent: false, statusPrefix: "PDF хуудасны формат шинэчлэгдлээ");
    }

    /// <summary>
    /// Opens the comments on the selected sheet. Anyone on the project may read
    /// and write them - a comment says something about a drawing without being
    /// able to change it, which is the whole point for a reviewer who must not
    /// be given the drawing.
    /// </summary>
    /// <summary>
    /// Turns marking on and off in the album panel. On, the sheet is drawn by
    /// Studio itself and can be marked; off, it goes back to the reader.
    /// </summary>
    private void ToggleSheetMarkup()
    {
        if (albumMarkupMode)
        {
            albumMarkupMode = false;
            albumMarkupSurface?.Release();
            albumSheetCommentButton.ToolTip = null;
            RefreshAlbumPagePreview();
            return;
        }

        if (albumPageSelectorBox.SelectedItem is not AlbumPageChoice choice)
        {
            SetStatus("Тэмдэглэгээ хийх хуудсаа «Хуудас» жагсаалтаас сонгоно уу.");
            return;
        }

        if (!account.IsSignedIn || string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId))
        {
            SetStatus("Хуудасны коммент Cloud ERA-д хадгалагдана. Эхлээд нэвтэрч, төслөө холбоно уу.");
            return;
        }

        if (string.IsNullOrWhiteSpace(ResolveAlbumPreviewPath()))
        {
            SetStatus(
                "Тэмдэглэгээ альбумын хуудсанд тавигдана. Эхлээд альбумаа байгуулна уу.");
            return;
        }

        albumMarkupMode = true;
        ShowAlbumMarkup(choice);
    }

    /// <summary>
    /// Puts the chosen page on the marking surface, in the panel the reader was
    /// in.
    ///
    /// The page is named the way the whole album names it, so the author and a
    /// reviewer who holds none of the sources arrive at the same conversation
    /// about the same drawing. A project that has never synced has no such name
    /// yet, and falls back to the name this device gives the sheet.
    /// </summary>
    private void ShowAlbumMarkup(AlbumPageChoice choice)
    {
        string? albumPath = ResolveAlbumPreviewPath();
        if (string.IsNullOrWhiteSpace(albumPath))
            return;

        AlbumPageWorkspaceItem? owned = albumPagesWorkspaceList.Items
            .OfType<AlbumPageWorkspaceItem>()
            .FirstOrDefault(item =>
                !item.IsGroup &&
                (item.BuiltPageNumber ?? ResolveBuiltAlbumPage(item)) == choice.PageNumber);

        string identity = StudioSheetCommentRules.AlbumPageIdentity(choice.PageKey);
        if (identity.Length == 0 && owned is not null)
        {
            SheetRecord? sheet = owned.Page is AlbumPageDefinition page
                ? state.Library.Find(page.SheetKey)
                : null;
            identity = StudioSheetCommentRules.PageIdentity(
                sheet,
                string.IsNullOrWhiteSpace(owned.GeneratedNavigationKey)
                    ? owned.CanonicalComponentCode
                    : owned.GeneratedNavigationKey);
        }

        if (identity.Length == 0)
        {
            SetStatus(
                "Энэ хуудсанд тэмдэглэгээ бэхлэх тогтвортой нэр алга. " +
                "Төслөө үүлэн рүү илгээснээр нэр үүснэ.");
            albumMarkupMode = false;
            return;
        }

        albumMarkupSurface ??= new SheetMarkupSurface(
            account,
            state.Project.Cloud.ServerProjectId,
            canWrite: true,
            onExit: ToggleSheetMarkup);

        albumPreviewHost.Children.Clear();
        if (albumMarkupSurface.Parent is Panel parent)
            parent.Children.Remove(albumMarkupSurface);
        albumPreviewHost.Children.Add(albumMarkupSurface);
        albumSheetCommentButton.ToolTip = "Тэмдэглэгээний горимоос гарах";

        _ = albumMarkupSurface.ShowPageAsync(
            albumPath,
            choice.PageNumber,
            identity,
            owned?.Number ?? choice.PageNumber.ToString(),
            owned?.Title ?? "");
    }

    private void EditPdfSourcePage()
    {
        if (albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem selected ||
            selected.Page is not AlbumPageDefinition page)
        {
            return;
        }

        SheetRecord? sheet = state.Library.Find(page.SheetKey);
        if (sheet is null)
        {
            SetStatus("Сонгосон альбумын хуудасны эх үүсвэр олдсонгүй.");
            return;
        }

        EditPdfSourcePage(sheet, page);
    }

    private void EditPdfSourcePage(
        SheetRecord sheet,
        AlbumPageDefinition page)
    {
        if (!state.HasOpenProject || !EnsureProjectContentPermission())
        {
            return;
        }

        if (sheet.Source.Application != SheetSourceApplication.Pdf)
        {
            SetStatus("PDF хэсэг засах багаж зөвхөн PDF эх үүсвэрт ажиллана.");
            return;
        }

        ProjectDesignSource? source = state.Project.Sources.FirstOrDefault(
            candidate => candidate.Id.Equals(
                sheet.Source.SourceId,
                StringComparison.OrdinalIgnoreCase));
        StudioPdfPageEditCloudDecision cloudDecision =
            StudioPdfPageEditCloudPolicy.Resolve(
                state.Project,
                source,
                account.Current?.Email,
                StudioDeviceIdentity.Fingerprint,
                source is not null &&
                StudioLocalSourceBindingPolicy.HasVerifiedPayload(source));
        if (!cloudDecision.Allowed)
        {
            SetStatus(
                $"{cloudDecision.Message} [reason: {cloudDecision.ReasonCode}]");
            return;
        }

        PageFormatDefinition studioFormat =
            PdfSourcePageStudioLayout.ResolvePreviewFormat(page, sheet.Entry);
        var dialog = new PdfSourcePageEditorWindow(
            sourceSheetPageImages,
            sheet,
            page.SourceCrop,
            studioFormat,
            PdfSourcePageStudioLayout.UsesInformationHeader(page, sheet.Entry),
            page.ScaleTextOverride)
        {
            Owner = Window.GetWindow(Root),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        PdfSourcePageEditCommitPolicy.ApplyAcceptedEdit(
            page,
            sheet.Entry,
            dialog.Result,
            dialog.ScaleTextOverride);
        if (!string.IsNullOrWhiteSpace(cloudDecision.ComponentCode))
        {
            ProjectCloudSyncMetadata.MarkAlbumComponentPendingForBinding(
                state.Project,
                cloudDecision.ComponentCode,
                account.Current?.Email ?? "",
                StudioDeviceIdentity.Fingerprint,
                isRemoval: false);
        }
        state.SaveProject();
        RefreshReceivedSheetWorkspace(selectSheetKey: sheet.Key);
        RefreshAlbumWorkspace(selectPageId: page.Id);
        UpdateAlbum(
            silent: false,
            statusPrefix:
                "PDF crop, Studio байрлал болон булангийн масштаб хадгалагдлаа",
            origin: cloudDecision.BuildOperation);
    }

    private void RefreshAlbumSourceCropControls()
    {
        bool enabled =
            albumSourceCropCheck.Visibility == Visibility.Visible &&
            albumSourceCropCheck.IsEnabled &&
            albumSourceCropCheck.IsChecked == true;
        albumCropLeftBox.IsEnabled = enabled;
        albumCropTopBox.IsEnabled = enabled;
        albumCropRightBox.IsEnabled = enabled;
        albumCropBottomBox.IsEnabled = enabled;
        albumCropFromDrawingAreaButton.IsEnabled = enabled;
    }

    private void ApplyDrawingAreaCropPreset()
    {
        if (bindingAlbumPage ||
            albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem selected ||
            selected.Page is not AlbumPageDefinition page)
        {
            return;
        }

        SheetRecord? sheet = state.Library.Find(page.SheetKey);
        if (sheet?.Source.Application != SheetSourceApplication.Pdf)
        {
            return;
        }

        PageFormatDefinition format = PageFormatCatalog.Resolve(page);
        if (format.Kind == PageFormatKind.SourceAsIs ||
            format.WidthMm <= 0 ||
            format.HeightMm <= 0)
        {
            SetStatus("Эх PDF хэмжээгээр горимд форматын цэвэр талбай байхгүй.");
            return;
        }

        albumCropLeftBox.Text = FormatCropMillimeters(format.DrawingArea.X);
        albumCropTopBox.Text = FormatCropMillimeters(format.DrawingArea.Y);
        albumCropRightBox.Text = FormatCropMillimeters(Math.Max(
            0,
            format.WidthMm - format.DrawingArea.X - format.DrawingArea.Width));
        albumCropBottomBox.Text = FormatCropMillimeters(Math.Max(
            0,
            format.HeightMm - format.DrawingArea.Y - format.DrawingArea.Height));
        page.PlacementMode = PagePlacementMode.PreservePhysicalSize;
        ApplyAlbumPageProperties();
    }

    private static string FormatCropMillimeters(double value) =>
        Math.Max(0, value).ToString(
            "0.##",
            System.Globalization.CultureInfo.CurrentCulture);

    private static double ParseCropMillimeters(string text, double fallback)
    {
        const System.Globalization.NumberStyles styles =
            System.Globalization.NumberStyles.Float |
            System.Globalization.NumberStyles.AllowThousands;
        if (double.TryParse(
                text.Trim(),
                styles,
                System.Globalization.CultureInfo.CurrentCulture,
                out double value) ||
            double.TryParse(
                text.Trim(),
                styles,
                System.Globalization.CultureInfo.InvariantCulture,
                out value))
        {
            return Math.Max(0, value);
        }
        return Math.Max(0, fallback);
    }

    private static bool TryParsePdfDimension(string text, out double value)
    {
        const System.Globalization.NumberStyles styles =
            System.Globalization.NumberStyles.Float |
            System.Globalization.NumberStyles.AllowThousands;
        bool parsed =
            double.TryParse(
                text.Trim(),
                styles,
                System.Globalization.CultureInfo.CurrentCulture,
                out value) ||
            double.TryParse(
                text.Trim(),
                styles,
                System.Globalization.CultureInfo.InvariantCulture,
                out value);
        return parsed && value is >= 100 and <= 3000;
    }

    private void ApplyAlbumOptions()
    {
        if (bindingAlbumPage || !CanEditProjectContent())
        {
            return;
        }

        state.Album.IncludeCover = includeCoverCheck.IsChecked == true;
        state.Album.IncludeTableOfContents = includeTocCheck.IsChecked == true;
    }

    private void ApplyAlbumGeneratedPageFormat()
    {
        if (bindingAlbumPage || !CanEditProjectContent() ||
            albumGeneratedFormatColumnsBox.SelectedItem is not ModuleCountChoice columns ||
            albumGeneratedFormatRowsBox.SelectedItem is not ModuleCountChoice rows)
        {
            return;
        }

        PageFormatDefinition format = WorkingDrawingAlbumFormatFactory.Create(
            columns.Value,
            rows.Value);
        PageFormatDefinition current = WorkingDrawingAlbumFormatFactory.Resolve(state.Album);
        if (string.Equals(current.GeometryHash, format.GeometryHash, StringComparison.Ordinal))
        {
            RefreshAlbumGeneratedFormatSummary(current);
            return;
        }

        state.Album.GeneratedPageFormat = format;
        state.SaveProject();
        RefreshAlbumGeneratedFormatSummary(format);
        UpdateAlbum(
            silent: false,
            statusPrefix: $"Альбумын нийтлэг формат {format.Code} · " +
                          $"{format.WidthMm:0}×{format.HeightMm:0} мм боллоо");
    }

    private void RefreshAlbumGeneratedFormatSummary(PageFormatDefinition format)
    {
        albumGeneratedFormatSummaryText.Text =
            $"{format.Code} · A3 {format.ModuleColumns}×{format.ModuleRows} · " +
            $"{format.WidthMm:0}×{format.HeightMm:0} мм · хөндлөн";
    }

    private void RemoveSelectedAlbumPage()
    {
        if (!EnsureProjectContentPermission() ||
            albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem selected ||
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
        if (!EnsureProjectContentPermission() ||
            albumPagesWorkspaceList.SelectedItem is not AlbumPageWorkspaceItem selected ||
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
        // A project bind also prepares the hidden album navigator. Do not open
        // and parse a potentially large PDF until the user enters the album page.
        if (activePage != StudioPage.Albums)
        {
            return;
        }
        if (inlineSiteContextEditor is not null)
        {
            return;
        }
        // Marking owns the panel while it is on. A selection made in the album
        // must not pull the sheet being marked out from under the reviewer.
        if (albumMarkupMode)
        {
            return;
        }

        // The chosen page is what the reader shows, whoever contributed it. A
        // page from another participant has no entry in this device's own list,
        // and showing nothing for it would leave most of the album unviewable.
        if (albumPageSelectorBox.SelectedItem is AlbumPageChoice chosen &&
            ResolveAlbumPreviewPath() is string builtAlbum &&
            !string.IsNullOrWhiteSpace(builtAlbum))
        {
            ShowAlbumPdfPage(builtAlbum, chosen.PageNumber);
            return;
        }

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

        string? previewPath = ResolveAlbumPreviewPath();
        if (!string.IsNullOrWhiteSpace(previewPath) &&
            (selected.BuiltPageNumber ?? ResolveBuiltAlbumPage(selected)) is int builtPage)
        {
            ShowAlbumPdfPage(previewPath, builtPage);
            return;
        }

        if (selected.Page is not AlbumPageDefinition sourcePage)
        {
            ShowCompositionPreview(selected);
            return;
        }

        var sheet = state.Library.Find(sourcePage.SheetKey);
        var format = sheet is null
            ? PageFormatCatalog.Resolve(sourcePage)
            : PageFormatCatalog.ResolveForConceptPage(sourcePage, sheet.Entry);
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
        else if (BuildingArchitectureConceptPageLayout.SupportsStudioChrome(format))
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
            AddConceptSheetPreviewChrome(
                canvas,
                format,
                pageTitle,
                pageNumber,
                sourcePage,
                sheet?.Entry);
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

    private void ResetAlbumPreviewForProjectChange()
    {
        if (inlineSiteContextEditor is not null)
        {
            albumPreviewHost.Children.Remove(inlineSiteContextEditor);
            inlineSiteContextEditor.Dispose();
            inlineSiteContextEditor = null;
            UpdateProjectChatWidgetVisibility();
            inlineSiteContextPersisted = false;
            albumPagesWorkspaceList.IsEnabled = true;
        }
        CancelVisualizationThumbnailLoading();
        albumPdfNavigationSerial++;
        albumThumbnailLoadCancellation?.Cancel();
        albumThumbnailLoadCancellation?.Dispose();
        albumThumbnailLoadCancellation = null;
        selectedAlbumWorkspaceKey = null;
        albumPreviewManifest.Clear();
        albumPagesWorkspaceList.SelectedItem = null;
        albumPagesWorkspaceList.ItemsSource = null;

        try
        {
            albumPdfViewer.CoreWebView2?.Navigate("about:blank");
        }
        catch (InvalidOperationException)
        {
        }

        if (albumPdfViewer.Parent is Panel currentParent)
        {
            currentParent.Children.Remove(albumPdfViewer);
        }
        albumPreviewHost.Children.Clear();
        albumPreviewHost.Children.Add(new TextBlock
        {
            Text = "Альбумын хуудас сонгоно уу",
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    private int? ResolveBuiltAlbumPage(AlbumPageWorkspaceItem selected)
    {
        string? previewPath = ResolveAlbumPreviewPath();
        bool usesSharedManifest =
            StudioAlbumPreviewPageMap.UsesSharedManifest(previewPath);
        var project = state.CreateAlbumBuildProject(
            reconcileLinkedProjectAssets: false);
        List<ConceptGeneratedPagePlan> generated =
            BuildingArchitectureConceptGeneratedPagePlanner.Create(project).ToList();

        if (usesSharedManifest)
        {
            IReadOnlyList<ProjectCloudAlbumComponentReference> manifest =
                albumPreviewManifest.Resolve(
                    previewPath,
                    state.Project.Cloud.SharedAlbumComponents);
            if (!string.IsNullOrWhiteSpace(selected.CanonicalComponentCode))
            {
                return StudioAlbumPreviewPageMap.ResolveCanonicalGeneratedPage(
                    manifest,
                    selected.CanonicalComponentCode,
                    selected.CanonicalComponentPageOffset);
            }

            if (selected.Page is not AlbumPageDefinition canonicalSourcePage)
                return null;

            if (!AlbumBuilder.TryCreateRequest(project, state.Library, out AlbumBuildRequest canonicalRequest))
                return null;
            return StudioAlbumPreviewPageMap.ResolveCanonicalSourcePage(
                state.Project,
                canonicalRequest,
                canonicalSourcePage.Id,
                manifest);
        }

        if (selected.Component?.Kind == AlbumCompositionKind.Generated)
        {
            if (selected.GeneratedPageIndex.HasValue)
                return selected.GeneratedPageIndex.Value + 1;
            var generatedIndex = generated.FindIndex(item => string.Equals(
                item.Component.Id,
                selected.Component.Id,
                StringComparison.OrdinalIgnoreCase));
            return generatedIndex < 0 ? null : generatedIndex + 1;
        }

        if (selected.VisualizationPlan is VisualizationAlbumPagePlan visualizationPlan)
        {
            if (!AlbumBuilder.TryCreateRequest(project, state.Library, out AlbumBuildRequest visualizationRequest))
                return null;
            return StudioAlbumPreviewPageMap.ResolveLocalVisualizationPage(
                visualizationRequest,
                visualizationPlan.PageIndex,
                ResolveLocalAlbumLeadingPageCount(project, generated.Count),
                buildPage => File.Exists(buildPage.Sheet.PdfPath));
        }

        if (selected.Page is not AlbumPageDefinition selectedPage)
        {
            return null;
        }

        // An album waiting on its sheets cannot be built, and asking which page
        // this item would occupy has no answer yet. That is not a failure to take
        // the workspace down with - the list simply shows no page number.
        if (!AlbumBuilder.TryCreateRequest(project, state.Library, out AlbumBuildRequest request))
            return null;
        return StudioAlbumPreviewPageMap.ResolveLocalSourcePage(
            request,
            selectedPage.Id,
            ResolveLocalAlbumLeadingPageCount(project, generated.Count),
            buildPage => File.Exists(buildPage.Sheet.PdfPath));
    }

    private static int ResolveLocalAlbumLeadingPageCount(
        AlbumProject project,
        int generatedPageCount)
    {
        int count = Math.Max(0, generatedPageCount);
        if (generatedPageCount == 0 && project.Album.IncludeCover)
            count++;
        if (project.Album.IncludeTableOfContents)
            count++;
        return count;
    }

    private int? ResolveSharedAlbumComponentPage(string componentCode)
    {
        string? previewPath = ResolveAlbumPreviewPath();
        if (!StudioAlbumPreviewPageMap.UsesSharedManifest(previewPath))
            return null;

        return StudioAlbumPreviewPageMap.ResolveCanonicalComponentPage(
            albumPreviewManifest.Resolve(
                previewPath,
                state.Project.Cloud.SharedAlbumComponents),
            componentCode);
    }

    private string? ResolveAlbumPreviewPath()
    {
        if (!string.IsNullOrWhiteSpace(lastAlbumPath) && File.Exists(lastAlbumPath))
            return lastAlbumPath;

        string? cloudPath = ResolveLastReceivedCloudAlbumPath();
        return !string.IsNullOrWhiteSpace(cloudPath) && File.Exists(cloudPath)
            ? cloudPath
            : null;
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

            string previewPdfPath = await PdfPreviewFileCache.GetPreviewPathAsync(targetPdfPath);
            if (navigationSerial != albumPdfNavigationSerial)
            {
                return;
            }

            var pdf = new FileInfo(previewPdfPath);
            var builder = new UriBuilder(new Uri(previewPdfPath))
            {
                Query = $"erksVersion={pdf.LastWriteTimeUtc.Ticks}-{pdf.Length}",
                Fragment = $"page={targetPage}&zoom=page-fit",
            };

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

    private void ShowCompositionPreview(AlbumPageWorkspaceItem selected)
    {
        PageFormatDefinition format = PageFormatCatalog.Resolve(
            PageFormatCatalog.ConceptA3LandscapeId);
        var canvas = new Canvas
        {
            Width = format.WidthMm,
            Height = format.HeightMm,
            Background = Brushes.White,
        };
        var component = selected.Component;
        if (selected.VisualizationPlan is VisualizationAlbumPagePlan visualizationPlan)
        {
            AddVisualizationPagePreview(canvas, visualizationPlan);
            ShowAlbumPreviewCanvas(canvas);
            return;
        }
        if (component?.GeneratedPageKind == AlbumGeneratedPageKind.Cover)
        {
            AddConceptCoverPreview(canvas);
            ShowAlbumPreviewCanvas(canvas);
            return;
        }

        AddConceptSheetPreviewChrome(
            canvas,
            format,
            selected.Title,
            selected.Number);

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

    private void AddConceptSheetPreviewChrome(
        Canvas canvas,
        PageFormatDefinition format,
        string title,
        string number,
        AlbumPageDefinition? page = null,
        SheetPackageEntry? entry = null)
    {
        bool hasInformationHeader = entry is not null && BuildingArchitectureConceptPageLayout.UsesInformationHeader(
            page is null
                ? entry.ContentKind
                : AlbumPageSourceMetadata.ResolveContentKind(page, entry),
            entry.Name,
            page?.TemplateSlotId);
        BuildingArchitectureConceptPageRegions regions =
            BuildingArchitectureConceptPageLayout.ResolveRegions(
                format,
                hasInformationHeader);
        var frame = AddPreviewRectangle(canvas, regions.Frame, Brushes.Transparent, Brushes.Black);
        frame.StrokeThickness = 0.9;
        AddPreviewLine(
            canvas,
            regions.SheetTitleArea.X,
            regions.SheetTitleArea.Y + regions.SheetTitleArea.Height,
            regions.SheetTitleArea.X + regions.SheetTitleArea.Width,
            regions.SheetTitleArea.Y + regions.SheetTitleArea.Height);
        if (hasInformationHeader)
            AddConceptElevationHeaderPreview(canvas, page, entry!, regions);
        PageRectMm titleArea = regions.SheetTitleArea;
        AddPreviewText(
            canvas,
            title,
            titleArea.X + 5,
            titleArea.Y + 1.5,
            titleArea.Width - 10,
            titleArea.Height - 2,
            7.5,
            FontWeights.Normal,
            Brushes.Black,
            TextAlignment.Right);

        var corner = AddPreviewRectangle(
            canvas,
            regions.TitleBlockArea,
            Brushes.White,
            Brushes.Black);
        corner.StrokeThickness = 0.8;

        BuildingArchitectureConceptCornerGrid grid =
            BuildingArchitectureConceptPageLayout.ResolveCornerGrid(regions.TitleBlockArea);
        var x0 = grid.X0;
        var x1 = grid.X1;
        var x2 = grid.X2;
        var x3 = grid.X3;
        var x4 = grid.X4;
        var x5 = grid.X5;
        var y0 = grid.Y0;
        var y1 = grid.Y1;
        var y2 = grid.Y2;
        var y3 = grid.Y3;
        var y4 = grid.Y4;
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
        ProjectMember? appointedArchitect = state.Project.Foundation.DesignCompany.Members
            .Where(member => member.Roles.Any(ProjectRoleSemantics.IsAppointedArchitect))
            .FirstOrDefault();
        var architect = appointedArchitect is null
            ? ""
            : MongolianPersonNameFormatter.ForDocument(
                appointedArchitect.FamilyName,
                appointedArchitect.GivenName,
                appointedArchitect.FullName);
        var companyMark = string.IsNullOrWhiteSpace(company.ShortName) ? company.Name : company.ShortName;

        AddPreviewText(canvas, ValueOrDash(companyMark), x0 + 2, y0 + 7, x1 - x0 - 4, 14, 6.5, FontWeights.Bold, Brushes.DimGray);
        AddPreviewCornerCell(canvas, state.Project.Name, x1, y0, x2, y1, TextAlignment.Left);
        AddPreviewCornerCell(canvas, "Нэр", x2, y0, x3, y1);
        AddPreviewCornerCell(canvas, "Гарын үсэг", x3, y0, x4, y1);
        AddPreviewCornerCell(canvas, "Загвар", x4, y0, x5, y1);
        AddPreviewCornerCell(canvas, companyRole, x1, y1, x2, y2, TextAlignment.Left);
        AddPreviewCornerCell(canvas, representative?.FullName ?? "", x2, y1, x3, y2);
        AddPreviewCornerCell(
            canvas,
            page is null
                ? DrawingScaleText.Normalize(entry?.ScaleText)
                : DrawingScaleText.Resolve(page, entry),
            x4,
            y1,
            x5,
            y2);
        AddPreviewCornerCell(canvas, "Архитектор", x1, y2, x2, y3, TextAlignment.Left);
        AddPreviewCornerCell(canvas, architect, x2, y2, x3, y3);
        AddPreviewCornerCell(canvas, $"Хуудас-{ValueOrDash(number)}", x4, y2, x5, y3);
        AddPreviewCornerCell(canvas, "Захиалагч", x1, y3, x2, y4, TextAlignment.Left);
        ProjectInitiationBasis basis = state.Project.Foundation.InitiationBasis;
        AddPreviewCornerCell(
            canvas,
            ValueOrDash(ProjectClientTypes.ResolveCoverPersonName(
                basis.ClientType,
                basis.ClientName,
                basis.ClientRepresentativeName)),
            x2,
            y3,
            x3,
            y4);
        AddPreviewCornerCell(canvas, $"{DateTime.Now:yyyy} он", x4, y3, x5, y4);
    }

    private void AddConceptElevationHeaderPreview(
        Canvas canvas,
        AlbumPageDefinition? page,
        SheetPackageEntry entry,
        BuildingArchitectureConceptPageRegions regions)
    {
        double x0 = regions.InformationArea.X;
        double xRole = regions.ApprovalRoleArea.X + regions.ApprovalRoleArea.Width;
        double xApproval = regions.ApprovalNameArea.X + regions.ApprovalNameArea.Width;
        double x1 = regions.InformationArea.X + regions.InformationArea.Width;
        double y0 = regions.InformationArea.Y;
        double y1 = regions.InformationArea.Y + regions.InformationArea.Height;
        AddPreviewLine(canvas, x0, y1, x1, y1);
        AddPreviewLine(canvas, xApproval, y0, xApproval, y1);

        ConceptElevationHeaderSnapshot roster = ConceptElevationHeaderResolver.Resolve(
            state.Project.Foundation.ApprovalWorkflow,
            state.Project.Foundation.PlanningTask);
        const double padding = 3.0;
        const double headingHeight = 4.5;
        const double gap = 1.0;
        int rowCount = Math.Max(1, roster.ApprovedBy.Count) + roster.ReviewedBy.Count;
        double rowHeight = (y1 - y0 - padding * 2 - headingHeight * 2 - gap) / rowCount;
        double y = y0 + padding;
        AddPreviewText(canvas, "БАТЛАВ:", x0 + padding, y, xRole - x0 - padding * 2, headingHeight, 9.4, FontWeights.Bold, Brushes.Black, TextAlignment.Left);
        y += headingHeight;
        foreach (ProjectApprovalEntry official in roster.ApprovedBy)
        {
            AddElevationOfficialPreview(canvas, official, x0, xRole, xApproval, y, rowHeight);
            y += rowHeight;
        }

        y += gap;
        AddPreviewText(canvas, "ХЯНАВ:", x0 + padding, y, xRole - x0 - padding * 2, headingHeight, 9.4, FontWeights.Bold, Brushes.Black, TextAlignment.Left);
        y += headingHeight;
        foreach (ProjectApprovalEntry official in roster.ReviewedBy)
        {
            AddElevationOfficialPreview(canvas, official, x0, xRole, xApproval, y, rowHeight);
            y += rowHeight;
        }

        AddPreviewText(canvas, "ТАЙЛБАР", xApproval + padding, y0 + 2, x1 - xApproval - padding * 2, 5, 9.4, FontWeights.Bold, Brushes.Black, TextAlignment.Left);
        string description = page?.ElevationDescriptionOverride ?? entry.SheetDescription;
        AddPreviewText(canvas, description, xApproval + padding, y0 + 8, x1 - xApproval - padding * 2, y1 - y0 - 11, 9.4, FontWeights.Normal, Brushes.Black, TextAlignment.Left);
    }

    private static void AddElevationOfficialPreview(
        Canvas canvas,
        ProjectApprovalEntry official,
        double x0,
        double xRole,
        double xApproval,
        double y,
        double height)
    {
        AddPreviewText(
            canvas,
            ConceptCoverApprovalResolver.DisplayPosition(official).ToUpperInvariant(),
            x0 + 3,
            y,
            xRole - x0 - 6,
            height,
            8.6,
            FontWeights.Normal,
            Brushes.Black,
            TextAlignment.Left);
        AddPreviewText(
            canvas,
            official.PersonName.ToUpperInvariant(),
            xRole + 1,
            y,
            xApproval - xRole - 2,
            height,
            8.6,
            FontWeights.Normal,
            Brushes.Black);
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
        const double horizontalPaddingMm = 0.6;
        const double verticalPaddingMm = 0.4;
        double x = x0 + (alignment == TextAlignment.Left ? 1.2 : horizontalPaddingMm);
        double width = Math.Max(1, x1 - x - horizontalPaddingMm);
        double height = Math.Max(1, y1 - y0 - verticalPaddingMm * 2);
        string value = text?.Trim() ?? "";
        double printedHeightMm = BuildingArchitectureConceptPageLayout.CornerTextHeightMm;
        TextBlock block;
        while (true)
        {
            block = CreatePreviewCornerTextBlock(value, width, printedHeightMm, alignment);
            block.Measure(new Size(width, double.PositiveInfinity));
            bool widthFits = PreviewCornerWordsFit(value, printedHeightMm, width);
            bool heightFits = block.DesiredSize.Height <= height + 0.01;
            if ((widthFits && heightFits) ||
                printedHeightMm <= BuildingArchitectureConceptPageLayout.CornerMinimumTextHeightMm)
            {
                break;
            }

            printedHeightMm = Math.Max(
                BuildingArchitectureConceptPageLayout.CornerMinimumTextHeightMm,
                printedHeightMm - 0.1);
        }

        double contentHeight = Math.Min(height, Math.Max(1, block.DesiredSize.Height));
        block.Height = contentHeight;
        Canvas.SetLeft(block, x);
        Canvas.SetTop(block, y0 + verticalPaddingMm + Math.Max(0, (height - contentHeight) * 0.5));
        canvas.Children.Add(block);
    }

    private static TextBlock CreatePreviewCornerTextBlock(
        string text,
        double width,
        double printedHeightMm,
        TextAlignment alignment) =>
        new()
        {
            Text = text,
            Width = width,
            FontFamily = new FontFamily(BuildingArchitectureConceptPageLayout.FontFamilyName),
            FontSize = printedHeightMm / BuildingArchitectureConceptPageLayout.ArialCapHeightRatio,
            FontWeight = FontWeights.Normal,
            Foreground = Brushes.Black,
            TextWrapping = TextWrapping.Wrap,
            TextTrimming = TextTrimming.None,
            TextAlignment = alignment,
        };

    private static bool PreviewCornerWordsFit(string text, double printedHeightMm, double width)
    {
        double fontSize = printedHeightMm / BuildingArchitectureConceptPageLayout.ArialCapHeightRatio;
        return text
            .Split(
                new[] { ' ', '\t', '\r', '\n' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .All(word =>
            {
                var formatted = new FormattedText(
                    word,
                    System.Globalization.CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        new FontFamily(BuildingArchitectureConceptPageLayout.FontFamilyName),
                        FontStyles.Normal,
                        FontWeights.Normal,
                        FontStretches.Normal),
                    fontSize,
                    Brushes.Black,
                    1.0);
                return formatted.WidthIncludingTrailingWhitespace <= width + 0.01;
            });
    }

    private void AddConceptCoverPreview(Canvas canvas)
    {
        var boundary = AddPreviewRectangle(
            canvas,
            BuildingArchitectureConceptPageLayout.Frame,
            Brushes.White,
            Brushes.Black);
        boundary.StrokeThickness = 0.6;

        ConceptCoverApprovalSnapshot approvalSnapshot = ConceptCoverApprovalResolver.Resolve(
            state.Project.Foundation.ApprovalWorkflow,
            state.Project.Foundation.PlanningTask);
        const double bodyTextHeightMm = BuildingArchitectureConceptPageLayout.CoverBodyTextHeightMm;
        const double projectNameTextHeightMm = BuildingArchitectureConceptPageLayout.CoverProjectNameTextHeightMm;
        const double tableLeftMm = BuildingArchitectureConceptPageLayout.CoverTableLeftMm;
        const double reviewRoleRightMm = BuildingArchitectureConceptPageLayout.CoverReviewRoleRightMm;
        const double reviewNameRightMm = BuildingArchitectureConceptPageLayout.CoverReviewNameRightMm;
        const double processedLeftMm = BuildingArchitectureConceptPageLayout.CoverProcessedLeftMm;
        const double logoRightMm = BuildingArchitectureConceptPageLayout.CoverProcessedLogoRightMm;
        const double processedRoleRightMm = BuildingArchitectureConceptPageLayout.CoverProcessedRoleRightMm;
        const double processedNameRightMm = BuildingArchitectureConceptPageLayout.CoverProcessedNameRightMm;
        const double tableRightMm = BuildingArchitectureConceptPageLayout.CoverTableRightMm;
        const double tableTopMm = BuildingArchitectureConceptPageLayout.CoverTableTopMm;
        const double columnHeaderBottomMm = BuildingArchitectureConceptPageLayout.CoverColumnHeaderBottomMm;
        AddCoverPreviewText(canvas, "БАТЛАВ:", BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 281.205, 50, 8), bodyTextHeightMm, FontWeights.Normal);
        const double approvedRowsTopMm = 262.205;
        var approvedRowTopMm = approvedRowsTopMm;
        foreach (ProjectApprovalEntry entry in approvalSnapshot.ApprovedBy)
        {
            string approvedRole = ConceptCoverApprovalResolver.DisplayPosition(entry).ToUpperInvariant();
            string approvedName = entry.PersonName.ToUpperInvariant();
            double approvedRowHeightMm = Math.Max(
                8.0,
                Math.Max(
                    MeasureCoverPreviewTextHeightMm(approvedRole, 120, bodyTextHeightMm),
                    MeasureCoverPreviewTextHeightMm(approvedName, 75, bodyTextHeightMm)) + 1.2);
            double approvedRowBottomMm = approvedRowTopMm - approvedRowHeightMm;
            AddCoverPreviewText(canvas, approvedRole,
                BuildingArchitectureConceptPageLayout.FromBottomLeft(105.8, approvedRowBottomMm, 225.8, approvedRowTopMm), bodyTextHeightMm, FontWeights.Normal, TextAlignment.Left);
            AddCoverPreviewText(canvas, approvedName,
                BuildingArchitectureConceptPageLayout.FromBottomLeft(277.4, approvedRowBottomMm, 352.4, approvedRowTopMm), bodyTextHeightMm, FontWeights.Normal, TextAlignment.Left);
            approvedRowTopMm = approvedRowBottomMm;
        }

        AddCoverPreviewText(canvas, ValueOrDash(state.Project.Foundation.InitiationBasis.SiteAddress),
            BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 220.510, 180, 8), bodyTextHeightMm, FontWeights.Normal);
        AddCoverPreviewText(canvas, state.Project.Name,
            BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 207.510, 220, 12), projectNameTextHeightMm, FontWeights.Normal);
        AddCoverPreviewText(canvas, AlbumCoverDocumentTitle.Resolve(
                state.AlbumDocument.Definition.TemplateId,
                drawsWorkingDrawingEtalon: false),
            BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 186.760, 110, 8), bodyTextHeightMm, FontWeights.Normal);
        AddCoverPreviewText(canvas, "ЗӨВШӨӨРӨЛЦСӨН:",
            BuildingArchitectureConceptPageLayout.FromBottomLeft(tableLeftMm, 162.36, processedLeftMm, 168.86), bodyTextHeightMm, FontWeights.Normal, TextAlignment.Left);
        AddCoverPreviewText(canvas, "БОЛОВСРУУЛСАН:",
            BuildingArchitectureConceptPageLayout.FromBottomLeft(processedLeftMm, 162.36, tableRightMm, 168.86), bodyTextHeightMm, FontWeights.Normal, TextAlignment.Left);

        const double reviewRowsTopMm = BuildingArchitectureConceptPageLayout.CoverColumnHeaderBottomMm;
        const double reviewRowsBaseHeightMm = BuildingArchitectureConceptPageLayout.CoverReviewRowsBaseHeightMm;
        const double cellVerticalPaddingMm = 1.2;
        var reviewRows = new List<(ProjectApprovalEntry Entry, double BottomMm, double TopMm)>(approvalSnapshot.EndorsedBy.Count);
        var reviewRowTopMm = reviewRowsTopMm;
        var reviewBaseRowHeightMm = reviewRowsBaseHeightMm / approvalSnapshot.EndorsedBy.Count;
        foreach (ProjectApprovalEntry entry in approvalSnapshot.EndorsedBy)
        {
            var roleHeightMm = MeasureCoverPreviewTextHeightMm(
                ConceptCoverApprovalResolver.DisplayPosition(entry),
                reviewRoleRightMm - tableLeftMm - 2.4,
                bodyTextHeightMm);
            var nameHeightMm = MeasureCoverPreviewTextHeightMm(
                entry.PersonName,
                reviewNameRightMm - reviewRoleRightMm - 2.4,
                bodyTextHeightMm);
            var rowHeightMm = Math.Max(
                reviewBaseRowHeightMm,
                Math.Max(roleHeightMm, nameHeightMm) + cellVerticalPaddingMm);
            var reviewRowBottomMm = reviewRowTopMm - rowHeightMm;
            reviewRows.Add((entry, reviewRowBottomMm, reviewRowTopMm));
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
        ProjectInitiationBasis initiationBasis = state.Project.Foundation.InitiationBasis;
        string clientType = ProjectClientTypes.Normalize(initiationBasis.ClientType);
        string clientRole = ProjectClientTypes.ResolveCoverRole(
            clientType,
            initiationBasis.ClientName,
            initiationBasis.ClientRepresentativePosition);
        string clientRepresentativeName = ValueOrDash(ProjectClientTypes.ResolveCoverPersonName(
            clientType,
            initiationBasis.ClientName,
            initiationBasis.ClientRepresentativeName));
        CompanyProfile clientOrganization = initiationBasis.ClientOrganizationSnapshot;
        const double topHeaderBottomMm = BuildingArchitectureConceptPageLayout.CoverColumnHeaderBottomMm;
        var clientRequiredHeightMm = Math.Max(
            MeasureCoverPreviewTextHeightMm(
                clientRole,
                processedRoleRightMm - logoRightMm - 2.4,
                bodyTextHeightMm),
            MeasureCoverPreviewTextHeightMm(
                clientRepresentativeName,
                processedNameRightMm - processedRoleRightMm - 2.4,
                bodyTextHeightMm));
        var companyRequiredHeightMm = Math.Max(
            MeasureCoverPreviewTextHeightMm(
                companyRole,
                processedRoleRightMm - logoRightMm - 2.4,
                bodyTextHeightMm),
            MeasureCoverPreviewTextHeightMm(
                representativeName,
                processedNameRightMm - processedRoleRightMm - 2.4,
                bodyTextHeightMm));
        var sharedDataHeightMm = Math.Max(
            Math.Max(
                BuildingArchitectureConceptPageLayout.CoverClientDataBaseHeightMm,
                BuildingArchitectureConceptPageLayout.CoverCompanyDataBaseHeightMm),
            Math.Max(clientRequiredHeightMm, companyRequiredHeightMm) + cellVerticalPaddingMm);
        var topDataBottomMm = topHeaderBottomMm - sharedDataHeightMm;
        var bottomHeaderBottomMm = topDataBottomMm - BuildingArchitectureConceptPageLayout.CoverSectionHeaderHeightMm;
        var processedColumnBottomMm = bottomHeaderBottomMm - sharedDataHeightMm;
        var tableBottomMm = Math.Min(reviewRows[^1].BottomMm, processedColumnBottomMm);

        var table = AddPreviewRectangle(
            canvas,
            BuildingArchitectureConceptPageLayout.FromBottomLeft(tableLeftMm, tableBottomMm, tableRightMm, tableTopMm),
            Brushes.White,
            Brushes.Black);
        table.StrokeThickness = 0.7;
        AddPreviewBottomLine(canvas, tableLeftMm, columnHeaderBottomMm, tableRightMm, columnHeaderBottomMm);
        AddPreviewBottomLine(canvas, processedLeftMm, tableBottomMm, processedLeftMm, tableTopMm);
        AddPreviewBottomLine(canvas, reviewRoleRightMm, tableBottomMm, reviewRoleRightMm, tableTopMm);
        AddPreviewBottomLine(canvas, reviewNameRightMm, tableBottomMm, reviewNameRightMm, tableTopMm);
        AddPreviewBottomLine(canvas, logoRightMm, topDataBottomMm, logoRightMm, tableTopMm);
        AddPreviewBottomLine(canvas, processedRoleRightMm, topDataBottomMm, processedRoleRightMm, tableTopMm);
        AddPreviewBottomLine(canvas, processedNameRightMm, topDataBottomMm, processedNameRightMm, tableTopMm);
        AddPreviewBottomLine(canvas, logoRightMm, tableBottomMm, logoRightMm, topDataBottomMm);
        AddPreviewBottomLine(canvas, processedRoleRightMm, tableBottomMm, processedRoleRightMm, topDataBottomMm);
        AddPreviewBottomLine(canvas, processedNameRightMm, tableBottomMm, processedNameRightMm, topDataBottomMm);
        AddPreviewBottomLine(canvas, processedLeftMm, topDataBottomMm, tableRightMm, topDataBottomMm);
        AddPreviewBottomLine(canvas, processedLeftMm, bottomHeaderBottomMm, tableRightMm, bottomHeaderBottomMm);

        for (var index = 0; index < reviewRows.Count - 1; index++)
        {
            AddPreviewBottomLine(canvas, tableLeftMm, reviewRows[index].BottomMm, processedLeftMm, reviewRows[index].BottomMm);
        }

        AddCoverPreviewCell(canvas, "Албан тушаал", tableLeftMm, columnHeaderBottomMm, reviewRoleRightMm, tableTopMm);
        AddCoverPreviewCell(canvas, "Нэр", reviewRoleRightMm, columnHeaderBottomMm, reviewNameRightMm, tableTopMm);
        AddCoverPreviewCell(canvas, "Гарын үсэг", reviewNameRightMm, columnHeaderBottomMm, processedLeftMm, tableTopMm);
        AddCoverPreviewCell(canvas, "Албан тушаал", logoRightMm, columnHeaderBottomMm, processedRoleRightMm, tableTopMm);
        AddCoverPreviewCell(canvas, "Нэр", processedRoleRightMm, columnHeaderBottomMm, processedNameRightMm, tableTopMm);
        AddCoverPreviewCell(canvas, "Гарын үсэг", processedNameRightMm, columnHeaderBottomMm, tableRightMm, tableTopMm);

        foreach (var row in reviewRows)
        {
            AddCoverPreviewCell(canvas, ConceptCoverApprovalResolver.DisplayPosition(row.Entry), tableLeftMm, row.BottomMm, reviewRoleRightMm, row.TopMm, TextAlignment.Left);
            AddCoverPreviewCell(canvas, row.Entry.PersonName, reviewRoleRightMm, row.BottomMm, reviewNameRightMm, row.TopMm);
        }

        var companyMark = string.IsNullOrWhiteSpace(company.ShortName) ? company.Name : company.ShortName;
        AddCoverPreviewCell(canvas, BuildingArchitectureConceptPageLayout.CoverProcessedTopSectionTitle, processedLeftMm, topHeaderBottomMm, logoRightMm, tableTopMm);
        if (!TryAddCoverPreviewLogo(
                canvas,
                company,
                BuildingArchitectureConceptPageLayout.FromBottomLeft(
                    processedLeftMm,
                    topDataBottomMm,
                    logoRightMm,
                    topHeaderBottomMm)))
        {
            AddCoverPreviewCell(canvas, ValueOrDash(companyMark), processedLeftMm, topDataBottomMm, logoRightMm, topHeaderBottomMm);
        }
        AddCoverPreviewCell(canvas, companyRole, logoRightMm, topDataBottomMm, processedRoleRightMm, topHeaderBottomMm);
        AddCoverPreviewCell(canvas, representativeName, processedRoleRightMm, topDataBottomMm, processedNameRightMm, topHeaderBottomMm);

        AddCoverPreviewCell(canvas, BuildingArchitectureConceptPageLayout.CoverProcessedBottomSectionTitle, processedLeftMm, bottomHeaderBottomMm, logoRightMm, topDataBottomMm);
        AddCoverPreviewCell(canvas, "Албан тушаал", logoRightMm, bottomHeaderBottomMm, processedRoleRightMm, topDataBottomMm);
        AddCoverPreviewCell(canvas, "Нэр", processedRoleRightMm, bottomHeaderBottomMm, processedNameRightMm, topDataBottomMm);
        AddCoverPreviewCell(canvas, "Гарын үсэг", processedNameRightMm, bottomHeaderBottomMm, tableRightMm, topDataBottomMm);
        if (ProjectClientTypes.UsesLogo(clientType))
        {
            _ = TryAddCoverPreviewLogo(
                canvas,
                clientOrganization,
                BuildingArchitectureConceptPageLayout.FromBottomLeft(
                    processedLeftMm,
                    tableBottomMm,
                    logoRightMm,
                    bottomHeaderBottomMm));
        }
        AddCoverPreviewCell(canvas, clientRole, logoRightMm, tableBottomMm, processedRoleRightMm, bottomHeaderBottomMm);
        AddCoverPreviewCell(canvas, clientRepresentativeName, processedRoleRightMm, tableBottomMm, processedNameRightMm, bottomHeaderBottomMm);

        AddCoverPreviewText(canvas, "Улаанбаатар хот",
            BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 26.125, 200, 12), bodyTextHeightMm, FontWeights.Normal);
        AddCoverPreviewText(canvas, $"{DateTime.Now:yyyy} он",
            BuildingArchitectureConceptPageLayout.CenteredFromBottomLeft(210, 15.625, 90, 12), bodyTextHeightMm, FontWeights.Normal);
    }

    private bool TryAddCoverPreviewLogo(
        Canvas canvas,
        CompanyProfile company,
        PageRectMm rect)
    {
        string path = ResolveClientLogoPath(company.LogoPath);
        BitmapSource? bitmap = LoadLocalBitmap(path);
        if (bitmap is null || rect.Width <= 3 || rect.Height <= 3)
            return false;

        company.Normalize();
        double viewportWidth = rect.Width - 3;
        double viewportHeight = rect.Height - 3;
        var viewport = new Canvas
        {
            Width = viewportWidth,
            Height = viewportHeight,
            ClipToBounds = true,
        };
        Canvas.SetLeft(viewport, rect.X + 1.5);
        Canvas.SetTop(viewport, rect.Y + 1.5);

        double contain = Math.Min(
            viewportWidth / Math.Max(1, bitmap.PixelWidth),
            viewportHeight / Math.Max(1, bitmap.PixelHeight));
        double width = bitmap.PixelWidth * contain * company.LogoScale;
        double height = bitmap.PixelHeight * contain * company.LogoScale;
        var image = new Image
        {
            Source = bitmap,
            Width = width,
            Height = height,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true,
        };
        Canvas.SetLeft(image,
            (viewportWidth - width) * 0.5 + company.LogoOffsetX * viewportWidth * 0.5);
        Canvas.SetTop(image,
            (viewportHeight - height) * 0.5 + company.LogoOffsetY * viewportHeight * 0.5);
        viewport.Children.Add(image);
        canvas.Children.Add(viewport);
        return true;
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
            BorderThickness = new Thickness(0),
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
            BorderThickness = new Thickness(0),
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

    private sealed record SourceWorkspaceItem(
        ProjectDesignSource? Source,
        bool IsVisualization,
        string Name,
        string Detail,
        ProjectCloudSourceReference? CloudSource = null,
        ProjectCloudAlbumComponentReference? CloudComponent = null,
        bool HasLocalPayload = true)
    {
        public bool IsCloudPlaceholder => !IsVisualization &&
            !HasLocalPayload &&
            (Source is not null ||
             CloudSource is not null ||
             CloudComponent is not null);

        public string SelectionKey => IsVisualization
            ? VisualizationSourceSelectionKey
            : Source is not null
                ? Source.Id
                : CloudSource is not null
                    ? "cloud-source:" + CloudSource.SourceId
                    : "cloud-component:" + (CloudComponent?.Code ?? "");

        public static SourceWorkspaceItem Visualizations(int imageCount, int imagesPerPage) => new(
            null,
            true,
            "Харагдах байдал",
            $"Зураг | {imageCount} зураг · {imagesPerPage}/хуудас");

        public static SourceWorkspaceItem Cloud(
            ProjectCloudSourceReference source,
            ProjectCloudAlbumComponentReference? component,
            string name,
            string detail) => new(
            null,
            false,
            name,
            detail,
            CloudSource: source,
            CloudComponent: component,
            HasLocalPayload: false);

        public static SourceWorkspaceItem Cloud(
            ProjectCloudAlbumComponentReference component,
            string name,
            string detail) => new(
            null,
            false,
            name,
            detail,
            CloudComponent: component,
            HasLocalPayload: false);

        public static SourceWorkspaceItem CloudBinding(
            ProjectDesignSource source,
            ProjectCloudSourceReference? cloudSource,
            ProjectCloudAlbumComponentReference? component,
            string name,
            string detail) => new(
            source,
            false,
            name,
            detail,
            CloudSource: cloudSource,
            CloudComponent: component,
            HasLocalPayload: false);

        public override string ToString() => $"{Name}\n{Detail}";
    }

    private sealed class SheetWorkspaceItem : INotifyPropertyChanged
    {
        private ImageSource? thumbnailSource;
        private string thumbnailMessage = "Уншиж байна";

        public SheetWorkspaceItem(
            SheetRecord record,
            string number,
            string name,
            string building,
            string application,
            string size,
            bool isActive,
            string status)
        {
            Record = record;
            Number = number;
            Name = name;
            Building = building;
            Application = application;
            Size = size;
            IsActive = isActive;
            Status = status;
        }

        public SheetRecord Record { get; }
        public string Number { get; }
        public string Name { get; }
        public string Building { get; }
        public string Application { get; }
        public string Size { get; }
        public bool IsActive { get; }
        public string Status { get; }
        public ImageSource? ThumbnailSource
        {
            get => thumbnailSource;
            private set
            {
                if (ReferenceEquals(thumbnailSource, value))
                {
                    return;
                }
                thumbnailSource = value;
                OnPropertyChanged();
            }
        }
        public string ThumbnailMessage
        {
            get => thumbnailMessage;
            private set
            {
                if (string.Equals(thumbnailMessage, value, StringComparison.Ordinal))
                {
                    return;
                }
                thumbnailMessage = value;
                OnPropertyChanged();
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public void SetThumbnail(ImageSource? image, string message)
        {
            ThumbnailSource = image;
            ThumbnailMessage = message;
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private enum AlbumWorkspaceNodeKind
    {
        Page,
        Album,
        Studio,
        GeneralPlan,
        EngineeringInfrastructure,
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
        public int? GeneratedPageIndex { get; init; }
        public string GeneratedNavigationKey { get; init; } = "";
        public VisualizationAlbumPagePlan? VisualizationPlan { get; init; }
        public string CanonicalComponentCode { get; init; } = "";
        public int CanonicalComponentPageOffset { get; init; }
        public ImageSource? ThumbnailSource => thumbnailSource;
        public string ThumbnailMessage => thumbnailMessage;

        public bool IsGroup => Kind != AlbumWorkspaceNodeKind.Page;
        public IAlbumPageRoleOwner? RoleOwner => Page is not null
            ? Page
            : Component?.Kind == AlbumCompositionKind.Generated
                ? Component
                : null;
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

    private sealed record PdfPageSizeChoice(string Code, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record PdfFormatValueChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ModuleCountChoice(int Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ContentKindChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record SectionChoice(Guid? Id, string Label)
    {
        public override string ToString() => Label;
    }
}
