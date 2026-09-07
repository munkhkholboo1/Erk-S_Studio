namespace ErkS.Platform.Core;

/// <summary>
/// The steps «Альбомыг шинэчлэх» walks through.
///
/// They are named here rather than left as strings at the call site because the
/// report's honesty rests on knowing WHICH step did not happen. A run that
/// stopped after sending and a run that finished are the same length in words
/// and completely different in meaning.
/// </summary>
public enum AlbumRefreshStep
{
    /// <summary>Read this device's own sources.</summary>
    ReadOwnSources,

    /// <summary>Give this device's contribution to the cloud.</summary>
    SendOwnContribution,

    /// <summary>Take in everything the cloud has, including other members' work.</summary>
    FetchCloudUpdates,

    /// <summary>Put the stored page composition and order back in step.</summary>
    RecomposePages,
}

/// <summary>
/// What became of one step.
///
/// 🔴 "NOT REACHED" AND "NOTHING TO DO" ARE DIFFERENT and must stay different.
/// Both leave the step having done no work, and only one of them is a reason to
/// worry. Folding them together is how a run that stopped at step 1 comes to
/// read like a run that had nothing to send.
/// </summary>
public enum AlbumRefreshStepOutcome
{
    /// <summary>An earlier step stopped the run before this one was attempted.</summary>
    NotReached,

    /// <summary>Attempted, and there was genuinely nothing for it to do.</summary>
    NothingToDo,

    /// <summary>Attempted and done.</summary>
    Done,

    /// <summary>Attempted and it did not work.</summary>
    Failed,
}

/// <summary>Which album the screen is actually showing when the run ends.</summary>
public enum AlbumOnScreen
{
    /// <summary>No album has been built or fetched yet.</summary>
    None,

    /// <summary>This device's own build - what it can make from its own sources.</summary>
    LocalPreview,

    /// <summary>The album the server assembled from every contributor.</summary>
    CloudCanonical,
}

public sealed record AlbumRefreshStepResult(
    AlbumRefreshStep Step,
    AlbumRefreshStepOutcome Outcome,
    string DetailMn);

/// <summary>
/// What the one action did, in numbers, including what it did not manage to do.
///
/// 🔴 A PARTIAL RUN MUST LOOK PARTIAL. The three commands this action replaces
/// each ended with a cheerful sentence, and a person could run "sync", have the
/// fetch quietly fail, and be told their album was up to date. The rule here is
/// blunt: the closing line is written from the step outcomes, so it cannot say
/// the work finished unless every step actually did.
///
/// WHY EVEN THE UNCHANGED CASE CARRIES A NUMBER. "Дараалал өөрчлөгдсөнгүй" with
/// no number is indistinguishable from a step that never ran - the same
/// ambiguity that let a stale page order survive unnoticed until a user
/// reported it. A number that stays zero is a measurement; a silence is not.
///
/// WHY THE ALBUM ON SCREEN IS ALWAYS NAMED. The screen can show this device's
/// own build or the album the server assembled, and they differ exactly when
/// something has gone wrong or is still in flight - which is the moment a person
/// most needs to know which one they are looking at.
/// </summary>
public sealed record AlbumRefreshReport
{
    private AlbumRefreshReport(
        IReadOnlyList<AlbumRefreshStepResult> steps,
        AlbumOnScreen albumOnScreen)
    {
        Steps = steps;
        AlbumOnScreen = albumOnScreen;
    }

    public IReadOnlyList<AlbumRefreshStepResult> Steps { get; }

    public AlbumOnScreen AlbumOnScreen { get; }

    /// <summary>Every step either did its work or genuinely had none.</summary>
    public bool IsComplete => Steps.Count == 4 && Steps.All(step =>
        step.Outcome is AlbumRefreshStepOutcome.Done or AlbumRefreshStepOutcome.NothingToDo);

    /// <summary>
    /// The run stopped early: a step failed, or a step was never reached.
    /// </summary>
    public bool StoppedEarly => Steps.Any(step => step.Outcome == AlbumRefreshStepOutcome.NotReached);

    public bool AnyStepFailed => Steps.Any(step => step.Outcome == AlbumRefreshStepOutcome.Failed);

    public AlbumRefreshStepOutcome OutcomeOf(AlbumRefreshStep step) =>
        Steps.FirstOrDefault(item => item.Step == step)?.Outcome
        ?? AlbumRefreshStepOutcome.NotReached;

