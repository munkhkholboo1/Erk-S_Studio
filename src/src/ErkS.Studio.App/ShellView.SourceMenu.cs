using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ErkS.Studio;

/// <summary>
/// The per-source actions, gathered into one menu on the source itself.
/// </summary>
/// <remarks>
/// These lived as six buttons stacked in the right-hand panel, all present at
/// once whether or not they applied. The user asked for them tidied away behind
/// a menu on each source, which is also where a reader looks for "what can I do
/// with this one".
///
/// Each entry is a view of its button rather than a copy of it. Whether an
/// action applies is already decided in one place - SetNativeSourceActionsVisible
/// works out ownership, cloud binding and edit rights - and repeating that
/// reasoning here is how the two would drift until the menu offered something
/// the button knew was impossible.
/// </remarks>
internal sealed partial class ShellView
{
    private ContextMenu? sourceActionsMenu;

    private ContextMenu BuildSourceActionsMenu()
    {
        if (sourceActionsMenu is not null)
            return sourceActionsMenu;

        var menu = new ContextMenu();
        foreach ((string header, Button button, UIElement? gate) in SourceActionButtons())
            menu.Items.Add(CreateSourceActionItem(header, button, gate));

        // Read the buttons as the menu opens: their state is settled by the
        // selection, and the selection is settled by the click that opened this.
        menu.Opened += (_, _) =>
        {
            foreach (object entry in menu.Items)
            {
                if (entry is not MenuItem item || item.Tag is not SourceActionBinding binding)
                    continue;

                // Some actions belong to one kind of source and are hidden by
                // the panel that holds them rather than by their own Visibility
                // - the visualisation controls work that way. Reading only the
                // button would offer "Зураг нэмэх" on a Revit source.
                bool applies = binding.Button.Visibility == Visibility.Visible &&
                    (binding.Gate is null || binding.Gate.Visibility == Visibility.Visible);
                item.Visibility = applies ? Visibility.Visible : Visibility.Collapsed;
                item.IsEnabled = binding.Button.IsEnabled;
            }
        };

        sourceActionsMenu = menu;
        return menu;
    }

    /// <summary>What an entry watches to decide whether it applies.</summary>
    private sealed record SourceActionBinding(Button Button, UIElement? Gate);

    private (string Header, Button Button, UIElement? Gate)[] SourceActionButtons() =>
    [
        ("Эх файл нээх", openNativeSourceButton, null),
        ("Хавтас нээх", openSourceFolderButton, null),
        ("Эх файлыг солих", relinkNativeSourceButton, null),
        ("Cloud эх үүсвэртэй холбох", bindCloudSourceButton, null),
        ("Хариуцагч шилжүүлэх", transferSourceCustodyButton, null),
        ("Бүртгэлээс хасах", removeDesignSourceButton, null),

        // The visualisation source's own actions. They were a second column of
        // buttons in the panel, outside the tidying entirely - the user pointed
        // at them by name.
        ("Зураг нэмэх", addVisualizationImagesButton, visualizationSourceControls),
        ("Эх файлыг дахин заах", relinkVisualizationImageButton, visualizationSourceControls),
        ("Хуудаснаас хасах", excludeVisualizationImagesButton, visualizationSourceControls),
        ("Хуудсанд оруулах", includeVisualizationImagesButton, visualizationSourceControls),
    ];

    private static MenuItem CreateSourceActionItem(string header, Button button, UIElement? gate)
    {
        var item = new MenuItem { Header = header, Tag = new SourceActionBinding(button, gate) };
        item.Click += (_, _) =>
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        return item;
    }

    /// <summary>
    /// Right-clicking a source selects it first.
    /// </summary>
    /// <remarks>
    /// Every action reads the selection, so a menu opened on a row that was not
    /// selected would act on a different source than the one pointed at -
    /// removal included.
    /// </remarks>
    private void SelectSourceUnderPointer(object sender, MouseButtonEventArgs args)
    {
        if (args.OriginalSource is not DependencyObject origin)
            return;

        ListBoxItem? row = FindAncestor<ListBoxItem>(origin);
        if (row is not null)
            row.IsSelected = true;
    }

    /// <summary>
    /// Opens the actions menu from the ⋯ button on a row.
    /// </summary>
    /// <remarks>
    /// Handled on the list rather than wired per button because the buttons are
    /// created by a template, once per row, with no instance to attach to.
    /// </remarks>
    private void OpenSourceActionsMenu(object sender, RoutedEventArgs args)
    {
        if (args.OriginalSource is not Button button ||
            button.Tag as string != SourceMenuButtonTag)
        {
            return;
        }

        ListBoxItem? row = FindAncestor<ListBoxItem>(button);
        if (row is not null)
            row.IsSelected = true;

        ContextMenu menu = BuildSourceActionsMenu();
        menu.PlacementTarget = button;
        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        menu.IsOpen = true;
        args.Handled = true;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null and not T)
            node = VisualTreeHelper.GetParent(node);
        return node as T;
    }
}
