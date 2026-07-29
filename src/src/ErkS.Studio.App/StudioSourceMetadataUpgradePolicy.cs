using System.Globalization;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal enum StudioSourceMetadataUpgradeReason
{
    LegacyBindingBackfilled,
    ExistingBindingVersioned,
    ExistingBindingIncomplete,
    SchemaCurrent,
    SchemaNewer,
    SchemaInvalid,
    RuntimeIdentityMissing,
    ImmutableOwnerMissing,
    ImmutableOwnerMismatch,
    SourceNotEditable,
    CloudRegistryIdentityMissing,
    CloudRegistryIdentityAmbiguous,
    VerifiedPayloadMissing,
}

internal sealed record StudioSourceMetadataUpgradeDecision(
    string SourceId,
    StudioSourceMetadataUpgradeReason Reason,
    bool Changed,
    bool BindingCreated,
    string Detail)
{
    public string ReasonCode => Reason switch
    {
        StudioSourceMetadataUpgradeReason.LegacyBindingBackfilled =>
            "source_metadata_legacy_binding_backfilled",
        StudioSourceMetadataUpgradeReason.ExistingBindingVersioned =>
            "source_metadata_existing_binding_versioned",
        StudioSourceMetadataUpgradeReason.ExistingBindingIncomplete =>
            "source_metadata_existing_binding_incomplete",
        StudioSourceMetadataUpgradeReason.SchemaCurrent =>
            "source_metadata_schema_current",
        StudioSourceMetadataUpgradeReason.SchemaNewer =>
            "source_metadata_schema_newer",
        StudioSourceMetadataUpgradeReason.SchemaInvalid =>
            "source_metadata_schema_invalid",
        StudioSourceMetadataUpgradeReason.RuntimeIdentityMissing =>
            "source_metadata_runtime_identity_missing",
        StudioSourceMetadataUpgradeReason.ImmutableOwnerMissing =>
            "source_metadata_immutable_owner_missing",
        StudioSourceMetadataUpgradeReason.ImmutableOwnerMismatch =>
            "source_metadata_immutable_owner_mismatch",
        StudioSourceMetadataUpgradeReason.SourceNotEditable =>
            "source_metadata_source_not_editable",
        StudioSourceMetadataUpgradeReason.CloudRegistryIdentityMissing =>
            "source_metadata_cloud_registry_identity_missing",
        StudioSourceMetadataUpgradeReason.CloudRegistryIdentityAmbiguous =>
            "source_metadata_cloud_registry_identity_ambiguous",
        StudioSourceMetadataUpgradeReason.VerifiedPayloadMissing =>
            "source_metadata_verified_payload_missing",
        _ => "source_metadata_upgrade_unknown",
    };
}

internal sealed record StudioSourceMetadataUpgradeReport(
    int TargetSchemaVersion,
    IReadOnlyList<StudioSourceMetadataUpgradeDecision> Decisions)
{
    public int ChangedCount => Decisions.Count(decision => decision.Changed);

    public int BoundCount =>
        Decisions.Count(decision => decision.BindingCreated);
}

/// <summary>
/// Versioned, idempotent upgrade boundary for source-local metadata.
///
/// Version 1 may backfill a completely missing legacy binding only when the
/// immutable contributor, editable Cloud registry stream, current account,
/// exact device, and verified physical payload all agree. Existing bindings
/// are never rewritten. Skipped legacy rows remain unversioned so a later
/// Source Check can retry after payload or registry evidence becomes valid.
/// </summary>
internal static class StudioSourceMetadataUpgradePolicy
{
    internal const string SchemaVersionKey =
        "local.metadataSchemaVersion";
    internal const int CurrentSchemaVersion = 1;

    public static StudioSourceMetadataUpgradeReport Apply(
        ProjectWorkspace project,
        string? currentAccountEmail,
        string? currentDeviceFingerprint,
        Func<ProjectDesignSource, bool> hasVerifiedLocalPayload)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(hasVerifiedLocalPayload);

        string account = NormalizeEmail(currentAccountEmail);
        string device = NormalizeDevice(currentDeviceFingerprint);
        var decisions = new List<StudioSourceMetadataUpgradeDecision>();
        foreach (ProjectDesignSource source in project.Sources ?? [])
        {
            decisions.Add(UpgradeSource(
                project,
                source,
                account,
                device,
                hasVerifiedLocalPayload));
        }

