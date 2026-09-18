using System.Net;
using System.Text;
using ErkS.CloudEra.Client.Generated;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The wire speaks camelCase; the file on disk speaks PascalCase.
///
/// 🔴 THE SAME DOCUMENT, TWO SPELLINGS, AND ONE READER WOULD SILENTLY RETURN NOTHING.
/// Studio's response store reads `<code>.responses.json` with the default naming policy —
/// `SurveyId`, `Responses`, `QuestionId`. The Cloud ERA route returns the same document
/// through ASP.NET's default, which is camelCase. Reusing the file reader on the HTTP body
/// would deserialise every field to null: no exception, HTTP 200, an empty list, and a
/// survey that looks as though the public ignored it. SRV found the divergence before
/// either side had written a line against it.
///
/// ⚠ SO THIS TEST ASSERTS THE VALUES, NOT THE ABSENCE OF A CRASH. «It parsed» is exactly
/// what the broken version also does. Every field below is checked for CONTENT, because a
/// document of sixteen nulls parses perfectly.
/// </summary>
[Collection(StudioDataRootCollection.Name)]
public sealed class THEWIRESpellingMustNotArriveEmptyTests
{
    /// <summary>The body exactly as the server sends it — camelCase throughout.</summary>
    private const string WireBody = """
        {
          "surveyId": "05402a414b134b0cbb230795ea6b29c1",
          "cursor": "1:3f93d33ab042449fa071c38200f61beb",
          "collectedAtUtc": "2026-09-19T04:03:42+00:00",
          "sinceAccepted": false,
          "responses": [
            {
              "id": "3f93d33ab042449fa071c38200f61beb",
              "submittedAtUtc": "2026-09-19T04:03:40+00:00",
              "answers": [
                { "questionId": "5dea36c7c4e04f65ad9d5ef1675bd428",
                  "optionIds": ["dee41f55ad494ced9a511fd4d5136170"],
                  "text": "Өөр баг", "number": null },
                { "questionId": "f51095aa7cdd41ecb059267071d09e62",
                  "optionIds": [], "text": "", "number": 3 }
              ]
            }
          ]
        }
        """;

    [Fact]
    public async Task THEDOCUMENTArrivesPOPULATEDNotJustParsed()
    {
        var handler = new StubHandler(WireBody);
        using var httpClient = new HttpClient(handler);
        var client = new CloudEraGeneratedClient(httpClient)
        {
            BaseUrl = "https://erk-s.mn/",
            AccessToken = "access-token",
        };

        CitizenSurveyResponseFeedRecord document =
            await client.ListCloudEraCitizenSurveyResponsesAsync(
                "88659be5416a4853adb6232ac8c7d689",
                "05402a414b134b0cbb230795ea6b29c1",
                since: null,
                null,
                null,
                CancellationToken.None);

        // 🔴 CONTENT, NOT SHAPE. A document whose every field is null also «parses».
        Assert.Equal("05402a414b134b0cbb230795ea6b29c1", document.SurveyId);
        Assert.Equal("1:3f93d33ab042449fa071c38200f61beb", document.Cursor);
        Assert.Single(document.Responses);

        CitizenSurveyResponseRecord response = document.Responses.First();
        Assert.Equal("3f93d33ab042449fa071c38200f61beb", response.Id);
        Assert.Equal(2, response.Answers.Count);

        CitizenSurveyAnswerRecord written = response.Answers.First();
        Assert.Equal("5dea36c7c4e04f65ad9d5ef1675bd428", written.QuestionId);
        Assert.Equal(["dee41f55ad494ced9a511fd4d5136170"], written.OptionIds);

        // ⚠ THE WRITE-IN TRAVELS WITH ITS TICK — the decision that keeps four of the
        // owner's questions inside the statistics instead of becoming free text.
        Assert.Equal("Өөр баг", written.Text);
        Assert.Null(written.Number);

        // ⚠ AND A NUMBER IS A NUMBER. If it arrived as text the average, the range and the
        // histogram would all quietly disappear from the page.
        CitizenSurveyAnswerRecord counted = response.Answers.Last();
        Assert.Equal(3, counted.Number);
        Assert.Empty(counted.OptionIds);
    }

