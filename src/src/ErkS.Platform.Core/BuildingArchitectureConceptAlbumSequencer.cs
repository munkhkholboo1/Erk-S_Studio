using System.Globalization;
using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

/// <summary>
/// One Studio-owned position in the concept album. Source sheet numbers remain
/// metadata; <see cref="AutomaticNumber"/> is the number printed by Studio.
/// </summary>
public sealed class ConceptAlbumSourcePage
{
    public required AlbumPageDefinition Page { get; init; }
    public required AlbumCompositionItem? Slot { get; init; }
    public required SheetRecord? Sheet { get; init; }
    public required ProjectDesignSource? Source { get; init; }
    public required string AutomaticNumber { get; init; }
    public required string SourceGroupKey { get; init; }
    public required string SourceGroupTitle { get; init; }
    public required bool IsFixedTemplatePage { get; init; }

    public string Number => string.IsNullOrWhiteSpace(Page.NumberOverride)
        ? AutomaticNumber
        : Page.NumberOverride;

    public string SectionKey => IsFixedTemplatePage
        ? $"fixed:{Slot?.SectionTitle ?? "source"}"
        : SourceGroupKey;

    public string SectionTitle => IsFixedTemplatePage
        ? Slot?.SectionTitle ?? SourceGroupTitle
        : SourceGroupTitle;
}

/// <summary>
/// Defines the authoritative source-page order for a building architecture
/// concept album: fixed general pages, then source, building and drawing kind.
/// </summary>
public static class BuildingArchitectureConceptAlbumSequencer
{
    public static IReadOnlyList<ConceptAlbumSourcePage> Create(
        AlbumDefinition definition,
        IEnumerable<AlbumPageDefinition> pages,
        SheetLibrary library,
        IReadOnlyList<ProjectDesignSource> sources,
        int generatedPageCount = -1,
        IReadOnlyList<ProjectBuildingGroup>? buildingGroups = null,
        IReadOnlyDictionary<string, string>? sheetBuildingAssignments = null) =>
        Create(
            definition,
            pages,
            library,
            sources,
            generatedPageCount,
            buildingGroups,
            sheetBuildingAssignments,
            authoritativeSourceId: null);

