using Xunit;

namespace ErkS.Studio.Tests;

public sealed class StudioAlbumPageIdentityTests
{
    [Fact]
    public void ImmutableSourcePageIdentityDoesNotDependOnSemanticClassification()
    {
        string initial = StudioAlbumPageIdentity.Create(
            "Architect@ErkS.Local",
            "school-source",
            "native-sheet-42",
            3);
        string afterReclassification = StudioAlbumPageIdentity.Create(
            "architect@erks.local",
            "school-source",
            "native-sheet-42",
            3);

        Assert.Equal(initial, afterReclassification);
        Assert.StartsWith("album-page:", initial);
    }
}
