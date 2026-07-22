using System.Diagnostics;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
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
    private string? boundAlbumProjectId;
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
        ConfigureVisualizationImagesList();
        sourceContentTitle.Foreground = StudioTheme.TextBrush;
        sourceContentHost.Children.Add(receivedSheetsWorkspaceList);
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
        var rescan = StudioWidgets.CreateButton("Эх үүсвэр шалгах");
        rescan.ToolTip =
            "Зөвхөн энэ төхөөрөмжийн Revit/AutoCAD package, АТД, гэрчилгээ, тусгай зөвшөөрөл " +
            "болон харагдах байдлын файлын өөрчлөлтийг шалгаж локал album-ыг шинэчилнэ. Cloud төслийг татахгүй.";
        rescan.Click += (_, _) => CheckForSourceUpdates();
        sourceGroup.Children.Add(addSource);
        sourceGroup.Children.Add(addVisualizationSource);
        sourceGroup.Children.Add(rescan);
        ribbon.Children.Add(sourceGroup);
        return ribbon;
    }

    private void CheckForSourceUpdates()
    {
        if (!state.HasOpenProject || sourceRefreshInProgress || !EnsureProjectContentPermission())
        {
            return;
        }

        sourceRefreshInProgress = true;
        var selectedSourceId = (designSourcesWorkspaceList.SelectedItem as SourceWorkspaceItem)?.SelectionKey;
        SheetIntakeScanResult scan;
        var assetScan = new ProjectAssetSourceReconciliationResult();
        CityGenProjectSiteReconciliationResult siteScan;
        try
        {
            SetStatus("Локал эх үүсвэрийн өөрчлөлт шалгаж байна...");
            assetScan.Merge(ReconcileCompanyAssetSources());
            assetScan.Merge(state.ReconcileProjectAssetSources());
            siteScan = state.ReconcileCityGenProjectSite();
            scan = state.Intake.Rescan();
        }
        catch (Exception exception)
        {
            sourceRefreshInProgress = false;
            SetStatus($"Локал эх үүсвэр шалгахад алдаа: {exception.Message}");
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
                UpdateAlbum(
                    silent: false,
                    statusPrefix: BuildSourceRefreshSummary(scan, assetScan, siteScan));
            }
            finally
            {
                sourceRefreshInProgress = false;
            }
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private static string BuildSourceRefreshSummary(
        SheetIntakeScanResult scan,
        ProjectAssetSourceReconciliationResult assets,
        CityGenProjectSiteReconciliationResult site)
    {
        var summary = scan.ChangedPackageCount == 0
            ? $"{scan.ManifestCount} package шалгав, шинэ source өөрчлөлтгүй"
            : $"{scan.ChangedPackageCount} package шинэчлэгдэж, " +
              $"{scan.UpdatedSheetCount} sheet шинэчлэгдэн, {scan.RemovedSheetCount} sheet хасагдав";
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

        var itemStyle = new Style(typeof(ListViewItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.TextBrush));
        itemStyle.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 4, 5, 4)));
        receivedSheetsWorkspaceList.ItemContainerStyle = itemStyle;

        var view = new GridView();
        var headerStyle = new Style(typeof(GridViewColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, StudioTheme.PanelAltBrush));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.MutedTextBrush));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
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
        SetStatus(dialog.ResultSource.Kind == DesignSourceKind.Revit
            ? $"RVT эх үүсвэр холбогдлоо: {dialog.ResultSource.DisplayName}. Revit-ийн Альбум хэсгээс Studio руу илгээнэ."
            : $"Эх үүсвэр нэмэгдлээ: {dialog.ResultSource.DisplayName}");
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
        string sourceOwner = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
        if (string.IsNullOrWhiteSpace(sourceOwner))
            sourceOwner = currentOwner;
        string sourceKey = ProjectCloudSyncMetadata.CloudSourceKey(source);
        ProjectCloudSourceReference? sharedSource = state.Project.Cloud.SharedSources
            .OfType<ProjectCloudSourceReference>()
            .FirstOrDefault(item =>
                string.Equals(item.SourceKey, sourceKey, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(item.OwnerEmail, sourceOwner, StringComparison.OrdinalIgnoreCase));
        if (sharedSource is not null &&
            account.IsSignedIn &&
            state.Project.Cloud.Origin.Equals(ProjectOrigins.Cloud, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId))
        {
            try
            {
                await account.RetireSourcePackageAsync(
                    state.Project.Cloud.ServerProjectId,
                    sharedSource.SourceId);
                state.Project.Cloud.SharedSources.RemoveAll(item =>
                    item is not null &&
                    string.Equals(item.SourceId, sharedSource.SourceId, StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (
                exception is StudioAccountException or HttpRequestException or TaskCanceledException)
            {
                SetStatus("Cloud эх үүсвэрийг салгаж чадсангүй. Локал холбоос хэвээр үлдлээ: " + exception.Message);
                return;
            }
        }

        if (!string.IsNullOrWhiteSpace(sourceOwner) && !string.IsNullOrWhiteSpace(sourceKey))
        {
            state.MarkAlbumComponentChanged(
                StudioAlbumComponentIdentity.SourceCode(sourceOwner, sourceKey));
        }

        int removedPageCount = state.RemoveDesignSource(source);
        RefreshSourceWorkspace();
        RefreshAlbumWorkspace();
        UpdateAlbum(silent: true, statusPrefix: "Эх үүсвэр хасагдсан альбум шинэчлэгдлээ");
        SetStatus(
            $"Эх үүсвэрийн бүртгэл болон {removedPageCount} альбумын хуудасны холбоосыг хаслаа: " +
            $"{source.DisplayName}. Эх файл, хүлээн авсан PDF-үүд хэвээр үлдсэн.");
    }

    private void RelinkSelectedNativeSource()
    {
        if (!EnsureProjectContentPermission() ||
            designSourcesWorkspaceList.SelectedItem is not SourceWorkspaceItem { Source: ProjectDesignSource source })
        {
            return;
        }
        if (!CanEditLocalSource(source))
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
        source.Metadata ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.Metadata["local.nativeRelinkedAtUtc"] = DateTimeOffset.UtcNow.ToString("O");
        state.SaveProject();
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
        try
        {
            IReadOnlyList<StudioCloudDesignPackage> packages = await account.ListDesignPackagesAsync(projectId);
            string currentEmail = account.Current?.Email ?? "";
            List<StudioCloudSourcePackage> available = LatestCloudSources(packages)
                .Where(cloudSource => cloudSource.CustodianEmail.Equals(currentEmail, StringComparison.OrdinalIgnoreCase))
                .Where(cloudSource => !state.Project.Sources.Any(local =>
                    !ReferenceEquals(local, source) &&
                    ProjectCloudSyncMetadata.CloudSourceKey(local).Equals(cloudSource.SourceKey, StringComparison.OrdinalIgnoreCase)))
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
            if (!state.HasOpenProject ||
                !state.Project.Cloud.ServerProjectId.Equals(projectId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            ProjectCloudSyncMetadata.BindToCloudSource(
                state.Project,
                source,
                dialog.SelectedSource.SourceKey);
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
    }

    private async Task TransferCloudSourceCustodyAsync()
    {
        if (!state.HasOpenProject || !CanManageProjectTeam())
        {
            SetStatus("Cloud source-ийн хариуцагч шилжүүлэхэд төслийн баг удирдах role шаардлагатай.");
            return;
        }
        string projectId = state.Project.Cloud.ServerProjectId;
        try
        {
            StudioCloudProjectDetail project = await account.GetProjectAsync(projectId);
            IReadOnlyList<StudioProjectRole> roleCatalog = await account.ListProjectRolesAsync();
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
                dialog.Draft.ParticipantId);
            SetStatus(
                $"Cloud source хариуцагч шилжлээ: {dialog.Draft.DisplayLabel}. " +
                "Native файлыг талууд платформоос гадуур хүлээлцэж, шинэ хариуцагч локал файлаа дахин холбоно.");
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            SetStatus("Cloud source хариуцагч шилжсэнгүй: " + exception.Message);
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
        if (visualizations.IsConfiguredForProject(state.Project.ProjectId))
        {
            items.Add(SourceWorkspaceItem.Visualizations(
                visualizations.ImagesForProject(state.Project.ProjectId).Count,
                visualizations.ImagesPerPage));
            representedCloudSources.Add(CloudSourceIdentity(
                currentOwner,
                StudioAlbumComponentIdentity.VisualizationSourceKey));
        }
        if (!string.IsNullOrWhiteSpace(currentOwner) && HasOwnedAtdDocuments(currentOwner))
        {
            representedCloudSources.Add(CloudSourceIdentity(
                currentOwner,
                StudioAlbumComponentIdentity.AtdSourceKey));
        }
        items.AddRange(state.Project.Sources
            .Select(source =>
            {
                string owner = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
                if (string.IsNullOrWhiteSpace(owner))
                    owner = currentOwner;
                string sourceKey = ProjectCloudSyncMetadata.CloudSourceKey(source);
                representedCloudSources.Add(CloudSourceIdentity(
                    owner,
                    sourceKey));
                ProjectCloudAlbumComponentReference? component = sharedComponents.FirstOrDefault(item =>
                    CloudSourceIdentity(item.OwnerEmail, item.SourceKey).Equals(
                        CloudSourceIdentity(owner, sourceKey),
                        StringComparison.OrdinalIgnoreCase));
                string detail = $"{source.DisplayName}  |  {SourceStatusLabel(source.Status)}";
                if (component is not null)
                    detail += $" | Альбум #{component.Order}";
                return new SourceWorkspaceItem(
                    source,
                    false,
                    SourceDocumentLabel(source),
                    detail,
                    CloudComponent: component);
            })
            .ToList());
        foreach (ProjectCloudSourceReference cloudSource in
                 (state.Project.Cloud.SharedSources ?? []).OfType<ProjectCloudSourceReference>())
        {
            string identity = CloudSourceIdentity(cloudSource.OwnerEmail, cloudSource.SourceKey);
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
            string identity = CloudSourceIdentity(component.OwnerEmail, component.SourceKey);
            if (!representedCloudSources.Add(identity))
                continue;
            items.Add(SourceWorkspaceItem.Cloud(
                component,
                string.IsNullOrWhiteSpace(component.Label) ? component.SourceKey : component.Label,
                $"Cloud album slot | {component.OwnerEmail} | " +
                $"{component.PageNumbers.Count} page | Зөвхөн харах"));
        }
        designSourcesWorkspaceList.ItemsSource = items;
        designSourcesWorkspaceList.SelectedItem = items.FirstOrDefault(item =>
            string.Equals(item.SelectionKey, selectSourceId, StringComparison.OrdinalIgnoreCase));
        if (designSourcesWorkspaceList.SelectedItem is null && items.Count > 0)
        {
            designSourcesWorkspaceList.SelectedIndex = 0;
        }

        RefreshReceivedSheetWorkspace();
        RefreshSourceDetails();
    }

    private void RefreshReceivedSheetWorkspace()
    {
        if (designSourcesWorkspaceList.SelectedItem is SourceWorkspaceItem { IsCloudPlaceholder: true })
        {
            receivedSheetsWorkspaceList.Visibility = Visibility.Visible;
            visualizationImagesWorkspaceList.Visibility = Visibility.Collapsed;
            sourceContentTitle.Text = "Cloud эх үүсвэрийн байрлал";
            receivedSheetsWorkspaceList.ItemsSource = Array.Empty<SheetWorkspaceItem>();
            return;
        }

        bool visualizationsSelected =
            designSourcesWorkspaceList.SelectedItem is SourceWorkspaceItem { IsVisualization: true };
        receivedSheetsWorkspaceList.Visibility = visualizationsSelected ? Visibility.Collapsed : Visibility.Visible;
        visualizationImagesWorkspaceList.Visibility = visualizationsSelected ? Visibility.Visible : Visibility.Collapsed;
        sourceContentTitle.Text = visualizationsSelected ? "Харагдах байдлын зураг" : "Хүлээн авсан sheets";
        if (visualizationsSelected)
        {
            RefreshVisualizationImagesList();
            return;
        }

        var records = state.Library.Snapshot().AsEnumerable();
        if (designSourcesWorkspaceList.SelectedItem is SourceWorkspaceItem { Source: ProjectDesignSource source })
        {
            records = source.UseLegacySheetKeys
                ? records.Where(record => string.IsNullOrWhiteSpace(record.SourceId))
                : records.Where(record => string.Equals(
                    record.SourceId,
                    source.Id,
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
            ProjectCloudSourceReference? cloudSource = selected.CloudSource;
            ProjectCloudAlbumComponentReference? component = selected.CloudComponent;
            string owner = cloudSource?.OwnerEmail ?? component?.OwnerEmail ?? "";
            string sourceKey = cloudSource?.SourceKey ?? component?.SourceKey ?? "";
            int itemCount = component?.PageNumbers.Count ?? cloudSource?.SheetCount ?? 0;
            sourceDetailsText.Text =
                $"Эх үүсвэр: {selected.Name}\n" +
                $"Эзэмшигч: {(string.IsNullOrWhiteSpace(owner) ? "-" : owner)}\n" +
                $"Source key: {(string.IsNullOrWhiteSpace(sourceKey) ? "-" : sourceKey)}\n" +
                $"Альбумын дараалал: {(component?.Order.ToString() ?? "-")}\n" +
                $"Хуудас / sheet: {itemCount}";
            sourceWorkflowText.Text =
                "Энэ нь Cloud ERA-аас ирсэн metadata placeholder. Эх файл дамжуулагдаагүй. " +
                "Үүсгэсэн хэрэглэгч нь өөрийн төхөөрөмжөөс шинэчлэх, солих эсвэл хасах эрхтэй.";
            openNativeSourceButton.Visibility = Visibility.Collapsed;
            openSourceFolderButton.Visibility = Visibility.Collapsed;
            visualizationSourceControls.Visibility = Visibility.Collapsed;
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
        sourceWorkflowText.Text = source.Kind switch
        {
            DesignSourceKind.Revit when sheetCount == 0 =>
                "RVT холбоос бэлэн. Revit дээр файлаа нээгээд Erk-S Platform > Альбум > Studio руу илгээхэд хуудаснууд энд автоматаар орж ирнэ.",
            DesignSourceKind.Revit =>
                "Revit дээр өөрчлөлт хийсний дараа Erk-S Platform > Альбум > Studio руу илгээхэд нэмэгдсэн, өөрчлөгдсөн, хасагдсан хуудаснууд автоматаар шинэчлэгдэнэ.",
            _ => "Native эх файл Studio болон Cloud ERA руу хуулагдахгүй; зөвхөн энэ төхөөрөмж дээрх холбоос хадгалагдана.",
        };
    }

    private void SetNativeSourceActionsVisible(bool hasNativeSource, bool ownsSource = false)
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

        bool canEdit = hasNativeSource && ownsSource && CanEditProjectContent();
        relinkNativeSourceButton.IsEnabled = canEdit;
        removeDesignSourceButton.IsEnabled = canEdit;
        bindCloudSourceButton.IsEnabled = canEdit && account.IsSignedIn;
        transferSourceCustodyButton.IsEnabled = cloudProject && CanManageProjectTeam();
    }

    private bool CanEditLocalSource(ProjectDesignSource source)
    {
        string owner = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
        if (string.IsNullOrWhiteSpace(owner))
            return true;
        string current = (account.Current?.Email ?? "").Trim().ToLowerInvariant();
        return !string.IsNullOrWhiteSpace(current) &&
            owner.Equals(current, StringComparison.OrdinalIgnoreCase);
    }

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
        var updateAlbum = StudioWidgets.CreateIconTextButton("icon-album.svg", "Эх үүсвэрээс шинэчлэх");
        updateAlbum.ToolTip =
            "Бүх локал linked source-ийг шалгаж, өөрчлөгдсөн мэдээллээр album-ыг дахин бүрдүүлнэ. " +
            "Устсан source-ийн агуулга хуудсанд үлдэхгүй. Cloud мэдээлэл татахгүй.";
        updateAlbum.Background = StudioTheme.AccentBrush;
        updateAlbum.BorderBrush = StudioTheme.AccentBrush;
        updateAlbum.Click += (_, _) => CheckForSourceUpdates();
        var editVisualizations = StudioWidgets.CreateIconTextButton(
            "icon-sources.svg",
            "Харагдах байдал",
            "Альбумын хуудсан дээрх зургуудыг сонгож идэвхгүй болгох эсвэл буцаан оруулах");
        editVisualizations.Click += (_, _) => EditVisualizationAlbumPages();
        var editSiteContext = StudioWidgets.CreateIconTextButton(
            "icon-project.svg",
            "Байршлын зураг",
            "Байршлын схем болон орчны тоймын хамрах хүрээг тохируулна");
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
            if (!string.IsNullOrWhiteSpace(lastAlbumPath) && File.Exists(lastAlbumPath))
            {
                Process.Start(new ProcessStartInfo(lastAlbumPath) { UseShellExecute = true });
            }
        };
        documentGroup.Children.Add(save);
        documentGroup.Children.Add(updateAlbum);
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

    private void EditSiteContextMaps()
    {
        if (!EnsureProjectContentPermission())
            return;

        var dialog = new SiteContextMapEditorDialog(
            state.ResolveProjectFolder(),
            state.Project.ProjectId,
            state.Project.SiteContext)
        {
            Owner = Window.GetWindow(Root),
        };
        bool persistedDuringDialog = false;
        dialog.SiteContextSaved += snapshot =>
        {
            state.Project.SiteContext = snapshot;
            state.MarkSiteContextChanged();
            persistedDuringDialog = true;
        };
        _ = dialog.ShowDialog();
        if (!dialog.HasSavedChanges)
            return;

        if (!persistedDuringDialog)
        {
            state.Project.SiteContext = dialog.Result;
            state.MarkSiteContextChanged();
        }
        RefreshAlbumWorkspace(selectItemKey: "component:site-context:None:1");
        UpdateAlbum(
            silent: false,
            statusPrefix: "Байршлын схем болон орчны тойм шинэчлэгдлээ");
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
                sheet.Entry.ContentKind,
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
        int visualizationImageCount = CurrentProjectVisualizationImages().Count;
        includeCoverCheck.Visibility = hasComposition ? Visibility.Collapsed : Visibility.Visible;
        if (hasComposition)
        {
            var ready = state.Album.Composition.Count(item =>
                item.Kind == AlbumCompositionKind.Generated ||
                (item.Id.Equals("visualizations", StringComparison.OrdinalIgnoreCase) &&
                 visualizationImageCount > 0) ||
                state.Album.Pages.Any(page => string.Equals(
                    page.TemplateSlotId,
                    item.Id,
                    StringComparison.OrdinalIgnoreCase)));
            albumInfoText.Text =
                $"Бүрдэл {ready}/{state.Album.Composition.Count} · {state.Album.Pages.Count} source sheet · " +
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

        AlbumProject buildProject = state.CreateAlbumBuildProject();
        IReadOnlyList<ConceptGeneratedPagePlan> generatedPlans =
            BuildingArchitectureConceptGeneratedPagePlanner.Create(buildProject);
        var sequence = BuildingArchitectureConceptAlbumSequencer.Create(
            state.Album,
            state.Album.Pages,
            state.Library,
            state.Project.Sources,
            generatedPlans.Count);
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
            }));
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
        // A project bind also prepares the hidden album navigator. Do not open
        // and parse a potentially large PDF until the user enters the album page.
        if (activePage != StudioPage.Albums)
        {
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
        CancelVisualizationThumbnailLoading();
        albumPdfNavigationSerial++;
        albumThumbnailLoadCancellation?.Cancel();
        albumThumbnailLoadCancellation?.Dispose();
        albumThumbnailLoadCancellation = null;
        loadedAlbumPdfDocumentKey = null;
        selectedAlbumWorkspaceKey = null;
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
        var project = state.CreateAlbumBuildProject();
        List<ConceptGeneratedPagePlan> generated =
            BuildingArchitectureConceptGeneratedPagePlanner.Create(project).ToList();

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
            int visualizationPageOffset = generated.Count;
            if (generated.Count == 0 && project.Album.IncludeCover)
                visualizationPageOffset++;
            if (project.Album.IncludeTableOfContents)
                visualizationPageOffset++;

            AlbumBuildRequest visualizationRequest = AlbumBuilder.CreateRequest(project, state.Library);
            visualizationPageOffset += visualizationRequest.Sections
                .SelectMany(section => section.Pages)
                .Where(buildPage => File.Exists(buildPage.Sheet.PdfPath))
                .Sum(buildPage => Math.Max(1, buildPage.Sheet.Entry.PageCount));
            return visualizationPageOffset + visualizationPlan.PageIndex + 1;
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

            string previewPdfPath = await PdfPreviewFileCache.GetPreviewPathAsync(targetPdfPath);
            if (navigationSerial != albumPdfNavigationSerial)
            {
                return;
            }

            var pdf = new FileInfo(previewPdfPath);
            var documentKey = $"{pdf.FullName}|{pdf.LastWriteTimeUtc.Ticks}|{pdf.Length}";
            var builder = new UriBuilder(new Uri(previewPdfPath))
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
            entry.ContentKind,
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
        AddPreviewCornerCell(canvas, entry?.ScaleText ?? "", x4, y1, x5, y2);
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
        AddCoverPreviewText(canvas, "/ЗАГВАР ЗУРАГ/",
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
        ProjectCloudAlbumComponentReference? CloudComponent = null)
    {
        public bool IsCloudPlaceholder => Source is null &&
            (CloudSource is not null || CloudComponent is not null);

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
            CloudComponent: component);

        public static SourceWorkspaceItem Cloud(
            ProjectCloudAlbumComponentReference component,
            string name,
            string detail) => new(
            null,
            false,
            name,
            detail,
            CloudComponent: component);

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
        public int? GeneratedPageIndex { get; init; }
        public string GeneratedNavigationKey { get; init; } = "";
        public VisualizationAlbumPagePlan? VisualizationPlan { get; init; }
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
