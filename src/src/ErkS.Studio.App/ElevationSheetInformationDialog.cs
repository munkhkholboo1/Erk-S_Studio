using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed class ElevationSheetInformationDialog : Window
{
    private readonly string sourceDescription;
    private readonly TextBox descriptionBox = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 190,
        VerticalContentAlignment = VerticalAlignment.Top,
    };
    private readonly CheckBox followSourceCheck = new()
    {
        Content = "Эх үүсвэрийн Хуудасны тайлбарыг дагах",
        Margin = new Thickness(0, 0, 0, 10),
    };

    public string? DescriptionOverride { get; private set; }

    public ElevationSheetInformationDialog(
        string number,
        string title,
        string sourceDescription,
        string? currentOverride,
        ConceptElevationHeaderSnapshot roster)
    {
        this.sourceDescription = sourceDescription ?? "";
        Title = "Нүүр талын мэдээлэл";
        Width = 700;
        Height = 560;
        MinWidth = 560;
        MinHeight = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        ShowInTaskbar = false;
        StudioTheme.Apply(this);

        followSourceCheck.IsChecked = currentOverride is null;
        descriptionBox.Text = currentOverride ?? this.sourceDescription;
        followSourceCheck.Checked += (_, _) => ApplySourceMode();
        followSourceCheck.Unchecked += (_, _) => ApplySourceMode();
        Content = BuildContent(number, title, roster);
        ApplySourceMode();
    }

    private UIElement BuildContent(
        string number,
        string title,
        ConceptElevationHeaderSnapshot roster)
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.Click += (_, _) => DialogResult = false;
        Button save = StudioWidgets.CreatePrimaryButton("Хадгалах");
        save.IsDefault = true;
        save.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(save);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var content = new StackPanel();
        content.Children.Add(StudioWidgets.CreateTitle("Нүүр талын мэдээлэл"));
        content.Children.Add(new TextBlock
        {
            Text = $"{number}  {title}",
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });
        content.Children.Add(CreateRosterSummary(roster));
        content.Children.Add(StudioWidgets.CreateSectionHeader("ТАЙЛБАР"));
        content.Children.Add(followSourceCheck);
        content.Children.Add(descriptionBox);
        root.Children.Add(new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        return root;
    }

    private static UIElement CreateRosterSummary(ConceptElevationHeaderSnapshot roster)
    {
        string approved = FormatOfficials(roster.ApprovedBy);
        string reviewed = FormatOfficials(roster.ReviewedBy);
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        panel.Children.Add(new TextBlock
        {
            Text = "БАТЛАВ: " + (string.IsNullOrWhiteSpace(approved) ? "-" : approved),
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 5),
        });
        panel.Children.Add(new TextBlock
        {
            Text = "ХЯНАВ: " + (string.IsNullOrWhiteSpace(reviewed)
                ? "АТД хэсгээс хүн сонгоогүй байна"
                : reviewed),
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        return panel;
    }

    private static string FormatOfficials(IEnumerable<ProjectApprovalEntry> entries) =>
        string.Join(", ", entries
            .Where(entry => !string.IsNullOrWhiteSpace(entry.PersonName) ||
                            !string.IsNullOrWhiteSpace(entry.PositionTitle))
            .Select(entry => string.Join(" ", new[]
            {
                entry.PositionTitle.Trim(),
                entry.PersonName.Trim(),
            }.Where(value => !string.IsNullOrWhiteSpace(value)))));

    private void ApplySourceMode()
    {
        bool followSource = followSourceCheck.IsChecked == true;
        descriptionBox.IsReadOnly = followSource;
        if (followSource)
            descriptionBox.Text = sourceDescription;
    }

    private void Accept()
    {
        DescriptionOverride = followSourceCheck.IsChecked == true
            ? null
            : descriptionBox.Text.Trim();
        DialogResult = true;
    }
}
