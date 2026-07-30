using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ErkS.CloudEra.Client.Generated;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class GeneratedCloudEraClientRuntimeTests
{
    [Fact]
    public async Task GeneratedClient_AppliesServerAuthenticationAndRelationshipBoundary()
    {
        var handler = new RecordingHandler();
        using var httpClient = new HttpClient(handler);
        var client = new CloudEraGeneratedClient(httpClient)
        {
            BaseUrl = "https://erk-s.mn/",
            AccessToken = "access-token",
        };

        CloudEraProjectListResponse response = await client.ListCloudEraProjectsAsync(CancellationToken.None);

        Assert.Empty(response.Projects);
        Assert.Equal("https://erk-s.mn/api/cloud-era/v1/projects", handler.RequestUri?.AbsoluteUri);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "access-token"), handler.Authorization);
        Assert.Equal(
            "ERKS-RELATIONSHIP-BOUNDARY-2026-07-17",
            handler.RelationshipBoundaryAcknowledgement);
    }

    [Fact]
    public async Task ContractWrapper_MapsGeneratedProjectResponseToStudioModel()
    {
        const string json = """
            {
              "apiVersion": "1.0",
              "serverTimeUtc": "2026-07-18T00:00:00Z",
              "projects": [
                {
                  "projectId": "project-1",
                  "projectCode": "STUDIO-001",
                  "name": "Vector project",
                  "status": "Active",
                  "currentStage": "ConceptDesign",
                  "templateId": "MN-BLD-ARCH-CONCEPT",
                  "templateVersion": "1",
                  "clientName": "Client",
                  "planningAuthorityName": "Authority",
                  "designOrganizationName": "Erk-S LLC",
                  "updatedAtUtc": "2026-07-18T00:00:00Z",
                  "currentUserRoles": ["ProjectAdmin"],
                  "currentUserScopes": ["project:write"],
                  "currentUserIsCreator": true,
                  "concurrencyToken": "etag-1"
                }
              ]
            }
            """;
        var handler = new RecordingHandler(json);
        using var httpClient = new HttpClient(handler);
        var client = new CloudEraGeneratedContractClient(httpClient);

        StudioCloudProjectListResponse response = await client.ListProjectsAsync(
            new CloudEraClientContext("https://erk-s.mn", "access-token"),
            CancellationToken.None);

        StudioCloudProjectSummary project = Assert.Single(response.Projects);
        Assert.Equal("project-1", project.ProjectId);
        Assert.Equal("STUDIO-001", project.ProjectCode);
        Assert.Equal("etag-1", project.ConcurrencyToken);
    }

    [Fact]
    public async Task ContractWrapper_PreservesCanonicalAlbumRebuildSignal()
    {
        const string tombstone =
            "generated:building-sub-cover:studio-building:deleted-a";
        const string json = """
            [
              {
                "albumId": "album-1",
                "designPackageId": "package-1",
                "albumType": "BuildingArchitectureConcept",
                "title": "Concept album",
                "currentRevisionId": "revision-7",
                "requiredBuildingCompositionVersion": 4,
                "canonicalRebuildPending": true,
                "canonicalReflowRequired": true,
                "pendingComponentTombstoneCodes": [
                  "generated:building-sub-cover:studio-building:deleted-a"
                ],
                "revisions": [
                  {
                    "revisionId": "revision-7",
                    "revisionNumber": 7,
                    "pdfFileId": "file-7",
                    "pdfSha256": "hash-7",
                    "sourceUploadSha256": "source-hash-7",
                    "pageCount": 3,
                    "pageSizeSummary": "A3",
                    "buildingCompositionVersion": 3,
                    "status": "Draft",
                    "projectSnapshotId": "project-snapshot-7",
                    "organizationSnapshotId": "organization-snapshot-7",
                    "createdAtUtc": "2026-07-30T00:00:00Z",
                    "sectionManifest": [
                      {
                        "code": "source:owner:general-plan",
                        "label": "General plan",
                        "order": 100500,
                        "pageNumbers": [1, 2, 3],
                        "status": "Available",
                        "ownerEmail": "owner@example.com",
                        "sourceKey": "general-plan",
                        "componentKind": "Source",
                        "sectionKey": "fixed:Ерөнхий төлөвлөгөө",
                        "sequenceKey": "traffic-scheme",
                        "pages": [
                          {
                            "pageNumber": 1,
                            "pageKey": "album-page:stable",
                            "sortKey": "GP-2",
                            "sectionKey": "source-building:general-plan",
                            "sequenceKey": "traffic-scheme"
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            ]
            """;
        var handler = new RecordingHandler(json);
        using var httpClient = new HttpClient(handler);
        var client = new CloudEraGeneratedContractClient(httpClient);

        IReadOnlyList<StudioCloudAlbum> albums = await client.ListAlbumsAsync(
            new CloudEraClientContext("https://erk-s.mn", "access-token"),
            "project-1",
            CancellationToken.None);

        StudioCloudAlbum album = Assert.Single(albums);
        Assert.True(album.CanonicalRebuildPending);
        Assert.True(album.CanonicalReflowRequired);
        Assert.Equal(4, album.RequiredBuildingCompositionVersion);
        Assert.Equal([tombstone], album.PendingComponentTombstoneCodes);
        StudioCloudAlbumRevision revision = Assert.Single(album.Revisions);
        Assert.Equal(3, revision.BuildingCompositionVersion);
        Assert.Equal("source-hash-7", revision.SourceUploadSha256);
        StudioCloudAlbumSection component =
            Assert.Single(revision.SectionManifest);
        Assert.Equal("fixed:Ерөнхий төлөвлөгөө", component.SectionKey);
        Assert.Equal("traffic-scheme", component.SequenceKey);
        StudioCloudAlbumComponentPage page = Assert.Single(component.Pages);
        Assert.Equal("album-page:stable", page.PageKey);
        Assert.Equal("GP-2", page.SortKey);
    }

    [Fact]
    public async Task ContractWrapper_PropagatesServerTraceIdentifierFromErrorBody()
    {
        const string json = """
            {
              "code": "project_conflict",
              "message": "Project changed.",
              "traceId": "server-trace-generated",
              "currentSourceId": "source-current",
              "currentRevisionId": "revision-current"
            }
            """;
        var handler = new RecordingHandler(json, HttpStatusCode.PreconditionFailed);
        using var httpClient = new HttpClient(handler);
        var client = new CloudEraGeneratedContractClient(httpClient);

        StudioAccountException error = await Assert.ThrowsAsync<StudioAccountException>(
            () => client.ListProjectsAsync(
                new CloudEraClientContext("https://erk-s.mn", "access-token"),
                CancellationToken.None));

        Assert.Equal("project_conflict", error.ErrorCode);
        Assert.Equal("server-trace-generated", error.TraceId);
        Assert.Equal("source-current", error.CurrentSourceId);
        Assert.Equal("revision-current", error.CurrentRevisionId);
        Assert.Equal(
            "source-current",
            error.FieldErrors["currentSourceId"].Single());
        Assert.Equal(
            "revision-current",
            error.FieldErrors["currentRevisionId"].Single());
    }

    [Fact]
    public async Task ContractWrapper_SerializesExpectedBaseSourceIdForSourceCas()
    {
        const string json = """
            {
              "sourceId": "source-new",
              "sourceKey": "source-key",
              "sourcePurpose": "Building",
              "registeredBy": "owner@erks.local",
              "contentHash": "hash-new"
            }
            """;
        var handler = new RecordingHandler(json);
        using var httpClient = new HttpClient(handler);
        var client = new CloudEraGeneratedContractClient(httpClient);

        StudioCloudSourcePackage registered = await client.RegisterSourcePackageAsync(
            new CloudEraClientContext("https://erk-s.mn", "access-token"),
            "project-1",
            new StudioCloudSourcePackageCreateRequest
            {
                ExpectedBaseSourceId = "source-current",
                SourceKey = "source-key",
                SourceApplication = "Revit",
                SourcePurpose = "Building",
                ManifestId = "manifest-2",
                ContentHash = "hash-new",
            },
            CancellationToken.None);

        using JsonDocument body = JsonDocument.Parse(
            Assert.IsType<string>(handler.RequestBody));
        Assert.Equal(
            "source-current",
            body.RootElement
                .GetProperty("expectedBaseSourceId")
                .GetString());
        Assert.Equal(
            "Building",
            body.RootElement
                .GetProperty("sourcePurpose")
                .GetString());
        Assert.Equal("Building", registered.SourcePurpose);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly string responseJson;

        private readonly HttpStatusCode statusCode;

        public RecordingHandler(
            string? responseJson = null,
            HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            this.statusCode = statusCode;
            this.responseJson = responseJson ?? """
                {
                  "apiVersion": "1.0",
                  "serverTimeUtc": "2026-07-18T00:00:00Z",
                  "projects": []
                }
                """;
        }

        public Uri? RequestUri { get; private set; }
        public AuthenticationHeaderValue? Authorization { get; private set; }
        public string? RelationshipBoundaryAcknowledgement { get; private set; }
        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            Authorization = request.Headers.Authorization;
            RelationshipBoundaryAcknowledgement = request.Headers.TryGetValues(
                "X-ErkS-Relationship-Boundary",
                out IEnumerable<string>? values)
                ? values.Single()
                : null;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            };
        }
    }
}
