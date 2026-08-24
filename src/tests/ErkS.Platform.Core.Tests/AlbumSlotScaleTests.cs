namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The scale a slot is supposed to be drawn at.
///
/// It belongs to the slot rather than to whatever page arrives, because it is
/// a requirement of the set: the standard says this sheet is М1:1500. Held on
/// the page alone, a drawing returned at the wrong scale is the one nobody can
/// catch, since there is nothing to compare it against.
///
/// AutoCAD reads this field out of the album file, so an absent one would have
/// left the other side reading an empty string forever.
/// </summary>
public sealed class AlbumSlotScaleTests
{
    [Fact]
    public void ASlotWithNoPrescribedScaleIsOrdinary()
    {
        // Most slots in the two existing urban planning templates prescribe
        // nothing, and the standard they follow does not fix scales.
        var slot = new AlbumCompositionItem();

        Assert.Equal("", slot.Scale);
    }

    [Fact]
    public void TheScaleIsKeptAsTheStandardWritesIt()
    {
        // Not parsed into a ratio: what goes on the sheet is this text, and
        // reconstructing it from a number invites a different spelling.
        var slot = new AlbumCompositionItem { Scale = "М1:1500" };

        Assert.Equal("М1:1500", slot.Scale);
    }

    [Fact]
    public void TheScaleSurvivesTheAlbumFile()
    {
        // The album file is where AutoCAD reads it from.
        var definition = new AlbumDefinition
        {
            Composition = [new AlbumCompositionItem { Id = "topographic-base", Scale = "М1:1500" }],
        };

        string json = System.Text.Json.JsonSerializer.Serialize(definition);
        AlbumDefinition? reopened =
            System.Text.Json.JsonSerializer.Deserialize<AlbumDefinition>(json);

        Assert.Equal("М1:1500", reopened?.Composition[0].Scale);
    }

    [Fact]
    public void AnAlbumFileWrittenBeforeTheFieldStillOpens()
    {
        const string json = """
            { "composition": [ { "id": "topographic-base", "title": "Байр зүйн дэвсгэр зураг" } ] }
            """;

        AlbumDefinition? definition = System.Text.Json.JsonSerializer.Deserialize<AlbumDefinition>(
            json,
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));

        Assert.Equal("", definition?.Composition[0].Scale);
    }

    [Fact]
    public void TheTwoExistingTemplatesPrescribeNoScale()
    {
        // Adding the field must not put a scale on drawings whose standard
        // never asked for one - those albums have to come out unchanged.
        AlbumDefinition masterPlan = ProjectTypes.UrbanPlanning.UrbanPlanningAlbumTemplate
            .CreateDefinition("master-plan");

        Assert.All(masterPlan.Composition, slot => Assert.Equal("", slot.Scale));
    }
}
