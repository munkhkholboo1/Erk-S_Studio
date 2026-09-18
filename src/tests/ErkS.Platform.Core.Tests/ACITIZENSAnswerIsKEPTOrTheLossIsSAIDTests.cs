using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// What a citizen submitted is kept, or the program says it could not read it.
///
/// 🔴 THIS IS THE ONE PLACE IN THE SURVEY FEATURE WHERE DATA CAN BE LOST FOR GOOD. The
/// tally can be recomputed, a chart redrawn, a finding re-read - but an answer dropped on
/// collection is gone, and the citizen is not coming back to fill the form again. So the
/// store never overwrites what it holds, never silently swallows a file it cannot parse,
/// and never writes outside the project it was given.
///
/// ⚠ AND ZERO HAS TWO MEANINGS HERE, exactly as it did on the sheets page: «0 хариулт»
/// can mean nobody has answered yet or that the file holding the answers is unreadable.
/// The first is a fact worth showing; the second is a fault that must be said out loud.
/// </summary>
public sealed class ACITIZENSAnswerIsKEPTOrTheLossIsSAIDTests
{
    [Fact]
    public void SAVEDAnswersComeBackWithTheirOptionsIntact()
    {
        using var project = new TemporaryProject();
        ProjectCitizenSurvey survey = project.Survey();

        var document = new CitizenSurveyResponseDocument { SurveyId = survey.Id };
        CitizenSurveyResponseStore.Merge(document, [Response("r1", "q1", "o2")], "cursor-7");
        CitizenSurveyResponseStore.Save(project.ProjectPath, survey, document);

        CitizenSurveyResponseDocument back =
            CitizenSurveyResponseStore.LoadDocument(project.ProjectPath, survey);

        Assert.Single(back.Responses);
        Assert.Equal("r1", back.Responses[0].Id);
        Assert.Equal(["o2"], back.Responses[0].Answers[0].OptionIds);
        Assert.Equal("cursor-7", back.Cursor);
    }

    [Fact]
    public void MERGEAddsWhatIsNewAndCountsONLYThat()
    {
        var document = new CitizenSurveyResponseDocument { SurveyId = "s" };

        int first = CitizenSurveyResponseStore.Merge(
            document, [Response("r1", "q1", "o1"), Response("r2", "q1", "o2")], "c1");
        int second = CitizenSurveyResponseStore.Merge(
            document, [Response("r2", "q1", "o2"), Response("r3", "q1", "o1")], "c2");

        Assert.Equal(2, first);

        // 🔴 ONE, NOT TWO. The caller prints this number - «2 шинэ хариулт ирлээ» over one
        // new form would be a claim the program never checked, and the same page also
        // shows the total, so the two would disagree in front of the reader.
        Assert.Equal(1, second);
        Assert.Equal(3, document.Responses.Count);
    }

    [Fact]
    public void MERGENeverDropsAnAnswerAlreadyHeld()
    {
        var document = new CitizenSurveyResponseDocument { SurveyId = "s" };
        CitizenSurveyResponseStore.Merge(document, [Response("r1", "q1", "o1")], "c1");

        // A collection that returns nothing - the server had nothing new, or answered
        // oddly - must leave what is held exactly as it was.
        CitizenSurveyResponseStore.Merge(document, [], "");
        CitizenSurveyResponseStore.Merge(document, null, null);

        Assert.Single(document.Responses);
        Assert.Equal("r1", document.Responses[0].Id);

        // ⚠ AND THE CURSOR SURVIVES AN EMPTY ARRIVAL. Blanking it would make the next
        // collection start from the beginning - harmless - or, if the server reads a blank
        // cursor as «from now», skip everything submitted in between. Silently.
        Assert.Equal("c1", document.Cursor);
    }

    [Fact]
    public void ANANSWERWithNoIdIsRefusedRatherThanStoredTwice()
    {
        var document = new CitizenSurveyResponseDocument { SurveyId = "s" };

        int added = CitizenSurveyResponseStore.Merge(
            document, [Response("", "q1", "o1"), Response("   ", "q1", "o1")], "c1");

        // Without an id there is nothing to recognise a repeat by, so each collection
        // would add the same form again and the participant count would climb on its own.
        Assert.Equal(0, added);
        Assert.Empty(document.Responses);
    }

    [Fact]
    public void ANUNREADFileIsEMPTYOnlyWhenItIsNOTThere()
    {
        using var project = new TemporaryProject();
        ProjectCitizenSurvey survey = project.Survey();

        // Nothing collected yet: a true zero, and not an error.
        Assert.Empty(CitizenSurveyResponseStore.Load(project.ProjectPath, survey));

        // A file that exists but cannot be read is NOT a zero.
        // GetFullPath, not Combine: the relative path holds a forward slash and the store
        // normalises it, so a fixture that did not would compare two spellings of one file.
        string path = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(project.ProjectPath)!, survey.ResponsesRelativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "{ this is not json");

        InvalidDataException thrown = Assert.Throws<InvalidDataException>(
            () => CitizenSurveyResponseStore.Load(project.ProjectPath, survey));
        Assert.Contains(path, thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void THESTOREWritesInsideTheProjectOrNotAtAll()
    {
        using var project = new TemporaryProject();
        ProjectCitizenSurvey survey = project.Survey();
        survey.ResponsesRelativePath = Path.Combine("..", "..", "escaped.erksresponses");

        var document = new CitizenSurveyResponseDocument { SurveyId = survey.Id };
        CitizenSurveyResponseStore.Merge(document, [Response("r1", "q1", "o1")], "c1");

        Assert.Throws<InvalidDataException>(
            () => CitizenSurveyResponseStore.Save(project.ProjectPath, survey, document));
    }

    [Fact]
    public void THESTUDIOPageCatchesTheThrowItsOwnContractPromisesToCatch()
    {
        // WARNING IN PROSE IS NOT A GUARD. The store throws deliberately and its comment
        // says the throw is «left to the caller, which has a status line to say so on».
        // That sentence was true of the store and false of the caller: nothing caught it,
        // so an unreadable file took the whole window down instead of saying one line.
        string page = CodeOnly(ReadAppSource("ShellView.Surveys.cs"));

        Assert.Contains("catch (InvalidDataException", page, StringComparison.Ordinal);
    }

    private static CitizenSurveyResponse Response(string id, string questionId, string optionId) =>
        new()
        {
            Id = id,
            Answers = [new CitizenSurveyAnswer { QuestionId = questionId, OptionIds = [optionId] }],
        };

    private sealed class TemporaryProject : IDisposable
    {
        private readonly string folder;

        public TemporaryProject()
        {
            folder = Path.Combine(
                Path.GetTempPath(), "erks-survey-store-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            ProjectPath = Path.Combine(folder, "test.erksproj");
            File.WriteAllText(ProjectPath, "{}");
        }

        public string ProjectPath { get; }

        public ProjectCitizenSurvey Survey() => new()
        {
            Id = "survey-1",
            ResponsesRelativePath = "surveys/survey-1.erksresponses",
        };

        public void Dispose()
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }
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
