using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Renders the gallery for real, because the regression it guards was invisible
/// to every other kind of check.
/// </summary>
/// <remarks>
/// The list was correctly configured, correctly populated, and drew nothing:
/// the container template presented its content through a GridViewRowPresenter
/// that no longer had any columns. Nothing about the ItemsSource, the item
/// objects or the templates was wrong, so no test of those could have failed.
/// The only thing that was wrong was what appeared on screen, so that is what
/// this measures.
/// </remarks>
public sealed class StudioGalleryRenderTests
{
    [Fact]
    public void SixSheetsDrawSixCards()
    {
        string report = RenderGallery(
            itemCount: 6,
            containerStyle: StudioGalleryList.CreateItemContainerStyle,
            inspect: Describe);

        // The info bar said "Хүлээн авсан: 6 sheet" while the pane was empty.
        // A count and a surface that disagree is the failure being pinned here.
        Assert.StartsWith("cards=6 ", report);
    }

    [Fact]
    public void EachCardHasRealSizeRatherThanCollapsingToNothing()
    {
        Size card = RenderGallery(
            itemCount: 3,
            containerStyle: StudioGalleryList.CreateItemContainerStyle,
            inspect: list =>
            {
                ContentPresenter first = Cards(list).First();
                return new Size(first.ActualWidth, first.ActualHeight);
            });

        // A presenter with nothing to present still occupies the tree and still
        // reports zero by zero, which is exactly how the blank pane looked.
        // Counting containers alone would have passed on the bug.
        Assert.True(card.Width > 40, $"card width was {card.Width}");
        Assert.True(card.Height > 20, $"card height was {card.Height}");
    }

    [Fact]
    public void TheTableEraContainerIsWhatDrewNothing()
    {
        // Kept as the counter-example: this is the container the list carried
        // out of its table days, rendered against the same items. If a future
        // change reaches for a row presenter in a viewless list again, the
        // contrast here says what happens.
        Size row = RenderGallery(
            itemCount: 3,
            containerStyle: BuildTableEraContainerStyle,
            inspect: list =>
            {
                GridViewRowPresenter presenter =
                    FindDescendants<GridViewRowPresenter>(list).First();
                return new Size(presenter.ActualWidth, presenter.ActualHeight);
            });

        Assert.Equal(0, row.Width);
        Assert.Equal(0, row.Height);
    }

    /// <summary>
    /// Reports what the list actually did, so a failure names the reason
    /// instead of only the count.
    /// </summary>
    private static string Describe(ListView list) =>
        $"cards={Cards(list).Count} template={(list.Template is null ? "none" : "set")} "
        + $"containers={list.ItemContainerGenerator.Status} "
        + $"size={list.ActualWidth}x{list.ActualHeight} items={list.Items.Count}";

    private static List<ContentPresenter> Cards(ListView list) =>
        FindDescendants<ContentPresenter>(list)
            .Where(presenter => presenter.TemplatedParent is ListViewItem)
            .ToList();

    /// <summary>
    /// Builds the gallery, lays it out for real, and hands it to
    /// <paramref name="inspect"/> while it is still standing.
    /// </summary>
    private static T RenderGallery<T>(
        int itemCount,
        Func<Style> containerStyle,
        Func<ListView, T> inspect) =>
        OnStaThread(() =>
        {
            var list = new ListView
            {
                View = null,
                // Built here rather than by the caller: a Style belongs to the
                // thread that created it, and the test method does not run on
                // this one.
                ItemContainerStyle = containerStyle(),
                ItemsPanel = StudioGalleryList.CreateWrapPanel(),
                ItemTemplate = BuildCardTemplate(),
                ItemsSource = Enumerable
                    .Range(1, itemCount)
                    .Select(number => new SheetCardStandIn($"A-{number:00}", "Erin_Apartment_Type_1"))
                    .ToList(),
            };

            // A ListView only picks up its default template once it belongs to
            // a window - unlike a Button, which templates itself anywhere. Left
            // unrooted it has no template at all, generates no containers, and
            // draws nothing: the exact appearance of the bug, arrived at for an
            // entirely unrelated reason. So the test would have "reproduced"
            // the failure without ever touching the code under test.
            var window = new Window
            {
                Width = 700,
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

                // Waiting for the condition itself rather than for a fixed
                // number of dispatcher drains. A drain count is a guess about
                // how much work arrives before the list is ready, and a guess
                // that is occasionally low turns this into a test that reports
                // an empty gallery when the gallery is fine - the same reading
                // as the bug, from an unrelated cause. The timeout below fails
                // loudly instead.
                PumpUntil(
                    list.Dispatcher,
                    () => list.IsLoaded
                        && list.ActualWidth > 0
                        && list.ItemContainerGenerator.Status
                            == GeneratorStatus.ContainersGenerated,
                    TimeSpan.FromSeconds(20));
                window.UpdateLayout();

                return inspect(list);
            }
            finally
            {
                window.Close();
            }
        });

    /// <summary>
    /// Runs the render thread's own message loop until <paramref name="done"/>
    /// holds, and gives up loudly rather than letting the caller measure a list
    /// that has not finished arriving.
    /// </summary>
    private static void PumpUntil(Dispatcher dispatcher, Func<bool> done, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;

        while (!done())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException(
                    $"The gallery had not finished rendering after {timeout.TotalSeconds:0} seconds.");
            }

            var frame = new DispatcherFrame();
            dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    /// <summary>
    /// The same shape as the real sheet card: a fixed-width block with a
    /// picture frame and a line of text under it.
    /// </summary>
    private static DataTemplate BuildCardTemplate()
    {
        var card = new FrameworkElementFactory(typeof(StackPanel));
        card.SetValue(FrameworkElement.WidthProperty, 186.0);

        var frame = new FrameworkElementFactory(typeof(Border));
        frame.SetValue(FrameworkElement.HeightProperty, 124.0);
        frame.SetValue(Border.BackgroundProperty, Brushes.Gray);
        card.AppendChild(frame);

        var title = new FrameworkElementFactory(typeof(TextBlock));
        title.SetBinding(TextBlock.TextProperty, new Binding(nameof(SheetCardStandIn.Title)));
        card.AppendChild(title);

        return new DataTemplate(typeof(SheetCardStandIn)) { VisualTree = card };
    }

    private static Style BuildTableEraContainerStyle()
    {
        var style = new Style(typeof(ListViewItem));
        var template = new ControlTemplate(typeof(ListViewItem));

        var host = new FrameworkElementFactory(typeof(Border));
        var row = new FrameworkElementFactory(typeof(GridViewRowPresenter));
        row.SetBinding(
            GridViewRowPresenter.ContentProperty,
            new Binding(nameof(ContentControl.Content))
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent),
            });
        row.SetBinding(
            GridViewRowPresenter.ColumnsProperty,
            new Binding($"{nameof(ListView.View)}.{nameof(GridView.Columns)}")
            {
                RelativeSource = new RelativeSource(
                    RelativeSourceMode.FindAncestor,
                    typeof(ListView),
                    1),
            });
        host.AppendChild(row);
        template.VisualTree = host;

        style.Setters.Add(new Setter(Control.TemplateProperty, template));
        return style;
    }

    private static IEnumerable<T> FindDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (child is T match)
                yield return match;

            foreach (T nested in FindDescendants<T>(child))
                yield return nested;
        }
    }

    /// <summary>
    /// WPF controls only live on a single-threaded-apartment thread, and xunit
    /// does not run tests on one.
    /// </summary>
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
            throw new InvalidOperationException("Rendering the gallery failed.", failure);

        return result;
    }

    private sealed record SheetCardStandIn(string Number, string Title);
}
