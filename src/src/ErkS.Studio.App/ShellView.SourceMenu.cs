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
        foreach ((string header, Button button) in SourceActionButtons())
            menu.Items.Add(CreateSourceActionItem(header, button));

        // Read the buttons as the menu opens: their state is settled by the
        // selection, and the selection is settled by the click that opened this.
        menu.Opened += (_, _) =>
        {
            foreach (object entry in menu.Items)
            {
                if (entry is MenuItem item && item.Tag is Button source)
                {
                    item.IsEnabled = source.IsEnabled;
                    item.Visibility = source.Visibility;
                }
            }
        };

        sourceActionsMenu = menu;
        return menu;
    }

    private (string Header, Button Button)[] SourceActionButtons() =>
    [
        ("Эх файл нээх", openNativeSourceButton),
        ("Хавтас нээх", openSourceFolderButton),
        ("Эх файлыг солих", relinkNativeSourceButton),
        ("Cloud эх үүсвэртэй холбох", bindCloudSourceButton),
        ("Хариуцагч шилжүүлэх", transferSourceCustodyButton),
        ("Бүртгэлээс хасах", removeDesignSourceButton),
    ];

    private static MenuItem CreateSourceActionItem(string header, Button button)
    {
        var item = new MenuItem { Header = header, Tag = button };
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
