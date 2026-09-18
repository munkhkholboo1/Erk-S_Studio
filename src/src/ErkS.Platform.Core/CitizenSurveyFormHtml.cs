using System.Text;

namespace ErkS.Platform.Core;

/// <summary>
/// The survey as a single self-contained page a person can fill in.
///
/// \U0001F534 WHY THIS EXISTS AT ALL. The server route that serves the public form is not
/// built yet, and the owner is running a trial with the project's own members:
/// \u00ab\u0431\u0438 \u0442\u04e9\u0441\u043b\u0438\u0439\u043d \u0433\u0438\u0448\u04af\u04af\u0434\u044d\u044d\u0440 \u04e9\u04e9\u0440\u0441\u0434\u04e9\u04e9\u0440 \u043d\u044c \u0441\u0443\u0434\u0430\u043b\u0433\u0430\u0430 \u0431\u04e9\u0433\u043b\u04af\u04af\u043b\u0436 \u0431\u043e\u043b\u043e\u0432\u0441\u0440\u0443\u0443\u043b\u0430\u043b\u0442 \u0445\u0438\u0439\u0433\u0434\u044d\u0436 \u0431\u0430\u0439\u0433\u0430\u0430 \u044d\u0441\u044d\u0445\u0438\u0439\u0433
/// \u0448\u0430\u043b\u0433\u0430\u043d\u0430.\u00bb What has to be true for that trial is not that a QR resolves - it is
/// that real answers from real people reach the analysis. This file is that path, and it
/// needs no server, no network and no install: one .html on a shared folder or a memory
/// stick, opened by whoever is filling it in.
///
/// \U0001F534 THE OWNER'S WORDS, REPRODUCED EXACTLY - the same invariant the public form owes
/// (contract invariant 7). No phrase substitution runs here because nothing here is
/// served; the text is written into the file as published. Every string that reaches the
/// page goes through <see cref="Escape"/>, which is about HTML correctness, not rewording:
/// an ampersand in a question must still read as an ampersand.
///
/// \u26a0 TICKED FIRST, WRITTEN LAST, exactly as the window and the owner's own paper do it.
/// A person who meets a blank box halfway through a form often stops there.
///
/// \U0001F534 ONE FILE, TWO MODES, AND THE PAGE DECIDES BY WHERE IT IS. Served over http(s)
/// it POSTs back to its own address; opened from a folder it writes the file out instead.
/// The body is byte-identical either way, so the offline route stays available if the
/// server is not up - and neither mode is a second implementation that could drift.
///
/// \u26a0 IT WRITES A <see cref="CitizenSurveyResponseDocument"/>, NOT A SHAPE OF ITS OWN. The
/// file a member hands back is deserialised by the same reader that reads collected
/// answers, so there is one format and one merge - a second one would drift, and the
/// drift would be invisible until a trial's answers failed to load.
/// </summary>
public static class CitizenSurveyFormHtml
{
    /// <summary>What a filled-in file is called, so Studio can offer to read them.</summary>
    public const string AnswerFileExtension = ".erksanswer";

    public static string Build(ProjectCitizenSurvey? survey)
    {
        ArgumentNullException.ThrowIfNull(survey);

        var page = new StringBuilder();
        page.AppendLine("<!DOCTYPE html>");
        page.AppendLine("<html lang=\"mn\"><head><meta charset=\"utf-8\">");
        page.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        page.Append("<title>").Append(Escape(survey.Title)).AppendLine("</title>");
        page.AppendLine(Style);
        page.AppendLine("</head><body>");

        page.Append("<h1>").Append(Escape(survey.Title)).AppendLine("</h1>");
        if (!string.IsNullOrWhiteSpace(survey.Purpose))
            page.Append("<p class=\"purpose\">").Append(Escape(survey.Purpose)).AppendLine("</p>");

        page.AppendLine("<form id=\"f\">");

        var number = 0;
        foreach (CitizenSurveyQuestion question in survey.TickedQuestions())
            AppendQuestion(page, question, ++number);

        IReadOnlyList<CitizenSurveyQuestion> written = survey.WrittenQuestions();
        if (written.Count > 0)
        {
            page.AppendLine("<h2>\u0411\u0438\u0447\u043c\u044d\u043b \u0445\u044d\u0441\u044d\u0433</h2>");
            foreach (CitizenSurveyQuestion question in written)
                AppendQuestion(page, question, ++number);
        }

        page.AppendLine("</form>");
        page.AppendLine("<button id=\"save\" type=\"button\">\u0425\u0430\u0440\u0438\u0443\u043b\u0442\u044b\u0433 \u0445\u0430\u0434\u0433\u0430\u043b\u0430\u0445</button>");
        page.AppendLine("<p class=\"note\" id=\"done\"></p>");
        page.Append(Script(survey.Id));
        page.AppendLine("</body></html>");
        return page.ToString();
    }

