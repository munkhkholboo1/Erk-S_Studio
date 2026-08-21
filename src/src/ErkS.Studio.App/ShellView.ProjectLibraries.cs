using System.IO;
using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

/// <summary>
/// Судалгаа and Бичиг баримт: the studies a general plan is drawn from and the
/// paperwork filed with it. They are project inputs, so they are registered and
/// stored like the foundation documents rather than composed into the album.
/// </summary>
internal sealed partial class ShellView
{
    private readonly ListView researchDocumentsList = new() { MinHeight = 220 };
    private readonly Button researchAddButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Файл нэмэх",
        "Судалгааны PDF эсвэл зурган файл сонгох");
    private readonly Button researchRelinkButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Эх файлыг дахин заах",
        "Сонгосон судалгааны source link-ийг ID-г нь өөрчлөхгүйгээр шинэчлэх");
    private readonly Button researchRemoveButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Хасах",
        "Сонгосон судалгааг төслөөс хасах");

    private readonly ListView recordDocumentsList = new() { MinHeight = 220 };
    private readonly Button recordAddButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Файл нэмэх",
        "Бичиг баримтын PDF эсвэл зурган файл сонгох");
    private readonly Button recordRelinkButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Эх файлыг дахин заах",
        "Сонгосон бичиг баримтын source link-ийг ID-г нь өөрчлөхгүйгээр шинэчлэх");
    private readonly Button recordRemoveButton = StudioWidgets.CreateGlyphTextButton(
        "",
        "Хасах",
        "Сонгосон бичиг баримтыг төслөөс хасах");

    /// <summary>
    /// Судалгаа and Бичиг баримт belong to a general plan - a partial or a
    /// development one - and to no other stage.
    /// </summary>
    private bool ProjectOwnsGeneralPlanLibraries() =>
        state.HasOpenProject &&
        ErkS.Platform.Core.ProjectTypes.UrbanPlanning.UrbanPlanningAlbumTemplate.Supports(
            state.Project.Identity.ProjectType,
            state.Project.Identity.StageCode);

    private UIElement BuildResearchPage()
    {
        researchAddButton.Click += (_, _) => AddProjectLibraryDocuments(
            ProjectDocumentCategories.Research);
        researchRelinkButton.Click += (_, _) => RelinkProjectLibraryDocument(
            ProjectDocumentCategories.Research);
        researchRemoveButton.Click += (_, _) => RemoveProjectLibraryDocument(
            ProjectDocumentCategories.Research);
        return BuildProjectLibraryPage(
            "Судалгаа",
            "Ерөнхий төлөвлөгөөний үндэслэл болсон судалгаанууд. Файлыг төслийн дотор хуулбарлан хадгална.",
            researchDocumentsList,
            researchAddButton,
            researchRelinkButton,
            researchRemoveButton);
    }

    private UIElement BuildRecordsPage()
    {
        recordAddButton.Click += (_, _) => AddProjectLibraryDocuments(
            ProjectDocumentCategories.Record);
        recordRelinkButton.Click += (_, _) => RelinkProjectLibraryDocument(
            ProjectDocumentCategories.Record);
        recordRemoveButton.Click += (_, _) => RemoveProjectLibraryDocument(
            ProjectDocumentCategories.Record);
        return BuildProjectLibraryPage(
            "Бичиг баримт",
            "Төслийн хамт хөтлөгдөх албан бичиг, шийдвэр, зөвшөөрлүүд. Файлыг төслийн дотор хуулбарлан хадгална.",
            recordDocumentsList,
            recordAddButton,
            recordRelinkButton,
            recordRemoveButton);
    }

    private UIElement BuildProjectLibraryPage(
        string title,
        string hint,
        ListView list,
        Button addButton,
        Button relinkButton,
        Button removeButton)
    {
        ConfigureDocumentList(list);
        var panel = new StackPanel
        {
            Margin = new Thickness(18),
            MaxWidth = 980,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.Children.Add(StudioWidgets.CreateTitle(title));
        panel.Children.Add(BuildDocumentCollectionEditor(
            list,
            addButton,
            relinkButton,
            removeButton,
            hint));
        return StudioWidgets.CreateScrollHost(panel);
    }

    private List<ProjectFileReference> ProjectLibrary(string category) =>
        IsResearchLibrary(category)
            ? state.Project.ResearchDocuments
            : state.Project.RecordDocuments;

    private ListView ProjectLibraryList(string category) =>
        IsResearchLibrary(category) ? researchDocumentsList : recordDocumentsList;

    private static bool IsResearchLibrary(string category) =>
        category.Equals(
            ProjectDocumentCategories.Research,
            StringComparison.OrdinalIgnoreCase);

    private static string ProjectLibraryTitle(string category) =>
        IsResearchLibrary(category) ? "Судалгаа" : "Бичиг баримт";

    private void AddProjectLibraryDocuments(string category)
    {
        if (!state.HasOpenProject || state.ProjectPath is null)
            return;

        string label = ProjectLibraryTitle(category);
        List<ProjectFileReference> documents = ProjectLibrary(category);
        int previousCount = documents.Count;
        foreach (string sourcePath in ChooseDocumentFiles($"{label} файл сонгох"))
        {
            try
            {
                ProjectDocumentAssetInspection inspection =
                    ProjectDocumentAssetInspector.Inspect(sourcePath);
                string relativePath = ProjectDocumentFileStore.StoreInsideProject(
                    state.ProjectPath,
                    category,
                    sourcePath);
                ProjectFileReference document = CreateDocumentReference(
                    sourcePath,
                    relativePath,
                    category,
                    Path.GetFileNameWithoutExtension(sourcePath),
                    inspection);
                StudioAuxiliarySourceLocalityPolicy.Bind(
                    state.Project,
                    document,
                    account.Current?.Email,
                    StudioDeviceIdentity.Fingerprint);
                AddDocumentIfMissing(documents, document);
            }
            catch (Exception exception) when (
                exception is IOException or InvalidDataException or
                    UnauthorizedAccessException or InvalidOperationException)
            {
                SetStatus($"{label} нэмсэнгүй: {exception.Message}");
            }
        }

        if (documents.Count == previousCount)
            return;

        state.SaveProject();
        RefreshProjectLibrary(category);
        SetStatus($"{label} бүртгэгдлээ.");
    }

    private void RemoveProjectLibraryDocument(string category)
    {
        if (!state.HasOpenProject ||
            ProjectLibraryList(category).SelectedItem is not DocumentAssetRow selected)
        {
            return;
        }

        ProjectLibrary(category).RemoveAll(document =>
            document.Id.Equals(selected.Document.Id, StringComparison.OrdinalIgnoreCase));
        state.SaveProject();
        RefreshProjectLibrary(category);
        SetStatus($"{ProjectLibraryTitle(category)}-аас хаслаа.");
    }

    private void RelinkProjectLibraryDocument(string category)
    {
        if (!state.HasOpenProject || state.ProjectPath is null ||
            ProjectLibraryList(category).SelectedItem is not DocumentAssetRow selected)
        {
            return;
        }

        string label = ProjectLibraryTitle(category);
        string? sourcePath = ChooseSingleDocumentFile($"{label}-ын эх файлыг дахин заах");
        if (sourcePath is null)
            return;

        try
        {
            ProjectDocumentAssetInspection inspection =
                ProjectDocumentAssetInspector.Inspect(sourcePath);
            string relativePath = ProjectDocumentFileStore.StoreInsideProject(
                state.ProjectPath,
                category,
                sourcePath);
            ApplyDocumentRevision(
                selected.Document,
                CreateDocumentReference(
                    sourcePath,
                    relativePath,
                    category,
                    selected.Document.Title,
                    inspection));
            state.SaveProject();
            RefreshProjectLibrary(category);
            SetStatus($"{label}-ын эх файл шинэчлэгдлээ.");
        }
        catch (Exception exception) when (
            exception is IOException or InvalidDataException or
                UnauthorizedAccessException or InvalidOperationException)
        {
            SetStatus($"{label}-ын эх файлыг заасангүй: {exception.Message}");
        }
    }

    private void RefreshProjectLibrary(string category)
    {
        ListView list = ProjectLibraryList(category);
        string? selectedId = (list.SelectedItem as DocumentAssetRow)?.Document.Id;
        list.ItemsSource = ProjectLibrary(category)
            .Select(document => new DocumentAssetRow(document))
            .ToList();
        if (selectedId is not null)
        {
            list.SelectedItem = list.Items
                .OfType<DocumentAssetRow>()
                .FirstOrDefault(row =>
                    row.Document.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase));
        }
    }

    private void RefreshProjectLibraries()
    {
        if (!state.HasOpenProject)
            return;
        RefreshProjectLibrary(ProjectDocumentCategories.Research);
        RefreshProjectLibrary(ProjectDocumentCategories.Record);
    }
}
