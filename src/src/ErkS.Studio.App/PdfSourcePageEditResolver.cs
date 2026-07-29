using ErkS.Platform.Contracts;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal enum PdfSourcePageEditState
{
    NoSelection,
    MultipleSelection,
    NotPdf,
    Inactive,
    AlbumPageMissing,
    AmbiguousAlbumPage,
    Ready,
}

internal sealed record PdfSourcePageEditResolution(
    PdfSourcePageEditState State,
    SheetRecord? Sheet = null,
    AlbumPageDefinition? Page = null)
{
    public bool IsButtonEnabled =>
        State is PdfSourcePageEditState.Inactive
            or PdfSourcePageEditState.AlbumPageMissing
            or PdfSourcePageEditState.Ready;
}

internal static class PdfSourcePageEditResolver
{
    public static PdfSourcePageEditResolution Resolve(
        ProjectDesignSource? source,
        IReadOnlyList<SheetRecord> selectedSheets,
        IReadOnlyList<AlbumPageDefinition> albumPages)
    {
        ArgumentNullException.ThrowIfNull(selectedSheets);
        ArgumentNullException.ThrowIfNull(albumPages);

        if (selectedSheets.Count == 0)
        {
            return new PdfSourcePageEditResolution(
                PdfSourcePageEditState.NoSelection);
        }

        if (selectedSheets.Count > 1)
        {
            return new PdfSourcePageEditResolution(
                PdfSourcePageEditState.MultipleSelection);
        }

        SheetRecord sheet = selectedSheets[0];
        if (source?.Kind != DesignSourceKind.Pdf ||
            sheet.Source.Application != SheetSourceApplication.Pdf)
        {
            return new PdfSourcePageEditResolution(
                PdfSourcePageEditState.NotPdf,
                sheet);
        }

        if (!source.IsSheetActive(sheet.Entry.SheetId))
        {
            return new PdfSourcePageEditResolution(
                PdfSourcePageEditState.Inactive,
                sheet);
        }

        List<AlbumPageDefinition> matchingPages = albumPages
            .Where(candidate => string.Equals(
                candidate.SheetKey,
                sheet.Key,
                StringComparison.Ordinal))
            .Take(2)
            .ToList();
        if (matchingPages.Count > 1)
        {
            return new PdfSourcePageEditResolution(
                PdfSourcePageEditState.AmbiguousAlbumPage,
                sheet);
        }

        AlbumPageDefinition? page = matchingPages.SingleOrDefault();
        return page is null
            ? new PdfSourcePageEditResolution(
                PdfSourcePageEditState.AlbumPageMissing,
                sheet)
            : new PdfSourcePageEditResolution(
                PdfSourcePageEditState.Ready,
                sheet,
                page);
    }
}
