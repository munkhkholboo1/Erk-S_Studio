using System.Windows;
using System.Windows.Controls;

namespace ErkS.Studio;

internal sealed record StudioListPickerRow(string Key, string Title, string Detail);

/// <summary>
/// Picks one or more rows from a plain list. Used where the choice is a simple
/// "which of these" and a full workspace would be in the way.
/// </summary>
internal sealed class StudioListPickerDialog : Window
{
    private readonly ListView list = new() { SelectionMode = SelectionMode.Extended };

    public StudioListPickerDialog(string title, IReadOnlyList<StudioListPickerRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        Title = title;
        Width = 640;
        Height = 520;
        MinWidth = 460;
        MinHeight = 360;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);

        var view = new GridView();
        view.Columns.Add(new GridViewColumn
        {
            Header = "Нэр",
            Width = 350,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(StudioListPickerRow.Title)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Мэдээлэл",
            Width = 230,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(StudioListPickerRow.Detail)),
        });
        list.View = view;
        list.ItemsSource = rows;
        list.MouseDoubleClick += (_, _) => Accept();

        var root = new DockPanel { Margin = new Thickness(16) };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };
        var cancel = StudioWidgets.CreateButton("Болих");
        cancel.Click += (_, _) => DialogResult = false;
        var accept = StudioWidgets.CreatePrimaryButton("Нэмэх");
        accept.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(accept);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);
        root.Children.Add(list);
        Content = root;
    }

    public IReadOnlyList<string> SelectedKeys { get; private set; } = [];

    private void Accept()
    {
        SelectedKeys = list.SelectedItems
            .OfType<StudioListPickerRow>()
            .Select(row => row.Key)
            .ToList();
        if (SelectedKeys.Count == 0)
        {
            StudioMessageDialog.Show(this, "Дор хаяж нэгийг сонгоно уу.");
            return;
        }
        DialogResult = true;
    }
}