    [Fact]
    public async Task THECURSORIsSENTSoCollectionCanADVANCE()
    {
        // Without `since` on the query the server returns everything every time. It would
        // still be correct - the merge deduplicates - but the collection would never
        // narrow, and the cursor the server mints would be carried nowhere.
        var handler = new StubHandler(WireBody);
        using var httpClient = new HttpClient(handler);
        var client = new CloudEraGeneratedClient(httpClient)
        {
            BaseUrl = "https://erk-s.mn/",
            AccessToken = "access-token",
        };

        await client.ListCloudEraCitizenSurveyResponsesAsync(
            "88659be5416a4853adb6232ac8c7d689",
            "05402a414b134b0cbb230795ea6b29c1",
            since: "1:3f93d33ab042449fa071c38200f61beb",
            null,
            null,
            CancellationToken.None);

        string asked = handler.RequestUri?.AbsoluteUri ?? "";
        Assert.Contains("88659be5416a4853adb6232ac8c7d689", asked, StringComparison.Ordinal);
        Assert.Contains("05402a414b134b0cbb230795ea6b29c1", asked, StringComparison.Ordinal);
        Assert.Contains("since=", asked, StringComparison.Ordinal);
        Assert.Contains("3f93d33ab042449fa071c38200f61beb", asked, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANEMPTYCursorAsksForEVERYTHING()
    {
        // The first collection has no watermark. It must not send «since=» with nothing
        // after it and leave the server guessing what that means.
        var handler = new StubHandler(WireBody);
        using var httpClient = new HttpClient(handler);
        var client = new CloudEraGeneratedClient(httpClient)
        {
            BaseUrl = "https://erk-s.mn/",
            AccessToken = "access-token",
        };

        await client.ListCloudEraCitizenSurveyResponsesAsync(
            "p", "s", since: null, null, null, CancellationToken.None);

        Assert.DoesNotContain("since=", handler.RequestUri?.AbsoluteUri ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public async Task THEWRAPPERHandsCoreAPopulatedDocumentNotAnEmptyOne()
    {
        // 🔴 THE WHOLE JOURNEY IN ONE TEST: camelCase off the wire, through the generated
        // client, through the Studio wrapper, into the Core document the response store
        // merges. That conversion is the single place where the two spellings meet, and
        // if it were wrong every field would be null while everything still reported
        // success — which is exactly how a consultation comes back looking ignored.
        var handler = new StubHandler(WireBody);
        using var httpClient = new HttpClient(handler);
        var wrapper = new CloudEraGeneratedContractClient(httpClient);

        StudioCitizenSurveyFetch fetched =
            await wrapper.FetchCitizenSurveyResponsesAsync(
                new CloudEraClientContext("https://erk-s.mn/", "access-token"),
                "88659be5416a4853adb6232ac8c7d689",
                "05402a414b134b0cbb230795ea6b29c1",
                since: null);

        ErkS.Platform.Core.CitizenSurveyResponseDocument document = fetched.Document;
        Assert.Equal("05402a414b134b0cbb230795ea6b29c1", document.SurveyId);
        Assert.Equal("1:3f93d33ab042449fa071c38200f61beb", document.Cursor);
        Assert.Single(document.Responses);
        Assert.Equal("3f93d33ab042449fa071c38200f61beb", document.Responses[0].Id);
        Assert.Equal(2, document.Responses[0].Answers.Count);

        // ⚠ The two values that carry meaning rather than shape: a written word that
        // travels with its tick, and a number that is still a number.
        Assert.Equal("Өөр баг", document.Responses[0].Answers[0].Text);
        Assert.Equal(3, document.Responses[0].Answers[1].Number);

        // And it merges — the document is not merely populated, it is USABLE.
        var held = new ErkS.Platform.Core.CitizenSurveyResponseDocument
        {
            SurveyId = document.SurveyId,
        };
        Assert.Equal(
            1,
            ErkS.Platform.Core.CitizenSurveyResponseStore.Merge(
                held, document.Responses, document.Cursor));
        Assert.Equal("1:3f93d33ab042449fa071c38200f61beb", held.Cursor);
    }

    [Fact]
    public async Task AREFUSEDCursorIsCARRIEDBackNotSwallowed()
    {
        // 🔴 THE FLAG EXISTS TO MAKE A SILENT COST VISIBLE, so it has to reach the caller.
        // An unrecognised cursor makes the server return everything — correct, and
        // identical in every outward way to an ordinary first read. If the format ever
        // drifted, every collection would re-download the whole consultation forever:
        // working, green, and quietly heavier each time. A field nobody reads would leave
        // that exactly as invisible as it was before SRV added it.
        var handler = new StubHandler(WireBody);
        using var httpClient = new HttpClient(handler);
        var wrapper = new CloudEraGeneratedContractClient(httpClient);

        StudioCitizenSurveyFetch fetched = await wrapper.FetchCitizenSurveyResponsesAsync(
            new CloudEraClientContext("https://erk-s.mn/", "access-token"),
            "p", "s", since: "1:aaaa");

        Assert.False(fetched.SinceAccepted);

        // And the page reports it rather than keeping it to itself.
        string page = File.ReadAllText(Path.Combine(
            FindSourceRoot().FullName, "ErkS.Studio.App", "ShellView.Surveys.cs"),
            System.Text.Encoding.UTF8);
        Assert.Contains("fetched.SinceAccepted", page, StringComparison.Ordinal);
        Assert.Contains("cursorRefused", page, StringComparison.Ordinal);
    }

    private static DirectoryInfo FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src");
            if (Directory.Exists(candidate))
                return new DirectoryInfo(candidate);
            directory = directory.Parent;
        }

        Assert.Fail("the source tree was not found; this test reads it");
        return new DirectoryInfo(".");
    }

    private sealed class StubHandler(string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            return Task.FromResult(response);
        }
    }
}
