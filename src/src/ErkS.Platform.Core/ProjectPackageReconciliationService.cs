using System.Security.Cryptography;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core.ProjectTypes.UrbanPlanning;

namespace ErkS.Platform.Core;

public sealed record ProjectPackageReconciliationResult(
    string SourceId,
    int RemovedAlbumPageCount,
    IReadOnlyList<string> StudioComposedPagesSkipped);

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

        // A Portfolio entry is presentation material: it never enters the
        // sheet library, the album, or any album composition, and only Album
        // entries count as a full snapshot's authoritative sheet set.
        List<SheetPackageEntry> albumSheets = manifest.Sheets
            .Where(entry => !SheetDestinations.IsPortfolio(entry.Destination))
            .ToList();
        foreach (SheetPackageEntry entry in albumSheets)
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
        bool usesUrbanPlanningTemplate = UrbanPlanningAlbumTemplate.Supports(
            project.Identity.ProjectType,
            project.Identity.StageCode);
        bool usesWorkingDrawingTemplate = BuildingWorkingDrawingAlbumTemplate.Supports(
            project.Identity.ProjectType,
            project.Identity.StageCode);
        int removedAlbumPageCount = 0;
        var studioComposedSkipped = new List<string>();
        if (manifest.PackageScope == SheetPackageScope.FullSnapshot &&
            library.IsCurrentAuthoritativeSnapshot(manifest, source.Id))
        {
            HashSet<string> authoritativeKeys = albumSheets
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

            HashSet<string> authoritativeSheetIds = albumSheets
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

        foreach (SheetPackageEntry entry in albumSheets)
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

            AddIfAny(studioComposedSkipped, SynchronizeActiveSheet(
                album,
                entry,
                key,
                usesConceptTemplate,
                usesUrbanPlanningTemplate,
                usesWorkingDrawingTemplate));
        }

        // A source is recognised as a building only here, once its package is read, so it
        // is given a building group before its sheets are filed. The package's own
        // per-sheet building identity is more specific than the source default and is
        // applied first.
        //
        // Only the concept album may gain a group this way. It alone builds a section per
        // building (AlbumBuilder.IsBuildingSectionKey) and so is the only album whose writer
        // draws a building sub-cover; anywhere else a group would demand a cover that is
        // never rendered, which is what blocked the project's sync. The working-drawing
        // album composes disciplines, not buildings, and the urban-planning album has no
        // building types at all - engineering infrastructure arrives there as its own source.
        bool composesBuildings = usesConceptTemplate;
        bool addedBuildingAssignments =
            composesBuildings &&
            ProjectDesignSourceClassification.EnsureBuildingGroupForSource(
                project,
                source);
        addedBuildingAssignments |=
            ProjectDesignSourceClassification.ApplyPackageBuildingGroupAssignments(
                project,
                source,
                packageSource,
                albumSheets,
                composesBuildings);
        addedBuildingAssignments |=
            ProjectDesignSourceClassification.ApplyDefaultBuildingGroupAssignments(
                project,
                source,
                albumSheets.Select(entry =>
                    SheetRecord.MakeKey(packageSource, entry, source.Id)));
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
        else if (usesUrbanPlanningTemplate)
        {
            OrderUrbanPlanningPages(album, albumSheets, packageSource, source.Id);
        }

        return new ProjectPackageReconciliationResult(
            source.Id,
            removedAlbumPageCount,
            studioComposedSkipped);
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
        bool usesUrbanPlanningTemplate = UrbanPlanningAlbumTemplate.Supports(
            project.Identity.ProjectType,
            project.Identity.StageCode);
        bool usesWorkingDrawingTemplate = BuildingWorkingDrawingAlbumTemplate.Supports(
            project.Identity.ProjectType,
            project.Identity.StageCode);
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
                    // Re-activating a sheet can hit the same collision, and the
                    // guard has to hold here too. The message is dropped: this
                    // method answers with a count, and a package's problems are
                    // reported where the package arrives. Written as a discard
                    // rather than left implicit, because a silently absent
                    // album page is the thing being prevented.
                    _ = SynchronizeActiveSheet(
                        album,
                        record.Entry,
                        record.Key,
                        usesConceptTemplate,
                        usesUrbanPlanningTemplate,
                        usesWorkingDrawingTemplate);
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
        else if (usesUrbanPlanningTemplate)
        {
            OrderUrbanPlanningPages(
                album,
                targetRecords.Select(record => new KeyValuePair<string, int>(
                    record.Key,
                    record.SourceSheetIndex)));
        }

        return removedPageCount;
    }

    private static string? SynchronizeActiveSheet(
        AlbumDefinition album,
        SheetPackageEntry entry,
        string key,
        bool usesConceptTemplate,
        bool usesUrbanPlanningTemplate,
        bool usesWorkingDrawingTemplate)
    {
        List<AlbumPageDefinition> pages = album.Pages
            .Where(page => string.Equals(page.SheetKey, key, StringComparison.Ordinal))
            .ToList();

        // A sheet that stands in for a page Studio composes gets no album page.
        // It keeps its place in the library and the source list; what it must
        // not do is print beside the page it duplicates. Checked before the
        // page is created rather than after, so there is nothing to undo.
        if (StudioComposedPageCollision.Find(album, entry) is { } composed)
        {
            foreach (AlbumPageDefinition duplicate in pages)
                album.Pages.Remove(duplicate);
            return StudioComposedPageCollision.Describe(entry, composed);
        }

        if ((usesConceptTemplate || usesUrbanPlanningTemplate || usesWorkingDrawingTemplate) && pages.Count == 0)
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
            else if (usesUrbanPlanningTemplate)
            {
                AlbumCompositionItem? slot = UrbanPlanningAlbumTemplate.FindSourceSlot(
                    album,
                    entry.Number,
                    AlbumPageSourceMetadata.ResolveContentKind(page, entry),
                    entry.Name,
                    entry.TemplateSlotId);
                if (slot is not null)
                {
                    page.TemplateSlotId = slot.Id;
                    page.SectionId = album.Sections
                        .FirstOrDefault(section =>
                            section.Title.Equals(slot.SectionTitle, StringComparison.OrdinalIgnoreCase))
                        ?.Id;
                }
            }
            else if (usesWorkingDrawingTemplate)
            {
                AlbumCompositionItem? slot =
                    BuildingWorkingDrawingAlbumTemplate.FindSourceSlot(album);
                if (slot is not null)
                {
                    page.TemplateSlotId = slot.Id;
                    page.SectionId = album.Sections
                        .FirstOrDefault(section => section.Title.Equals(
                            slot.SectionTitle,
                            StringComparison.OrdinalIgnoreCase))
                        ?.Id;
                }
            }
            PageFormatResolver.ApplySourceFormat(
                page,
                entry,
                forceStudioChrome: usesWorkingDrawingTemplate);
        }

        return null;
    }

    /// <summary>Keeps the two call sites above from repeating a null check.</summary>
    private static void AddIfAny(List<string> messages, string? message)
    {
        if (!string.IsNullOrWhiteSpace(message))
            messages.Add(message);
    }

    private static void OrderUrbanPlanningPages(
        AlbumDefinition album,
        IReadOnlyList<SheetPackageEntry> albumSheets,
        SheetPackageSource packageSource,
        string projectSourceId)
    {
        OrderUrbanPlanningPages(
            album,
            albumSheets
            .Select((entry, index) => new
            {
                Key = SheetRecord.MakeKey(packageSource, entry, projectSourceId),
                Index = index,
            })
            .Select(item => new KeyValuePair<string, int>(item.Key, item.Index)));
    }

    private static void OrderUrbanPlanningPages(
        AlbumDefinition album,
        IEnumerable<KeyValuePair<string, int>> orderedSourceSheets)
    {
        Dictionary<string, int> sourceOrder = orderedSourceSheets
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Min(item => item.Value), StringComparer.Ordinal);
        Dictionary<string, int> originalOrder = album.Pages
            .Select((page, index) => new { page.Id, Index = index })
            .ToDictionary(item => item.Id.ToString("N"), item => item.Index, StringComparer.Ordinal);

        List<AlbumPageDefinition> ordered = album.Pages
            .OrderBy(page => sourceOrder.ContainsKey(page.SheetKey) ? 0 : 1)
            .ThenBy(page => sourceOrder.TryGetValue(page.SheetKey, out int order) ? order : int.MaxValue)
            .ThenBy(page => originalOrder[page.Id.ToString("N")])
            .ToList();
        album.Pages.Clear();
        album.Pages.AddRange(ordered);
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