    private static void AppendQuestion(StringBuilder page, CitizenSurveyQuestion question, int number)
    {
        page.Append("<fieldset data-q=\"").Append(Escape(question.Id)).AppendLine("\">");
        page.Append("<legend>").Append(number).Append(". ").Append(Escape(question.Text));
        if (!string.IsNullOrWhiteSpace(question.Unit))
            page.Append(" <span class=\"unit\">(").Append(Escape(question.Unit)).Append(")</span>");
        page.AppendLine("</legend>");

        switch (question.Kind)
        {
            case CitizenSurveyQuestionKinds.FreeText:
                page.AppendLine("<textarea rows=\"4\" data-text=\"1\"></textarea>");
                break;

            case CitizenSurveyQuestionKinds.Number:
                page.AppendLine("<input type=\"number\" step=\"1\" min=\"0\" data-number=\"1\">");
                break;

            default:
                string type = question.Kind == CitizenSurveyQuestionKinds.MultipleChoice
                    ? "checkbox"
                    : "radio";
                foreach (CitizenSurveyOption option in question.Options)
                {
                    page.Append("<label><input type=\"").Append(type)
                        .Append("\" name=\"").Append(Escape(question.Id))
                        .Append("\" value=\"").Append(Escape(option.Id)).Append("\"> ")
                        .Append(Escape(option.Text)).AppendLine("</label>");
                    if (option.InvitesOwnWords)
                        page.AppendLine("<input type=\"text\" class=\"own\" data-text=\"1\">");
                }

                break;
        }

        page.AppendLine("</fieldset>");
    }

    /// <summary>
    /// HTML escaping only. It changes how a character is spelled for a browser, never
    /// which character the reader sees - the owner's wording survives it unchanged.
    /// </summary>
    private static string Escape(string? value) => (value ?? "")
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);

    private const string Style = """
        <style>
        body { font-family: Segoe UI, Arial, sans-serif; max-width: 760px; margin: 24px auto;
               padding: 0 16px; color: #1c1c1c; background: #fff; line-height: 1.5; }
        h1 { font-size: 22px; } h2 { font-size: 17px; margin-top: 28px; }
        .purpose { color: #555; }
        fieldset { border: 1px solid #ddd; border-radius: 6px; margin: 0 0 14px; padding: 10px 14px; }
        legend { font-weight: 600; padding: 0 6px; }
        label { display: block; padding: 3px 0; }
        input[type=text], input[type=number], textarea { width: 100%; padding: 6px;
               border: 1px solid #ccc; border-radius: 4px; font: inherit; }
        .own { margin: 2px 0 6px 22px; width: calc(100% - 22px); }
        .unit { font-weight: 400; color: #666; }
        button { font: inherit; padding: 10px 18px; border: 0; border-radius: 6px;
                 background: #1f6feb; color: #fff; cursor: pointer; }
        .note { color: #137333; font-weight: 600; }
        </style>
        """;

    /// <summary>
    /// \u26a0 THE KEY NAMES BELOW ARE THE DTO'S, AND A TEST HOLDS THEM THERE. Renaming a
    /// property on <see cref="CitizenSurveyResponse"/> without changing this string would
    /// produce files that parse into empty answers - every member's form loading as blank,
    /// silently, on the day of a trial.
    /// </summary>
    private static string Script(string surveyId) => $$"""
        <script>
        document.getElementById('save').addEventListener('click', function () {
          var answers = [];
          document.querySelectorAll('fieldset').forEach(function (box) {
            var id = box.getAttribute('data-q');
            var picked = [];
            box.querySelectorAll('input[type=radio],input[type=checkbox]').forEach(function (i) {
              if (i.checked) picked.push(i.value);
            });
            var text = '';
            box.querySelectorAll('[data-text]').forEach(function (t) {
              if (t.value && t.value.trim()) text = t.value.trim();
            });
            var numberBox = box.querySelector('[data-number]');
            var num = numberBox && numberBox.value !== '' ? Number(numberBox.value) : null;
            if (picked.length || text || num !== null) {
              answers.push({ QuestionId: id, OptionIds: picked, Text: text, Number: num });
            }
          });
          if (!answers.length) {
            document.getElementById('done').textContent = 'Хариулт бөглөөгүй байна.';
            return;
          }
          // \u26a0 MINTED ONCE PER FILLED-IN FORM, NOT PER ATTEMPT. A phone on a weak
          // signal retries; a new id per attempt would enter that person two, three
          // times and nothing downstream could tell the copies apart from real people.
          window.erksResponseId = window.erksResponseId ||
            (crypto.randomUUID ? crypto.randomUUID() : String(Date.now()) + Math.random())
              .replace(/-/g, '');
          var doc = {
            SurveyId: '{{surveyId}}',
            Cursor: '',
            CollectedAtUtc: new Date().toISOString(),
            Responses: [{
              Id: window.erksResponseId,
              SubmittedAtUtc: new Date().toISOString(),
              Answers: answers
            }]
          };
          var said = document.getElementById('done');
          var body = JSON.stringify(doc, null, 2);
          if (location.protocol === 'http:' || location.protocol === 'https:') {
            said.textContent = 'Илгээж байна…';
            fetch(location.pathname, {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: body
            }).then(function (r) {
              if (!r.ok) throw new Error(String(r.status));
              said.textContent = 'Баярлалаа. Таны хариулт хүлээн авагдлаа.';
              document.getElementById('save').disabled = true;
            }).catch(function () {
              said.textContent =
                'Илгээж чадсангүй. Дахин дарна уу — давхар бүртгэгдэхгүй.';
            });
            return;
          }
          var blob = new Blob([body], { type: 'application/json' });
          var a = document.createElement('a');
          a.href = URL.createObjectURL(blob);
          a.download = doc.Responses[0].Id + '{{CitizenSurveyFormHtml.AnswerFileExtension}}';
          a.click();
          said.textContent = 'Хадгалагдлаа. Файлыг төслийн ажилтанд өгнө үү.';
        });
        </script>
        """;
}
