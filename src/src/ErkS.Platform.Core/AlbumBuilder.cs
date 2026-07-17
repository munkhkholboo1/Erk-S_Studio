namespace ErkS.Platform.Core;

/// <summary>Everything a PDF writer needs to compose one resolved album.</summary>
public sealed class AlbumBuildRequest
{
    public required AlbumProject Project { get; init; }
    public required IReadOnlyList<AlbumBuildSection> Sections { get; init; }
}

public sealed class AlbumBuildSection
{
    public required string Title { get; init; }
    public required IReadOnlyList<AlbumBuildPage> Pages { get; init; }
    public IReadOnlyList<SheetRecord> Sheets => Pages.Select(page => page.Sheet).ToList();
}

public sealed class AlbumBuildPage
{
    public required SheetRecord Sheet { get; init; }
    public required AlbumPageDefinition Definition { get; init; }
    public required PageFormatDefinition Format { get; init; }
    public string StudioNumber { get; init; } = "";

    public string Number => !string.IsNullOrWhiteSpace(Definition.NumberOverride)
        ? Definition.NumberOverride
        : !string.IsNullOrWhiteSpace(StudioNumber)
            ? StudioNumber
            : Sheet.Entry.Number;

    public string Title => string.IsNullOrWhiteSpace(Definition.TitleOverride)
        ? Sheet.Entry.Name
        : Definition.TitleOverride;
}

public sealed class AlbumBuildResult
{
    public required string OutputPath { get; init; }
    public required int SheetCount { get; init; }
    public required int PageCount { get; init; }
    public List<string> Warnings { get; } = [];
}

public interface IAlbumPdfWriter
{
    AlbumBuildResult Compose(AlbumBuildRequest request, string outputPath);
}

/// <summary>Resolves the album page model against the live sheet library.</summary>
public sealed class AlbumBuilder
{
    private readonly IAlbumPdfWriter writer;

    public AlbumBuilder(IAlbumPdfWriter writer)
    {
        this.writer = writer;
    }

    public AlbumBuildResult Build(AlbumProject project, SheetLibrary library, string outputPath)
    {
        return writer.Compose(CreateRequest(project, library), outputPath);
    }

    public static AlbumBuildRequest CreateRequest(AlbumProject project, SheetLibrary library)
    {
        return project.Album.Pages.Count > 0
            ? CreateConfiguredRequest(project, library)
            : CreateLegacyRequest(project, library);
    }

    private static AlbumBuildRequest CreateConfiguredRequest(AlbumProject project, SheetLibrary library)
    {
        if (string.Equals(
                project.Album.TemplateId,
                BuildingArchitectureConceptAlbumTemplate.TemplateId,
                StringComparison.OrdinalIgnoreCase))
        {
            return CreateConceptConfiguredRequest(project, library);
        }

        var sections = new List<AlbumBuildSection>();
        var definedSectionIds = new HashSet<Guid>();

        foreach (var section in project.Album.Sections)
        {
            definedSectionIds.Add(section.Id);
            var pages = ResolvePages(
                BuildingArchitectureConceptAlbumTemplate.OrderSourcePages(
                    project.Album,
                    project.Album.Pages.Where(page => page.SectionId == section.Id)),
                library);
            if (pages.Count > 0)
            {
                sections.Add(new AlbumBuildSection { Title = section.Title, Pages = pages });
            }
        }

        var unsectioned = ResolvePages(
            BuildingArchitectureConceptAlbumTemplate.OrderSourcePages(
                project.Album,
                project.Album.Pages.Where(page =>
                    !page.SectionId.HasValue || !definedSectionIds.Contains(page.SectionId.Value))),
            library);
        if (unsectioned.Count > 0)
        {
            sections.Add(new AlbumBuildSection
            {
                Title = sections.Count == 0 ? "" : "Бусад",
                Pages = unsectioned,
            });
        }

        return new AlbumBuildRequest { Project = project, Sections = sections };
    }

