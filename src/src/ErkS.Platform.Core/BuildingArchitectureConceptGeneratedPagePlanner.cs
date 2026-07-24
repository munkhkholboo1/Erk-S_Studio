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
    public const int DocumentPagesPerAlbumPage = 2;
    public const string DesignOrganizationTitle = "ЗУРАГ ТӨСӨЛ БОЛОВСРУУЛСАН БАЙГУУЛЛАГА";
    public const string ApprovedPlanningTaskTitle = "БАТЛАГДСАН АРХИТЕКТУР ТӨЛӨВЛӨЛТИЙН ДААЛГАВАР";

    public static IReadOnlyList<ConceptGeneratedPagePlan> Create(AlbumProject project)
    {
        var drafts = new List<PageDraft>();
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
                        createPlaceholder: true);
                    AddDocumentBatches(
                        drafts,
                        component,
                        DesignOrganizationTitle,
                        "ТУСГАЙ ЗӨВШӨӨРӨЛ",
                        ConceptGeneratedDocumentKind.CompanyDesignLicense,
                        project.Company.DesignLicenseDocuments,
                        createPlaceholder: true);
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
                        createPlaceholder: true);
                    break;
                case AlbumGeneratedPageKind.SiteContext:
                    drafts.Add(PageDraft.Empty(component, component.Title));
                    break;
                default:
                    drafts.Add(PageDraft.Empty(component, component.Title));
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
        int pagesPerAlbumPage = DocumentPagesPerAlbumPage)
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
        int batchCount = Math.Max(createPlaceholder ? 1 : 0,
            (int)Math.Ceiling(pages.Count / (double)pagesPerAlbumPage));
        for (int batch = 0; batch < batchCount; batch++)
        {
            target.Add(new PageDraft
            {
                Component = component,
                Title = title,
                DocumentKind = kind,
                DocumentLabel = documentLabel,
                BatchNumber = batch + 1,
                BatchCount = batchCount,
                DocumentPages = pages
                    .Skip(batch * pagesPerAlbumPage)
                    .Take(pagesPerAlbumPage)
                    .ToList(),
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
