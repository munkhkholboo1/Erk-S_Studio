using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// The «Санал асуулга» page: the project's surveys, and what their answers say.
///
/// 🔴 THE RESULTS ARE READ HERE, IN STUDIO - the owner's first correction: «Гол нь студио
/// дээрээ үр дүнг нь авна шүү.» The server carries the form and collects the answers; the
/// reading, the charts and the analysis are Studio's, beside the drawings they inform.
///
/// ⚠ EVERY NUMBER ON THIS PAGE COMES FROM Core, NOT FROM HERE. The tally, the findings,
/// the cross-tab and even the bar geometry are computed and tested in
/// ErkS.Platform.Core; this file turns them into shapes. A share worked out while
/// assembling a panel would be untestable, and the two mistakes it makes - a bar past its
/// track, two questions at different scales - look fine in a screenshot of plausible data.
/// </summary>
internal sealed partial class ShellView
{
    private ListBox? surveyListBox;
    private StackPanel? surveyDetailPanel;
    private ComboBox? surveySplitBox;
    private ComboBox? surveyTemplateBox;
    private string selectedSurveyId = "";
    private string selectedSplitQuestionId = "";

    /// <summary>Set while the detail panel is being rebuilt. See RefreshSurveyDetail.</summary>
    private bool refreshingSurveyDetail;

    /// <summary>The bar track, in pixels. A fixed width keeps every question comparable.</summary>
    private const double SurveyTrackWidth = 320;

    /// <summary>Where a public form lives. Studio mints the code under it, by agreement.</summary>
    private const string StudioSurveyBaseUrl = "https://erk-s.mn";

    private UIElement BuildSurveysPage()
    {
        var root = new DockPanel { Margin = new Thickness(18) };

        var toolbar = new DockPanel
        {
            Margin = new Thickness(0, 0, 0, 12),

            // Otherwise the last button stretches across the whole window.
            LastChildFill = false,
        };
        Button addButton = StudioWidgets.CreatePrimaryButton("Шинэ санал асуулга");
        addButton.Margin = new Thickness(0, 0, 8, 0);
        addButton.Click += (_, _) =>
            AddCitizenSurvey(surveyTemplateBox?.SelectedValue as string ?? "");
        // The list of forms, even while it holds one: the owner will add a form usable on
        // any project, and it has to arrive as a second row rather than as a replacement.
        surveyTemplateBox = new ComboBox
        {
            Width = 380,
            Margin = new Thickness(0, 0, 8, 0),
            DisplayMemberPath = "Label",
            SelectedValuePath = "Id",
            ItemsSource = CitizenSurveyTemplates.All
                .Select(form => new SurveyTemplateRow(
                    form.Id, form.Name + "  • " + form.Availability))
                .ToList(),
        };
        surveyTemplateBox.SelectedIndex = 0;

        var formButton = new Button
        {
            Content = "Маягт гаргах",
            Margin = new Thickness(0, 0, 8, 0),
        };
        formButton.Click += (_, _) => ExportSurveyForm();

        var importButton = new Button
        {
            Content = "Хариулт оруулах",
            Margin = new Thickness(0, 0, 8, 0),
        };
        importButton.Click += (_, _) => ImportSurveyAnswers();

        var definitionButton = new Button
        {
            Content = "Асуулгын JSON",
            Margin = new Thickness(0, 0, 8, 0),
        };
        definitionButton.Click += (_, _) => ExportSurveyDefinition();

        var refreshButton = new Button { Content = "Үр дүнг шинэчлэх" };
        refreshButton.Click += (_, _) => RefreshSurveyDetail();
        toolbar.Children.Add(surveyTemplateBox);
        toolbar.Children.Add(addButton);
        toolbar.Children.Add(formButton);
        toolbar.Children.Add(importButton);
        toolbar.Children.Add(definitionButton);

        var collectButton = new Button
        {
            Content = "Хариулт татах",
            Margin = new Thickness(0, 0, 8, 0),
        };
        collectButton.Click += async (_, _) => await CollectSurveyAnswersAsync();
        toolbar.Children.Add(collectButton);

        var clearButton = new Button
        {
            Content = "Хариулт цэвэрлэх",
            Margin = new Thickness(0, 0, 8, 0),
        };
        clearButton.Click += (_, _) => ClearSurveyAnswers();
        toolbar.Children.Add(clearButton);
        toolbar.Children.Add(refreshButton);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        surveyListBox = new ListBox
        {
            Width = 260,
            Margin = new Thickness(0, 0, 16, 0),
            DisplayMemberPath = "Label",
            SelectedValuePath = "Id",
        };
        // The same hazard one level up: RefreshSurveyWorkspace assigns SelectedValue,
        // which raises this. It is not fatal here - RefreshSurveyDetail does not rebuild
        // the list - but it did refresh the panel twice on every project open, and the
        // guard above now makes the second pass a no-op rather than duplicated work.
        surveyListBox.SelectionChanged += (_, _) =>
        {
            selectedSurveyId = (surveyListBox.SelectedValue as string) ?? "";
            selectedSplitQuestionId = "";
            RefreshSurveyDetail();
        };
        DockPanel.SetDock(surveyListBox, Dock.Left);
        root.Children.Add(surveyListBox);

        surveyDetailPanel = new StackPanel();
        root.Children.Add(new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = surveyDetailPanel,
        });

