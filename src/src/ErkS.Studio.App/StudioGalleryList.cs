using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;

namespace ErkS.Studio;

/// <summary>
/// How a list draws its items when they are cards rather than table rows.
/// </summary>
/// <remarks>
/// This exists because of a regression that shipped.
///
/// The received-sheets list was a table, and its rows were drawn by a
/// hand-written container template built around a GridViewRowPresenter - the
/// element whose entire job is to lay a row out against a GridView's columns.
/// When the list became a card gallery, its View was set to null and an
/// ItemTemplate was given. Both correct, and both ignored: a
/// GridViewRowPresenter does not consult ContentTemplate, and with no columns
/// to lay out it drew nothing at all.
///
/// So the pane went blank while the info bar above it still said "Хүлээн
/// авсан: 6 sheet". The data was there the whole time; nothing was rendering
/// it. That is the shape of failure worth naming - a count that says the
/// content exists, beside a surface that shows none of it.
///
/// The two facts that had to agree - "no columns" and "row presenter" - sat a
/// hundred lines apart in one method, so nothing about reading either one
/// suggested the other. Keeping the gallery's container here means a list in
/// gallery mode asks for the gallery container by name.
/// </remarks>
internal static class StudioGalleryList
{
    /// <summary>Lays items out left to right, wrapping to the pane's width.</summary>
    public static ItemsPanelTemplate CreateWrapPanel()
    {
        var panel = new FrameworkElementFactory(typeof(WrapPanel));
        panel.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
        return new ItemsPanelTemplate { VisualTree = panel };
    }

    /// <summary>
    /// The container for one card: a background that reacts to hover and
    /// selection, wrapped around whatever the list's ItemTemplate draws.
    /// </summary>
    public static Style CreateItemContainerStyle()
    {
        var style = new Style(typeof(ListViewItem));
        style.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.TextBrush));
        style.Setters.Add(new Setter(Control.BackgroundProperty, Brushes.Transparent));
        style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(5, 4, 5, 4)));

        var template = new ControlTemplate(typeof(ListViewItem));
        var rowBackground = new FrameworkElementFactory(typeof(Border), "RowBackground");
        rowBackground.SetBinding(
            Border.BackgroundProperty,
            new Binding(nameof(Control.Background))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            });
        rowBackground.SetBinding(
            Border.PaddingProperty,
            new Binding(nameof(Control.Padding))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            });
        rowBackground.SetBinding(
            System.Windows.Documents.TextElement.ForegroundProperty,
            new Binding(nameof(Control.Foreground))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            });
        rowBackground.SetValue(Border.CornerRadiusProperty, new CornerRadius(4));
        rowBackground.SetValue(UIElement.SnapsToDevicePixelsProperty, true);

        // A ContentPresenter, not a GridViewRowPresenter: this is what reads
        // the list's ItemTemplate. Inside a ListViewItem's own template it
        // picks up Content and ContentTemplate from the container without
        // being told to.
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Left);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        presenter.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        rowBackground.AppendChild(presenter);
        template.VisualTree = rowBackground;

        var hover = new Trigger
        {
            Property = UIElement.IsMouseOverProperty,
            Value = true,
        };
        hover.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(29, 40, 54)),
            "RowBackground"));
        hover.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.TextBrush));
        template.Triggers.Add(hover);

        var focusedSelection = new MultiTrigger();
        focusedSelection.Conditions.Add(
            new System.Windows.Condition(ListBoxItem.IsSelectedProperty, true));
        focusedSelection.Conditions.Add(
            new System.Windows.Condition(Selector.IsSelectionActiveProperty, true));
        focusedSelection.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(25, 79, 132)),
            "RowBackground"));
        focusedSelection.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.TextBrush));
        template.Triggers.Add(focusedSelection);

        var unfocusedSelection = new MultiTrigger();
        unfocusedSelection.Conditions.Add(
            new System.Windows.Condition(ListBoxItem.IsSelectedProperty, true));
        unfocusedSelection.Conditions.Add(
            new System.Windows.Condition(Selector.IsSelectionActiveProperty, false));
        unfocusedSelection.Setters.Add(new Setter(
            Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(35, 57, 82)),
            "RowBackground"));
        unfocusedSelection.Setters.Add(new Setter(Control.ForegroundProperty, StudioTheme.TextBrush));
        template.Triggers.Add(unfocusedSelection);

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }
}