    private static IReadOnlyList<ConceptAlbumSourcePage> Create(
        AlbumDefinition definition,
        IEnumerable<AlbumPageDefinition> pages,
        SheetLibrary library,
        IReadOnlyList<ProjectDesignSource> sources,
        int generatedPageCount,
        IReadOnlyList<ProjectBuildingGroup>? buildingGroups,
        IReadOnlyDictionary<string, string>? sheetBuildingAssignments,
        string? authoritativeSourceId)
    {
        List<ProjectBuildingGroup> normalizedBuildingGroups =
            ProjectBuildingComposition.NormalizeGroups(buildingGroups);
        Dictionary<string, ProjectBuildingGroup> buildingGroupsById =
            normalizedBuildingGroups.ToDictionary(
                group => group.Id,
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> normalizedAssignments =
            ProjectBuildingComposition.NormalizeAssignments(
                sheetBuildingAssignments,
                normalizedBuildingGroups);
        List<AlbumPageDefinition> pageList = pages.ToList();
        Dictionary<string, int> sourceOrder = pageList
            .Select((page, index) => new
            {
                SourceId = ExtractSourceIdentity(page.SheetKey),
                Index = index,
            })
            .Where(item => !string.IsNullOrWhiteSpace(item.SourceId))
            .GroupBy(item => item.SourceId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Min(item => item.Index),
                StringComparer.OrdinalIgnoreCase);

        var candidates = pageList
            .Where(page => BuildingArchitectureConceptAlbumTemplate.FindSlot(
                definition,
                page.TemplateSlotId)?.Kind != AlbumCompositionKind.Generated)
            .Select((page, index) => CreateCandidate(
                definition,
                page,
                index,
                library,
                sources,
                sourceOrder,
                buildingGroupsById,
                normalizedAssignments,
                authoritativeSourceId))
            .ToList();

        var automaticBuildingSourceCounts = candidates
            .Where(candidate => candidate.IsPackageBuilding)
            .GroupBy(candidate => candidate.SourceGroupKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .Select(candidate => candidate.SourceId)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count(),
                StringComparer.OrdinalIgnoreCase);
        foreach (Candidate candidate in candidates.Where(candidate => candidate.IsPackageBuilding))
        {
            candidate.SourceGroupTitle =
                automaticBuildingSourceCounts[candidate.SourceGroupKey] > 1
                    ? candidate.BuildingTitle
                    : $"{candidate.SourceSortName} · {candidate.BuildingTitle}";
        }

        var firstBuildingPositions = candidates
            .GroupBy(candidate => candidate.SourceGroupKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Min(candidate => candidate.OriginalIndex),
                StringComparer.OrdinalIgnoreCase);

        var fixedPages = candidates
            .Where(candidate => candidate.IsFixedTemplatePage)
            .OrderBy(candidate => candidate.SlotOrder)
            .ThenBy(candidate => candidate.SourceOrder)
            .ThenBy(candidate => candidate.SourceSheetOrder)
            .ThenBy(candidate => candidate.OriginalIndex)
            .ToList();

        var pdfSourceBlockOrders = candidates
            .Where(candidate => !candidate.IsFixedTemplatePage && candidate.IsPdfSource)
            .GroupBy(candidate => candidate.SourceBlockKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Min(candidate => candidate.SlotOrder),
                StringComparer.OrdinalIgnoreCase);

        var drawingPages = candidates
            .Where(candidate => !candidate.IsFixedTemplatePage)
            .OrderBy(candidate => candidate.DrawingBand)
            .ThenBy(candidate => candidate.BuildingOrder)
            .ThenBy(candidate => firstBuildingPositions[candidate.SourceGroupKey])
            // Within a building, the kind of drawing decides - not which
            // product sent it. Two products serve one building and each
            // numbers its own set from one.
            .ThenBy(candidate => candidate.BuildingPageTypeOrder)
            .ThenBy(candidate => candidate.IsPdfSource
                ? pdfSourceBlockOrders[candidate.SourceBlockKey]
                : candidate.SlotOrder)
            .ThenBy(candidate => candidate.SourceOrder)
            .ThenBy(candidate => candidate.SourceSortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.SourceSheetOrder)
            .ThenBy(candidate => candidate.OriginalIndex)
            .ToList();

        var maximumReservedNumber = definition.Composition
            .Select(item => TryParseFixedNumber(item.Number, out var number) ? number : -1)
            .DefaultIfEmpty(-1)
            .Max();
        int generatedComponentCount = definition.Composition.Count(item =>
            item.Kind == AlbumCompositionKind.Generated);
        int generatedPageOffset = generatedPageCount < 0
            ? 0
            : Math.Max(0, generatedPageCount - generatedComponentCount);
        int adjustedMaximumReservedNumber = maximumReservedNumber + generatedPageOffset;
        var numberWidth = Math.Max(2, Math.Max(0, adjustedMaximumReservedNumber)
            .ToString(CultureInfo.InvariantCulture).Length);
        var nextDrawingNumber = Math.Max(0, adjustedMaximumReservedNumber + 1);
        var result = new List<ConceptAlbumSourcePage>(candidates.Count);

        foreach (var candidate in fixedPages)
        {
            var automaticNumber = TryParseFixedNumber(candidate.Slot?.Number, out var fixedNumber)
                ? (fixedNumber + generatedPageOffset).ToString($"D{numberWidth}", CultureInfo.InvariantCulture)
                : (nextDrawingNumber++).ToString($"D{numberWidth}", CultureInfo.InvariantCulture);
            result.Add(candidate.ToSequenceItem(automaticNumber));
        }

        foreach (var candidate in drawingPages)
        {
            result.Add(candidate.ToSequenceItem(
                (nextDrawingNumber++).ToString($"D{numberWidth}", CultureInfo.InvariantCulture)));
        }

        return result;
    }

    public static IReadOnlyList<AlbumPageDefinition> OrderPages(
        AlbumDefinition definition,
        IEnumerable<AlbumPageDefinition> pages,
        SheetLibrary library,
        IReadOnlyList<ProjectDesignSource> sources,
        IReadOnlyList<ProjectBuildingGroup>? buildingGroups = null,
        IReadOnlyDictionary<string, string>? sheetBuildingAssignments = null)
    {
        return Create(
                definition,
                pages,
                library,
                sources,
                generatedPageCount: -1,
                buildingGroups: buildingGroups,
                sheetBuildingAssignments: sheetBuildingAssignments,
                authoritativeSourceId: null)
            .Select(item => item.Page)
            .ToList();
    }

    /// <summary>
    /// Applies authoritative package/PDF order only to the source whose package
    /// is currently being reconciled. Other sources retain their project-owned
    /// order so a runtime cache hydration cannot reshuffle unrelated pages.
    /// </summary>
    public static IReadOnlyList<AlbumPageDefinition> OrderPagesAfterSourceReconciliation(
        AlbumDefinition definition,
        IEnumerable<AlbumPageDefinition> pages,
        SheetLibrary library,
        IReadOnlyList<ProjectDesignSource> sources,
        string authoritativeSourceId,
        IReadOnlyList<ProjectBuildingGroup>? buildingGroups = null,
        IReadOnlyDictionary<string, string>? sheetBuildingAssignments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authoritativeSourceId);
        return Create(
                definition,
                pages,
                library,
                sources,
                generatedPageCount: -1,
                buildingGroups: buildingGroups,
                sheetBuildingAssignments: sheetBuildingAssignments,
                authoritativeSourceId: authoritativeSourceId.Trim())
            .Select(item => item.Page)
            .ToList();
    }

