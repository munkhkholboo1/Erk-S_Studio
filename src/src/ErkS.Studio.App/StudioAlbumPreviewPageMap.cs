using System.IO;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Resolves a navigator item to the physical page in the PDF currently shown.
/// A canonical Cloud-union PDF is mapped from its persisted component manifest;
/// a locally built PDF is mapped from the exact page-writing order.
/// </summary>
internal static class StudioAlbumPreviewPageMap
{
    public static bool UsesSharedManifest(string? pdfPath)
    {
        if (string.IsNullOrWhiteSpace(pdfPath))
            return false;

        try
        {
            string fullPath = Path.GetFullPath(pdfPath);
            DirectoryInfo? directory = new FileInfo(fullPath).Directory;
            while (directory is not null)
            {
                if (directory.Name.Equals(
                        "cloud",
                        StringComparison.OrdinalIgnoreCase) ||
                    directory.Name.Equals(
                        "cloud-local",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
                directory = directory.Parent;
            }
            return false;
        }
        catch (Exception exception) when (
            exception is ArgumentException or
            NotSupportedException or
            PathTooLongException)
        {
            return false;
        }
    }

    public static int? ResolveCanonicalComponentPage(
        IEnumerable<ProjectCloudAlbumComponentReference>? manifest,
        string? componentCode,
        int zeroBasedPageOffset = 0)
    {
        string normalizedCode = (componentCode ?? "").Trim();
        if (string.IsNullOrWhiteSpace(normalizedCode) || zeroBasedPageOffset < 0)
            return null;

        ProjectCloudAlbumComponentReference[] matches = (manifest ?? [])
            .Where(component =>
                component is not null &&
                (component.Code ?? "").Trim().Equals(
                    normalizedCode,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return ResolveUniqueComponentPage(matches, zeroBasedPageOffset);
    }

    public static int? ResolveCanonicalGeneratedPage(
        IEnumerable<ProjectCloudAlbumComponentReference>? manifest,
        string? componentCode,
        int zeroBasedPageOffset = 0)
    {
        ProjectCloudAlbumComponentReference[] components = (manifest ?? [])
            .Where(component => component is not null)
            .ToArray();
        int? exact = ResolveCanonicalComponentPage(
            components,
            componentCode,
            zeroBasedPageOffset);
        if (exact.HasValue)
            return exact;

        string sourceKey = (componentCode ?? "").Trim() switch
        {
            var code when code.Equals(
                ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                StringComparison.OrdinalIgnoreCase) =>
                StudioAlbumComponentIdentity.AtdSourceKey,
            var code when code.Equals(
                ProjectCloudSyncMetadata.VisualizationsComponentCode,
                StringComparison.OrdinalIgnoreCase) =>
                StudioAlbumComponentIdentity.VisualizationSourceKey,
            _ => "",
        };
        if (string.IsNullOrWhiteSpace(sourceKey))
            return null;

        ProjectCloudAlbumComponentReference[] sourceMatches = components
            .Where(component =>
                (component.SourceKey ?? "").Trim().Equals(
                    sourceKey,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return ResolveUniqueComponentPage(sourceMatches, zeroBasedPageOffset);
    }

    public static int? ResolveCanonicalSourcePage(
        ProjectWorkspace workspace,
        AlbumBuildRequest request,
        Guid pageId,
        IEnumerable<ProjectCloudAlbumComponentReference>? manifest)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(request);

        AlbumBuildSection? targetSection = request.Sections.FirstOrDefault(
            section => section.Pages.Any(page =>
                page.Definition.Id == pageId));
        AlbumBuildPage? target = targetSection?.Pages.FirstOrDefault(page =>
            page.Definition.Id == pageId);
        if (targetSection is null || target is null)
            return null;
        string? targetCode = ResolveCanonicalSourceComponentCode(
            workspace,
            targetSection,
            target);
        if (targetCode is null)
            return null;

        int matchingPageOffset = 0;
        foreach (AlbumBuildSection section in request.Sections)
        {
            foreach (AlbumBuildPage buildPage in section.Pages)
            {
                string? code = ResolveCanonicalSourceComponentCode(
                    workspace,
                    section,
                    buildPage);
                if (buildPage.Definition.Id == pageId)
                {
                    return code is null ||
                        !code.Equals(targetCode, StringComparison.OrdinalIgnoreCase)
                        ? null
                        : ResolveCanonicalComponentPage(
                            manifest,
                            targetCode,
                            matchingPageOffset);
                }

                if (code?.Equals(
                        targetCode,
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    matchingPageOffset += OutputPageCount(buildPage);
                }
            }
        }

        return null;
    }

    public static int? ResolveLocalSourcePage(
        AlbumBuildRequest request,
        Guid pageId,
        int leadingPageCount,
        Func<AlbumBuildPage, bool>? isAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        isAvailable ??= static _ => true;
        int pageNumber = Math.Max(0, leadingPageCount);

        foreach (AlbumBuildSection section in request.Sections)
        {
            if (section.Kind == AlbumBuildSectionKind.Building &&
                section.Pages.Count > 0)
            {
                pageNumber++;
            }

            foreach (AlbumBuildPage buildPage in section.Pages)
            {
                if (buildPage.Definition.Id == pageId)
                    return isAvailable(buildPage) ? pageNumber + 1 : null;
                if (!isAvailable(buildPage))
                    return null;
                pageNumber += OutputPageCount(buildPage);
            }
        }

        return null;
    }

    public static int? ResolveLocalVisualizationPage(
        AlbumBuildRequest request,
        int visualizationPageIndex,
        int leadingPageCount,
        Func<AlbumBuildPage, bool>? isAvailable = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (visualizationPageIndex < 0)
            return null;

        isAvailable ??= static _ => true;
        int pageNumber = Math.Max(0, leadingPageCount);
        foreach (AlbumBuildSection section in request.Sections)
        {
            if (section.Kind == AlbumBuildSectionKind.Building &&
                section.Pages.Count > 0)
            {
                pageNumber++;
            }

            foreach (AlbumBuildPage buildPage in section.Pages)
            {
                if (!isAvailable(buildPage))
                    return null;
                pageNumber += OutputPageCount(buildPage);
            }
        }

        return pageNumber + visualizationPageIndex + 1;
    }

    public static string GeneratedComponentCode(
        AlbumCompositionItem component,
        ConceptGeneratedDocumentKind documentKind)
    {
        ArgumentNullException.ThrowIfNull(component);
        string kind = documentKind == ConceptGeneratedDocumentKind.None
            ? component.GeneratedPageKind.ToString()
            : documentKind.ToString();
        return $"generated:{component.Id}:{kind}";
    }

    private static int? ResolveUniqueComponentPage(
        IReadOnlyList<ProjectCloudAlbumComponentReference> matches,
        int zeroBasedPageOffset)
    {
        if (matches.Count != 1 || zeroBasedPageOffset < 0)
            return null;

        int[] pages = (matches[0].PageNumbers ?? [])
            .Where(page => page > 0)
            .Order()
            .ToArray();
        if (pages.Length != pages.Distinct().Count() ||
            zeroBasedPageOffset >= pages.Length)
        {
            return null;
        }

        return pages[zeroBasedPageOffset];
    }

    private static string? ResolveCanonicalSourceComponentCode(
        ProjectWorkspace workspace,
        AlbumBuildSection section,
        AlbumBuildPage buildPage)
    {
        string sourceIdentity = !string.IsNullOrWhiteSpace(buildPage.Sheet.SourceId)
            ? buildPage.Sheet.SourceId
            : buildPage.Sheet.SourceIdentity;
        ProjectDesignSource? source = StudioLegacySourceResolver.Resolve(
            workspace,
            sourceIdentity);
        if (source is null)
            return null;

        string ownerEmail = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
        string sourceKey = ProjectCloudSyncMetadata.CloudSourceKey(source);
        if (string.IsNullOrWhiteSpace(ownerEmail) ||
            string.IsNullOrWhiteSpace(sourceKey))
        {
            return null;
        }

        string sectionKey = StudioAlbumComponentIdentity.CanonicalBuildingSectionKey(
            workspace,
            section.Key);
        string code = StudioAlbumComponentIdentity.SourceSliceCode(
            ownerEmail,
            sourceKey,
            sectionKey,
            buildPage.Definition.TemplateSlotId ?? "");
        return StudioAlbumComponentIdentity.CanonicalComponentCode(
            workspace,
            code);
    }

    private static int OutputPageCount(AlbumBuildPage page) =>
        page.Sheet.Entry.PdfPageNumber > 0
            ? 1
            : Math.Max(1, page.Sheet.Entry.PageCount);
}
