using System.Net;
using System.Net.Http;
using System.Text.Json;
using ErkS.CloudEra.Client.Generated;

namespace ErkS.Studio;

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
            client => client.ListCloudEraProjectsAsync(cancellationToken));

    public Task<StudioCloudProjectDetail> GetProjectAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraProjectDetailDto, StudioCloudProjectDetail>(
            context,
            client => client.GetCloudEraProjectAsync(projectId, cancellationToken));

    public Task<StudioCloudProjectDetail> CreateProjectAsync(
        CloudEraClientContext context,
        StudioCloudProjectCreateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraProjectDetailDto, StudioCloudProjectDetail>(
            context,
            client => client.CreateCloudEraProjectAsync(
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
                Convert<CloudEraDesignOrganizationAssignmentRequest>(request),
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
                Convert<CloudEraConceptArchitectAssignmentRequest>(request),
                cancellationToken));

    public Task<IReadOnlyList<StudioCloudDesignPackage>> ListDesignPackagesAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<ICollection<CloudEraDesignPackageDto>, IReadOnlyList<StudioCloudDesignPackage>>(
            context,
            client => client.ListCloudEraDesignPackagesAsync(projectId, cancellationToken));

    public Task<IReadOnlyList<StudioCloudAlbum>> ListAlbumsAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<ICollection<CloudEraAlbumDto>, IReadOnlyList<StudioCloudAlbum>>(
            context,
            client => client.ListCloudEraAlbumsAsync(projectId, cancellationToken));

    public Task<StudioCloudAlbum> EnsureConceptAlbumAsync(
        CloudEraClientContext context,
        string projectId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraAlbumDto, StudioCloudAlbum>(
            context,
            client => client.EnsureCloudEraConceptAlbumAsync(projectId, cancellationToken));

    public Task<StudioCloudSourcePackage> RegisterSourcePackageAsync(
        CloudEraClientContext context,
        string projectId,
        StudioCloudSourcePackageCreateRequest request,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<CloudEraSourcePackageDto, StudioCloudSourcePackage>(
            context,
            client => client.RegisterCloudEraSourcePackageAsync(
                projectId,
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
                    if (!string.IsNullOrWhiteSpace(error.Message))
                        message = error.Message;
                }
            }
            catch (JsonException)
            {
                // Preserve the generated client's controlled HTTP error when the body is not JSON.
            }
        }

        return exception.StatusCode is >= 100 and <= 599
            ? new StudioAccountException(message, (HttpStatusCode)exception.StatusCode, code)
            : new StudioAccountException(message);
    }
}
