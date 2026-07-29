using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

/// <summary>
/// Keeps a cropped PDF page and its Studio destination in sync. The source
/// document stays untouched: only the album page records its Studio frame and
/// placement policy.
/// </summary>
public static class PdfSourcePageStudioLayout
{
    public static PageFormatDefinition ResolvePreviewFormat(
        AlbumPageDefinition page,
        SheetPackageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(entry);

        PageFormatDefinition configured =
            PageFormatCatalog.ResolveForConceptPage(page, entry);
        if (configured.Kind != PageFormatKind.SourceAsIs)
            return configured;

        PageFormatDefinition inferred =
            PdfSourcePageFormatFactory.CreateForSource(entry.WidthMm, entry.HeightMm);
        return BuildingArchitectureConceptPageLayout.UsesInformationHeader(
            AlbumPageSourceMetadata.ResolveContentKind(page, entry),
            entry.Name,
            page.TemplateSlotId)
            ? BuildingArchitectureConceptPageLayout.ApplyElevationGeometry(inferred)
            : inferred;
    }

    /// <summary>
    /// Applies the Studio page used by the editor after a crop is confirmed.
    /// Cropped drawing content keeps its 1:1 physical size while its offset
    /// remains available for visual drag placement.
    /// </summary>
    public static void ApplyConfirmedCrop(
        AlbumPageDefinition page,
        SheetPackageEntry entry)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(entry);

        PageFormatDefinition format = ResolvePreviewFormat(page, entry);
        if (PageFormatCatalog.Resolve(page).Kind == PageFormatKind.SourceAsIs)
        {
            page.PageFormatId = format.Id;
            page.PageFormatSnapshot = format;
        }

        page.FollowSourceFormat = false;
        page.PlacementMode = PagePlacementMode.PreservePhysicalSize;
    }
}
