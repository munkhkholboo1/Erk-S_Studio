using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;
using Microsoft.Win32;

namespace ErkS.Studio;

/// <summary>
/// Edits what Studio writes into the working drawings' 01 «ЕРӨНХИЙ ХЭСЭГ»:
/// the explanatory note, the set's composition and the technical indicators.
/// The drawing list needs no editing - it is the album.
/// </summary>
internal sealed class WorkingDrawingFrontMatterDialog : Window
{
    private readonly TextBox noteBox = new()
    {
        AcceptsReturn = true,
        AcceptsTab = false,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalContentAlignment = VerticalAlignment.Top,
        MinHeight = 320,
    };
    private readonly ObservableCollection<WorkingDrawingCompositionRow> composition = [];
    private readonly ObservableCollection<WorkingDrawingIndicatorRow> indicators = [];

    public WorkingDrawingFrontMatter Result { get; private set; } = new();

    public WorkingDrawingFrontMatterDialog(WorkingDrawingFrontMatter? current)
    {
        Title = "Ерөнхий хэсгийн мэдээлэл";
        Width = 860;
        Height = 720;
        MinWidth = 640;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        StudioTheme.Apply(this);

        Fill(current ?? new WorkingDrawingFrontMatter());
        Content = BuildContent();
    }

    private void Fill(WorkingDrawingFrontMatter matter)
    {
        noteBox.Text = (matter.ExplanatoryNote ?? "").Replace("\n", Environment.NewLine);
        composition.Clear();
        foreach (WorkingDrawingCompositionRow row in matter.Composition)
            composition.Add(row.Clone());
        indicators.Clear();
        foreach (WorkingDrawingIndicatorRow row in matter.Indicators)
            indicators.Add(row.Clone());
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(20) };

        var actions = new DockPanel { Margin = new Thickness(0, 16, 0, 0) };
        Button import = StudioWidgets.CreateButton("Ноорог файлаас оруулах…");
        import.ToolTip = "Markdown ноорог (жишээ нь PFR-ийн тайлбар бичгийн ноорог) уншиж тайлбар, иж бүрдэл, ТЭЗҮ-г бөглөнө.";
        import.Click += (_, _) => ImportDraft();
        DockPanel.SetDock(import, Dock.Left);
        actions.Children.Add(import);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.Click += (_, _) => DialogResult = false;
        Button save = StudioWidgets.CreatePrimaryButton("Хадгалах");
        save.Click += (_, _) => Accept();
        right.Children.Add(cancel);
        right.Children.Add(save);
        actions.Children.Add(right);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        header.Children.Add(StudioWidgets.CreateTitle("Ерөнхий хэсгийн мэдээлэл"));
        header.Children.Add(new TextBlock
        {
            Text = "Эх үүсвэрийн 01 «ЕРӨНХИЙ ХЭСЭГ» хуудсанд нөөцөлсөн хүрээнд хэвлэгдэнэ. " +
                   "Зургийн жагсаалтыг альбумаас өөрөө гаргана. «[⚠ …]» нь бөглөх хэсэг. " +
                   "Иж бүрдлийн тоо хоосон бол альбумын өөрийн хуудсыг тоолно.",
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var tabs = new TabControl();
        tabs.Items.Add(new TabItem { Header = "Тайлбар бичиг", Content = noteBox });
        tabs.Items.Add(new TabItem
        {
            Header = "Иж бүрдлийн жагсаалт",
            Content = Grid(
                composition,
                ("Марк", nameof(WorkingDrawingCompositionRow.Mark), 90),
                ("Хэсэг", nameof(WorkingDrawingCompositionRow.Name), 380),
                ("Хуудасны тоо", nameof(WorkingDrawingCompositionRow.SheetCount), 120)),
        });
        tabs.Items.Add(new TabItem
        {
            Header = "Техник эдийн засгийн үзүүлэлт",
            Content = Grid(
                indicators,
                ("№", nameof(WorkingDrawingIndicatorRow.Number), 60),
                ("Үзүүлэлт", nameof(WorkingDrawingIndicatorRow.Name), 340),
                ("Хэмжих нэгж", nameof(WorkingDrawingIndicatorRow.Unit), 110),
                ("Хэмжээ", nameof(WorkingDrawingIndicatorRow.Value), 120)),
        });
        root.Children.Add(tabs);
        return root;
    }

    private static DataGrid Grid<T>(ObservableCollection<T> rows, params (string Header, string Path, double Width)[] columns)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            CanUserAddRows = true,
            CanUserDeleteRows = true,
            HeadersVisibility = DataGridHeadersVisibility.Column,
            MinHeight = 320,
        };
        foreach ((string header, string path, double width) in columns)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = header,
                Binding = new System.Windows.Data.Binding(path)
                {
                    UpdateSourceTrigger = System.Windows.Data.UpdateSourceTrigger.PropertyChanged,
                },
                Width = width,
            });
        }
        return grid;
    }

    private void ImportDraft()
    {
        var picker = new OpenFileDialog
        {
            Title = "Тайлбар бичгийн ноорог сонгох",
            Filter = "Markdown (*.md)|*.md|Текст (*.txt)|*.txt|Бүх файл (*.*)|*.*",
        };
        if (picker.ShowDialog(this) != true)
            return;

        bool hasContent = noteBox.Text.Trim().Length > 0 || composition.Count > 0 || indicators.Count > 0;
        if (hasContent &&
            MessageBox.Show(
                this,
                "Одоо бичигдсэн тайлбар, иж бүрдэл, ТЭЗҮ-г нооргоор солих уу?",
                "Ноорог оруулах",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            Fill(WorkingDrawingFrontMatterDraft.Parse(File.ReadAllText(picker.FileName)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, "Файлыг уншиж чадсангүй: " + exception.Message, "Ноорог оруулах",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Accept()
    {
        var matter = new WorkingDrawingFrontMatter
        {
            ExplanatoryNote = noteBox.Text,
            Composition = composition.Where(row => row is not null).Select(row => row.Clone()).ToList(),
            Indicators = indicators.Where(row => row is not null).Select(row => row.Clone()).ToList(),
        };
        matter.Normalize();
        Result = matter;
        DialogResult = true;
    }
}
