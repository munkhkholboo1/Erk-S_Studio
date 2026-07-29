using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ErkS.Studio;
using Xunit;

namespace ErkS.Studio.App.Tests;

public sealed class CloudEraChunkedAlbumUploaderTests
{
    [Fact]
    public async Task UploadAsync_PropagatesServerTraceIdentifierOnConflict()
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(path, Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF"));
        try
        {
            using HttpClient client = new(new ConflictHandler());

            StudioAccountException error = await Assert.ThrowsAsync<StudioAccountException>(
                () => CloudEraChunkedAlbumUploader.UploadAsync(
                    client,
                    "http://127.0.0.1:5055",
                    "token-value",
                    "project1",
                    "album1",
                    path,
                    pageCount: 1,
                    pageSizeSummary: "A4",
                    projectConcurrencyToken: "project-token-1",
                    expectedBaseRevisionId: "revision0",
                    inheritComponentManifest: true,
                    componentManifest: null,
                    CancellationToken.None));

            Assert.Equal("album_revision_conflict", error.ErrorCode);
            Assert.Equal("server-trace-chunk", error.TraceId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UploadAsync_SendsOrderedBoundedChunksAndCompletesRevision()
    {
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% vector-content\n%%EOF");
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(path, pdf);
        try
        {
            RecordingHandler handler = new(
                chunkSize: 7,
                expectedFile: pdf,
                expectedBaseRevisionId: "revision0",
                inheritComponentManifest: true);
            using HttpClient client = new(handler);

            StudioCloudAlbumRevision revision = await CloudEraChunkedAlbumUploader.UploadAsync(
                client,
                "http://127.0.0.1:5055",
                "token-value",
                "project1",
                "album1",
                path,
                pageCount: 4,
                pageSizeSummary: "A3",
                projectConcurrencyToken: "project-token-1",
                expectedBaseRevisionId: "revision0",
                inheritComponentManifest: true,
                componentManifest: null,
                CancellationToken.None);

            Assert.Equal("revision1", revision.RevisionId);
            Assert.Equal(pdf, handler.ReceivedFile);
            Assert.All(handler.AuthorizationSchemes, scheme => Assert.Equal("Bearer", scheme));
            Assert.Equal(handler.ExpectedChunkCount, handler.ChunkRequests);
            Assert.Equal(1, handler.CompleteRequests);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task UploadAsync_SendsSuppliedComponentManifestInStartRequest()
    {
        byte[] pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n% vector-content\n%%EOF");
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(path, pdf);
        try
        {
            RecordingHandler handler = new(
                chunkSize: 7,
                expectedFile: pdf,
                expectedBaseRevisionId: "revision0",
                expectComponentManifest: true);
            using HttpClient client = new(handler);

            await CloudEraChunkedAlbumUploader.UploadAsync(
                client,
                "http://127.0.0.1:5055",
                "token-value",
                "project1",
                "album1",
                path,
                pageCount: 4,
                pageSizeSummary: "A3",
                projectConcurrencyToken: "project-token-1",
                expectedBaseRevisionId: "revision0",
                inheritComponentManifest: false,
                componentManifest:
                [
                    new StudioCloudAlbumSection
                    {
                        Code = "generated:cover",
                        Label = "Cover",
                        Order = 0,
                        PageNumbers = [1],
                        Status = "Available",
                    },
                ],
                CancellationToken.None);

            Assert.Equal(1, handler.CompleteRequests);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class RecordingHandler(
        int chunkSize,
        byte[] expectedFile,
        string? expectedBaseRevisionId = null,
        bool inheritComponentManifest = false,
        bool expectComponentManifest = false) : HttpMessageHandler
    {
        private readonly List<byte[]> chunks = [];
        private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

        public int ChunkRequests { get; private set; }
        public int CompleteRequests { get; private set; }
        public int ExpectedChunkCount => (expectedFile.Length + chunkSize - 1) / chunkSize;
        public byte[] ReceivedFile => chunks.SelectMany(chunk => chunk).ToArray();
        public List<string?> AuthorizationSchemes { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            AuthorizationSchemes.Add(request.Headers.Authorization?.Scheme);
            string path = request.RequestUri?.AbsolutePath ?? "";
            if (request.Method == HttpMethod.Post && path.EndsWith("/revisions/uploads", StringComparison.Ordinal))
            {
                StudioCloudAlbumUploadStartRequest? start = await request.Content!
                    .ReadFromJsonAsync<StudioCloudAlbumUploadStartRequest>(json, cancellationToken);
                Assert.NotNull(start);
                Assert.Equal(expectedFile.LongLength, start.SizeBytes);
                Assert.Equal(4, start.PageCount);
                Assert.Equal("project-token-1", start.ProjectConcurrencyToken);
                Assert.Equal(expectedBaseRevisionId, start.ExpectedBaseRevisionId);
                Assert.Equal(inheritComponentManifest, start.InheritComponentManifest);
                if (expectComponentManifest)
                {
                    Assert.NotNull(start.ComponentManifest);
                    StudioCloudAlbumSection component = Assert.Single(start.ComponentManifest);
                    Assert.Equal("generated:cover", component.Code);
                    Assert.Equal([1], component.PageNumbers);
                }
                else
                {
                    Assert.Null(start.ComponentManifest);
                }
                return Json(new StudioCloudAlbumUploadSession
                {
                    UploadId = "upload1",
                    ChunkSizeBytes = chunkSize,
                    TotalChunks = ExpectedChunkCount,
                });
            }

            if (request.Method == HttpMethod.Put && path.Contains("/chunks/", StringComparison.Ordinal))
            {
                int index = int.Parse(path[(path.LastIndexOf('/') + 1)..]);
                byte[] body = await request.Content!.ReadAsByteArrayAsync(cancellationToken);
                Assert.Equal(index, chunks.Count);
                Assert.Equal(
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(body)).ToLowerInvariant(),
                    request.Headers.GetValues("X-Chunk-SHA256").Single());
                chunks.Add(body);
                ChunkRequests++;
                return Json(new StudioCloudAlbumUploadSession
                {
                    UploadId = "upload1",
                    ChunkSizeBytes = chunkSize,
                    TotalChunks = ExpectedChunkCount,
                    ReceivedChunks = Enumerable.Range(0, chunks.Count).ToArray(),
                });
            }

            if (request.Method == HttpMethod.Post && path.EndsWith("/complete", StringComparison.Ordinal))
            {
                CompleteRequests++;
                Assert.Equal(expectedFile, ReceivedFile);
                return Json(new StudioCloudAlbumRevision
                {
                    RevisionId = "revision1",
                    RevisionNumber = 1,
                    PdfSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expectedFile)).ToLowerInvariant(),
                    PageCount = 4,
                    PageSizeSummary = "A3",
                    Status = "Draft",
                });
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private HttpResponseMessage Json<T>(T value) => new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(value, options: json),
        };
    }

    private sealed class ConflictHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(
                new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = JsonContent.Create(
                        new StudioCloudApiError
                        {
                            Code = "album_revision_conflict",
                            Message = "Album changed.",
                            TraceId = "server-trace-chunk",
                        }),
                });
    }
}
