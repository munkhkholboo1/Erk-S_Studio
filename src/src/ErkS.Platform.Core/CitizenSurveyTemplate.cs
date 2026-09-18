namespace ErkS.Platform.Core;

/// <summary>
/// The citizens' survey for a partial master plan, as the owner wrote it.
///
/// 🔴 TRANSCRIBED FROM THEIR DOCUMENT, NOT COMPOSED. Every question and every option below
/// is a line of «санал асуулга.docx» (2026-09-18). A survey is a legal instrument in a
/// planning process - the words citizens answer are quoted back in the plan - so inventing
/// a smoother phrasing would put words in the owner's mouth. The extraction is vendored
/// beside the tests and a test compares this list against it, so a typo here goes red
/// rather than shipping.
///
/// 🔴 THE PLACE NAME IS A PARAMETER BECAUSE FOUR QUESTIONS CARRY IT. «Зуунмод хотын төв»
/// appears in questions 11, 13, 14 and 15 of their form. Shipped as a constant it would be
/// correct once and wrong for every other settlement, and the owner asked for a survey per
/// PROJECT. The five баг names are Zuunmod's own, so they are seeded and then edited - a
/// different town has different ones and nobody but the owner knows them.
///
/// ⚠ WHAT I DECIDED AND THE OWNER MAY DISAGREE WITH: question 11 asks which service
/// organisations should be ADDED - «байгууллагуудыг», plural, with a long written line
/// after «Бусад» - so it is set as MULTIPLE choice. The rest are single. Question 14 is
/// plural too but its last option is «Бүгдийг хослуулсан зохион байгуулалт», which only
/// makes sense as one choice among the others. Every kind is editable in Studio, so this
/// is a starting point rather than a ruling.
/// </summary>
public static class CitizenSurveyTemplate
{
    /// <summary>The placeholder the owner's own heading uses for the settlement.</summary>
    public const string SettlementToken = "{СУУРИН}";

    public const string DefaultTitle = "ИРГЭДИЙН САНАЛ АСУУЛГА";

    public const string DefaultPurpose =
        "Судалгааны зорилго: Хэсэгчилсэн Ерөнхий төлөвлөгөө боловсруулахад бодит байдалд " +
        "нийцүүлэн хэрэгжүүлэх боломжтой хэтийн төлөвлөгөө боловсруулахад оршино.";

