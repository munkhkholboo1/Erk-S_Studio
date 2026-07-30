using System.IO;
using System.Security.Cryptography;
using System.Text;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private AlbumBuildResult BuildAlbumContributionSnapshot(string workFolder)
    {
        AlbumProject buildProject = state.CreateAlbumBuildProject(
            reconcileLinkedProjectAssets: false);
        string ownerEmail = CurrentCloudOwnerEmail();
        PlanningTaskInformation planningTask = buildProject.PlanningTask;
        buildProject.PlanningTask = new PlanningTaskInformation
        {
            AtdNumber = planningTask.AtdNumber,
            IssuedAtUtc = planningTask.IssuedAtUtc,
            IssuingAuthorityName = planningTask.IssuingAuthorityName,
            Status = planningTask.Status,
            Summary = planningTask.Summary,
            Requirements = planningTask.Requirements.ToList(),
            Documents = planningTask.Documents
                .Where(document => IsDocumentOwnedBy(document, ownerEmail) &&
                    !document.IsCloudPlaceholder)
                .Select(document => document.Clone())
                .ToList(),
            ServerDocumentId = planningTask.ServerDocumentId,
            ServerDocumentVersion = planningTask.ServerDocumentVersion,
            DocumentCloudSyncStatus = planningTask.DocumentCloudSyncStatus,
            AuthorityMembers = planningTask.AuthorityMembers.ToList(),
        };
        HashSet<string> availableSheetKeys = state.Library.Snapshot()
            .Select(sheet => sheet.Key)
            .ToHashSet(StringComparer.Ordinal);
        AlbumDefinition album = buildProject.Album;
        buildProject.Album = new AlbumDefinition
        {
            Title = album.Title,
            TemplateId = album.TemplateId,
            IncludeCover = album.IncludeCover,
            IncludeTableOfContents = album.IncludeTableOfContents,
            Composition = album.Composition.ToList(),
            Sections = album.Sections.Select(section => new AlbumSection
            {
                Id = section.Id,
                Title = section.Title,
                SheetKeys = section.SheetKeys
                    .Where(availableSheetKeys.Contains)
                    .ToList(),
            }).ToList(),
            Pages = album.Pages
                .Where(page => availableSheetKeys.Contains(page.SheetKey))
                .ToList(),
        };

        Directory.CreateDirectory(workFolder);
        return state.Builder.Build(
            buildProject,
            state.Library,
            Path.Combine(workFolder, "contribution-snapshot.pdf"));
    }

    private bool TryDeferSourceRefreshAlbumBuild(
        StudioWorkspaceOperation operation,
        AlbumBuildException buildException,
        string? statusPrefix,
        out Exception? localValidationFailure)
    {
        localValidationFailure = null;
        IEnumerable<string> albumSheetKeys = state.Album.Pages.Count > 0
            ? state.Album.Pages.Select(page => page.SheetKey)
            : state.Album.Sections.SelectMany(section => section.SheetKeys);
        IReadOnlyList<ProjectDesignSource> localSources =
            StudioSourceRefreshScope.OwnedSources(
                state.Project,
                account.Current?.Email,
                StudioDeviceIdentity.Fingerprint);
        IEnumerable<string> knownCloudSourceIdentities =
            state.Project.Sources.Select(source => source.Id)
                .Concat((state.Project.Cloud.SharedSources ?? [])
                    .SelectMany(source => new[]
                    {
                        source.SourceKey,
                        source.SourceId,
                    }));
        bool cloudLinked =
            state.Project.Cloud.Origin.Equals(
                ProjectOrigins.Cloud,
                StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId);
        StudioSourceRefreshAlbumResolution resolution =
            StudioSourceRefreshAlbumPolicy.Resolve(
                operation,
                cloudLinked,
                HasCurrentCloudAlbumPreview(),
                albumSheetKeys,
                state.Library.VerifiedSnapshot().Select(sheet => sheet.Key),
                localSources.Select(source => source.Id),
                knownCloudSourceIdentities,
                buildException.Issues);
        if (!resolution.ShouldDefer)
            return false;

        if (!TryValidateLocalAlbumContribution(
                "source-refresh-validation",
                out AlbumBuildResult localBuild,
                out localValidationFailure))
        {
            return false;
        }

        string? currentPreview = ResolveCurrentProjectAlbumPath();
        string previewMessage =
            !string.IsNullOrWhiteSpace(currentPreview) && File.Exists(currentPreview)
                ? "Одоогийн canonical album preview хэвээр үлдлээ."
                : "Canonical album preview энэ төхөөрөмжид хараахан татагдаагүй.";
        string deferredMessage =
            operation == StudioWorkspaceOperation.LocalPdfPageEdit
                ? $"PDF хуудасны засвар болон локал component баталгаажиж хадгалагдлаа: {localBuild.SheetCount} sheet. " +
                  $"{resolution.UnavailableCloudSheetKeys.Count} cloud-only sheet-ийн локал PDF байхгүй тул " +
                  $"canonical merge Cloud Sync хүртэл pending хэвээр үлдлээ. {previewMessage} " +
                  $"[reason: {resolution.ReasonCode}]"
                : $"Локал contribution баталгаажиж хадгалагдлаа: {localBuild.SheetCount} sheet. " +
                  $"{resolution.UnavailableCloudSheetKeys.Count} cloud-only sheet-ийн локал PDF байхгүй тул " +
                  $"album rebuild Cloud Sync хүртэл хойшлогдлоо. {previewMessage} " +
                  $"[reason: {resolution.ReasonCode}]";
        SetStatus(string.IsNullOrWhiteSpace(statusPrefix)
            ? deferredMessage
            : $"{statusPrefix}. {deferredMessage}");
        return true;
    }

    private bool TryDeferLocalPdfPageEditWithoutCanonical(
        StudioPdfPageEditAlbumRouteDecision route,
        string? statusPrefix,
        out Exception? localValidationFailure)
    {
        if (!TryValidateLocalAlbumContribution(
                "pdf-page-edit-validation",
                out AlbumBuildResult localBuild,
                out localValidationFailure))
        {
            return false;
        }

        string? currentPreview = ResolveCurrentProjectAlbumPath();
        string previewMessage =
            !string.IsNullOrWhiteSpace(currentPreview) && File.Exists(currentPreview)
                ? "Одоогийн canonical album preview болон Cloud-only component-үүд хэвээр үлдлээ."
                : "Canonical album preview энэ төхөөрөмжид хараахан татагдаагүй.";
        string deferredMessage =
            $"PDF хуудасны засвар болон локал component баталгаажиж хадгалагдлаа: {localBuild.SheetCount} sheet. " +
            $"{route.CloudOnlyComponentCount} Cloud-only component локал payload-гүй, usable canonical manifest байхгүй тул " +
            $"full-local partial album үүсгээгүй. {previewMessage} Cloud Sync canonical merge-ийг дуусгана. " +
            "[reason: pdf_page_edit_cloud_album_deferred]";
        SetStatus(string.IsNullOrWhiteSpace(statusPrefix)
            ? deferredMessage
            : $"{statusPrefix}. {deferredMessage}");
        return true;
    }

    private bool TryValidateLocalAlbumContribution(
        string validationPurpose,
        out AlbumBuildResult localBuild,
        out Exception? validationFailure)
    {
        string validationFolder = Path.Combine(
            state.ResolveOutputFolder(),
            "cloud-local",
            validationPurpose,
            Guid.NewGuid().ToString("N"));
        localBuild = null!;
        validationFailure = null;
        try
        {
            // Build only locally verified pages so missing collaborator payloads
            // cannot hide a corrupt local PDF or replace the canonical preview.
            localBuild = BuildAlbumContributionSnapshot(validationFolder);
            state.SaveProject();
            return true;
        }
        catch (Exception exception)
        {
            validationFailure = exception;
            return false;
        }
        finally
        {
            try
            {
                if (Directory.Exists(validationFolder))
                    Directory.Delete(validationFolder, recursive: true);
            }
            catch (IOException)
            {
                // Validation output is disposable cache; cleanup is best effort.
            }
            catch (UnauthorizedAccessException)
            {
                // Validation succeeded, so cleanup must not turn it into failure.
            }
        }
    }

    private List<StudioCloudAlbumSection> CreateCanonicalComponentManifest(
        AlbumBuildResult build,
        IReadOnlyList<StudioCloudSourcePackage> activeServerSources,
        IReadOnlyList<StudioCloudAlbumSection>? existingManifest = null)
    {
        string ownerEmail = CurrentCloudOwnerEmail();
        bool hasOwnedAtd = HasOwnedAtdDocuments(ownerEmail);
        bool hasVisualizations = HasLocalVisualizationImages();
        Dictionary<string, int> sourceOrder = activeServerSources
            .Where(source => !string.IsNullOrWhiteSpace(source.SourceKey) &&
                !string.IsNullOrWhiteSpace(source.RegisteredBy))
            .Select(source => new
            {
                Source = source,
                Code = StudioAlbumComponentIdentity.SourceCode(source.RegisteredBy, source.SourceKey),
            })
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(item => item.Source.RegisteredAtUtc)
                .ThenBy(item => item.Source.SourceId, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(source => source.Source.RegisteredAtUtc)
            .ThenBy(source => source.Code, StringComparer.OrdinalIgnoreCase)
            .Select((source, index) => new { source.Code, Index = index })
            .ToDictionary(item => item.Code, item => item.Index, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, StudioCloudAlbumSection> existingByCode = (existingManifest ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .GroupBy(
                item => StudioAlbumComponentIdentity.CanonicalComponentCode(
                    state.Project,
                    item.Code),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var merged = new Dictionary<string, StudioCloudAlbumSection>(StringComparer.OrdinalIgnoreCase);
        foreach (AlbumBuildComponent component in build.Components)
        {
            AlbumComponentIdentity identity = CanonicalAlbumComponentIdentity(
                component,
                ownerEmail,
                hasOwnedAtd,
                hasVisualizations,
                existingByCode);
            string code = identity.Code;
            existingByCode.TryGetValue(code, out StudioCloudAlbumSection? existing);
            int order = StudioAlbumComponentOrderPolicy.Resolve(
                state.Project,
                identity.Code,
                identity.SourceKey,
                component.Order,
                sourceOrder);
            if (!merged.TryGetValue(code, out StudioCloudAlbumSection? section))
            {
                section = new StudioCloudAlbumSection
                {
                    Code = code,
                    Label = existing?.Label ?? component.Label,
                    Order = order,
                    Status = "Available",
                    OwnerEmail = identity.OwnerEmail,
                    SourceKey = identity.SourceKey,
                    ComponentKind = identity.ComponentKind,
                };
                merged.Add(code, section);
            }
            section.PageNumbers = section.PageNumbers
                .Concat(component.PageNumbers)
                .Distinct()
                .Order()
                .ToArray();
            section.Order = Math.Min(section.Order, order);
        }

        List<StudioCloudAlbumSection> manifest = merged.Values
            .OrderBy(item => item.Order)
            .ThenBy(item => item.PageNumbers.FirstOrDefault())
            .ToList();
        int[] pages = manifest.SelectMany(item => item.PageNumbers).Order().ToArray();
        if (!pages.SequenceEqual(Enumerable.Range(1, build.PageCount)))
            throw new InvalidDataException("Rendered album component manifest does not cover every page exactly once.");
        return manifest;
    }

    private AlbumComponentIdentity CanonicalAlbumComponentIdentity(
        AlbumBuildComponent component,
        string ownerEmail,
        bool hasOwnedAtd,
        bool hasVisualizations,
        IReadOnlyDictionary<string, StudioCloudAlbumSection> existingByCode)
    {
        const string sourcePrefix = "source:";
        string normalized = component.Code.Trim();
        if (ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode(normalized))
        {
            return AlbumComponentIdentity.Generated(
                StudioAlbumComponentIdentity.CanonicalComponentCode(
                    state.Project,
                    normalized));
        }
        string canonicalSectionKey =
            StudioAlbumComponentIdentity.CanonicalBuildingSectionKey(
                state.Project,
                component.SectionKey);
        if (normalized.Equals(ProjectCloudSyncMetadata.ApprovedAtdComponentCode, StringComparison.OrdinalIgnoreCase) &&
            hasOwnedAtd)
        {
            return AlbumComponentIdentity.Source(ownerEmail, StudioAlbumComponentIdentity.AtdSourceKey);
        }
        if (normalized.Equals(ProjectCloudSyncMetadata.VisualizationsComponentCode, StringComparison.OrdinalIgnoreCase) &&
            hasVisualizations)
        {
            return AlbumComponentIdentity.Source(ownerEmail, StudioAlbumComponentIdentity.VisualizationSourceKey);
        }
        if (normalized.Equals(
                ProjectCloudSyncMetadata.SiteContextComponentCode,
                StringComparison.OrdinalIgnoreCase))
        {
            ProjectSiteContextEditAuthority authority =
                ProjectSiteContextEditingPolicy.Resolve(state.Project, ownerEmail);
            if (authority.CanEdit &&
                !string.IsNullOrWhiteSpace(authority.SourceKey))
            {
                return AlbumComponentIdentity.SiteContext(
                    string.IsNullOrWhiteSpace(authority.SourceOwnerEmail)
                        ? ownerEmail
                        : authority.SourceOwnerEmail,
                    authority.SourceKey);
            }
            if (existingByCode.TryGetValue(
                    ProjectCloudSyncMetadata.SiteContextComponentCode,
                    out StudioCloudAlbumSection? existingSiteContext) &&
                !string.IsNullOrWhiteSpace(existingSiteContext.SourceKey))
            {
                return AlbumComponentIdentity.SiteContext(
                    existingSiteContext.OwnerEmail,
                    existingSiteContext.SourceKey);
            }
            return AlbumComponentIdentity.Generated(normalized);
        }
        if (!normalized.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            return AlbumComponentIdentity.Generated(normalized);
        if (StudioAlbumComponentIdentity.IsOwnedSourceCode(normalized) &&
            existingByCode.TryGetValue(normalized, out StudioCloudAlbumSection? existing))
        {
            return new AlbumComponentIdentity(
                normalized,
                existing.OwnerEmail,
                existing.SourceKey,
                StudioAlbumComponentIdentity.SourceComponentKind);
        }

        string localIdentity = !string.IsNullOrWhiteSpace(component.SourceIdentity)
            ? component.SourceIdentity.Trim()
            : StudioAlbumComponentIdentity.BaseSourceCode(normalized)[sourcePrefix.Length..].Trim();
        if (StudioAlbumComponentIdentity.TryResolveExistingSource(
                normalized,
                localIdentity,
                existingByCode.Values,
                out StudioCloudAlbumSection? existingSource) &&
            existingSource is not null)
        {
            return AlbumComponentIdentity.Source(
                existingSource.OwnerEmail,
                existingSource.SourceKey,
                canonicalSectionKey,
                component.SequenceKey);
        }

        ProjectDesignSource? source =
            StudioLegacySourceResolver.Resolve(
                state.Project,
                localIdentity);
        if (source is null)
            return AlbumComponentIdentity.Generated(normalized);
        string sourceOwner = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
        return AlbumComponentIdentity.Source(
            string.IsNullOrWhiteSpace(sourceOwner) ? ownerEmail : sourceOwner,
            ProjectCloudSyncMetadata.CloudSourceKey(source),
            canonicalSectionKey,
            component.SequenceKey);
    }

    private bool TryBuildCloudUnionAlbumPreview(
        out AlbumBuildResult result,
        bool collectUi = true)
    {
        result = null!;
        if (!TryGetCachedCanonicalAlbum(out string canonicalPdfPath, out StudioCloudAlbumRevision revision))
            return false;

        if (collectUi)
            CollectUiToProject();
        if (!TryBuildCloudUnionAlbumPreview(
                canonicalPdfPath,
                revision,
                out result))
        {
            return false;
        }

        state.SaveProject();
        if (activePage == StudioPage.Albums)
            RefreshAlbumWorkspace(selectItemKey: selectedAlbumWorkspaceKey);
        RefreshSyncUi();
        return true;
    }

    private bool TryBuildCloudUnionAlbumPreview(
        string canonicalPdfPath,
        StudioCloudAlbumRevision revision,
        out AlbumBuildResult result)
    {
        result = null!;
        if (!state.HasOpenProject ||
            string.IsNullOrWhiteSpace(state.ProjectPath) ||
            !account.IsSignedIn ||
            !File.Exists(canonicalPdfPath) ||
            !HasCompleteComponentManifest(revision))
        {
            return false;
        }

        IReadOnlyList<string> rendererMigrationCodes = PrepareAlbumRendererMigration(revision);
        StudioCloudUnionPendingScope pendingScope =
            StudioCloudUnionPreviewScope.Resolve(
                state.Project,
                account.Current?.Email,
                StudioDeviceIdentity.Fingerprint,
                hasVerifiedDocumentPayload: document =>
                    StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(
                        state.ProjectPath!,
                        document),
                hasVerifiedVisualizationPayload: image =>
                    StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(
                        state.ProjectPath!,
                        image));
        IReadOnlyList<ProjectSourceSyncCandidate> pendingSources =
            pendingScope.Sources;
        IReadOnlyList<string> rawPendingComponents =
            pendingScope.ComponentCodes;
        string ownerEmail = CurrentCloudOwnerEmail();
        Dictionary<string, string> pendingCodeMap = rawPendingComponents
            .ToDictionary(
                code => code,
                code => CanonicalPendingComponentCode(code, ownerEmail),
                StringComparer.OrdinalIgnoreCase);
        HashSet<string> requestedCodes = pendingCodeMap.Values
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (pendingSources.Count == 0 && requestedCodes.Count == 0)
        {
            result = PointPrimaryAlbumAtCanonical(canonicalPdfPath, revision);
            return true;
        }

        string workRoot = Path.Combine(
            state.ResolveOutputFolder(),
            "cloud-local",
            "component-build");
        string workFolder = Path.Combine(workRoot, Guid.NewGuid().ToString("N"));
        try
        {
            AlbumBuildResult localBuild = BuildAlbumContributionSnapshot(workFolder);
            List<StudioCloudSourcePackage> activeServerSources = SharedCloudSources();
            List<StudioCloudAlbumSection> rendered = CreateCanonicalComponentManifest(
                localBuild,
                activeServerSources,
                revision.SectionManifest);
            List<StudioCloudAlbumSection> selected = rendered
                .Where(component =>
                    MatchesAnyPendingSource(component, pendingSources, ownerEmail) ||
                    requestedCodes.Any(code =>
                        MatchesRequestedComponentCode(component, code)))
                .ToList();
            StudioBuildingSubCoverSelection coverSelection =
                StudioBuildingSubCoverSelectionPolicy.IncludeRequiredCovers(
                    state.Project,
                    rendered,
                    selected);
            if (coverSelection.MissingRequiredCoverCodes.Count > 0)
            {
                throw new InvalidDataException(
                    "Барилгын source хуудас render хийгдсэн боловч шаардлагатай дэд нүүр үүссэнгүй: " +
                    string.Join(", ", coverSelection.MissingRequiredCoverCodes) +
                    " [reason: building_subcover_render_missing]");
            }
            selected = coverSelection.Components.ToList();
            string[] missing = requestedCodes
                .Where(code => selected.All(component =>
                    !MatchesRequestedComponentCode(component, code)))
                .ToArray();
            StudioMissingAlbumComponentResolution missingResolution =
                StudioAlbumComponentAcknowledgementPolicy.ResolveMissingComponents(
                    missing,
                    IsCurrentBuildingSubCover);
            string[] unrenderedSourcesWithSheets = pendingSources
                .Where(source =>
                    source.SheetCount > 0 &&
                    selected.All(component =>
                        !MatchesPendingSource(component, source, ownerEmail)))
                .Select(source => source.SourceKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (unrenderedSourcesWithSheets.Length > 0)
            {
                throw new InvalidDataException(
                    "Pending source has sheets but its album component could not be rendered locally: " +
                    string.Join(", ", unrenderedSourcesWithSheets));
            }
            string[] unrenderedRendererMigrations = missing
                .Where(code => rendererMigrationCodes.Contains(
                    code,
                    StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (unrenderedRendererMigrations.Length > 0)
            {
                throw new InvalidDataException(
                    "A locally owned album component requires a renderer upgrade but could not be rendered. " +
                    "The existing Cloud component was preserved: " +
                    string.Join(", ", unrenderedRendererMigrations));
            }

            var patches = new List<AlbumComponentPdfPatch>();
            for (int index = 0; index < selected.Count; index++)
            {
                StudioCloudAlbumSection component = selected[index];
                string componentPdfPath = Path.Combine(workFolder, $"component-{index:D2}.pdf");
                AlbumComponentPdfExtractor.Extract(
                    localBuild.OutputPath,
                    component.PageNumbers,
                    componentPdfPath);
                patches.Add(new AlbumComponentPdfPatch(
                    component.Code,
                    component.Order,
                    componentPdfPath));
            }

            Dictionary<string, StudioCloudAlbumSection> currentByCode = revision.SectionManifest
                .GroupBy(component => component.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(component => component.Order)
                        .ThenBy(component => component.PageNumbers.FirstOrDefault())
                        .First(),
                    StringComparer.OrdinalIgnoreCase);
            AddLegacyComponentMigrationPatches(patches, selected, currentByCode);
            AddCanonicalAliasMigrationPatches(
                patches,
                selected,
                revision.SectionManifest);
            foreach (string code in missingResolution.RemovalCodes)
            {
                if (!currentByCode.TryGetValue(code, out StudioCloudAlbumSection? current))
                    continue;
                patches.Add(new AlbumComponentPdfPatch(
                    code,
                    current.Order,
                    "",
                    Remove: true));
            }
            AddStaleSourceComponentRemovalPatches(
                patches,
                selected,
                currentByCode.Values,
                pendingSources,
                ownerEmail);

            string previewFolder = Path.Combine(state.ResolveOutputFolder(), "cloud-local");
            string outputPath = Path.Combine(
                previewFolder,
                $"{SafeFileName(state.Project.PrimaryAlbum.Title)}-working-{Guid.NewGuid():N}.pdf");
            AlbumComponentPdfCompositionResult composition = AlbumComponentPdfComposer.Compose(
                canonicalPdfPath,
                revision.PageCount,
                revision.SectionManifest.Select(component => new AlbumComponentPdfSlot(
                    component.Code,
                    component.Order,
                    component.PageNumbers)).ToList(),
                patches,
                outputPath);

            CanonicalTitleBlockPreview canonicalPreview =
                PrepareCanonicalTitleBlockPreview(outputPath, composition.Components);
            outputPath = canonicalPreview.Path;
            string relativePath = ProjectWorkspacePaths.ToRelativePath(state.ProjectPath, outputPath);
            string sha256 = canonicalPreview.Sha256;
            ProjectAlbumRecord album = state.Project.PrimaryAlbum;
            album.LastPdfPath = relativePath;
            album.LastPdfSha256 = sha256;
            album.LastPageCount = canonicalPreview.PageCount;
            album.LastPageSizeSummary = revision.PageSizeSummary?.Trim() ?? "";
            lastAlbumPath = outputPath;

            result = new AlbumBuildResult
            {
                OutputPath = outputPath,
                SheetCount = localBuild.SheetCount,
                PageCount = canonicalPreview.PageCount,
            };
            result.Warnings.AddRange(localBuild.Warnings);
            if (missingResolution.DeferredCodes.Count > 0)
            {
                result.Warnings.Add(
                    "Энэ төхөөрөмж дээр render хийгдээгүй барилгын дэд нүүрийг " +
                    "Cloud хувилбараас устгалгүй, pending хэвээр хадгаллаа: " +
                    string.Join(", ", missingResolution.DeferredCodes));
            }
            Dictionary<string, StudioCloudAlbumSection> renderedByCode = rendered
                .ToDictionary(component => component.Code, StringComparer.OrdinalIgnoreCase);
            foreach (AlbumComponentPdfSlot component in composition.Components)
            {
                StudioCloudAlbumSection? source = renderedByCode.GetValueOrDefault(component.Code) ??
                    currentByCode.GetValueOrDefault(component.Code);
                result.Components.Add(new AlbumBuildComponent
                {
                    Code = component.Code,
                    Label = source?.Label ?? component.Code,
                    Order = component.Order,
                    PageNumbers = component.PageNumbers.ToList(),
                });
            }

            if (rendererMigrationCodes.Count > 0)
                MarkAlbumRendererCurrent();

            CloudAlbumCacheMaintenance.Cleanup(previewFolder, outputPath);
            return true;
        }
        finally
        {
            if (ProjectWorkspacePaths.IsInside(workRoot, workFolder) && Directory.Exists(workFolder))
            {
                try
                {
                    Directory.Delete(workFolder, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private IReadOnlyList<string> PrepareAlbumRendererMigration(
        StudioCloudAlbumRevision revision)
    {
        if (state.Project.PrimaryAlbum.RendererRevision >=
            StudioAlbumRendererMigration.CurrentRevision)
        {
            return [];
        }

        string ownerEmail = CurrentCloudOwnerEmail();
        var manifest = (revision.SectionManifest ?? [])
            .Select(component => new ProjectCloudAlbumComponentReference
            {
                Code = component.Code ?? "",
                Label = component.Label ?? "",
                Order = component.Order,
                PageNumbers = (component.PageNumbers ?? []).ToList(),
                Status = component.Status ?? "",
                OwnerEmail = component.OwnerEmail ?? "",
                SourceKey = component.SourceKey ?? "",
                ComponentKind = component.ComponentKind ?? "",
            })
            .ToList();
        bool hasVisualizations = HasLocalVisualizationImages();
        IReadOnlyList<string> rawCodes =
            StudioAlbumRendererMigration.SelectLocallyRenderableComponents(
                state.Project,
                manifest,
                ownerEmail,
                HasOwnedAtdDocuments(ownerEmail),
                hasVisualizations);
        if (!ProjectSiteContextEditingPolicy.Resolve(state.Project, ownerEmail).CanEdit)
        {
            rawCodes = rawCodes
                .Where(code => !code.Equals(
                    ProjectCloudSyncMetadata.SiteContextComponentCode,
                    StringComparison.OrdinalIgnoreCase))
                .ToList();
        }
        if (rawCodes.Count == 0)
        {
            MarkAlbumRendererCurrent();
            return [];
        }

        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(state.Project, rawCodes);
        return rawCodes
            .Select(code => CanonicalPendingComponentCode(code, ownerEmail))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private void MarkAlbumRendererCurrent()
    {
        state.Project.PrimaryAlbum.RendererRevision =
            StudioAlbumRendererMigration.CurrentRevision;
    }

    private bool TryGetCachedCanonicalAlbum(
        out string canonicalPdfPath,
        out StudioCloudAlbumRevision revision)
    {
        canonicalPdfPath = ResolveLastReceivedCloudAlbumPath() ?? "";
        ProjectCloudLink cloud = state.Project.Cloud;
        List<StudioCloudAlbumSection> components = (cloud.SharedAlbumComponents ?? [])
            .Where(component => !string.IsNullOrWhiteSpace(component.Code))
            .Select(component => new StudioCloudAlbumSection
            {
                Code = component.Code,
                Label = component.Label,
                Order = component.Order,
                PageNumbers = (component.PageNumbers ?? []).ToArray(),
                Status = component.Status,
                OwnerEmail = component.OwnerEmail,
                SourceKey = component.SourceKey,
                ComponentKind = component.ComponentKind,
            })
            .ToList();
        int pageCount = components
            .SelectMany(component => component.PageNumbers)
            .DefaultIfEmpty(0)
            .Max();
        revision = new StudioCloudAlbumRevision
        {
            RevisionId = cloud.LastReceivedAlbumRevisionId,
            RevisionNumber = cloud.LastReceivedAlbumRevisionNumber,
            PdfSha256 = cloud.LastReceivedAlbumSha256,
            PageCount = pageCount,
            PageSizeSummary = state.Project.PrimaryAlbum.LastPageSizeSummary,
            SectionManifest = components,
        };
        bool hasVerifiedServerRevision =
            !string.IsNullOrWhiteSpace(canonicalPdfPath) &&
            File.Exists(canonicalPdfPath) &&
            HasCompleteComponentManifest(revision);
        StudioCanonicalAlbumPreviewDecision previewDecision =
            StudioCanonicalAlbumPreviewPolicy.Resolve(
                StudioCanonicalAlbumRebuildPolicy.ResolvePersisted(state.Project),
                hasVerifiedServerRevision);
        if (!previewDecision.CanDisplay)
        {
            return false;
        }

        return TryNormalizeCachedCanonicalAlbum(ref canonicalPdfPath, revision);
    }

    private bool TryNormalizeCachedCanonicalAlbum(
        ref string canonicalPdfPath,
        StudioCloudAlbumRevision revision)
    {
        List<StudioCloudAlbumSection> original = revision.SectionManifest
            .Select(CloneAlbumSection)
            .ToList();
        Dictionary<string, int> sourceOrder = SharedCloudSources()
            .Where(source =>
                string.IsNullOrWhiteSpace(source.Status) ||
                source.Status.Equals("Registered", StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                source => StudioAlbumComponentIdentity.SourceCode(
                    source.RegisteredBy,
                    source.SourceKey),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(source => source.RegisteredAtUtc)
                .ThenBy(source => source.SourceId, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(source => source.RegisteredAtUtc)
            .ThenBy(source => source.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(source => source.RegisteredBy, StringComparer.OrdinalIgnoreCase)
            .Select((source, index) => new
            {
                Code = StudioAlbumComponentIdentity.SourceCode(
                    source.RegisteredBy,
                    source.SourceKey),
                Index = index,
            })
            .ToDictionary(item => item.Code, item => item.Index, StringComparer.OrdinalIgnoreCase);

        StudioAlbumComponentManifestNormalizationPlan plan =
            StudioAlbumComponentManifestNormalizer.CreatePlan(
                state.Project,
                original,
                sourceOrder);
        List<StudioCloudAlbumSection> targetManifest = plan.TargetManifest
            .Select(CloneAlbumSection)
            .ToList();
        int targetPageCount = targetManifest
            .SelectMany(component => component.PageNumbers)
            .DefaultIfEmpty(0)
            .Max();
        if (!plan.RequiresPdfRewrite)
        {
            revision.SectionManifest = targetManifest;
            revision.PageCount = targetPageCount;
            return true;
        }

        string signatureText = string.Join(
            "\n",
            plan.OriginalSlots
                .OrderBy(component => component.Code, StringComparer.OrdinalIgnoreCase)
                .Select(component =>
                    $"{component.Code}|{component.Order}|{string.Join(",", component.PageNumbers)}")
                .Concat(plan.TargetManifest
                    .OrderBy(component => component.Code, StringComparer.OrdinalIgnoreCase)
                    .Select(component =>
                        $"target:{component.Code}|{component.Order}|" +
                        $"{string.Join(",", component.PageNumbers)}"))
                .Prepend(revision.RevisionId)
                .Prepend(CleanSha256(revision.PdfSha256))
                .Prepend("canonical-album-order-v3"));
        string signature = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(signatureText)))[..16].ToLowerInvariant();
        string canonicalFolder = Path.Combine(
            state.ResolveOutputFolder(),
            "cloud-local",
            "canonical");
        Directory.CreateDirectory(canonicalFolder);
        string outputPath = Path.Combine(
            canonicalFolder,
            $"{SafeFileName(state.Project.PrimaryAlbum.Title)}-R" +
            $"{Math.Max(0, revision.RevisionNumber)}-{signature}.pdf");
        string hashPath = outputPath + ".sha256";

        if (!File.Exists(outputPath))
        {
            string temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp.pdf";
            try
            {
                Dictionary<string, StudioCloudAlbumSection> targetByCode =
                    plan.TargetManifest.ToDictionary(
                        component => component.Code,
                        StringComparer.OrdinalIgnoreCase);
                List<AlbumComponentPdfPatch> removals = plan.RemovedCodes
                    .Select(code => new AlbumComponentPdfPatch(
                        code,
                        0,
                        "",
                        Remove: true))
                    .ToList();
                AlbumComponentPdfCompositionResult composition =
                    AlbumComponentPdfComposer.Compose(
                        canonicalPdfPath,
                        plan.OriginalPageCount,
                        plan.OriginalSlots.Select(component => new AlbumComponentPdfSlot(
                            component.Code,
                            plan.CanonicalCodeByRetainedCode.TryGetValue(
                                    component.Code,
                                    out string? canonicalCode)
                                ? targetByCode[canonicalCode].Order
                                : component.Order,
                            component.PageNumbers)).ToList(),
                        removals,
                        temporaryPath);
                if (composition.PageCount != targetPageCount ||
                    composition.Components.Count != plan.TargetManifest.Count ||
                    composition.Components.Any(component =>
                    {
                        if (!plan.CanonicalCodeByRetainedCode.TryGetValue(
                                component.Code,
                                out string? canonicalCode))
                        {
                            return true;
                        }
                        StudioCloudAlbumSection expected = targetByCode[canonicalCode];
                        return component.Order != expected.Order ||
                            !component.PageNumbers.SequenceEqual(expected.PageNumbers);
                    }))
                {
                    throw new InvalidDataException(
                        "Canonical album reorder did not match the expected component manifest.");
                }

                File.Move(temporaryPath, outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        if (!File.Exists(outputPath))
            throw new FileNotFoundException("Canonical album reorder output was not created.", outputPath);

        string outputSha256 = ReadCachedSha256(hashPath);
        if (string.IsNullOrWhiteSpace(outputSha256))
        {
            outputSha256 = ComputeFileSha256(outputPath);
            File.WriteAllText(hashPath, outputSha256, Encoding.ASCII);
        }
        revision.SectionManifest = targetManifest;
        revision.PageCount = targetPageCount;
        revision.PdfSha256 = outputSha256;
        canonicalPdfPath = outputPath;
        CloudAlbumCacheMaintenance.Cleanup(canonicalFolder, outputPath);
        return true;
    }

    private static StudioCloudAlbumSection CloneAlbumSection(
        StudioCloudAlbumSection component) => new()
    {
        Code = component.Code,
        Label = component.Label,
        Order = component.Order,
        PageNumbers = (component.PageNumbers ?? []).ToArray(),
        Status = component.Status,
        OwnerEmail = component.OwnerEmail,
        SourceKey = component.SourceKey,
        ComponentKind = component.ComponentKind,
    };

    private static string ReadCachedSha256(string path)
    {
        if (!File.Exists(path))
            return "";
        string value = File.ReadAllText(path).Trim().ToLowerInvariant();
        return value.Length == 64 && value.All(Uri.IsHexDigit) ? value : "";
    }

    private List<StudioCloudSourcePackage> SharedCloudSources() =>
        StudioSharedSourceProjection
            .Create(state.Project.Cloud.SharedSources ?? [])
            .ToList();

    private AlbumBuildResult PointPrimaryAlbumAtCanonical(
        string canonicalPdfPath,
        StudioCloudAlbumRevision revision)
    {
        List<AlbumComponentPdfSlot> components = revision.SectionManifest
            .Select(component => new AlbumComponentPdfSlot(
                component.Code,
                component.Order,
                component.PageNumbers))
            .ToList();
        CanonicalTitleBlockPreview canonicalPreview =
            PrepareCanonicalTitleBlockPreview(canonicalPdfPath, components);
        string previewPath = canonicalPreview.Path;
        string relativePath = ProjectWorkspacePaths.ToRelativePath(
            state.ProjectPath!,
            previewPath);
        ProjectAlbumRecord album = state.Project.PrimaryAlbum;
        album.LastPdfPath = relativePath;
        album.LastPdfSha256 = canonicalPreview.Sha256;
        album.LastPageCount = canonicalPreview.PageCount;
        album.LastPageSizeSummary = revision.PageSizeSummary?.Trim() ?? "";
        lastAlbumPath = previewPath;
        var result = new AlbumBuildResult
        {
            OutputPath = previewPath,
            SheetCount = state.Library.Snapshot().Count,
            PageCount = canonicalPreview.PageCount,
        };
        result.Components.AddRange(revision.SectionManifest.Select(component => new AlbumBuildComponent
        {
            Code = component.Code,
            Label = component.Label,
            Order = component.Order,
            PageNumbers = component.PageNumbers.ToList(),
        }));
        return result;
    }

    private CanonicalTitleBlockPreview PrepareCanonicalTitleBlockPreview(
        string inputPdfPath,
        IReadOnlyList<AlbumComponentPdfSlot> components)
    {
        AlbumProject canonicalProject = state.CreateAlbumBuildProject(
            reconcileLinkedProjectAssets: false);
        string inputSha256 = ComputeFileSha256(inputPdfPath);
        string signature =
            PdfSharpAlbumWriter.ComputeCanonicalTitleBlockSignature(canonicalProject);
        string cacheFolder = Path.Combine(
            state.ResolveOutputFolder(),
            "cloud-local",
            "titleblock");
        Directory.CreateDirectory(cacheFolder);
        string outputPath = Path.Combine(
            cacheFolder,
            $"canonical-{inputSha256[..16]}-{signature[..16]}.pdf");
        if (!File.Exists(outputPath))
        {
            string temporaryPath = outputPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                PdfSharpAlbumWriter.RestampCanonicalTitleBlocks(
                    inputPdfPath,
                    canonicalProject,
                    components,
                    temporaryPath);
                File.Move(temporaryPath, outputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (IOException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        int pageCount;
        using (var document = PdfSharp.Pdf.IO.PdfReader.Open(
                   outputPath,
                   PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
        {
            pageCount = document.PageCount;
        }
        string outputSha256 = ComputeFileSha256(outputPath);
        CloudAlbumCacheMaintenance.Cleanup(cacheFolder, outputPath);
        return new CanonicalTitleBlockPreview(
            outputPath,
            outputSha256,
            signature,
            pageCount);
    }

    private async Task<CanonicalTitleBlockPublicationOutcome>
        PublishCanonicalTitleBlockRevisionAsync(
            string projectId,
            string albumId,
            StudioCloudAlbumRevision startingRevision)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(albumId);
        ArgumentNullException.ThrowIfNull(startingRevision);

        string root = Path.Combine(
            state.ResolveOutputFolder(),
            "cloud",
            "titleblock-publish");
        string workFolder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workFolder);
        StudioCloudAlbumRevision candidate = startingRevision;
        try
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                StudioCloudProjectDetail canonical =
                    await account.GetProjectAsync(projectId);
                state.LinkCurrentProjectToCloud(
                    canonical,
                    account.Current!.ServerUrl,
                    account.Current.Email,
                    preserveCreation: true,
                    preserveSyncState: true);
                await ApplyCloudProjectRenderProfileAsync(canonical);

                IReadOnlyList<StudioCloudAlbum> albums =
                    await account.ListAlbumsAsync(projectId);
                StudioCloudAlbum album = albums.FirstOrDefault(item =>
                        item.AlbumId.Equals(albumId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException(
                        "Canonical Cloud album disappeared while its title block was being updated.");
                candidate = CurrentCloudAlbumRevision(album)
                    ?? throw new InvalidDataException(
                        "Canonical Cloud album has no current revision.");
                if (!HasCompleteComponentManifest(candidate))
                {
                    throw new InvalidDataException(
                        "Canonical Cloud album has no complete component manifest. " +
                        "Its shared pages cannot be restamped safely.");
                }

                AlbumProject canonicalProject = state.CreateAlbumBuildProject(
                    reconcileLinkedProjectAssets: false);
                string signature =
                    PdfSharpAlbumWriter.ComputeCanonicalTitleBlockSignature(
                        canonicalProject);
                string inputPath = Path.Combine(
                    workFolder,
                    $"canonical-r{candidate.RevisionNumber:D4}-{attempt}.pdf");
                await account.DownloadAlbumRevisionPdfAsync(candidate, inputPath);
                string downloadedHash = ComputeFileSha256(inputPath);
                if (!string.IsNullOrWhiteSpace(candidate.PdfSha256) &&
                    !downloadedHash.Equals(
                        candidate.PdfSha256,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        "Downloaded canonical album hash did not match the current Cloud revision.");
                }

                if (PdfSharpAlbumWriter.HasCanonicalTitleBlockSignature(
                        inputPath,
                        signature))
                {
                    return new CanonicalTitleBlockPublicationOutcome(
                        candidate,
                        signature,
                        Uploaded: false);
                }

                string outputPath = Path.Combine(
                    workFolder,
                    $"canonical-titleblock-{attempt}.pdf");
                List<AlbumComponentPdfSlot> components = candidate.SectionManifest
                    .Select(component => new AlbumComponentPdfSlot(
                        component.Code,
                        component.Order,
                        component.PageNumbers))
                    .ToList();
                PdfSharpAlbumWriter.RestampCanonicalTitleBlocks(
                    inputPath,
                    canonicalProject,
                    components,
                    outputPath);

                // Recheck both metadata and album revision immediately before
                // upload. A collaborator may have merged another component
                // while this device was repainting the canonical cells.
                StudioCloudProjectDetail gate =
                    await account.GetProjectAsync(projectId);
                IReadOnlyList<StudioCloudAlbum> gateAlbums =
                    await account.ListAlbumsAsync(projectId);
                StudioCloudAlbum? gateAlbum = gateAlbums.FirstOrDefault(item =>
                    item.AlbumId.Equals(albumId, StringComparison.OrdinalIgnoreCase));
                StudioCloudAlbumRevision? gateRevision =
                    gateAlbum is null ? null : CurrentCloudAlbumRevision(gateAlbum);
                if (gateRevision is null ||
                    !gateRevision.RevisionId.Equals(
                        candidate.RevisionId,
                        StringComparison.OrdinalIgnoreCase) ||
                    !gate.Project.ConcurrencyToken.Equals(
                        canonical.Project.ConcurrencyToken,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                StudioCloudAlbumRevision uploaded;
                try
                {
                    // The base revision check and manifest inheritance happen
                    // inside revision creation, so no manifestless revision is
                    // exposed between two requests.
                    uploaded = await account.UploadAlbumRevisionAsync(
                        projectId,
                        albumId,
                        outputPath,
                        candidate.PageCount,
                        candidate.PageSizeSummary,
                        gate.Project.ConcurrencyToken,
                        expectedBaseRevisionId: candidate.RevisionId,
                        inheritComponentManifest: true);
                }
                catch (StudioAccountException exception) when (
                    exception.StatusCode is System.Net.HttpStatusCode.Conflict or
                        System.Net.HttpStatusCode.PreconditionFailed)
                {
                    continue;
                }

                IReadOnlyList<StudioCloudAlbum> confirmedAlbums =
                    await account.ListAlbumsAsync(projectId);
                StudioCloudAlbum? confirmedAlbum = confirmedAlbums.FirstOrDefault(item =>
                    item.AlbumId.Equals(albumId, StringComparison.OrdinalIgnoreCase));
                StudioCloudAlbumRevision? confirmed = confirmedAlbum is null
                    ? null
                    : CurrentCloudAlbumRevision(confirmedAlbum);
                if (confirmed is not null &&
                    confirmed.RevisionId.Equals(
                        uploaded.RevisionId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new CanonicalTitleBlockPublicationOutcome(
                        uploaded,
                        signature,
                        Uploaded: true);
                }
            }

            throw new InvalidOperationException(
                "Cloud album changed repeatedly while its canonical title block was being updated. " +
                "No collaborator page was overwritten; run Sync again.");
        }
        finally
        {
            if (ProjectWorkspacePaths.IsInside(root, workFolder) &&
                Directory.Exists(workFolder))
            {
                try
                {
                    Directory.Delete(workFolder, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private void AddLegacyComponentMigrationPatches(
        ICollection<AlbumComponentPdfPatch> patches,
        IReadOnlyList<StudioCloudAlbumSection> selected,
        IReadOnlyDictionary<string, StudioCloudAlbumSection> currentByCode)
    {
        foreach (StudioCloudAlbumSection component in selected.Where(item =>
                     item.ComponentKind.Equals(
                         StudioAlbumComponentIdentity.SourceComponentKind,
                         StringComparison.OrdinalIgnoreCase)))
        {
            string legacyCode = component.SourceKey switch
            {
                StudioAlbumComponentIdentity.AtdSourceKey => ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                StudioAlbumComponentIdentity.VisualizationSourceKey => ProjectCloudSyncMetadata.VisualizationsComponentCode,
                _ => "source:" + component.SourceKey,
            };
            if (!currentByCode.TryGetValue(legacyCode, out StudioCloudAlbumSection? legacy) ||
                patches.Any(item => item.Code.Equals(legacyCode, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            if (!component.SourceKey.Equals(
                    StudioAlbumComponentIdentity.AtdSourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                !component.SourceKey.Equals(
                    StudioAlbumComponentIdentity.VisualizationSourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                !StudioLegacySourceResolver.CanRetireUnqualifiedComponent(
                    state.Project,
                    component.SourceKey,
                    component.OwnerEmail))
            {
                // source:<SourceKey> did not encode an immutable owner.
                // Preserve it when more than one contributor can own the key.
                continue;
            }
            patches.Add(new AlbumComponentPdfPatch(
                legacy.Code,
                legacy.Order,
                "",
                Remove: true));
        }
    }

    private void AddCanonicalAliasMigrationPatches(
        ICollection<AlbumComponentPdfPatch> patches,
        IReadOnlyList<StudioCloudAlbumSection> selected,
        IEnumerable<StudioCloudAlbumSection> current)
    {
        HashSet<string> selectedCodes = selected
            .Select(component => component.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (StudioCloudAlbumSection component in current)
        {
            string canonicalCode = StudioAlbumComponentIdentity.CanonicalComponentCode(
                state.Project,
                component.Code);
            if (canonicalCode.Equals(component.Code, StringComparison.OrdinalIgnoreCase) ||
                !selectedCodes.Contains(canonicalCode) ||
                patches.Any(patch => patch.Code.Equals(
                    component.Code,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            patches.Add(new AlbumComponentPdfPatch(
                component.Code,
                component.Order,
                "",
                Remove: true));
        }
    }

    private async Task<AlbumComponentMergeOutcome> MergePendingAlbumComponentsAsync(
        string projectId,
        StudioCloudAlbum serverAlbum,
        StudioCloudAlbumRevision currentRevision,
        string projectConcurrencyToken,
        IReadOnlyList<ProjectSourceSyncCandidate> pendingSources,
        IReadOnlyList<string> rawPendingComponents,
        IReadOnlyList<StudioCloudSourcePackage> activeServerSources,
        IReadOnlyList<string> rendererMigrationCodes)
    {
        if (!HasCompleteComponentManifest(currentRevision))
        {
            throw new InvalidOperationException(
                "The current Cloud album has no complete component manifest. " +
                "A device with the complete album must Sync once before collaborators can add components.");
        }

        string ownerEmail = CurrentCloudOwnerEmail();
        Dictionary<string, string> pendingCodeMap = rawPendingComponents
            .ToDictionary(
                code => code,
                code => CanonicalPendingComponentCode(code, ownerEmail),
                StringComparer.OrdinalIgnoreCase);
        HashSet<string> requestedCodes = pendingCodeMap.Values
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (pendingSources.Count == 0 && requestedCodes.Count == 0)
            return new AlbumComponentMergeOutcome(currentRevision, 0, [], []);

        string root = Path.Combine(state.ResolveOutputFolder(), "cloud", "component-sync");
        string workFolder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        try
        {
            AlbumBuildResult build = BuildAlbumContributionSnapshot(workFolder);
            List<StudioCloudAlbumSection> rendered = CreateCanonicalComponentManifest(
                build,
                activeServerSources,
                currentRevision.SectionManifest);
            List<StudioCloudAlbumSection> selected = rendered
                .Where(component =>
                    MatchesAnyPendingSource(component, pendingSources, ownerEmail) ||
                    requestedCodes.Any(code =>
                        MatchesRequestedComponentCode(component, code)))
                .ToList();
            StudioBuildingSubCoverSelection coverSelection =
                StudioBuildingSubCoverSelectionPolicy.IncludeRequiredCovers(
                    state.Project,
                    rendered,
                    selected);
            if (coverSelection.MissingRequiredCoverCodes.Count > 0)
            {
                throw new InvalidDataException(
                    "Барилгын source хуудас render хийгдсэн боловч шаардлагатай дэд нүүр үүссэнгүй: " +
                    string.Join(", ", coverSelection.MissingRequiredCoverCodes) +
                    " [reason: building_subcover_render_missing]");
            }
            selected = coverSelection.Components.ToList();
            string[] missing = requestedCodes
                .Where(code => selected.All(component =>
                    !MatchesRequestedComponentCode(component, code)))
                .ToArray();
            StudioMissingAlbumComponentResolution missingResolution =
                StudioAlbumComponentAcknowledgementPolicy.ResolveMissingComponents(
                    missing,
                    IsCurrentBuildingSubCover);
            string[] unrenderedSourcesWithSheets = pendingSources
                .Where(source =>
                    source.SheetCount > 0 &&
                    selected.All(component =>
                        !MatchesPendingSource(component, source, ownerEmail)))
                .Select(source => source.SourceKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (unrenderedSourcesWithSheets.Length > 0)
            {
                throw new InvalidDataException(
                    "Pending source has sheets but its album component could not be rendered locally: " +
                    string.Join(", ", unrenderedSourcesWithSheets));
            }
            string[] unrenderedRendererMigrations = missing
                .Where(code => rendererMigrationCodes.Contains(
                    code,
                    StringComparer.OrdinalIgnoreCase))
                .ToArray();
            if (unrenderedRendererMigrations.Length > 0)
            {
                throw new InvalidDataException(
                    "A locally owned album component requires a renderer upgrade but could not be rendered. " +
                    "The existing Cloud component was preserved: " +
                    string.Join(", ", unrenderedRendererMigrations));
            }

            var uploads = new List<StudioAlbumComponentUpload>();
            for (int index = 0; index < selected.Count; index++)
            {
                StudioCloudAlbumSection component = selected[index];
                string outputPath = Path.Combine(workFolder, $"component-{index:D2}.pdf");
                AlbumComponentPdfExtractor.Extract(
                    build.OutputPath,
                    component.PageNumbers,
                    outputPath);
                uploads.Add(new StudioAlbumComponentUpload(
                    component.Code,
                    component.Label,
                    component.Order,
                    outputPath,
                    SourceKey: component.SourceKey,
                    ComponentKind: component.ComponentKind));
            }

            Dictionary<string, StudioCloudAlbumSection> currentByCode = currentRevision.SectionManifest
                .GroupBy(component => component.Code, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .OrderBy(component => component.Order)
                        .ThenBy(component => component.PageNumbers.FirstOrDefault())
                        .First(),
                    StringComparer.OrdinalIgnoreCase);
            AddLegacyComponentMigrationRemovals(uploads, selected, currentByCode);
            AddCanonicalAliasMigrationRemovals(
                uploads,
                selected,
                currentRevision.SectionManifest);
            foreach (string code in missingResolution.RemovalCodes)
            {
                if (!currentByCode.TryGetValue(code, out StudioCloudAlbumSection? current))
                    continue;
                uploads.Add(new StudioAlbumComponentUpload(
                    code,
                    current.Label,
                    current.Order,
                    "",
                    Remove: true,
                    SourceKey: current.SourceKey,
                    ComponentKind: current.ComponentKind));
            }
            foreach (StudioCloudAlbumSection current in
                     StudioAlbumComponentRemovalPlanner.FindMissingSourceComponents(
                         currentByCode.Values,
                         missingResolution.RemovalCodes))
            {
                if (uploads.Any(upload => upload.Code.Equals(
                        current.Code,
                        StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                uploads.Add(new StudioAlbumComponentUpload(
                    current.Code,
                    current.Label,
                    current.Order,
                    "",
                    Remove: true,
                    SourceKey: current.SourceKey,
                    ComponentKind: current.ComponentKind));
            }
            AddStaleSourceComponentRemovalUploads(
                uploads,
                selected,
                currentByCode.Values,
                pendingSources,
                ownerEmail);
            StudioCanonicalAlbumRebuildResolution canonicalRebuild =
                StudioCanonicalAlbumRebuildPolicy.ResolvePersisted(
                    state.Project);
            uploads = StudioCanonicalAlbumRebuildPolicy.ApplyTombstoneUploads(
                    canonicalRebuild,
                    currentRevision.SectionManifest,
                    uploads)
                .ToList();

            StudioCloudAlbumRevision merged = uploads.Count == 0
                ? currentRevision
                : await account.MergeAlbumComponentsAsync(
                    projectId,
                    serverAlbum.AlbumId,
                    currentRevision.RevisionId,
                    projectConcurrencyToken,
                    uploads);
            IReadOnlyList<string> confirmedPendingCodes =
                StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
                    pendingCodeMap,
                    merged.SectionManifest,
                    uploads,
                    missingResolution.DeferredCodes);
            return new AlbumComponentMergeOutcome(
                merged,
                uploads.Count,
                confirmedPendingCodes,
                missingResolution.DeferredCodes);
        }
        finally
        {
            if (ProjectWorkspacePaths.IsInside(root, workFolder) && Directory.Exists(workFolder))
            {
                try
                {
                    Directory.Delete(workFolder, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private async Task<StudioCloudAlbumRevision?> TryBootstrapAlbumComponentManifestAsync(
        string projectId,
        StudioCloudAlbum serverAlbum,
        StudioCloudAlbumRevision currentRevision,
        IReadOnlyList<StudioCloudSourcePackage> activeServerSources)
    {
        if (HasCompleteComponentManifest(currentRevision))
            return currentRevision;

        if (StudioAlbumComponentIdentity.HasNoAssignedPages(
                currentRevision.SectionManifest) &&
            currentRevision.PageCount > 0)
        {
            return await account.SetAlbumComponentManifestAsync(
                projectId,
                serverAlbum.AlbumId,
                currentRevision.RevisionId,
                state.Project.Cloud.ServerSnapshot.ConcurrencyToken,
                currentRevision.RevisionId,
                [
                    StudioAlbumComponentIdentity.CreateLegacySnapshotSection(
                        currentRevision.PageCount),
                ]);
        }

        string root = Path.Combine(state.ResolveOutputFolder(), "cloud", "component-bootstrap");
        string workFolder = Path.Combine(root, Guid.NewGuid().ToString("N"));
        try
        {
            AlbumBuildResult build = BuildAlbumContributionSnapshot(workFolder);
            string renderedHash = ComputeFileSha256(build.OutputPath);
            if (build.PageCount != currentRevision.PageCount ||
                !renderedHash.Equals(currentRevision.PdfSha256, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            List<StudioCloudAlbumSection> manifest = CreateCanonicalComponentManifest(
                build,
                activeServerSources,
                currentRevision.SectionManifest);
            return await account.SetAlbumComponentManifestAsync(
                projectId,
                serverAlbum.AlbumId,
                currentRevision.RevisionId,
                state.Project.Cloud.ServerSnapshot.ConcurrencyToken,
                currentRevision.RevisionId,
                manifest);
        }
        finally
        {
            if (ProjectWorkspacePaths.IsInside(root, workFolder) && Directory.Exists(workFolder))
            {
                try
                {
                    Directory.Delete(workFolder, recursive: true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static bool HasCompleteComponentManifest(StudioCloudAlbumRevision revision)
        => StudioAlbumComponentIdentity.IsMergeReady(
            revision.SectionManifest ?? [],
            revision.PageCount);

    private static bool ComponentManifestsEqual(
        IReadOnlyList<StudioCloudAlbumSection> left,
        IReadOnlyList<StudioCloudAlbumSection> right)
    {
        if (left.Count != right.Count ||
            left.Select(item => item.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != left.Count ||
            right.Select(item => item.Code)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != right.Count)
        {
            return false;
        }

        Dictionary<string, StudioCloudAlbumSection> rightByCode = right
            .ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
        return left.All(item =>
            rightByCode.TryGetValue(item.Code, out StudioCloudAlbumSection? other) &&
            item.Order == other.Order &&
            item.Label.Equals(other.Label, StringComparison.Ordinal) &&
            item.OwnerEmail.Equals(other.OwnerEmail, StringComparison.OrdinalIgnoreCase) &&
            item.SourceKey.Equals(other.SourceKey, StringComparison.OrdinalIgnoreCase) &&
            item.ComponentKind.Equals(other.ComponentKind, StringComparison.OrdinalIgnoreCase) &&
            item.PageNumbers.SequenceEqual(other.PageNumbers));
    }

    private string CanonicalPendingComponentCode(string code, string ownerEmail)
    {
        string normalized = (code ?? "").Trim();
        if (normalized.Equals(ProjectCloudSyncMetadata.ApprovedAtdComponentCode, StringComparison.OrdinalIgnoreCase))
            return StudioAlbumComponentIdentity.SourceCode(ownerEmail, StudioAlbumComponentIdentity.AtdSourceKey);
        if (normalized.Equals(ProjectCloudSyncMetadata.VisualizationsComponentCode, StringComparison.OrdinalIgnoreCase))
            return StudioAlbumComponentIdentity.SourceCode(ownerEmail, StudioAlbumComponentIdentity.VisualizationSourceKey);
        normalized = StudioAlbumComponentIdentity.CanonicalComponentCode(
            state.Project,
            normalized);
        if (StudioAlbumComponentIdentity.IsOwnedSourceCode(normalized))
            return normalized;
        if (normalized.StartsWith("source:", StringComparison.OrdinalIgnoreCase))
        {
            string identity = normalized["source:".Length..].Trim();
            ProjectDesignSource? source =
                StudioLegacySourceResolver.Resolve(
                    state.Project,
                    identity);
            if (source is not null)
            {
                string sourceOwner =
                    ProjectCloudSyncMetadata.CloudOwnerEmail(source);
                if (string.IsNullOrWhiteSpace(sourceOwner))
                    sourceOwner = ownerEmail;
                return StudioAlbumComponentIdentity.SourceCode(
                    sourceOwner,
                    ProjectCloudSyncMetadata.CloudSourceKey(source));
            }
        }
        return normalized;
    }

    private bool IsCurrentBuildingSubCover(string componentCode)
    {
        string code = StudioAlbumComponentIdentity.CanonicalBuildingSubCoverCode(
            state.Project,
            componentCode);
        return state.Project.BuildingGroups.Any(group =>
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(group)
                .Equals(code, StringComparison.OrdinalIgnoreCase));
    }

    private static bool MatchesRequestedComponentCode(
        StudioCloudAlbumSection component,
        string requestedCode)
    {
        string requested = (requestedCode ?? "").Trim();
        if (component.Code.Equals(requested, StringComparison.OrdinalIgnoreCase))
            return true;
        if (!IsSourceComponent(component) ||
            !StudioAlbumComponentIdentity.IsOwnedSourceCode(requested) ||
            StudioAlbumComponentIdentity.TryGetSourceSlice(
                requested,
                out _,
                out _))
        {
            return false;
        }

        return StudioAlbumComponentIdentity.BaseSourceCode(component.Code)
            .Equals(
                StudioAlbumComponentIdentity.BaseSourceCode(requested),
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesAnyPendingSource(
        StudioCloudAlbumSection component,
        IReadOnlyList<ProjectSourceSyncCandidate> pendingSources,
        string currentOwnerEmail) =>
        pendingSources.Any(source =>
            MatchesPendingSource(component, source, currentOwnerEmail));

    private static bool MatchesPendingSource(
        StudioCloudAlbumSection component,
        ProjectSourceSyncCandidate source,
        string currentOwnerEmail)
    {
        if (!IsSourceComponent(component))
            return false;

        string sourceOwner = ProjectCloudSyncMetadata.CloudOwnerEmail(source.Source);
        if (string.IsNullOrWhiteSpace(sourceOwner))
            sourceOwner = (currentOwnerEmail ?? "").Trim().ToLowerInvariant();
        string expectedBaseCode = StudioAlbumComponentIdentity.SourceCode(
            sourceOwner,
            source.SourceKey);

        if (StudioAlbumComponentIdentity.IsOwnedSourceCode(component.Code))
        {
            return StudioAlbumComponentIdentity.BaseSourceCode(component.Code)
                .Equals(expectedBaseCode, StringComparison.OrdinalIgnoreCase);
        }

        return component.SourceKey.Equals(
                source.SourceKey,
                StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrWhiteSpace(component.OwnerEmail) ||
             component.OwnerEmail.Equals(
                 sourceOwner,
                 StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSourceComponent(StudioCloudAlbumSection component) =>
        StudioAlbumComponentIdentity.IsSourceComponent(component);

    private static IEnumerable<StudioCloudAlbumSection> StaleSourceComponents(
        IReadOnlyList<StudioCloudAlbumSection> selected,
        IEnumerable<StudioCloudAlbumSection> current,
        IReadOnlyList<ProjectSourceSyncCandidate> pendingSources,
        string ownerEmail)
    {
        HashSet<string> selectedCodes = selected
            .Where(IsSourceComponent)
            .Select(component => component.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        HashSet<string> selectedBaseCodes = selected
            .Where(component =>
                IsSourceComponent(component) &&
                StudioAlbumComponentIdentity.IsOwnedSourceCode(component.Code))
            .Select(component =>
                StudioAlbumComponentIdentity.BaseSourceCode(component.Code))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return current.Where(component =>
        {
            if (!IsSourceComponent(component) ||
                selectedCodes.Contains(component.Code))
            {
                return false;
            }

            bool replacedByRenderedSlice =
                StudioAlbumComponentIdentity.IsOwnedSourceCode(component.Code) &&
                selectedBaseCodes.Contains(
                    StudioAlbumComponentIdentity.BaseSourceCode(component.Code));
            return replacedByRenderedSlice ||
                MatchesAnyPendingSource(component, pendingSources, ownerEmail);
        });
    }

    private static void AddStaleSourceComponentRemovalPatches(
        ICollection<AlbumComponentPdfPatch> patches,
        IReadOnlyList<StudioCloudAlbumSection> selected,
        IEnumerable<StudioCloudAlbumSection> current,
        IReadOnlyList<ProjectSourceSyncCandidate> pendingSources,
        string ownerEmail)
    {
        foreach (StudioCloudAlbumSection component in StaleSourceComponents(
                     selected,
                     current,
                     pendingSources,
                     ownerEmail))
        {
            if (patches.Any(patch => patch.Code.Equals(
                    component.Code,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            patches.Add(new AlbumComponentPdfPatch(
                component.Code,
                component.Order,
                "",
                Remove: true));
        }
    }

    private static void AddStaleSourceComponentRemovalUploads(
        ICollection<StudioAlbumComponentUpload> uploads,
        IReadOnlyList<StudioCloudAlbumSection> selected,
        IEnumerable<StudioCloudAlbumSection> current,
        IReadOnlyList<ProjectSourceSyncCandidate> pendingSources,
        string ownerEmail)
    {
        foreach (StudioCloudAlbumSection component in StaleSourceComponents(
                     selected,
                     current,
                     pendingSources,
                     ownerEmail))
        {
            if (uploads.Any(upload => upload.Code.Equals(
                    component.Code,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            uploads.Add(new StudioAlbumComponentUpload(
                component.Code,
                component.Label,
                component.Order,
                "",
                Remove: true,
                SourceKey: component.SourceKey,
                ComponentKind: component.ComponentKind));
        }
    }

    private void AddLegacyComponentMigrationRemovals(
        ICollection<StudioAlbumComponentUpload> uploads,
        IReadOnlyList<StudioCloudAlbumSection> selected,
        IReadOnlyDictionary<string, StudioCloudAlbumSection> currentByCode)
    {
        foreach (StudioCloudAlbumSection component in selected.Where(item =>
                     item.ComponentKind.Equals(
                         StudioAlbumComponentIdentity.SourceComponentKind,
                         StringComparison.OrdinalIgnoreCase)))
        {
            string legacyCode = component.SourceKey switch
            {
                StudioAlbumComponentIdentity.AtdSourceKey => ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                StudioAlbumComponentIdentity.VisualizationSourceKey => ProjectCloudSyncMetadata.VisualizationsComponentCode,
                _ => "source:" + component.SourceKey,
            };
            if (!currentByCode.TryGetValue(legacyCode, out StudioCloudAlbumSection? legacy) ||
                uploads.Any(item => item.Code.Equals(legacyCode, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            if (!component.SourceKey.Equals(
                    StudioAlbumComponentIdentity.AtdSourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                !component.SourceKey.Equals(
                    StudioAlbumComponentIdentity.VisualizationSourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                !StudioLegacySourceResolver.CanRetireUnqualifiedComponent(
                    state.Project,
                    component.SourceKey,
                    component.OwnerEmail))
            {
                continue;
            }
            uploads.Add(new StudioAlbumComponentUpload(
                legacy.Code,
                legacy.Label,
                legacy.Order,
                "",
                Remove: true,
                SourceKey: legacy.SourceKey,
                ComponentKind: legacy.ComponentKind));
        }
    }

    private void AddCanonicalAliasMigrationRemovals(
        ICollection<StudioAlbumComponentUpload> uploads,
        IReadOnlyList<StudioCloudAlbumSection> selected,
        IEnumerable<StudioCloudAlbumSection> current)
    {
        HashSet<string> selectedCodes = selected
            .Select(component => component.Code)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (StudioCloudAlbumSection component in current)
        {
            string canonicalCode = StudioAlbumComponentIdentity.CanonicalComponentCode(
                state.Project,
                component.Code);
            if (canonicalCode.Equals(component.Code, StringComparison.OrdinalIgnoreCase) ||
                !selectedCodes.Contains(canonicalCode) ||
                uploads.Any(upload => upload.Code.Equals(
                    component.Code,
                    StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            uploads.Add(new StudioAlbumComponentUpload(
                component.Code,
                component.Label,
                component.Order,
                "",
                Remove: true,
                SourceKey: component.SourceKey,
                ComponentKind: component.ComponentKind));
        }
    }

    private bool HasOwnedAtdDocuments(string ownerEmail) =>
        state.Project.Foundation.PlanningTask.Documents.Any(document =>
            document.Category.Equals(ProjectDocumentCategories.ApprovedPlanningTask, StringComparison.OrdinalIgnoreCase) &&
            document.IsAvailable &&
            !document.IsCloudPlaceholder &&
            IsDocumentOwnedBy(document, ownerEmail));

    private bool IsDocumentOwnedBy(ProjectFileReference document, string ownerEmail) =>
        state.ProjectPath is not null &&
        StudioAuxiliarySourceLocalityPolicy.IsLocalDocument(
            state.Project,
            document,
            ownerEmail,
            StudioDeviceIdentity.Fingerprint,
            StudioAuxiliarySourceLocalityPolicy.HasVerifiedPayload(
                state.ProjectPath,
                document));

    private void MarkOwnedAtdDocumentsSynced(string ownerEmail)
    {
        foreach (ProjectFileReference document in state.Project.Foundation.PlanningTask.Documents.Where(document =>
                     document.Category.Equals(ProjectDocumentCategories.ApprovedPlanningTask, StringComparison.OrdinalIgnoreCase) &&
                     IsDocumentOwnedBy(document, ownerEmail) &&
                     !document.IsCloudPlaceholder))
        {
            if (string.IsNullOrWhiteSpace(document.CloudContributionId))
                document.CloudContributionId = Guid.NewGuid().ToString("N");
            document.CloudSyncStatus = ProjectDocumentCloudSyncStatuses.Synced;
        }
        state.Project.Foundation.PlanningTask.DocumentCloudSyncStatus =
            ProjectDocumentCloudSyncStatuses.Synced;
    }

    private string CurrentCloudOwnerEmail()
    {
        string owner = (account.Current?.Email ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(owner))
            throw new InvalidOperationException("Cloud source contribution requires a signed-in account.");
        return owner;
    }

    private sealed record AlbumComponentIdentity(
        string Code,
        string OwnerEmail,
        string SourceKey,
        string ComponentKind)
    {
        public static AlbumComponentIdentity Generated(string code) => new(
            code,
            "",
            "",
            StudioAlbumComponentIdentity.GeneratedComponentKind);

        public static AlbumComponentIdentity Source(
            string ownerEmail,
            string sourceKey,
            string sectionKey = "",
            string sequenceKey = "") => new(
            StudioAlbumComponentIdentity.SourceSliceCode(
                ownerEmail,
                sourceKey,
                sectionKey,
                sequenceKey),
            ownerEmail.Trim().ToLowerInvariant(),
            sourceKey.Trim(),
            StudioAlbumComponentIdentity.SourceComponentKind);

        public static AlbumComponentIdentity SiteContext(
            string ownerEmail,
            string sourceKey) => new(
            ProjectCloudSyncMetadata.SiteContextComponentCode,
            ownerEmail.Trim().ToLowerInvariant(),
            sourceKey.Trim(),
            StudioAlbumComponentIdentity.SiteContextComponentKind);
    }

    private sealed record AlbumComponentMergeOutcome(
        StudioCloudAlbumRevision Revision,
        int ComponentCount,
        IReadOnlyList<string> ComponentCodes,
        IReadOnlyList<string> DeferredComponentCodes);

    private sealed record CanonicalTitleBlockPreview(
        string Path,
        string Sha256,
        string Signature,
        int PageCount);

    private sealed record CanonicalTitleBlockPublicationOutcome(
        StudioCloudAlbumRevision Revision,
        string Signature,
        bool Uploaded);
}
