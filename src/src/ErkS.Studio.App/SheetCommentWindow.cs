using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Comments placed on one sheet of the album.
///
/// The sheet is drawn as an image rather than shown through the PDF viewer,
/// because a comment has to point at a place on the drawing and the viewer
/// gives away neither its surface nor the point that was clicked. A pin is
/// stored as a fraction of the page, so it lands on the same part of the
/// drawing whatever size it is drawn at and whatever format the sheet is later
/// re-issued in.
/// </summary>
internal sealed class SheetCommentWindow : Window
{
    private const double LogicalPageWidth = 1180d;
    private const double PinSize = 30d;

    private readonly StudioAccountService account;
    private readonly PdfPageImageCache imageCache;
    private readonly SheetRecord sheet;
    private readonly string projectId;
    private readonly string pageIdentity;
    private readonly string pageLabel;
    private readonly int pageNumber;
    private readonly bool canWrite;

    private readonly Grid pageHost = new();
    private readonly Image pageImage = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    private readonly Canvas pinLayer = new() { Background = Brushes.Transparent };
    private readonly StackPanel threadPanel = new();
    private readonly TextBlock summaryText = new()
    {
        FontSize = 12,
        Foreground = StudioTheme.MutedTextBrush,
        Margin = new Thickness(0, 4, 0, 0),
    };
    private readonly TextBlock hintText = new()
    {
        FontSize = 12,
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10),
    };
    private readonly ComboBox kindBox = new() { Width = 190 };
    private readonly Button placeButton = StudioWidgets.CreatePrimaryButton("Хуудсанд коммент тавих");

    private IReadOnlyList<StudioSheetComment> comments = [];
    private string currentUserEmail = "";
    private string selectedCommentId = "";
    private bool placing;
    private bool busy;

    public SheetCommentWindow(
        StudioAccountService account,
        PdfPageImageCache imageCache,
        SheetRecord sheet,
        string projectId,
        string pageNumberText,
        string pageTitle,
        int pageNumber,
        bool canWrite)
    {
        this.account = account ?? throw new ArgumentNullException(nameof(account));
        this.imageCache = imageCache ?? throw new ArgumentNullException(nameof(imageCache));
        this.sheet = sheet ?? throw new ArgumentNullException(nameof(sheet));
        this.projectId = (projectId ?? "").Trim();
        this.pageNumber = pageNumber;
        this.canWrite = canWrite;
        pageIdentity = StudioSheetCommentRules.PageIdentity(sheet);
        pageLabel = StudioSheetCommentRules.PageLabel(pageNumberText, pageTitle);

        Title = "Хуудасны коммент — " + pageLabel;
        Width = 1500;
        Height = 940;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = StudioTheme.WindowBackgroundBrush;
        Content = BuildLayout();
        StudioTheme.Apply(this);

        Loaded += async (_, _) =>
        {
            await LoadPageImageAsync();
            await ReloadAsync();
        };
    }

    private UIElement BuildLayout()
    {
        var root = new DockPanel { Margin = new Thickness(16) };
        UIElement header = BuildHeader();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var threads = new Border
        {
            Width = 400,
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Margin = new Thickness(16, 0, 0, 0),
            Padding = new Thickness(14, 12, 8, 12),
            Child = new ScrollViewer
            {
                Content = threadPanel,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            },
        };
        DockPanel.SetDock(threads, Dock.Right);
        root.Children.Add(threads);

        pageHost.Children.Add(pageImage);
        pageHost.Children.Add(pinLayer);
        pageHost.HorizontalAlignment = HorizontalAlignment.Center;
        pageHost.VerticalAlignment = VerticalAlignment.Top;
        pinLayer.MouseLeftButtonUp += OnPageClicked;

        var page = new Border
        {
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16),
            Child = new ScrollViewer
            {
                Content = pageHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };
        root.Children.Add(page);
        return root;
    }

    private UIElement BuildHeader()
    {
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };
        kindBox.ItemsSource = StudioSheetCommentRules.Kinds
            .Select(kind => new KindChoice(kind, StudioSheetCommentRules.KindLabel(kind)))
            .ToList();
        kindBox.DisplayMemberPath = nameof(KindChoice.Label);
        kindBox.SelectedIndex = StudioSheetCommentRules.Kinds.ToList()
            .IndexOf(StudioSheetCommentRules.KindNote);
        kindBox.Margin = new Thickness(0, 0, 10, 0);
        kindBox.VerticalAlignment = VerticalAlignment.Center;
        actions.Children.Add(kindBox);
        placeButton.Click += (_, _) => TogglePlacing();
        placeButton.IsEnabled = canWrite;
        placeButton.ToolTip = canWrite
            ? "Дараа нь зураг дээр дарж комментын байрлалыг сонгоно"
            : "Энэ төсөлд коммент бичих эрхгүй байна";
        actions.Children.Add(placeButton);
        DockPanel.SetDock(actions, Dock.Right);
        header.Children.Add(actions);

        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        words.Children.Add(new TextBlock
        {
            Text = pageLabel.Length == 0 ? "Хуудас" : pageLabel,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        words.Children.Add(summaryText);
        header.Children.Add(words);

        return header;
    }

    private void TogglePlacing()
    {
        placing = !placing;
        pinLayer.Cursor = placing ? Cursors.Cross : Cursors.Arrow;
        placeButton.Content = placing ? "Байрлал сонгож байна… (болих)" : "Хуудсанд коммент тавих";
        hintText.Text = placing
            ? "Зураг дээр коммент тавих цэгээ дарна уу."
            : "";
        RenderThreads();
    }

    private async Task LoadPageImageAsync()
    {
        BitmapSource? bitmap = await imageCache.GetPageAsync(
            sheet.PdfPath,
            Math.Max(1, sheet.Entry.PdfPageNumber),
            2200,
            CancellationToken.None);
        if (bitmap is null)
        {
            pageHost.Width = LogicalPageWidth;
            pageHost.Height = LogicalPageWidth * 297d / 420d;
            return;
        }

        pageImage.Source = bitmap;
        // The host takes the page's own proportions so the image fills it
        // exactly - a pin's fraction can then be multiplied by the host's size
        // with no letterbox to correct for.
        double aspect = bitmap.PixelHeight <= 0
            ? 297d / 420d
            : bitmap.PixelHeight / (double)bitmap.PixelWidth;
        pageHost.Width = LogicalPageWidth;
        pageHost.Height = Math.Max(120d, LogicalPageWidth * aspect);
        RenderPins();
    }

    private async Task ReloadAsync()
    {
        if (pageIdentity.Length == 0)
        {
            summaryText.Text = "Энэ хуудсанд коммент бэхлэх тогтвортой дугаар алга.";
            placeButton.IsEnabled = false;
            return;
        }

        try
        {
            StudioSheetCommentList list = await account.ListSheetCommentsAsync(projectId, pageIdentity);
            currentUserEmail = list.CurrentUserEmail;
            Apply(list);
        }
        catch (Exception exception) when (
            exception is StudioAccountException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            summaryText.Text = "Комментыг уншиж чадсангүй: " + exception.Message;
        }
    }

    private void Apply(StudioSheetCommentList list)
    {
        comments = StudioSheetCommentRules.InReadingOrder(list.Comments).ToList();
        int open = comments.Count(item => !StudioSheetCommentRules.IsResolved(item.Status));
        int change = comments.Count(item =>
            !StudioSheetCommentRules.IsResolved(item.Status) &&
            StudioSheetCommentRules.Normalize(item.Kind) == StudioSheetCommentRules.KindChangeRequired);
        summaryText.Text = comments.Count == 0
            ? "Энэ хуудсанд коммент алга."
            : $"{comments.Count} коммент · {open} нээлттэй · {change} засах шаардлагатай";
        RenderPins();
        RenderThreads();
    }

    private void RenderPins()
    {
        pinLayer.Children.Clear();
        if (pageHost.Width <= 0 || pageHost.Height <= 0)
            return;

        int index = 1;
        foreach (StudioSheetComment comment in comments)
        {
            bool resolved = StudioSheetCommentRules.IsResolved(comment.Status);
            bool selected = comment.CommentId.Equals(selectedCommentId, StringComparison.OrdinalIgnoreCase);
            var pin = new Border
            {
                Width = PinSize,
                Height = PinSize,
                CornerRadius = new CornerRadius(PinSize / 2),
                Background = StudioSheetCommentRules.KindBrush(comment.Kind),
                BorderBrush = selected ? Brushes.White : StudioTheme.PanelBrush,
                BorderThickness = new Thickness(selected ? 3 : 2),
                Opacity = resolved ? 0.45 : 1d,
                Cursor = Cursors.Hand,
                ToolTip = StudioSheetCommentRules.KindLabel(comment.Kind) + " · " + comment.AuthorDisplayName,
                Child = new TextBlock
                {
                    Text = index.ToString(),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            string commentId = comment.CommentId;
            pin.MouseLeftButtonUp += (_, args) =>
            {
                args.Handled = true;
                selectedCommentId = commentId;
                RenderPins();
                RenderThreads();
            };
            Canvas.SetLeft(pin, (comment.AnchorX * pageHost.Width) - (PinSize / 2));
            Canvas.SetTop(pin, (comment.AnchorY * pageHost.Height) - (PinSize / 2));
            pinLayer.Children.Add(pin);
            index++;
        }
    }

    private void OnPageClicked(object sender, MouseButtonEventArgs args)
    {
        if (!placing || !canWrite || pageHost.Width <= 0 || pageHost.Height <= 0)
            return;

        Point point = args.GetPosition(pinLayer);
        double x = Math.Clamp(point.X / pageHost.Width, 0d, 1d);
        double y = Math.Clamp(point.Y / pageHost.Height, 0d, 1d);
        placing = false;
        pinLayer.Cursor = Cursors.Arrow;
        placeButton.Content = "Хуудсанд коммент тавих";
        hintText.Text = "";
        RenderThreads(newAnchor: (x, y));
    }

    private void RenderThreads((double X, double Y)? newAnchor = null)
    {
        threadPanel.Children.Clear();
        if (hintText.Text.Length > 0)
            threadPanel.Children.Add(hintText);

        if (newAnchor is not null)
            threadPanel.Children.Add(BuildComposer(newAnchor.Value));

        if (comments.Count == 0 && newAnchor is null)
        {
            threadPanel.Children.Add(StudioWidgets.CreateHint(
                canWrite
                    ? "Энэ хуудсанд коммент алга. «Хуудсанд коммент тавих» дараад зураг дээрээ цэгээ сонгоно уу."
                    : "Энэ хуудсанд коммент алга."));
            return;
        }

        int index = 1;
        foreach (StudioSheetComment comment in comments)
        {
            threadPanel.Children.Add(BuildThreadCard(comment, index));
            index++;
        }
    }

    private UIElement BuildComposer((double X, double Y) anchor)
    {
        var card = new Border
        {
            Background = StudioTheme.InputBrush,
            BorderBrush = StudioTheme.AccentBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 6, 12),
        };
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "Шинэ коммент",
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            Margin = new Thickness(0, 0, 0, 8),
        });
        var input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 90,
            MaxLength = StudioSheetCommentRules.MaximumBodyLength,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        body.Children.Add(input);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        Button cancel = StudioWidgets.CreateButton("Болих");
        cancel.Margin = new Thickness(0, 0, 8, 0);
        cancel.Click += (_, _) => RenderThreads();
        actions.Children.Add(cancel);
        Button save = StudioWidgets.CreatePrimaryButton("Нэмэх");
        save.Click += async (_, _) => await AddAsync(anchor, input.Text);
        actions.Children.Add(save);
        body.Children.Add(actions);

        card.Child = body;
        input.Focus();
        return card;
    }

    private UIElement BuildThreadCard(StudioSheetComment comment, int index)
    {
        bool resolved = StudioSheetCommentRules.IsResolved(comment.Status);
        bool selected = comment.CommentId.Equals(selectedCommentId, StringComparison.OrdinalIgnoreCase);
        var card = new Border
        {
            Background = selected ? StudioTheme.InputBrush : StudioTheme.PanelAltBrush,
            BorderBrush = selected ? StudioTheme.AccentBrush : StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 6, 10),
            Opacity = resolved ? 0.75 : 1d,
            Cursor = Cursors.Hand,
        };
        string commentId = comment.CommentId;
        card.MouseLeftButtonUp += (_, _) =>
        {
            selectedCommentId = commentId;
            RenderPins();
            RenderThreads();
        };

        var body = new StackPanel();

        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(new Border
        {
            Width = 22,
            Height = 22,
            CornerRadius = new CornerRadius(11),
            Background = StudioSheetCommentRules.KindBrush(comment.Kind),
            Margin = new Thickness(0, 0, 8, 0),
            Child = new TextBlock
            {
                Text = index.ToString(),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        });
        top.Children.Add(new TextBlock
        {
            Text = StudioSheetCommentRules.KindLabel(comment.Kind),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioSheetCommentRules.KindBrush(comment.Kind),
            VerticalAlignment = VerticalAlignment.Center,
        });
        top.Children.Add(new TextBlock
        {
            Text = "  ·  " + StudioSheetCommentRules.StatusLabel(comment.Status),
            FontSize = 11,
            Foreground = resolved ? StudioTheme.SuccessBrush : StudioTheme.MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
        });
        body.Children.Add(top);

        body.Children.Add(new TextBlock
        {
            Text = comment.Body,
            FontSize = 13,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 9, 0, 0),
        });
        body.Children.Add(new TextBlock
        {
            Text = comment.AuthorDisplayName +
                (comment.AuthorRoleLabel.Length == 0 ? "" : " · " + comment.AuthorRoleLabel) +
                " · " + comment.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            FontSize = 10.5,
            Foreground = StudioTheme.FaintTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 7, 0, 0),
        });

        if (resolved && comment.ResolvedByDisplayName.Length > 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = "Шийдсэн: " + comment.ResolvedByDisplayName,
                FontSize = 10.5,
                Foreground = StudioTheme.SuccessBrush,
                Margin = new Thickness(0, 4, 0, 0),
            });
        }

        foreach (StudioSheetCommentReply reply in comment.Replies)
            body.Children.Add(BuildReply(reply));

        if (selected && canWrite)
            body.Children.Add(BuildReplyBox(comment));
        if (selected && comment.CanManage)
            body.Children.Add(BuildManageRow(comment, resolved));

        card.Child = body;
        return card;
    }

    private static UIElement BuildReply(StudioSheetCommentReply reply)
    {
        var card = new Border
        {
            Background = StudioTheme.PanelBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Margin = new Thickness(14, 8, 0, 0),
        };
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = reply.Body,
            FontSize = 12,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(new TextBlock
        {
            Text = reply.AuthorDisplayName + " · " +
                reply.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"),
            FontSize = 10,
            Foreground = StudioTheme.FaintTextBrush,
            Margin = new Thickness(0, 5, 0, 0),
        });
        card.Child = body;
        return card;
    }

    private UIElement BuildReplyBox(StudioSheetComment comment)
    {
        var panel = new StackPanel { Margin = new Thickness(14, 10, 0, 0) };
        var input = new TextBox
        {
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 54,
            MaxLength = StudioSheetCommentRules.MaximumBodyLength,
        };
        panel.Children.Add(input);
        Button send = StudioWidgets.CreateButton("Хариулах");
        send.HorizontalAlignment = HorizontalAlignment.Right;
        send.Margin = new Thickness(0, 8, 0, 0);
        send.Click += async (_, _) => await ReplyAsync(comment.CommentId, input.Text);
        panel.Children.Add(send);
        return panel;
    }

    private UIElement BuildManageRow(StudioSheetComment comment, bool resolved)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0),
        };
        Button toggle = StudioWidgets.CreateButton(resolved ? "Дахин нээх" : "Шийдэгдсэн болгох");
        toggle.Margin = new Thickness(0, 0, 8, 0);
        toggle.Click += async (_, _) => await SetStatusAsync(
            comment.CommentId,
            resolved ? StudioSheetCommentRules.StatusOpen : StudioSheetCommentRules.StatusResolved);
        row.Children.Add(toggle);
        Button remove = StudioWidgets.CreateButton("Устгах");
        remove.Click += async (_, _) => await DeleteAsync(comment);
        row.Children.Add(remove);
        return row;
    }

    private async Task AddAsync((double X, double Y) anchor, string body)
    {
        string text = StudioSheetCommentRules.CleanBody(body);
        if (text.Length == 0 || busy)
            return;

        string kind = kindBox.SelectedItem is KindChoice choice
            ? choice.Kind
            : StudioSheetCommentRules.KindNote;
        await RunAsync(() => account.AddSheetCommentAsync(
            projectId,
            new StudioSheetCommentCreateRequest
            {
                PageIdentity = pageIdentity,
                PageLabel = pageLabel,
                PageNumber = pageNumber,
                AnchorX = anchor.X,
                AnchorY = anchor.Y,
                Kind = kind,
                Body = text,
            }));
    }

    private async Task ReplyAsync(string commentId, string body)
    {
        string text = StudioSheetCommentRules.CleanBody(body);
        if (text.Length == 0 || busy)
            return;

        await RunAsync(() => account.ReplyToSheetCommentAsync(projectId, commentId, text));
    }

    private async Task SetStatusAsync(string commentId, string status)
    {
        if (busy)
            return;

        await RunAsync(() => account.SetSheetCommentStatusAsync(projectId, commentId, status));
    }

    private async Task DeleteAsync(StudioSheetComment comment)
    {
        if (busy)
            return;

        if (MessageBox.Show(
                this,
                "Энэ комментыг устгах уу? Хариултууд нь хамт устана.",
                "Коммент устгах",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning) != MessageBoxResult.OK)
        {
            return;
        }

        selectedCommentId = "";
        await RunAsync(() => account.DeleteSheetCommentAsync(projectId, comment.CommentId));
    }

    private async Task RunAsync(Func<Task<StudioSheetCommentList>> operation)
    {
        busy = true;
        try
        {
            Apply(await operation());
        }
        catch (Exception exception) when (
            exception is StudioAccountException or System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            summaryText.Text = "Үйлдэл гүйцэтгэгдсэнгүй: " + exception.Message;
        }
        finally
        {
            busy = false;
        }
    }

    private sealed record KindChoice(string Kind, string Label);
}