        return root;
    }

    private sealed record SurveyRow(string Id, string Label);

    private sealed record SurveyTemplateRow(string Id, string Label);

    private void RefreshSurveyWorkspace()
    {
        if (surveyListBox is null || !state.HasOpenProject)
            return;

        state.Project.CitizenSurveys.Normalize();
        List<SurveyRow> rows = state.Project.CitizenSurveys.OrderedSurveys()
            .Select(survey => new SurveyRow(
                survey.Id,
                $"{survey.Order}. {Blank(survey.Title, "Нэргүй асуулга")}" +
                (survey.IsOpen ? "  • нээлттэй" : "")))
            .ToList();

        surveyListBox.ItemsSource = rows;
        if (rows.Count == 0)
        {
            // ⚠ AN EMPTY PAGE EXPLAINS ITSELF. A blank pane reads as «broken», and the
            // owner asked exactly the right question about one: is this where my form
            // appears? It is - once there is one - and the page should be the thing that
            // says so rather than a message somewhere else.
            selectedSurveyId = "";
            surveyDetailPanel?.Children.Clear();
            surveyDetailPanel?.Children.Add(SectionTitle(
                "Энэ төсөлд санал асуулга хараахан үүсээгүй байна."));
            surveyDetailPanel?.Children.Add(Muted(
                "Дээрх жагсаалтаас маягтаа сонгоод «Шинэ санал асуулга» дарна уу. " +
                "Асуулга үүсмэгц зүүн талын жагсаалтад гарч ирнэ; дээр нь дарахад " +
                "оролцогчдын тоо, диаграмм, дүгнэлт, ба QR нь нээгдэнэ."));
            return;
        }

        if (rows.All(row => row.Id != selectedSurveyId))
            selectedSurveyId = rows[0].Id;
        surveyListBox.SelectedValue = selectedSurveyId;
        RefreshSurveyDetail();
    }

    /// <summary>
    /// Starts a survey from one of the offered forms.
    ///
    /// 🔴 THE FORM IS NAMED BY THE CALLER, NOT DECIDED HERE. The owner has one form in
    /// trial use today and will ADD a general one; a button that built «the» survey would
    /// have to choose between them the day the second arrives, inside a method that is
    /// also doing file paths and saving.
    /// </summary>
    private void AddCitizenSurvey(string templateId)
    {
        if (!state.HasOpenProject)
            return;

        // Seeded from the owner's own document, with the project's settlement written in.
        // ⚠ THE DISTRICT, NOT THE PROVINCE: the questions name «Зуунмод хотын төв» -
        // the settlement being planned - and «Төв аймаг» is the province it sits in.
        ProjectSiteLocation where = state.Project.Foundation.InitiationBasis.SiteLocation;
        string settlement = string.IsNullOrWhiteSpace(where.DistrictName)
            ? where.ProvinceName
            : where.DistrictName;
        ProjectCitizenSurvey? survey = CitizenSurveyTemplates.Create(templateId, settlement);
        if (survey is null)
        {
            SetStatus($"«{templateId}» загвар энэ хувилбарт алга.");
            return;
        }

        survey.ResponsesRelativePath = $"surveys/{survey.Id}.erksresponses";

        state.Project.CitizenSurveys.Surveys.Add(survey);
        state.Project.CitizenSurveys.Normalize();
        state.SaveProject();

        selectedSurveyId = survey.Id;
        RefreshSurveyWorkspace();
        SetStatus($"Санал асуулга нэмэгдлээ: {survey.Questions.Count} асуулт.");
    }

    private void RefreshSurveyDetail()
    {
        if (surveyDetailPanel is null || !state.HasOpenProject)
            return;

        // 🔴 A SECOND LOCK, BECAUSE THE FIRST ONE IS AN ORDERING CONVENTION. Selecting
        // before subscribing fixes today's recursion, but every control added here in
        // future carries the same trap: this method rebuilds the whole panel, so any
        // handler that reaches it re-enters. One StackOverflow is unrecoverable - no
        // catch block runs, no message is shown, the window simply disappears - so the
        // cost of a redundant guard is nothing against what it prevents.
        if (refreshingSurveyDetail)
            return;

        refreshingSurveyDetail = true;
        try
        {
            RefreshSurveyDetailCore();
        }
        finally
        {
            refreshingSurveyDetail = false;
        }
    }

    private void RefreshSurveyDetailCore()
    {
        if (surveyDetailPanel is null || !state.HasOpenProject)
            return;

        surveyDetailPanel.Children.Clear();
        ProjectCitizenSurvey? survey = state.Project.CitizenSurveys.Find(selectedSurveyId);
        if (survey is null)
        {
            surveyDetailPanel.Children.Add(Muted(
                "Санал асуулга сонгогдоогүй байна. «Шинэ санал асуулга» дарж эхлүүлнэ үү."));
            return;
        }

        IReadOnlyList<CitizenSurveyResponse> responses;
        try
        {
            responses = LoadSurveyResponses(survey);
        }
        catch (InvalidDataException unreadable)
        {
            // 🔴 THE STORE THROWS ON PURPOSE AND THIS IS THE HAND THAT CATCHES IT. Its own
            // comment says the throw is left to the caller because the caller has a status
            // line - so the caller must actually have one. Showing «0 оролцсон» over
            // answers that are sitting unread on disk would invite the reasonable
            // conclusion that the consultation failed, and it is the ONE reading on this
            // page nothing downstream can correct.
            surveyDetailPanel.Children.Add(SurveyHeading(survey));
            surveyDetailPanel.Children.Add(Warning(
                "⚠ Хариултын файл уншигдсангүй. Энэ нь «хариулт алга» ГЭСЭН ҮГ БИШ — " +
                "бичигдсэн хариулт байж болно. Доорх замыг шалгана уу."));
            surveyDetailPanel.Children.Add(Muted(unreadable.Message));
            SetStatus("Санал асуулгын хариулт уншигдсангүй.");
            return;
        }

        CitizenSurveyResult result = CitizenSurveyTally.Of(survey, responses);

        surveyDetailPanel.Children.Add(SurveyHeading(survey));
        surveyDetailPanel.Children.Add(SurveyKpiRow(survey, result));

        if (result.UnreadableAnswerCount > 0)
        {
            // 🔴 SAID OUT LOUD, NEVER FOLDED IN. These answers named a question or option
            // the survey no longer has; counting them into nothing would make the result
            // look complete while it is short.
            surveyDetailPanel.Children.Add(Warning(
                $"⚠ {result.UnreadableAnswerCount} хариулт одоогийн асуулгад таарахгүй байна " +
                "(асуулга засварлагдсан байж магадгүй). Эдгээр нь доорх тоонд ОРООГҮЙ."));
        }

        surveyDetailPanel.Children.Add(SectionTitle("Сонжоо — үр дүнгээс гарах дүгнэлт"));
        IReadOnlyList<CitizenSurveyFinding> findings = CitizenSurveyFindings.Read(result);

        // 🔴 NOBODY HAS ANSWERED IS ONE FACT, NOT SIXTEEN. Below the reading threshold
        // every question yields its own «not enough answers» card, which is right once
        // answers are arriving and unreadable before any have: the page filled with
        // sixteen identical cards saying nothing, and the one sentence a reader needed -
        // that collection has not started - was nowhere on it.
        //
        // ⚠ The branch below it could never fire. It tested findings.Count == 0, which
        // only happens for a survey with NO QUESTIONS; a survey with questions and no
        // answers produces one card per question. The sentence existed, was correct, and
        // was unreachable - so the empty state was written and never shown.
        // 🔴 «НЭГ Ч АСУУЛТ БОСГОНД ХҮРЭЭГҮЙ» БАС НЭГ БАРИМТ. Below the reading threshold
        // every question yields its own «not enough answers» card. That is right once
        // SOME questions can be read and others cannot - the contrast is the information.
        // While NONE of them can, sixteen identical cards say one thing sixteen times and
        // bury the number that actually matters: how many people have answered so far.
        //
        // ⚠ The first version of this only caught ZERO responses. The owner's own first
        // submission put the page straight back into the same wall of cards - the defect
        // had not been fixed, only moved from 0 to the range 1..29.
        // 🔴 THE QUESTION IS ABOUT THE DATA, NOT ABOUT THE FINDINGS LIST — and my first
        // version got that wrong in a way the owner saw on their own screen. It asked
        // «is every finding TooFew?», but a free-text question with words in it yields a
        // Note («N бичмэл санал ирсэн»), which is neither a reading nor a shortage. One
        // written answer made the condition false and the sixteen-card wall came back.
        bool nothingReadableYet = result.Questions.Count > 0 &&
            result.Questions.All(
                question => question.Answered < CitizenSurveyFindings.MinimumBase);

        if (result.ResponseCount == 0)
        {
            surveyDetailPanel.Children.Add(Muted(
                "Хариулт хараахан ирээгүй байна. QR тараасны дараа энд оролцогчдын тоо, " +
                "асуулт бүрийн диаграмм, задаргаа болон дүгнэлт гарч ирнэ."));
        }
        else if (nothingReadableYet)
        {
            surveyDetailPanel.Children.Add(Muted(
                $"Одоогоор {result.ResponseCount} хүн хариулсан. Асуулт бүрийн тоон үр дүн " +
                "доор бэлэн байна, харин дүгнэлт гаргахад " +
                $"{CitizenSurveyFindings.MinimumBase} хариулт шаардлагатай тул нэг ч " +
                "асуултаар дүгнэлт хийгээгүй байна."));
        }
        else if (findings.Count == 0)
        {
            surveyDetailPanel.Children.Add(Muted("Дүгнэлт гаргах асуулт алга."));
        }
        else
        {
            foreach (CitizenSurveyFinding finding in findings)
                surveyDetailPanel.Children.Add(FindingCard(finding));
        }

        // ⚠ WHAT IS NOT A SHORTAGE IS STILL SHOWN. Below the base the «not enough
        // answers» cards are noise - they repeat one sentence per question - but a
        // written-answer note is a COUNT of something that actually arrived, and it is
        // useful from the first submission. Suppressing the noise must not suppress it.
        if (nothingReadableYet)
        {
            foreach (CitizenSurveyFinding finding in findings)
            {
                if (finding.Weight != CitizenSurveyFindingWeights.TooFew)
                    surveyDetailPanel.Children.Add(FindingCard(finding));
            }
        }

        // ⚠ THE NUMBERS STAY EITHER WAY. Only the READING is withheld below the base;
        // the counts, the bars and the split are shown from the first answer, because
        // they are facts rather than conclusions.

        // 🔴 TICKED FIRST, WRITTEN LAST, AND SEPARATELY - the owner's instruction, and
        // their own paper already reads that way. The two blocks are counted differently
        // too: a tick becomes a share, a sentence can only be read.
        var written = new HashSet<string>(
            survey.WrittenQuestions().Select(question => question.Id), StringComparer.Ordinal);

        surveyDetailPanel.Children.Add(SectionTitle("Сонголттой асуултууд — тоон үр дүн"));
        foreach (CitizenSurveyQuestionTally question in result.Questions)
        {
            if (!written.Contains(question.QuestionId))
                surveyDetailPanel.Children.Add(QuestionBlock(question));
        }

        surveyDetailPanel.Children.Add(SectionTitle("Задаргаа — нэг асуултыг нөгөөгөөр нь ангилах"));
        surveyDetailPanel.Children.Add(SplitPicker(survey, responses));

        // The written block, at the end and marked as its own thing.
        IReadOnlyList<CitizenSurveyQuestionTally> sentences = result.Questions
            .Where(question => written.Contains(question.QuestionId))
            .ToList();
        if (sentences.Count > 0)
        {
            surveyDetailPanel.Children.Add(SectionTitle("Бичмэл хэсэг — иргэдийн өөрийн үг"));
            surveyDetailPanel.Children.Add(Muted(
                "Эдгээрийг хувь, диаграмаар орлуулах боломжгүй — бүтнээр нь уншиж " +
                "төлөвлөгөөнд тусгана уу."));
            foreach (CitizenSurveyQuestionTally question in sentences)
                surveyDetailPanel.Children.Add(QuestionBlock(question));
        }
    }

    /// <summary>
    /// Writes the survey out as one self-contained page somebody can fill in.
    ///
    /// 🔴 THE TRIAL RUNS ON THIS, NOT ON THE QR. The server route is not built, and the
    /// owner is having the project's members fill the survey in to check that the
    /// processing works. A file on a shared folder needs no server, no network and no
    /// account - and the answers it produces go through exactly the same reader, tally and
    /// analysis as collected ones will.
    /// </summary>
    private void ExportSurveyForm()
    {
        ProjectCitizenSurvey? survey = state.Project.CitizenSurveys.Find(selectedSurveyId);
        if (survey is null)
        {
            SetStatus("Санал асуулга сонгоно уу.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Санал асуулгын маягт хадгалах",
            Filter = "Веб маягт (*.html)|*.html",

            // 🔴 THE SAME NAME EITHER BUTTON PRODUCES IT. SRV spotted the trap: this page
            // has two exports, and this one used to write «санал-асуулга.html» - a
            // perfectly good offline form that the server CANNOT serve, because the route
            // looks the file up by code. On a busy day the wrong button gets pressed, the
            // file lands in the server's folder, and /s/<code> answers 404 while a
            // correct-looking html sits right there. Naming it by the code removes the
            // choice rather than documenting it; offline use does not care what it is
            // called, so nothing is lost.
            //
            // ⚠ Before a code is issued there is nothing to name it after, and that form
            // is offline-only by definition - so it keeps a descriptive name and the
            // status line says so, instead of inventing a code that would not match.
            FileName = string.IsNullOrWhiteSpace(survey.PublicCode)
                ? "санал-асуулга.html"
                : $"{survey.PublicCode}.html",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllText(
                dialog.FileName, CitizenSurveyFormHtml.Build(survey), Encoding.UTF8);
            SetStatus(
                string.IsNullOrWhiteSpace(survey.PublicCode)
                    ? $"Маягт гарлаа: {Path.GetFileName(dialog.FileName)} — " +
                      $"{survey.Questions.Count} асуулт. Код үүсээгүй тул энэ нь ЗӨВХӨН " +
                      "офлайн: серверт тавихад ажиллахгүй. Бөглөсний дараа «Хариулт оруулах»."
                    : $"Маягт гарлаа: {Path.GetFileName(dialog.FileName)} — " +
                      $"{survey.Questions.Count} асуулт. Серверт ч, офлайн ч ажиллана.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus($"Маягт бичигдсэнгүй: {exception.Message}");
        }
    }

    /// <summary>
    /// Writes the definition the public form is built from.
    ///
    /// 🔴 THIS FILE CARRIES STUDIO'S IDENTIFIERS AND NOTHING MAY ALTER THEM. Every
    /// answer that comes back names a question id and an option id from here; one of them
    /// changed in transit and the answers resolve to nothing, while every number on the
    /// results page still adds up perfectly. That is the one failure this feature cannot
    /// detect from the outside, which is why the ids are pinned by a test rather than
    /// trusted to a convention.
    /// </summary>
    private void ExportSurveyDefinition()
    {
        ProjectCitizenSurvey? survey = state.Project.CitizenSurveys.Find(selectedSurveyId);
        if (survey is null)
        {
            SetStatus("Санал асуулга сонгоно уу.");
            return;
        }

        if (string.IsNullOrWhiteSpace(survey.PublicCode))
        {
            SetStatus("Эхлээд «Код үүсгэж QR гаргах» дарна уу — кодгүй маягт байршуулах боломжгүй.");
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Асуулгын тодорхойлолт ба маягт хадгалах",
            Filter = "JSON (*.json)|*.json",
            FileName = $"{survey.PublicCode}.json",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            // 🔴 TWO FILES, AND THE SECOND REMOVES THE RISKIEST WORK ON THE OTHER SIDE.
            // Without the .html, whoever serves the form has to re-implement the collector
            // script - the element hooks, the response key, the hand-over button and the
            // document's exact keys. One key spelled differently there and Studio reads
            // EVERY answer as empty, silently: a survey with no responses looks perfectly
            // normal. This page's script is tested; a second copy of it would not be.
            //
            // ⚠ IT ALSO MAKES CONTRACT INVARIANT 7 STRUCTURAL. The server returns these
            // bytes unchanged, so there is nothing on that side that could substitute
            // phrases into the owner's wording - it never passes through a page shell.
            string htmlPath = Path.ChangeExtension(dialog.FileName, ".html");

            File.WriteAllText(
                dialog.FileName, CitizenSurveyPublication.ToJson(survey, state.Project.ProjectId), Encoding.UTF8);
            File.WriteAllText(
                htmlPath, CitizenSurveyFormHtml.Build(survey), Encoding.UTF8);

            SetStatus(
                $"Хоёр файл гарлаа: {Path.GetFileName(dialog.FileName)} ба " +
                $"{Path.GetFileName(htmlPath)} — {survey.Questions.Count} асуулт, " +
                $"код {survey.PublicCode}, " +
                (survey.IsOpen ? "НЭЭЛТТЭЙ" : "ХААЛТТАЙ (хариулт авахгүй!)") +
                ". Хоёуланг нь серверт өгнө үү.");
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus($"Тодорхойлолт бичигдсэнгүй: {exception.Message}");
        }
    }

    /// <summary>
    /// Brings the citizens' answers down from the server, without anybody moving a file.
    ///
    /// 🔴 THE CURSOR IS A WATERMARK, NOT A GUARANTEE, and the merge is what makes that
    /// safe. Studio deduplicates on the response id, so a cursor that repeats work costs
    /// bandwidth and nothing else - which is why the server was asked to return MORE than
    /// requested whenever it is unsure. The opposite arrangement, a cursor trusted to be
    /// exact, would lose a citizen's answer with no outward sign at all.
    ///
    /// ⚠ EVERY OUTCOME IS NAMED, INCLUDING THE ZEROES. «0 шинэ» after a real
    /// collection and «0 шинэ» because nothing could be read are different facts, and
    /// a single silent number would let the second pass for the first.
    /// </summary>
    private async Task CollectSurveyAnswersAsync()
    {
        ProjectCitizenSurvey? survey = state.Project.CitizenSurveys.Find(selectedSurveyId);
        if (survey is null)
        {
            SetStatus("Санал асуулга сонгоно уу.");
            return;
        }

        string projectId = state.Project.ProjectId;
        if (string.IsNullOrWhiteSpace(projectId))
        {
            SetStatus("Төсөл үүлэнд бүртгэгдээгүй тул татах боломжгүй.");
            return;
        }

        CitizenSurveyResponseDocument held;
        try
        {
            held = CitizenSurveyResponseStore.LoadDocument(state.ProjectPath, survey);
        }
        catch (InvalidDataException unreadable)
        {
            SetStatus(unreadable.Message);
            return;
        }

        SetStatus("Хариулт татаж байна…");

        try
        {
            string sentCursor = held.Cursor;
            StudioCitizenSurveyFetch fetched =
                await account.FetchCitizenSurveyResponsesAsync(
                    projectId, survey.Id, sentCursor);
            CitizenSurveyResponseDocument arriving = fetched.Document;

            int added = CitizenSurveyResponseStore.Merge(
                held, arriving.Responses, arriving.Cursor);
            CitizenSurveyResponseStore.Save(state.ProjectPath, survey, held);

            // 🔴 A REFUSED CURSOR IS SAID OUT LOUD. The server returns everything when it
            // cannot read the watermark - correct, and indistinguishable from an ordinary
            // first read. If the format ever drifts, every collection silently re-downloads
            // the whole consultation: still working, still green, only slower each time.
            // Studio knows whether it SENT one, so only Studio can tell the two apart.
            bool cursorRefused = !string.IsNullOrWhiteSpace(sentCursor) && !fetched.SinceAccepted;

            SetStatus(
                $"{added} шинэ хариулт татагдлаа — нийт {held.Responses.Count}. " +
                (arriving.Responses.Count > added
                    ? $"({arriving.Responses.Count - added} нь аль хэдийн байсан.) "
                    : "") +
                (cursorRefused
                    ? "⚠ Сервер өмнөх тэмдэгийг таниагүй тул БҮГДИЙГ дахин татсан. " +
                      "Нэг удаа бол хэвийн; давтагдвал хэлээрэй."
                    : ""));
            RefreshSurveyDetail();
        }
        catch (StudioAccountException failure)
        {
            // 🔴 THE SERVER'S OWN SENTENCE, NOT A SUMMARY OF IT. On a conflict it names
            // the two definition files that both claim this survey - and that message is
            // the only thing that tells somebody which file to remove.
            SetStatus($"Татаж чадсангүй: {failure.Message}");
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidDataException)
        {
            SetStatus($"Татаж чадсангүй: {exception.Message}");
        }
    }

    /// <summary>
    /// Sets a trial run's answers aside so they do not become part of the result.
    ///
    /// 🔴 THE OWNER ASKED FOR THIS AND THE NEED IS REAL - a rehearsal's answers must
    /// not be counted among the public's. The danger is that the same button is still here
    /// when the answers ARE the public's, and what somebody submitted is the one thing in
    /// this feature that cannot be recomputed. Three things follow from that:
    ///
    ///   it names the COUNT and the SURVEY before doing anything, so the confirmation is
    ///     about this survey rather than about the word «clear»;
    ///   it MOVES the file rather than deleting it, so a mis-click costs a rename;
    ///   it says out loud that the SERVER still has its own copy, because a clear that
    ///     looked total but was not would be worse than no clear at all.
    /// </summary>
    private void ClearSurveyAnswers()
    {
        ProjectCitizenSurvey? survey = state.Project.CitizenSurveys.Find(selectedSurveyId);
        if (survey is null)
        {
            SetStatus("Санал асуулга сонгоно уу.");
            return;
        }

        int held;
        try
        {
            held = CitizenSurveyResponseStore.LoadDocument(state.ProjectPath, survey)
                .Responses.Count;
        }
        catch (InvalidDataException unreadable)
        {
            SetStatus(unreadable.Message);
            return;
        }

        if (held == 0)
        {
            SetStatus("Цэвэрлэх хариулт алга.");
            return;
        }

        // ⚠ THE COUNT IS IN THE QUESTION. «Are you sure?» is answered yes by reflex;
        // «19 answers from this survey» is read.
        if (MessageBox.Show(
                $"«{Blank(survey.Title, "Нэргүй асуулга")}» асуулгын {held} хариултыг " +
                "цэвэрлэх үү?\n\n" +
                "Файл устахгүй — хажууд нь хуулбар үлдэнэ. Сервер өөрийн " +
                "хуулбараа хадгалсаар байна.",
                "Хариулт цэвэрлэх",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        try
        {
            CitizenSurveyResponseStore.CitizenSurveyClearOutcome cleared =
                CitizenSurveyResponseStore.Clear(
                    state.ProjectPath, survey, DateTimeOffset.UtcNow);

            SetStatus(
                $"{cleared.Removed} хариулт цэвэрлэгдлээ. Хуулбар: " +
                $"{Path.GetFileName(cleared.ArchivePath)}. Сервер дээр хэвээр байгаа тул " +
                "дахин оруулбал буцаж ирнэ.");
            RefreshSurveyDetail();
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            SetStatus($"Цэвэрлэж чадсангүй: {exception.Message}");
        }
    }

    /// <summary>
    /// Reads filled-in files back in and merges them onto the survey.
    ///
    /// 🔴 A FILE FOR ANOTHER SURVEY IS REFUSED BY NAME, NOT COUNTED. Two rounds in one
    /// project ask different questions; merging one round's answers into the other would
    /// produce a result that is arithmetically perfect and about nothing. The answer ids
    /// also mean the same file read twice adds nobody.
    /// </summary>
    private void ImportSurveyAnswers()
    {
        ProjectCitizenSurvey? survey = state.Project.CitizenSurveys.Find(selectedSurveyId);
        if (survey is null)
        {
            SetStatus("Санал асуулга сонгоно уу.");
            return;
        }

        var dialog = new OpenFileDialog
        {
            Title = "Бөглөсөн хариултуудыг сонгох",
            Filter =
                "Санал асуулгын хариулт (*" + CitizenSurveyFormHtml.AnswerFileExtension +
                ")|*" + CitizenSurveyFormHtml.AnswerFileExtension + "|JSON (*.json)|*.json",
            Multiselect = true,
            CheckFileExists = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        CitizenSurveyResponseDocument held;
        try
        {
            held = CitizenSurveyResponseStore.LoadDocument(state.ProjectPath, survey);
        }
        catch (InvalidDataException unreadable)
        {
            SetStatus(unreadable.Message);
            return;
        }

        var added = 0;
        var foreign = 0;
        var unreadableFiles = new List<string>();

        foreach (string path in dialog.FileNames)
        {
            try
            {
                CitizenSurveyResponseDocument? arriving =
                    JsonSerializer.Deserialize<CitizenSurveyResponseDocument>(
                        File.ReadAllText(path, Encoding.UTF8));
                if (arriving is null)
                {
                    unreadableFiles.Add(Path.GetFileName(path));
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(arriving.SurveyId) &&
                    !arriving.SurveyId.Equals(survey.Id, StringComparison.OrdinalIgnoreCase))
                {
                    foreign++;
                    continue;
                }

                added += CitizenSurveyResponseStore.Merge(held, arriving.Responses, null);
            }
            catch (Exception exception) when (
                exception is JsonException or IOException or UnauthorizedAccessException)
            {
                unreadableFiles.Add(Path.GetFileName(path));
            }
        }

        if (added > 0)
            CitizenSurveyResponseStore.Save(state.ProjectPath, survey, held);

        // ⚠ EVERY OUTCOME NAMED, INCLUDING THE ZEROES. «0 нэмэгдлээ» from re-reading files
        // already merged and «0» from files nothing could read are different facts, and
        // a single silent number would let the second pass for the first.
        var said = new List<string> { $"{added} шинэ хариулт нэмэгдлээ" };
        if (foreign > 0)
            said.Add($"{foreign} файл ӨӨР асуулгынх тул АВААГҮЙ");
        if (unreadableFiles.Count > 0)
            said.Add($"уншигдсангүй: {string.Join(", ", unreadableFiles)}");
        said.Add($"нийт {held.Responses.Count}");

        SetStatus(string.Join(" — ", said));
        RefreshSurveyDetail();
    }

    private UIElement SurveyHeading(ProjectCitizenSurvey survey)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(new TextBlock
        {
            Text = Blank(survey.Title, "Нэргүй асуулга"),
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(Muted(Blank(survey.Purpose, "Зорилго бичигдээгүй.")));

        // 🔴 CHECKED, NOT PRINTED ON TRUST. The address shown here is the one that goes
        // on the QR, and a QR cannot be recalled once it is on a notice board - so a pair
        // that does not agree is named as a fault instead of being displayed as a link.
        CitizenSurveyLinkCheck link =
            CitizenSurveyPublicLink.Check(survey.PublicCode, survey.PublicFormUrl);
        if (link.IsUsable)
        {
            panel.Children.Add(Muted($"Маягт: {survey.PublicFormUrl}"));

            // 🔴 SAID EVERY TIME THE LINK IS SHOWN, not once when it was entered. This is
            // the sentence that stops a stand-in QR going onto a printed notice.
            if (survey.IsManualLink)
            {
                panel.Children.Add(Warning(
                    "⚠ ГАРААР БҮРТГЭСЭН хаяг — Студио өөрөө нийтлээгүй. Ажиллаж " +
                    "байгааг нь утснаас нэг удаа шалгана уу. Нийтлэх үед код өөрчлөгдвөл QR-ыг дахин үүсгэнэ."));
            }

            panel.Children.Add(SurveyQrBlock(survey));
        }
        else
        {
            panel.Children.Add(Warning("⚠ " + link.Refusal));
        }

        panel.Children.Add(OpenStateRow(survey));

        if (!link.IsUsable)
            panel.Children.Add(IssueCodeRow(survey));

        panel.Children.Add(ManualLinkRow(survey));
        return panel;
    }

    /// <summary>
    /// The headline numbers.
    ///
    /// ⚠ A HERO FIGURE, NOT A ONE-BAR CHART. The participant count is a single value, and
    /// a single value drawn as a bar has nothing to compare against - it is a number
    /// wearing a chart's clothes.
    /// </summary>
    /// <summary>
    /// Whether the survey is accepting answers, and the one control that changes it.
    ///
    /// 🔴 THE FIELD EXISTED, THE SERVER ENFORCED IT, AND NOTHING COULD SET IT. A survey
    /// is created closed; the public route answers 410 Gone to every submission while it
    /// is. So the page would load on a citizen's phone, they would fill in sixteen
    /// questions, press send - and lose all of it, with a message inviting them to try
    /// again, which would fail the same way forever. Nobody would learn anything: the
    /// owner sees no answers, the citizen sees a failure they cannot fix.
    ///
    /// Found by reading the owner's live project before they published it, not by a test.
    /// The third instance today of the same shape: a rule written, enforced, and wired to
    /// nothing that can satisfy it.
    ///
    /// ⚠ AND THE SERVER KEEPS ITS OWN COPY. Opening a survey here changes the project;
    /// the public route reads the exported definition file. Until that file is exported
    /// again the server still believes the survey is closed - so the row says so rather
    /// than leaving somebody to discover it through a citizen's failed submission.
    /// </summary>
    private UIElement OpenStateRow(ProjectCitizenSurvey survey)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

        if (survey.IsOpen)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "● НЭЭЛТТЭЙ — бөглөсөн хариултыг хүлээж авна.",
                FontWeight = FontWeights.SemiBold,
                Foreground = StudioTheme.AccentBrush,
                Margin = new Thickness(0, 0, 0, 4),
            });
        }
        else
        {
            panel.Children.Add(Warning(
                "⚠ ХААЛТТАЙ — иргэн маягтыг нээж бөглөнө, гэхдээ илгээх үед " +
                "СЕРВЕР ТАТГАЛЗАНА. Тараахаас өмнө нээнэ үү."));
        }

        var button = new Button
        {
            Content = survey.IsOpen
                ? "Асуулгыг хаах"
                : "Асуулгыг нээх",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) =>
        {
            survey.IsOpen = !survey.IsOpen;
            state.SaveProject();
            SetStatus(survey.IsOpen
                ? "Асуулга НЭЭГДЛЭЭ. Сервер өөрийн хуулбараас уншдаг тул " +
                  "«Асуулгын JSON»-ыг ДАХИН гаргаж серверт тавь."
                : "Асуулга хаагдлаа. Ирсэн хариулт устахгүй.");
            RefreshSurveyDetail();
        };
        panel.Children.Add(button);
        return panel;
    }

    /// <summary>
    /// Mints this survey's public address so the QR can be printed today.
    ///
    /// 🔴 THE CODE IS OURS BY AGREEMENT, WHICH IS WHY THIS BUTTON CAN EXIST. SRV, on
    /// reviewing the contract: «кодыг та үүсгэ, сервер түүнийг хадгалахаас өөр юу ч
    /// хийхгүй». So the address does not change when the route is finally deployed,
    /// and a poster printed now keeps working.
    ///
    /// ⚠ AND IT SAYS WHAT IS STILL MISSING. The QR is final; the page it opens is not
    /// live until somebody deploys. Printing before then is a decision, not an accident,
    /// so the sentence is on screen rather than in a document.
    /// </summary>
    private UIElement IssueCodeRow(ProjectCitizenSurvey survey)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        var button = new Button
        {
            Content = "Код үүсгэж QR гаргах",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        button.Click += (_, _) =>
        {
            if (!survey.IssuePublicCode(StudioSurveyBaseUrl))
            {
                SetStatus("Код үүсгэгдсэнгүй.");
                return;
            }

            state.SaveProject();
            SetStatus(
                $"Код үүслээ: {survey.PublicFormUrl} — QR ҮНДСЭН, дахин өөрчлөгдөхгүй. " +
                "Хуудас нь серверт тавигдсны дараа амьд болно.");
            RefreshSurveyDetail();
        };
        panel.Children.Add(button);
        return panel;
    }

    /// <summary>
    /// Lets the owner paste in the address the form was stood up at.
    ///
    /// 🔴 BECAUSE THE PEOPLE ANSWERING HAVE PHONES, NOT STUDIO. The owner's own words:
    /// «судалгаан хамрагдагсадад студио байхгүй шүү. жирийн иргэд. вэб браузер
    /// ашиглах байх … гар утаснаасаа.» A phone needs a URL, and until the publish route
    /// exists the URL comes from whoever stood the page up. Waiting for the route would
    /// mean no trial at all; inventing an address would mean a QR that leads nowhere. So
    /// the owner pastes the real one and Studio says plainly that it did not issue it.
    /// </summary>
    private UIElement ManualLinkRow(ProjectCitizenSurvey survey)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(Muted(
            "Маягт байрлах хаяг (жишээ https://erk-s.mn/s/xxxxx) — буулгаад бүртгэхэд QR гарна:"));

        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var box = new TextBox
        {
            Text = survey.PublicFormUrl,
            Width = 380,
            Margin = new Thickness(0, 0, 8, 0),
        };
        var button = new Button { Content = "Хаяг бүртгэх" };
        button.Click += (_, _) =>
        {
            if (!survey.AcceptManualLink(box.Text))
            {
                SetStatus(
                    "Хаяг бүртгэгдсэнгүй — https://.../s/<код> хэлбэртэй байх ёстой.");
                return;
            }

            state.SaveProject();
            SetStatus($"Хаяг бүртгэгдлээ: {survey.PublicFormUrl} — QR бэлэн.");
            RefreshSurveyDetail();
        };

        // The button belongs beside its box, not at the far edge of the window.
        row.LastChildFill = false;
        DockPanel.SetDock(box, Dock.Left);
        DockPanel.SetDock(button, Dock.Left);
        row.Children.Add(box);
        row.Children.Add(button);
        panel.Children.Add(row);
        return panel;
    }

    /// <summary>
    /// The QR for a published survey, drawn from the link that was checked.
    ///
    /// ⚠ IT APPEARS ONLY WHEN THE LINK PASSED. A square drawn from an unchecked address
    /// scans perfectly and leads nowhere, and it is printed - so the page shows the fault
    /// instead, and there is nothing here to photograph by mistake.
    /// </summary>
    private UIElement SurveyQrBlock(ProjectCitizenSurvey survey)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };

        CitizenSurveyQrImage qr = CitizenSurveyQrCode.For(survey.PublicCode, survey.PublicFormUrl);
        if (!qr.IsDrawn)
        {
            panel.Children.Add(Warning("⚠ " + qr.Refusal));
            return panel;
        }

        var source = new BitmapImage();
        source.BeginInit();
        source.StreamSource = new MemoryStream(qr.Png);
        source.CacheOption = BitmapCacheOption.OnLoad;
        source.EndInit();
        source.Freeze();

        panel.Children.Add(new Image
        {
            Source = source,
            Width = 180,
            Height = 180,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(0, 0, 0, 6),
        });

        var save = new Button
        {
            Content = "QR-ыг зургаар хадгалах",
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        save.Click += (_, _) => SaveSurveyQr(survey, qr);
        panel.Children.Add(save);
        return panel;
    }

    private void SaveSurveyQr(ProjectCitizenSurvey survey, CitizenSurveyQrImage qr)
    {
        var dialog = new SaveFileDialog
        {
            Title = "QR хадгалах",
            Filter = "PNG зураг (*.png)|*.png",
            FileName = $"qr-{survey.PublicCode}.png",
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            File.WriteAllBytes(dialog.FileName, qr.Png);
            SetStatus($"QR хадгалагдлаа: {dialog.FileName}");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            SetStatus($"QR бичигдсэнгүй: {exception.Message}");
        }
    }

    private UIElement SurveyKpiRow(ProjectCitizenSurvey survey, CitizenSurveyResult result)
    {
        var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 16) };
        row.Children.Add(StatTile(
            result.ResponseCount.ToString("N0"), "оролцсон хүн", hero: true));
        row.Children.Add(StatTile(survey.Questions.Count.ToString("N0"), "асуулт"));
        row.Children.Add(StatTile(
            result.Questions.Count(q => q.Answered > 0).ToString("N0"), "хариулт авсан асуулт"));
        row.Children.Add(StatTile(
            result.FirstSubmittedAtUtc is { } first ? first.ToLocalTime().ToString("MM-dd HH:mm") : "—",
            "эхний хариулт"));
        row.Children.Add(StatTile(
            result.LastSubmittedAtUtc is { } last ? last.ToLocalTime().ToString("MM-dd HH:mm") : "—",
            "сүүлийн хариулт"));
        return row;
    }

    private UIElement StatTile(string value, string caption, bool hero = false)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 12, 12) };
        panel.Children.Add(new TextBlock
        {
            Text = value,
            FontSize = hero ? 44 : 22,
            FontWeight = FontWeights.SemiBold,
            // Text wears text tokens - the accent belongs to the marks, not the numbers.
            Foreground = StudioTheme.TextBrush,
        });
        panel.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 11.5,
            Foreground = StudioTheme.MutedTextBrush,
        });
        return new Border
        {
            Background = StudioTheme.PanelAltBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 0, 10, 10),
            MinWidth = hero ? 170 : 130,
            Child = panel,
        };
    }

    private UIElement FindingCard(CitizenSurveyFinding finding)
    {
        Brush edge = finding.Weight switch
        {
            CitizenSurveyFindingWeights.Strong => StudioTheme.AccentBrush,
            CitizenSurveyFindingWeights.Attention => StudioTheme.WarningBrush,
            CitizenSurveyFindingWeights.TooFew => StudioTheme.BorderHoverBrush,
            _ => StudioTheme.BorderBrush,
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = finding.Headline,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = finding.QuestionText,
            FontSize = 11.5,
            Foreground = StudioTheme.FaintTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 4),
        });
        // 🔴 THE EVIDENCE IS NEVER OPTIONAL. A percentage with no base is an opinion
        // wearing a number, and this text goes into a planning document.
        panel.Children.Add(new TextBlock
        {
            Text = finding.Evidence,
            FontSize = 12,
            Foreground = StudioTheme.AccentSoftBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = finding.Consideration,
            FontSize = 12,
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });

        return new Border
        {
            Background = StudioTheme.PanelBrush,
            BorderBrush = edge,
            BorderThickness = new Thickness(3, 0, 0, 0),
            CornerRadius = new CornerRadius(0, 6, 6, 0),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8),
            MaxWidth = 760,

            // ⚠ LEFT, NOT CENTRED. A MaxWidth inside a stretching StackPanel leaves
            // WPF free to centre the child, and on a wide window these drifted into
            // the middle of the page while their own section headings stayed at the
            // margin - reading as two unrelated columns.
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = panel,
        };
    }

    private UIElement QuestionBlock(CitizenSurveyQuestionTally question)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 18),
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.Children.Add(new TextBlock
        {
            Text = question.Text,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
        });

        string counts = $"{question.Answered} хариулсан · {question.Skipped} алгассан";
        if (question.Kind == CitizenSurveyQuestionKinds.MultipleChoice)
        {
            // ⚠ SAID, NOT HIDDEN. Several ticks per person means the shares add past a
            // whole, which is correct and looks like an error unless it is explained.
            counts += " · олон сонголттой тул хувь нийлбэр 100%-аас давж болно";
        }
        panel.Children.Add(new TextBlock
        {
            Text = counts,
            FontSize = 11.5,
            Foreground = StudioTheme.FaintTextBrush,
            Margin = new Thickness(0, 2, 0, 8),
        });

        if (question.Kind == CitizenSurveyQuestionKinds.FreeText)
        {
            panel.Children.Add(WrittenAnswers(question.OwnWords));
            return panel;
        }

        if (question.Kind == CitizenSurveyQuestionKinds.Number)
        {
            panel.Children.Add(NumberSummary(question));
            return panel;
        }

        foreach (CitizenSurveyBarMark bar in CitizenSurveyChartLayout.Bars(
                     question, SurveyTrackWidth, option => MeasureValueWidth(option)))
        {
            panel.Children.Add(BarRow(bar));
        }

        if (question.OwnWords.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"«Бусад» дээр бичсэн {question.OwnWords.Count} хариулт:",
                FontSize = 11.5,
                Foreground = StudioTheme.MutedTextBrush,
                Margin = new Thickness(0, 8, 0, 4),
            });
            panel.Children.Add(WrittenAnswers(question.OwnWords));
        }

        return panel;
    }

    /// <summary>
    /// One bar. Single hue, capped thickness, rounded at the data end, value at the tip.
    /// </summary>
    private UIElement BarRow(CitizenSurveyBarMark bar)
    {
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };

        var label = new TextBlock
        {
            Text = bar.Label,
            Width = 240,
            FontSize = 12,
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 10, 0),
        };
        DockPanel.SetDock(label, Dock.Left);
        row.Children.Add(label);

        var value = new TextBlock
        {
            Text = $"{bar.Count}  ({bar.Share * 100:0.#}%)",
            FontSize = 12,
            // Text never wears the data colour.
            Foreground = StudioTheme.TextBrush,
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        DockPanel.SetDock(value, Dock.Right);
        row.Children.Add(value);

        var track = new Grid { Width = SurveyTrackWidth, Height = 24 };
        track.Children.Add(new Border
        {
            Background = StudioTheme.PanelAltBrush,
            CornerRadius = new CornerRadius(2),
            Height = 6,
            VerticalAlignment = VerticalAlignment.Center,
        });
        track.Children.Add(new Border
        {
            Background = StudioTheme.AccentBrush,
            // Rounded at the data end, square at the baseline.
            CornerRadius = new CornerRadius(
                0, CitizenSurveyChartLayout.BarEndCornerRadiusPx,
                CitizenSurveyChartLayout.BarEndCornerRadiusPx, 0),
            Width = Math.Max(bar.Count > 0 ? 2 : 0, bar.LengthPx),
            Height = CitizenSurveyChartLayout.BarThickness(24),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(track);
        return row;
    }

    private UIElement NumberSummary(CitizenSurveyQuestionTally question)
    {
        var row = new WrapPanel();
        row.Children.Add(StatTile(
            question.NumberAverage is { } average ? average.ToString("0.0") : "—", "дундаж"));
        row.Children.Add(StatTile(
            question.NumberSmallest is { } small ? small.ToString("0.#") : "—", "хамгийн бага"));
        row.Children.Add(StatTile(
            question.NumberLargest is { } large ? large.ToString("0.#") : "—", "хамгийн их"));
        return row;
    }

    private UIElement WrittenAnswers(IReadOnlyList<string> words)
    {
        if (words.Count == 0)
            return Muted("Бичмэл хариулт алга.");

        var panel = new StackPanel();
        foreach (string word in words)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "• " + word,
                FontSize = 12,
                Foreground = StudioTheme.TextBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 3),
            });
        }

        return new Border
        {
            Background = StudioTheme.PanelBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 10, 12, 10),
            MaxWidth = 760,

            // ⚠ LEFT, NOT CENTRED. A MaxWidth inside a stretching StackPanel leaves
            // WPF free to centre the child, and on a wide window these drifted into
            // the middle of the page while their own section headings stayed at the
            // margin - reading as two unrelated columns.
            HorizontalAlignment = HorizontalAlignment.Left,
            Child = panel,
        };
    }

    private UIElement SplitPicker(
        ProjectCitizenSurvey survey,
        IReadOnlyList<CitizenSurveyResponse> responses)
    {
        var panel = new StackPanel();
        IReadOnlyList<CitizenSurveyQuestion> splitters =
            CitizenSurveyCrossTab.SplittingQuestions(survey);
        if (splitters.Count == 0)
            return Muted("Ангилахад тохирох нэг сонголттой асуулт алга.");

        // 🔴 SELECT FIRST, SUBSCRIBE AFTERWARDS - THE ORDER IS THE WHOLE RULE. This method
        // runs inside RefreshSurveyDetail and builds a NEW ComboBox each time. Assigning
        // SelectedValue on a fresh box always raises SelectionChanged, so subscribing
        // before the assignment made the handler call RefreshSurveyDetail, which built
        // another box, which raised again - unbounded recursion ending in a
        // StackOverflowException, which .NET cannot catch: the whole window vanishes with
        // no error at all. That is what the owner saw when opening this page.
        //
        // ⚠ AND IT WAS HIDDEN BEHIND A DISCONNECTED WIRE. Nothing called this page's
        // refresh until 2d49027, so the page was merely blank rather than fatal; fixing
        // the missing call is what exposed it. A dead path hides its own defects.
        surveySplitBox = new ComboBox
        {
            Width = 420,
            ItemsSource = splitters.Select(q => new SurveyRow(q.Id, q.Text)).ToList(),
            DisplayMemberPath = "Label",
            SelectedValuePath = "Id",
            Margin = new Thickness(0, 0, 0, 10),
        };

        if (string.IsNullOrEmpty(selectedSplitQuestionId) ||
            splitters.All(question => question.Id != selectedSplitQuestionId))
        {
            selectedSplitQuestionId = splitters[0].Id;
        }

        surveySplitBox.SelectedValue = selectedSplitQuestionId;
        surveySplitBox.SelectionChanged += (_, _) =>
        {
            selectedSplitQuestionId = (surveySplitBox.SelectedValue as string) ?? "";
            RefreshSurveyDetail();
        };
        panel.Children.Add(surveySplitBox);

        foreach (CitizenSurveyQuestion question in survey.OrderedQuestions())
        {
            if (question.Id == selectedSplitQuestionId ||
                !CitizenSurveyQuestionKinds.TakesOptions(question.Kind))
            {
                continue;
            }

            CitizenSurveyCrossTabResult? tab = CitizenSurveyCrossTab.Of(
                survey, responses, selectedSplitQuestionId, question.Id);
            if (tab is null)
                continue;

            panel.Children.Add(CrossTabBlock(tab));
        }

        return panel;
    }

    /// <summary>
    /// A cross-tab as SMALL MULTIPLES - one single-hue chart per slice.
    ///
    /// 🔴 NOT A STACKED BAR, AND THAT IS A DELIBERATE CHOICE. A stack would need a
    /// categorical palette, and the palette validator could not be run on this machine -
    /// shipping an unchecked set of hues into a chart a council reads is exactly the kind
    /// of thing that looks fine to me and is unreadable to a colour-blind reader. Small
    /// multiples carry the same comparison with one hue and no legend at all, and the
    /// long Mongolian option names read better stacked vertically anyway.
    /// </summary>
    private UIElement CrossTabBlock(CitizenSurveyCrossTabResult tab)
    {
        var panel = new StackPanel
        {
            Margin = new Thickness(0, 0, 0, 18),
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        panel.Children.Add(new TextBlock
        {
            Text = tab.OfQuestionText,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = $"«{tab.ByQuestionText}»-ээр ангилсан",
            FontSize = 11.5,
            Foreground = StudioTheme.FaintTextBrush,
            Margin = new Thickness(0, 2, 0, 8),
        });

        foreach (CitizenSurveySegmentTally segment in tab.Segments)
        {
            panel.Children.Add(new TextBlock
            {
                Text = $"{segment.SegmentLabel} — {segment.Respondents} хүн",
                FontSize = 12,
                Foreground = StudioTheme.AccentSoftBrush,
                Margin = new Thickness(0, 6, 0, 4),
            });

            if (segment.Tally.Answered == 0)
            {
                panel.Children.Add(Muted("   хариулт алга"));
                continue;
            }

            foreach (CitizenSurveyBarMark bar in CitizenSurveyChartLayout.Bars(
                         segment.Tally, SurveyTrackWidth, option => MeasureValueWidth(option)))
            {
                panel.Children.Add(BarRow(bar));
            }
        }

        if (tab.UnsegmentedRespondents > 0)
        {
            // Columns that add to less than the total invite a wrong subtraction.
            panel.Children.Add(Muted(
                $"⚠ {tab.UnsegmentedRespondents} хүн энэ асуултад хариулсан ч " +
                $"«{tab.ByQuestionText}»-ыг хоосон орхисон тул дээрх ангилалд ороогүй."));
        }

        return panel;
    }

    /// <summary>
    /// How wide the value text will be drawn.
    ///
    /// ⚠ MEASURED HERE BECAUSE ONLY THE WINDOW KNOWS THE FONT. A rule in Core that guessed
    /// would clip «128 (100%)» on somebody else's machine, and a clipped value is worse
    /// than one moved outside the bar.
    /// </summary>
    private static double MeasureValueWidth(CitizenSurveyOptionTally option)
    {
        string text = $"{option.Count}  ({option.Share * 100:0.#}%)";
        var block = new TextBlock { Text = text, FontSize = 12 };
        block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return block.DesiredSize.Width;
    }

    private IReadOnlyList<CitizenSurveyResponse> LoadSurveyResponses(ProjectCitizenSurvey survey) =>
        CitizenSurveyResponseStore.Load(state.ProjectPath, survey);

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6),
    };

    private static TextBlock Warning(string text) => new()
    {
        Text = text,
        FontSize = 12,
        Foreground = StudioTheme.WarningBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
    };

    private static TextBlock SectionTitle(string text) => new()
    {
        Text = text,
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Foreground = StudioTheme.TextBrush,
        Margin = new Thickness(0, 16, 0, 10),
    };

    private static string Blank(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
