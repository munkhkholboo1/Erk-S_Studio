using System.Windows;
using System.Windows.Controls;

namespace ErkS.Studio;

internal sealed class CloudSourceBindingDialog : Window
{
    private readonly ListView sources = new();
    private readonly Button bindButton = StudioWidgets.CreatePrimaryButton("Холбох");

    public StudioCloudSourcePackage? SelectedSource { get; private set; }

    public CloudSourceBindingDialog(IReadOnlyList<StudioCloudSourcePackage> availableSources)
    {
        Title = "Cloud source холбох";
        Width = 760;
        Height = 500;
        MinWidth = 640;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);
        Content = BuildContent(availableSources);
    }

    private UIElement BuildContent(IReadOnlyList<StudioCloudSourcePackage> availableSources)
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        var cancel = StudioWidgets.CreateButton("Болих");
        cancel.IsCancel = true;
        bindButton.IsEnabled = false;
        bindButton.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(bindButton);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        header.Children.Add(StudioWidgets.CreateTitle("Cloud source сонгох"));
        header.Children.Add(StudioWidgets.CreateHint(
            "Энд зөвхөн source key, manifest болон хариуцагч харагдана. RVT/DWG файл, локал зам server-ээс ирэхгүй."));
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var view = new GridView();
        view.Columns.Add(new GridViewColumn
        {
            Header = "Эх үүсвэр",
            Width = 300,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(CloudSourceRow.Title)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Төрөл",
            Width = 110,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(CloudSourceRow.Application)),
        });
        view.Columns.Add(new GridViewColumn
        {
            Header = "Хариуцагч",
            Width = 210,
            DisplayMemberBinding = new System.Windows.Data.Binding(nameof(CloudSourceRow.Custodian)),
        });
        sources.View = view;
        sources.ItemsSource = availableSources.Select(source => new CloudSourceRow(source)).ToList();
        sources.SelectionChanged += (_, _) => bindButton.IsEnabled = sources.SelectedItem is CloudSourceRow;
        sources.MouseDoubleClick += (_, _) => Accept();
        root.Children.Add(sources);
        return root;
    }

    private void Accept()
    {
        if (sources.SelectedItem is not CloudSourceRow row)
            return;
        SelectedSource = row.Source;
        DialogResult = true;
    }

    private sealed record CloudSourceRow(StudioCloudSourcePackage Source)
    {
        public string Title => string.IsNullOrWhiteSpace(Source.SourceDocumentReference)
            ? Source.SourceKey
            : Source.SourceDocumentReference;

        public string Application => Source.SourceApplication;

        public string Custodian => string.IsNullOrWhiteSpace(Source.CustodianEmail)
            ? "Хариуцагч томилоогүй"
            : Source.CustodianEmail;
    }
}

internal sealed record CloudSourceCustodyDraft(string SourceKey, string ParticipantId, string DisplayLabel);

internal sealed class CloudSourceCustodyDialog : Window
{
    private readonly ComboBox sourceBox = new();
    private readonly ComboBox participantBox = new();
    private readonly Button assignButton = StudioWidgets.CreatePrimaryButton("Хариуцагч болгох");

    public CloudSourceCustodyDraft? Draft { get; private set; }

    public CloudSourceCustodyDialog(
        IReadOnlyList<StudioCloudSourcePackage> cloudSources,
        IReadOnlyList<StudioCloudParticipant> participants)
    {
        Title = "Cloud source хариуцагч";
        Width = 660;
        Height = 390;
        MinWidth = 580;
        MinHeight = 340;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        StudioTheme.Apply(this);
        Content = BuildContent(cloudSources, participants);
    }

    private UIElement BuildContent(
        IReadOnlyList<StudioCloudSourcePackage> cloudSources,
        IReadOnlyList<StudioCloudParticipant> participants)
    {
        var root = new DockPanel { Margin = new Thickness(20) };
        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancel = StudioWidgets.CreateButton("Болих");
        cancel.IsCancel = true;
        assignButton.IsEnabled = false;
        assignButton.Click += (_, _) => Accept();
        actions.Children.Add(cancel);
        actions.Children.Add(assignButton);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var content = new StackPanel();
        content.Children.Add(StudioWidgets.CreateTitle("Эх үүсвэрийн хариуцагч шилжүүлэх"));
        content.Children.Add(StudioWidgets.CreateHint(
            "Энэ нь cloud source-ийн metadata хариуцагчийг л солино. Native файл дамжихгүй."));

        sourceBox.Margin = new Thickness(0, 16, 0, 0);
        sourceBox.ItemsSource = cloudSources
            .Select(source => new SourceOption(
                source,
                string.IsNullOrWhiteSpace(source.SourceDocumentReference)
                    ? source.SourceKey
                    : source.SourceDocumentReference))
            .ToList();
        sourceBox.DisplayMemberPath = nameof(SourceOption.Label);
        sourceBox.SelectionChanged += (_, _) => RefreshAction();
        content.Children.Add(Labeled("Cloud source", sourceBox));

        participantBox.ItemsSource = participants
            .Where(item => item.Status.Equals("Active", StringComparison.OrdinalIgnoreCase))
            .Select(item => new ParticipantOption(
                item,
                string.IsNullOrWhiteSpace(item.DisplayName) ? item.AccountEmail : item.DisplayName))
            .ToList();
        participantBox.DisplayMemberPath = nameof(ParticipantOption.Label);
        participantBox.SelectionChanged += (_, _) => RefreshAction();
        content.Children.Add(Labeled("Шинэ хариуцагч", participantBox));
        root.Children.Add(content);
        return root;
    }

    private static UIElement Labeled(string label, Control control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = StudioTheme.MutedTextBrush,
            Margin = new Thickness(0, 0, 0, 6),
        });
        panel.Children.Add(control);
        return panel;
    }

    private void RefreshAction() => assignButton.IsEnabled =
        sourceBox.SelectedItem is SourceOption && participantBox.SelectedItem is ParticipantOption;

    private void Accept()
    {
        if (sourceBox.SelectedItem is not SourceOption source ||
            participantBox.SelectedItem is not ParticipantOption participant)
        {
            return;
        }
        Draft = new CloudSourceCustodyDraft(
            source.Source.SourceKey,
            participant.Participant.ParticipantId,
            source.Label + " -> " + participant.Label);
        DialogResult = true;
    }

    private sealed record SourceOption(StudioCloudSourcePackage Source, string Label);

    private sealed record ParticipantOption(StudioCloudParticipant Participant, string Label);
}
