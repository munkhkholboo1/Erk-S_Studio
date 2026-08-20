using System.IO;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Keeps the physical component order of the PDF currently shown in Studio.
/// A cloud-local union contains pending local pages that are intentionally not
/// present in the last server manifest yet, so mapping it through the persisted
/// server manifest would leave those pages stuck in a false waiting state.
/// </summary>
internal sealed class StudioAlbumPreviewManifestCache
{
    private string? previewPath;
    private IReadOnlyList<ProjectCloudAlbumComponentReference> manifest = [];

    public void Record(AlbumBuildResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        previewPath = NormalizePath(result.OutputPath);
        manifest = result.Components.Select(component =>
            new ProjectCloudAlbumComponentReference
            {
                Code = component.Code,
                Label = component.Label,
                Order = component.Order,
                PageNumbers = component.PageNumbers.ToList(),
                SectionKey = component.SectionKey,
                SequenceKey = component.SequenceKey,
                Pages = component.Pages.Select(page =>
                    new ProjectCloudAlbumComponentPageReference
                    {
                        PageNumber = page.PageNumber,
                        PageKey = page.PageKey,
                        SortKey = page.SortKey,
                        SectionKey = page.SectionKey,
                        SequenceKey = page.SequenceKey,
                    }).ToList(),
            }).ToList();
    }

    public IReadOnlyList<ProjectCloudAlbumComponentReference> Resolve(
        string? currentPreviewPath,
        IEnumerable<ProjectCloudAlbumComponentReference>? sharedManifest)
    {
        string? current = NormalizePath(currentPreviewPath);
        return current is not null &&
               previewPath is not null &&
               current.Equals(previewPath, StringComparison.OrdinalIgnoreCase)
            ? manifest
            : (sharedManifest ?? []).ToList();
    }

    public void Clear()
    {
        previewPath = null;
        manifest = [];
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception exception) when (
            exception is ArgumentException or
                NotSupportedException or
                PathTooLongException)
        {
            return null;
        }
    }
}
