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
    public void THESTUDIOPageShipsTheFORMBesideTheDefinition()
    {
        // \U0001F534 THE SERVER MUST NOT RE-IMPLEMENT THE COLLECTOR SCRIPT. SRV asked for the
        // rendered form alongside the definition, and the reason is the sharpest failure
        // this feature has: one document key spelled differently in a second copy of that
        // script and Studio reads EVERY answer as empty - silently, because a survey with
        // no responses looks completely normal. One tested copy, served byte for byte.
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));

        Assert.Contains("CitizenSurveyFormHtml.Build(survey)", page, StringComparison.Ordinal);
        Assert.Contains("Path.ChangeExtension(dialog.FileName", page, StringComparison.Ordinal);
    }

    [Fact]
    public void THESURVEYPageIsREFRESHEDWhenItIsOpened()
    {
        // \U0001F534 CODE EXISTS, NOBODY CALLS IT - caught by the owner, not by me, looking at
        // an empty pane and asking whether their form was supposed to be there. The list,
        // the detail panel and even the «pick a survey» hint were all built and all
        // reachable; the only caller was the «new survey» button, so the page showed
        // NOTHING until one existed. No rule was wrong - a line was never connected.
        string shell = CodeOnly(ReadAppSource("ShellView.cs"));

        Assert.Contains("RefreshSurveyWorkspace()", shell, StringComparison.Ordinal);

        // \u26a0 BOTH ROUTES ONTO THE PAGE: navigating to it, and already standing on it
        // when a project opens. Either one alone leaves half the ways in still blank,
        // and «it works if you click away and back» is how that hides.
        Assert.Equal(2, shell.Split("RefreshSurveyWorkspace()").Length - 1);
    }

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

    private static string CodeOnly(string source)
    {
        Assert.DoesNotContain("/" + "*", source, StringComparison.Ordinal);

        var kept = new List<string>();
        foreach (string line in source.Split((char)10))
        {
            string bare = line.TrimEnd((char)13);
            if (bare.TrimStart().StartsWith("//", StringComparison.Ordinal))
                continue;
            int at = bare.IndexOf("//", StringComparison.Ordinal);
            kept.Add(at >= 0 ? bare[..at] : bare);
        }

        return string.Join(((char)10).ToString(), kept);
    }

    private static string ReadAppSource(string fileName) =>
        File.ReadAllText(
            Path.Combine(FindSourceRoot().FullName, "ErkS.Studio.App", fileName),
            System.Text.Encoding.UTF8);

    private static DirectoryInfo FindSourceRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src");
            if (Directory.Exists(candidate))
                return new DirectoryInfo(candidate);
            directory = directory.Parent;
        }

        Assert.Fail("the source tree was not found; this test reads it");
        return new DirectoryInfo(".");
    }
}
