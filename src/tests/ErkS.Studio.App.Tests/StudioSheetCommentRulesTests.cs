using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioSheetCommentRulesTests
{
    [Fact]
    public void AlbumPageIdentity_NamesAPageTheSameWayForEverybodyLookingAtIt()
    {
        // The author holds the source and the reviewer holds none of it, but
        // both read the page key out of the shared album. If these two ever
        // disagreed, a reviewer would write into a conversation the author
        // never sees.
        string author = StudioSheetCommentRules.AlbumPageIdentity("AR-02:Plan:7");
        string reviewer = StudioSheetCommentRules.AlbumPageIdentity("ar-02:plan:7");

        Assert.Equal(author, reviewer);
        Assert.StartsWith("album:", author, StringComparison.Ordinal);
    }

    [Fact]
    public void AlbumPageIdentity_RefusesAPageWithNoKey()
    {
        Assert.Equal("", StudioSheetCommentRules.AlbumPageIdentity(null));
        Assert.Equal("", StudioSheetCommentRules.AlbumPageIdentity("   "));
    }

    [Fact]
    public void AlbumPageIdentity_DoesNotCollideWithTheNameGivenToALocalSheet()
    {
        Assert.NotEqual(
            StudioSheetCommentRules.AlbumPageIdentity("plan-7"),
            StudioSheetCommentRules.PageIdentity(sheet: null, generatedKey: "plan-7"));
    }
}
