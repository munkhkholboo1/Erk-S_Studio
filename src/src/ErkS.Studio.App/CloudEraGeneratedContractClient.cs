using System.Net;
using System.Net.Http;
using System.Text.Json;
using ErkS.CloudEra.Client.Generated;

namespace ErkS.Studio;

/// <summary>
/// 🔴 THE NULLS ARE THE DEVICE-FINGERPRINT HEADERS, AND THEIR POSITION IS LOAD-BEARING.
/// The server declares `X-ErkS-Device-Fingerprint` and its `-Legacy` twin on every Cloud
/// ERA operation. They are optional, and the generated code writes a header only when the
/// argument is not null - so null here sends exactly the request Studio sent before the
/// contract was regenerated.
///
/// ⚠ THE ORDER IS: path parameters, query parameters, THESE TWO, body, cancellation
/// token. Getting it wrong COMPILES, because every one of those parameters is a string:
/// my first attempt put the nulls first everywhere and quietly sent the project id as a
/// header on nine routes. Two runtime tests caught it; the compiler could not, and nine
/// other call sites had nothing watching them. If a new call is added here, read the
/// generated signature rather than copying the shape of the line above it.
/// </summary>
internal sealed class CloudEraGeneratedContractClient(HttpClient httpClient) : ICloudEraContractClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public Task<StudioCloudProjectListResponse> ListProjectsAsync(
        CloudEraClientContext context,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraProjectListResponse, StudioCloudProjectListResponse>(
            context,
            client => client.ListCloudEraProjectsAsync(null, null, cancellationToken));

    public Task<StudioCloudProjectDetail> GetProjectAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraProjectDetailDto, StudioCloudProjectDetail>(
            context,
            client => client.GetCloudEraProjectAsync(projectId, null, null, cancellationToken));

    public Task<StudioCloudProjectDetail> CreateProjectAsync(
        CloudEraClientContext context,
        StudioCloudProjectCreateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraProjectDetailDto, StudioCloudProjectDetail>(
            context,
            client => client.CreateCloudEraProjectAsync(
                null,
                null,
                Convert<CloudEraProjectCreateRequest>(request),
                cancellationToken));

    public Task<StudioCloudProjectDetail> AssignDesignOrganizationAsync(
        CloudEraClientContext context,
        string projectId,
        StudioCloudDesignOrganizationAssignmentRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraProjectDetailDto, StudioCloudProjectDetail>(
            context,
            client => client.AssignCloudEraDesignOrganizationAsync(
                projectId,
                null,
                null,
                Convert<CloudEraDesignOrganizationAssignmentRequest>(request),
                cancellationToken));

    public Task<StudioCloudStageAdvanceResponse> AdvanceProjectStageAsync(
        CloudEraClientContext context,
        string projectId,
        StudioCloudStageAdvanceRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraStageAdvanceResponse, StudioCloudStageAdvanceResponse>(
            context,
            client => client.AdvanceCloudEraProjectStageAsync(
                projectId,
                null,
                null,
                Convert<CloudEraStageAdvanceRequest>(request),
                cancellationToken));

    public Task<StudioCloudProjectDetail> UpdateParticipantRolesAsync(
        CloudEraClientContext context,
        string projectId,
        string participantId,
        StudioParticipantRoleUpdateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraProjectDetailDto, StudioCloudProjectDetail>(
            context,
            client => client.UpdateCloudEraParticipantRolesAsync(
                projectId,
                participantId,
                null,
                null,
                Convert<CloudEraParticipantRoleUpdateRequest>(request),
                cancellationToken));

    public Task<StudioCloudProjectDetail> AssignConceptArchitectAsync(
        CloudEraClientContext context,
        string projectId,
        StudioConceptArchitectAssignmentRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraProjectDetailDto, StudioCloudProjectDetail>(
            context,
            client => client.AssignCloudEraConceptArchitectAsync(
                projectId,
                null,
                null,
                Convert<CloudEraConceptArchitectAssignmentRequest>(request),
                cancellationToken));

    public Task<IReadOnlyList<StudioCloudDesignPackage>> ListDesignPackagesAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<ICollection<CloudEraDesignPackageDto>, IReadOnlyList<StudioCloudDesignPackage>>(
            context,
            client => client.ListCloudEraDesignPackagesAsync(projectId, null, null, cancellationToken));

    public Task<IReadOnlyList<StudioCloudAlbum>> ListAlbumsAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<ICollection<CloudEraAlbumDto>, IReadOnlyList<StudioCloudAlbum>>(
            context,
            client => client.ListCloudEraAlbumsAsync(projectId, null, null, cancellationToken));

    public Task<StudioCloudAlbum> EnsureConceptAlbumAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraAlbumDto, StudioCloudAlbum>(
            context,
            client => client.EnsureCloudEraConceptAlbumAsync(projectId, null, null, cancellationToken));

    public Task<StudioCloudSourcePackage> RegisterSourcePackageAsync(
        CloudEraClientContext context,
        string projectId,
        StudioCloudSourcePackageCreateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraSourcePackageDto, StudioCloudSourcePackage>(
            context,
            client => client.RegisterCloudEraSourcePackageAsync(
                projectId,
                null,
                null,
                Convert<CloudEraSourcePackageCreateRequest>(request),
                cancellationToken));

    private async Task<TStudio> ExecuteAsync<TGenerated, TStudio>(
        CloudEraClientContext context,
        Func<CloudEraGeneratedClient, Task<TGenerated>> operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(context.ServerUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(context.AccessToken);

        var client = new CloudEraGeneratedClient(httpClient)
        {
            BaseUrl = context.ServerUrl,
            AccessToken = context.AccessToken,
            RelationshipBoundaryPolicyVersion = StudioRelationshipBoundary.PolicyVersion,
        };

        try
        {
            TGenerated result = await operation(client).ConfigureAwait(false);
            return Convert<TStudio>(result!);
        }
        catch (ApiException exception)
        {
            throw ToStudioException(exception);
        }
    }

    private static T Convert<T>(object source)
    {
        try
        {
            byte[] json = JsonSerializer.SerializeToUtf8Bytes(source, source.GetType(), JsonOptions);
            return JsonSerializer.Deserialize<T>(json, JsonOptions)
                ?? throw new JsonException($"Cloud ERA response could not be converted to {typeof(T).Name}.");
        }
        catch (JsonException exception)
        {
            throw new StudioAccountException("Cloud ERA API contract mismatch: " + exception.Message);
        }
    }

    private static StudioAccountException ToStudioException(ApiException exception)
    {
        string code = "";
        string message = exception.Message;
        string traceId = "";
        string currentSourceId = "";
        string currentRevisionId = "";
        string currentOrganizationConcurrencyToken = "";
        IReadOnlyDictionary<string, string[]>? fieldErrors = null;
        if (!string.IsNullOrWhiteSpace(exception.Response))
        {
            try
            {
                StudioCloudApiError? error = JsonSerializer.Deserialize<StudioCloudApiError>(
                    exception.Response,
                    JsonOptions);
                if (error is not null)
                {
                    code = error.Code;
                    traceId = error.TraceId;
                    currentSourceId = error.CurrentSourceId;
                    currentRevisionId = error.CurrentRevisionId;
                    currentOrganizationConcurrencyToken = error.CurrentOrganizationConcurrencyToken;
                    fieldErrors = error.FieldErrors;
                    if (!string.IsNullOrWhiteSpace(error.Message))
                        message = error.Message;
                }
            }
            catch (JsonException)
            {
                // Preserve the generated client's controlled HTTP error when the body is not JSON.
            }
        }
        traceId = StudioCloudTraceIdentifier.Resolve(exception.Headers, traceId);

        return exception.StatusCode is >= 100 and <= 599
            ? new StudioAccountException(
                message,
                (HttpStatusCode)exception.StatusCode,
                code,
                traceId,
                fieldErrors,
                currentSourceId,
                currentRevisionId,
                currentOrganizationConcurrencyToken)
            : new StudioAccountException(message);
    }
}
