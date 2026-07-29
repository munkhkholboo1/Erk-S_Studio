using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioMultiParticipantSyncReliabilityTests
{
    [Fact]
    public async Task AlbumComponentUploadConflict_DoesNotMutateLocalRevisionPointersOrPendingQueue()
    {
        var project = new ProjectWorkspace
        {
            Cloud = new ProjectCloudLink
            {
                LastSyncedRevisionId = "revision-base",
                LastSyncedAlbumSha256 = "base-hash",
                LastReceivedAlbumRevisionId = "revision-base",
                LastReceivedAlbumSha256 = "base-hash",
            },
        };
        ProjectCloudSyncMetadata.MarkAlbumComponentsPending(
            project,
            [ProjectCloudSyncMetadata.CoverComponentCode]);
        using var handler = new RevisionConflictHandler();
        using var client = new HttpClient(handler);

        StudioAccountException error = await Assert.ThrowsAsync<StudioAccountException>(
            () => CloudEraAlbumComponentUploader.MergeAsync(
                client,
                "https://erk-s.test",
                "access-token",
                "project-1",
                "album-1",
                "revision-base",
                "project-token-1",
                [
                    new StudioAlbumComponentUpload(
                        ProjectCloudSyncMetadata.CoverComponentCode,
                        "Cover",
                        0,
                        "",
                        Remove: true,
                        ComponentKind:
                            StudioAlbumComponentIdentity.GeneratedComponentKind),
                ],
                CancellationToken.None));

        Assert.Equal(System.Net.HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("album_revision_conflict", error.ErrorCode);
        Assert.Equal(1, handler.RequestCount);
        Assert.Equal("revision-base", project.Cloud.LastSyncedRevisionId);
        Assert.Equal("base-hash", project.Cloud.LastSyncedAlbumSha256);
        Assert.Equal("revision-base", project.Cloud.LastReceivedAlbumRevisionId);
        Assert.Equal("base-hash", project.Cloud.LastReceivedAlbumSha256);
        Assert.Equal(
            [ProjectCloudSyncMetadata.CoverComponentCode],
            ProjectCloudSyncMetadata.PendingAlbumComponents(project));
    }

    [Fact]
    public async Task IdenticalGeneratedComponentRetry_SendsOneDescriptorPerAttempt()
    {
        using var handler = new SuccessfulRetryHandler();
        using var client = new HttpClient(handler);
        StudioAlbumComponentUpload[] upload =
        [
            new StudioAlbumComponentUpload(
                ProjectCloudSyncMetadata.CoverComponentCode,
                "Cover",
                0,
                "",
                Remove: true,
                ComponentKind:
                    StudioAlbumComponentIdentity.GeneratedComponentKind),
        ];

        _ = await CloudEraAlbumComponentUploader.MergeAsync(
            client,
            "https://erk-s.test",
            "access-token",
            "project-1",
            "album-1",
            "revision-base",
            "project-token-1",
            upload,
            CancellationToken.None);
        _ = await CloudEraAlbumComponentUploader.MergeAsync(
            client,
            "https://erk-s.test",
            "access-token",
            "project-1",
            "album-1",
            "revision-base",
            "project-token-1",
            upload,
            CancellationToken.None);

        Assert.Equal([1, 1], handler.DescriptorCounts);
        Assert.Equal(
            [
                ProjectCloudSyncMetadata.CoverComponentCode,
                ProjectCloudSyncMetadata.CoverComponentCode,
            ],
            handler.DescriptorCodes);
    }

    [Fact]
    public void InterleavedAddUpdateRemove_PreservesForeignOwnerWithSameSourceKey()
    {
        const string sourceKey = "shared-source";
        const string localOwner = "architect-a@erks.local";
        const string foreignOwner = "architect-b@erks.local";
        var localSource = new ProjectDesignSource
        {
            Id = "local-source",
            Kind = DesignSourceKind.Revit,
        };
        var project = new ProjectWorkspace
        {
            Sources = [localSource],
        };
        ProjectCloudSyncMetadata.BindToCloudSource(project, localSource, sourceKey);
        ProjectCloudSyncMetadata.BindCloudOwner(localSource, localOwner);

        var initial = new StudioCloudBuildingComposition
        {
            Version = 1,
            Groups =
            [
                Group("building-a", 1),
                Group("building-b", 2),
            ],
            SheetAssignments =
            [
                Assignment(localOwner, sourceKey, "sheet-1", "building-a"),
                Assignment(foreignOwner, sourceKey, "sheet-1", "building-b"),
            ],
        };
        Assert.True(StudioBuildingCompositionSync.ApplyCanonical(
            project,
            new SheetLibrary(),
            initial,
            preserveLocalEdits: true));

        var interleavedUpdate = new StudioCloudBuildingComposition
        {
            Version = 2,
            Groups = initial.Groups,
            SheetAssignments =
            [
                Assignment(foreignOwner, sourceKey, "sheet-1", "building-a"),
                Assignment(localOwner, sourceKey, "sheet-2", "building-b"),
            ],
        };
        Assert.True(StudioBuildingCompositionSync.ApplyCanonical(
            project,
            new SheetLibrary(),
            interleavedUpdate,
            preserveLocalEdits: true));

        Assert.True(StudioBuildingCompositionSync.RemoveSourceAssignments(
            project,
            localSource,
            []));

        ProjectCloudBuildingSheetAssignmentReference retained =
            Assert.Single(project.Cloud.SharedBuildingSheetAssignments);
        Assert.Equal(foreignOwner, retained.SourceOwnerEmail);
        Assert.Equal(sourceKey, retained.SourceKey);
        Assert.Equal("sheet-1", retained.SheetId);
        Assert.Equal("building-a", retained.BuildingGroupId);

        StudioCloudBuildingCompositionUpdateRequest retry =
            StudioBuildingCompositionSync.CreateUpdate(
                project,
                new SheetLibrary());
        StudioCloudBuildingSheetAssignment uploaded =
            Assert.Single(retry.SheetAssignments);
        Assert.Equal(foreignOwner, uploaded.SourceOwnerEmail);
        Assert.Equal(sourceKey, uploaded.SourceKey);
        Assert.Equal("sheet-1", uploaded.SheetId);
        Assert.Equal("building-a", uploaded.BuildingGroupId);
    }

    [Fact]
    public void ShuffledArrivalReopenAndRepeatedNormalization_ConvergeWithoutDuplicates()
    {
        const string sourceKey = "same-source-key";
        const string planner = "planner@erks.local";
        const string architect = "architect@erks.local";
        const string buildingId = "building-2";
        ProjectWorkspace project = Project(
            sourceKey,
            planner,
            architect,
            buildingId);
        string generalPlan =
            StudioAlbumComponentIdentity.SourceCode(planner, sourceKey);
        string building =
            StudioAlbumComponentIdentity.SourceSliceCode(
                architect,
                sourceKey,
                "studio-building:" + buildingId,
                "floor-plans");
        string subCover =
            "generated:building-sub-cover:studio-building:" + buildingId;
        StudioCloudAlbumSection[] arrival =
        [
            SourceSection(building, architect, sourceKey, 4),
            Generated(ProjectCloudSyncMetadata.CoverComponentCode, 1),
            SourceSection(generalPlan, planner, sourceKey, 2),
            Generated(subCover, 3),
            Generated(ProjectCloudSyncMetadata.CoverComponentCode, 5),
        ];
        var sourceOrder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [generalPlan] = 0,
            [StudioAlbumComponentIdentity.SourceCode(architect, sourceKey)] = 1,
        };
        int[][] shuffledIndexes =
        [
            [0, 1, 2, 3, 4],
            [4, 3, 2, 1, 0],
            [2, 4, 1, 0, 3],
            [3, 0, 4, 2, 1],
        ];

        string[]? canonicalSignature = null;
        foreach (int[] indexes in shuffledIndexes)
        {
            StudioCloudAlbumSection[] shuffled =
                indexes.Select(index => arrival[index]).ToArray();
            StudioAlbumComponentManifestNormalizationPlan normalized =
                StudioAlbumComponentManifestNormalizer.CreatePlan(
                    project,
                    shuffled,
                    sourceOrder);

            Assert.True(normalized.RequiresPdfRewrite);
            Assert.Single(
                normalized.TargetManifest,
                component => component.Code.Equals(
                    ProjectCloudSyncMetadata.CoverComponentCode,
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal(4, normalized.TargetManifest.Count);
            string[] signature = normalized.TargetManifest
                .Select(Signature)
                .ToArray();
            canonicalSignature ??= signature;
            Assert.Equal(canonicalSignature, signature);

            IReadOnlyList<StudioCloudAlbumSection> reopened =
                normalized.TargetManifest;
            for (int retry = 0; retry < 4; retry++)
            {
                StudioAlbumComponentManifestNormalizationPlan repeated =
                    StudioAlbumComponentManifestNormalizer.CreatePlan(
                        project,
                        reopened,
                        sourceOrder);
                Assert.False(repeated.RequiresPdfRewrite);
                Assert.Equal(canonicalSignature, repeated.TargetManifest.Select(Signature));
                Assert.Equal(
                    repeated.TargetManifest.Count,
                    repeated.TargetManifest
                        .Select(component => component.Code)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count());
                reopened = repeated.TargetManifest;
            }
        }
    }

    private static ProjectWorkspace Project(
        string sourceKey,
        string planner,
        string architect,
        string buildingId) => new()
    {
        BuildingGroups =
        [
            new ProjectBuildingGroup
            {
                Id = buildingId,
                Name = "Building 2",
                Order = 2,
            },
        ],
        Cloud = new ProjectCloudLink
        {
            SharedSources =
            [
                new ProjectCloudSourceReference
                {
                    SourceKey = sourceKey,
                    SourceApplication = "Erk-S CityGen for AutoCAD",
                    RegisteredBy = planner,
                    OwnerEmail = planner,
                    Status = "Registered",
                },
                new ProjectCloudSourceReference
                {
                    SourceKey = sourceKey,
                    SourceApplication = "Revit",
                    RegisteredBy = architect,
                    OwnerEmail = architect,
                    Status = "Registered",
                },
            ],
            SharedBuildingSheetAssignments =
            [
                new ProjectCloudBuildingSheetAssignmentReference
                {
                    SourceOwnerEmail = architect,
                    SourceKey = sourceKey,
                    SheetId = "sheet-1",
                    BuildingGroupId = buildingId,
                },
            ],
        },
    };

    private static StudioCloudBuildingGroup Group(
        string id,
        int order) => new()
    {
        Id = id,
        Name = id,
        Order = order,
    };

    private static StudioCloudBuildingSheetAssignment Assignment(
        string owner,
        string sourceKey,
        string sheetId,
        string buildingGroupId) => new()
    {
        SourceOwnerEmail = owner,
        SourceKey = sourceKey,
        SheetId = sheetId,
        BuildingGroupId = buildingGroupId,
    };

    private static StudioCloudAlbumSection Generated(
        string code,
        int page) => new()
    {
        Code = code,
        Label = code,
        Order = 0,
        PageNumbers = [page],
        Status = "Available",
        ComponentKind = StudioAlbumComponentIdentity.GeneratedComponentKind,
    };

    private static StudioCloudAlbumSection SourceSection(
        string code,
        string owner,
        string sourceKey,
        int page) => new()
    {
        Code = code,
        Label = sourceKey,
        Order = 0,
        PageNumbers = [page],
        Status = "Available",
        OwnerEmail = owner,
        SourceKey = sourceKey,
        ComponentKind = StudioAlbumComponentIdentity.SourceComponentKind,
    };

    private static string Signature(StudioCloudAlbumSection component) =>
        $"{component.Code}|{component.Order}|{string.Join(",", component.PageNumbers)}";

    private sealed class RevisionConflictHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(
                System.Net.HttpStatusCode.Conflict)
            {
                Content = new StringContent(
                    """
                    {
                      "message": "The canonical revision changed.",
                      "code": "album_revision_conflict"
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json"),
            });
        }
    }

    private sealed class SuccessfulRetryHandler : HttpMessageHandler
    {
        public List<int> DescriptorCounts { get; } = [];
        public List<string> DescriptorCodes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            MultipartFormDataContent multipart =
                Assert.IsType<MultipartFormDataContent>(request.Content);
            HttpContent descriptorPart = Assert.Single(
                multipart,
                part => part.Headers.ContentDisposition?.Name?.Trim('"')
                    .Equals("components", StringComparison.Ordinal) == true);
            string json = await descriptorPart.ReadAsStringAsync(
                cancellationToken);
            using System.Text.Json.JsonDocument document =
                System.Text.Json.JsonDocument.Parse(json);
            System.Text.Json.JsonElement descriptors =
                document.RootElement;
            DescriptorCounts.Add(descriptors.GetArrayLength());
            DescriptorCodes.Add(
                Assert.Single(descriptors.EnumerateArray().ToArray())
                    .GetProperty("code")
                    .GetString()!);

            return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "revisionId": "revision-base",
                      "pdfSha256": "base-hash",
                      "sectionManifest": [
                        {
                          "code": "generated:cover:Cover",
                          "pageNumbers": [1],
                          "componentKind": "Generated"
                        }
                      ]
                    }
                    """,
                    System.Text.Encoding.UTF8,
                    "application/json"),
            };
        }
    }
}
