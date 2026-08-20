using ErkS.Platform.Core;

namespace ErkS.Studio;

public sealed record StudioAlbumCompositionProgress(
    int ReadyRequired,
    int RequiredCount,
    int ReadyOptional,
    int OptionalCount)
{
    public string Summary => OptionalCount > 0
        ? $"Үндсэн бүрдэл {ReadyRequired}/{RequiredCount} · Нэмэлт {ReadyOptional}/{OptionalCount}"
        : $"Бүрдэл {ReadyRequired}/{RequiredCount}";

    public static StudioAlbumCompositionProgress Resolve(
        AlbumDefinition album,
        int visualizationImageCount)
    {
        ArgumentNullException.ThrowIfNull(album);
        bool IsReady(AlbumCompositionItem item) =>
            item.Kind == AlbumCompositionKind.Generated ||
            (item.Id.Equals("visualizations", StringComparison.OrdinalIgnoreCase) &&
             visualizationImageCount > 0) ||
            album.Pages.Any(page => string.Equals(
                page.TemplateSlotId,
                item.Id,
                StringComparison.OrdinalIgnoreCase));

        List<AlbumCompositionItem> required = album.Composition
            .Where(item => item.Required)
            .ToList();
        List<AlbumCompositionItem> optional = album.Composition
            .Where(item => !item.Required)
            .ToList();
        return new StudioAlbumCompositionProgress(
            required.Count(IsReady),
            required.Count,
            optional.Count(IsReady),
            optional.Count);
    }
}
