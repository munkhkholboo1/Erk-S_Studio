using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ErkS.Studio;
using Xunit;

namespace ErkS.Studio.App.Tests;

public sealed class CloudEraAlbumComponentUploaderTests
{
    [Fact]
    public async Task MergeAsync_UploadsAllComponentsInSingleAtomicRequest()
    {
        string firstPath = await WritePdfAsync("first-component");
        string secondPath = await WritePdfAsync("second-component");
        try
        {
            RecordingComponentHandler handler = new();
            using HttpClient client = new(handler);

            StudioCloudAlbumRevision revision = await CloudEraAlbumComponentUploader.MergeAsync(
                client,
                "https://erk-s.mn",
                "access-token",
                "project1",
                "album1",
                "revision1",
                "token1",
                [
                    new StudioAlbumComponentUpload(
                        "atd",
                        "ATD",
                        10,
                        firstPath,
                        SourceKey: "source1",
                        ComponentKind: "document"),
                    new StudioAlbumComponentUpload(
                        "sheets",
                        "Sheets",
                        20,
                        secondPath,
                        SourceKey: "source2",
                        ComponentKind: "source"),
                ],
                CancellationToken.None);

            Assert.Equal("revision2", revision.RevisionId);
            RecordedRequest request = Assert.Single(handler.Requests);
            Assert.Equal("revision1", request.ExpectedRevisionId);
            Assert.Equal("token1", request.ProjectConcurrencyToken);
            Assert.Collection(
                request.Descriptors,
                descriptor =>
                {
                    Assert.Equal("component0", descriptor.FieldName);
                    Assert.Equal("atd", descriptor.Code);
                    Assert.Equal("ATD", descriptor.Label);
                    Assert.Equal(10, descriptor.Order);
                    Assert.False(descriptor.Remove);
                    Assert.Equal("source1", descriptor.SourceKey);
                    Assert.Equal("document", descriptor.ComponentKind);
                },
                descriptor =>
                {
                    Assert.Equal("component1", descriptor.FieldName);
                    Assert.Equal("sheets", descriptor.Code);
                    Assert.Equal("Sheets", descriptor.Label);
                    Assert.Equal(20, descriptor.Order);
                    Assert.False(descriptor.Remove);
                    Assert.Equal("source2", descriptor.SourceKey);
                    Assert.Equal("source", descriptor.ComponentKind);
                });

            Assert.Equal(2, request.Files.Count);
            RecordedFile firstFile = request.Files["component0"];
            Assert.Equal(Path.GetFileName(firstPath), firstFile.FileName);
            Assert.Equal(await File.ReadAllBytesAsync(firstPath), firstFile.Bytes);
            RecordedFile secondFile = request.Files["component1"];
            Assert.Equal(Path.GetFileName(secondPath), secondFile.FileName);
            Assert.Equal(await File.ReadAllBytesAsync(secondPath), secondFile.Bytes);
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task MergeAsync_IncludesRemovalDescriptorWithoutFilePart()
    {
        string path = await WritePdfAsync("retained-component");
        try
        {
            RecordingComponentHandler handler = new();
            using HttpClient client = new(handler);

            await CloudEraAlbumComponentUploader.MergeAsync(
                client,
                "https://erk-s.mn",
                "access-token",
                "project1",
                "album1",
                "revision1",
                "token1",
                [
                    new StudioAlbumComponentUpload(
                        "retained",
                        "Retained",
                        10,
                        path,
                        SourceKey: "source1",
                        ComponentKind: "source"),
                    new StudioAlbumComponentUpload(
                        "removed",
                        "Removed",
                        20,
                        "",
                        Remove: true,
                        SourceKey: "source2",
                        ComponentKind: "source"),
                ],
                CancellationToken.None);

            RecordedRequest request = Assert.Single(handler.Requests);
            Assert.Equal(2, request.Descriptors.Count);
            StudioCloudAlbumComponentUploadDescriptor removal = request.Descriptors[1];
            Assert.Equal("component1", removal.FieldName);
            Assert.Equal("removed", removal.Code);
            Assert.True(removal.Remove);
            Assert.Single(request.Files);
            Assert.True(request.Files.ContainsKey("component0"));
            Assert.False(request.Files.ContainsKey("component1"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task MergeAsync_ReportsAllComponentNamesWhenServerRejectsBatchAsTooLarge()
    {
        string firstPath = await WritePdfAsync("large-component-1");
        string secondPath = await WritePdfAsync("large-component-2");
        try
        {
            using HttpClient client = new(new PayloadTooLargeHandler());

            StudioAccountException error = await Assert.ThrowsAsync<StudioAccountException>(
                () => CloudEraAlbumComponentUploader.MergeAsync(
                    client,
                    "https://erk-s.mn",
                    "access-token",
                    "project1",
                    "album1",
                    "revision1",
                    "token1",
                    [
                        new StudioAlbumComponentUpload("large1", "Large 1", 10, firstPath),
                        new StudioAlbumComponentUpload("large2", "Large 2", 20, secondPath),
                    ],
                    CancellationToken.None));

            Assert.Equal("album_component_too_large", error.ErrorCode);
            Assert.Equal("server-trace-large", error.TraceId);
            Assert.Equal("source:actual", error.FieldErrors["componentCode"].Single());
            Assert.Contains(Path.GetFileName(firstPath), error.Message, StringComparison.Ordinal);
            Assert.Contains(Path.GetFileName(secondPath), error.Message, StringComparison.Ordinal);
            Assert.Contains("HTTP 413", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task MergeAsync_RejectsMoreThanThirtyTwoDescriptors()
    {
        using HttpClient client = new(new RecordingComponentHandler());
        StudioAlbumComponentUpload[] components = Enumerable.Range(1, 33)
            .Select(index => new StudioAlbumComponentUpload(
                $"component-{index}",
                $"Component {index}",
                index,
                "",
                Remove: true))
            .ToArray();

        StudioAccountException error = await Assert.ThrowsAsync<StudioAccountException>(
            () => CloudEraAlbumComponentUploader.MergeAsync(
                client,
                "https://erk-s.mn",
                "access-token",
                "project1",
                "album1",
                "revision1",
                "token1",
                components,
                CancellationToken.None));

        Assert.Contains("32", error.Message, StringComparison.Ordinal);
    }

    private static async Task<string> WritePdfAsync(string marker)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(
            path,
            Encoding.ASCII.GetBytes($"%PDF-1.4\n% {marker}\n%%EOF"));
        return path;
    }

    private sealed class RecordingComponentHandler : HttpMessageHandler
    {
        private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal(
                "/api/cloud-era/v1/projects/project1/albums/album1/components",
                request.RequestUri?.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("access-token", request.Headers.Authorization?.Parameter);

            MultipartFormDataContent content = Assert.IsType<MultipartFormDataContent>(request.Content);
            List<HttpContent> parts = content.ToList();
            string descriptorJson = await ReadPartAsync(parts, "components", cancellationToken);
            List<StudioCloudAlbumComponentUploadDescriptor> descriptors =
                JsonSerializer.Deserialize<List<StudioCloudAlbumComponentUploadDescriptor>>(
                    descriptorJson,
                    json) ?? [];
            Dictionary<string, RecordedFile> files = [];
            foreach (HttpContent part in parts.Where(
                         part => part.Headers.ContentDisposition?.FileName is not null))
            {
                files.Add(
                    GetPartName(part),
                    new RecordedFile(
                        part.Headers.ContentDisposition!.FileName!.Trim('"'),
                        await part.ReadAsByteArrayAsync(cancellationToken)));
            }

            Requests.Add(
                new RecordedRequest(
                    await ReadPartAsync(parts, "expectedRevisionId", cancellationToken),
                    await ReadPartAsync(parts, "projectConcurrencyToken", cancellationToken),
                    descriptors,
                    files));

            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new StudioCloudAlbumRevision
                    {
                        RevisionId = "revision2",
                        RevisionNumber = 2,
                        Status = "Draft",
                    },
                    options: json),
            };
            response.Headers.ETag = new EntityTagHeaderValue("\"token2\"");
            return response;
        }

        private static async Task<string> ReadPartAsync(
            IEnumerable<HttpContent> parts,
            string name,
            CancellationToken cancellationToken) =>
            await Assert.Single(parts, part => GetPartName(part) == name)
                .ReadAsStringAsync(cancellationToken);

        private static string GetPartName(HttpContent part) =>
            part.Headers.ContentDisposition?.Name?.Trim('"') ?? "";
    }

    private sealed record RecordedRequest(
        string ExpectedRevisionId,
        string ProjectConcurrencyToken,
        List<StudioCloudAlbumComponentUploadDescriptor> Descriptors,
        Dictionary<string, RecordedFile> Files);

    private sealed record RecordedFile(string FileName, byte[] Bytes);

    private sealed class PayloadTooLargeHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge)
                {
                    Content = JsonContent.Create(
                        new StudioCloudApiError
                        {
                            Code = "request_too_large",
                            Message = "Payload too large.",
                            TraceId = "server-trace-large",
                            FieldErrors = new Dictionary<string, string[]>
                            {
                                ["componentCode"] = ["source:actual"],
                            },
                        }),
                });
    }
}
