using System.Text;
using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The link that goes on the QR is checked at the moment it is issued.
///
/// 🔴 BECAUSE THIS ONE VALUE CANNOT BE CORRECTED AFTERWARDS. Every other thing this
/// program gets from the server can be fetched again; a QR code is printed, posted on a
/// notice board and handed to people in the street. A wrong one fails as «the link does
/// not work», reported by a citizen, weeks later, if at all - and by then the consultation
/// period may be over.
///
/// ⚠ AND THE SERVER CAN GENUINELY GET IT WRONG WITHOUT ANY FAULT ON ITS SIDE. SRV
/// measured it: when the public base address is not configured, their publisher builds the
/// URL from the REQUEST HEADERS, and the live start-up script does not configure it. On
/// that route the value is not a security hole - the publisher is authenticated and gets
/// back its own spoofed address - but it is printed, so Studio verifies the pair it was
/// handed instead of trusting it.
/// </summary>
public sealed class THEPRINTEDLinkIsCHECKEDBeforeItIsPrintedTests
{
    [Fact]
    public void ALINKThatEndsInItsOwnCODEIsUsable()
    {
        CitizenSurveyLinkCheck checkedLink =
            CitizenSurveyPublicLink.Check("ab12cd", "https://erk-s.mn/s/ab12cd");

        Assert.True(checkedLink.IsUsable);
        Assert.Equal("", checkedLink.Refusal);
    }

    [Fact]
    public void ALINKForSomeoneElsesCODEIsREFUSED()
    {
        // The pair disagreeing is the shape a copy-paste or a cached response takes, and
        // it is the one the QR carries: the code is right in the project file and the
        // paper sends people somewhere else.
        CitizenSurveyLinkCheck checkedLink =
            CitizenSurveyPublicLink.Check("ab12cd", "https://erk-s.mn/s/zz99zz");

        Assert.False(checkedLink.IsUsable);
        Assert.Contains("ab12cd", checkedLink.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ANEMPTYSideIsREFUSEDRatherThanTreatedAsAgreement()
    {
        // ABSENCE MUST NOT BE A VALUE: two blanks «match» under any string comparison, and
        // an unpublished survey would sail through the check and print a QR of nothing.
        Assert.False(CitizenSurveyPublicLink.Check("", "").IsUsable);
        Assert.False(CitizenSurveyPublicLink.Check("ab12cd", "").IsUsable);
        Assert.False(CitizenSurveyPublicLink.Check("", "https://erk-s.mn/s/ab12cd").IsUsable);
        Assert.False(CitizenSurveyPublicLink.Check(null, null).IsUsable);
    }

    [Fact]
    public void ANUNPUBLISHEDSurveySaysSoRatherThanBlamingTheAddress()
    {
        // ⚠ A SABOTAGE SWEEP TAUGHT ME WHAT THESE TWO GUARDS ACTUALLY BUY. Removing them
        // changes no VERDICT - the segment match refuses an empty code anyway - so the
        // test above stayed green without them. What they carry is the SENTENCE, and the
        // sentence is the whole of their value: this text is what the owner reads on the
        // page. «Маягтын хаяг веб хаяг биш байна» over a survey that simply has not
        // been published yet sends somebody hunting for a fault that does not exist.
        CitizenSurveyLinkCheck unpublished = CitizenSurveyPublicLink.Check("", "");

        Assert.False(unpublished.IsUsable);
        Assert.Contains("нийтлэгдээгүй", unpublished.Refusal, StringComparison.Ordinal);

        // And a published code whose address never arrived is a different fault again.
        CitizenSurveyLinkCheck noAddress = CitizenSurveyPublicLink.Check("ab12cd", "");

        Assert.False(noAddress.IsUsable);
        Assert.Contains("хаяг ирээгүй", noAddress.Refusal, StringComparison.Ordinal);
    }

    [Fact]
    public void ALINKThatIsNotAWEBAddressIsREFUSED()
    {
        // A relative path prints a QR that opens nothing, and a non-http scheme printed on
        // a public notice is worse than a dead link.
        Assert.False(CitizenSurveyPublicLink.Check("ab12cd", "/s/ab12cd").IsUsable);
        Assert.False(CitizenSurveyPublicLink.Check("ab12cd", "erk-s.mn/s/ab12cd").IsUsable);
        Assert.False(CitizenSurveyPublicLink.Check("ab12cd", "file:///s/ab12cd").IsUsable);
        Assert.False(
            CitizenSurveyPublicLink.Check("ab12cd", "javascript:alert(1)//s/ab12cd").IsUsable);
    }

    [Fact]
    public void ACODEThatMerelyAPPEARSInTheLinkIsNotAMATCH()
    {
        // ⚠ THE CHECK IS ON THE PATH'S LAST SEGMENT, NOT ON «contains». A substring test
        // passes for an address that sends the citizen somewhere else entirely and merely
        // mentions the code on the way - a query parameter, another project's folder.
        Assert.False(
            CitizenSurveyPublicLink.Check("ab12cd", "https://elsewhere.example/x?next=ab12cd")
                .IsUsable);
        Assert.False(
            CitizenSurveyPublicLink.Check("ab12cd", "https://erk-s.mn/s/ab12cd/extra").IsUsable);
        Assert.False(
            CitizenSurveyPublicLink.Check("ab12cd", "https://erk-s.mn/other/ab12cd").IsUsable);
    }

    [Fact]
    public void ATRAILINGSlashIsTheSameLink()
    {
        // Tolerated because it is the same page, and refusing it would send somebody
        // hunting for a fault that is not there.
        Assert.True(CitizenSurveyPublicLink.Check("ab12cd", "https://erk-s.mn/s/ab12cd/").IsUsable);
    }

    [Fact]
    public void BUILDINGFromAnOwnerSetBaseProducesALinkThatPASSESTheCheck()
    {
        // The two halves must agree, or the escape hatch for a server that cannot know
        // its own address would produce links the check then rejects.
        foreach (string baseUrl in new[]
                 {
                     "https://erk-s.mn", "https://erk-s.mn/", "  https://erk-s.mn  ",
                 })
        {
            string built = CitizenSurveyPublicLink.Build(baseUrl, "ab12cd");
            Assert.True(
                CitizenSurveyPublicLink.Check("ab12cd", built).IsUsable,
                baseUrl + " built " + built);
        }
    }

    [Fact]
    public void THESURVEYRefusesToRECORDALinkItWouldNotPrint()
    {
        var survey = new ProjectCitizenSurvey();

        Assert.False(survey.AcceptPublicLink("ab12cd", "https://erk-s.mn/s/zz99zz"));

        // 🔴 AND IT KEEPS NOTHING FROM THE REFUSED PAIR. Storing the code while
        // rejecting the url - or the other way round - leaves the project holding half a
        // publication, which reads on screen as «published».
        Assert.Equal("", survey.PublicCode);
        Assert.Equal("", survey.PublicFormUrl);

        Assert.True(survey.AcceptPublicLink("ab12cd", "https://erk-s.mn/s/ab12cd"));
        Assert.Equal("ab12cd", survey.PublicCode);
        Assert.Equal("https://erk-s.mn/s/ab12cd", survey.PublicFormUrl);
    }

    [Fact]
    public void THEPAGEChecksTheLinkItShowsRatherThanPrintingWhateverIsHeld()
    {
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));

        Assert.Contains("CitizenSurveyPublicLink.Check", page, StringComparison.Ordinal);
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
            Encoding.UTF8);

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
