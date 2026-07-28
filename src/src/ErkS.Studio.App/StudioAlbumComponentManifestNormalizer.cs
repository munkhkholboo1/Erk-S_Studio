using System.IO;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record StudioAlbumComponentManifestNormalizationPlan(
    IReadOnlyList<StudioCloudAlbumSection> OriginalSlots,
    IReadOnlyList<StudioCloudAlbumSection> TargetManifest,
    IReadOnlyDictionary<string, string> CanonicalCodeByRetainedCode,
    IReadOnlyList<string> RemovedCodes,
    int OriginalPageCount,
    bool RequiresPdfRewrite);

internal static class StudioAlbumComponentManifestNormalizer
{
    public static StudioAlbumComponentManifestNormalizationPlan CreatePlan(
        ProjectWorkspace project,
        IReadOnlyList<StudioCloudAlbumSection> manifest,
        IReadOnlyDictionary<string, int> sourceOrder)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(sourceOrder);
        if (manifest.Count == 0)
            throw new InvalidDataException("Album component manifest is empty.");

        StudioCloudAlbumSection[] manifestEntries = manifest
            .Where(component => component is not null)
            .Select(Clone)
            .ToArray();
        var canonicalInputCodeBySlotCode =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        List<StudioCloudAlbumSection> originalSlots = manifestEntries
            .GroupBy(
                component => (component.Code ?? "").Trim(),
                StringComparer.OrdinalIgnoreCase)
            .SelectMany(group => CreateOriginalSlots(
                group,
                canonicalInputCodeBySlotCode))
            .OrderBy(component => component.PageNumbers.DefaultIfEmpty(int.MaxValue).Min())
            .ThenBy(component => component.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (originalSlots.Any(component => string.IsNullOrWhiteSpace(component.Code)))
            throw new InvalidDataException("Every album component requires a stable code.");

        int[] originalPages = originalSlots
            .SelectMany(component => component.PageNumbers)
            .Order()
            .ToArray();
        int originalPageCount = originalPages.DefaultIfEmpty(0).Max();
        if (originalPageCount < 1 ||
            originalPages.Length != originalPages.Distinct().Count() ||
            !originalPages.SequenceEqual(Enumerable.Range(1, originalPageCount)))
        {
            throw new InvalidDataException(
                "Album component manifest must cover every PDF page exactly once.");
        }

        var retained = new List<RetainedComponent>();
        var removedCodes = new List<string>();
        foreach (IGrouping<string, StudioCloudAlbumSection> canonicalGroup in
                 originalSlots.GroupBy(
                     component =>
                     {
                         string inputCode = canonicalInputCodeBySlotCode.GetValueOrDefault(
                             component.Code,
                             component.Code);
                         return StudioAlbumComponentIdentity.CanonicalComponentCode(
                             project,
                             inputCode);
                     },
                     StringComparer.OrdinalIgnoreCase))
        {
            string canonicalCode = canonicalGroup.Key;
            StudioCloudAlbumSection[] candidates = canonicalGroup
                .OrderBy(component =>
                    canonicalInputCodeBySlotCode.GetValueOrDefault(
                            component.Code,
                            component.Code)
                        .Equals(canonicalCode, StringComparison.OrdinalIgnoreCase)
                        ? 0
                        : 1)
                .ThenBy(component =>
                    component.PageNumbers.DefaultIfEmpty(int.MaxValue).Min())
                .ThenBy(component => component.Code, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            StudioCloudAlbumSection selected = candidates[0];
            StudioCloudAlbumSection target = Clone(selected);
            target.Code = canonicalCode;
            target.Label = FirstNonEmpty(
                selected.Label,
                candidates.Select(component => component.Label));
            target.OwnerEmail = FirstNonEmpty(
                selected.OwnerEmail,
                candidates.Select(component => component.OwnerEmail));
            target.SourceKey = FirstNonEmpty(
                selected.SourceKey,
                candidates.Select(component => component.SourceKey));
            target.ComponentKind = FirstNonEmpty(
                selected.ComponentKind,
                candidates.Select(component => component.ComponentKind));
            target.Status = FirstNonEmpty(
                selected.Status,
                candidates.Select(component => component.Status),
                "Available");
            target.Order = StudioAlbumComponentOrderPolicy.Resolve(
                project,
                target.Code,
                target.SourceKey,
                target.Order,
                sourceOrder);
            retained.Add(new RetainedComponent(selected.Code, target));
            removedCodes.AddRange(candidates
                .Skip(1)
                .Select(component => component.Code));
        }

        retained = retained
            .OrderBy(component => component.Target.Order)
            .ThenBy(component =>
                component.Target.PageNumbers.DefaultIfEmpty(int.MaxValue).Min())
            .ThenBy(component => component.Target.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();

        int nextPage = 1;
        foreach (RetainedComponent component in retained)
        {
            int pageCount = component.Target.PageNumbers.Length;
            component.Target.PageNumbers = Enumerable
                .Range(nextPage, pageCount)
                .ToArray();
            nextPage += pageCount;
        }

        IReadOnlyDictionary<string, string> canonicalCodeByRetainedCode = retained
            .ToDictionary(
                component => component.OriginalCode,
                component => component.Target.Code,
                StringComparer.OrdinalIgnoreCase);
        int[] retainedPhysicalPages = retained
            .Select(component => originalSlots.Single(slot =>
                slot.Code.Equals(
                    component.OriginalCode,
                    StringComparison.OrdinalIgnoreCase)))
            .SelectMany(component => component.PageNumbers)
            .ToArray();
        bool requiresPdfRewrite =
            removedCodes.Count > 0 ||
            !retainedPhysicalPages.SequenceEqual(
                Enumerable.Range(1, originalPageCount));

        return new StudioAlbumComponentManifestNormalizationPlan(
            originalSlots,
            retained.Select(component => Clone(component.Target)).ToList(),
            canonicalCodeByRetainedCode,
            removedCodes
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(code => code, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            originalPageCount,
            requiresPdfRewrite);
    }

    private static IEnumerable<StudioCloudAlbumSection> CreateOriginalSlots(
        IGrouping<string, StudioCloudAlbumSection> group,
        IDictionary<string, string> canonicalInputCodeBySlotCode)
    {
        StudioCloudAlbumSection[] candidates = group
            .OrderBy(component =>
                (component.PageNumbers ?? []).DefaultIfEmpty(int.MaxValue).Min())
            .ThenBy(component => component.Order)
            .ThenBy(component => component.OwnerEmail, StringComparer.OrdinalIgnoreCase)
            .ThenBy(component => component.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        for (int index = 0; index < candidates.Length; index++)
        {
            StudioCloudAlbumSection slot = Clone(candidates[index]);
            string slotCode = index == 0
                ? group.Key
                : DuplicateSlotCode(group.Key, index);
            slot.Code = slotCode;
            slot.PageNumbers = (slot.PageNumbers ?? [])
                .Distinct()
                .Order()
                .ToArray();
            slot.Label = FirstNonEmpty(
                slot.Label,
                candidates.Select(component => component.Label));
            slot.OwnerEmail = FirstNonEmpty(
                slot.OwnerEmail,
                candidates.Select(component => component.OwnerEmail));
            slot.SourceKey = FirstNonEmpty(
                slot.SourceKey,
                candidates.Select(component => component.SourceKey));
            slot.ComponentKind = FirstNonEmpty(
                slot.ComponentKind,
                candidates.Select(component => component.ComponentKind));
            slot.Status = FirstNonEmpty(
                slot.Status,
                candidates.Select(component => component.Status),
                "Available");
            canonicalInputCodeBySlotCode[slotCode] = group.Key;
            yield return slot;
        }
    }

    private static string DuplicateSlotCode(string code, int index) =>
        $"{code}~duplicate-{index}";

    private static StudioCloudAlbumSection Clone(
        StudioCloudAlbumSection component) => new()
    {
        Code = component.Code ?? "",
        Label = component.Label ?? "",
        Order = component.Order,
        PageNumbers = (component.PageNumbers ?? []).ToArray(),
        Status = component.Status ?? "",
        OwnerEmail = component.OwnerEmail ?? "",
        SourceKey = component.SourceKey ?? "",
        ComponentKind = component.ComponentKind ?? "",
    };

    private static string FirstNonEmpty(
        string? preferred,
        IEnumerable<string?> alternatives,
        string fallback = "")
    {
        if (!string.IsNullOrWhiteSpace(preferred))
            return preferred.Trim();
        return alternatives
            .Select(value => value?.Trim() ?? "")
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ??
            fallback;
    }

    private sealed record RetainedComponent(
        string OriginalCode,
        StudioCloudAlbumSection Target);
}
