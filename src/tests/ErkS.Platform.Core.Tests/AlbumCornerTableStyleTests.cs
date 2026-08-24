namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Which corner title block a project's sheets carry.
///
/// The setting lives in the project file rather than the album document
/// because AutoCAD and Revit draw sheets of their own and read that file
/// already. A choice they could not see would produce one album with two
/// different title blocks bound into it.
///
/// The value that matters most is the blank one: a project saved before this
/// existed must look exactly the same the next time its album is built.
/// </summary>
public sealed class AlbumCornerTableStyleTests
{
    [Fact]
    public void AProjectThatNeverChoseKeepsWhateverItAlreadyDrew()
    {
        // Not "the default style" - "the style this album already had". The
        // template decides, exactly as it did before the setting existed.
        var project = new ProjectWorkspace();

        Assert.Equal(AlbumCornerTableStyles.TemplateDecides, project.AlbumStyle.CornerTable);
    }

    [Theory]
    [InlineData("concept-190x28", AlbumCornerTableStyles.Concept)]
    [InlineData("CONCEPT-190X28", AlbumCornerTableStyles.Concept)]
    [InlineData("  working-drawing-180x36  ", AlbumCornerTableStyles.WorkingDrawing)]
    public void AStoredValueIsReadBackWhateverItsCasingOrSpacing(string stored, string expected)
    {
        // Three products write this file. Case and stray spaces are not worth
        // a rejected project.
        Assert.Equal(expected, AlbumCornerTableStyles.Normalize(stored));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void NothingChosenMeansTheTemplateDecides(string? stored)
    {
        Assert.Equal(AlbumCornerTableStyles.TemplateDecides, AlbumCornerTableStyles.Normalize(stored));
    }

    [Fact]
    public void AStyleThisVersionDoesNotKnowIsNotGuessedAt()
    {
        // A newer Studio may add a third block. An older one meeting it must
        // fall back to what the template draws rather than picking whichever
        // of the two it likes - silently redrawing someone's sheets as
        // something else is worse than not honouring their choice.
        Assert.Equal(
            AlbumCornerTableStyles.TemplateDecides,
            AlbumCornerTableStyles.Normalize("gost-185x55"));
        Assert.False(AlbumCornerTableStyles.IsKnown("gost-185x55"));
    }

    [Fact]
    public void TheStoredValueNamesItsOwnMeasurements()
    {
        // AutoCAD and Revit read this string. "concept" alone would send them
        // looking for a definition Studio keeps to itself; the dimensions make
        // the contract answer its own question.
        Assert.Contains("190x28", AlbumCornerTableStyles.Concept, StringComparison.Ordinal);
        Assert.Contains("180x36", AlbumCornerTableStyles.WorkingDrawing, StringComparison.Ordinal);
    }

    [Fact]
    public void TheChoiceSurvivesBeingSavedAndOpenedAgain()
    {
        var project = new ProjectWorkspace();
        project.AlbumStyle.CornerTable = AlbumCornerTableStyles.WorkingDrawing;

        string json = System.Text.Json.JsonSerializer.Serialize(project);
        ProjectWorkspace? reopened =
            System.Text.Json.JsonSerializer.Deserialize<ProjectWorkspace>(json);

        Assert.Equal(
            AlbumCornerTableStyles.WorkingDrawing,
            reopened?.AlbumStyle.CornerTable);
    }

    [Fact]
    public void AProjectFileWrittenBeforeThisSettingStillOpens()
    {
        // The node is simply absent in every project saved until today.
        const string json = """
            { "projectId": "p1", "identity": { "name": "Эмээлт" } }
            """;

        ProjectWorkspace? project =
            System.Text.Json.JsonSerializer.Deserialize<ProjectWorkspace>(
                json,
                new System.Text.Json.JsonSerializerOptions(
                    System.Text.Json.JsonSerializerDefaults.Web));

        Assert.NotNull(project);
        Assert.Equal(
            AlbumCornerTableStyles.TemplateDecides,
            project!.AlbumStyle.CornerTable);
    }
}
