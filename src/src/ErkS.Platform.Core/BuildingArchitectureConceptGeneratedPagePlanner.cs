using System.Globalization;

namespace ErkS.Platform.Core;

public enum ConceptGeneratedDocumentKind
{
    None,
    CompanyRegistrationCertificate,
    CompanyDesignLicense,
    ApprovedPlanningTask,
}

public sealed class ConceptGeneratedDocumentPage
{
    public required ProjectFileReference Document { get; init; }
    public required int SourcePageNumber { get; init; }
}

public sealed class ConceptGeneratedPagePlan
{
    public required AlbumCompositionItem Component { get; init; }
    public required int OutputIndex { get; init; }
    public required string Number { get; init; }
    public required string Title { get; init; }
    public required ConceptGeneratedDocumentKind DocumentKind { get; init; }
    public required string DocumentLabel { get; init; }
    public required int BatchNumber { get; init; }
    public required int BatchCount { get; init; }
    public required IReadOnlyList<ConceptGeneratedDocumentPage> DocumentPages { get; init; }

    public string NavigationKey =>
        $"{Component.Id}:{DocumentKind}:{BatchNumber.ToString(CultureInfo.InvariantCulture)}";
}

/// <summary>
/// Expands the three logical Studio components into the physical PDF pages.
/// Registration and license documents never share a physical album page.
/// </summary>
public static class BuildingArchitectureConceptGeneratedPagePlanner
{
    /// <summary>
    /// How many faces of a scanned document share one album page.
    ///
    /// Read from the sheet the album is drawn on rather than fixed here: the
    /// client set it per format - four on A2, two on A3 - and the generated
    /// pages are A3 today. When they can be A2 this follows without a change.
    /// </summary>
    public static int DocumentPagesPerAlbumPage(AlbumProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        PageFormatDefinition? format = project.Album.GeneratedPageFormat;
        return format is not null && PageFormatCatalog.IsUsable(format)
            ? DocumentFaceDistribution.Capacity(format.WidthMm, format.HeightMm)
            : DocumentFaceDistribution.Capacity(
                BuildingArchitectureConceptPageLayout.PageWidthMm,
                BuildingArchitectureConceptPageLayout.PageHeightMm);
    }

    public const string DesignOrganizationTitle = "ЗУРАГ ТӨСӨЛ БОЛОВСРУУЛСАН БАЙГУУЛЛАГА";
    public const string ApprovedPlanningTaskTitle = "БАТЛАГДСАН АРХИТЕКТУР ТӨЛӨВЛӨЛТИЙН ДААЛГАВАР";

    public static IReadOnlyList<ConceptGeneratedPagePlan> Create(AlbumProject project)
    {
        var drafts = new List<PageDraft>();
        int facesPerPage = DocumentPagesPerAlbumPage(project);
        foreach (AlbumCompositionItem component in project.Album.Composition
                     .Where(item => item.Kind == AlbumCompositionKind.Generated)
                     .OrderBy(FixedGeneratedPageOrder)
                     .ThenBy(item => item.Order))
        {
            switch (component.GeneratedPageKind)
            {
                case AlbumGeneratedPageKind.Cover:
                    drafts.Add(PageDraft.Empty(component, component.Title));
                    break;
                case AlbumGeneratedPageKind.DesignOrganization:
                    AddDocumentBatches(
                        drafts,
                        component,
                        DesignOrganizationTitle,
                        "БАЙГУУЛЛАГЫН ГЭРЧИЛГЭЭ",
                        ConceptGeneratedDocumentKind.CompanyRegistrationCertificate,
                        project.Company.RegistrationCertificateDocuments,
                        createPlaceholder: true,
                        facesPerPage);
                    AddDocumentBatches(
                        drafts,
                        component,
                        DesignOrganizationTitle,
                        "ТУСГАЙ ЗӨВШӨӨРӨЛ",
                        ConceptGeneratedDocumentKind.CompanyDesignLicense,
                        project.Company.DesignLicenseDocuments,
                        createPlaceholder: true,
                        facesPerPage);
                    break;
                case AlbumGeneratedPageKind.PlanningTask:
                    List<ProjectFileReference> planningDocuments = project.PlanningTask.Documents
                        .Where(IsPlanningTaskDocument)
                        .ToList();
                    AddDocumentBatches(
                        drafts,
                        component,
                        ApprovedPlanningTaskTitle,
                        "БАТЛАГДСАН ХУУЛБАР",
                        ConceptGeneratedDocumentKind.ApprovedPlanningTask,
                        planningDocuments,
                        createPlaceholder: true,
                        facesPerPage);
                    break;
                case AlbumGeneratedPageKind.SiteContext:
                    drafts.Add(PageDraft.Empty(component, component.Title));
                    break;
                default:
                    // The drawing list is the one generated component whose
                    // length depends on the album's own contents, so it is the
                    // one that can need more than a page. Reserving them HERE is
                    // what keeps every later sheet's number right: this count
                    // feeds the sequencer, and the writer breaks by the same
                    // rule rather than by its own arithmetic.
                    AddDrawingListPages(drafts, component, project);
                    break;
            }
        }

        int width = Math.Max(2, Math.Max(0, drafts.Count - 1)
            .ToString(CultureInfo.InvariantCulture).Length);
        return drafts.Select((draft, index) => new ConceptGeneratedPagePlan
        {
            Component = draft.Component,
            OutputIndex = index,
            Number = index.ToString($"D{width}", CultureInfo.InvariantCulture),
            Title = draft.Title,
            DocumentKind = draft.DocumentKind,
            DocumentLabel = draft.DocumentLabel,
            BatchNumber = draft.BatchNumber,
            BatchCount = draft.BatchCount,
            DocumentPages = draft.DocumentPages,
        }).ToList();
    }

