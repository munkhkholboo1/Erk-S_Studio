using System.IO;
using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Where the owner records who signs, district by district.
///
/// 🔴 THE SOURCE IS STILL THE OWNER'S DECISION AND THIS SCREEN DOES NOT PREJUDGE
/// IT. Nothing publishes these officials today; whether the list is maintained
/// here or served by SRV is a question about who keeps it up to date. If it
/// becomes served, this screen stops being the primary surface and remains the
/// one that corrects a row - so it is kept SIMPLE AND COMPLETE rather than built
/// into a fast bulk-entry tool that may never be the way rows arrive.
///
/// 🔴 THE CASCADE IS NOT REBUILT HERE. <see cref="AdministrativeUnitPicker"/>
/// already holds it, in Core, tested - the wards are simply not asked for. The
/// alternative considered and rejected was a second picker of this screen's own:
/// the same mechanism under a different name, maintained twice and fixed once.
/// </summary>
internal sealed class OfficialsDirectoryDialog : Window
{
    private readonly StudioOfficialsDirectory directory;
    private readonly AdministrativeUnitPicker picker;

    private readonly ComboBox provinceBox = new();
    private readonly ComboBox districtBox = new();
    private readonly StackPanel rowsPanel = new();
    private readonly List<OfficialRowEditor> rowEditors = [];
    private readonly Button addRowButton = StudioWidgets.CreateButton("Мөр нэмэх");
    private readonly Button saveButton = StudioWidgets.CreatePrimaryButton("Хадгалах");

    private readonly TextBlock sourceText = StudioWidgets.CreateHint("");
    private readonly TextBlock lossText = StudioWidgets.CreateHint("");
    private readonly TextBlock statusText = StudioWidgets.CreateHint("");

    private List<OfficialsDirectoryEntry> all = [];
    private bool binding;

    public OfficialsDirectoryDialog(
        StudioOfficialsDirectory directory,
        IAdministrativeUnitCatalogue catalogue)
    {
        this.directory = directory;
        picker = new AdministrativeUnitPicker(catalogue);

        Title = "Албан тушаалтны лавлах";
        Width = 760;
        Height = 560;
        MinWidth = 640;
        MinHeight = 460;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);

        Content = BuildBody();

