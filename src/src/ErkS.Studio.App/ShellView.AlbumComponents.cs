using System.IO;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private AlbumBuildResult BuildAlbumContributionSnapshot(string workFolder)
    {
        AlbumProject buildProject = state.CreateAlbumBuildProject();
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
        IReadOnlyList<StudioCloudSourcePackage> activeServerSources)
    {
        Dictionary<string, int> sourceOrder = activeServerSources
            .Where(source => !string.IsNullOrWhiteSpace(source.SourceKey))
            .GroupBy(source => source.SourceKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderBy(item => item.RegisteredAtUtc).Last())
            .OrderBy(source => source.RegisteredAtUtc)
            .ThenBy(source => source.SourceKey, StringComparer.OrdinalIgnoreCase)
            .Select((source, index) => new { source.SourceKey, Index = index })
            .ToDictionary(item => item.SourceKey, item => item.Index, StringComparer.OrdinalIgnoreCase);

        var merged = new Dictionary<string, StudioCloudAlbumSection>(StringComparer.OrdinalIgnoreCase);
        foreach (AlbumBuildComponent component in build.Components)
        {
            string code = CanonicalAlbumComponentCode(component.Code);
            int order = CanonicalAlbumComponentOrder(code, component.Order, sourceOrder);
            if (!merged.TryGetValue(code, out StudioCloudAlbumSection? section))
            {
                section = new StudioCloudAlbumSection
                {
                    Code = code,
                    Label = component.Label,
                    Order = order,
                    Status = "Available",
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

    private string CanonicalAlbumComponentCode(string localCode)
    {
        const string sourcePrefix = "source:";
        if (!localCode.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
            return localCode.Trim();

        string localIdentity = localCode[sourcePrefix.Length..].Trim();
        ProjectDesignSource? source = state.Project.Sources.FirstOrDefault(item =>
            item.Id.Equals(localIdentity, StringComparison.OrdinalIgnoreCase));
        return source is null
            ? sourcePrefix + localIdentity
            : sourcePrefix + ProjectCloudSyncMetadata.CloudSourceKey(source);
    }

    private static int CanonicalAlbumComponentOrder(
        string code,
        int localOrder,
        IReadOnlyDictionary<string, int> sourceOrder)
    {
        const string sourcePrefix = "source:";
        if (code.StartsWith(sourcePrefix, StringComparison.OrdinalIgnoreCase))
        {
            string sourceKey = code[sourcePrefix.Length..];
            return sourceOrder.TryGetValue(sourceKey, out int index)
                ? 1_000 + index
                : 10_000 + Math.Max(0, localOrder);
        }
        if (code.Equals(ProjectCloudSyncMetadata.VisualizationsComponentCode, StringComparison.OrdinalIgnoreCase))
            return 20_000;
        return Math.Max(0, localOrder);
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

        HashSet<string> requestedCodes = pendingSources
            .Select(source => "source:" + source.SourceKey)
            .Concat(ProjectCloudSyncMetadata.PendingAlbumComponents(state.Project))
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
                activeServerSources);
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
                    code.Equals("source:" + source.SourceKey, StringComparison.OrdinalIgnoreCase)))
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
                    outputPath));
            }

            Dictionary<string, StudioCloudAlbumSection> currentByCode = currentRevision.SectionManifest
                .ToDictionary(component => component.Code, StringComparer.OrdinalIgnoreCase);
            foreach (string code in missing)
            {
                if (!currentByCode.TryGetValue(code, out StudioCloudAlbumSection? current))
                    continue;
                uploads.Add(new StudioAlbumComponentUpload(
                    code,
                    current.Label,
                    current.Order,
                    "",
                    Remove: true));
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
                         requestedCodes.Contains("source:" + source.SourceKey)))
            {
                ProjectCloudSyncMetadata.MarkSourceSynced(source);
            }
            ProjectCloudSyncMetadata.MarkAlbumComponentsSynced(
                state.Project,
                requestedCodes);
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
                activeServerSources);
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
            item.PageNumbers.SequenceEqual(other.PageNumbers));
    }

    private sealed record AlbumComponentMergeOutcome(
        StudioCloudAlbumRevision Revision,
        int ComponentCount,
        IReadOnlyList<string> ComponentCodes);
}
