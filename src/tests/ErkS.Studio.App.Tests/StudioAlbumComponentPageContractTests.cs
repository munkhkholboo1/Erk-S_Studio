using Xunit;

namespace ErkS.Studio.Tests;

public sealed class StudioAlbumComponentPageContractTests
{
    [Fact]
    public void ExtractedComponentRebasesCanonicalAlbumPagesToComponentPdf()
    {
        var component = new StudioCloudAlbumSection
        {
            Code = "source:architect:school",
            PageNumbers = [5, 7],
            Pages =
            [
                new StudioCloudAlbumComponentPage
                {
                    PageNumber = 7,
                    PageKey = "album-page:b",
                    SortKey = "A-10",
                    SequenceKey = "floor-plans",
                },
                new StudioCloudAlbumComponentPage
                {
                    PageNumber = 5,
                    PageKey = "album-page:a",
                    SortKey = "A-2",
                    SequenceKey = "floor-plans",
                },
            ],
        };

        List<StudioCloudAlbumComponentPage> rebased =
            ShellView.RebaseComponentPages(component);

        Assert.Equal([1, 2], rebased.Select(page => page.PageNumber));
        Assert.Equal(
            ["album-page:a", "album-page:b"],
            rebased.Select(page => page.PageKey));
        Assert.Equal(["A-2", "A-10"], rebased.Select(page => page.SortKey));
    }
}
