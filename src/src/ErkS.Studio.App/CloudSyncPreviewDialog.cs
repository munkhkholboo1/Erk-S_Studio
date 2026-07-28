using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErkS.Studio;

internal sealed class CloudSyncPreviewDialog : Window
{
    private readonly CloudSyncPreviewPlan plan;

    public CloudSyncPreviewDialog(CloudSyncPreviewPlan plan)
    {
        this.plan = plan;
        Title = "Cloud ERA Sync";
        Width = 820;
        Height = 700;
        MinWidth = 680;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        StudioTheme.Apply(this);
        Content = BuildContent();
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(18) };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.Click += (_, _) => DialogResult = false;
        Button confirm = StudioWidgets.CreatePrimaryButton("Sync хийх");
        confirm.IsEnabled = plan.HasUploads || plan.HasDownloads;
        confirm.Click += (_, _) => DialogResult = true;
        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        heading.Children.Add(StudioWidgets.CreateTitle("Cloud ERA Sync шалгалт"));
        heading.Children.Add(StudioWidgets.CreateHint(
            $"{plan.ProjectCode} · {plan.DeviceLabel}\n" +
            "Native DWG/RVT файл дамжихгүй. Зөвхөн source metadata, жижиг proxy/component PDF болон canonical мэдээлэл солилцоно."));
        DockPanel.SetDock(heading, Dock.Top);
        root.Children.Add(heading);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var body = new StackPanel();
        body.Children.Add(BuildSection(
            "CLOUD РУУ ИЛГЭЭХ",
            plan.Uploads,
            StudioTheme.AccentBrush,
            "Энэ Studio-оос илгээх өөрчлөлт алга."));
        body.Children.Add(BuildSection(
            "CLOUD-ООС ХҮЛЭЭН АВАХ",
            plan.Downloads,
            new SolidColorBrush(Color.FromRgb(68, 193, 137)),
            "Cloud ERA дээр татах шинэ өөрчлөлт алга."));
        body.Children.Add(BuildSection(
            "ИЛГЭЭГДЭХГҮЙ · PENDING ҮЛДЭНЭ",
            plan.Blocked,
            new SolidColorBrush(Color.FromRgb(236, 166, 63)),
            "Эрхийн зөрчилтэй локал өөрчлөлт алга."));
        scroll.Content = body;
        root.Children.Add(scroll);
        return root;
    }

    private static UIElement BuildSection(
        string title,
        IReadOnlyList<CloudSyncChangeItem> items,
        Brush accent,
        string emptyText)
    {
        var section = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
        var heading = StudioWidgets.CreateSectionHeader($"{title}  {items.Count}");
        heading.Foreground = accent;
        section.Children.Add(heading);

        if (items.Count == 0)
        {
            TextBlock empty = StudioWidgets.CreateHint(emptyText);
            empty.Margin = new Thickness(0, 7, 0, 0);
            section.Children.Add(empty);
            return section;
        }

        foreach (CloudSyncChangeItem item in items)
        {
            var text = new StackPanel { Margin = new Thickness(0, 2, 0, 2) };
            TextBlock name = StudioWidgets.CreateText(item.Title);
            name.FontWeight = FontWeights.SemiBold;
            TextBlock detail = StudioWidgets.CreateHint(item.Detail);
            detail.TextWrapping = TextWrapping.Wrap;
            text.Children.Add(name);
            text.Children.Add(detail);

            Border card = StudioWidgets.CreateCard(text);
            card.BorderThickness = new Thickness(3, 0, 0, 0);
            card.BorderBrush = accent;
            card.Padding = new Thickness(11, 8, 11, 8);
            card.Margin = new Thickness(0, 6, 0, 0);
            section.Children.Add(card);
        }
        return section;
    }
}
