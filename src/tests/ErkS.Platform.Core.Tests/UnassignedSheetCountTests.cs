namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Sheets that belong to no building.
///
/// They are not dropped - they stay with the first album - so nothing ever
/// looked broken. But on a set split by building, a sheet in the first album
/// is a section filed under the wrong building, and the only person who could
/// notice was the one reading the finished PDF.
/// </summary>
public sealed class UnassignedSheetCountTests
{
    [Fact]
    public void ASheetNoBuildingClaimedIsCounted()
    {
        int count = ProjectBuildingComposition.CountUnassignedSheets(
            Groups(),
            new Dictionary<string, string> { ["src-a|s1"] = "b1" },
            ["src-a|s1", "src-a|s2"]);

        Assert.Equal(1, count);
    }

    [Fact]
    public void AProjectWithNoBuildingsHasNothingToBeWrongAbout()
    {
        // Every sheet is unassigned here and that is simply what a project
        // without buildings looks like. Counting it would warn most users
        // about nothing and teach them to ignore the message.
        int count = ProjectBuildingComposition.CountUnassignedSheets(
            [],
            new Dictionary<string, string>(),
            ["src-a|s1", "src-a|s2"]);

        Assert.Equal(0, count);
    }

    [Fact]
    public void AnAssignmentPointingAtNothingCountsAsUnassigned()
    {
        // A blank group id is not an assignment, however it got there.
        int count = ProjectBuildingComposition.CountUnassignedSheets(
            Groups(),
            new Dictionary<string, string> { ["src-a|s1"] = "  " },
            ["src-a|s1"]);

        Assert.Equal(1, count);
    }

    [Fact]
    public void TheSameSheetIsNotCountedTwice()
    {
        // Pages repeat a sheet key when one drawing appears more than once;
        // the user has one sheet to file, so they hear about one.
        int count = ProjectBuildingComposition.CountUnassignedSheets(
            Groups(),
            new Dictionary<string, string>(),
            ["src-a|s1", "SRC-A|S1", "src-a|s1"]);

        Assert.Equal(1, count);
    }

    [Fact]
    public void GeneratedPagesWithoutASheetAreNotCounted()
    {
        // Covers and drawing lists have no sheet key and belong to no
        // building by design.
        int count = ProjectBuildingComposition.CountUnassignedSheets(
            Groups(),
            new Dictionary<string, string>(),
            ["", "   ", null!]);

        Assert.Equal(0, count);
    }

    [Fact]
    public void EverythingFiledMeansSilence()
    {
        int count = ProjectBuildingComposition.CountUnassignedSheets(
            Groups(),
            new Dictionary<string, string>
            {
                ["src-a|s1"] = "b1",
                ["src-a|s2"] = "b2",
            },
            ["src-a|s1", "src-a|s2"]);

        Assert.Equal(0, count);
    }

    private static List<ProjectBuildingGroup> Groups() =>
    [
        new() { Id = "b1", Name = "А блок" },
        new() { Id = "b2", Name = "Б блок" },
    ];
}
