using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A QR is drawn only for a link that was checked, and never for one that was not.
///
/// 🔴 A SQUARE THAT SCANS AND LEADS NOWHERE IS WORSE THAN NO SQUARE. It is printed,
/// posted and handed out looking exactly like finished work; the failure surfaces as a
/// member of the public saying the link is dead, weeks later. So the refusal is the
/// important half of this rule, not the drawing.
/// </summary>
public sealed class AQRIsDrawnONLYForACheckedLinkTests
{
    private const string GoodUrl = "https://erk-s.mn/s/ab12cd";

    [Fact]
    public void ACHECKEDLinkProducesARealPNG()
    {
        CitizenSurveyQrImage image = CitizenSurveyQrCode.For("ab12cd", GoodUrl);

        Assert.True(image.IsDrawn);
        Assert.Equal("", image.Refusal);

        // The PNG signature, so this is an image and not an empty buffer that happens
        // to have length - the one way a "drawn" QR could still be nothing.
        Assert.True(image.Png.Length > 100);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, image.Png.Take(4).ToArray());
    }

    [Fact]
    public void ALINKThatFailedTheCheckIsNOTDrawn()
    {
        foreach ((string code, string url) in new[]
                 {
                     ("ab12cd", "https://erk-s.mn/s/zz99zz"),
                     ("ab12cd", ""),
                     ("", GoodUrl),
                     ("ab12cd", "/s/ab12cd"),
                     ("ab12cd", "https://elsewhere.example/x?next=ab12cd"),
                 })
        {
            CitizenSurveyQrImage image = CitizenSurveyQrCode.For(code, url);

            Assert.False(image.IsDrawn, code + " / " + url + " was drawn");
            Assert.NotEqual("", image.Refusal);
            Assert.Empty(image.Png);
        }
    }

    [Fact]
    public void THEREFUSALIsTheLinkCheckSOwnSentenceNotASecondOpinion()
    {
        // Two vocabularies for one fault would put two different explanations of the same
        // thing on one screen. The QR has nothing of its own to say about a bad link.
        CitizenSurveyQrImage image = CitizenSurveyQrCode.For("ab12cd", "");
        CitizenSurveyLinkCheck link = CitizenSurveyPublicLink.Check("ab12cd", "");

        Assert.Equal(link.Refusal, image.Refusal);
    }

    [Fact]
    public void THESAMELinkDrawsTheSameSquareEveryTime()
    {
        // A QR that varied between runs would mean a reprint never matches the poster
        // already on the wall, and nobody could tell which of the two is current.
        byte[] first = CitizenSurveyQrCode.For("ab12cd", GoodUrl).Png;
        byte[] second = CitizenSurveyQrCode.For("ab12cd", GoodUrl).Png;

        Assert.Equal(first, second);
    }

    [Fact]
    public void ADIFFERENTSurveyDrawsADifferentSquare()
    {
        // The positive control for the test above: equality must mean "same link", not
        // "this rule returns one fixed image whatever it is given".
        byte[] one = CitizenSurveyQrCode.For("ab12cd", GoodUrl).Png;
        byte[] other = CitizenSurveyQrCode.For("zz99zz", "https://erk-s.mn/s/zz99zz").Png;

        Assert.NotEqual(one, other);
    }
}
