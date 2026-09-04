using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErkS.Studio;

/// <summary>
/// Which row a pointer gesture in the project list actually landed on.
///
/// The defect this exists for: the list opened whatever was SELECTED when a
/// double-click arrived, and a ListView raises MouseDoubleClick for a click
/// anywhere inside it - the empty space beside the last card, the space under
/// the last row, a stage heading, the scroll bar. So a person clicking around
/// the catalogue selected a project, clicked twice on nothing, and the project
/// they had merely selected opened at them.
///
/// The rule here is that a double-click opens the row it is ON. Landing on
/// anything that is not a row opens nothing at all - not the selection, not the
/// last thing touched. That is also why the card's own actions handle is
/// excluded: its clicks mean "show me the menu", and they are inside a card, so
/// without naming it they would read as clicks on the project.
/// </summary>
internal static class StudioProjectListGesture
{
    /// <summary>
    /// Marks the three-dot handle on a card. A tag rather than a type test,
    /// because the handle is a plain Border built by a template factory and
    /// there is no class of its own to look for.
    /// </summary>
    public const string ActionsHandleTag = "erks.project.actions-handle";

    /// <summary>
    /// The item under <paramref name="originalSource"/>, or null when the
    /// gesture did not land on one.
    /// </summary>
    public static object? ItemUnder(ItemsControl list, object? originalSource)
    {
        ArgumentNullException.ThrowIfNull(list);

        DependencyObject? node = originalSource as DependencyObject;
        while (node is not null && !ReferenceEquals(node, list))
        {
            if (node is FrameworkElement element &&
                string.Equals(element.Tag as string, ActionsHandleTag, StringComparison.Ordinal))
            {
                return null;
            }

            if (node is ListBoxItem container)
            {
                object item = list.ItemContainerGenerator.ItemFromContainer(container);
                return item == DependencyProperty.UnsetValue ? container.DataContext : item;
            }

            node = ParentOf(node);
        }

        return null;
    }

    /// <summary>
    /// One step up the tree. Visual first, because that is the tree a hit test
    /// answers in. The content-element arm is defensive rather than exercised:
    /// no card today puts a run or a hyperlink inside its text, and if one ever
    /// does, a content element has no visual parent and the walk would end an
    /// element early - reading a click on the title as a click on nothing.
    /// </summary>
    private static DependencyObject? ParentOf(DependencyObject node) => node switch
    {
        Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(node),
        FrameworkContentElement content => (DependencyObject?)content.Parent ?? content.TemplatedParent,
        _ => LogicalTreeHelper.GetParent(node),
    };
}
