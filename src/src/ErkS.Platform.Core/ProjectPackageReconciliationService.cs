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

        // Keep the time-of-check/time-of-use boundary without hashing and
        // parsing every PDF twice. A changed file invalidates the cheap intake
        // snapshot and falls back to full verification before any mutation.
        SheetPackageLoadResult currentResult = result.IsVerificationCurrent()
            ? result
            : SheetPackageReader.Load(result.ManifestPath);
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

        string manifestSha256 = currentResult.ManifestSha256;
        if (string.IsNullOrWhiteSpace(manifestSha256))
        {
            using FileStream stream = File.OpenRead(currentResult.ManifestPath);
            manifestSha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }
        ProjectCloudSyncMetadata.RecordPackage(project, source, manifest, manifestSha256);

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
            List<string> staleBuildingAssignments = project.SheetBuildingAssignments.Keys
                .Where(key =>
                    SheetRecord.BelongsToSource(key, packageSource, source.Id) &&
                    !authoritativeKeys.Contains(key))
                .ToList();
            foreach (string key in staleBuildingAssignments)
                project.SheetBuildingAssignments.Remove(key);
            if (staleBuildingAssignments.Count > 0)
                ProjectCloudSyncMetadata.MarkBuildingCompositionPending(project);

            HashSet<string> authoritativeSheetIds = manifest.Sheets
                .Select(entry => entry.SheetId?.Trim() ?? "")
                .Where(sheetId => sheetId.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (string staleSheetId in (source.InactiveSheetIds ?? [])
                         .Where(sheetId => !authoritativeSheetIds.Contains(sheetId))
                         .ToList())
            {
                source.RemoveSheetActivityState(staleSheetId);
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

            if (!source.IsSheetActive(entry.SheetId))
            {
                removedAlbumPageCount += RemoveSheetFromAlbum(album, key);
                continue;
            }

            SynchronizeActiveSheet(album, entry, key, usesConceptTemplate);
        }

        bool addedBuildingAssignments =
            ProjectDesignSourceClassification.ApplyDefaultBuildingGroupAssignments(
                project,
                source,
                manifest.Sheets.Select(entry =>
                    SheetRecord.MakeKey(packageSource, entry, source.Id)));
        addedBuildingAssignments |=
            ProjectDesignSourceClassification.ApplyPackageBuildingGroupAssignments(
                project,
                source,
                packageSource,
                manifest.Sheets);
        if (addedBuildingAssignments)
            ProjectCloudSyncMetadata.MarkBuildingCompositionPending(project);

        if (usesConceptTemplate)
        {
            IReadOnlyList<AlbumPageDefinition> orderedPages =
                BuildingArchitectureConceptAlbumSequencer
                    .OrderPagesAfterSourceReconciliation(
                    album,
                    album.Pages,
                    library,
                    project.Sources,
                    source.Id,
                    project.BuildingGroups,
                    project.SheetBuildingAssignments);
            album.Pages.Clear();
            album.Pages.AddRange(orderedPages);
        }

        return new ProjectPackageReconciliationResult(source.Id, removedAlbumPageCount);
    }

    public static int SetSheetActivity(
        ProjectWorkspace project,
        AlbumDefinition album,
        SheetLibrary library,
        ProjectDesignSource source,
        IEnumerable<string> sheetIds,
        bool active)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(album);
        ArgumentNullException.ThrowIfNull(library);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(sheetIds);

        if (!project.Sources.Any(existing =>
                string.Equals(existing.Id, source.Id, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The source does not belong to the current project.");
        }

        HashSet<string> targetIds = sheetIds
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (targetIds.Count == 0)
        {
            return 0;
        }

        bool usesConceptTemplate = string.Equals(
            album.TemplateId,
            BuildingArchitectureConceptAlbumTemplate.TemplateId,
            StringComparison.OrdinalIgnoreCase);
        List<SheetRecord> targetRecords = library.Snapshot()
            .Where(record =>
                record.IsVerified &&
                targetIds.Contains(record.Entry.SheetId) &&
                (string.Equals(record.SourceId, source.Id, StringComparison.OrdinalIgnoreCase) ||
                 (source.UseLegacySheetKeys && string.IsNullOrWhiteSpace(record.SourceId))))
            .ToList();
        int removedPageCount = 0;
        foreach (string sheetId in targetIds)
        {
            List<SheetRecord> sheetRecords = targetRecords
                .Where(record =>
                    string.Equals(
                        record.Entry.SheetId,
                        sheetId,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (active)
            {
                source.SetSheetActive(sheetId, active: true);
                ProjectInactiveSourceSheetState? inactiveState =
                    source.TakeInactiveSheetState(sheetId);
                if (inactiveState is not null)
                {
                    RestoreInactiveSheetState(
                        project,
                        album,
                        inactiveState,
                        sheetRecords.FirstOrDefault()?.Key ?? inactiveState.SheetKey);
                }

                foreach (SheetRecord record in sheetRecords)
                {
                    SynchronizeActiveSheet(
                        album,
                        record.Entry,
                        record.Key,
                        usesConceptTemplate);
                }
            }
            else
            {
                if (source.IsSheetActive(sheetId))
                {
                    ProjectInactiveSourceSheetState? inactiveState =
                        CaptureInactiveSheetState(project, album, sheetId, sheetRecords);
                    if (inactiveState is not null)
                    {
                        source.StoreInactiveSheetState(inactiveState);
                    }
                }

                source.SetSheetActive(sheetId, active: false);
                foreach (SheetRecord record in sheetRecords)
                {
                    removedPageCount += RemoveSheetFromAlbum(album, record.Key);
                }
            }
        }

        if (usesConceptTemplate)
        {
            IReadOnlyList<AlbumPageDefinition> orderedPages =
                BuildingArchitectureConceptAlbumSequencer
                    .OrderPagesAfterSourceReconciliation(
                    album,
                    album.Pages,
                    library,
                    project.Sources,
                    source.Id,
                    project.BuildingGroups,
                    project.SheetBuildingAssignments);
            album.Pages.Clear();
            album.Pages.AddRange(orderedPages);
        }

        return removedPageCount;
    }

    private static void SynchronizeActiveSheet(
        AlbumDefinition album,
        SheetPackageEntry entry,
        string key,
        bool usesConceptTemplate)
    {
        List<AlbumPageDefinition> pages = album.Pages
            .Where(page => string.Equals(page.SheetKey, key, StringComparison.Ordinal))
            .ToList();
        if (usesConceptTemplate && pages.Count == 0)
        {
            var newPage = new AlbumPageDefinition { SheetKey = key };
            album.Pages.Add(newPage);
            pages.Add(newPage);
        }

        foreach (AlbumPageDefinition page in pages)
        {
            page.SourceBuildingIdSnapshot = (entry.BuildingId ?? "").Trim();
            page.SourceBuildingNameSnapshot = (entry.BuildingName ?? "").Trim();
            if (usesConceptTemplate)
            {
                AlbumCompositionItem? slot =
                    BuildingArchitectureConceptAlbumTemplate.FindSourceSlot(
                        album,
                        AlbumPageSourceMetadata.ResolveContentKind(page, entry),
                        entry.Discipline,
                        entry.Name);
                if (slot is not null)
                {
                    page.TemplateSlotId = slot.Id;
                    page.SectionId =
                        BuildingArchitectureConceptAlbumTemplate.ResolveSectionId(album, slot);
                }
            }
            PageFormatResolver.ApplySourceFormat(page, entry);
        }
    }

    private static ProjectInactiveSourceSheetState? CaptureInactiveSheetState(
        ProjectWorkspace project,
        AlbumDefinition album,
        string sheetId,
        IReadOnlyList<SheetRecord> records)
    {
        HashSet<string> keys = records
            .Select(record => record.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (keys.Count == 0)
        {
            return null;
        }

        string sheetKey = records[0].Key;
        var state = new ProjectInactiveSourceSheetState
        {
            SheetId = sheetId,
            SheetKey = sheetKey,
            BuildingGroupId = keys
                .Select(key => project.SheetBuildingAssignments.TryGetValue(key, out string? groupId)
                    ? groupId
                    : "")
                .FirstOrDefault(groupId => !string.IsNullOrWhiteSpace(groupId)) ?? "",
        };

        for (int index = 0; index < album.Pages.Count; index++)
        {
            AlbumPageDefinition page = album.Pages[index];
            if (keys.Contains(page.SheetKey))
            {
                state.AlbumPages.Add(new ProjectInactiveAlbumPageState
                {
                    AlbumIndex = index,
                    Page = page,
                });
            }
        }

        foreach (AlbumSection section in album.Sections)
        {
            for (int index = 0; index < section.SheetKeys.Count; index++)
            {
                if (keys.Contains(section.SheetKeys[index]))
                {
                    state.SectionPlacements.Add(new ProjectInactiveAlbumSectionPlacement
                    {
                        SectionId = section.Id,
                        SheetIndex = index,
                    });
                }
            }
        }

        return state;
    }

    private static void RestoreInactiveSheetState(
        ProjectWorkspace project,
        AlbumDefinition album,
        ProjectInactiveSourceSheetState state,
        string currentSheetKey)
    {
        state.Normalize();
        string restoredKey = string.IsNullOrWhiteSpace(currentSheetKey)
            ? state.SheetKey
            : currentSheetKey.Trim();
        if (restoredKey.Length == 0)
        {
            return;
        }

        foreach (ProjectInactiveAlbumPageState pageState in state.AlbumPages
                     .OrderBy(item => item.AlbumIndex))
        {
            AlbumPageDefinition page = pageState.Page;
            page.SheetKey = restoredKey;
            bool alreadyPresent = album.Pages.Any(existing =>
                existing.Id == page.Id ||
                ReferenceEquals(existing, page));
            if (!alreadyPresent)
            {
                album.Pages.Insert(
                    Math.Clamp(pageState.AlbumIndex, 0, album.Pages.Count),
                    page);
            }
        }

        foreach (ProjectInactiveAlbumSectionPlacement placement in state.SectionPlacements
                     .OrderBy(item => item.SheetIndex))
        {
            AlbumSection? section = album.Sections.FirstOrDefault(item =>
                item.Id == placement.SectionId);
            if (section is null ||
                section.SheetKeys.Contains(restoredKey, StringComparer.Ordinal))
            {
                continue;
            }

            section.SheetKeys.Insert(
                Math.Clamp(placement.SheetIndex, 0, section.SheetKeys.Count),
                restoredKey);
        }

        if (!string.IsNullOrWhiteSpace(state.BuildingGroupId))
        {
            if (!string.Equals(state.SheetKey, restoredKey, StringComparison.Ordinal))
            {
                project.SheetBuildingAssignments.Remove(state.SheetKey);
            }
            project.SheetBuildingAssignments[restoredKey] = state.BuildingGroupId;
            ProjectCloudSyncMetadata.MarkBuildingCompositionPending(project);
        }
    }

    private static int RemoveSheetFromAlbum(AlbumDefinition album, string sheetKey)
    {
        int removed = album.Pages.RemoveAll(page =>
            string.Equals(page.SheetKey, sheetKey, StringComparison.Ordinal));
        foreach (AlbumSection section in album.Sections)
        {
            section.SheetKeys.RemoveAll(key =>
                string.Equals(key, sheetKey, StringComparison.Ordinal));
        }
        return removed;
    }
}
