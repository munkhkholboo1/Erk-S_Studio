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

        CitizenSurveyResponseDocumentRecord document =
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
