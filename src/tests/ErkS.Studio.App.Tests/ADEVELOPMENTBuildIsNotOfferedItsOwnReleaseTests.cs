using System.Text.Json;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Owner, 2026-11-15: «студио шинэчлэлт хийж чадахгуй байна» - the desktop's development Studio (0.001.78-dev)
/// was offered v0.001.180, its own published self, signed by the release publisher a development build does not
/// trust, and the install failed. A development build asks as the release it was last published as; the dialog
/// shows the Mongolian notes the release was published with.
/// </summary>
public sealed class ADEVELOPMENTBuildIsNotOfferedItsOwnReleaseTests
{
    [Fact]
    public void ADEVELOPMENTBuildAsksAsItsPublishedRelease()
    {
        Assert.True(StudioReleaseInfo.IsDevelopmentBuild);
        Assert.Matches(@"^\d+\.\d+\.\d+$", StudioReleaseInfo.PublishedCounterpartVersion);
        Assert.Equal(StudioReleaseInfo.PublishedCounterpartVersion, StudioReleaseInfo.UpdateCheckVersion);
    }

    [Fact]
    public void THEDialogShowsTheMongolianNotes()
    {
        StudioUpdateLatestResponse update = JsonSerializer.Deserialize<StudioUpdateLatestResponse>(
            """{"releaseNotes":"Studio: grouped by location.","releaseNotesMn":"Studio: байршлаар бүлэглэнэ"}""",
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

        Assert.Equal("Studio: байршлаар бүлэглэнэ", update.DisplayNotes);
        Assert.Equal("only english", new StudioUpdateLatestResponse { ReleaseNotes = "only english" }.DisplayNotes);
    }
}