        return new StudioSourceMetadataUpgradeReport(
            CurrentSchemaVersion,
            decisions);
    }

    internal static int SchemaVersion(ProjectDesignSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.Metadata ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!source.Metadata.TryGetValue(
                SchemaVersionKey,
                out string? raw) ||
            string.IsNullOrWhiteSpace(raw))
        {
            return 0;
        }

        return int.TryParse(
            raw.Trim(),
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out int version)
            ? version
            : -1;
    }

    private static StudioSourceMetadataUpgradeDecision UpgradeSource(
        ProjectWorkspace project,
        ProjectDesignSource source,
        string account,
        string device,
        Func<ProjectDesignSource, bool> hasVerifiedLocalPayload)
    {
        int schemaVersion = SchemaVersion(source);
        if (schemaVersion > CurrentSchemaVersion)
        {
            return Decision(
                source,
                StudioSourceMetadataUpgradeReason.SchemaNewer,
                "Source metadata was written by a newer Studio version.");
        }
        if (schemaVersion == CurrentSchemaVersion)
        {
            return Decision(
                source,
                StudioSourceMetadataUpgradeReason.SchemaCurrent,
                "Source metadata is already at the current schema version.");
        }
        if (schemaVersion < 0)
        {
            return Decision(
                source,
                StudioSourceMetadataUpgradeReason.SchemaInvalid,
                "Source metadata schema version is invalid; no implicit repair was applied.");
        }

        bool hasAccountBinding =
            !string.IsNullOrWhiteSpace(
                StudioLocalSourceBindingPolicy.BindingAccountEmail(source));
        bool hasDeviceBinding =
            !string.IsNullOrWhiteSpace(
                StudioLocalSourceBindingPolicy.BindingDeviceFingerprint(source));
        if (hasAccountBinding || hasDeviceBinding)
        {
            if (!hasAccountBinding || !hasDeviceBinding)
            {
                return Decision(
                    source,
                    StudioSourceMetadataUpgradeReason.ExistingBindingIncomplete,
                    "A partial local binding exists; explicit relink is required and no field was overwritten.");
            }

            PersistSchemaVersion(source);
            return Decision(
                source,
                StudioSourceMetadataUpgradeReason.ExistingBindingVersioned,
                "The existing account/device binding was preserved and marked schema-current.",
                changed: true);
        }

        if (string.IsNullOrWhiteSpace(account) ||
            string.IsNullOrWhiteSpace(device))
        {
            return Decision(
                source,
                StudioSourceMetadataUpgradeReason.RuntimeIdentityMissing,
                "A signed-in account and exact device fingerprint are required.");
        }

        string immutableOwner =
            StudioLocalSourceBindingPolicy.ResolveLegacyImmutableOwner(
                project,
                source);
        if (string.IsNullOrWhiteSpace(immutableOwner))
        {
            return Decision(
                source,
                StudioSourceMetadataUpgradeReason.ImmutableOwnerMissing,
                "The source has no unambiguous immutable contributor.");
        }
        if (!immutableOwner.Equals(
                account,
                StringComparison.OrdinalIgnoreCase))
        {
            return Decision(
                source,
                StudioSourceMetadataUpgradeReason.ImmutableOwnerMismatch,
                $"The immutable contributor is '{immutableOwner}', not the signed-in account.");
        }

        ProjectSourceEditAuthority authority =
            ProjectCloudSyncAuthority.ResolveSource(
                project,
                source,
                account);
        if (!authority.CanEdit ||
            !authority.OwnerEmail.Equals(
                account,
                StringComparison.OrdinalIgnoreCase))
        {
            return Decision(
                source,
                StudioSourceMetadataUpgradeReason.SourceNotEditable,
                "The signed-in account is not the current editor of this immutable source stream.");
        }

        CloudRegistryIdentityStatus registryStatus =
            CloudRegistryIdentity(project, source, immutableOwner);
        if (registryStatus == CloudRegistryIdentityStatus.Missing)
        {
            return Decision(
                source,
                StudioSourceMetadataUpgradeReason.CloudRegistryIdentityMissing,
                "No complete Cloud registry row matches immutable owner + SourceKey.");
        }
        if (registryStatus == CloudRegistryIdentityStatus.Ambiguous)
        {
            return Decision(
                source,
                StudioSourceMetadataUpgradeReason.CloudRegistryIdentityAmbiguous,
                "Several active Cloud registry rows match immutable owner + SourceKey.");
        }

        if (!hasVerifiedLocalPayload(source))
        {
            return Decision(
                source,
                StudioSourceMetadataUpgradeReason.VerifiedPayloadMissing,
                "The exact local source/package payload could not be verified.");
        }

        StudioLocalSourceBindingPolicy.Bind(source, account, device);
        PersistSchemaVersion(source);
        return Decision(
            source,
            StudioSourceMetadataUpgradeReason.LegacyBindingBackfilled,
            "Legacy source binding was safely backfilled for this account and device.",
            changed: true,
            bindingCreated: true);
    }

    private static CloudRegistryIdentityStatus CloudRegistryIdentity(
        ProjectWorkspace project,
        ProjectDesignSource source,
        string immutableOwner)
    {
        if (!IsCloudLinked(project))
            return CloudRegistryIdentityStatus.Exact;

        string sourceKey =
            ProjectCloudSyncMetadata.CloudSourceKey(source).Trim();
        ProjectCloudSourceReference[] matches =
            (project.Cloud.SharedSources ?? [])
            .Where(candidate =>
                !string.Equals(
                    candidate.Status,
                    "Retired",
                    StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(candidate.SourceId) &&
                string.Equals(
                    candidate.SourceKey,
                    sourceKey,
                    StringComparison.OrdinalIgnoreCase) &&
                StudioSharedSourceProjection.ImmutableOwner(candidate).Equals(
                    immutableOwner,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length switch
        {
            1 => CloudRegistryIdentityStatus.Exact,
            > 1 => CloudRegistryIdentityStatus.Ambiguous,
            _ => CloudRegistryIdentityStatus.Missing,
        };
    }

    private static bool IsCloudLinked(ProjectWorkspace project) =>
        project.Cloud.Origin.Equals(
            ProjectOrigins.Cloud,
            StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(project.Cloud.ServerProjectId);

    private static void PersistSchemaVersion(ProjectDesignSource source)
    {
        source.Metadata ??=
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        source.Metadata[SchemaVersionKey] =
            CurrentSchemaVersion.ToString(CultureInfo.InvariantCulture);
    }

    private static StudioSourceMetadataUpgradeDecision Decision(
        ProjectDesignSource source,
        StudioSourceMetadataUpgradeReason reason,
        string detail,
        bool changed = false,
        bool bindingCreated = false) =>
        new(
            source.Id?.Trim() ?? "",
            reason,
            changed,
            bindingCreated,
            detail);

    private static string NormalizeEmail(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private static string NormalizeDevice(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();

    private enum CloudRegistryIdentityStatus
    {
        Exact,
        Missing,
        Ambiguous,
    }
}
