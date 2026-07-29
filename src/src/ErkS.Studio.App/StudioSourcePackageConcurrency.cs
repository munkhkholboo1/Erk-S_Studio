using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Resolves the exact canonical source snapshot that a local source update is
/// based on. Source keys are participant-scoped, so they are never sufficient
/// by themselves to select a stream.
/// </summary>
internal static class StudioSourcePackageConcurrency
{
    public static string ExpectedBaseSourceId(
        ProjectWorkspace project,
        ProjectSourceSyncCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(candidate);

        string sourceKey = candidate.SourceKey.Trim();
        string immutableOwner =
            ProjectCloudSyncMetadata.CloudOwnerEmail(candidate.Source);
        if (string.IsNullOrWhiteSpace(sourceKey) ||
            string.IsNullOrWhiteSpace(immutableOwner))
        {
            return "";
        }

        ProjectCloudSourceReference[] matches =
            (project.Cloud?.SharedSources ?? [])
                .Where(source =>
                    IsActive(source.Status) &&
                    source.SourceKey.Equals(
                        sourceKey,
                        StringComparison.OrdinalIgnoreCase) &&
                    StudioSharedSourceProjection.ImmutableOwner(source).Equals(
                        immutableOwner,
                        StringComparison.OrdinalIgnoreCase))
                .ToArray();
        return matches.Length switch
        {
            0 => "",
            1 => matches[0].SourceId.Trim(),
            _ => throw new InvalidOperationException(
                "Cloud source stream is ambiguous. Run Cloud Sync before " +
                "uploading this source again."),
        };
    }

    private static bool IsActive(string? status) =>
        string.IsNullOrWhiteSpace(status) ||
        status.Equals("Registered", StringComparison.OrdinalIgnoreCase);
}
