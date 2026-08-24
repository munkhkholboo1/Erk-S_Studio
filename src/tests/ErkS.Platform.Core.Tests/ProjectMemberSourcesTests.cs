using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Attributing a source to a person only works when the data says whose it is.
/// </summary>
public sealed class ProjectMemberSourcesTests
{
    private static ProjectCloudSourceReference Source(
        string registeredBy,
        string document = "",
        int sheets = 0) => new()
        {
            SourceId = Guid.NewGuid().ToString("N"),
            SourceKey = "key-" + document,
            SourceApplication = "Revit",
            SourceDocumentReference = document,
            SheetCount = sheets,
            RegisteredBy = registeredBy,
        };

    [Fact]
    public void OnlyTheSourcesThisPersonRegisteredAreCounted()
    {
        ProjectMemberSourceSummary summary = ProjectMemberSources.For(
            [
                Source("a@erk-s.mn", "Блок А", 12),
                Source("b@erk-s.mn", "Блок Б", 30),
                Source("a@erk-s.mn", "Блок В", 8),
            ],
            "a@erk-s.mn");

        Assert.Equal(2, summary.Count);
        Assert.Equal(20, summary.SheetCount);
        Assert.Equal(["Блок А", "Блок В"], summary.Names);
    }

    [Fact]
    public void TheEmailIsMatchedWithoutCaseOrSpacing()
    {
        // RegisteredBy is stored lower-cased by the sync, but the member list
        // carries whatever the account was typed as.
        ProjectMemberSourceSummary summary = ProjectMemberSources.For(
            [Source("a@erk-s.mn", "Блок А", 5)],
            "  A@Erk-S.MN  ");

        Assert.Equal(1, summary.Count);
    }

    [Fact]
    public void AMemberWhoRegisteredNothingReadsAsNone()
    {
        ProjectMemberSourceSummary summary = ProjectMemberSources.For(
            [Source("b@erk-s.mn", "Блок Б", 30)],
            "a@erk-s.mn");

        Assert.False(summary.Any);
        Assert.Equal(0, summary.SheetCount);
    }

    [Fact]
    public void ASourceWithNobodyRecordedIsAttributedToNobody()
    {
        // Local-only sources carry an organisation but no person. Guessing an
        // owner would put someone's name on work the data does not claim.
        ProjectMemberSourceSummary summary = ProjectMemberSources.For(
            [Source("", "Блок А", 5)],
            "a@erk-s.mn");

        Assert.False(summary.Any);
    }

    [Fact]
    public void AMemberWithNoEmailMatchesNothing()
    {
        // Otherwise an empty email would match every source that also has none.
        ProjectMemberSourceSummary summary = ProjectMemberSources.For(
            [Source("", "Блок А", 5)],
            "");

        Assert.False(summary.Any);
    }

    [Fact]
    public void ASourceWithoutADocumentNameStillHasSomethingToShow()
    {
        ProjectMemberSourceSummary summary = ProjectMemberSources.For(
            [Source("a@erk-s.mn")],
            "a@erk-s.mn");

        string name = Assert.Single(summary.Names);
        Assert.False(string.IsNullOrWhiteSpace(name));
    }

    [Fact]
    public void NegativeSheetCountsDoNotSubtract()
    {
        // A malformed manifest should not make a colleague's contribution look
        // smaller than it is.
        ProjectMemberSourceSummary summary = ProjectMemberSources.For(
            [Source("a@erk-s.mn", "Блок А", -4), Source("a@erk-s.mn", "Блок Б", 10)],
            "a@erk-s.mn");

        Assert.Equal(10, summary.SheetCount);
    }

    [Fact]
    public void NoSourcesAtAllIsHandled()
    {
        Assert.False(ProjectMemberSources.For(null, "a@erk-s.mn").Any);
        Assert.False(ProjectMemberSources.For([], "a@erk-s.mn").Any);
    }
}
