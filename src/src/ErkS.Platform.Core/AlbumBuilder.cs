using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

/// <summary>Consecutive pages of one section, in the order the album stores them.</summary>
public sealed record AlbumSourceRun(string Title, IReadOnlyList<AlbumPageDefinition> Pages);

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

public enum AlbumBuildSectionKind
{
    Standard,
    Building,
}

public sealed class AlbumBuildSection
{
    public string Key { get; init; } = "";
    public required string Title { get; init; }
    public AlbumBuildSectionKind Kind { get; init; }
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

    public string ScaleText => DrawingScaleText.Resolve(Definition, Sheet.Entry);
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
    public string SourceIdentity { get; init; } = "";
    public string SectionKey { get; init; } = "";
    public string SequenceKey { get; init; } = "";
    public List<int> PageNumbers { get; init; } = [];
    public List<AlbumBuildComponentPage> Pages { get; init; } = [];
}

public sealed class AlbumBuildComponentPage
{
    public required int PageNumber { get; init; }
    public string PageKey { get; init; } = "";

    /// <summary>
    /// What this page is called on the drawing. A component covering six pages
    /// has one label for all six; this is the name of the one page.
    /// </summary>
    public string Title { get; init; } = "";
    public string NativeSheetId { get; init; } = "";
    public int NativePageNumber { get; init; }
    public string SortKey { get; init; } = "";
    public string SectionKey { get; init; } = "";
    public string SequenceKey { get; init; } = "";
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
            // A drawing that came back at the wrong scale is the one nobody
            // catches by eye. Reported, never refused: whoever plotted it
            // knows things this does not, and a build that stops is a build
            // that gets worked around.
            result.Warnings.AddRange(DrawingScaleSurvey.Review(request));
            result.Components.AddRange(temporaryResult.Components.Select(component => new AlbumBuildComponent
            {
                Code = component.Code,
                Label = component.Label,
                Order = component.Order,
                SourceIdentity = component.SourceIdentity,
                SectionKey = component.SectionKey,
                SequenceKey = component.SequenceKey,
                PageNumbers = component.PageNumbers.ToList(),
                Pages = component.Pages.Select(page =>
                    new AlbumBuildComponentPage
                    {
                        PageNumber = page.PageNumber,
                        PageKey = page.PageKey,
                        Title = page.Title,
                        NativeSheetId = page.NativeSheetId,
                        NativePageNumber = page.NativePageNumber,
                        SortKey = page.SortKey,
                        SectionKey = page.SectionKey,
                        SequenceKey = page.SequenceKey,
                    }).ToList(),
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

    /// <summary>
    /// Builds the request, or reports that it cannot be built yet. An album whose
    /// sheets have not been received references pages nothing can resolve - an
    /// ordinary state for a project waiting on a delivery, not a fault. Callers
    /// that only want to look at the album, rather than produce it, ask this way
    /// so an unbuildable album stays a fact about the album.
    /// </summary>
    public static bool TryCreateRequest(
        AlbumProject project,
        SheetLibrary library,
        out AlbumBuildRequest request)
    {
        try
        {
            request = CreateRequest(project, library);
            return true;
        }
        catch (AlbumBuildException)
        {
            request = null!;
            return false;
        }
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

        if (IsUrbanPlanningAlbum(project.Album))
            return CreateSourceOrderedRequest(project, library);

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

    private static bool IsUrbanPlanningAlbum(AlbumDefinition album) =>
        ProjectTypes.UrbanPlanning.UrbanPlanningAlbumTemplate.IsUrbanPlanningTemplate(
            album.TemplateId);

    /// <summary>
    /// Keeps the album in the order its pages are stored, which for a general
    /// plan is the order the sheets carry in AutoCAD.
    ///
    /// Ordering by template slot instead put every sheet the slot matcher did
    /// not recognise at the end of the album - and the matcher's numbered
    /// branch never fires for an AutoCAD package, whose numbers are bare
    /// "00".."14" while the slots are numbered "ЕТ-03".."ЕТ-17". Sections are
    /// read off the page order as runs rather than imposed on it, so a sheet
    /// belonging to no section stays where it is instead of being swept into a
    /// trailing bucket.
    /// </summary>
    private static AlbumBuildRequest CreateSourceOrderedRequest(
        AlbumProject project,
        SheetLibrary library)
    {
        var sections = new List<AlbumBuildSection>();
        foreach (AlbumSourceRun run in BuildSourceOrderedRuns(project.Album))
        {
            List<AlbumBuildPage> resolved = ResolvePages(run.Pages, library);
            if (resolved.Count > 0)
                sections.Add(new AlbumBuildSection { Title = run.Title, Pages = resolved });
        }

        return new AlbumBuildRequest { Project = project, Sections = sections };
    }

    /// <summary>
    /// The album's pages grouped into consecutive runs of one section, in the
    /// order the pages are stored.
    /// </summary>
    public static IReadOnlyList<AlbumSourceRun> BuildSourceOrderedRuns(AlbumDefinition album)
    {
        ArgumentNullException.ThrowIfNull(album);
        Dictionary<Guid, string> titles = album.Sections
            .GroupBy(section => section.Id)
            .ToDictionary(group => group.Key, group => group.First().Title);
        var runs = new List<(string Title, List<AlbumPageDefinition> Pages)>();
        foreach (AlbumPageDefinition page in album.Pages)
        {
            string title = page.SectionId.HasValue &&
                titles.TryGetValue(page.SectionId.Value, out string? value)
                    ? value
                    : "";
            if (runs.Count == 0 ||
                !runs[^1].Title.Equals(title, StringComparison.Ordinal))
            {
                runs.Add((title, []));
            }
            runs[^1].Pages.Add(page);
        }

        return runs
            .Select(run => new AlbumSourceRun(run.Title, run.Pages))
            .ToList();
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
            generatedPageCount,
            project.BuildingGroups,
            project.SheetBuildingAssignments);
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
                .Select(run => new AlbumBuildSection
                {
                    Key = run.Key,
                    Title = run.Title,
                    Kind = IsBuildingSectionKey(run.Key)
                        ? AlbumBuildSectionKind.Building
                        : AlbumBuildSectionKind.Standard,
                    Pages = run.Pages,
                })
                .ToList(),
        };
    }

    private static bool IsBuildingSectionKey(string key) =>
        key.StartsWith("studio-building:", StringComparison.OrdinalIgnoreCase) ||
        key.StartsWith("package-building:", StringComparison.OrdinalIgnoreCase);

    private static AlbumBuildPage CreateConceptBuildPage(ConceptAlbumSourcePage item)
    {
        var sheet = item.Sheet ?? throw new InvalidOperationException("Concept album source page is unresolved.");
        var definition = item.Page;
        var configured = PageFormatCatalog.ResolveForConceptPage(definition, sheet.Entry);
        if (PageFormatResolver.TryResolveSourceFormat(sheet.Entry, out var sourceFormat) &&
            sourceFormat.Kind == PageFormatKind.WorkingDrawing)
        {
            // A stale concept album/page snapshot must not suppress the chrome
            // explicitly requested by an AutoCAD/Revit working-drawing sheet.
            definition = CloneForStudioChrome(item.Page);
            configured = sourceFormat;
        }
        else if (configured.Kind == PageFormatKind.SourceAsIs &&
            PageFormatResolver.TryResolveSourceFormat(sheet.Entry, out sourceFormat))
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
                ScaleTextOverride = item.Page.ScaleTextOverride,
                ContentKindOverride = item.Page.ContentKindOverride,
                RoleAssignments = item.Page.RoleAssignments.Select(assignment => assignment.Clone()).ToList(),
                SourceCrop = item.Page.SourceCrop?.DeepClone(),
                ElevationDescriptionOverride = item.Page.ElevationDescriptionOverride,
            };
            configured = BuildingArchitectureConceptPageLayout.UsesInformationHeader(
                AlbumPageSourceMetadata.ResolveContentKind(item.Page, sheet.Entry),
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

            AlbumPageDefinition buildDefinition = definition;
            PageFormatDefinition format = PageFormatCatalog.ResolveForConceptPage(definition, sheet.Entry);
            // A producer-declared working-drawing format is authoritative. The
            // Studio-owned grid, sheet title and title block must not disappear
            // merely because an older project retained a stale album template.
            if (PageFormatResolver.TryResolveSourceFormat(sheet.Entry, out PageFormatDefinition sourceFormat) &&
                sourceFormat.Kind == PageFormatKind.WorkingDrawing)
            {
                buildDefinition = CloneForStudioChrome(definition);
                format = sourceFormat;
            }

            result.Add(new AlbumBuildPage
            {
                Sheet = sheet,
                Definition = buildDefinition,
                Format = format,
            });
        }

        return result;
    }

    private static AlbumPageDefinition CloneForStudioChrome(AlbumPageDefinition page) => new()
    {
        Id = page.Id,
        SheetKey = page.SheetKey,
        TemplateSlotId = page.TemplateSlotId,
        SectionId = page.SectionId,
        PageFormatId = page.PageFormatId,
        PageFormatSnapshot = page.PageFormatSnapshot,
        FollowSourceFormat = page.FollowSourceFormat,
        PlacementMode = PagePlacementMode.FullPage,
        NumberOverride = page.NumberOverride,
        TitleOverride = page.TitleOverride,
        ScaleTextOverride = page.ScaleTextOverride,
        ContentKindOverride = page.ContentKindOverride,
        RoleAssignments = page.RoleAssignments.Select(assignment => assignment.Clone()).ToList(),
        SourceCrop = page.SourceCrop?.DeepClone(),
        ElevationDescriptionOverride = page.ElevationDescriptionOverride,
        SourceBuildingIdSnapshot = page.SourceBuildingIdSnapshot,
        SourceBuildingNameSnapshot = page.SourceBuildingNameSnapshot,
    };

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
        PageFormatDefinition format = PageFormatCatalog.Resolve(definition.PageFormatId);
        if (PageFormatResolver.TryResolveSourceFormat(sheet.Entry, out PageFormatDefinition sourceFormat) &&
            sourceFormat.Kind == PageFormatKind.WorkingDrawing)
        {
            definition = CloneForStudioChrome(definition);
            format = sourceFormat;
        }
        return new AlbumBuildPage
        {
            Sheet = sheet,
            Definition = definition,
            Format = format,
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
