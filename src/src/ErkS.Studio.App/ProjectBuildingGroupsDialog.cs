using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed class ProjectBuildingGroupsDialog : Window
{
    private readonly AlbumDefinition album;
    private readonly IReadOnlyList<ProjectDesignSource> sources;
    private readonly IReadOnlyList<SheetRecord> records;
    private readonly List<ProjectBuildingGroup> groups;
    private readonly Dictionary<string, string> assignments;
    private readonly ListBox groupList = new();
    private readonly TextBox groupNameBox = new();
    private readonly ComboBox sourceFilter = new();
    private readonly ListView sheetList = new();
    private readonly TextBlock assignmentHint = StudioWidgets.CreateHint("");
    private bool bindingGroups;
    private string selectedGroupId = "";

    public IReadOnlyList<ProjectBuildingGroup> ResultGroups { get; private set; } = [];

    public IReadOnlyDictionary<string, string> ResultAssignments { get; private set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public ProjectBuildingGroupsDialog(
        ProjectWorkspace project,
        AlbumDefinition album,
        IReadOnlyList<SheetRecord> records)
    {
        this.album = album;
        sources = project.Sources;
        this.records = records
            .Where(IsDrawingSheet)
            .OrderBy(ResolveSourceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(ResolveDrawingOrder)
            .ThenBy(record => record.Entry.Number, StringComparer.OrdinalIgnoreCase)
            .ToList();
        groups = ProjectBuildingComposition.NormalizeGroups(project.BuildingGroups);
        assignments = ProjectBuildingComposition.NormalizeAssignments(
            project.SheetBuildingAssignments,
            groups);

        Title = "Барилгын бүлэг ба хуудасны бүрдэл";
        Width = 1080;
        Height = 720;
        MinWidth = 850;
        MinHeight = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        StudioTheme.Apply(this);

        Content = BuildContent();
        RefreshSourceFilter();
        RefreshGroupList(groups.FirstOrDefault()?.Id);
        RefreshSheets();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(16) };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.Click += (_, _) => DialogResult = false;
        Button save = StudioWidgets.CreatePrimaryButton("Хадгалах");
        save.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(StudioWidgets.CreateTitle("Барилгын иж бүрдэл"));
        heading.Children.Add(StudioWidgets.CreateHint(
            "Revit, AutoCAD болон бусад эх үүсвэрийн хуудсыг нэг барилгад онооно. " +
            "Альбумд ерөнхий төлөвлөгөөний дараа барилгын дарааллаар, дотроо " +
            "Байгуулалт → Огтлол → Нүүр тал дарааллаар орно."));
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var workspace = new Grid();
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        workspace.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        workspace.Children.Add(BuildGroupPanel());

        UIElement sheetPanel = BuildSheetPanel();
        Grid.SetColumn(sheetPanel, 2);
        workspace.Children.Add(sheetPanel);
        root.Children.Add(workspace);
        return root;
    }

    private UIElement BuildGroupPanel()
    {
        var panel = new DockPanel();
        TextBlock title = StudioWidgets.CreateSectionHeader("БАРИЛГЫН ДАРААЛАЛ");
        DockPanel.SetDock(title, Dock.Top);
        panel.Children.Add(title);

        var editor = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        editor.Children.Add(StudioWidgets.CreateHint("Сонгосон барилгын нэр"));
        groupNameBox.Margin = new Thickness(0, 4, 0, 8);
        groupNameBox.TextChanged += (_, _) => UpdateSelectedGroupName();
        groupNameBox.LostKeyboardFocus += (_, _) =>
            RefreshGroupList(selectedGroupId);
        groupNameBox.KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Key != Key.Enter)
            {
                return;
            }
            RefreshGroupList(selectedGroupId);
            Keyboard.ClearFocus();
            eventArgs.Handled = true;
        };
        editor.Children.Add(groupNameBox);

        var commands = new UniformGrid { Columns = 4 };
        Button add = StudioWidgets.CreateButton("+");
        add.ToolTip = "Барилга нэмэх";
        add.Click += (_, _) => AddGroup();
        Button up = StudioWidgets.CreateButton("↑");
        up.ToolTip = "Альбумд урагшлуулах";
        up.Click += (_, _) => MoveSelectedGroup(-1);
        Button down = StudioWidgets.CreateButton("↓");
        down.ToolTip = "Альбумд хойшлуулах";
        down.Click += (_, _) => MoveSelectedGroup(1);
        Button remove = StudioWidgets.CreateButton("×");
        remove.ToolTip = "Барилгын бүлэг хасах";
        remove.Click += (_, _) => RemoveSelectedGroup();
        commands.Children.Add(add);
        commands.Children.Add(up);
        commands.Children.Add(down);
        commands.Children.Add(remove);
        editor.Children.Add(commands);
        DockPanel.SetDock(editor, Dock.Bottom);
        panel.Children.Add(editor);

        groupList.BorderThickness = new Thickness(0);
        groupList.SelectionChanged += (_, _) => SelectGroup();
        panel.Children.Add(groupList);
        return panel;
    }

    private UIElement BuildSheetPanel()
    {
        var panel = new DockPanel();
        var controls = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
        controls.Children.Add(StudioWidgets.CreateSectionHeader("ХУУДАСНЫ ОНООЛТ"));

        var filterRow = new Grid();
        filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        filterRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        sourceFilter.SelectionChanged += (_, _) => RefreshSheets();
        filterRow.Children.Add(sourceFilter);
        Button assignVisible = StudioWidgets.CreateButton("Харагдаж буй бүгдийг оноох");
        assignVisible.Margin = new Thickness(8, 0, 0, 0);
        assignVisible.Click += (_, _) => AssignVisibleSheets();
        Grid.SetColumn(assignVisible, 1);
        filterRow.Children.Add(assignVisible);
        controls.Children.Add(filterRow);

        var assignmentRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
        };
        Button assign = StudioWidgets.CreatePrimaryButton("Сонгосныг оноох");
        assign.Click += (_, _) => AssignSelectedSheets();
        Button clear = StudioWidgets.CreateButton("Оноолт салгах");
        clear.Click += (_, _) => ClearSelectedAssignments();
        assignmentRow.Children.Add(assign);
        assignmentRow.Children.Add(clear);
        assignmentRow.Children.Add(assignmentHint);
        controls.Children.Add(assignmentRow);
        DockPanel.SetDock(controls, Dock.Top);
        panel.Children.Add(controls);

        ConfigureSheetList();
        panel.Children.Add(sheetList);
        return panel;
    }

    private void ConfigureSheetList()
    {
        sheetList.SelectionMode = SelectionMode.Extended;
        sheetList.BorderThickness = new Thickness(0);
        sheetList.Background = StudioTheme.InputBrush;
        sheetList.Foreground = StudioTheme.TextBrush;

        var view = new GridView();
        var headerStyle = new Style(typeof(GridViewColumnHeader));
        headerStyle.Setters.Add(new Setter(Control.BackgroundProperty, StudioTheme.PanelAltBrush));
        headerStyle.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.MutedTextBrush));
        headerStyle.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
        headerStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(7, 5, 7, 5)));
        view.ColumnHeaderContainerStyle = headerStyle;
        view.Columns.Add(new GridViewColumn
        {
            Header = "Эх үүсвэр",
            Width = 170,
            DisplayMemberBinding = new Binding(nameof(BuildingSheetRow.Source)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Төрөл",
            Width = 130,
            DisplayMemberBinding = new Binding(nameof(BuildingSheetRow.DrawingKind)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Дугаар",
            Width = 75,
            DisplayMemberBinding = new Binding(nameof(BuildingSheetRow.Number)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Хуудасны нэр",
            Width = 220,
            DisplayMemberBinding = new Binding(nameof(BuildingSheetRow.Name)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Барилга",
            Width = 180,
            DisplayMemberBinding = new Binding(nameof(BuildingSheetRow.Building)),
        });
        sheetList.View = view;
    }

    private void RefreshSourceFilter()
    {
        string selectedId = (sourceFilter.SelectedItem as SourceFilterChoice)?.Id ?? "";
        var choices = new List<SourceFilterChoice>
        {
            new("", "Бүх эх үүсвэр"),
        };
        choices.AddRange(records
            .GroupBy(record => record.SourceId ?? "", StringComparer.OrdinalIgnoreCase)
            .Select(group => new SourceFilterChoice(
                group.Key,
                ResolveSourceName(group.First())))
            .OrderBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase));
        sourceFilter.ItemsSource = choices;
        sourceFilter.SelectedItem = choices.FirstOrDefault(choice =>
            choice.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase)) ?? choices[0];
    }

    private void RefreshGroupList(string? selectId)
    {
        bindingGroups = true;
        try
        {
            var rows = groups
                .OrderBy(group => group.Order)
                .Select(group => new BuildingGroupChoice(group.Id, group.Order, group.Name))
                .ToList();
            groupList.ItemsSource = rows;
            groupList.SelectedItem = rows.FirstOrDefault(row =>
                row.Id.Equals(selectId, StringComparison.OrdinalIgnoreCase));
            if (groupList.SelectedItem is null && rows.Count > 0)
            {
                groupList.SelectedIndex = 0;
            }
            selectedGroupId = (groupList.SelectedItem as BuildingGroupChoice)?.Id ?? "";
            groupNameBox.Text = groups.FirstOrDefault(group =>
                group.Id.Equals(selectedGroupId, StringComparison.OrdinalIgnoreCase))?.Name ?? "";
        }
        finally
        {
            bindingGroups = false;
        }
        RefreshAssignmentHint();
    }

    private void SelectGroup()
    {
        if (bindingGroups)
        {
            return;
        }
        selectedGroupId = (groupList.SelectedItem as BuildingGroupChoice)?.Id ?? "";
        bindingGroups = true;
        groupNameBox.Text = groups.FirstOrDefault(group =>
            group.Id.Equals(selectedGroupId, StringComparison.OrdinalIgnoreCase))?.Name ?? "";
        bindingGroups = false;
        RefreshAssignmentHint();
    }

    private void UpdateSelectedGroupName()
    {
        if (bindingGroups || string.IsNullOrWhiteSpace(selectedGroupId))
        {
            return;
        }
        ProjectBuildingGroup? group = groups.FirstOrDefault(item =>
            item.Id.Equals(selectedGroupId, StringComparison.OrdinalIgnoreCase));
        if (group is not null)
        {
            group.Name = groupNameBox.Text;
        }
        RefreshAssignmentHint();
    }

    private void AddGroup()
    {
        var group = new ProjectBuildingGroup
        {
            Name = $"Барилга {groups.Count + 1}",
            Order = groups.Count + 1,
        };
        groups.Add(group);
        RefreshGroupList(group.Id);
        groupNameBox.Focus();
        groupNameBox.SelectAll();
    }

    private void MoveSelectedGroup(int direction)
    {
        if (string.IsNullOrWhiteSpace(selectedGroupId))
        {
            return;
        }
        int index = groups.FindIndex(group =>
            group.Id.Equals(selectedGroupId, StringComparison.OrdinalIgnoreCase));
        int target = index + direction;
        if (index < 0 || target < 0 || target >= groups.Count)
        {
            return;
        }
        (groups[index], groups[target]) = (groups[target], groups[index]);
        for (var position = 0; position < groups.Count; position++)
        {
            groups[position].Order = position + 1;
        }
        RefreshGroupList(selectedGroupId);
    }

    private void RemoveSelectedGroup()
    {
        if (string.IsNullOrWhiteSpace(selectedGroupId))
        {
            return;
        }
        ProjectBuildingGroup? group = groups.FirstOrDefault(item =>
            item.Id.Equals(selectedGroupId, StringComparison.OrdinalIgnoreCase));
        if (group is null)
        {
            return;
        }
        MessageBoxResult result = StudioMessageDialog.Show(
            this,
            $"“{group.Name}” бүлгийг хасах уу? Хуудас болон эх файл устахгүй, зөвхөн оноолт сална.",
            "Барилгын бүлэг",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
        {
            return;
        }

        groups.Remove(group);
        foreach (string sheetKey in assignments
                     .Where(pair => pair.Value.Equals(group.Id, StringComparison.OrdinalIgnoreCase))
                     .Select(pair => pair.Key)
                     .ToList())
        {
            assignments.Remove(sheetKey);
        }
        for (var index = 0; index < groups.Count; index++)
        {
            groups[index].Order = index + 1;
        }
        RefreshGroupList(groups.FirstOrDefault()?.Id);
        RefreshSheets();
    }

    private void AssignSelectedSheets()
    {
        if (!TryGetSelectedGroup(out ProjectBuildingGroup group))
        {
            return;
        }
        foreach (BuildingSheetRow row in sheetList.SelectedItems.OfType<BuildingSheetRow>())
        {
            assignments[row.Record.Key] = group.Id;
        }
        RefreshSheets();
    }

    private void AssignVisibleSheets()
    {
        if (!TryGetSelectedGroup(out ProjectBuildingGroup group))
        {
            return;
        }
        foreach (BuildingSheetRow row in sheetList.Items.OfType<BuildingSheetRow>())
        {
            assignments[row.Record.Key] = group.Id;
        }
        RefreshSheets();
    }

    private void ClearSelectedAssignments()
    {
        foreach (BuildingSheetRow row in sheetList.SelectedItems.OfType<BuildingSheetRow>())
        {
            assignments.Remove(row.Record.Key);
        }
        RefreshSheets();
    }

    private bool TryGetSelectedGroup(out ProjectBuildingGroup group)
    {
        ProjectBuildingGroup? selected = groups.FirstOrDefault(item =>
            item.Id.Equals(selectedGroupId, StringComparison.OrdinalIgnoreCase));
        if (selected is not null)
        {
            group = selected;
            return true;
        }

        group = null!;
        StudioMessageDialog.Show(
            this,
            "Эхлээд барилгын бүлэг нэмээд сонгоно уу.",
            "Барилгын бүлэг",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        return false;
    }

    private void RefreshSheets()
    {
        string sourceId = (sourceFilter.SelectedItem as SourceFilterChoice)?.Id ?? "";
        sheetList.ItemsSource = records
            .Where(record =>
                string.IsNullOrWhiteSpace(sourceId) ||
                record.SourceId.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
            .Select(record => new BuildingSheetRow(
                record,
                ResolveSourceName(record),
                ResolveDrawingKind(record),
                record.Entry.Number,
                record.Entry.Name,
                ResolveBuildingLabel(record)))
            .ToList();
    }

    private void RefreshAssignmentHint()
    {
        string name = groups.FirstOrDefault(group =>
            group.Id.Equals(selectedGroupId, StringComparison.OrdinalIgnoreCase))?.Name ?? "";
        assignmentHint.Text = string.IsNullOrWhiteSpace(name)
            ? "  Барилга сонгоогүй"
            : $"  → {name.Trim()}";
    }

    private string ResolveBuildingLabel(SheetRecord record)
    {
        string explicitName = ProjectBuildingComposition.ResolveAssignedGroupName(
            record.Key,
            groups,
            assignments);
        if (!string.IsNullOrWhiteSpace(explicitName))
        {
            return explicitName;
        }
        if (!string.IsNullOrWhiteSpace(record.Entry.BuildingName))
        {
            return $"{record.Entry.BuildingName.Trim()} (package)";
        }
        if (!string.IsNullOrWhiteSpace(record.Entry.BuildingId))
        {
            return $"{record.Entry.BuildingId.Trim()} (package)";
        }
        return "Оноогоогүй";
    }

    private bool IsDrawingSheet(SheetRecord record)
    {
        AlbumPageDefinition? page = album.Pages.FirstOrDefault(item =>
            item.SheetKey.Equals(record.Key, StringComparison.OrdinalIgnoreCase));
        if (page is null)
        {
            return false;
        }
        AlbumCompositionItem? slot =
            BuildingArchitectureConceptAlbumTemplate.FindSlot(album, page.TemplateSlotId);
        return slot is null || slot.Kind != AlbumCompositionKind.SourceSlot || slot.AllowMultiple;
    }

    private int ResolveDrawingOrder(SheetRecord record)
    {
        AlbumPageDefinition? page = album.Pages.FirstOrDefault(item =>
            item.SheetKey.Equals(record.Key, StringComparison.OrdinalIgnoreCase));
        AlbumCompositionItem? slot = page is null
            ? null
            : BuildingArchitectureConceptAlbumTemplate.FindSlot(album, page.TemplateSlotId);
        return slot?.Order ?? int.MaxValue;
    }

    private string ResolveDrawingKind(SheetRecord record)
    {
        string sourceGroup = record.Entry.ContentKind?.Trim() ?? "";
        if (sourceGroup.Equals("Байгуулалт", StringComparison.OrdinalIgnoreCase) ||
            sourceGroup.Contains("Давхрын байгуулалт", StringComparison.OrdinalIgnoreCase))
        {
            return "Байгуулалт";
        }
        if (sourceGroup.Contains("Огтлол", StringComparison.OrdinalIgnoreCase))
        {
            return "Огтлол";
        }
        if (sourceGroup.Contains("Нүүр тал", StringComparison.OrdinalIgnoreCase))
        {
            return "Нүүр тал";
        }

        AlbumPageDefinition? page = album.Pages.FirstOrDefault(item =>
            item.SheetKey.Equals(record.Key, StringComparison.OrdinalIgnoreCase));
        AlbumCompositionItem? slot = page is null
            ? null
            : BuildingArchitectureConceptAlbumTemplate.FindSlot(album, page.TemplateSlotId);
        if (!string.IsNullOrWhiteSpace(slot?.SectionTitle))
        {
            return slot.SectionTitle.Trim();
        }
        if (!string.IsNullOrWhiteSpace(record.Entry.ContentKind))
        {
            return record.Entry.ContentKind.Trim();
        }
        return record.Entry.Discipline?.Trim() ?? "Зургийн хуудас";
    }

    private string ResolveSourceName(SheetRecord record)
    {
        ProjectDesignSource? source = sources.FirstOrDefault(item =>
            item.Id.Equals(record.SourceId, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(source?.NativeDocumentTitle))
        {
            return source.NativeDocumentTitle.Trim();
        }
        if (!string.IsNullOrWhiteSpace(source?.NativeDocumentPath))
        {
            return Path.GetFileName(source.NativeDocumentPath.Trim());
        }
        if (!string.IsNullOrWhiteSpace(source?.Name))
        {
            return source.Name.Trim();
        }
        if (!string.IsNullOrWhiteSpace(record.Source.DocumentTitle))
        {
            return record.Source.DocumentTitle.Trim();
        }
        return record.Source.Application.ToString();
    }

    private void Accept()
    {
        UpdateSelectedGroupName();
        List<ProjectBuildingGroup> normalized =
            ProjectBuildingComposition.NormalizeGroups(groups);
        bool duplicateName = normalized
            .GroupBy(group => group.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1);
        if (duplicateName)
        {
            StudioMessageDialog.Show(
                this,
                "Барилгын бүлгийн нэр давхардаж байна. Барилга бүрийг ялгах нэр өгнө үү.",
                "Барилгын бүлэг",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        ResultGroups = normalized.Select(group => group.Clone()).ToList();
        ResultAssignments = ProjectBuildingComposition.NormalizeAssignments(
            assignments,
            normalized);
        DialogResult = true;
    }

    private sealed record BuildingGroupChoice(string Id, int Order, string Name)
    {
        public override string ToString() => $"{Order}. {Name}";
    }

    private sealed record SourceFilterChoice(string Id, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record BuildingSheetRow(
        SheetRecord Record,
        string Source,
        string DrawingKind,
        string Number,
        string Name,
        string Building);
}
