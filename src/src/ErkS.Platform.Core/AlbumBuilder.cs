using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

public sealed class AlbumBuildException : Exception
{
    public AlbumBuildException(IEnumerable<string> issues, Exception? innerException = null)
        : base(CreateMessage(issues), innerException)
    {
        Issues = issues.Where(issue => !string.IsNullOrWhiteSpace(issue)).ToList();
    }

    public IReadOnlyList<string> Issues { get; }

    private static string CreateMessage(IEnumerable<string> issues)
    {
        var materialized = issues.Where(issue => !string.IsNullOrWhiteSpace(issue)).ToList();
        return materialized.Count == 0
            ? "Album build failed its integrity check."
            : "Album build rejected: " + string.Join(" | ", materialized);
    }
}

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
    public List<AlbumBuildComponent> Components { get; } = [];
}

public sealed class AlbumBuildComponent
{
    public required string Code { get; init; }
    public required string Label { get; init; }
    public required int Order { get; init; }
    public List<int> PageNumbers { get; init; } = [];
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
        var request = CreateRequest(project, library);
        VerifySourcePackages(request);

        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath) ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileNameWithoutExtension(fullOutputPath)}.{Guid.NewGuid():N}.tmp.pdf");

        try
        {
            var temporaryResult = writer.Compose(request, temporaryPath);
            if (!File.Exists(temporaryPath))
            {
                throw new AlbumBuildException(["PDF writer did not produce an output file."]);
            }

            File.Move(temporaryPath, fullOutputPath, overwrite: true);
            var result = new AlbumBuildResult
            {
                OutputPath = outputPath,
                SheetCount = temporaryResult.SheetCount,
                PageCount = temporaryResult.PageCount,
            };
            result.Warnings.AddRange(temporaryResult.Warnings);
            result.Components.AddRange(temporaryResult.Components.Select(component => new AlbumBuildComponent
            {
                Code = component.Code,
                Label = component.Label,
                Order = component.Order,
                PageNumbers = component.PageNumbers.ToList(),
            }));
            return result;
        }
        catch (AlbumBuildException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new AlbumBuildException([exception.Message], exception);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (IOException)
            {
                // A failed build must not be masked by best-effort temp cleanup.
            }
            catch (UnauthorizedAccessException)
            {
                // A failed build must not be masked by best-effort temp cleanup.
            }
        }
    }

    public static AlbumBuildRequest CreateRequest(AlbumProject project, SheetLibrary library)
    {
        return project.Album.Pages.Count > 0
            ? CreateConfiguredRequest(project, library)
            : CreateLegacyRequest(project, library);
    }

    private static AlbumBuildRequest CreateConfiguredRequest(AlbumProject project, SheetLibrary library)
    {
        RejectUnresolvedPages(project.Album.Pages, library);

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
        int generatedPageCount = BuildingArchitectureConceptGeneratedPagePlanner
            .Create(project)
            .Count;
        var sequence = BuildingArchitectureConceptAlbumSequencer.Create(
            project.Album,
            project.Album.Pages,
            library,
            project.DesignSources,
            generatedPageCount);
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
        var configured = PageFormatCatalog.ResolveForConceptPage(definition, sheet.Entry);
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
                ElevationDescriptionOverride = item.Page.ElevationDescriptionOverride,
            };
            configured = BuildingArchitectureConceptPageLayout.IsElevationSheet(
                sheet.Entry.ContentKind,
                sheet.Entry.Name,
                item.Page.TemplateSlotId)
                ? BuildingArchitectureConceptPageLayout.ApplyElevationGeometry(sourceFormat)
                : sourceFormat;
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
            var sheet = library.FindVerified(definition.SheetKey);
            if (sheet is null)
            {
                continue;
            }

            result.Add(new AlbumBuildPage
            {
                Sheet = sheet,
                Definition = definition,
                Format = PageFormatCatalog.ResolveForConceptPage(definition, sheet.Entry),
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
                var record = library.FindVerified(key);
                if (record is null)
                {
                    throw new AlbumBuildException([$"Album sheet '{key}' is missing or unverified."]);
                }

                pages.Add(CreateLegacyPage(record, section.Id));
                usedKeys.Add(key);
            }

            sections.Add(new AlbumBuildSection { Title = section.Title, Pages = pages });
        }

        var unassigned = library.VerifiedSnapshot()
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

    private static void RejectUnresolvedPages(
        IEnumerable<AlbumPageDefinition> pages,
        SheetLibrary library)
    {
        var issues = pages
            .Where(page => !string.IsNullOrWhiteSpace(page.SheetKey))
            .Where(page => library.FindVerified(page.SheetKey) is null)
            .Select(page => $"Album sheet '{page.SheetKey}' is missing or unverified.")
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (issues.Count > 0)
        {
            throw new AlbumBuildException(issues);
        }
    }

    private static void VerifySourcePackages(AlbumBuildRequest request)
    {
        var records = request.Sections
            .SelectMany(section => section.Pages)
            .Select(page => page.Sheet)
            .DistinctBy(record => record.Key)
            .ToList();
        var issues = new List<string>();

        foreach (var record in records.Where(record => !record.IsVerified))
        {
            issues.Add($"Sheet '{record.DisplayLabel}' is not verified.");
        }

        foreach (var manifestGroup in records.GroupBy(
                     record => record.ManifestPath,
                     StringComparer.OrdinalIgnoreCase))
        {
            var verification = SheetPackageReader.Load(manifestGroup.Key);
            if (!verification.IsLossless || verification.Manifest is null)
            {
                issues.AddRange(verification.Issues.Select(issue =>
                    $"Package '{Path.GetFileName(manifestGroup.Key)}': {issue}"));
                continue;
            }

            foreach (var record in manifestGroup)
            {
                if (verification.Manifest.PackageId != record.PackageId)
                {
                    issues.Add($"Sheet '{record.DisplayLabel}': package identity changed after intake.");
                    continue;
                }

                var entry = verification.Manifest.Sheets.FirstOrDefault(candidate =>
                    string.Equals(candidate.SheetId, record.Entry.SheetId, StringComparison.OrdinalIgnoreCase));
                if (entry is null || !verification.TryGetVerifiedPdfPath(entry, out var verifiedPath))
                {
                    issues.Add($"Sheet '{record.DisplayLabel}': verified package entry is unavailable.");
                    continue;
                }
                if (!string.Equals(
                        Path.GetFullPath(verifiedPath),
                        Path.GetFullPath(record.PdfPath),
                        StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Sheet '{record.DisplayLabel}': package PDF path changed after intake.");
                }
                if (!string.Equals(entry.Sha256, record.Entry.Sha256, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add($"Sheet '{record.DisplayLabel}': package hash changed after intake.");
                }
            }
        }

        if (issues.Count > 0)
        {
            throw new AlbumBuildException(issues);
        }
    }

    private sealed class ConceptSectionRun(string key, string title)
    {
        public string Key { get; } = key;
        public string Title { get; } = title;
        public List<AlbumBuildPage> Pages { get; } = [];
    }
}
