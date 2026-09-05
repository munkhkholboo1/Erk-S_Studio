using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The recogniser used to answer "Citizen" to everything it did not know: an
/// unfamiliar value, a typo, a field never filled and a client type from a
/// newer server all became "a private citizen", and the cover page said so.
///
/// PFR met the same shape from the other side - an English enum against a
/// Mongolian comparison - and every organisation client would have printed as a
/// citizen. Both halves of that defect are the same sentence: an unanswered
/// question given a confident answer.
///
/// Measured before the change: all 24 projects on this machine hold one of the
/// three codes, so nothing stored reads differently.
/// </summary>
public sealed class ProjectClientTypeRecognitionTests
{
    [Theory]
    [InlineData("Citizen")]
    [InlineData("Organization")]
    [InlineData("GovernmentAuthority")]
    [InlineData("organization")]
    public void AKnownCodeComesBackAsItself(string value)
    {
        Assert.Equal(
            value.ToLowerInvariant(),
            ProjectClientTypes.Recognize(value).ToLowerInvariant());
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Байгууллага")]
    [InlineData("Individual")]
    [InlineData("Partnership")]
    public void ANYTHINGElseIsUnknown_AndUnknownIsNotCitizen(string? value)
    {
        // The whole point. "Байгууллага" is included deliberately: it is the
        // display word for an ORGANISATION, and the old fallback would have
        // called it a citizen - the exact inversion PFR measured.
        Assert.Equal("", ProjectClientTypes.Recognize(value));
    }

    [Fact]
    public void AnUnknownTypeIsNotNAMEDOnACover()
    {
        // Saying nothing beats naming the wrong party on a printed sheet.
        Assert.Equal("", ProjectClientTypes.DisplayName("Partnership"));
        Assert.Equal("Иргэн", ProjectClientTypes.DisplayName("Citizen"));
        Assert.Equal("Байгууллага", ProjectClientTypes.DisplayName("Organization"));
        Assert.Equal("Төрийн байгууллага", ProjectClientTypes.DisplayName("GovernmentAuthority"));
    }

    [Fact]
    public void EachCallerDecidesForItself_TheAnswersAreNotTheSame()
    {
        // Three questions about the same unknown value, three different right
        // answers - which is why the recogniser must not decide for them.
        Assert.Equal("", ProjectClientTypes.DisplayName("Partnership"));
        Assert.False(ProjectClientTypes.UsesLogo("Partnership"));
        Assert.False(ProjectClientTypes.ShowsDirectClientName("Partnership"));
        // A form field still needs a name, and the neutral one claims nothing.
        Assert.Equal("Захиалагчийн нэр", ProjectClientTypes.ClientNameFieldLabel("Partnership"));
    }

    [Fact]
    public void AGovernmentClientIsNeverTreatedAsACitizen()
    {
        // The error that would have reached a printed cover page.
        Assert.True(ProjectClientTypes.UsesLogo("GovernmentAuthority"));
        Assert.False(ProjectClientTypes.ShowsDirectClientName("GovernmentAuthority"));
    }
}
