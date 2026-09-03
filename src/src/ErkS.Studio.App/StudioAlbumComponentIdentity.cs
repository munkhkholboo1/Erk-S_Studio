using System.Security.Cryptography;
using System.Text;
using System.IO;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal static class StudioAlbumComponentIdentity
{
    private const string AlbumSliceMarker = "|album-slice|";

    public const string SourceComponentKind = "Source";
    public const string GeneratedComponentKind = "Generated";
    public const string LegacySnapshotComponentCode =
        "legacy:cloud-album-snapshot";
    public const string LegacySnapshotComponentKind = "LegacySnapshot";
    public const int LegacySnapshotComponentOrder = -1_000_000;
    public const string SiteContextComponentKind =
        ProjectSiteContextEditingPolicy.SiteContextComponentKind;
    public const string AtdSourceKey = "foundation-atd";
    public const string VisualizationSourceKey = "visualizations";

    public static string SourceCode(string ownerEmail, string sourceKey)
    {
        string owner = (ownerEmail ?? "").Trim().ToLowerInvariant();
        string key = (sourceKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(key))
            throw new InvalidDataException("A source component requires an owner and source key.");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(owner));
        return $"source:{Convert.ToHexString(hash)[..16].ToLowerInvariant()}:{key}";
    }

    public static string SourceSliceCode(
        string ownerEmail,
        string sourceKey,
        string sectionKey,
        string sequenceKey)
    {
        string section = (sectionKey ?? "").Trim();
        string sequence = (sequenceKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(section) &&
            string.IsNullOrWhiteSpace(sequence))
        {
            return SourceCode(ownerEmail, sourceKey);
        }

        return SourceCode(ownerEmail, sourceKey) +
            AlbumSliceMarker +
            EncodeSliceValue(section) +
            "." +
            EncodeSliceValue(sequence);
    }

    public static string SourceBuildingCode(
        string ownerEmail,
        string sourceKey,
        string sectionKey) =>
        SourceSliceCode(ownerEmail, sourceKey, sectionKey, "");

    public static string BaseSourceCode(string code)
    {
        string normalized = (code ?? "").Trim();
        int markerIndex = normalized.IndexOf(
            AlbumSliceMarker,
            StringComparison.OrdinalIgnoreCase);
        return markerIndex < 0 ? normalized : normalized[..markerIndex];
    }

    public static bool TryGetSourceSlice(
        string code,
        out string sectionKey,
        out string sequenceKey)
    {
        sectionKey = "";
        sequenceKey = "";
        string normalized = (code ?? "").Trim();
        int markerIndex = normalized.IndexOf(
            AlbumSliceMarker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || !IsOwnedSourceCode(normalized[..markerIndex]))
            return false;

        string payload = normalized[(markerIndex + AlbumSliceMarker.Length)..];
        int separatorIndex = payload.IndexOf('.');
        if (separatorIndex < 0)
            return false;

        try
        {
            sectionKey = DecodeSliceValue(payload[..separatorIndex]);
            sequenceKey = DecodeSliceValue(payload[(separatorIndex + 1)..]);
            return !string.IsNullOrWhiteSpace(sectionKey) ||
                !string.IsNullOrWhiteSpace(sequenceKey);
        }
        catch (FormatException)
        {
            sectionKey = "";
            sequenceKey = "";
            return false;
        }
    }

    public static bool TryGetBuildingSectionKey(
        string code,
        out string sectionKey)
    {
        bool parsed = TryGetSourceSlice(code, out sectionKey, out _);
        return parsed &&
            (sectionKey.StartsWith("studio-building:", StringComparison.OrdinalIgnoreCase) ||
             sectionKey.StartsWith("package-building:", StringComparison.OrdinalIgnoreCase));
    }

    public static string CanonicalBuildingSectionKey(
        ProjectWorkspace project,
        string sectionKey)
    {
        ArgumentNullException.ThrowIfNull(project);
        string identity = (sectionKey ?? "").Trim();
        return TryResolveBuildingGroup(project, identity, out ProjectBuildingGroup group)
            ? $"studio-building:{group.Id.Trim()}"
            : identity;
    }

    public static string CanonicalBuildingSubCoverCode(
        ProjectWorkspace project,
        string componentCode)
    {
        ArgumentNullException.ThrowIfNull(project);
        string code = (componentCode ?? "").Trim();
        if (!ProjectCloudSyncMetadata.IsBuildingSubCoverComponentCode(code))
            return code;

        string identity = code[
            ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix.Length..].Trim();
        return TryResolveBuildingGroup(project, identity, out ProjectBuildingGroup group)
            ? ProjectCloudSyncMetadata.BuildingSubCoverComponentCode(group)
            : code;
    }

    public static string CanonicalComponentCode(
        ProjectWorkspace project,
        string componentCode)
    {
        ArgumentNullException.ThrowIfNull(project);
        string code = CanonicalBuildingSubCoverCode(project, componentCode);
        if (!TryGetSourceSlice(code, out string sectionKey, out string sequenceKey))
            return code;

        string canonicalSectionKey = CanonicalBuildingSectionKey(project, sectionKey);
        if (canonicalSectionKey.Equals(sectionKey, StringComparison.OrdinalIgnoreCase))
            return code;

        return BaseSourceCode(code) +
            AlbumSliceMarker +
            EncodeSliceValue(canonicalSectionKey) +
            "." +
            EncodeSliceValue(sequenceKey);
    }

    public static bool TryResolveBuildingGroup(
        ProjectWorkspace project,
        string identity,
        out ProjectBuildingGroup group)
    {
        ArgumentNullException.ThrowIfNull(project);
        const string studioBuildingPrefix = "studio-building:";
        const string packageBuildingIdPrefix = "package-building:id:";
        const string packageBuildingNamePrefix = "package-building:name:";

        string normalized = (identity ?? "").Trim();
        string groupId = normalized;
        string groupName = normalized;
        if (normalized.StartsWith(studioBuildingPrefix, StringComparison.OrdinalIgnoreCase))
        {
            groupId = normalized[studioBuildingPrefix.Length..].Trim();
            groupName = "";
        }
        else if (normalized.StartsWith(packageBuildingIdPrefix, StringComparison.OrdinalIgnoreCase))
        {
            groupId = normalized[packageBuildingIdPrefix.Length..].Trim();
            groupName = "";
        }
        else if (normalized.StartsWith(packageBuildingNamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            groupId = "";
            groupName = normalized[packageBuildingNamePrefix.Length..].Trim();
        }

        ProjectBuildingGroup? matched = project.BuildingGroups.FirstOrDefault(candidate =>
            (!string.IsNullOrWhiteSpace(groupId) &&
             candidate.Id.Equals(groupId, StringComparison.OrdinalIgnoreCase)) ||
            (!string.IsNullOrWhiteSpace(groupName) &&
             candidate.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)));
        group = matched!;
        return matched is not null;
    }

    public static bool IsOwnedSourceCode(string code)
    {
        string[] parts = BaseSourceCode(code).Split(':', 3);
        return parts.Length == 3 &&
            parts[0].Equals("source", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Length == 16 &&
            parts[1].All(Uri.IsHexDigit) &&
            !string.IsNullOrWhiteSpace(parts[2]);
    }

    public static bool TryResolveExistingSource(
        string componentCode,
        string sourceIdentity,
        IEnumerable<StudioCloudAlbumSection> existingComponents,
        out StudioCloudAlbumSection? existing)
    {
        existing = null;
        StudioCloudAlbumSection[] sourceComponents = (existingComponents ?? [])
            .Where(component =>
                component.ComponentKind.Equals(
                    SourceComponentKind,
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(component.OwnerEmail) &&
                !string.IsNullOrWhiteSpace(component.SourceKey))
            .ToArray();

        string normalizedCode = (componentCode ?? "").Trim();
        existing = sourceComponents.FirstOrDefault(component =>
            component.Code.Equals(normalizedCode, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return true;

        string identity = (sourceIdentity ?? "").Trim();
        if (string.IsNullOrWhiteSpace(identity) &&
            normalizedCode.StartsWith("source:", StringComparison.OrdinalIgnoreCase))
        {
            string baseCode = BaseSourceCode(normalizedCode);
            identity = baseCode["source:".Length..].Trim();
        }
        if (string.IsNullOrWhiteSpace(identity))
            return false;

        StudioCloudAlbumSection[] matches = sourceComponents
            .Where(component =>
                component.SourceKey.Equals(identity, StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                component =>
                    $"{component.OwnerEmail.Trim().ToLowerInvariant()}|{component.SourceKey.Trim()}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        if (matches.Length != 1)
            return false;

        existing = matches[0];
        return true;
    }

    public static bool HasNoAssignedPages(
        IEnumerable<StudioCloudAlbumSection> components) =>
        !(components ?? []).Any(component =>
            (component.PageNumbers ?? []).Length > 0);

    public static bool IsSourceComponent(StudioCloudAlbumSection component)
    {
        ArgumentNullException.ThrowIfNull(component);
        if ((component.PageNumbers ?? []).Length == 0 &&
            string.Equals(
                component.Status,
                "Planned",
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return string.Equals(
                component.ComponentKind,
                SourceComponentKind,
                StringComparison.OrdinalIgnoreCase) ||
            (component.Code ?? "").StartsWith(
                "source:",
                StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The shared-component rows the source workspace lists. Kept here rather
    /// than inline in the view so the rule has a test: a site-context component
    /// carries a SourceKey too, and listing it as a source was the bug this
    /// predicate replaced.
    /// </summary>
    public static bool IsSourceComponentReference(ProjectCloudAlbumComponentReference component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return string.Equals(
                component.ComponentKind,
                SourceComponentKind,
                StringComparison.OrdinalIgnoreCase) ||
            (component.Code ?? "").StartsWith(
                "source:",
                StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsLegacySnapshot(StudioCloudAlbumSection component)
    {
        ArgumentNullException.ThrowIfNull(component);
        return component.Code.Equals(
                LegacySnapshotComponentCode,
                StringComparison.OrdinalIgnoreCase) ||
            component.ComponentKind.Equals(
                LegacySnapshotComponentKind,
                StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsLegacySnapshot(
        IEnumerable<StudioCloudAlbumSection> components) =>
        (components ?? []).Any(IsLegacySnapshot);

    public static bool HasCompletePageCoverage(
        IEnumerable<StudioCloudAlbumSection> components,
        int pageCount)
    {
        int[] pages = (components ?? [])
            .SelectMany(component => component.PageNumbers ?? [])
            .Order()
            .ToArray();
        return pageCount > 0 &&
            pages.Length == pages.Distinct().Count() &&
            pages.SequenceEqual(Enumerable.Range(1, pageCount));
    }

    public static bool IsMergeReady(
        IEnumerable<StudioCloudAlbumSection> components,
        int pageCount) =>
        HasCompletePageCoverage(components, pageCount) &&
        !ContainsLegacySnapshot(components);

    public static bool HasRecoverablePriorManifest(
        StudioCloudAlbumRevision current,
        IEnumerable<StudioCloudAlbumRevision> revisions)
    {
        ArgumentNullException.ThrowIfNull(current);
        return HasNoAssignedPages(current.SectionManifest) &&
            (revisions ?? []).Any(candidate =>
                candidate.RevisionNumber < current.RevisionNumber &&
                candidate.PageCount == current.PageCount &&
                IsMergeReady(
                    candidate.SectionManifest,
                    candidate.PageCount));
    }

    public static StudioCloudAlbumSection CreateLegacySnapshotSection(
        int pageCount)
    {
        if (pageCount < 1)
            throw new InvalidDataException("Album page count must be at least one.");

        return new StudioCloudAlbumSection
        {
            Code = LegacySnapshotComponentCode,
            Label = "Legacy cloud album snapshot",
            Order = LegacySnapshotComponentOrder,
            PageNumbers = Enumerable.Range(1, pageCount).ToArray(),
            Status = "Available",
            OwnerEmail = "",
            SourceKey = "",
            ComponentKind = LegacySnapshotComponentKind,
        };
    }

    private static string EncodeSliceValue(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string DecodeSliceValue(string value)
    {
        string normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => "",
        };
        return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }
}