    public static int NextAutomaticNumber(
        AlbumDefinition definition,
        IEnumerable<ConceptAlbumSourcePage> sourcePages,
        int generatedPageCount) => NextAutomaticNumber(
            definition,
            sourcePages.Select(page => page.AutomaticNumber),
            generatedPageCount);

    public static int NextAutomaticNumber(
        AlbumDefinition definition,
        IEnumerable<string> automaticNumbers,
        int generatedPageCount)
    {
        int maximumReservedNumber = definition.Composition
            .Select(item => TryParseFixedNumber(item.Number, out int number) ? number : -1)
            .DefaultIfEmpty(-1)
            .Max();
        int generatedComponentCount = definition.Composition.Count(item =>
            item.Kind == AlbumCompositionKind.Generated);
        int generatedPageOffset = Math.Max(0, generatedPageCount - generatedComponentCount);
        int maximumUsedNumber = maximumReservedNumber + generatedPageOffset;
        foreach (string automaticNumber in automaticNumbers)
        {
            if (int.TryParse(
                    automaticNumber,
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int number))
            {
                maximumUsedNumber = Math.Max(maximumUsedNumber, number);
            }
        }
        return Math.Max(0, maximumUsedNumber + 1);
    }

    private static Candidate CreateCandidate(
        AlbumDefinition definition,
        AlbumPageDefinition page,
        int originalIndex,
        SheetLibrary library,
        IReadOnlyList<ProjectDesignSource> sources,
        IReadOnlyDictionary<string, int> sourceOrder,
        IReadOnlyDictionary<string, ProjectBuildingGroup> buildingGroupsById,
        IReadOnlyDictionary<string, string> sheetBuildingAssignments,
        string? authoritativeSourceId)
    {
        var sheet = library.FindVerified(page.SheetKey);
        var slot = BuildingArchitectureConceptAlbumTemplate.FindSlot(definition, page.TemplateSlotId);
        string persistedSourceId = ExtractSourceIdentity(page.SheetKey);
        bool useHydratedPackageMetadata =
            !string.IsNullOrWhiteSpace(authoritativeSourceId) &&
            (persistedSourceId.Equals(
                 authoritativeSourceId,
                 StringComparison.OrdinalIgnoreCase) ||
             (sheet is not null &&
              sheet.SourceId.Equals(
                  authoritativeSourceId,
                  StringComparison.OrdinalIgnoreCase)));
        var sourceId = useHydratedPackageMetadata
            ? sheet?.SourceId ?? persistedSourceId
            : persistedSourceId;
        var source = sources.FirstOrDefault(item =>
            string.Equals(item.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        var sourceTitle = ResolveSourceTitle(
            source,
            useHydratedPackageMetadata ? sheet : null);
        var buildingIdentity = ResolveBuildingIdentity(
            page,
            useHydratedPackageMetadata ? sheet : null);
        ProjectBuildingGroup? assignedGroup = null;
        var hasExplicitAssignment =
            sheetBuildingAssignments.TryGetValue(page.SheetKey, out var assignedGroupId) &&
            buildingGroupsById.TryGetValue(assignedGroupId, out assignedGroup);
        var hasPackageBuilding = !string.IsNullOrWhiteSpace(buildingIdentity.Title);
        var isGeneralPlan =
            source is not null &&
            ProjectDesignSourceClassification.IsGeneralPlan(source);
        var groupKey = hasExplicitAssignment
            ? $"studio-building:{assignedGroup!.Id}"
            : hasPackageBuilding
                ? $"package-building:{buildingIdentity.Key}"
                : $"source-building:{sourceId}";
        var groupTitle = hasExplicitAssignment
            ? assignedGroup!.Name
            : hasPackageBuilding
                ? buildingIdentity.Title
                : sourceTitle;

        return new Candidate
        {
            Page = page,
            Slot = slot,
            Sheet = sheet,
            Source = source,
            OriginalIndex = originalIndex,
            SourceOrder =
                sourceOrder.TryGetValue(persistedSourceId, out int index) ||
                sourceOrder.TryGetValue(sourceId, out index)
                    ? index
                    : originalIndex,
            SourceSheetOrder = ResolveSourceSheetOrder(
                sheet,
                originalIndex,
                useHydratedPackageMetadata
                    ? SourceSheetOrderMode.AuthoritativePackageOrder
                    : SourceSheetOrderMode.PersistedAlbumOrder),
            SourceId = sourceId,
            SourceSortName = sourceTitle,
            SourceGroupKey = groupKey,
            SourceGroupTitle = groupTitle,
            BuildingTitle = hasPackageBuilding ? buildingIdentity.Title : "",
            DrawingBand = isGeneralPlan
                ? 0
                : hasExplicitAssignment || hasPackageBuilding
                    ? 1
                    : 2,
            BuildingPageTypeOrder = ErkS.Platform.Core.BuildingPageTypeOrder.Of(
                AlbumPageSourceMetadata.ResolveContentKind(page, sheet?.Entry ?? new SheetPackageEntry())),
            BuildingOrder = hasExplicitAssignment
                ? assignedGroup!.Order
                : hasPackageBuilding
                    ? int.MaxValue - 1
                    : int.MaxValue,
            IsPackageBuilding = !hasExplicitAssignment && hasPackageBuilding,
            SlotOrder = slot?.Order ?? int.MaxValue,
            IsPdfSource =
                source?.Kind == DesignSourceKind.Pdf ||
                (useHydratedPackageMetadata &&
                 sheet?.Source.Application == SheetSourceApplication.Pdf),
            IsFixedTemplatePage = slot is
            {
                Kind: AlbumCompositionKind.SourceSlot,
                AllowMultiple: false,
            },
        };
    }

    private static int ResolveSourceSheetOrder(
        SheetRecord? sheet,
        int persistedOrder,
        SourceSheetOrderMode mode)
    {
        // The library is an asynchronously hydrated runtime cache. Normal
        // preview/build ordering must therefore use the project-owned page
        // order, or merely opening a project can reshuffle equal-rank pages.
        // Reconciliation explicitly opts into package/PDF order before saving
        // newly inserted or updated pages back to the album document.
        if (mode == SourceSheetOrderMode.PersistedAlbumOrder ||
            sheet is null)
        {
            return persistedOrder;
        }

        return sheet.Entry.PdfPageNumber > 0
            ? sheet.Entry.PdfPageNumber - 1
            : sheet.SourceSheetIndex;
    }

    private static (string Key, string Title) ResolveBuildingIdentity(
        AlbumPageDefinition page,
        SheetRecord? sheet)
    {
        string buildingId = !string.IsNullOrWhiteSpace(
                page.SourceBuildingIdSnapshot)
            ? page.SourceBuildingIdSnapshot.Trim()
            : sheet?.Entry.BuildingId?.Trim() ?? "";
        string buildingName = !string.IsNullOrWhiteSpace(
                page.SourceBuildingNameSnapshot)
            ? page.SourceBuildingNameSnapshot.Trim()
            : sheet?.Entry.BuildingName?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(buildingId))
        {
            string title = string.IsNullOrWhiteSpace(buildingName)
                ? buildingId
                : buildingName;
            return ($"id:{buildingId}", title);
        }

        if (!string.IsNullOrWhiteSpace(buildingName))
        {
            return ($"name:{buildingName}", buildingName);
        }

        // Until a producer supplies building metadata, one native source is one
        // building group. This keeps separate RVT/DWG files from interleaving.
        return ("source-building", "");
    }

    private static string ResolveSourceTitle(ProjectDesignSource? source, SheetRecord? sheet)
    {
        if (!string.IsNullOrWhiteSpace(source?.NativeDocumentTitle))
        {
            return source.NativeDocumentTitle.Trim();
        }

        if (!string.IsNullOrWhiteSpace(source?.NativeDocumentPath))
        {
            return Path.GetFileName(source.NativeDocumentPath.Trim());
        }

        if (!string.IsNullOrWhiteSpace(source?.Name))
        {
            return source.Name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(sheet?.Source.DocumentTitle))
        {
            return sheet.Source.DocumentTitle.Trim();
        }

        return sheet is null ? "Эх үүсвэр олдсонгүй" : sheet.Source.Application.ToString();
    }

    private static string ExtractSourceIdentity(string sheetKey)
    {
        if (string.IsNullOrWhiteSpace(sheetKey))
        {
            return "missing-source";
        }

        var separator = sheetKey.IndexOf('|');
        return separator > 0 ? sheetKey[..separator] : sheetKey;
    }

    private static bool TryParseFixedNumber(string? value, out int number) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out number);

