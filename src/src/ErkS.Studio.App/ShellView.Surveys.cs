using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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

    /// <summary>The bar track, in pixels. A fixed width keeps every question comparable.</summary>
    private const double SurveyTrackWidth = 320;

    private UIElement BuildSurveysPage()
    {
        var root = new DockPanel { Margin = new Thickness(18) };

        var toolbar = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
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

        var refreshButton = new Button { Content = "Үр дүнг шинэчлэх" };
        refreshButton.Click += (_, _) => RefreshSurveyDetail();
        toolbar.Children.Add(surveyTemplateBox);
        toolbar.Children.Add(addButton);
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
        if (rows.Count > 0 && rows.All(row => row.Id != selectedSurveyId))
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
        if (findings.Count == 0)
        {
            surveyDetailPanel.Children.Add(Muted("Хариулт ирээгүй тул дүгнэлт алга."));
        }
        else
        {
            foreach (CitizenSurveyFinding finding in findings)
                surveyDetailPanel.Children.Add(FindingCard(finding));
        }

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
            panel.Children.Add(Muted($"Маягт: {survey.PublicFormUrl}"));
        else
            panel.Children.Add(Warning("⚠ " + link.Refusal));

        return panel;
    }

    /// <summary>
    /// The headline numbers.
    ///
    /// ⚠ A HERO FIGURE, NOT A ONE-BAR CHART. The participant count is a single value, and
    /// a single value drawn as a bar has nothing to compare against - it is a number
    /// wearing a chart's clothes.
    /// </summary>
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
            Child = panel,
        };
    }

    private UIElement QuestionBlock(CitizenSurveyQuestionTally question)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 18), MaxWidth = 760 };
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

        surveySplitBox = new ComboBox
        {
            Width = 420,
            ItemsSource = splitters.Select(q => new SurveyRow(q.Id, q.Text)).ToList(),
            DisplayMemberPath = "Label",
            SelectedValuePath = "Id",
            Margin = new Thickness(0, 0, 0, 10),
        };
        surveySplitBox.SelectionChanged += (_, _) =>
        {
            selectedSplitQuestionId = (surveySplitBox.SelectedValue as string) ?? "";
            RefreshSurveyDetail();
        };
        if (string.IsNullOrEmpty(selectedSplitQuestionId))
            selectedSplitQuestionId = splitters[0].Id;
        surveySplitBox.SelectedValue = selectedSplitQuestionId;
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
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 18), MaxWidth = 760 };
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
