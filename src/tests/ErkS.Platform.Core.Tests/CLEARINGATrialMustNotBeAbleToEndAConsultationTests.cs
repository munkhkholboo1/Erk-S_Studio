using ErkS.Platform.Core;
using Xunit;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Clearing a trial's answers must not be able to end a real consultation.
///
/// 🔴 THE OWNER ASKED FOR THIS BUTTON AND THE NEED IS REAL: a trial run's answers must
/// not be counted among the public's. But the same button sits there when the answers are
/// real, and what a citizen submitted is the one thing in this feature that cannot be
/// recomputed. So it MOVES the file rather than deleting it - the worst outcome of a
/// mis-click becomes a rename instead of a consultation with nothing to show.
/// </summary>
public sealed class CLEARINGATrialMustNotBeAbleToEndAConsultationTests
{
    private static readonly DateTimeOffset Stamp =
        new(2026, 9, 19, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ITMOVESTheAnswersAsideAndReportsHowMany()
    {
        using var project = new TemporaryProject();
        ProjectCitizenSurvey survey = project.Survey();

        var held = new CitizenSurveyResponseDocument { SurveyId = survey.Id };
        CitizenSurveyResponseStore.Merge(held, [Response("r1"), Response("r2")], "cursor-9");
        CitizenSurveyResponseStore.Save(project.ProjectPath, survey, held);

        CitizenSurveyResponseStore.CitizenSurveyClearOutcome cleared =
            CitizenSurveyResponseStore.Clear(project.ProjectPath, survey, Stamp);

        Assert.Equal(2, cleared.Removed);
        Assert.True(File.Exists(cleared.ArchivePath), "the answers were deleted, not moved");
        Assert.Empty(CitizenSurveyResponseStore.Load(project.ProjectPath, survey));

        // 🔴 AND THE MOVED FILE IS STILL READABLE. A rename that produced something nobody
        // could open would be a delete with extra steps.
        CitizenSurveyResponseDocument recovered =
            System.Text.Json.JsonSerializer.Deserialize<CitizenSurveyResponseDocument>(
                File.ReadAllText(cleared.ArchivePath))!;
        Assert.Equal(2, recovered.Responses.Count);
    }

    [Fact]
    public void THECURSORSurvivesSoClearedAnswersDoNOTComeBack()
    {
        // ⚠ THE SUBTLE HALF. The cursor is the watermark of what has already been
        // collected. Resetting it would refill the survey on the next fetch with exactly
        // the responses somebody just cleared - a clear that undoes itself, silently.
        using var project = new TemporaryProject();
        ProjectCitizenSurvey survey = project.Survey();

        var held = new CitizenSurveyResponseDocument { SurveyId = survey.Id };
        CitizenSurveyResponseStore.Merge(held, [Response("r1")], "cursor-9");
        CitizenSurveyResponseStore.Save(project.ProjectPath, survey, held);

        CitizenSurveyResponseStore.Clear(project.ProjectPath, survey, Stamp);

        Assert.Equal(
            "cursor-9",
            CitizenSurveyResponseStore.LoadDocument(project.ProjectPath, survey).Cursor);
    }

    [Fact]
    public void CLEARINGNothingMovesNothing()
    {
        // ABSENCE MUST NOT BE A VALUE: an empty clear must not leave an archive file
        // suggesting answers once existed, nor report a count nobody can account for.
        using var project = new TemporaryProject();
        ProjectCitizenSurvey survey = project.Survey();

        CitizenSurveyResponseStore.CitizenSurveyClearOutcome nothing =
            CitizenSurveyResponseStore.Clear(project.ProjectPath, survey, Stamp);

        Assert.Equal(0, nothing.Removed);
        Assert.Equal("", nothing.ArchivePath);

        var empty = new CitizenSurveyResponseDocument { SurveyId = survey.Id };
        CitizenSurveyResponseStore.Save(project.ProjectPath, survey, empty);

        CitizenSurveyResponseStore.CitizenSurveyClearOutcome again =
            CitizenSurveyResponseStore.Clear(project.ProjectPath, survey, Stamp);

        Assert.Equal(0, again.Removed);
        Assert.Equal("", again.ArchivePath);
    }

    [Fact]
    public void TWOCLEARSDoNotOverwriteTheFirstArchive()
    {
        // Two trials on one day would otherwise collide, and the second would destroy the
        // first - the exact loss the move was there to prevent.
        using var project = new TemporaryProject();
        ProjectCitizenSurvey survey = project.Survey();

        var first = new CitizenSurveyResponseDocument { SurveyId = survey.Id };
        CitizenSurveyResponseStore.Merge(first, [Response("r1")], null);
        CitizenSurveyResponseStore.Save(project.ProjectPath, survey, first);
        string one = CitizenSurveyResponseStore.Clear(project.ProjectPath, survey, Stamp).ArchivePath;

        var second = new CitizenSurveyResponseDocument { SurveyId = survey.Id };
        CitizenSurveyResponseStore.Merge(second, [Response("r2")], null);
        CitizenSurveyResponseStore.Save(project.ProjectPath, survey, second);
        string two = CitizenSurveyResponseStore
            .Clear(project.ProjectPath, survey, Stamp.AddMinutes(1)).ArchivePath;

        Assert.NotEqual(one, two);
        Assert.True(File.Exists(one), "the first archive was overwritten by the second clear");
        Assert.True(File.Exists(two));
    }

    private static CitizenSurveyResponse Response(string id) =>
        new()
        {
            Id = id,
            Answers = [new CitizenSurveyAnswer { QuestionId = "q", OptionIds = ["o"] }],
        };

    private sealed class TemporaryProject : IDisposable
    {
        private readonly string folder;

        public TemporaryProject()
        {
            folder = Path.Combine(
                Path.GetTempPath(), "erks-survey-clear-" + Guid.NewGuid().ToString("N"));
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
}
