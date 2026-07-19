using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private readonly StackPanel approvedByRowsPanel = new();
    private readonly StackPanel endorsedByRowsPanel = new();
    private readonly List<ApprovalEditorRow> approvedByEditorRows = [];
    private readonly List<ApprovalEditorRow> endorsedByEditorRows = [];
    private readonly Button addApprovedByButton = StudioWidgets.CreateGlyphTextButton(
        "\uE710",
        "Батлах албан тушаалтан нэмэх",
        "БАТЛАВ хэсэгт мөр нэмэх");
    private readonly Button addEndorsedByButton = StudioWidgets.CreateGlyphTextButton(
        "\uE710",
        "Зөвшөөрөлцөх албан тушаалтан нэмэх",
        "ЗӨВШӨӨРӨЛЦСӨН хэсэгт мөр нэмэх");
    private bool approvalEditorInitialized;

    private UIElement BuildConceptApprovalEditor()
    {
        if (!approvalEditorInitialized)
        {
            addApprovedByButton.Click += (_, _) => AddApprovalRow(ApprovalRosterKind.ApprovedBy);
            addEndorsedByButton.Click += (_, _) => AddApprovalRow(ApprovalRosterKind.EndorsedBy);
            approvalEditorInitialized = true;
        }

        var root = new StackPanel();
        root.Children.Add(StudioWidgets.CreateHint(
            "Энэ жагсаалт нүүр хуудасны бичиглэлд хэрэглэгдэнэ. Cloud ERA төслийн багийн эрх, гишүүнчлэлээс тусдаа мэдээлэл."));

        root.Children.Add(StudioWidgets.CreateSectionHeader("БАТЛАВ"));
        root.Children.Add(StudioWidgets.CreateHint(
            "Ерөнхий архитекторуудыг нүүр хуудсанд гарах дарааллаар оруулна. Нэгээс гурав хүртэл мөртэй байж болно."));
        root.Children.Add(BuildApprovalColumnHeader(ApprovalRosterKind.ApprovedBy));
        root.Children.Add(approvedByRowsPanel);
        addApprovedByButton.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(addApprovedByButton);

        root.Children.Add(StudioWidgets.CreateSectionHeader("ЗӨВШӨӨРӨЛЦСӨН"));
        root.Children.Add(StudioWidgets.CreateHint(
            "Загвар зургийг зөвшөөрөлцөх байгууллага, албан тушаалтанг нүүр хуудсанд гарах дарааллаар оруулна. Хоёроос зургаа хүртэл мөртэй байна."));
        root.Children.Add(StudioWidgets.CreateHint(
            "Нүүр талын ХЯНАВ хэсэгт орох албан тушаалтнуудыг энэ жагсаалтаас сонгоно. Бүх зөвшөөрөлцсөн тал автоматаар орохгүй."));
        root.Children.Add(BuildApprovalColumnHeader(ApprovalRosterKind.EndorsedBy));
        root.Children.Add(endorsedByRowsPanel);
        addEndorsedByButton.HorizontalAlignment = HorizontalAlignment.Left;
        root.Children.Add(addEndorsedByButton);

        root.Children.Add(StudioWidgets.CreateSectionHeader("ЗӨВШИЛЦСӨН"));
        root.Children.Add(StudioWidgets.CreateHint(
            "ЗӨВШИЛЦСӨН нь ажлын зургийн шатны тусдаа roster. Загвар зургийн нүүр хуудсанд орохгүй бөгөөд ажлын зургийн бүтэц нээгдэхэд эндээс тусдаа тохируулна."));

        if (state.HasOpenProject)
            BindConceptApprovalEditor();
        return root;
    }

    private static Grid BuildApprovalColumnHeader(ApprovalRosterKind kind)
    {
        Grid grid = CreateApprovalGrid();
        grid.Margin = new Thickness(0, 4, 0, 4);
        AddHeader("№", 0, TextAlignment.Center);
        AddHeader("Байгууллага", 1);
        AddHeader("Албан тушаал", 2);
        AddHeader("Нэр", 3);
        AddHeader(kind == ApprovalRosterKind.ApprovedBy ? "Нүүр тал" : "Нүүр талын ХЯНАВ", 4, TextAlignment.Center);

        void AddHeader(string text, int column, TextAlignment alignment = TextAlignment.Left)
        {
            var label = new TextBlock
            {
                Text = text,
                Foreground = StudioTheme.MutedTextBrush,
                FontSize = StudioTheme.HintFontSize,
                TextAlignment = alignment,
                Margin = new Thickness(6, 0, 6, 0),
            };
            Grid.SetColumn(label, column);
            grid.Children.Add(label);
        }

        return grid;
    }

    private void BindConceptApprovalEditor()
    {
        if (!approvalEditorInitialized || !state.HasOpenProject)
            return;

        ConceptCoverApprovalSnapshot snapshot = ConceptCoverApprovalResolver.Resolve(
            state.Project.Foundation.ApprovalWorkflow,
            state.Project.Foundation.PlanningTask);
        ReplaceApprovalRows(ApprovalRosterKind.ApprovedBy, snapshot.ApprovedBy);
        ReplaceApprovalRows(ApprovalRosterKind.EndorsedBy, snapshot.EndorsedBy);
        RefreshConceptApprovalEditorUi();
    }

    private ConceptDesignApprovalRoster CaptureConceptDesignApprovalDraft() => new()
    {
        IsConfigured = true,
        ApprovedBy = ReadApprovalEntries(approvedByEditorRows),
        EndorsedBy = ReadApprovalEntries(endorsedByEditorRows),
    };

    private static bool ConceptApprovalDiffers(
        ProjectApprovalWorkflow workflow,
        ConceptDesignApprovalRoster draft)
    {
        ConceptDesignApprovalRoster current = workflow.ConceptDesign;
        return current.IsConfigured != draft.IsConfigured ||
            EntriesDiffer(current.ApprovedBy, draft.ApprovedBy) ||
            EntriesDiffer(current.EndorsedBy, draft.EndorsedBy);
    }

    private static bool EntriesDiffer(
        IReadOnlyList<ProjectApprovalEntry> current,
        IReadOnlyList<ProjectApprovalEntry> draft)
    {
        if (current.Count != draft.Count)
            return true;
        for (var index = 0; index < current.Count; index++)
        {
            if (!string.Equals(current[index].OrganizationName, draft[index].OrganizationName, StringComparison.Ordinal) ||
                !string.Equals(current[index].PositionTitle, draft[index].PositionTitle, StringComparison.Ordinal) ||
                !string.Equals(current[index].PersonName, draft[index].PersonName, StringComparison.Ordinal) ||
                current[index].IncludeInElevationHeader != draft[index].IncludeInElevationHeader)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyConceptApprovalDraft(ConceptDesignApprovalRoster draft)
    {
        state.Project.Foundation.ApprovalWorkflow ??= new ProjectApprovalWorkflow();
        state.Project.Foundation.ApprovalWorkflow.ConceptDesign = draft.Clone();
    }

    private void AddApprovalRow(ApprovalRosterKind kind)
    {
        List<ApprovalEditorRow> rows = RowsFor(kind);
        int maximum = MaximumFor(kind);
        List<ProjectApprovalEntry> entries = ReadApprovalEntries(rows);
        if (entries.Count >= maximum)
        {
            SetStatus($"{RosterLabel(kind)} хэсэг хамгийн ихдээ {maximum} мөртэй байна.");
            return;
        }

        entries.Add(new ProjectApprovalEntry());
        ReplaceApprovalRows(kind, entries);
        RefreshConceptApprovalEditorUi();
        RowsFor(kind)[^1].OrganizationBox.Focus();
    }

    private void RemoveApprovalRow(ApprovalRosterKind kind, ApprovalEditorRow row)
    {
        List<ApprovalEditorRow> rows = RowsFor(kind);
        int minimum = MinimumFor(kind);
        int index = rows.IndexOf(row);
        if (index < 0)
            return;
        if (rows.Count <= minimum)
        {
            SetStatus($"{RosterLabel(kind)} хэсэг хамгийн багадаа {minimum} мөртэй байна.");
            return;
        }

        List<ProjectApprovalEntry> entries = ReadApprovalEntries(rows);
        entries.RemoveAt(index);
        ReplaceApprovalRows(kind, entries);
        RefreshConceptApprovalEditorUi();
    }

    private void MoveApprovalRow(ApprovalRosterKind kind, ApprovalEditorRow row, int direction)
    {
        List<ApprovalEditorRow> rows = RowsFor(kind);
        int index = rows.IndexOf(row);
        int target = index + direction;
        if (index < 0 || target < 0 || target >= rows.Count)
            return;

        List<ProjectApprovalEntry> entries = ReadApprovalEntries(rows);
        (entries[index], entries[target]) = (entries[target], entries[index]);
        ReplaceApprovalRows(kind, entries);
        RefreshConceptApprovalEditorUi();
        RowsFor(kind)[target].OrganizationBox.Focus();
    }

    private void ReplaceApprovalRows(
        ApprovalRosterKind kind,
        IEnumerable<ProjectApprovalEntry> source)
    {
        List<ApprovalEditorRow> rows = RowsFor(kind);
        StackPanel panel = PanelFor(kind);
        rows.Clear();
        panel.Children.Clear();

        foreach (ProjectApprovalEntry sourceEntry in source.Take(MaximumFor(kind)))
        {
            ProjectApprovalEntry entry = sourceEntry.Clone();
            entry.Normalize();
            ApprovalEditorRow row = CreateApprovalEditorRow(kind, entry, rows.Count);
            rows.Add(row);
            panel.Children.Add(row.Root);
        }

        while (rows.Count < MinimumFor(kind))
        {
            var entry = new ProjectApprovalEntry
            {
                PositionTitle = kind == ApprovalRosterKind.ApprovedBy && rows.Count == 0
                    ? "Ерөнхий архитектор"
                    : "",
            };
            ApprovalEditorRow row = CreateApprovalEditorRow(kind, entry, rows.Count);
            rows.Add(row);
            panel.Children.Add(row.Root);
        }
    }

    private ApprovalEditorRow CreateApprovalEditorRow(
        ApprovalRosterKind kind,
        ProjectApprovalEntry entry,
        int index)
    {
        Grid root = CreateApprovalGrid();
        root.Margin = new Thickness(0, 0, 0, StudioTheme.SpaceXs);

        var number = new TextBlock
        {
            Text = (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Foreground = StudioTheme.MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(number, 0);
        root.Children.Add(number);

        var organization = new TextBox { Text = entry.OrganizationName, Margin = new Thickness(3, 0, 3, 0) };
        var position = new TextBox { Text = entry.PositionTitle, Margin = new Thickness(3, 0, 3, 0) };
        var person = new TextBox { Text = entry.PersonName, Margin = new Thickness(3, 0, 3, 0) };
        Grid.SetColumn(organization, 1);
        Grid.SetColumn(position, 2);
        Grid.SetColumn(person, 3);
        root.Children.Add(organization);
        root.Children.Add(position);
        root.Children.Add(person);

        var elevationHeader = new CheckBox
        {
            Content = kind == ApprovalRosterKind.ApprovedBy ? "БАТЛАВ" : "ХЯНАВ",
            IsChecked = kind == ApprovalRosterKind.ApprovedBy || entry.IncludeInElevationHeader,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = kind == ApprovalRosterKind.ApprovedBy
                ? "Нүүр хуудсан дээрх БАТЛАВ албан тушаалтан нүүр талын хуудсанд мөн орно."
                : "Сонговол энэ албан тушаалтан нүүр талын ХЯНАВ хэсэгт орно.",
        };
        Grid.SetColumn(elevationHeader, 4);
        root.Children.Add(elevationHeader);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var up = StudioWidgets.CreateGlyphButton("\uE74A", "Дээш зөөх");
        var down = StudioWidgets.CreateGlyphButton("\uE74B", "Доош зөөх");
        var remove = StudioWidgets.CreateGlyphButton("\uE74D", "Мөр хасах");
        actions.Children.Add(up);
        actions.Children.Add(down);
        actions.Children.Add(remove);
        Grid.SetColumn(actions, 5);
        root.Children.Add(actions);

        var row = new ApprovalEditorRow(
            entry.Id,
            root,
            organization,
            position,
            person,
            elevationHeader,
            kind,
            up,
            down,
            remove);
        up.Click += (_, _) => MoveApprovalRow(kind, row, -1);
        down.Click += (_, _) => MoveApprovalRow(kind, row, 1);
        remove.Click += (_, _) => RemoveApprovalRow(kind, row);
        return row;
    }

    private void RefreshConceptApprovalEditorUi()
    {
        bool editable = state.HasOpenProject && foundationEditMode &&
            !foundationSaveInProgress && CanEditProjectInformation();
        RefreshRows(ApprovalRosterKind.ApprovedBy, editable);
        RefreshRows(ApprovalRosterKind.EndorsedBy, editable);
        addApprovedByButton.IsEnabled = editable &&
            approvedByEditorRows.Count < ProjectApprovalRosterLimits.MaxApprovedBy;
        addEndorsedByButton.IsEnabled = editable &&
            endorsedByEditorRows.Count < ProjectApprovalRosterLimits.MaxEndorsedBy;
    }

    private void RefreshRows(ApprovalRosterKind kind, bool editable)
    {
        List<ApprovalEditorRow> rows = RowsFor(kind);
        for (var index = 0; index < rows.Count; index++)
        {
            ApprovalEditorRow row = rows[index];
            row.OrganizationBox.IsReadOnly = !editable;
            row.PositionBox.IsReadOnly = !editable;
            row.PersonBox.IsReadOnly = !editable;
            row.ElevationHeaderCheck.IsEnabled = editable && kind == ApprovalRosterKind.EndorsedBy;
            row.UpButton.IsEnabled = editable && index > 0;
            row.DownButton.IsEnabled = editable && index < rows.Count - 1;
            row.RemoveButton.IsEnabled = editable && rows.Count > MinimumFor(kind);
        }
    }

    private static List<ProjectApprovalEntry> ReadApprovalEntries(IEnumerable<ApprovalEditorRow> rows) =>
        rows.Select(row => new ProjectApprovalEntry
        {
            Id = row.Id,
            OrganizationName = row.OrganizationBox.Text.Trim(),
            PositionTitle = row.PositionBox.Text.Trim(),
            PersonName = row.PersonBox.Text.Trim(),
            IncludeInElevationHeader = row.Kind == ApprovalRosterKind.EndorsedBy &&
                row.ElevationHeaderCheck.IsChecked == true,
        }).ToList();

    private static Grid CreateApprovalGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 150 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star), MinWidth = 160 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.35, GridUnitType.Star), MinWidth = 130 });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
        return grid;
    }

    private List<ApprovalEditorRow> RowsFor(ApprovalRosterKind kind) =>
        kind == ApprovalRosterKind.ApprovedBy ? approvedByEditorRows : endorsedByEditorRows;

    private StackPanel PanelFor(ApprovalRosterKind kind) =>
        kind == ApprovalRosterKind.ApprovedBy ? approvedByRowsPanel : endorsedByRowsPanel;

    private static int MinimumFor(ApprovalRosterKind kind) =>
        kind == ApprovalRosterKind.ApprovedBy
            ? ProjectApprovalRosterLimits.MinApprovedBy
            : ProjectApprovalRosterLimits.MinEndorsedBy;

    private static int MaximumFor(ApprovalRosterKind kind) =>
        kind == ApprovalRosterKind.ApprovedBy
            ? ProjectApprovalRosterLimits.MaxApprovedBy
            : ProjectApprovalRosterLimits.MaxEndorsedBy;

    private static string RosterLabel(ApprovalRosterKind kind) =>
        kind == ApprovalRosterKind.ApprovedBy ? "БАТЛАВ" : "ЗӨВШӨӨРӨЛЦСӨН";

    private enum ApprovalRosterKind
    {
        ApprovedBy,
        EndorsedBy,
    }

    private sealed record ApprovalEditorRow(
        string Id,
        Grid Root,
        TextBox OrganizationBox,
        TextBox PositionBox,
        TextBox PersonBox,
        CheckBox ElevationHeaderCheck,
        ApprovalRosterKind Kind,
        Button UpButton,
        Button DownButton,
        Button RemoveButton);
}