    private sealed class Candidate
    {
        public required AlbumPageDefinition Page { get; init; }
        public required AlbumCompositionItem? Slot { get; init; }
        public required SheetRecord? Sheet { get; init; }
        public required ProjectDesignSource? Source { get; init; }
        public required int OriginalIndex { get; init; }
        public required int SourceOrder { get; init; }
        public required int SourceSheetOrder { get; init; }
        public required string SourceId { get; init; }
        public required string SourceSortName { get; init; }
        public required string SourceGroupKey { get; init; }
        public string SourceBlockKey => $"{SourceGroupKey}\u001f{SourceId}";
        public required string SourceGroupTitle { get; set; }
        public required string BuildingTitle { get; init; }
        public required int DrawingBand { get; init; }
        public required int BuildingOrder { get; init; }

        /// <summary>
        /// Where this drawing belongs among its building's pages. It sorts
        /// before the source, so a building reads the same however many
        /// products contributed to it and in whatever order they exported.
        /// </summary>
        public required int BuildingPageTypeOrder { get; init; }
        public required bool IsPackageBuilding { get; init; }
        public required int SlotOrder { get; init; }
        public required bool IsPdfSource { get; init; }
        public required bool IsFixedTemplatePage { get; init; }

        public ConceptAlbumSourcePage ToSequenceItem(string automaticNumber) => new()
        {
            Page = Page,
            Slot = Slot,
            Sheet = Sheet,
            Source = Source,
            AutomaticNumber = automaticNumber,
            SourceGroupKey = SourceGroupKey,
            SourceGroupTitle = SourceGroupTitle,
            IsFixedTemplatePage = IsFixedTemplatePage,
        };
    }

    private enum SourceSheetOrderMode
    {
        PersistedAlbumOrder,
        AuthoritativePackageOrder,
    }
}
