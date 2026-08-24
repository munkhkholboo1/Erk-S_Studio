using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Filing the source list under the people who registered its entries.
/// </summary>
internal sealed partial class ShellView
{
    /// <summary>
    /// The heading one source belongs under.
    /// </summary>
    /// <remarks>
    /// The name comes from the project's own member list, which is on disk and
    /// always available. The photograph comes from what the server last said,
    /// which may not have arrived yet - so a missing photograph falls back to
    /// initials rather than leaving a blank circle.
    /// </remarks>
    private SourceOwnerGroup ResolveSourceOwner(string? ownerEmail)
    {
        string email = (ownerEmail ?? "").Trim();
        if (email.Length == 0)
            return SourceOwnerGroup.ThisDevice;

        string displayName = email;
        if (state.HasOpenProject)
        {
            ProjectMember? member = state.Project.Foundation.DesignCompany.Members
                .Concat(state.Project.Foundation.PlanningTask.AuthorityMembers)
                .FirstOrDefault(candidate => candidate.Email.Trim().Equals(
                    email,
                    StringComparison.OrdinalIgnoreCase));
            if (member is not null && !string.IsNullOrWhiteSpace(member.FullName))
                displayName = member.FullName;
        }

        state.ParticipantPresence.TryGetValue(email, out ParticipantPresenceInfo? presence);
        string initials = string.IsNullOrWhiteSpace(presence?.Initials)
            ? StudioOrganizationCrest.Initials(displayName)
            : presence!.Initials;

        return new SourceOwnerGroup(
            email,
            displayName,
            initials,
            presence?.ProfileImageUrl ?? "");
    }

    /// <summary>
    /// The heading drawn above each person's sources.
    /// </summary>
    private static DataTemplate CreateSourceOwnerHeaderTemplate()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 12, 2, 4));

        var avatar = new FrameworkElementFactory(typeof(Border));
        avatar.SetValue(FrameworkElement.WidthProperty, 22.0);
        avatar.SetValue(FrameworkElement.HeightProperty, 22.0);
        avatar.SetValue(Border.CornerRadiusProperty, new CornerRadius(11));
        avatar.SetValue(Border.BackgroundProperty, StudioTheme.InputBrush);
        avatar.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 0, 8, 0));
        avatar.SetValue(DockPanel.DockProperty, Dock.Left);

        var monogram = new FrameworkElementFactory(typeof(TextBlock));
        monogram.SetBinding(TextBlock.TextProperty, new Binding("Name.Initials"));
        monogram.SetValue(TextBlock.FontSizeProperty, 9.0);
        monogram.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        monogram.SetValue(TextBlock.ForegroundProperty, StudioTheme.MutedTextBrush);
        monogram.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        monogram.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        avatar.AppendChild(monogram);
        row.AppendChild(avatar);

        var count = new FrameworkElementFactory(typeof(TextBlock));
        count.SetBinding(TextBlock.TextProperty, new Binding("ItemCount"));
        count.SetValue(TextBlock.FontSizeProperty, 10.0);
        count.SetValue(TextBlock.ForegroundProperty, StudioTheme.FaintTextBrush);
        count.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        count.SetValue(FrameworkElement.MarginProperty, new Thickness(6, 0, 2, 0));
        count.SetValue(DockPanel.DockProperty, Dock.Right);
        row.AppendChild(count);

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding("Name.DisplayName"));
        name.SetValue(TextBlock.FontSizeProperty, 11.5);
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.ForegroundProperty, StudioTheme.TextBrush);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        name.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        row.AppendChild(name);

        return new DataTemplate { VisualTree = row };
    }

    /// <summary>
    /// One source, in two lines instead of a run of pipe characters.
    /// </summary>
    /// <remarks>
    /// The old row read "Revit | someone@example.com | 12 sheet | Альбумын
    /// байрлал хүлээгдэж байна | Зөвхөн харах" - every fact given equal weight
    /// and none of it scannable. The document's name is what a person looks
    /// for; the rest belongs underneath it, quieter.
    /// </remarks>
    /// <summary>Marks the ⋯ button so the list can tell it apart.</summary>
    internal const string SourceMenuButtonTag = "source-actions";

    private static DataTemplate CreateSourceItemTemplate()
    {
        var row = new FrameworkElementFactory(typeof(DockPanel));
        row.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 3));

        // A visible handle for the actions. Right-click alone would work, but
        // nobody finds a menu they cannot see - and the user asked for the
        // three dots by name.
        var menuButton = new FrameworkElementFactory(typeof(Button));
        menuButton.SetValue(ContentControl.ContentProperty, "⋯");
        menuButton.SetValue(FrameworkElement.TagProperty, SourceMenuButtonTag);
        menuButton.SetValue(FrameworkElement.WidthProperty, 24.0);
        menuButton.SetValue(FrameworkElement.HeightProperty, 22.0);
        menuButton.SetValue(Control.PaddingProperty, new Thickness(0));
        menuButton.SetValue(Control.BorderThicknessProperty, new Thickness(0));
        menuButton.SetValue(Control.BackgroundProperty, Brushes.Transparent);
        menuButton.SetValue(Control.ForegroundProperty, StudioTheme.MutedTextBrush);
        menuButton.SetValue(Control.FontSizeProperty, 14.0);
        menuButton.SetValue(FrameworkElement.ToolTipProperty, "Энэ эх үүсвэрийн үйлдлүүд");
        menuButton.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        menuButton.SetValue(DockPanel.DockProperty, Dock.Right);
        row.AppendChild(menuButton);

        var root = new FrameworkElementFactory(typeof(StackPanel));

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new Binding(nameof(SourceWorkspaceItem.Name)));
        name.SetValue(TextBlock.FontSizeProperty, 12.5);
        name.SetValue(TextBlock.ForegroundProperty, StudioTheme.TextBrush);
        name.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        root.AppendChild(name);

        var detail = new FrameworkElementFactory(typeof(TextBlock));
        detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(SourceWorkspaceItem.Summary)));
        detail.SetValue(TextBlock.FontSizeProperty, 10.5);
        detail.SetValue(TextBlock.ForegroundProperty, StudioTheme.MutedTextBrush);
        detail.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        detail.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 2, 0, 0));
        root.AppendChild(detail);

        row.AppendChild(root);
        return new DataTemplate(typeof(SourceWorkspaceItem)) { VisualTree = row };
    }
}
