namespace ErkS.CloudEra.Client;

public sealed record CloudEraCapabilitiesSnapshot(
    string ApiVersion,
    string ApiBasePath,
    string OpenApiDocumentPath,
    IReadOnlyDictionary<string, bool> Features);

public sealed class CloudEraContractMismatchException : Exception
{
    public int? StatusCode { get; }

    public CloudEraContractMismatchException(string message, int? statusCode = null) : base(message)
    {
        StatusCode = statusCode;
    }
}

public static class CloudEraCapabilityPolicy
{
    public const string SupportedApiVersion = "1.0";

    public static void EnsureCompatible(CloudEraCapabilitiesSnapshot capabilities)
    {
        ArgumentNullException.ThrowIfNull(capabilities);
        if (!Version.TryParse(capabilities.ApiVersion, out Version? server) ||
            !Version.TryParse(SupportedApiVersion, out Version? client) ||
            server.Major != client.Major)
        {
            throw new CloudEraContractMismatchException(
                $"Cloud ERA API version '{capabilities.ApiVersion}' is not compatible with Studio API version '{SupportedApiVersion}'.");
        }
    }

    public static bool Supports(CloudEraCapabilitiesSnapshot capabilities, string feature)
    {
        EnsureCompatible(capabilities);
        return capabilities.Features.TryGetValue(feature, out bool enabled) && enabled;
    }

    public static void Require(CloudEraCapabilitiesSnapshot capabilities, string feature)
    {
        if (!Supports(capabilities, feature))
        {
            throw new CloudEraContractMismatchException(
                $"Cloud ERA server does not advertise required feature '{feature}'.");
        }
    }
}

public static class CloudEraFeatures
{
    public const string Projects = "projects";
    public const string Organizations = "organizations";
    public const string Collaboration = "collaboration";
    public const string ConceptArchitectAssignment = "concept-architect-assignment";
    public const string ParticipantRoleManagement = "participant-role-management";
    public const string SourcePackagesV4 = "source-packages-v4";
    public const string AlbumRevisions = "album-revisions";
    public const string AlbumComponentMergeV1 = "album-component-merge-v1";
    public const string ChunkedAlbumUploadsV1 = "chunked-album-uploads-v1";
    public const string OptimisticConcurrency = "optimistic-concurrency";
    public const string IdempotentSync = "idempotent-sync";
    public const string RelationshipBoundary = "relationship-boundary";
    public const string NativeSourceRemainsLocal = "native-source-remains-local";
}
