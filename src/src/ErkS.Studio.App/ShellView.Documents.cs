using System.IO;
using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using Microsoft.Win32;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private readonly ListView atdDocumentsList = new() { MinHeight = 92, MaxHeight = 190 };
    private readonly Button atdAddDocumentsButton = StudioWidgets.CreateGlyphTextButton(
        "\uE8E5",
        "Файл нэмэх",
        "АТД-ийн PDF эсвэл зурган хуулбар сонгох");
    private readonly Button atdRelinkDocumentButton = StudioWidgets.CreateGlyphTextButton(
        "\uE71B",
        "Эх файлыг дахин заах",
        "Сонгосон хуулбарын source link-ийг ID-г нь өөрчлөхгүйгээр шинэчлэх");
    private readonly Button atdRemoveDocumentButton = StudioWidgets.CreateGlyphTextButton(
        "\uE74D",
        "Хасах",
        "Сонгосон АТД-ийн хуулбарыг төслөөс хасах");
    private List<ProjectFileReference> atdDocumentDrafts = [];

    private readonly ListView companyRegistrationDocumentsList = new() { MinHeight = 92, MaxHeight = 180 };
    private readonly ListView companyLicenseDocumentsList = new() { MinHeight = 92, MaxHeight = 180 };
    private readonly Button companyAddRegistrationDocumentButton = StudioWidgets.CreateGlyphTextButton(
        "\uE8E5",
        "Файл нэмэх",
        "Байгууллагын гэрчилгээний PDF эсвэл зураг сонгох");
    private readonly Button companyRelinkRegistrationDocumentButton = StudioWidgets.CreateGlyphTextButton(
        "\uE71B",
        "Эх файлыг дахин заах",
        "Сонгосон гэрчилгээний source link-ийг шинэчлэх");
    private readonly Button companyRemoveRegistrationDocumentButton = StudioWidgets.CreateGlyphTextButton(
        "\uE74D",
        "Хасах",
        "Сонгосон гэрчилгээний хуулбарыг хасах");
    private readonly Button companyAddLicenseDocumentButton = StudioWidgets.CreateGlyphTextButton(
        "\uE8E5",
        "Файл нэмэх",
        "Тусгай зөвшөөрлийн PDF эсвэл зураг сонгох");
    private readonly Button companyRelinkLicenseDocumentButton = StudioWidgets.CreateGlyphTextButton(
        "\uE71B",
        "Эх файлыг дахин заах",
        "Сонгосон тусгай зөвшөөрлийн source link-ийг шинэчлэх");
    private readonly Button companyRemoveLicenseDocumentButton = StudioWidgets.CreateGlyphTextButton(
        "\uE74D",
        "Хасах",
        "Сонгосон тусгай зөвшөөрлийн хуулбарыг хасах");
    private List<ProjectFileReference> companyRegistrationDocumentDrafts = [];
    private List<ProjectFileReference> companyLicenseDocumentDrafts = [];

    private UIElement BuildAtdDocumentEditor()
    {
        ConfigureDocumentList(atdDocumentsList);
        atdDocumentsList.SelectionChanged += (_, _) =>
        {
            bool canEditSelected = atdDocumentsList.SelectedItem is DocumentAssetRow selected &&
                CanEditAtdDocument(selected.Document);
            atdRelinkDocumentButton.IsEnabled = foundationEditMode &&
                !foundationSaveInProgress &&
                canEditSelected;
            atdRemoveDocumentButton.IsEnabled = foundationEditMode &&
                !foundationSaveInProgress &&
                canEditSelected;
        };
        atdAddDocumentsButton.Click += (_, _) => AddAtdDocuments();
        atdRelinkDocumentButton.Click += (_, _) => RelinkAtdDocument();
        atdRemoveDocumentButton.Click += (_, _) => RemoveAtdDocument();
        return BuildDocumentCollectionEditor(
            atdDocumentsList,
            atdAddDocumentsButton,
            atdRelinkDocumentButton,
            atdRemoveDocumentButton,
            "PDF, PNG, JPG сонгож болно. Олон файл болон олон хуудаст PDF-ийг Studio автоматаар зохиомжлоно.");
    }

    private UIElement BuildCompanyRegistrationDocumentEditor()
    {
        ConfigureDocumentList(companyRegistrationDocumentsList);
        companyRegistrationDocumentsList.SelectionChanged += (_, _) =>
        {
            companyRelinkRegistrationDocumentButton.IsEnabled =
                companyEditorMode != CompanyEditorMode.View &&
                !companySaveInProgress &&
                selectedCompanyEntry?.CanManage == true &&
                companyRegistrationDocumentsList.SelectedItem is not null;
            companyRemoveRegistrationDocumentButton.IsEnabled =
                companyEditorMode != CompanyEditorMode.View &&
                !companySaveInProgress &&
                selectedCompanyEntry?.CanManage == true &&
                companyRegistrationDocumentsList.SelectedItem is not null;
        };
        companyAddRegistrationDocumentButton.Click += (_, _) => AddCompanyDocuments(
            ProjectDocumentCategories.CompanyRegistrationCertificate);
        companyRelinkRegistrationDocumentButton.Click += (_, _) => RelinkCompanyDocument(
            ProjectDocumentCategories.CompanyRegistrationCertificate);
        companyRemoveRegistrationDocumentButton.Click += (_, _) => RemoveCompanyDocument(
            ProjectDocumentCategories.CompanyRegistrationCertificate);
        return BuildDocumentCollectionEditor(
            companyRegistrationDocumentsList,
            companyAddRegistrationDocumentButton,
            companyRelinkRegistrationDocumentButton,
            companyRemoveRegistrationDocumentButton,
            "Байгууллагын гэрчилгээний бүх хуудсыг зураг эсвэл нэг/олон PDF-ээр оруулна.");
    }

    private UIElement BuildCompanyLicenseDocumentEditor()
    {
        ConfigureDocumentList(companyLicenseDocumentsList);
        companyLicenseDocumentsList.SelectionChanged += (_, _) =>
        {
            companyRelinkLicenseDocumentButton.IsEnabled =
                companyEditorMode != CompanyEditorMode.View &&
                !companySaveInProgress &&
                selectedCompanyEntry?.CanManage == true &&
                companyLicenseDocumentsList.SelectedItem is not null;
            companyRemoveLicenseDocumentButton.IsEnabled =
                companyEditorMode != CompanyEditorMode.View &&
                !companySaveInProgress &&
                selectedCompanyEntry?.CanManage == true &&
                companyLicenseDocumentsList.SelectedItem is not null;
        };
        companyAddLicenseDocumentButton.Click += (_, _) => AddCompanyDocuments(
            ProjectDocumentCategories.CompanyDesignLicense);
        companyRelinkLicenseDocumentButton.Click += (_, _) => RelinkCompanyDocument(
            ProjectDocumentCategories.CompanyDesignLicense);
        companyRemoveLicenseDocumentButton.Click += (_, _) => RemoveCompanyDocument(
            ProjectDocumentCategories.CompanyDesignLicense);
        return BuildDocumentCollectionEditor(
            companyLicenseDocumentsList,
            companyAddLicenseDocumentButton,
            companyRelinkLicenseDocumentButton,
            companyRemoveLicenseDocumentButton,
            "Тусгай зөвшөөрлийн бүх хуудсыг гэрчилгээтэй холилгүй тусдаа хадгална.");
    }

    private static UIElement BuildDocumentCollectionEditor(
        ListView list,
        Button addButton,
        Button relinkButton,
        Button removeButton,
        string hint)
    {
        var panel = new StackPanel { MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left };
        panel.Children.Add(list);
        var actions = new WrapPanel { Margin = new Thickness(0, 7, 0, 2) };
        actions.Children.Add(addButton);
        actions.Children.Add(relinkButton);
        actions.Children.Add(removeButton);
        panel.Children.Add(actions);
        panel.Children.Add(StudioWidgets.CreateHint(hint));
        return panel;
    }

    private static void ConfigureDocumentList(ListView list)
    {
        var view = new GridView();
        view.Columns.Add(new GridViewColumn
        {
            Header = "Файл",
            Width = 360,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(DocumentAssetRow.FileName)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Төрөл",
            Width = 88,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(DocumentAssetRow.Type)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Хуудас",
            Width = 72,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(DocumentAssetRow.Pages)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Хэмжээ",
            Width = 90,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(DocumentAssetRow.Size)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Төлөв",
            Width = 128,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(DocumentAssetRow.Status)),
        });
        list.View = view;
    }

    private void StartAtdDocumentEdit()
    {
        atdDocumentDrafts = ApprovedAtdDocuments(state.Project.Foundation.PlanningTask.Documents)
            .Select(document => document.Clone())
            .ToList();
        RefreshAtdDocumentList();
    }

    private void BindAtdDocumentsFromProject()
    {
        if (!state.HasOpenProject)
        {
            atdDocumentDrafts = [];
        }
        else if (!foundationEditMode)
        {
            atdDocumentDrafts = ApprovedAtdDocuments(state.Project.Foundation.PlanningTask.Documents)
                .Select(document => document.Clone())
                .ToList();
        }
        RefreshAtdDocumentList();
    }

    private void AddAtdDocuments()
    {
        if (!foundationEditMode || foundationSaveInProgress || state.ProjectPath is null)
        {
            SetStatus("АТД-ийн хуулбар нэмэхийн тулд эхлээд Засварлах дарна уу.");
            return;
        }

        int previousCount = atdDocumentDrafts.Count;
        foreach (string sourcePath in ChooseDocumentFiles("АТД-ийн батлагдсан хуулбар сонгох"))
        {
            try
            {
                ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
                string relativePath = ProjectDocumentFileStore.StoreInsideProject(
                    state.ProjectPath,
                    ProjectDocumentCategories.ApprovedPlanningTask,
                    sourcePath);
                ProjectFileReference document = CreateDocumentReference(
                    sourcePath,
                    relativePath,
                    ProjectDocumentCategories.ApprovedPlanningTask,
                    "Батлагдсан архитектур төлөвлөлтийн даалгавар",
                    inspection);
                document.CloudOwnerEmail = CurrentCloudOwnerEmail();
                document.CloudContributionId = Guid.NewGuid().ToString("N");
                AddDocumentIfMissing(atdDocumentDrafts, document);
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                SetStatus($"АТД-ийн хуулбар нэмсэнгүй: {exception.Message}");
            }
        }
        RefreshAtdDocumentList();
        RefreshFoundationEditUi();
        if (atdDocumentDrafts.Count > previousCount)
            SetStatus("АТД-ийн хуулбар сонгогдлоо. Төслийн мэдээллийг Хадгалах дарна уу.");
    }

    private void RemoveAtdDocument()
    {
        if (!foundationEditMode || foundationSaveInProgress ||
            atdDocumentsList.SelectedItem is not DocumentAssetRow selected ||
            !CanEditAtdDocument(selected.Document))
            return;
        atdDocumentDrafts.RemoveAll(document => document.Id.Equals(selected.Document.Id, StringComparison.OrdinalIgnoreCase));
        RefreshAtdDocumentList();
        SetStatus("АТД-ийн хуулбарыг жагсаалтаас хаслаа. Өөрчлөлтийг Хадгалах дарна уу.");
    }

    private void RelinkAtdDocument()
    {
        if (!foundationEditMode || foundationSaveInProgress || state.ProjectPath is null ||
            atdDocumentsList.SelectedItem is not DocumentAssetRow selected ||
            !CanEditAtdDocument(selected.Document))
        {
            return;
        }

        string? sourcePath = ChooseSingleDocumentFile("АТД-ийн шинэ эх файлыг заах");
        if (sourcePath is null)
            return;
        try
        {
            ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
            string relativePath = ProjectDocumentFileStore.StoreInsideProject(
                state.ProjectPath,
                ProjectDocumentCategories.ApprovedPlanningTask,
                sourcePath);
            ApplyDocumentRevision(selected.Document, CreateDocumentReference(
                sourcePath,
                relativePath,
                ProjectDocumentCategories.ApprovedPlanningTask,
                selected.Document.Title,
                inspection));
            RefreshAtdDocumentList();
            RefreshFoundationEditUi();
            SetStatus("АТД-ийн эх файл дахин холбогдлоо. Төслийн мэдээллийг Хадгалах дарна уу.");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SetStatus($"АТД-ийн эх файл холбогдсонгүй: {exception.Message}");
        }
    }

    private void ApplyAtdDocumentDrafts() => ApplyAtdDocumentDrafts(atdDocumentDrafts);

    private void ApplyAtdDocumentDrafts(IEnumerable<ProjectFileReference> drafts)
    {
        List<ProjectFileReference> materialized = drafts.Select(document => document.Clone()).ToList();
        List<ProjectFileReference> documents = state.Project.Foundation.PlanningTask.Documents;
        bool changed = !DocumentListsEqual(
            ApprovedAtdDocuments(documents),
            materialized);
        documents.RemoveAll(document => document.Category.Equals(
            ProjectDocumentCategories.ApprovedPlanningTask,
            StringComparison.OrdinalIgnoreCase));
        documents.AddRange(materialized);
        if (changed)
        {
            state.Project.Foundation.PlanningTask.DocumentCloudSyncStatus =
                ProjectDocumentCloudSyncStatuses.PendingUpload;
        }
    }

    private bool AtdDocumentDraftsDifferFromProject() => !DocumentListsEqual(
        ApprovedAtdDocuments(state.Project.Foundation.PlanningTask.Documents),
        atdDocumentDrafts);

    private bool AtdDocumentDraftsDifferFromProject(IReadOnlyList<ProjectFileReference> drafts) => !DocumentListsEqual(
        ApprovedAtdDocuments(state.Project.Foundation.PlanningTask.Documents),
        drafts);

    private void RefreshAtdDocumentList()
    {
        atdDocumentsList.ItemsSource = atdDocumentDrafts.Select(document => new DocumentAssetRow(document)).ToList();
        bool canEditSelected = atdDocumentsList.SelectedItem is DocumentAssetRow selected &&
            CanEditAtdDocument(selected.Document);
        atdRelinkDocumentButton.IsEnabled = foundationEditMode &&
            !foundationSaveInProgress &&
            canEditSelected;
        atdRemoveDocumentButton.IsEnabled = foundationEditMode &&
            !foundationSaveInProgress &&
            canEditSelected;
    }

    private void BindCompanyDocumentDrafts(CompanyProfile profile)
    {
        companyRegistrationDocumentDrafts = profile.RegistrationCertificateDocuments
            .Select(document => document.Clone())
            .ToList();
        companyLicenseDocumentDrafts = profile.DesignLicenseDocuments
            .Select(document => document.Clone())
            .ToList();
        RefreshCompanyDocumentLists();
    }

    private void AddCompanyDocuments(string category)
    {
        if (companyEditorMode == CompanyEditorMode.View ||
            companySaveInProgress ||
            selectedCompanyEntry is null ||
            !selectedCompanyEntry.CanManage)
        {
            return;
        }
        List<ProjectFileReference> target = category == ProjectDocumentCategories.CompanyRegistrationCertificate
            ? companyRegistrationDocumentDrafts
            : companyLicenseDocumentDrafts;
        string title = category == ProjectDocumentCategories.CompanyRegistrationCertificate
            ? "Байгууллагын гэрчилгээ"
            : "Тусгай зөвшөөрөл";
        CompanyLibraryStore store = EnsureCompanyLibraryStore();
        int previousCount = target.Count;
        foreach (string sourcePath in ChooseDocumentFiles(title + " сонгох"))
        {
            try
            {
                ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
                string storedPath = store.StoreDocument(
                    selectedCompanyEntry.Profile.OrganizationId,
                    category,
                    sourcePath);
                AddDocumentIfMissing(target, CreateDocumentReference(
                    sourcePath,
                    storedPath,
                    category,
                    title,
                    inspection));
                companyDocumentsChanged = true;
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                SetStatus($"{title} нэмсэнгүй: {exception.Message}");
            }
        }
        RefreshCompanyDocumentLists();
        if (target.Count > previousCount)
            SetStatus($"{title} сонгогдлоо. Компанийн мэдээллийг Хадгалах дарна уу.");
    }

    private void RemoveCompanyDocument(string category)
    {
        if (companyEditorMode == CompanyEditorMode.View ||
            companySaveInProgress ||
            selectedCompanyEntry is null ||
            !selectedCompanyEntry.CanManage)
        {
            return;
        }
        ListView list = category == ProjectDocumentCategories.CompanyRegistrationCertificate
            ? companyRegistrationDocumentsList
            : companyLicenseDocumentsList;
        List<ProjectFileReference> target = category == ProjectDocumentCategories.CompanyRegistrationCertificate
            ? companyRegistrationDocumentDrafts
            : companyLicenseDocumentDrafts;
        if (list.SelectedItem is not DocumentAssetRow selected)
            return;
        target.RemoveAll(document => document.Id.Equals(selected.Document.Id, StringComparison.OrdinalIgnoreCase));
        companyDocumentsChanged = true;
        RefreshCompanyDocumentLists();
        SetStatus($"{(category == ProjectDocumentCategories.CompanyRegistrationCertificate ? "Гэрчилгээ" : "Тусгай зөвшөөрөл")}-ний хуулбарыг жагсаалтаас хаслаа. Хадгалах дарна уу.");
    }

    private void RelinkCompanyDocument(string category)
    {
        if (companyEditorMode == CompanyEditorMode.View ||
            companySaveInProgress ||
            selectedCompanyEntry is null ||
            !selectedCompanyEntry.CanManage)
        {
            return;
        }
        ListView list = category == ProjectDocumentCategories.CompanyRegistrationCertificate
            ? companyRegistrationDocumentsList
            : companyLicenseDocumentsList;
        if (list.SelectedItem is not DocumentAssetRow selected)
            return;

        string title = category == ProjectDocumentCategories.CompanyRegistrationCertificate
            ? "Байгууллагын гэрчилгээ"
            : "Тусгай зөвшөөрөл";
        string? sourcePath = ChooseSingleDocumentFile(title + "-ийн шинэ эх файлыг заах");
        if (sourcePath is null)
            return;
        try
        {
            ProjectDocumentAssetInspection inspection = ProjectDocumentAssetInspector.Inspect(sourcePath);
            string storedPath = EnsureCompanyLibraryStore().StoreDocument(
                selectedCompanyEntry.Profile.OrganizationId,
                category,
                sourcePath);
            ApplyDocumentRevision(selected.Document, CreateDocumentReference(
                sourcePath,
                storedPath,
                category,
                selected.Document.Title,
                inspection));
            companyDocumentsChanged = true;
            RefreshCompanyDocumentLists();
            SetStatus($"{title}-ийн эх файл дахин холбогдлоо. Компанийн мэдээллийг Хадгалах дарна уу.");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            SetStatus($"{title}-ийн эх файл холбогдсонгүй: {exception.Message}");
        }
    }

    private void ApplyCompanyDocumentDrafts(CompanyProfile profile)
    {
        profile.RegistrationCertificateDocuments = companyRegistrationDocumentDrafts
            .Select(document => document.Clone())
            .ToList();
        profile.DesignLicenseDocuments = companyLicenseDocumentDrafts
            .Select(document => document.Clone())
            .ToList();
    }

    private void RefreshCompanyDocumentLists()
    {
        companyRegistrationDocumentsList.ItemsSource = companyRegistrationDocumentDrafts
            .Select(document => new DocumentAssetRow(document))
            .ToList();
        companyLicenseDocumentsList.ItemsSource = companyLicenseDocumentDrafts
            .Select(document => new DocumentAssetRow(document))
            .ToList();
        bool enabled = companyEditorMode != CompanyEditorMode.View &&
            !companySaveInProgress &&
            selectedCompanyEntry?.CanManage == true;
        companyAddRegistrationDocumentButton.IsEnabled = enabled;
        companyAddLicenseDocumentButton.IsEnabled = enabled;
        companyRelinkRegistrationDocumentButton.IsEnabled = enabled &&
            companyRegistrationDocumentsList.SelectedItem is not null;
        companyRelinkLicenseDocumentButton.IsEnabled = enabled &&
            companyLicenseDocumentsList.SelectedItem is not null;
        companyRemoveRegistrationDocumentButton.IsEnabled = enabled &&
            companyRegistrationDocumentsList.SelectedItem is not null;
        companyRemoveLicenseDocumentButton.IsEnabled = enabled &&
            companyLicenseDocumentsList.SelectedItem is not null;
    }

    private static IReadOnlyList<string> ChooseDocumentFiles(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "PDF болон зураг|*.pdf;*.png;*.jpg;*.jpeg|PDF|*.pdf|Зураг|*.png;*.jpg;*.jpeg",
            Multiselect = true,
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : [];
    }

    private static string? ChooseSingleDocumentFile(string title)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = "PDF болон зураг|*.pdf;*.png;*.jpg;*.jpeg|PDF|*.pdf|Зураг|*.png;*.jpg;*.jpeg",
            Multiselect = false,
            CheckFileExists = true,
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static ProjectFileReference CreateDocumentReference(
        string originalPath,
        string storedPath,
        string category,
        string title,
        ProjectDocumentAssetInspection inspection) => new()
        {
            Category = category,
            Title = title,
            RelativePath = storedPath,
            OriginalFileName = Path.GetFileName(originalPath),
            LinkedSourcePath = Path.GetFullPath(originalPath),
            LinkedSourceLastWriteTimeUtc = new DateTimeOffset(
                File.GetLastWriteTimeUtc(originalPath),
                TimeSpan.Zero),
            IsAvailable = true,
            ContentType = inspection.ContentType,
            SizeBytes = inspection.SizeBytes,
            PageCount = inspection.PageCount,
            Sha256 = inspection.Sha256,
            CloudSyncStatus = ProjectDocumentCloudSyncStatuses.PendingUpload,
            AddedAtUtc = DateTimeOffset.UtcNow,
        };

    private static void AddDocumentIfMissing(
        ICollection<ProjectFileReference> documents,
        ProjectFileReference candidate)
    {
        ProjectFileReference? linked = documents.FirstOrDefault(document =>
            SameDocumentOwner(document, candidate) &&
            PathsEqual(document.LinkedSourcePath, candidate.LinkedSourcePath));
        if (linked is not null)
        {
            ApplyDocumentRevision(linked, candidate);
            return;
        }
        ProjectFileReference? sameContent = documents.FirstOrDefault(document =>
            SameDocumentOwner(document, candidate) &&
            document.Sha256.Equals(candidate.Sha256, StringComparison.OrdinalIgnoreCase));
        if (sameContent is not null)
        {
            if (string.IsNullOrWhiteSpace(sameContent.LinkedSourcePath))
            {
                sameContent.LinkedSourcePath = candidate.LinkedSourcePath;
                sameContent.LinkedSourceLastWriteTimeUtc = candidate.LinkedSourceLastWriteTimeUtc;
                sameContent.OriginalFileName = candidate.OriginalFileName;
                sameContent.RelativePath = candidate.RelativePath;
                sameContent.IsAvailable = true;
            }
            return;
        }
        documents.Add(candidate);
    }

    private static void ApplyDocumentRevision(
        ProjectFileReference target,
        ProjectFileReference revision)
    {
        bool contentChanged = !target.Sha256.Equals(
            revision.Sha256,
            StringComparison.OrdinalIgnoreCase);
        target.Category = revision.Category;
        target.Title = revision.Title;
        target.RelativePath = revision.RelativePath;
        target.OriginalFileName = revision.OriginalFileName;
        target.LinkedSourcePath = revision.LinkedSourcePath;
        target.LinkedSourceLastWriteTimeUtc = revision.LinkedSourceLastWriteTimeUtc;
        target.IsAvailable = true;
        target.ContentType = revision.ContentType;
        target.SizeBytes = revision.SizeBytes;
        target.PageCount = revision.PageCount;
        target.Sha256 = revision.Sha256;
        target.ServerFileId = "";
        target.ServerFileRevisionId = "";
        target.CloudSyncStatus = ProjectDocumentCloudSyncStatuses.PendingUpload;
        target.Version = contentChanged ? Math.Max(1, target.Version) + 1 : Math.Max(1, target.Version);
    }

    private static bool PathsEqual(string? left, string? right)
    {
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            return false;
        try
        {
            return Path.GetFullPath(left).Equals(
                Path.GetFullPath(right),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or
                ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static IEnumerable<ProjectFileReference> ApprovedAtdDocuments(
        IEnumerable<ProjectFileReference> documents) => documents.Where(document =>
            document.Category.Equals(ProjectDocumentCategories.ApprovedPlanningTask, StringComparison.OrdinalIgnoreCase));

    private static bool DocumentListsEqual(
        IEnumerable<ProjectFileReference> left,
        IEnumerable<ProjectFileReference> right)
    {
        string[] leftKeys = left.Select(DocumentIdentity).Order(StringComparer.Ordinal).ToArray();
        string[] rightKeys = right.Select(DocumentIdentity).Order(StringComparer.Ordinal).ToArray();
        return leftKeys.SequenceEqual(rightKeys, StringComparer.Ordinal);
    }

    private static string DocumentIdentity(ProjectFileReference document) =>
        $"{document.Category}|{document.Sha256}|{document.PageCount}|{document.RelativePath}|" +
        $"{document.LinkedSourcePath}|{document.IsAvailable}|{document.Version}|" +
        $"{document.ServerFileRevisionId}|{document.CloudSyncStatus}|{document.CloudOwnerEmail}|" +
        $"{document.CloudContributionId}|{document.IsCloudPlaceholder}";

    private bool CanEditAtdDocument(ProjectFileReference document)
    {
        if (document.IsCloudPlaceholder)
            return false;
        string owner = (document.CloudOwnerEmail ?? "").Trim();
        return string.IsNullOrWhiteSpace(owner) ||
            owner.Equals(CurrentCloudOwnerEmail(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool SameDocumentOwner(ProjectFileReference left, ProjectFileReference right) =>
        (left.CloudOwnerEmail ?? "").Trim().Equals(
            (right.CloudOwnerEmail ?? "").Trim(),
            StringComparison.OrdinalIgnoreCase);

    private sealed record DocumentAssetRow(ProjectFileReference Document)
    {
        public string FileName => ProjectAssetDisplayName.ForDocument(Document);
        public string Type => Document.ContentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase)
            ? "PDF"
            : "Зураг";
        public string Pages => Math.Max(1, Document.PageCount).ToString();
        public string Status => !Document.IsAvailable
            ? "Эх файл олдсонгүй"
            : Document.IsCloudPlaceholder || !string.IsNullOrWhiteSpace(Document.CloudOwnerEmail)
                ? Document.CloudSyncStatus.Equals(ProjectDocumentCloudSyncStatuses.PendingUpload, StringComparison.OrdinalIgnoreCase)
                    ? "Cloud sync хүлээгдэж байна"
                    : "Cloud · " + (string.IsNullOrWhiteSpace(Document.CloudOwnerEmail)
                        ? "эх үүсвэр"
                        : Document.CloudOwnerEmail)
            : Document.CloudSyncStatus switch
            {
                ProjectDocumentCloudSyncStatuses.PendingUpload => "Cloud sync хүлээгдэж байна",
                ProjectDocumentCloudSyncStatuses.Conflict => "Cloud зөрчил",
                ProjectDocumentCloudSyncStatuses.Synced => "Cloud · шинэчлэгдсэн",
                _ => "Бэлэн",
            };
        public string Size => Document.SizeBytes <= 0
            ? "-"
            : Document.SizeBytes >= 1024 * 1024
                ? $"{Document.SizeBytes / 1024d / 1024d:0.0} MB"
                : $"{Document.SizeBytes / 1024d:0} KB";
    }
}
