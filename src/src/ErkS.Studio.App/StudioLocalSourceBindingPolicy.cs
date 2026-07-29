using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using System.IO;

namespace ErkS.Studio;

/// <summary>
/// Separates a device-local native payload from its Cloud ERA registry row.
/// Ownership/custody grants authority, but never proves that this account and
/// device actually possess the native file or received package.
/// </summary>
internal static class StudioLocalSourceBindingPolicy
{
    private const string AccountKey = "local.bindingAccountEmail";
    private const string DeviceKey = "local.bindingDeviceFingerprint";
    private const string VersionKey = "local.bindingVersion";
    private const string CurrentVersion = "1";

    public static void Bind(
        ProjectDesignSource source,
        string accountEmail,
        string deviceFingerprint)
    {
        ArgumentNullException.ThrowIfNull(source);
        string account = NormalizeEmail(accountEmail);
        string device = NormalizeDevice(deviceFingerprint);
        if (string.IsNullOrWhiteSpace(account))
            throw new ArgumentException(
                "A signed-in account is required for a local source binding.",
                nameof(accountEmail));
        if (string.IsNullOrWhiteSpace(device))
            throw new ArgumentException(
                "A device fingerprint is required for a local source binding.",
                nameof(deviceFingerprint));

        source.Metadata ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.Metadata[AccountKey] = account;
        source.Metadata[DeviceKey] = device;
        source.Metadata[VersionKey] = CurrentVersion;
    }

    public static bool IsLocal(
        ProjectDesignSource source,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        bool hasVerifiedPayload)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!hasVerifiedPayload)
            return false;

        string boundAccount = Value(source, AccountKey);
        string boundDevice = Value(source, DeviceKey);
        return !string.IsNullOrWhiteSpace(boundAccount) &&
            !string.IsNullOrWhiteSpace(boundDevice) &&
            boundAccount.Equals(
                NormalizeEmail(currentAccountEmail),
                StringComparison.OrdinalIgnoreCase) &&
            boundDevice.Equals(
                NormalizeDevice(currentDeviceFingerprint),
                StringComparison.Ordinal);
    }

    public static bool TryExplicitRelink(
        ProjectDesignSource source,
        string? authorizedControllerEmail,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        bool hasVerifiedPayload)
    {
        ArgumentNullException.ThrowIfNull(source);
        string controller = NormalizeEmail(authorizedControllerEmail);
        string current = NormalizeEmail(currentAccountEmail);
        if (!hasVerifiedPayload ||
            string.IsNullOrWhiteSpace(controller) ||
            !controller.Equals(current, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(
                NormalizeDevice(currentDeviceFingerprint)))
        {
            return false;
        }

        Bind(source, current, currentDeviceFingerprint!);
        return true;
    }

    public static string ResolveLegacyImmutableOwner(
        ProjectWorkspace project,
        ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);

        string storedOwner =
            ProjectCloudSyncMetadata.CloudOwnerEmail(source);
        if (!string.IsNullOrWhiteSpace(storedOwner))
            return storedOwner;

        string sourceKey =
            ProjectCloudSyncMetadata.CloudSourceKey(source);
        string[] immutableOwners = (project.Cloud?.SharedSources ?? [])
            .Where(shared =>
                !shared.Status.Equals(
                    "Retired",
                    StringComparison.OrdinalIgnoreCase) &&
                shared.SourceKey.Equals(
                    sourceKey,
                    StringComparison.OrdinalIgnoreCase))
            .Select(StudioSharedSourceProjection.ImmutableOwner)
            .Where(owner => !string.IsNullOrWhiteSpace(owner))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return immutableOwners.Length == 1
            ? immutableOwners[0]
            : "";
    }

    internal static bool HasVerifiedPayload(ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        try
        {
            if (!string.IsNullOrWhiteSpace(source.NativeDocumentPath) &&
                (File.Exists(source.NativeDocumentPath) ||
                 Directory.Exists(source.NativeDocumentPath)))
            {
                return true;
            }

            if (string.IsNullOrWhiteSpace(source.InboxFolder) ||
                !Directory.Exists(source.InboxFolder))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(source.Id))
                return false;

            string expectedManifestId =
                ProjectCloudSyncMetadata.RecordedSourceManifestId(source);
            string expectedContentHash =
                ProjectCloudSyncMetadata.RecordedSourceContentHash(source);
            return Directory.EnumerateFiles(
                    source.InboxFolder,
                    "*" + SheetPackageManifest.ManifestSuffix,
                    SearchOption.AllDirectories)
                .Any(manifestPath =>
                {
                    SheetPackageLoadResult package =
                        SheetPackageReader.Load(manifestPath);
                    return package.IsLossless &&
                        package.Manifest is not null &&
                        !string.IsNullOrWhiteSpace(
                            package.Manifest.Source.SourceId) &&
                        package.Manifest.Source.SourceId.Equals(
                            source.Id,
                            StringComparison.OrdinalIgnoreCase) &&
                        MatchesCurrentRecordedPackage(
                            package,
                            expectedManifestId,
                            expectedContentHash);
                });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool MatchesCurrentRecordedPackage(
        SheetPackageLoadResult package,
        string expectedManifestId,
        string expectedContentHash)
    {
        if (package.Manifest is null)
            return false;
        if (!string.IsNullOrWhiteSpace(expectedManifestId) &&
            (!Guid.TryParse(expectedManifestId, out Guid expectedPackageId) ||
             package.Manifest.PackageId != expectedPackageId))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(expectedContentHash) ||
            package.ManifestSha256.Equals(
                expectedContentHash,
                StringComparison.OrdinalIgnoreCase);
    }

    private static string Value(ProjectDesignSource source, string key)
    {
        source.Metadata ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        return source.Metadata.TryGetValue(key, out string? value)
            ? value?.Trim() ?? ""
            : "";
    }

    private static string NormalizeEmail(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private static string NormalizeDevice(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
