using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A survey that nothing can open is a survey that refuses every citizen.
///
/// 🔴 CAUGHT IN THE OWNER'S LIVE PROJECT, MINUTES BEFORE THEY PUBLISHED IT. The template
/// creates a survey with `IsOpen = false`; the definition carries that flag to the server;
/// the public route answers **410 Gone** to every submission while it is false. And Studio
/// had no control that could set it - the flag was written, published and enforced, with
/// nothing anywhere able to satisfy it.
///
/// What that would have looked like: the QR opens, the page renders, a citizen answers
/// sixteen questions, presses send - and loses all of it behind a message inviting them to
/// try again, which would fail identically forever. The owner would see zero responses and
/// conclude the public ignored the consultation.
///
/// ⚠ THE SERVER KEEPS ITS OWN COPY, so opening it here is only half. The route reads the
/// exported definition file; until that is exported again the server still believes the
/// survey is closed. Both halves are asserted below.
/// </summary>
public sealed class ASURVEYNothingCanOPENRefusesEveryCitizenTests
{
    [Fact]
    public void THEFLAGTravelsToTheServerSoSOMETHINGMustBeAbleToSetIt()
    {
        // The premise: this is not a cosmetic label. It is published, and the far side
        // refuses submissions on it.
        ProjectCitizenSurvey survey =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Зуунмод");
        survey.IssuePublicCode("https://erk-s.mn");

        Assert.False(survey.IsOpen, "the template no longer starts closed; re-read this test");
        Assert.False(CitizenSurveyPublication.For(survey, "project-under-test").IsOpen);

        survey.IsOpen = true;
        Assert.True(CitizenSurveyPublication.For(survey, "project-under-test").IsOpen);
    }

    [Fact]
    public void THESTUDIOPageCanOPENAndCLOSEIt()
    {
        // 🔴 THE MISSING HALF. Before this, IsOpen appeared in the page exactly once - as a
        // «нээлттэй» tag in the list - and no code path assigned it. A reader of that file
        // would have seen the flag mentioned and assumed it was handled.
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));

        Assert.Contains("survey.IsOpen = !survey.IsOpen;", page, StringComparison.Ordinal);
        Assert.Contains("OpenStateRow(survey)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ACLOSEDSurveySaysSoWhereItWillBeREAD()
    {
        // A closed survey must be loud on the page and named in the export, because both
        // are moments where somebody is about to hand the thing to the public.
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));

        Assert.Contains("ХААЛТТАЙ", page, StringComparison.Ordinal);
        Assert.Contains("ХААЛТТАЙ (хариулт авахгүй!)", page, StringComparison.Ordinal);
    }

    [Fact]
    public void OPENINGItSaysTheSERVERCopyMustBeRefreshed()
    {
        // ⚠ Opening it in Studio changes nothing on the server until the definition is
        // exported again. Left unsaid, that gap is discovered through a citizen's failed
        // submission - the most expensive place to learn it.
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));

        Assert.Contains("ДАХИН", page, StringComparison.Ordinal);
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