        directory.Load();
        all = [.. directory.All];
        RefreshProvinces();
        RefreshState();
    }

    private UIElement BuildBody()
    {
        var root = new DockPanel { Margin = new Thickness(18) };

        var header = new StackPanel();
        header.Children.Add(StudioWidgets.CreateTitle("Албан тушаалтны лавлах"));
        header.Children.Add(StudioWidgets.CreateHint(
            "Төслийн хаягаар зөвшилцөх, хянах албан тушаалтныг санал болгоход энэ лавлах " +
            "хэрэглэгдэнэ. Сум/дүүрэг тус бүрээр нэг удаа бөглөөд, өөрчлөгдөхөд нь засна."));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var chooser = new StackPanel { Margin = new Thickness(0, 12, 0, 8) };
        provinceBox.SelectionChanged += (_, _) => OnProvinceChosen();
        districtBox.SelectionChanged += (_, _) => OnDistrictChosen();
        chooser.Children.Add(StudioWidgets.CreateFormRow("Аймаг, нийслэл", provinceBox));
        chooser.Children.Add(StudioWidgets.CreateFormRow("Сум, дүүрэг", districtBox));
        DockPanel.SetDock(chooser, Dock.Top);
        root.Children.Add(chooser);

        var footer = new StackPanel();
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        addRowButton.Click += (_, _) => AddRow();
        saveButton.Click += (_, _) => Save();
        buttons.Children.Add(addRowButton);
        buttons.Children.Add(saveButton);
        footer.Children.Add(buttons);

        // 🔴 THREE SENTENCES, NOT ONE. «Nothing filled in», «the file would not
        // read» and «some rows were refused» are three different things to do
        // next, and each of them looks exactly like an empty list from here.
        footer.Children.Add(sourceText);
        footer.Children.Add(lossText);
        footer.Children.Add(statusText);
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);

        root.Children.Add(StudioWidgets.CreateScrollHost(rowsPanel));
        return root;
    }

    private void RefreshProvinces()
    {
        binding = true;
        provinceBox.Items.Clear();
        foreach (AdministrativeUnit unit in picker.ProvinceChoices().Units)
            provinceBox.Items.Add(new UnitItem(unit));
        binding = false;
    }

    private void OnProvinceChosen()
    {
        if (binding)
            return;

        picker.ChooseProvince((provinceBox.SelectedItem as UnitItem)?.Unit);

        binding = true;
        districtBox.Items.Clear();
        foreach (AdministrativeUnit unit in picker.DistrictChoices().Units)
            districtBox.Items.Add(new UnitItem(unit));
        binding = false;

        OnDistrictChosen();
    }

    private void OnDistrictChosen()
    {
        if (binding)
            return;

        picker.ChooseDistrict((districtBox.SelectedItem as UnitItem)?.Unit);
        ShowRows(OfficialsDirectoryEdit.RowsOf(all, ChosenUnitCode));
        RefreshState();
    }

    private string ChosenUnitCode => picker.District?.UnitCode ?? "";

    private void ShowRows(IReadOnlyList<OfficialsDirectoryEntry> rows)
    {
        rowEditors.Clear();
        rowsPanel.Children.Clear();
        foreach (OfficialsDirectoryEntry row in rows)
            AppendRow(row);
    }

    private void AddRow()
    {
        if (ChosenUnitCode.Length == 0)
        {
            // Refused rather than collected: a row typed with no district chosen
            // would be discarded on save, and the person would have no way to
            // know which of their rows went.
            statusText.Text = "Эхлээд сум, дүүргээ сонгоно уу.";
            return;
        }

        AppendRow(new OfficialsDirectoryEntry { UnitCode = ChosenUnitCode });
        rowEditors[^1].OrganizationBox.Focus();
    }

    private void AppendRow(OfficialsDirectoryEntry entry)
    {
        var editor = new OfficialRowEditor(entry);
        editor.RemoveButton.Click += (_, _) =>
        {
            rowEditors.Remove(editor);
            rowsPanel.Children.Remove(editor.Root);
        };
        rowEditors.Add(editor);
        rowsPanel.Children.Add(editor.Root);
    }

    private void Save()
    {
        if (ChosenUnitCode.Length == 0)
        {
            statusText.Text = "Эхлээд сум, дүүргээ сонгоно уу.";
            return;
        }

        all =
        [
            .. OfficialsDirectoryEdit.ReplaceUnit(
                all,
                ChosenUnitCode,
                [.. rowEditors.Select(editor => editor.Read())]),
        ];

        try
        {
            directory.Save(all, DateTimeOffset.UtcNow);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            statusText.Text = "Хадгалагдсангүй: " + exception.Message;
            return;
        }

        // 🔴 WHAT THE READER KEPT, NOT WHAT WAS SENT. The save re-reads the file,
        // so the list shown afterwards is the one the next run will see - and a
        // row the reader refused disappears here, in front of the person, rather
        // than months later on a sheet.
        all = [.. directory.All];
        ShowRows(OfficialsDirectoryEdit.RowsOf(all, ChosenUnitCode));
        statusText.Text = $"Хадгаллаа — лавлахад {directory.Count} мөр байна.";
        RefreshState(keepStatus: true);
    }

    private void RefreshState(bool keepStatus = false)
    {
        sourceText.Text = directory.SourceMn;
        lossText.Text = directory.LossMn;
        lossText.Visibility = directory.LossMn.Length == 0
            ? Visibility.Collapsed
            : Visibility.Visible;

        if (!keepStatus)
            statusText.Text = directory.UnavailableReasonMn;

        addRowButton.IsEnabled = ChosenUnitCode.Length > 0;
        saveButton.IsEnabled = ChosenUnitCode.Length > 0;
    }

    private sealed record UnitItem(AdministrativeUnit Unit)
    {
        public override string ToString() => Unit.NameMn;
    }

    /// <summary>One official's row on screen.</summary>
    private sealed class OfficialRowEditor
    {
        public OfficialRowEditor(OfficialsDirectoryEntry entry)
        {
            UnitCode = entry.UnitCode;
            OrganizationBox.Text = entry.OrganizationName;
            PositionBox.Text = entry.PositionTitle;
            PersonBox.Text = entry.PersonName;

            foreach (OfficialBodyKind kind in Enum.GetValues<OfficialBodyKind>())
                KindBox.Items.Add(new KindItem(kind));
            KindBox.SelectedIndex = Array.IndexOf(Enum.GetValues<OfficialBodyKind>(), entry.Kind);

            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });

            Place(KindBox, 0);
            Place(OrganizationBox, 1);
            Place(PositionBox, 2);
            Place(PersonBox, 3);
            Place(RemoveButton, 4);
            Root = grid;

            void Place(UIElement element, int column)
            {
                if (element is Control control)
                    control.Margin = new Thickness(2, 0, 2, 0);
                Grid.SetColumn(element, column);
                grid.Children.Add(element);
            }
        }

        public string UnitCode { get; }

        public ComboBox KindBox { get; } = new();

        public TextBox OrganizationBox { get; } = new();

        public TextBox PositionBox { get; } = new();

        public TextBox PersonBox { get; } = new();

        public Button RemoveButton { get; } = StudioWidgets.CreateInlineButton("✕");

        public Grid Root { get; }

        public OfficialsDirectoryEntry Read() => new()
        {
            UnitCode = UnitCode,
            Kind = (KindBox.SelectedItem as KindItem)?.Kind ?? OfficialBodyKind.EmergencyManagement,
            OrganizationName = OrganizationBox.Text,
            PositionTitle = PositionBox.Text,
            PersonName = PersonBox.Text,
        };

        private sealed record KindItem(OfficialBodyKind Kind)
        {
            public override string ToString() => OfficialBodyLabels.Mongolian(Kind);
        }
    }
}
