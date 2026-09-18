namespace ErkS.Platform.Core;

/// <summary>What a question asks for, and therefore how it can be answered and counted.</summary>
public static class CitizenSurveyQuestionKinds
{
    /// <summary>One option of several.</summary>
    public const string SingleChoice = "SingleChoice";

    /// <summary>
    /// Any number of options. Kept apart from <see cref="SingleChoice"/> because the two
    /// cannot share a tally: shares of a multiple-choice question add up past 100% and
    /// presenting them as a whole would be a lie in a document a council reads.
    /// </summary>
    public const string MultipleChoice = "MultipleChoice";

    /// <summary>A count written in, «Ам бүлийн тоо: ____ хүн».</summary>
    public const string Number = "Number";

    /// <summary>Sentences in the citizen's own words.</summary>
    public const string FreeText = "FreeText";

    public static readonly IReadOnlyList<string> All =
        [SingleChoice, MultipleChoice, Number, FreeText];

    public static bool TakesOptions(string? kind) =>
        SingleChoice.Equals(kind, StringComparison.Ordinal) ||
        MultipleChoice.Equals(kind, StringComparison.Ordinal);
}

/// <summary>One answer a citizen can tick.</summary>
public sealed class CitizenSurveyOption
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Text { get; set; } = "";

    /// <summary>
    /// «Бусад: ______» — ticking it also opens a line to write on.
    ///
    /// 🔴 THE WRITTEN HALF IS THE PART WORTH READING AND THE PART EASIEST TO LOSE. A tally
    /// that counted this option and dropped the words behind it would report «Бусад: 34»
    /// and destroy the only place the survey lets somebody say something nobody thought
    /// to ask. The words are carried on the answer and reported beside the count.
    /// </summary>
    public bool InvitesOwnWords { get; set; }
}

/// <summary>One question of the survey.</summary>
public sealed class CitizenSurveyQuestion
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Where it stands on the paper. 1-based, as a citizen counts.</summary>
    public int Order { get; set; }

    public string Text { get; set; } = "";

    /// <summary><see cref="CitizenSurveyQuestionKinds"/>.</summary>
    public string Kind { get; set; } = CitizenSurveyQuestionKinds.SingleChoice;

    /// <summary>What a written number counts - «хүн». Shown after the box.</summary>
    public string Unit { get; set; } = "";

    public List<CitizenSurveyOption> Options { get; set; } = [];

    public CitizenSurveyQuestion Clone() => new()
    {
        Id = Id,
        Order = Order,
        Text = Text,
        Kind = Kind,
        Unit = Unit,
        Options = Options
            .Select(option => new CitizenSurveyOption
            {
                Id = option.Id,
                Text = option.Text,
                InvitesOwnWords = option.InvitesOwnWords,
            })
            .ToList(),
    };
}

/// <summary>One citizen's filled-in form.</summary>
public sealed class CitizenSurveyResponse
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset SubmittedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public List<CitizenSurveyAnswer> Answers { get; set; } = [];
}

/// <summary>What one citizen said to one question.</summary>
public sealed class CitizenSurveyAnswer
{
    public string QuestionId { get; set; } = "";

    /// <summary>Ticked options. One for a single choice, any number for a multiple.</summary>
    public List<string> OptionIds { get; set; } = [];

    /// <summary>Their own words - a free-text answer, or the line beside «Бусад».</summary>
    public string Text { get; set; } = "";

    /// <summary>A written count. Absent rather than zero when nothing was written.</summary>
    public double? Number { get; set; }
}

/// <summary>
/// The citizens' survey a project collects, and the form a QR code opens.
///
/// 🔴 IT BELONGS TO THE PROJECT, WHICH IS THE WHOLE POINT OF THE REQUEST: «студио дээр
/// төсөлтэй холбох ёстой. Аль төслөөс qr үүсгэхээ сонгодог байна. иргэд санал асуулгыг
/// бөглөхөд тухайн төсөл дээр нь нэгтгэгдэж үр дүн нь боловсруулагддаг байна.» So the
/// survey is a section of the workspace like the portfolio or the boards, not a separate
/// document that has to be matched back up afterwards.
///
/// ⚠ THE QUESTIONS ARE THE PROJECT'S, NOT THE PRODUCT'S. The form the owner supplied
/// names Зуунмод in four of its questions, so a survey shipped as a fixed list would be
/// wrong for the second town that used it. It is seeded from their document by
/// <see cref="CitizenSurveyTemplate"/> and then edited per project.
/// </summary>
public sealed class ProjectCitizenSurvey
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Where it sits in the project's list of surveys.</summary>
    public int Order { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Where this survey's answers are kept, relative to the project folder.
    ///
    /// 🔴 THEIR OWN FILE, BECAUSE A CONSULTATION IS THOUSANDS OF ROWS. Held inside the
    /// project file, a few hundred citizens would make every ordinary save rewrite - and
    /// eventually re-verify - a document that is opened on every screen in Studio. The
    /// album keeps its pages the same way, for the same reason.
    /// </summary>
    public string ResponsesRelativePath { get; set; } = "";

    /// <summary>The code the public form is reached by - erk-s.mn/s/{code}.</summary>
    public string PublicCode { get; set; } = "";

    /// <summary>The full address the QR encodes, as the server issued it.</summary>
    public string PublicFormUrl { get; set; } = "";

    /// <summary>«ИРГЭДИЙН САНАЛ АСУУЛГА», over the project's own heading.</summary>
    public string Title { get; set; } = "";

    /// <summary>«Судалгааны зорилго: …» - shown to the citizen before the questions.</summary>
    public string Purpose { get; set; } = "";

    /// <summary>The settlement the questions speak about - «Зуунмод».</summary>
    public string SettlementName { get; set; } = "";

    /// <summary>
    /// Whether the form accepts answers right now.
    ///
    /// ⚠ CLOSING IS NOT DELETING. A closed survey keeps every response it gathered; this
    /// only stops new ones. The owner's own rule for their material applies here too -
    /// what a citizen wrote is theirs and is only ever added to.
    /// </summary>
    public bool IsOpen { get; set; }

    public DateTimeOffset? OpenedAtUtc { get; set; }

    public DateTimeOffset? ClosedAtUtc { get; set; }

    public List<CitizenSurveyQuestion> Questions { get; set; } = [];

    /// <summary>
    /// The questions in the order a citizen meets them.
    ///
    /// Sorted rather than trusted: <see cref="CitizenSurveyQuestion.Order"/> is editable,
    /// and a list that merely happened to be in order would put a reordered question in
    /// the wrong place on the printed form only.
    /// </summary>
    public IReadOnlyList<CitizenSurveyQuestion> OrderedQuestions() =>
        Questions.OrderBy(question => question.Order).ThenBy(question => question.Id, StringComparer.Ordinal).ToList();

    public bool HasQuestions => Questions.Count > 0;
}
