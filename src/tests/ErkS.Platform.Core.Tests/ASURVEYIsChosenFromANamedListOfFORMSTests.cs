using System.Text;
using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The form Studio offers must be a NAMED one, chosen from a list of forms.
///
/// THE OWNER'S OWN FRAMING, and the reason this seam exists before its second user:
/// «миний чамд огsон санал асуулга Зуун-Модын хэсэгчилсэн ерөнхий төлөвлөгөөнд зориулсан
/// санал асуулга. одоогоор туршилтын шатанд байгаагаас гадна бодит хэрэглээ нь энэ
/// төсөлд байгаа тул тухайн санал асуулгад тохируулж хийгээрэй. цаашид аль ч төсөлд
/// хэрэглэж болох санал асуулгыг боловсруулж нэмнэ.»
///
/// So today there is exactly ONE form and it is theirs. The danger is not in that; it is
/// in the SECOND one. A button that hard-codes «make the partial master plan survey»
/// either grows a boolean when the general form arrives, or - worse - the general form
/// quietly replaces the specific one and a project in trial use loses the questions it
/// was collecting answers against. So the form is picked from a catalogue by id, and
/// these tests fail the day a form exists that the catalogue does not name.
/// </summary>
public sealed class ASURVEYIsChosenFromANamedListOfFORMSTests
{
    [Fact]
    public void THECATALOGUEOffersTheOWNERSFormToday()
    {
        var offered = CitizenSurveyTemplates.All;

        Assert.NotEmpty(offered);
        Assert.Contains(offered, form => form.Id == CitizenSurveyTemplates.PartialMasterPlanId);

        // Its ids are what a saved survey carries, so two forms may never share one.
        Assert.Equal(
            offered.Select(form => form.Id).Distinct(StringComparer.Ordinal).Count(),
            offered.Count);
    }

    [Fact]
    public void EVERYOfferedFormCanActuallyBeBuilt()
    {
        // The list is a promise. A name on it that Create cannot honour is worse than
        // an absent one: the button appears, the citizen is invited, nothing is asked.
        foreach (CitizenSurveyTemplateInfo form in CitizenSurveyTemplates.All)
        {
            ProjectCitizenSurvey built = CitizenSurveyTemplates.Create(form.Id, "Зуунмод");

            Assert.NotNull(built);
            Assert.True(built!.HasQuestions, form.Id + " built no questions");
            Assert.False(string.IsNullOrWhiteSpace(form.Name), form.Id + " has no name");
        }
    }

    [Fact]
    public void ABUILTSurveyRemembersWhichFormItCameFrom()
    {
        // Without this, a project holding two rounds cannot say which form each round
        // used - and the day the general form lands, every older survey looks like it.
        ProjectCitizenSurvey built =
            CitizenSurveyTemplates.Create(CitizenSurveyTemplates.PartialMasterPlanId, "Зуунмод")!;

        Assert.Equal(CitizenSurveyTemplates.PartialMasterPlanId, built.TemplateId);
    }

    [Fact]
    public void ANUNKNOWNFormBuildsNOTHINGRatherThanSomethingElse()
    {
        // ABSENCE MUST NOT BE A VALUE. Falling back to «the only form I have» would mean
        // that a typo, or a project saved by a newer Studio naming a form this build does
        // not carry, silently collects answers against the WRONG questionnaire.
        Assert.Null(CitizenSurveyTemplates.Create("no-such-form", "Зуунмод"));
        Assert.Null(CitizenSurveyTemplates.Create("", "Зуунмод"));
        Assert.Null(CitizenSurveyTemplates.Create(null, "Зуунмод"));
    }

    [Fact]
    public void THEOWNERSFormIsTHEIRSNotARewordingOfIt()
    {
        // The catalogue must not become a place where the questions get "improved".
        // Built through the catalogue or built directly, it is the same document.
        ProjectCitizenSurvey viaCatalogue =
            CitizenSurveyTemplates.Create(CitizenSurveyTemplates.PartialMasterPlanId, "Зуунмод")!;
        ProjectCitizenSurvey direct =
            CitizenSurveyTemplate.CreatePartialMasterPlanSurvey("Зуунмод");

        Assert.Equal(
            direct.OrderedQuestions().Select(question => question.Text).ToList(),
            viaCatalogue.OrderedQuestions().Select(question => question.Text).ToList());
        Assert.Equal(direct.Title, viaCatalogue.Title);
    }

    [Fact]
    public void THESTUDIOPageAsksTheCATALOGUERatherThanNamingAFormItself()
    {
        // TEXT-ANCHORED, SO IT IS ANCHORED TO CODE, NOT PROSE: the comment above the
        // button is allowed to name the owner's form; the code is not.
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));

        Assert.Contains("CitizenSurveyTemplates", page, StringComparison.Ordinal);
        Assert.DoesNotContain("CreatePartialMasterPlanSurvey", page, StringComparison.Ordinal);
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
