using System.Globalization;

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
        int generatedPageCount = -1)
    {
        var sourceOrder = sources
            .Select((source, index) => new { source.Id, Index = index })
            .Where(item => !string.IsNullOrWhiteSpace(item.Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Index, StringComparer.OrdinalIgnoreCase);

        var candidates = pages
            .Where(page => BuildingArchitectureConceptAlbumTemplate.FindSlot(
                definition,
                page.TemplateSlotId)?.Kind != AlbumCompositionKind.Generated)
            .Select((page, index) => CreateCandidate(
                definition,
                page,
                index,
                library,
                sources,
                sourceOrder))
            .ToList();

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
            .ThenBy(candidate => candidate.OriginalIndex)
            .ToList();

        var drawingPages = candidates
            .Where(candidate => !candidate.IsFixedTemplatePage)
            .OrderBy(candidate => candidate.SourceOrder)
            .ThenBy(candidate => candidate.SourceSortName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => firstBuildingPositions[candidate.SourceGroupKey])
            .ThenBy(candidate => candidate.SlotOrder)
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
        IReadOnlyList<ProjectDesignSource> sources) =>
        Create(definition, pages, library, sources)
            .Select(item => item.Page)
            .ToList();

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
        IReadOnlyDictionary<string, int> sourceOrder)
    {
        var sheet = library.FindVerified(page.SheetKey);
        var slot = BuildingArchitectureConceptAlbumTemplate.FindSlot(definition, page.TemplateSlotId);
        var sourceId = sheet?.SourceId ?? ExtractSourceIdentity(page.SheetKey);
        var source = sources.FirstOrDefault(item =>
            string.Equals(item.Id, sourceId, StringComparison.OrdinalIgnoreCase));
        var sourceTitle = ResolveSourceTitle(source, sheet);
        var buildingIdentity = ResolveBuildingIdentity(sheet);
        var groupKey = $"{sourceId}|{buildingIdentity.Key}";
        var groupTitle = string.IsNullOrWhiteSpace(buildingIdentity.Title)
            ? sourceTitle
            : $"{sourceTitle} · {buildingIdentity.Title}";

        return new Candidate
        {
            Page = page,
            Slot = slot,
            Sheet = sheet,
            Source = source,
            OriginalIndex = originalIndex,
            SourceOrder = sourceOrder.TryGetValue(sourceId, out var index) ? index : int.MaxValue,
            SourceSortName = sourceTitle,
            SourceGroupKey = groupKey,
            SourceGroupTitle = groupTitle,
            SlotOrder = slot?.Order ?? int.MaxValue,
            IsFixedTemplatePage = slot is
            {
                Kind: AlbumCompositionKind.SourceSlot,
                AllowMultiple: false,
            },
        };
    }

    private static (string Key, string Title) ResolveBuildingIdentity(SheetRecord? sheet)
    {
        if (!string.IsNullOrWhiteSpace(sheet?.Entry.BuildingId))
        {
            var title = string.IsNullOrWhiteSpace(sheet.Entry.BuildingName)
                ? sheet.Entry.BuildingId.Trim()
                : sheet.Entry.BuildingName.Trim();
            return ($"id:{sheet.Entry.BuildingId.Trim()}", title);
        }

        if (!string.IsNullOrWhiteSpace(sheet?.Entry.BuildingName))
        {
            var title = sheet.Entry.BuildingName.Trim();
            return ($"name:{title}", title);
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
        public required string SourceSortName { get; init; }
        public required string SourceGroupKey { get; init; }
        public required string SourceGroupTitle { get; init; }
        public required int SlotOrder { get; init; }
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
}
