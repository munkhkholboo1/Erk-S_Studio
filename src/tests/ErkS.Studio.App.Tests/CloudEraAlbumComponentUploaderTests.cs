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
    public async Task MergeAsync_UploadsComponentsSeparatelyAndChainsRevisionAndProjectToken()
    {
        string firstPath = await WritePdfAsync("first-component");
        string secondPath = await WritePdfAsync("second-component");
        try
        {
            SequentialComponentHandler handler = new(firstPath, secondPath);
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

            Assert.Equal("revision3", revision.RevisionId);
            Assert.Equal(2, handler.RequestCount);
        }
        finally
        {
            File.Delete(firstPath);
            File.Delete(secondPath);
        }
    }

    [Fact]
    public async Task MergeAsync_ReportsComponentNameWhenServerRejectsLargeUpload()
    {
        string path = await WritePdfAsync("large-component");
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
                    [new StudioAlbumComponentUpload("large", "Large", 10, path)],
                    CancellationToken.None));

            Assert.Equal("album_component_too_large", error.ErrorCode);
            Assert.Contains(Path.GetFileName(path), error.Message, StringComparison.Ordinal);
            Assert.Contains("HTTP 413", error.Message, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static async Task<string> WritePdfAsync(string marker)
    {
        string path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(
            path,
            Encoding.ASCII.GetBytes($"%PDF-1.4\n% {marker}\n%%EOF"));
        return path;
    }

    private sealed class SequentialComponentHandler(string firstPath, string secondPath)
        : HttpMessageHandler
    {
        private readonly JsonSerializerOptions json = new(JsonSerializerDefaults.Web);

        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            Assert.Equal(HttpMethod.Put, request.Method);
            Assert.Equal(
                "/api/cloud-era/v1/projects/project1/albums/album1/components",
                request.RequestUri?.AbsolutePath);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("access-token", request.Headers.Authorization?.Parameter);

            MultipartFormDataContent content = Assert.IsType<MultipartFormDataContent>(request.Content);
            List<HttpContent> parts = content.ToList();
            Assert.Equal(4, parts.Count);
            Assert.Equal(
                RequestCount == 1 ? "revision1" : "revision2",
                await ReadPartAsync(parts, "expectedRevisionId", cancellationToken));
            Assert.Equal(
                RequestCount == 1 ? "token1" : "token2",
                await ReadPartAsync(parts, "projectConcurrencyToken", cancellationToken));

            string descriptorJson = await ReadPartAsync(parts, "components", cancellationToken);
            string expectedPath = RequestCount == 1 ? firstPath : secondPath;
            string unexpectedPath = RequestCount == 1 ? secondPath : firstPath;
            Assert.Contains(RequestCount == 1 ? "\"code\":\"atd\"" : "\"code\":\"sheets\"", descriptorJson);

            HttpContent file = Assert.Single(
                parts,
                part => GetPartName(part) == "component0");
            Assert.Equal(Path.GetFileName(expectedPath), file.Headers.ContentDisposition?.FileName?.Trim('"'));
            byte[] actualBytes = await file.ReadAsByteArrayAsync(cancellationToken);
            Assert.Equal(await File.ReadAllBytesAsync(expectedPath, cancellationToken), actualBytes);
            Assert.DoesNotContain(
                Encoding.ASCII.GetString(await File.ReadAllBytesAsync(unexpectedPath, cancellationToken)),
                Encoding.ASCII.GetString(actualBytes),
                StringComparison.Ordinal);

            string revisionId = RequestCount == 1 ? "revision2" : "revision3";
            string token = RequestCount == 1 ? "token2" : "token3";
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(
                    new StudioCloudAlbumRevision
                    {
                        RevisionId = revisionId,
                        RevisionNumber = RequestCount + 1,
                        Status = "Draft",
                    },
                    options: json),
            };
            response.Headers.ETag = new EntityTagHeaderValue($"\"{token}\"");
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
                        }),
                });
    }
}
