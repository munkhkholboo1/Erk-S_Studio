using System.Security.Cryptography;
using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

public sealed record ProjectPackageReconciliationResult(
    string SourceId,
    int RemovedAlbumPageCount);

/// <summary>
/// Applies an already verified package to project metadata and album pages.
/// Rejected input returns before mutating any project-owned state.
/// </summary>
public static class ProjectPackageReconciliationService
{
    public static ProjectPackageReconciliationResult? Apply(
        ProjectWorkspace project,
        AlbumDefinition album,
        SheetLibrary library,
        SheetPackageLoadResult result)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(album);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(result);

        if (!result.IsLossless || result.Manifest is null)
        {
            return null;
        }

        // Close the time-of-check/time-of-use gap between intake and project
        // mutation. Re-read the package before touching album/cloud metadata.
        SheetPackageLoadResult currentResult = SheetPackageReader.Load(result.ManifestPath);
        if (!currentResult.IsLossless || currentResult.Manifest is null ||
            currentResult.Manifest.PackageId != result.Manifest.PackageId)
        {
            return null;
        }

        SheetPackageManifest manifest = currentResult.Manifest;
        if (!string.IsNullOrWhiteSpace(manifest.ProjectId) &&
            !string.Equals(
                manifest.ProjectId.Trim(),
                project.ProjectId.Trim(),
                StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        SheetPackageSource packageSource = manifest.Source;
        ProjectDesignSource? source = project.Sources.FirstOrDefault(existing =>
            string.Equals(existing.Id, packageSource.SourceId, StringComparison.OrdinalIgnoreCase));
        if (source is null)
        {
            return null;
        }

        foreach (SheetPackageEntry entry in manifest.Sheets)
        {
            string key = SheetRecord.MakeKey(packageSource, entry, source.Id);
            SheetRecord? record = library.FindVerified(key);
            if (record?.PackageId != manifest.PackageId ||
                !string.Equals(record.Entry.Sha256, entry.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }
        if (manifest.PackageScope == SheetPackageScope.FullSnapshot &&
            !library.IsCurrentAuthoritativeSnapshot(manifest, source.Id))
        {
            return null;
        }

        source.Status = DesignSourceStatuses.Connected;
        source.LastPackageAtUtc = manifest.ExportedAtUtc;
        source.ApplicationVersion = packageSource.ApplicationVersion;
        if (string.IsNullOrWhiteSpace(source.NativeDocumentTitle))
        {
            source.NativeDocumentTitle = packageSource.DocumentTitle;
        }
        if (string.IsNullOrWhiteSpace(source.NativeDocumentPath))
        {
            source.NativeDocumentPath = packageSource.DocumentPath;
        }

        using (FileStream stream = File.OpenRead(currentResult.ManifestPath))
        {
            string manifestSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            ProjectCloudSyncMetadata.RecordPackage(project, source, manifest, manifestSha256);
        }

        bool usesConceptTemplate = string.Equals(
            album.TemplateId,
            BuildingArchitectureConceptAlbumTemplate.TemplateId,
            StringComparison.OrdinalIgnoreCase);
        int removedAlbumPageCount = 0;
        if (manifest.PackageScope == SheetPackageScope.FullSnapshot &&
            library.IsCurrentAuthoritativeSnapshot(manifest, source.Id))
        {
            HashSet<string> authoritativeKeys = manifest.Sheets
                .Select(entry => SheetRecord.MakeKey(packageSource, entry, source.Id))
                .ToHashSet(StringComparer.Ordinal);
            removedAlbumPageCount = album.Pages.RemoveAll(page =>
                SheetRecord.BelongsToSource(page.SheetKey, packageSource, source.Id) &&
                !authoritativeKeys.Contains(page.SheetKey));
            foreach (AlbumSection section in album.Sections)
            {
                section.SheetKeys.RemoveAll(key =>
                    SheetRecord.BelongsToSource(key, packageSource, source.Id) &&
                    !authoritativeKeys.Contains(key));
            }
        }

        foreach (SheetPackageEntry entry in manifest.Sheets)
        {
            string key = SheetRecord.MakeKey(packageSource, entry, source.Id);
            SheetRecord? currentRecord = library.FindVerified(key);
            if (currentRecord?.PackageId != manifest.PackageId)
            {
                continue;
            }

            List<AlbumPageDefinition> pages = album.Pages
                .Where(page => string.Equals(page.SheetKey, key, StringComparison.Ordinal))
                .ToList();
            if (usesConceptTemplate && pages.Count == 0)
            {
                var newPage = new AlbumPageDefinition { SheetKey = key };
                album.Pages.Add(newPage);
                pages.Add(newPage);
            }

            AlbumCompositionItem? slot = usesConceptTemplate
                ? BuildingArchitectureConceptAlbumTemplate.FindSourceSlot(album, entry)
                : null;
            foreach (AlbumPageDefinition page in pages)
            {
                if (usesConceptTemplate)
                {
                    page.TemplateSlotId = slot?.Id ?? "";
                    page.SectionId = BuildingArchitectureConceptAlbumTemplate.ResolveSectionId(album, slot);
                }
                PageFormatResolver.ApplySourceFormat(page, entry);
            }
        }

        if (usesConceptTemplate)
        {
            IReadOnlyList<AlbumPageDefinition> orderedPages =
                BuildingArchitectureConceptAlbumSequencer.OrderPages(
                    album,
                    album.Pages,
                    library,
                    project.Sources);
            album.Pages.Clear();
            album.Pages.AddRange(orderedPages);
        }

        return new ProjectPackageReconciliationResult(source.Id, removedAlbumPageCount);
    }
}