    /// <summary>
    /// The owner's form, with the settlement name written into the questions that name it.
    /// </summary>
    /// <param name="settlementName">«Зуунмод», or whichever town this project plans.</param>
    public static ProjectCitizenSurvey CreatePartialMasterPlanSurvey(string? settlementName)
    {
        string place = (settlementName ?? "").Trim();
        string town = place.Length == 0 ? SettlementToken : place;

        var survey = new ProjectCitizenSurvey
        {
            Title = DefaultTitle,
            Purpose = DefaultPurpose,
            SettlementName = place,
            IsOpen = false,
        };

        var order = 0;

        Ask("Таны оршин суудаг баг:", CitizenSurveyQuestionKinds.SingleChoice,
            [
                ("1-р баг (Номт)", false),
                ("2-р баг (Ланс)", false),
                ("3-р баг (Баянхошуу)", false),
                ("4-р баг (Зуунмод)", false),
                ("5-р баг (Нацагдорж)", false),
                ("Бусад", true),
            ]);

        Ask("Таны нас:", CitizenSurveyQuestionKinds.SingleChoice,
            [("18–24", false), ("25–39", false), ("40–59", false), ("60-аас дээш", false)]);

        Ask("Таны хүйс:", CitizenSurveyQuestionKinds.SingleChoice,
            [("Эрэгтэй", false), ("Эмэгтэй", false)]);

        AskNumber("Ам бүлийн тоо:", "хүн");

        Ask("Та ямар сууцанд амьдардаг вэ?", CitizenSurveyQuestionKinds.SingleChoice,
            [
                ("Гэр сууц, хашаа", false),
                ("Байшин сууц, хашаа", false),
                ("Орон сууц", false),
                ("Нийтийн байр", false),
            ]);

        Ask("Та тухайн газарт хэдэн жил амьдарч байгаа вэ?", CitizenSurveyQuestionKinds.SingleChoice,
            [
                ("Түр оршин суугч", false),
                ("10 хүртэлх жил", false),
                ("10-20жил", false),
                ("20-аас дээш жил", false),
            ]);

        Ask("Та цаашид шинээр төлөвлөж буй орон сууцны хороололд амьдрах уу?",
            CitizenSurveyQuestionKinds.SingleChoice,
            [("Тийм", false), ("Үгүй", false)]);

        Ask("Та одоо амьдарч байгаа хашаа байшингийн нөхцөлөө сайжруулах уу?",
            CitizenSurveyQuestionKinds.SingleChoice,
            [
                ("Шинээр байшин барина", false),
                ("Одоо байгаа байшингаа өргөтгөнө", false),
                ("Орон сууцанд орно", false),
            ]);

        Ask("Та хаана ажилладаг вэ?", CitizenSurveyQuestionKinds.SingleChoice,
            [
                ("Хувийн байгууллагад", false),
                ("Төрийн байгууллагад", false),
                ("Хувиараа бизнес эрхлэгч", false),
                ("Бусад", true),
            ]);

        Ask("Танай өрхийн орлогын хэмжээ", CitizenSurveyQuestionKinds.SingleChoice,
            [
                ("500,000-1,000,000", false),
                ("1,000,000-2,000,000", false),
                ("2,000,000- төгрөгөөс дээш", false),
            ]);

        // ⚠ The one set as MULTIPLE - see the note on this class.
        Ask($"{town} хотын төвийн ойрын ирээдүйд (5-10 жил) ямар төрийн үйлчилгээний " +
            "байгууллагуудыг нэмэгдүүлэх шаардлагатай вэ?",
            CitizenSurveyQuestionKinds.MultipleChoice,
            [
                ("Цэцэрлэг, сургууль", false),
                ("Өрхийн болон нэгдсэн эмнэлэг", false),
                ("Ахмадын сувилал", false),
                ("Хүүхэд, залуучуудын хөгжлийн төв", false),
                ("Зочид буудал", false),
                ("Өдөр тутмын өргөн хэрэглээний худалдааны төв", false),
                ("Бусад", true),
            ]);

        Ask("Одоогийн хотын төвийн газрын ашиглалтыг та хэрхэн дүгнэж байна вэ?",
            CitizenSurveyQuestionKinds.SingleChoice,
            [
                ("Барилгажилт хэт нягтаршсан, зай талбай багатай", false),
                ("Зарим хэсэгт ашиглалт муу, дахин төлөвлөлт хийх", false),
                ("Зохион байгуулалт сайн, тэнцвэртэй", false),
                ("Тодорхой мэдээлэлгүй", false),
            ]);

        Ask($"{town} хотын төвийн ирээдүйг хэрхэн хөгжүүлэхийг хүсэж байна вэ?",
            CitizenSurveyQuestionKinds.SingleChoice,
            [
                ("Үйлдвэржилтийг дэмжсэн", false),
                ("Хөдөө аж ахуйн салбарыг дэмжсэн", false),
                ("Аялал,жуулчлалыг дэмжсэн", false),
                ("Харилцаа,холбоо,худалдааны салбарыг дэмжсэн", false),
                ("Бусад", true),
            ]);

        Ask($"{town} хотын төвийн орчин, нөхцөлийг сайжруулах чиглэлээр ямар бүтээн " +
            "байгуулалтууд хийх хэрэгтэй вэ?",
            CitizenSurveyQuestionKinds.SingleChoice,
            [
                ("Түүхэн ба соёлын өв уламжлалыг шингээсэн", false),
                ("Эко, ногоон барилгажилтын стандартыг баримталсан", false),
                ("Оновчтой, их хотын шинэлэг өнгө төрх бүхий", false),
                ("Бүгдийг хослуулсан зохион байгуулалт", false),
            ]);

        AskFreeText($"{town} хотын төвийн хөгжлийг 10 жилийн дараа ямар байхыг төсөөлж байна вэ?");
        AskFreeText("Ерөнхий төлөвлөгөөнд тусгах таны санал:");

        return survey;

        void Ask(string text, string kind, IReadOnlyList<(string Text, bool OwnWords)> options)
        {
            survey.Questions.Add(new CitizenSurveyQuestion
            {
                Order = ++order,
                Text = text,
                Kind = kind,
                Options = options
                    .Select(option => new CitizenSurveyOption
                    {
                        Text = option.Text,
                        InvitesOwnWords = option.OwnWords,
                    })
                    .ToList(),
            });
        }

        void AskNumber(string text, string unit) =>
            survey.Questions.Add(new CitizenSurveyQuestion
            {
                Order = ++order,
                Text = text,
                Kind = CitizenSurveyQuestionKinds.Number,
                Unit = unit,
            });

        void AskFreeText(string text) =>
            survey.Questions.Add(new CitizenSurveyQuestion
            {
                Order = ++order,
                Text = text,
                Kind = CitizenSurveyQuestionKinds.FreeText,
            });
    }
}
