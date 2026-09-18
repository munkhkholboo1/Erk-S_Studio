using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// An address entered by hand is never mistaken for a published one.
///
/// 🔴 THE PUBLISH ROUTE ISSUES THE REAL CODE. Until it exists the owner is pasting in
/// whatever address the form was stood up at, and that address may not survive publishing.
/// The artefact this produces is a QR, and a QR gets PRINTED - so the distinction has to
/// live in the data, not in somebody's memory of which button they pressed.
/// </summary>
public sealed class AHANDEnteredAddressIsNeverMistakenForAPUBLISHEDOneTests
{
    [Fact]
    public void THECODEIsTakenFromTheAddressSoTheyCannotDisagree()
    {
        Assert.Equal("ab12cd", CitizenSurveyPublicLink.CodeFromUrl("https://erk-s.mn/s/ab12cd"));
        Assert.Equal("ab12cd", CitizenSurveyPublicLink.CodeFromUrl("https://erk-s.mn/s/ab12cd/"));
        Assert.Equal("ab12cd", CitizenSurveyPublicLink.CodeFromUrl("  https://a.b/x/s/ab12cd  "));
    }

    [Fact]
    public void ANADDRESSWithNoCodeInItYieldsNOTHING()
    {
        // ABSENCE MUST NOT BE A VALUE: returning the last segment of any address would
        // invent a code from a home page and draw a QR of it.
        foreach (string address in new[]
                 {
                     "https://erk-s.mn", "https://erk-s.mn/", "https://erk-s.mn/other/ab12cd",
                     "/s/ab12cd", "erk-s.mn/s/ab12cd", "", "   ",
                 })
        {
            Assert.Equal("", CitizenSurveyPublicLink.CodeFromUrl(address));
        }
    }

    [Fact]
    public void AHANDEnteredAddressIsMARKEDAndItsCodeIsTaken()
    {
        var survey = new ProjectCitizenSurvey();

        Assert.True(survey.AcceptManualLink("https://erk-s.mn/s/ab12cd"));

        Assert.True(survey.IsManualLink);
        Assert.Equal("ab12cd", survey.PublicCode);
        Assert.Equal("https://erk-s.mn/s/ab12cd", survey.PublicFormUrl);
    }

    [Fact]
    public void PUBLISHINGCLEARSTheMark()
    {
        // It has to clear itself. A mark cleared by hand would outlive the stand-in and
        // sit on a genuinely published survey saying it could not be trusted.
        var survey = new ProjectCitizenSurvey();
        survey.AcceptManualLink("https://erk-s.mn/s/ab12cd");

        Assert.True(survey.AcceptPublicLink("real99", "https://erk-s.mn/s/real99"));

        Assert.False(survey.IsManualLink);
        Assert.Equal("real99", survey.PublicCode);
    }

    [Fact]
    public void AREFUSEDAddressChangesNOTHING()
    {
        var survey = new ProjectCitizenSurvey();
        survey.AcceptPublicLink("real99", "https://erk-s.mn/s/real99");

        Assert.False(survey.AcceptManualLink("https://erk-s.mn/no-code-here"));
        Assert.False(survey.AcceptManualLink("not an address"));

        Assert.False(survey.IsManualLink);
        Assert.Equal("real99", survey.PublicCode);
        Assert.Equal("https://erk-s.mn/s/real99", survey.PublicFormUrl);
    }

    [Fact]
    public void AHANDEnteredAddressStillDrawsARealQR()
    {
        // The point of allowing it: the owner can put a working QR on screen the moment
        // somebody gives them an address, without waiting for the publish route.
        var survey = new ProjectCitizenSurvey();
        survey.AcceptManualLink("https://erk-s.mn/s/ab12cd");

        Assert.True(CitizenSurveyQrCode.For(survey.PublicCode, survey.PublicFormUrl).IsDrawn);
    }
}