    private static int FixedGeneratedPageOrder(AlbumCompositionItem component) =>
        component.GeneratedPageKind switch
        {
            AlbumGeneratedPageKind.Cover => 0,
            AlbumGeneratedPageKind.DesignOrganization => 10,
            AlbumGeneratedPageKind.PlanningTask => 20,
            AlbumGeneratedPageKind.None => 25 + Math.Max(0, component.Order),
            AlbumGeneratedPageKind.SiteContext => 30,
            _ => 100 + Math.Max(0, component.Order),
        };

    private static void AddDocumentBatches(
        ICollection<PageDraft> target,
        AlbumCompositionItem component,
        string title,
        string documentLabel,
        ConceptGeneratedDocumentKind kind,
        IEnumerable<ProjectFileReference>? documents,
        bool createPlaceholder,
        int pagesPerAlbumPage)
    {
        if (pagesPerAlbumPage <= 0)
            throw new ArgumentOutOfRangeException(nameof(pagesPerAlbumPage));

        List<ConceptGeneratedDocumentPage> pages = (documents ?? [])
            .Where(document => document is not null && document.IsAvailable)
            .SelectMany(document => Enumerable.Range(1, Math.Max(1, document.PageCount))
                .Select(pageNumber => new ConceptGeneratedDocumentPage
                {
                    Document = document,
                    SourcePageNumber = pageNumber,
                }))
            .ToList();
        // Spread rather than packed: five faces make 3 and 2, not 4 and 1.
        IReadOnlyList<int> perPage = DocumentFaceDistribution.Distribute(
            pages.Count,
            pagesPerAlbumPage);
        int batchCount = Math.Max(createPlaceholder ? 1 : 0, perPage.Count);
        int taken = 0;
        for (int batch = 0; batch < batchCount; batch++)
        {
            int take = batch < perPage.Count ? perPage[batch] : 0;
            target.Add(new PageDraft
            {
                Component = component,
                Title = title,
                DocumentKind = kind,
                DocumentLabel = documentLabel,
                BatchNumber = batch + 1,
                BatchCount = batchCount,
                DocumentPages = pages.Skip(taken).Take(take).ToList(),
            });
            taken += take;
        }
    }

    private static void AddDrawingListPages(
        ICollection<PageDraft> target,
        AlbumCompositionItem component,
        AlbumProject project)
    {
        if (!DrawingListPagination.UsesWorkingDrawingFormat(project.Album))
        {
            // Every other family draws a single list page - the older A4 one,
            // which does its own paging inside one component.
            target.Add(PageDraft.Empty(component, component.Title));
            return;
        }

        int rowsPerPage = DrawingListPagination.RowsPerPage(
            WorkingDrawingPageLayout.Resolve(
                WorkingDrawingAlbumFormatFactory.Resolve(project.Album)));
        int pageCount = DrawingListPagination.PageCount(project.Album.Pages.Count, rowsPerPage);
        for (int index = 0; index < pageCount; index++)
        {
            target.Add(new PageDraft
            {
                Component = component,
                Title = component.Title,
                BatchNumber = index + 1,
                BatchCount = pageCount,
            });
        }
    }

    private static bool IsPlanningTaskDocument(ProjectFileReference document) =>
        document.Category.Equals(ProjectDocumentCategories.ApprovedPlanningTask, StringComparison.OrdinalIgnoreCase) ||
        (string.IsNullOrWhiteSpace(document.Category) &&
         (document.Title.Contains("АТД", StringComparison.OrdinalIgnoreCase) ||
          document.Title.Contains("төлөвлөлтийн даалгавар", StringComparison.OrdinalIgnoreCase)));

    private sealed class PageDraft
    {
        public required AlbumCompositionItem Component { get; init; }
        public required string Title { get; init; }
        public ConceptGeneratedDocumentKind DocumentKind { get; init; }
        public string DocumentLabel { get; init; } = "";
        public int BatchNumber { get; init; } = 1;
        public int BatchCount { get; init; } = 1;
        public IReadOnlyList<ConceptGeneratedDocumentPage> DocumentPages { get; init; } = [];

        public static PageDraft Empty(AlbumCompositionItem component, string title) => new()
        {
            Component = component,
            Title = title,
        };
    }
}
