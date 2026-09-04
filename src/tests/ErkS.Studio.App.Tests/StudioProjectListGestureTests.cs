using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Renders a real list and hit-tests real points, because the thing under test
/// is what a click lands on. A ListView raises MouseDoubleClick for a click
/// anywhere inside itself, and the list used to answer that by opening whatever
/// was selected - so a person clicking about the catalogue had a project open
/// at them. No test of the items, the templates or the selection could have
/// caught that; only a click that lands on nothing can.
/// </summary>
public sealed class StudioProjectListGestureTests
{
    private sealed record CardStandIn(string Name);

    [Fact]
    public void AClickOnACardResolvesToThatCard()
    {
        object? item = OnRenderedList(list =>
        {
            ListBoxItem container = Containers(list)[1];
            return StudioProjectListGesture.ItemUnder(list, HitTestCentreOf(list, container));
        });

        Assert.Equal(new CardStandIn("B"), item);
    }

    [Fact]
    public void AClickOnTheCardsOwnTitleStillResolvesToTheCard()
    {
        // The click a person actually makes lands on the title, several
        // elements below the container - not on the container itself, which is
        // the only thing a walk of one step would find.
        object? item = OnRenderedList(list =>
        {
            TextBlock title = Descendants(Containers(list)[0])
                .OfType<TextBlock>()
                .First();
            return StudioProjectListGesture.ItemUnder(list, HitTestCentreOf(list, title));
        });

        Assert.Equal(new CardStandIn("A"), item);
    }

    [Fact]
    public void AClickOnTheEmptySpaceOpensNothing_ThoughTheListItselfWasHit()
    {
        // The defect. The empty space beside and below the cards belongs to the
        // list, so a double-click there does reach it - the first assert proves
        // the click was not simply missing the control - and it must still mean
        // nothing rather than "open the selected project".
        (bool listWasHit, object? item) = OnRenderedList(list =>
        {
            // Something IS selected, which is the whole point: the old answer
            // to a click on nothing was "open the selection".
            list.SelectedIndex = 0;
            var empty = new Point(list.ActualWidth - 40, list.ActualHeight - 40);
            IInputElement? hit = list.InputHitTest(empty);
            return (hit is not null, StudioProjectListGesture.ItemUnder(list, hit));
        });

        Assert.True(listWasHit, "the empty-space click did not land on the list at all");
        Assert.Null(item);
    }

    [Fact]
    public void TheActionsHandleIsNotTheProjectItSitsOn()
    {
        // The three-dot handle is inside the card, so without being named it
        // reads as a click on the project. Its clicks mean "menu".
        object? item = OnRenderedList(list =>
        {
            FrameworkElement handle = Descendants(Containers(list)[0])
                .OfType<FrameworkElement>()
                .First(element => (element.Tag as string) == StudioProjectListGesture.ActionsHandleTag);
            return StudioProjectListGesture.ItemUnder(list, HitTestCentreOf(list, handle));
        });

        Assert.Null(item);
    }

    /// <summary>
    /// Hit-tests the centre of <paramref name="element"/> the way a mouse does,
    /// rather than handing the element straight to the resolver: a click gives
    /// back whatever is topmost at that point, which is the whole question.
    /// </summary>
    private static IInputElement? HitTestCentreOf(ListView list, FrameworkElement element)
    {
        Point centre = element
            .TransformToAncestor(list)
            .Transform(new Point(element.ActualWidth / 2, element.ActualHeight / 2));
        return list.InputHitTest(centre);
    }

    private static List<ListBoxItem> Containers(ListView list) =>
        Descendants(list).OfType<ListBoxItem>().ToList();

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            yield return child;
            foreach (DependencyObject nested in Descendants(child))
                yield return nested;
        }
    }

    private static T OnRenderedList<T>(Func<ListView, T> inspect) =>
        OnStaThread(() =>
        {
            var list = new ListView
            {
                View = null,
                // The app's own list is transparent, and a transparent brush is
                // still hit-testable - which is exactly why its empty space
                // takes clicks.
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                ItemsPanel = WrapPanelTemplate(),
                ItemTemplate = CardTemplate(),
                ItemsSource = new List<CardStandIn>
                {
                    new("A"),
                    new("B"),
                    new("C"),
                },
            };

            var window = new Window
            {
                Width = 760,
                Height = 520,
                Content = list,
                WindowStyle = WindowStyle.None,
                ShowInTaskbar = false,
                ShowActivated = false,
                Left = -32000,
                Top = -32000,
            };

            try
            {
                window.Show();
                PumpUntil(
                    list.Dispatcher,
                    () => list.IsLoaded
                        && list.ActualWidth > 0
                        && list.ItemContainerGenerator.Status == GeneratorStatus.ContainersGenerated,
                    TimeSpan.FromSeconds(20));
                window.UpdateLayout();
                return inspect(list);
            }
            finally
            {
                window.Close();
            }
        });

    private static ItemsPanelTemplate WrapPanelTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(WrapPanel));
        panel.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
        return new ItemsPanelTemplate { VisualTree = panel };
    }

    /// <summary>
    /// The same shape as a project card: a fixed tile, a title, and the
    /// three-dot handle in its corner.
    /// </summary>
    private static DataTemplate CardTemplate()
    {
        var card = new FrameworkElementFactory(typeof(Grid));
        card.SetValue(FrameworkElement.WidthProperty, 200d);
        card.SetValue(FrameworkElement.HeightProperty, 150d);

        var plate = new FrameworkElementFactory(typeof(Border));
        plate.SetValue(Border.BackgroundProperty, Brushes.Gray);
        card.AppendChild(plate);

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(
            TextBlock.TextProperty,
            new System.Windows.Data.Binding(nameof(CardStandIn.Name)));
        title.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        title.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        card.AppendChild(title);

        var handle = new FrameworkElementFactory(typeof(Border));
        handle.SetValue(FrameworkElement.WidthProperty, 26d);
        handle.SetValue(FrameworkElement.HeightProperty, 26d);
        handle.SetValue(Border.BackgroundProperty, Brushes.DarkSlateGray);
        handle.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Right);
        handle.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Top);
        handle.SetValue(FrameworkElement.TagProperty, StudioProjectListGesture.ActionsHandleTag);
        card.AppendChild(handle);

        return new DataTemplate(typeof(CardStandIn)) { VisualTree = card };
    }

    private static void PumpUntil(Dispatcher dispatcher, Func<bool> done, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (!done())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"The list had not rendered after {timeout.TotalSeconds:0} seconds.");
            }

            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    private static T OnStaThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new InvalidOperationException("Rendering the project list failed.", failure);

        return result;
    }
}
