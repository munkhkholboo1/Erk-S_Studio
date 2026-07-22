using System.IO;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private AlbumBuildResult BuildAlbumContributionSnapshot(string workFolder)
    {
        AlbumProject buildProject = state.CreateAlbumBuildProject();
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

    private List<StudioCloudAlbumSection> CreateCanonicalComponentManifest(
        AlbumBuildResult build,
        IReadOnlyList<StudioCloudSourcePackage> activeServerSources,
        IReadOnlyList<StudioCloudAlbumSection>? existingManifest = null)
    {
        string ownerEmail = CurrentCloudOwnerEmail();
        bool hasOwnedAtd = HasOwnedAtdDocuments(ownerEmail);
        bool hasVisualizations = CurrentProjectVisualizationSource()
            .ImagesForProject(state.Project.ProjectId)
            .Any(image => image.IsAvailable && image.IsIncludedInAlbum);
        Dictionary<string, int> sourceOrder = activeServerSources
            .Where(source => !string.IsNullOrWhiteSpace(source.SourceKey) &&
                !string.IsNullOrWhiteSpace(source.RegisteredBy))
            .Select(source => new
            {
                Source = source,
                Code = StudioAlbumComponentIdentity.SourceCode(source.RegisteredBy, source.SourceKey),
            })
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.Source.RegisteredAtUtc).Last())
            .OrderBy(source => source.Source.RegisteredAtUtc)
            .ThenBy(source => source.Code, StringComparer.OrdinalIgnoreCase)
            .Select((source, index) => new { source.Code, Index = index })
            .ToDictionary(item => item.Code, item => item.Index, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, StudioCloudAlbumSection> existingByCode = (existingManifest ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Code))
            .GroupBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var merged = new Dictionary<string, StudioCloudAlbumSection>(StringComparer.OrdinalIgnoreCase);
        foreach (AlbumBuildComponent component in build.Components)
        {
            AlbumComponentIdentity identity = CanonicalAlbumComponentIdentity(
                component.Code,
                ownerEmail,
                hasOwnedAtd,
                hasVisualizations,
                existingByCode);
            string code = identity.Code;
            int order = existingByCode.TryGetValue(code, out StudioCloudAlbumSection? existing)
                ? existing.Order
                : CanonicalAlbumComponentOrder(identity, component.Order, sourceOrder);
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
        string localCode,
        string ownerEmail,
        bool hasOwnedAtd,
        bool hasVisualizations,
        IReadOnlyDictionary<string, StudioCloudAlbumSection> existingByCode)
    {
        const string sourcePrefix = "source:";
        string normalized = localCode.Trim();
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

        string localIdentity = normalized[sourcePrefix.Length..].Trim();
        ProjectDesignSource? source = state.Project.Sources.FirstOrDefault(item =>
            item.Id.Equals(localIdentity, StringComparison.OrdinalIgnoreCase) ||
            ProjectCloudSyncMetadata.CloudSourceKey(item).Equals(localIdentity, StringComparison.OrdinalIgnoreCase));
        if (source is null)
            return AlbumComponentIdentity.Generated(normalized);
        string sourceOwner = ProjectCloudSyncMetadata.CloudOwnerEmail(source);
        return AlbumComponentIdentity.Source(
            string.IsNullOrWhiteSpace(sourceOwner) ? ownerEmail : sourceOwner,
            ProjectCloudSyncMetadata.CloudSourceKey(source));
    }

    private static int CanonicalAlbumComponentOrder(
        AlbumComponentIdentity identity,
        int localOrder,
        IReadOnlyDictionary<string, int> sourceOrder)
    {
        string code = identity.Code;
        const string sourcePrefix = "source:";
        if (code.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (identity.SourceKey.Equals(
                    StudioAlbumComponentIdentity.AtdSourceKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Math.Max(0, localOrder);
            }
            if (identity.SourceKey.Equals(
                    StudioAlbumComponentIdentity.VisualizationSourceKey,
                    StringComparison.OrdinalIgnoreCase))
            {
                return 20_000;
            }
            return sourceOrder.TryGetValue(code, out int index)
                ? 1_000 + index
                : 10_000 + Math.Max(0, localOrder);
        }
        return Math.Max(0, localOrder);
    }

    private bool TryBuildCloudUnionAlbumPreview(out AlbumBuildResult result)
    {
        result = null!;
        if (!TryGetCachedCanonicalAlbum(out string canonicalPdfPath, out StudioCloudAlbumRevision revision))
            return false;

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

        IReadOnlyList<ProjectSourceSyncCandidate> pendingSources =
            ProjectCloudSyncMetadata.PendingSourcePackages(state.Project);
        IReadOnlyList<string> rawPendingComponents =
            ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project);
        string ownerEmail = CurrentCloudOwnerEmail();
        Dictionary<string, string> sourceCodes = pendingSources
            .GroupBy(source => source.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => StudioAlbumComponentIdentity.SourceCode(ownerEmail, group.Key),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> pendingCodeMap = rawPendingComponents
            .ToDictionary(
                code => code,
                code => CanonicalPendingComponentCode(code, ownerEmail),
                StringComparer.OrdinalIgnoreCase);
        HashSet<string> requestedCodes = sourceCodes.Values
            .Concat(pendingCodeMap.Values)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (requestedCodes.Count == 0)
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
                .Where(component => requestedCodes.Contains(component.Code))
                .ToList();
            string[] missing = requestedCodes
                .Where(code => selected.All(component =>
                    !component.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            string[] unrenderedSourcesWithSheets = missing
                .Where(code => pendingSources.Any(source =>
                    source.SheetCount > 0 &&
                    sourceCodes.TryGetValue(source.SourceKey, out string? sourceCode) &&
                    code.Equals(sourceCode, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (unrenderedSourcesWithSheets.Length > 0)
            {
                throw new InvalidDataException(
                    "Pending source has sheets but its album component could not be rendered locally: " +
                    string.Join(", ", unrenderedSourcesWithSheets));
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
                .ToDictionary(component => component.Code, StringComparer.OrdinalIgnoreCase);
            AddLegacyComponentMigrationPatches(patches, selected, currentByCode);
            foreach (string code in missing)
            {
                if (!currentByCode.TryGetValue(code, out StudioCloudAlbumSection? current))
                    continue;
                patches.Add(new AlbumComponentPdfPatch(
                    code,
                    current.Order,
                    "",
                    Remove: true));
            }

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

            string relativePath = ProjectWorkspacePaths.ToRelativePath(state.ProjectPath, outputPath);
            string sha256 = ComputeFileSha256(outputPath);
            ProjectAlbumRecord album = state.Project.PrimaryAlbum;
            album.LastPdfPath = relativePath;
            album.LastPdfSha256 = sha256;
            album.LastPageCount = composition.PageCount;
            album.LastPageSizeSummary = revision.PageSizeSummary?.Trim() ?? "";
            lastAlbumPath = outputPath;

            result = new AlbumBuildResult
            {
                OutputPath = outputPath,
                SheetCount = localBuild.SheetCount,
                PageCount = composition.PageCount,
            };
            result.Warnings.AddRange(localBuild.Warnings);
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
        return !string.IsNullOrWhiteSpace(canonicalPdfPath) &&
            File.Exists(canonicalPdfPath) &&
            HasCompleteComponentManifest(revision);
    }

    private List<StudioCloudSourcePackage> SharedCloudSources() =>
        (state.Project.Cloud.SharedSources ?? [])
            .Where(source => !string.IsNullOrWhiteSpace(source.SourceKey) &&
                !string.IsNullOrWhiteSpace(source.OwnerEmail))
            .Select(source => new StudioCloudSourcePackage
            {
                SourceId = source.SourceId,
                SourceKey = source.SourceKey,
                SourceApplication = source.SourceApplication,
                SourceDocumentReference = source.SourceDocumentReference,
                ManifestId = source.ManifestId,
                ContentHash = source.ContentHash,
                SheetCount = source.SheetCount,
                Status = source.Status,
                RegisteredBy = source.OwnerEmail,
                RegisteredAtUtc = source.RegisteredAtUtc,
            })
            .ToList();

    private AlbumBuildResult PointPrimaryAlbumAtCanonical(
        string canonicalPdfPath,
        StudioCloudAlbumRevision revision)
    {
        string relativePath = ProjectWorkspacePaths.ToRelativePath(
            state.ProjectPath!,
            canonicalPdfPath);
        ProjectAlbumRecord album = state.Project.PrimaryAlbum;
        album.LastPdfPath = relativePath;
        album.LastPdfSha256 = CleanSha256(revision.PdfSha256);
        album.LastPageCount = revision.PageCount;
        album.LastPageSizeSummary = revision.PageSizeSummary?.Trim() ?? "";
        lastAlbumPath = canonicalPdfPath;
        var result = new AlbumBuildResult
        {
            OutputPath = canonicalPdfPath,
            SheetCount = state.Library.Snapshot().Count,
            PageCount = revision.PageCount,
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

    private static void AddLegacyComponentMigrationPatches(
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
            patches.Add(new AlbumComponentPdfPatch(
                legacy.Code,
                legacy.Order,
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
        IReadOnlyList<StudioCloudSourcePackage> activeServerSources)
    {
        if (!HasCompleteComponentManifest(currentRevision))
        {
            throw new InvalidOperationException(
                "The current Cloud album has no complete component manifest. " +
                "A device with the complete album must Sync once before collaborators can add components.");
        }

        string ownerEmail = CurrentCloudOwnerEmail();
        IReadOnlyList<string> rawPendingComponents =
            ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project);
        Dictionary<string, string> sourceCodes = pendingSources
            .GroupBy(source => source.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => StudioAlbumComponentIdentity.SourceCode(ownerEmail, group.Key),
                StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string> pendingCodeMap = rawPendingComponents
            .ToDictionary(
                code => code,
                code => CanonicalPendingComponentCode(code, ownerEmail),
                StringComparer.OrdinalIgnoreCase);
        HashSet<string> requestedCodes = sourceCodes.Values
            .Concat(pendingCodeMap.Values)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requestedCodes.Count == 0)
            return new AlbumComponentMergeOutcome(currentRevision, 0, []);

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
                .Where(component => requestedCodes.Contains(component.Code))
                .ToList();
            string[] missing = requestedCodes
                .Where(code => selected.All(component =>
                    !component.Code.Equals(code, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            string[] unrenderedSourcesWithSheets = missing
                .Where(code => pendingSources.Any(source =>
                    source.SheetCount > 0 &&
                    sourceCodes.TryGetValue(source.SourceKey, out string? sourceCode) &&
                    code.Equals(sourceCode, StringComparison.OrdinalIgnoreCase)))
                .ToArray();
            if (unrenderedSourcesWithSheets.Length > 0)
            {
                throw new InvalidDataException(
                    "Pending source has sheets but its album component could not be rendered locally: " +
                    string.Join(", ", unrenderedSourcesWithSheets));
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
                .ToDictionary(component => component.Code, StringComparer.OrdinalIgnoreCase);
            AddLegacyComponentMigrationRemovals(uploads, selected, currentByCode);
            foreach (string code in missing)
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

            StudioCloudAlbumRevision merged = uploads.Count == 0
                ? currentRevision
                : await account.MergeAlbumComponentsAsync(
                    projectId,
                    serverAlbum.AlbumId,
                    currentRevision.RevisionId,
                    projectConcurrencyToken,
                    uploads);
            foreach (ProjectSourceSyncCandidate source in pendingSources.Where(source =>
                         sourceCodes.TryGetValue(source.SourceKey, out string? code) &&
                         requestedCodes.Contains(code)))
            {
                ProjectCloudSyncMetadata.MarkSourceSynced(source);
            }
            if (pendingCodeMap.TryGetValue(
                    ProjectCloudSyncMetadata.ApprovedAtdComponentCode,
                    out string? pendingAtdCode) &&
                requestedCodes.Contains(pendingAtdCode))
            {
                MarkOwnedAtdDocumentsSynced(ownerEmail);
            }
            ProjectCloudSyncMetadata.MarkAlbumComponentsSynced(
                state.Project,
                rawPendingComponents);
            return new AlbumComponentMergeOutcome(
                merged,
                uploads.Count,
                requestedCodes.ToArray());
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
    {
        int[] pages = (revision.SectionManifest ?? [])
            .SelectMany(component => component.PageNumbers ?? [])
            .Order()
            .ToArray();
        return revision.PageCount > 0 &&
            pages.Length == pages.Distinct().Count() &&
            pages.SequenceEqual(Enumerable.Range(1, revision.PageCount));
    }

    private static bool ComponentManifestsEqual(
        IReadOnlyList<StudioCloudAlbumSection> left,
        IReadOnlyList<StudioCloudAlbumSection> right)
    {
        Dictionary<string, StudioCloudAlbumSection> rightByCode = right
            .ToDictionary(item => item.Code, StringComparer.OrdinalIgnoreCase);
        return left.Count == right.Count && left.All(item =>
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
        if (StudioAlbumComponentIdentity.IsOwnedSourceCode(normalized))
            return normalized;
        if (normalized.StartsWith("source:", StringComparison.OrdinalIgnoreCase))
        {
            string identity = normalized["source:".Length..].Trim();
            ProjectDesignSource? source = state.Project.Sources.FirstOrDefault(item =>
                item.Id.Equals(identity, StringComparison.OrdinalIgnoreCase) ||
                ProjectCloudSyncMetadata.CloudSourceKey(item).Equals(identity, StringComparison.OrdinalIgnoreCase));
            if (source is not null)
            {
                return StudioAlbumComponentIdentity.SourceCode(
                    ownerEmail,
                    ProjectCloudSyncMetadata.CloudSourceKey(source));
            }
        }
        return normalized;
    }

    private static void AddLegacyComponentMigrationRemovals(
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

    private bool HasOwnedAtdDocuments(string ownerEmail) =>
        state.Project.Foundation.PlanningTask.Documents.Any(document =>
            document.Category.Equals(ProjectDocumentCategories.ApprovedPlanningTask, StringComparison.OrdinalIgnoreCase) &&
            document.IsAvailable &&
            !document.IsCloudPlaceholder &&
            IsDocumentOwnedBy(document, ownerEmail));

    private static bool IsDocumentOwnedBy(ProjectFileReference document, string ownerEmail) =>
        string.IsNullOrWhiteSpace(document.CloudOwnerEmail) ||
        document.CloudOwnerEmail.Equals(ownerEmail, StringComparison.OrdinalIgnoreCase);

    private void MarkOwnedAtdDocumentsSynced(string ownerEmail)
    {
        foreach (ProjectFileReference document in state.Project.Foundation.PlanningTask.Documents.Where(document =>
                     document.Category.Equals(ProjectDocumentCategories.ApprovedPlanningTask, StringComparison.OrdinalIgnoreCase) &&
                     IsDocumentOwnedBy(document, ownerEmail) &&
                     !document.IsCloudPlaceholder))
        {
            document.CloudOwnerEmail = ownerEmail;
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

        public static AlbumComponentIdentity Source(string ownerEmail, string sourceKey) => new(
            StudioAlbumComponentIdentity.SourceCode(ownerEmail, sourceKey),
            ownerEmail.Trim().ToLowerInvariant(),
            sourceKey.Trim(),
            StudioAlbumComponentIdentity.SourceComponentKind);
    }

    private sealed record AlbumComponentMergeOutcome(
        StudioCloudAlbumRevision Revision,
        int ComponentCount,
        IReadOnlyList<string> ComponentCodes);
}