    private static AlbumBuildRequest CreateConceptConfiguredRequest(
        AlbumProject project,
        SheetLibrary library)
    {
        var sequence = BuildingArchitectureConceptAlbumSequencer.Create(
            project.Album,
            project.Album.Pages,
            library,
            project.DesignSources);
        var sectionRuns = new List<ConceptSectionRun>();

        foreach (var item in sequence)
        {
            if (item.Sheet is null)
            {
                continue;
            }

            if (sectionRuns.Count == 0 || !string.Equals(
                    sectionRuns[^1].Key,
                    item.SectionKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                sectionRuns.Add(new ConceptSectionRun(item.SectionKey, item.SectionTitle));
            }

            sectionRuns[^1].Pages.Add(CreateConceptBuildPage(item));
        }

        return new AlbumBuildRequest
        {
            Project = project,
            Sections = sectionRuns
                .Select(run => new AlbumBuildSection { Title = run.Title, Pages = run.Pages })
                .ToList(),
        };
    }

    private static AlbumBuildPage CreateConceptBuildPage(ConceptAlbumSourcePage item)
    {
        var sheet = item.Sheet ?? throw new InvalidOperationException("Concept album source page is unresolved.");
        var definition = item.Page;
        var configured = PageFormatCatalog.Resolve(definition);
        if (configured.Kind == PageFormatKind.SourceAsIs &&
            PageFormatResolver.TryResolveSourceFormat(sheet.Entry, out var sourceFormat))
        {
            // During migration Revit/AutoCAD may still send the complete sheet.
            // Keep it full-size and let Studio cover only its header/corner zones.
            definition = new AlbumPageDefinition
            {
                Id = item.Page.Id,
                SheetKey = item.Page.SheetKey,
                TemplateSlotId = item.Page.TemplateSlotId,
                SectionId = item.Page.SectionId,
                PageFormatId = item.Page.PageFormatId,
                PageFormatSnapshot = item.Page.PageFormatSnapshot,
                FollowSourceFormat = item.Page.FollowSourceFormat,
                PlacementMode = PagePlacementMode.FullPage,
                NumberOverride = item.Page.NumberOverride,
                TitleOverride = item.Page.TitleOverride,
            };
            configured = sourceFormat;
        }

        return new AlbumBuildPage
        {
            Sheet = sheet,
            Definition = definition,
            Format = configured,
            StudioNumber = item.AutomaticNumber,
        };
    }

    private static List<AlbumBuildPage> ResolvePages(
        IEnumerable<AlbumPageDefinition> definitions,
        SheetLibrary library)
    {
        var result = new List<AlbumBuildPage>();
        foreach (var definition in definitions)
        {
            var sheet = library.Find(definition.SheetKey);
            if (sheet is null)
            {
                continue;
            }

            result.Add(new AlbumBuildPage
            {
                Sheet = sheet,
                Definition = definition,
                Format = PageFormatCatalog.Resolve(definition),
            });
        }

        return result;
    }

    private static AlbumBuildRequest CreateLegacyRequest(AlbumProject project, SheetLibrary library)
    {
        var sections = new List<AlbumBuildSection>();
        var usedKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var section in project.Album.Sections)
        {
            var pages = new List<AlbumBuildPage>();
            foreach (var key in section.SheetKeys)
            {
                var record = library.Find(key);
                if (record is null)
                {
                    continue;
                }

                pages.Add(CreateLegacyPage(record, section.Id));
                usedKeys.Add(key);
            }

            sections.Add(new AlbumBuildSection { Title = section.Title, Pages = pages });
        }

        var unassigned = library.Snapshot()
            .Where(record => !usedKeys.Contains(record.Key))
            .Select(record => CreateLegacyPage(record, null))
            .ToList();
        if (unassigned.Count > 0)
        {
            sections.Add(new AlbumBuildSection
            {
                Title = sections.Count == 0 ? "" : "Бусад",
                Pages = unassigned,
            });
        }

        return new AlbumBuildRequest { Project = project, Sections = sections };
    }

    private static AlbumBuildPage CreateLegacyPage(SheetRecord sheet, Guid? sectionId)
    {
        var definition = new AlbumPageDefinition
        {
            SheetKey = sheet.Key,
            SectionId = sectionId,
            PageFormatId = PageFormatCatalog.SourceAsIsId,
            PlacementMode = PagePlacementMode.FullPage,
        };
        return new AlbumBuildPage
        {
            Sheet = sheet,
            Definition = definition,
            Format = PageFormatCatalog.Resolve(definition.PageFormatId),
        };
    }

    private sealed class ConceptSectionRun(string key, string title)
    {
        public string Key { get; } = key;
        public string Title { get; } = title;
        public List<AlbumBuildPage> Pages { get; } = [];
    }
}
