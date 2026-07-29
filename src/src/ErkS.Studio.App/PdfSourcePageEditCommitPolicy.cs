using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Applies an accepted PDF page edit as one atomic album-page mutation.
/// Title-block scale is metadata, but choosing an explicit value also confirms
/// the Studio sheet shown by the editor so the production PDF can render it.
/// </summary>
internal static class PdfSourcePageEditCommitPolicy
{
    public static void ApplyAcceptedEdit(
        AlbumPageDefinition page,
        SheetPackageEntry entry,
        SourcePageCropDefinition result,
        string? scaleTextOverride)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(result);

        string? normalizedScaleOverride = scaleTextOverride is null
            ? null
            : DrawingScaleText.Normalize(scaleTextOverride);
        bool scaleOverrideChanged = !string.Equals(
            page.ScaleTextOverride,
            normalizedScaleOverride,
            StringComparison.Ordinal);
        bool repairsExplicitScaleOnSourceAsIs =
            normalizedScaleOverride is not null &&
            PageFormatCatalog.Resolve(page).Kind == PageFormatKind.SourceAsIs;

        SourcePageCropDefinition acceptedCrop = result.DeepClone();
        acceptedCrop.ScalePercent = 100;
        page.SourceCrop = acceptedCrop;
        page.ScaleTextOverride = normalizedScaleOverride;

        if (PdfSourcePagePlacementGeometry.HasCompositionEdits(acceptedCrop) ||
            scaleOverrideChanged ||
            repairsExplicitScaleOnSourceAsIs)
        {
            PdfSourcePageStudioLayout.ApplyConfirmedCrop(page, entry);
        }
    }
}