    /// <summary>
    /// The full report, one line per step and a closing line that agrees with
    /// them.
    /// </summary>
    public string ComposeMn()
    {
        var lines = new List<string>();
        foreach (AlbumRefreshStepResult step in Steps)
            lines.Add(Mark(step.Outcome) + " " + step.DetailMn);

        lines.Add("");
        lines.Add(ClosingLineMn);
        lines.Add(AlbumOnScreenLineMn);
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Every step's numbers on one line, then the verdict, then which album is
    /// on screen.
    ///
    /// 🔴 THE NUMBERS ARE THE POINT, not the verdict. "Альбом шинэчлэгдлээ" is
    /// what the three commands this action replaces already said, and it is
    /// precisely what left a person unable to tell a run that did something
    /// from a run that did nothing. Four counts and a statement of which album
    /// they are looking at is a different kind of sentence: it can be checked.
    /// </summary>
    public string StatusLineMn =>
        string.Join(" · ", Steps.Select(step => step.DetailMn)) +
        " — " + ClosingLineMn + " " + AlbumOnScreenLineMn;

    /// <summary>
    /// The one sentence a person reads if they read nothing else. It is derived
    /// from the outcomes rather than passed in, so no call site can write
    /// "дууслаа" over a run that did not.
    /// </summary>
    public string ClosingLineMn
    {
        get
        {
            if (IsComplete)
                return "Альбом шинэчлэгдэж дууслаа.";

            if (OutcomeOf(AlbumRefreshStep.FetchCloudUpdates) == AlbumRefreshStepOutcome.Failed)
            {
                // 🔴 THE SHAPE MASTER ASKED FOR IN SO MANY WORDS: say what DID
                // happen and what did NOT, in the same breath, rather than
                // reporting a failure that erases the successful half.
                bool sent = OutcomeOf(AlbumRefreshStep.SendOwnContribution) == AlbumRefreshStepOutcome.Done;
                return sent
                    ? "Таны оруулга үүл рүү өгөгдсөн; бусдын шинэчлэлтийг татаж чадсангүй."
                    : "Бусдын шинэчлэлтийг татаж чадсангүй.";
            }

            if (OutcomeOf(AlbumRefreshStep.ReadOwnSources) == AlbumRefreshStepOutcome.Failed)
                return "Эх үүсвэрийг уншиж чадсангүй; үүл рүү юу ч илгээгээгүй.";

            if (OutcomeOf(AlbumRefreshStep.SendOwnContribution) == AlbumRefreshStepOutcome.Failed)
                return "Таны оруулгыг үүл рүү өгч чадсангүй; альбом хэвээр байна.";

            if (OutcomeOf(AlbumRefreshStep.RecomposePages) == AlbumRefreshStepOutcome.Failed)
                return "Хуудасны бүрдэл, дарааллыг шинэчилж чадсангүй.";

            return "Альбомын шинэчлэл бүрэн дуусаагүй.";
        }
    }

    public string AlbumOnScreenLineMn => AlbumOnScreen switch
    {
        AlbumOnScreen.CloudCanonical => "Дэлгэц дээр: үүлэн нэгдсэн альбом.",
        AlbumOnScreen.LocalPreview => "Дэлгэц дээр: энэ төхөөрөмжийн өөрийн бүтээсэн хувилбар.",
        _ => "Дэлгэц дээр: альбом хараахан байхгүй.",
    };

    private static string Mark(AlbumRefreshStepOutcome outcome) => outcome switch
    {
        AlbumRefreshStepOutcome.Done => "✓",
        AlbumRefreshStepOutcome.NothingToDo => "–",
        AlbumRefreshStepOutcome.Failed => "✗",
        _ => "·",
    };

    /// <summary>
    /// Builds a report. The steps are supplied in the order they ran, and any
    /// step of the four that is absent is recorded as NOT REACHED rather than
    /// left out - a missing line would read as a step that had nothing to do.
    /// </summary>
    public static AlbumRefreshReport Create(
        IEnumerable<AlbumRefreshStepResult> steps,
        AlbumOnScreen albumOnScreen)
    {
        var supplied = steps.ToList();
        var complete = new List<AlbumRefreshStepResult>();
        foreach (AlbumRefreshStep step in new[]
        {
            AlbumRefreshStep.ReadOwnSources,
            AlbumRefreshStep.SendOwnContribution,
            AlbumRefreshStep.FetchCloudUpdates,
            AlbumRefreshStep.RecomposePages,
        })
        {
            AlbumRefreshStepResult? result = supplied.FirstOrDefault(item => item.Step == step);
            complete.Add(result ?? new AlbumRefreshStepResult(
                step,
                AlbumRefreshStepOutcome.NotReached,
                DefaultNotReachedTextMn(step)));
        }

        return new AlbumRefreshReport(complete, albumOnScreen);
    }

    private static string DefaultNotReachedTextMn(AlbumRefreshStep step) => step switch
    {
        AlbumRefreshStep.ReadOwnSources => "Эх үүсвэр уншаагүй.",
        AlbumRefreshStep.SendOwnContribution => "Үүл рүү юу ч илгээгээгүй.",
        AlbumRefreshStep.FetchCloudUpdates => "Үүлнээс юу ч татаагүй.",
        AlbumRefreshStep.RecomposePages => "Хуудасны дараалал шалгагдаагүй.",
        _ => "",
    };

    // The step lines the orchestrator uses, kept here so the wording - and the
    // rule that an unchanged result still carries its number - lives with the
    // report rather than being retyped at each call site.

    public static AlbumRefreshStepResult SourcesRead(int checkedCount, int changedCount) =>
        new(
            AlbumRefreshStep.ReadOwnSources,
            changedCount > 0 ? AlbumRefreshStepOutcome.Done : AlbumRefreshStepOutcome.NothingToDo,
            $"Эх үүсвэр: {checkedCount} шалгав, {changedCount} өөрчлөгдсөн.");

    public static AlbumRefreshStepResult SourcesFailed(string reasonMn) =>
        new(AlbumRefreshStep.ReadOwnSources, AlbumRefreshStepOutcome.Failed,
            "Эх үүсвэрийг уншиж чадсангүй: " + reasonMn);

    public static AlbumRefreshStepResult ContributionSent(int componentCount) =>
        new(
            AlbumRefreshStep.SendOwnContribution,
            componentCount > 0 ? AlbumRefreshStepOutcome.Done : AlbumRefreshStepOutcome.NothingToDo,
            componentCount > 0
                ? $"Таны оруулга: {componentCount} хэсэг үүл рүү өгөв."
                : "Таны оруулга: өгөх шинэ хэсэг байсангүй.");

    public static AlbumRefreshStepResult ContributionFailed(string reasonMn) =>
        new(AlbumRefreshStep.SendOwnContribution, AlbumRefreshStepOutcome.Failed,
            "Таны оруулгыг өгч чадсангүй: " + reasonMn);

    public static AlbumRefreshStepResult CloudFetched(bool cloudChanged, string revisionLabel) =>
        new(
            AlbumRefreshStep.FetchCloudUpdates,
            cloudChanged ? AlbumRefreshStepOutcome.Done : AlbumRefreshStepOutcome.NothingToDo,
            cloudChanged
                ? $"Үүлэн альбом: шинэ хувилбар {revisionLabel} татав."
                : "Үүлэн альбом: өөрчлөгдөөгүй, дахин татаагүй.");

    public static AlbumRefreshStepResult CloudFetchFailed(string reasonMn) =>
        new(AlbumRefreshStep.FetchCloudUpdates, AlbumRefreshStepOutcome.Failed,
            "Бусдын шинэчлэлтийг татаж чадсангүй: " + reasonMn);

    public static AlbumRefreshStepResult PagesRecomposed(int pageCount, int movedCount) =>
        new(
            AlbumRefreshStep.RecomposePages,
            movedCount > 0 ? AlbumRefreshStepOutcome.Done : AlbumRefreshStepOutcome.NothingToDo,
            // The unchanged case keeps its numbers on purpose - see the type
            // comment. "Nothing moved" and "nothing was examined" must not look
            // alike.
            movedCount > 0
                ? $"Хуудасны дараалал: {pageCount} хуудсаас {movedCount} шилжив."
                : $"Хуудасны дараалал: {pageCount} хуудас шалгав, өөрчлөгдсөнгүй.");

    public static AlbumRefreshStepResult RecomposeFailed(string reasonMn) =>
        new(AlbumRefreshStep.RecomposePages, AlbumRefreshStepOutcome.Failed,
            "Хуудасны дарааллыг шинэчилж чадсангүй: " + reasonMn);
}
