namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Laying a scanned document out across album pages.
///
/// The client gave both halves of this by example. How many fit: four faces on
/// an A2 sheet, two on A3. How they are spread: «Хэрэв 5 хуудас байвал 3, 2
/// хуудсаар салгаж хуудаслана. 6 байвал 3, 3. 7 байвал 4, 3. 8 байвал 4, 4.»
///
/// Filling each page to capacity would be the obvious implementation and would
/// give 4 and 1 for five faces - a page with one scan in the corner and three
/// empty quarters beside it. The examples are the specification precisely
/// because that is not what a person laying out a document does.
/// </summary>
public sealed class DocumentFaceDistributionTests
{
    [Theory]
    [InlineData(5, new[] { 3, 2 })]
    [InlineData(6, new[] { 3, 3 })]
    [InlineData(7, new[] { 4, 3 })]
    [InlineData(8, new[] { 4, 4 })]
    public void TheClientsOwnExamplesOnA2(int faces, int[] expected)
    {
        Assert.Equal(expected, DocumentFaceDistribution.Distribute(faces, capacity: 4));
    }

    [Theory]
    [InlineData(3, new[] { 2, 1 })]
    [InlineData(5, new[] { 2, 2, 1 })]
    public void TheSameRuleOnA3(int faces, int[] expected)
    {
        // Half the capacity, same spreading.
        Assert.Equal(expected, DocumentFaceDistribution.Distribute(faces, capacity: 2));
    }

    [Theory]
    [InlineData(1, 4, new[] { 1 })]
    [InlineData(4, 4, new[] { 4 })]
    [InlineData(2, 2, new[] { 2 })]
    public void ADocumentThatFitsOnOnePageGetsOnePage(int faces, int capacity, int[] expected)
    {
        Assert.Equal(expected, DocumentFaceDistribution.Distribute(faces, capacity));
    }

    [Fact]
    public void NoPageEverExceedsWhatFitsOnIt()
    {
        // The spreading rounds up, so it is worth stating that it cannot round
        // up past the sheet.
        for (int faces = 1; faces <= 40; faces++)
        {
            foreach (int capacity in new[] { 2, 4 })
            {
                Assert.All(
                    DocumentFaceDistribution.Distribute(faces, capacity),
                    count => Assert.InRange(count, 1, capacity));
            }
        }
    }

    [Fact]
    public void EveryFaceIsPlacedExactlyOnce()
    {
        for (int faces = 1; faces <= 40; faces++)
        {
            foreach (int capacity in new[] { 2, 4 })
            {
                Assert.Equal(faces, DocumentFaceDistribution.Distribute(faces, capacity).Sum());
            }
        }
    }

    [Fact]
    public void ThePagesNeverGrowTowardTheEnd()
    {
        // The odd face belongs on an earlier page. A document whose last page
        // is the fullest reads as though it ran out of room.
        for (int faces = 1; faces <= 40; faces++)
        {
            IReadOnlyList<int> counts = DocumentFaceDistribution.Distribute(faces, capacity: 4);
            for (int page = 1; page < counts.Count; page++)
                Assert.True(counts[page] <= counts[page - 1], $"{faces} faces: {string.Join(",", counts)}");
        }
    }

    [Fact]
    public void ThePageCountIsTheFewestThatCanHoldThem()
    {
        // Spreading must not cost an extra page.
        for (int faces = 1; faces <= 40; faces++)
        {
            foreach (int capacity in new[] { 2, 4 })
            {
                int fewest = (faces + capacity - 1) / capacity;
                Assert.Equal(fewest, DocumentFaceDistribution.Distribute(faces, capacity).Count);
            }
        }
    }

    [Fact]
    public void NothingToPlaceMakesNoPages()
    {
        // Whether an empty document still deserves a placeholder is the
        // caller's decision, not this one's.
        Assert.Empty(DocumentFaceDistribution.Distribute(0, capacity: 4));
    }

    [Theory]
    [InlineData(594, 420, 4)]
    [InlineData(420, 594, 4)]
    [InlineData(420, 297, 2)]
    [InlineData(297, 420, 2)]
    public void CapacityFollowsTheSheet(double widthMm, double heightMm, int expected)
    {
        // A2 either way up holds four; A3 holds two.
        Assert.Equal(expected, DocumentFaceDistribution.Capacity(widthMm, heightMm));
    }

    [Fact]
    public void ASheetSizeNobodyNamedGetsTheCautiousCount()
    {
        // A1 might well hold more. Nobody has said so, and guessing produces
        // scans too small to read on a page nobody checked.
        Assert.Equal(2, DocumentFaceDistribution.Capacity(210, 297));
    }
}
