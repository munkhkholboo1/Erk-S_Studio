using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private static readonly SolidColorBrush ProjectChatAccentBrush =
        new(Color.FromRgb(53, 213, 208));
    private static readonly SolidColorBrush ProjectChatAccentInkBrush =
        new(Color.FromRgb(0, 22, 21));
    private static readonly SolidColorBrush ProjectChatAccentBorderBrush =
        new(Color.FromArgb(82, 53, 213, 208));
    private const double ProjectChatRightOffset = 22;
    private const double ProjectChatBottomOffset = 34;
    private const double ProjectChatEditorBottomOffset = 104;

    private readonly Grid projectChatWidgetHost = new()
    {
        Width = 380,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Visibility = Visibility.Collapsed,
    };
    private readonly Popup projectChatPopup = new()
    {
        AllowsTransparency = true,
        Focusable = true,
        Placement = PlacementMode.Custom,
        PopupAnimation = PopupAnimation.Fade,
        StaysOpen = true,
    };
    private readonly Grid projectChatLauncherLayer = new()
    {
        Width = 124,
        Height = 60,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
    };
    private readonly Border projectChatLauncher = new()
    {
        Width = 112,
        Height = 54,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Background = ProjectChatAccentBrush,
        BorderBrush = ProjectChatAccentBorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(27),
        Cursor = Cursors.Hand,
        Focusable = true,
        ToolTip = "Төслийн чат",
    };
    private readonly TextBlock projectChatLauncherBadgeText = new()
    {
        FontSize = 9,
        FontWeight = FontWeights.Bold,
        Foreground = Brushes.White,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Border projectChatLauncherBadge = new()
    {
        MinWidth = 18,
        Height = 18,
        Padding = new Thickness(4, 0, 4, 0),
        Background = StudioTheme.DangerBrush,
        CornerRadius = new CornerRadius(9),
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Top,
        Margin = new Thickness(0, -5, -4, 0),
        Visibility = Visibility.Collapsed,
        IsHitTestVisible = false,
    };
    private readonly Border projectChatDock = new()
    {
        Width = 380,
        Height = 580,
        HorizontalAlignment = HorizontalAlignment.Right,
        VerticalAlignment = VerticalAlignment.Bottom,
        Background = StudioTheme.PanelBrush,
        BorderBrush = StudioTheme.BorderBrush,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(14),
        Visibility = Visibility.Collapsed,
    };
    private readonly Grid projectChatMemberListView = new();
    private readonly Grid projectChatConversationView = new() { Visibility = Visibility.Collapsed };
    private readonly StackPanel projectChatConversationListPanel = new();
    private readonly StackPanel projectChatMessagesPanel = new();
    private readonly TextBlock projectChatProjectText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        FontSize = StudioTheme.HintFontSize,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock projectChatPeerNameText = new()
    {
        Foreground = StudioTheme.TextBrush,
        FontWeight = FontWeights.SemiBold,
        FontSize = 14,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly TextBlock projectChatPeerRoleText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        FontSize = 11,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };
    private readonly Grid projectChatPeerAvatarHost = new() { Width = 36, Height = 36 };
    private readonly TextBlock projectChatStatusText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        FontSize = 11,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(14, 5, 14, 10),
    };
    private readonly TextBlock projectChatAttachmentText = new()
    {
        Foreground = StudioTheme.AccentSoftBrush,
        FontSize = 11,
        TextTrimming = TextTrimming.CharacterEllipsis,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Grid projectChatAttachmentRow = new()
    {
        Margin = new Thickness(12, 0, 12, 6),
        Visibility = Visibility.Collapsed,
    };
    private readonly TextBox projectChatComposer = new()
    {
        AcceptsReturn = true,
        AcceptsTab = false,
        Focusable = true,
        IsReadOnly = false,
        IsTabStop = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalContentAlignment = VerticalAlignment.Center,
        MinHeight = 38,
        MaxHeight = 82,
        MaxLength = StudioProjectChatRules.MaxBodyLength,
        Margin = new Thickness(6, 0, 6, 0),
        ToolTip = "Мессеж",
    };
    private readonly ScrollViewer projectChatScrollHost = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        Padding = new Thickness(12, 10, 12, 10),
    };
    private readonly Dictionary<string, ImageSource> projectChatAvatarCache =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer projectChatEmojiHoldTimer =
        new() { Interval = TimeSpan.FromMilliseconds(460) };
    private CancellationTokenSource? projectChatLoadCancellation;
    private string projectChatAttachmentPath = "";
    private string projectChatBoundProjectId = "";
    private string projectChatSelectedPeerEmail = "";
    private string projectChatRenderKey = "";
    private bool projectChatRefreshInProgress;
    private bool projectChatSendInProgress;
    private bool projectChatEmojiHoldTriggered;
    private IReadOnlyList<string> projectChatEmojiChoices = StudioEmojiCatalog.Choices;

    private UIElement BuildProjectChatWidget()
    {
        projectChatLauncher.Child = BuildProjectChatLauncherContent();
        projectChatLauncher.MouseLeftButtonUp += async (_, _) => await OpenProjectChatWidgetAsync();
        projectChatLauncher.KeyDown += async (_, args) =>
        {
            if (args.Key is not (Key.Enter or Key.Space))
                return;

            args.Handled = true;
            await OpenProjectChatWidgetAsync();
        };
        System.Windows.Automation.AutomationProperties.SetName(
            projectChatLauncher,
            "Төслийн чат");
        projectChatLauncherBadge.Child = projectChatLauncherBadgeText;
        projectChatLauncherLayer.Children.Add(projectChatLauncher);
        projectChatLauncherLayer.Children.Add(projectChatLauncherBadge);

        var dockLayout = new Grid();
        dockLayout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        dockLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var views = new Grid();
        views.Children.Add(BuildProjectChatMemberList());
        views.Children.Add(BuildProjectChatConversation());
        Grid.SetRow(views, 0);
        dockLayout.Children.Add(views);
        Grid.SetRow(projectChatStatusText, 1);
        dockLayout.Children.Add(projectChatStatusText);
        projectChatDock.Child = dockLayout;

        projectChatWidgetHost.Children.Add(projectChatLauncherLayer);
        projectChatWidgetHost.Children.Add(projectChatDock);
        projectChatEmojiHoldTimer.Tick += (_, _) =>
        {
            projectChatEmojiHoldTimer.Stop();
            projectChatEmojiHoldTriggered = true;
            OpenProjectChatEmojiMenu();
        };
        UpdateProjectChatAttachmentText();
        return projectChatWidgetHost;
    }

    private UIElement BuildProjectChatLauncherContent()
    {
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(new Image
        {
            Source = SvgIconLoader.TryLoad(StudioWidgets.GetAssetPath("logo-erks.svg")),
            Width = 20,
            Height = 20,
            Margin = new Thickness(0, 0, 8, 0),
        });
        content.Children.Add(new TextBlock
        {
            Text = "Chat",
            Foreground = ProjectChatAccentInkBrush,
            FontSize = 14,
            FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        return content;
    }

    private UIElement BuildProjectChatMemberList()
    {
        projectChatMemberListView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        projectChatMemberListView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid
        {
            Margin = new Thickness(16, 14, 10, 10),
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(new TextBlock
        {
            Text = "Төслийн чат",
            Foreground = StudioTheme.TextBrush,
            FontWeight = FontWeights.SemiBold,
            FontSize = 17,
        });
        heading.Children.Add(projectChatProjectText);
        header.Children.Add(heading);
        Button close = StudioWidgets.CreateGlyphButton("\uE711", "Чатыг хаах");
        close.Margin = new Thickness(6, 0, 0, 0);
        close.Click += (_, _) => CloseProjectChatWidget();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        projectChatMemberListView.Children.Add(header);

        var listScroll = new ScrollViewer
        {
            Content = projectChatConversationListPanel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Padding = new Thickness(10, 0, 10, 10),
        };
        Grid.SetRow(listScroll, 1);
        projectChatMemberListView.Children.Add(listScroll);
        return projectChatMemberListView;
    }

    private UIElement BuildProjectChatConversation()
    {
        projectChatConversationView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        projectChatConversationView.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        projectChatConversationView.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Grid
        {
            Margin = new Thickness(10, 10, 10, 8),
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Button back = StudioWidgets.CreateGlyphButton("\uE72B", "Гишүүдийн жагсаалт");
        back.Margin = new Thickness(0, 0, 8, 0);
        back.Click += (_, _) => ShowProjectChatMemberList();
        header.Children.Add(back);
        Grid.SetColumn(projectChatPeerAvatarHost, 1);
        header.Children.Add(projectChatPeerAvatarHost);
        var peer = new StackPanel
        {
            Margin = new Thickness(9, 1, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        peer.Children.Add(projectChatPeerNameText);
        peer.Children.Add(projectChatPeerRoleText);
        Grid.SetColumn(peer, 2);
        header.Children.Add(peer);
        Button close = StudioWidgets.CreateGlyphButton("\uE711", "Чатыг хаах");
        close.Margin = new Thickness(0);
        close.Click += (_, _) => CloseProjectChatWidget();
        Grid.SetColumn(close, 3);
        header.Children.Add(close);
        projectChatConversationView.Children.Add(header);

        projectChatMessagesPanel.Margin = new Thickness(0);
        projectChatScrollHost.Content = projectChatMessagesPanel;
        Grid.SetRow(projectChatScrollHost, 1);
        projectChatConversationView.Children.Add(projectChatScrollHost);

        UIElement composer = BuildProjectChatComposer();
        Grid.SetRow(composer, 2);
        projectChatConversationView.Children.Add(composer);
        return projectChatConversationView;
    }

    private UIElement BuildProjectChatComposer()
    {
        InputMethod.SetIsInputMethodEnabled(projectChatComposer, true);

        var root = new StackPanel
        {
            Margin = new Thickness(10, 4, 10, 10),
        };
        projectChatAttachmentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        projectChatAttachmentRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        projectChatAttachmentRow.Children.Add(projectChatAttachmentText);
        Button removeAttachment = StudioWidgets.CreateGlyphButton("\uE711", "Хавсралтыг арилгах");
        removeAttachment.Width = 28;
        removeAttachment.Height = 28;
        removeAttachment.Margin = new Thickness(6, 0, 0, 0);
        removeAttachment.Click += (_, _) =>
        {
            projectChatAttachmentPath = "";
            UpdateProjectChatAttachmentText();
        };
        Grid.SetColumn(removeAttachment, 1);
        projectChatAttachmentRow.Children.Add(removeAttachment);
        root.Children.Add(projectChatAttachmentRow);

        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Button attach = StudioWidgets.CreateGlyphButton("\uE723", "Файл хавсаргах");
        attach.Width = 38;
        attach.Height = 38;
        attach.Margin = new Thickness(0);
        attach.Click += (_, _) => ChooseProjectChatAttachment();
        row.Children.Add(attach);

        projectChatComposer.PreviewKeyDown += async (_, args) =>
        {
            if (args.Key == Key.Enter &&
                !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                args.Handled = true;
                await SendProjectChatMessageAsync();
            }
        };
        Grid.SetColumn(projectChatComposer, 1);
        row.Children.Add(projectChatComposer);

        Button quickEmoji = new()
        {
            Content = StudioEmojiPresenter.Create("\U0001F525", 24),
            ToolTip = "Дарвал 🔥 илгээнэ. Удаан дарвал emoji сонгоно.",
            Width = 40,
            Height = 38,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, 6, 0),
            FontSize = 18,
            Background = new SolidColorBrush(Color.FromRgb(52, 43, 35)),
        };
        quickEmoji.PreviewMouseLeftButtonDown += (_, args) =>
        {
            projectChatEmojiHoldTriggered = false;
            projectChatEmojiHoldTimer.Stop();
            projectChatEmojiHoldTimer.Start();
            args.Handled = true;
        };
        quickEmoji.PreviewMouseLeftButtonUp += async (_, args) =>
        {
            projectChatEmojiHoldTimer.Stop();
            if (!projectChatEmojiHoldTriggered)
                await SendProjectChatMessageAsync("\U0001F525");
            args.Handled = true;
        };
        quickEmoji.MouseLeave += (_, _) => projectChatEmojiHoldTimer.Stop();
        quickEmoji.MouseRightButtonUp += (_, args) =>
        {
            OpenProjectChatEmojiMenu();
            args.Handled = true;
        };
        Grid.SetColumn(quickEmoji, 2);
        row.Children.Add(quickEmoji);

        Button send = StudioWidgets.CreatePrimaryButton("Send");
        send.Height = 38;
        send.Margin = new Thickness(0);
        send.Click += async (_, _) => await SendProjectChatMessageAsync();
        Grid.SetColumn(send, 3);
        row.Children.Add(send);
        root.Children.Add(row);
        return root;
    }

    private void UpdateProjectChatWidgetVisibility()
    {
        bool available = projectWorkspaceOpen &&
                         state.HasOpenProject &&
                         account.IsSignedIn &&
                         !string.IsNullOrWhiteSpace(state.Project.Cloud.ServerProjectId);
        projectChatWidgetHost.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        projectChatPopup.IsOpen = available;
        if (!available)
        {
            projectChatRefreshTimer.Stop();
            projectChatDock.Visibility = Visibility.Collapsed;
            projectChatLauncherLayer.Visibility = Visibility.Visible;
            ResetProjectChatForCurrentProject();
            return;
        }

        string previousProjectId = projectChatBoundProjectId;
        ResetProjectChatForCurrentProject();
        projectChatRefreshTimer.Start();
        if (!projectChatBoundProjectId.Equals(previousProjectId, StringComparison.Ordinal))
            _ = RefreshProjectChatAsync(silent: true);
        RepositionProjectChatPopup();
    }

    private async Task OpenProjectChatWidgetAsync()
    {
        if (!state.HasOpenProject || !account.IsSignedIn)
            return;
        projectChatLauncherLayer.Visibility = Visibility.Collapsed;
        projectChatDock.Visibility = Visibility.Visible;
        ShowProjectChatMemberList();
        RepositionProjectChatPopup();
        await RefreshProjectChatAsync(silent: false);
    }

    private void CloseProjectChatWidget()
    {
        projectChatSelectedPeerEmail = "";
        projectChatDock.Visibility = Visibility.Collapsed;
        projectChatLauncherLayer.Visibility = Visibility.Visible;
        ShowProjectChatMemberList();
        RepositionProjectChatPopup();
    }

    private CustomPopupPlacement[] PlaceProjectChatPopup(
        Size popupSize,
        Size targetSize,
        Point offset)
    {
        double bottomOffset = inlineSiteContextEditor is null
            ? ProjectChatBottomOffset
            : ProjectChatEditorBottomOffset;
        return
        [
            new CustomPopupPlacement(
                new Point(
                    Math.Max(0, targetSize.Width - popupSize.Width - ProjectChatRightOffset),
                    Math.Max(0, targetSize.Height - popupSize.Height - bottomOffset)),
                PopupPrimaryAxis.None),
        ];
    }

    private void RepositionProjectChatPopup()
    {
        if (!projectChatPopup.IsOpen)
            return;

        dispatcher.BeginInvoke(
            new Action(() =>
            {
                double offset = projectChatPopup.HorizontalOffset;
                projectChatPopup.HorizontalOffset = offset + 0.01;
                projectChatPopup.HorizontalOffset = offset;
            }),
            DispatcherPriority.Loaded);
    }

    private void ShowProjectChatMemberList()
    {
        projectChatSelectedPeerEmail = "";
        projectChatConversationView.Visibility = Visibility.Collapsed;
        projectChatMemberListView.Visibility = Visibility.Visible;
        projectChatRenderKey = "";
    }

    private async Task OpenProjectChatConversationAsync(StudioProjectChatConversation conversation)
    {
        projectChatSelectedPeerEmail = conversation.PeerEmail.Trim();
        projectChatPeerNameText.Text = string.IsNullOrWhiteSpace(conversation.DisplayName)
            ? conversation.PeerEmail
            : conversation.DisplayName;
        projectChatPeerRoleText.Text = conversation.RoleLabel;
        SetProjectChatPeerAvatar(
            conversation.Initials,
            conversation.ProfileImageUrl);
        projectChatMemberListView.Visibility = Visibility.Collapsed;
        projectChatConversationView.Visibility = Visibility.Visible;
        projectChatMessagesPanel.Children.Clear();
        projectChatMessagesPanel.Children.Add(StudioWidgets.CreateHint("Мессежийг уншиж байна..."));
        projectChatRenderKey = "";
        await RefreshProjectChatAsync(silent: false);
        FocusProjectChatComposer();
    }

    private void FocusProjectChatComposer()
    {
        dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (projectChatDock.Visibility != Visibility.Visible ||
                    projectChatConversationView.Visibility != Visibility.Visible ||
                    !projectChatComposer.IsVisible ||
                    !projectChatComposer.IsEnabled)
                {
                    return;
                }

                projectChatComposer.Focus();
                Keyboard.Focus(projectChatComposer);
                projectChatComposer.CaretIndex = projectChatComposer.Text.Length;
                projectChatComposer.SelectionLength = 0;
            }),
            DispatcherPriority.Input);
    }

    private void ResetProjectChatForCurrentProject()
    {
        string projectId = state.HasOpenProject
            ? state.Project.Cloud.ServerProjectId.Trim()
            : "";
        if (projectChatBoundProjectId.Equals(projectId, StringComparison.Ordinal))
            return;

        projectChatLoadCancellation?.Cancel();
        projectChatLoadCancellation?.Dispose();
        projectChatLoadCancellation = null;
        projectChatBoundProjectId = projectId;
        projectChatSelectedPeerEmail = "";
        projectChatRenderKey = "";
        projectChatConversationListPanel.Tag = null;
        projectChatConversationListPanel.Children.Clear();
        projectChatMessagesPanel.Children.Clear();
        projectChatComposer.Clear();
        projectChatAttachmentPath = "";
        projectChatProjectText.Text = state.HasOpenProject
            ? $"{state.Project.Code} · {state.Project.Name}"
            : "";
        projectChatStatusText.Text = string.IsNullOrWhiteSpace(projectId)
            ? "Cloud ERA-д холбогдсон төсөлд чат ашиглана."
            : "Төслийн гишүүдийг уншиж байна...";
        UpdateProjectChatAttachmentText();
        ShowProjectChatMemberList();
        UpdateProjectChatUnreadBadge(0);
    }

    private async Task RefreshProjectChatAsync(bool silent)
    {
        if (projectChatRefreshInProgress ||
            !state.HasOpenProject ||
            projectChatWidgetHost.Visibility != Visibility.Visible)
        {
            return;
        }

        string projectId = state.Project.Cloud.ServerProjectId.Trim();
        if (string.IsNullOrWhiteSpace(projectId) || !account.IsSignedIn)
            return;

        projectChatRefreshInProgress = true;
        projectChatLoadCancellation?.Cancel();
        projectChatLoadCancellation?.Dispose();
        projectChatLoadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = projectChatLoadCancellation.Token;
        if (!silent && projectChatDock.Visibility == Visibility.Visible)
            projectChatStatusText.Text = "Чатыг шинэчилж байна...";

        try
        {
            string selectedPeer = projectChatSelectedPeerEmail;
            StudioProjectChatResponse response = await account.GetProjectChatAsync(
                projectId,
                take: string.IsNullOrWhiteSpace(selectedPeer) ? 1 : 100,
                peerEmail: string.IsNullOrWhiteSpace(selectedPeer) ? null : selectedPeer,
                cancellationToken: cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                !projectId.Equals(state.Project.Cloud.ServerProjectId, StringComparison.Ordinal))
            {
                return;
            }

            IReadOnlyList<StudioProjectChatConversation> conversations =
                ResolveProjectChatConversations(response);
            RenderProjectChat(response, conversations, scrollToEnd: !silent);
            projectChatStatusText.Text = response.IsValid
                ? string.IsNullOrWhiteSpace(projectChatSelectedPeerEmail)
                    ? $"{conversations.Count} гишүүн"
                    : $"{response.Messages.Count} мессеж"
                : response.Message;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception) when (
            exception is StudioAccountException or HttpRequestException or IOException)
        {
            projectChatStatusText.Text = "Чат шинэчлэгдсэнгүй: " + exception.Message;
        }
        finally
        {
            projectChatRefreshInProgress = false;
        }
    }

    private void RenderProjectChat(
        StudioProjectChatResponse response,
        IReadOnlyList<StudioProjectChatConversation> conversations,
        bool scrollToEnd)
    {
        projectChatEmojiChoices = response.ReactionChoices.Length > 0
            ? response.ReactionChoices
            : StudioEmojiCatalog.Choices;
        projectChatProjectText.Text = string.IsNullOrWhiteSpace(response.ProjectCode)
            ? response.ProjectName
            : $"{response.ProjectCode} · {response.ProjectName}";
        UpdateProjectChatUnreadBadge(response.UnreadTotal);
        RenderProjectChatConversations(conversations);

        if (string.IsNullOrWhiteSpace(projectChatSelectedPeerEmail))
            return;
        if (response.SelectedPeer is not null)
        {
            projectChatPeerNameText.Text = string.IsNullOrWhiteSpace(response.SelectedPeer.DisplayName)
                ? response.SelectedPeer.Email
                : response.SelectedPeer.DisplayName;
            projectChatPeerRoleText.Text = response.SelectedPeer.RoleLabel;
            SetProjectChatPeerAvatar(
                response.SelectedPeer.Initials,
                response.SelectedPeer.ProfileImageUrl);
        }

        string renderKey = response.SelectedPeerEmail + "|" + string.Join(
            "|",
            response.Messages.Select(message =>
                message.MessageId + ":" + message.ReadByPeer + ":" +
                string.Join(",", message.Reactions.Select(reaction =>
                    $"{reaction.Reaction}:{reaction.Count}:{reaction.ReactedByMe}"))));
        if (projectChatRenderKey.Equals(renderKey, StringComparison.Ordinal))
            return;

        bool hadMessages = projectChatMessagesPanel.Children.Count > 0;
        projectChatRenderKey = renderKey;
        projectChatMessagesPanel.Children.Clear();
        if (response.Messages.Count == 0)
        {
            projectChatMessagesPanel.Children.Add(StudioWidgets.CreateHint(
                "Одоогоор мессеж алга. Энэ төслийн гишүүнтэй эндээс шууд ярилцана."));
            return;
        }

        foreach (StudioProjectChatMessage message in response.Messages)
            projectChatMessagesPanel.Children.Add(BuildProjectChatMessage(message, response.ReactionChoices));

        if (scrollToEnd || !hadMessages)
            dispatcher.BeginInvoke(new Action(() => projectChatScrollHost.ScrollToEnd()));
    }

    internal static IReadOnlyList<StudioProjectChatConversation> ResolveProjectChatConversations(
        StudioProjectChatResponse response)
    {
        if (response.Conversations is { Count: > 0 })
            return response.Conversations;

        string currentEmail = response.CurrentUserEmail.Trim();
        return response.Participants
            .Where(participant =>
                !string.IsNullOrWhiteSpace(participant.Email) &&
                !participant.Email.Equals(currentEmail, StringComparison.OrdinalIgnoreCase))
            .GroupBy(
                participant => participant.Email.Trim(),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Select(participant => new StudioProjectChatConversation
            {
                PeerEmail = participant.Email.Trim(),
                DisplayName = participant.DisplayName,
                Initials = participant.Initials,
                RoleLabel = participant.RoleLabel,
                ProfileImageUrl = participant.ProfileImageUrl,
            })
            .OrderBy(conversation => conversation.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private void RenderProjectChatConversations(
        IReadOnlyList<StudioProjectChatConversation> conversations)
    {
        string key = string.Join(
            "|",
            conversations.Select(item =>
                $"{item.PeerEmail}:{item.DisplayName}:{item.RoleLabel}:{item.ProfileImageUrl}:" +
                $"{item.LastMessageAtUtc:O}:{item.UnreadCount}:{item.LastMessagePreview}"));
        if (projectChatConversationListPanel.Tag is string current &&
            current.Equals(key, StringComparison.Ordinal))
        {
            return;
        }

        projectChatConversationListPanel.Tag = key;
        projectChatConversationListPanel.Children.Clear();
        if (conversations.Count == 0)
        {
            projectChatConversationListPanel.Children.Add(StudioWidgets.CreateHint(
                "Энэ төсөлд чатлах өөр гишүүн алга."));
            return;
        }

        foreach (StudioProjectChatConversation conversation in conversations)
            projectChatConversationListPanel.Children.Add(BuildProjectChatConversationRow(conversation));
    }

    private UIElement BuildProjectChatConversationRow(StudioProjectChatConversation conversation)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(BuildProjectChatAvatar(
            conversation.Initials,
            conversation.ProfileImageUrl,
            42));

        var text = new StackPanel
        {
            Margin = new Thickness(10, 1, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(conversation.DisplayName)
                ? conversation.PeerEmail
                : conversation.DisplayName,
            Foreground = StudioTheme.TextBrush,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13.5,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        text.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(conversation.LastMessagePreview)
                ? conversation.RoleLabel
                : conversation.LastMessagePreview,
            Foreground = StudioTheme.MutedTextBrush,
            FontSize = 11,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        Grid.SetColumn(text, 1);
        row.Children.Add(text);

        if (conversation.UnreadCount > 0)
        {
            var badge = new Border
            {
                MinWidth = 22,
                Height = 22,
                Padding = new Thickness(5, 0, 5, 0),
                Background = StudioTheme.AccentBrush,
                CornerRadius = new CornerRadius(11),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = conversation.UnreadCount > 99 ? "99+" : conversation.UnreadCount.ToString(),
                    Foreground = Brushes.White,
                    FontSize = 9,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
            Grid.SetColumn(badge, 2);
            row.Children.Add(badge);
        }

        var item = new Border
        {
            Background = StudioTheme.PanelAltBrush,
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(10, 9, 10, 9),
            Margin = new Thickness(0, 0, 0, 6),
            Cursor = Cursors.Hand,
            Child = row,
        };
        item.MouseLeftButtonUp += async (_, _) => await OpenProjectChatConversationAsync(conversation);
        item.MouseEnter += (_, _) => item.Background = StudioTheme.ButtonBrush;
        item.MouseLeave += (_, _) => item.Background = StudioTheme.PanelAltBrush;
        return item;
    }

    private UIElement BuildProjectChatMessage(
        StudioProjectChatMessage message,
        IReadOnlyList<string> reactionChoices)
    {
        var body = new StackPanel();
        if (!string.IsNullOrWhiteSpace(message.Body))
            body.Children.Add(BuildProjectChatMessageBody(message.Body));
        if (!string.IsNullOrWhiteSpace(message.AttachmentFileName))
        {
            Button attachment = StudioWidgets.CreateGlyphTextButton(
                "\uE8A5",
                message.AttachmentFileName,
                message.AttachmentExpired ? "Хавсралтын хугацаа дууссан" : "Хавсралтыг хадгалах");
            attachment.IsEnabled = !message.AttachmentExpired;
            attachment.Margin = new Thickness(0, 7, 0, 0);
            attachment.HorizontalAlignment = HorizontalAlignment.Left;
            attachment.Click += async (_, _) => await SaveProjectChatAttachmentAsync(message);
            body.Children.Add(attachment);
        }

        var reactions = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        foreach (StudioProjectChatReaction reaction in message.Reactions.Where(item => item.Count > 0))
        {
            Button reactionButton = StudioWidgets.CreateInlineButton("");
            reactionButton.Content = StudioEmojiPresenter.CreateWithCount(
                reaction.Reaction,
                reaction.Count,
                17);
            reactionButton.Margin = new Thickness(0, 0, 5, 0);
            reactionButton.Background = reaction.ReactedByMe
                ? StudioTheme.AccentBrush
                : StudioTheme.ButtonBrush;
            string reactionValue = reaction.Reaction;
            reactionButton.Click += async (_, _) => await ReactToProjectChatMessageAsync(
                message.MessageId,
                reactionValue);
            reactions.Children.Add(reactionButton);
        }

        Button addReaction = StudioWidgets.CreateInlineButton("+");
        addReaction.ToolTip = "Reaction нэмэх";
        ContextMenu reactionMenu = BuildProjectChatReactionMenu(message.MessageId, reactionChoices);
        addReaction.Click += (_, _) =>
        {
            reactionMenu.PlacementTarget = addReaction;
            reactionMenu.IsOpen = true;
        };
        reactions.Children.Add(addReaction);
        body.Children.Add(reactions);

        string time = string.IsNullOrWhiteSpace(message.DisplayTime)
            ? message.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            : message.DisplayTime;
        body.Children.Add(new TextBlock
        {
            Text = message.IsMine
                ? $"{time}  ✓✓ {message.ReadLabel}"
                : time,
            FontSize = 9.5,
            Foreground = message.IsMine && message.ReadByPeer
                ? StudioTheme.AccentSoftBrush
                : StudioTheme.FaintTextBrush,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
        });

        var bubble = new Border
        {
            Background = message.IsMine
                ? new SolidColorBrush(Color.FromRgb(20, 66, 61))
                : StudioTheme.PanelAltBrush,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 9, 12, 8),
            MaxWidth = 292,
            HorizontalAlignment = message.IsMine
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left,
            Child = body,
        };
        return new Border
        {
            Margin = new Thickness(0, 0, 0, 8),
            HorizontalAlignment = message.IsMine
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left,
            Child = bubble,
        };
    }

    private UIElement BuildProjectChatMessageBody(string messageBody)
    {
        IReadOnlyList<StudioEmojiSegment> segments = StudioEmojiCatalog.Tokenize(messageBody);
        bool isSingleEmoji = segments.Count == 1 && segments[0].IsEmoji;
        var text = new TextBlock
        {
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
        };
        foreach (StudioEmojiSegment segment in segments)
        {
            if (!segment.IsEmoji)
            {
                text.Inlines.Add(new System.Windows.Documents.Run(segment.Text));
                continue;
            }

            var inline = new System.Windows.Documents.InlineUIContainer(
                StudioEmojiPresenter.Create(segment.Text, isSingleEmoji ? 34 : 19))
            {
                BaselineAlignment = BaselineAlignment.Center,
            };
            text.Inlines.Add(inline);
        }

        return text;
    }

    private ContextMenu BuildProjectChatReactionMenu(
        string messageId,
        IReadOnlyList<string> reactionChoices)
    {
        IReadOnlyList<string> choices = reactionChoices.Count > 0
            ? reactionChoices
            : StudioEmojiCatalog.Choices;
        return BuildProjectChatEmojiPicker(
            choices,
            reactionValue => ReactToProjectChatMessageAsync(
                messageId,
                reactionValue));
    }

    private Grid BuildProjectChatAvatar(string initials, string imageUrl, double size)
    {
        var host = new Grid { Width = size, Height = size };
        host.Children.Add(new Ellipse
        {
            Width = size,
            Height = size,
            Fill = new SolidColorBrush(Color.FromRgb(44, 105, 83)),
        });
        host.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(initials) ? "?" : initials,
            Foreground = Brushes.White,
            FontWeight = FontWeights.SemiBold,
            FontSize = size <= 36 ? 10 : 11,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        });
        var image = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.UniformToFill,
            Clip = new EllipseGeometry(new Rect(0, 0, size, size)),
            Visibility = Visibility.Collapsed,
        };
        host.Children.Add(image);
        if (!string.IsNullOrWhiteSpace(imageUrl))
            _ = LoadProjectChatAvatarAsync(imageUrl, image);
        return host;
    }

    private void SetProjectChatPeerAvatar(string initials, string imageUrl)
    {
        projectChatPeerAvatarHost.Children.Clear();
        projectChatPeerAvatarHost.Children.Add(BuildProjectChatAvatar(initials, imageUrl, 36));
    }

    private async Task LoadProjectChatAvatarAsync(string imageUrl, Image target)
    {
        try
        {
            if (!projectChatAvatarCache.TryGetValue(imageUrl, out ImageSource? source))
            {
                byte[]? bytes = await account.DownloadProjectChatAssetAsync(imageUrl);
                if (bytes is null)
                    return;
                using var stream = new MemoryStream(bytes, writable: false);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
                source = bitmap;
                projectChatAvatarCache[imageUrl] = source;
            }
            target.Source = source;
            target.Visibility = Visibility.Visible;
        }
        catch (Exception exception) when (
            exception is StudioAccountException or HttpRequestException or IOException or NotSupportedException)
        {
            SetStatus("Чатын profile зураг ачаалагдсангүй: " + exception.Message);
        }
    }

    private void UpdateProjectChatUnreadBadge(int unread)
    {
        projectChatLauncherBadgeText.Text = unread > 99 ? "99+" : unread.ToString();
        projectChatLauncherBadge.Visibility = unread > 0
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ChooseProjectChatAttachment()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Чатад хавсаргах файл",
            Filter = StudioProjectChatRules.AttachmentDialogFilter,
            Multiselect = false,
        };
        if (dialog.ShowDialog() != true)
            return;

        StudioProjectChatAttachmentValidation validation =
            StudioProjectChatRules.ValidateAttachment(dialog.FileName);
        if (!validation.IsValid)
        {
            projectChatStatusText.Text = validation.Message;
            return;
        }

        projectChatAttachmentPath = dialog.FileName;
        UpdateProjectChatAttachmentText();
        projectChatStatusText.Text = "Файл хавсаргагдлаа.";
    }

    private void UpdateProjectChatAttachmentText()
    {
        bool hasAttachment = !string.IsNullOrWhiteSpace(projectChatAttachmentPath);
        projectChatAttachmentText.Text = hasAttachment
            ? Path.GetFileName(projectChatAttachmentPath)
            : "";
        projectChatAttachmentRow.Visibility = hasAttachment
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private async Task SendProjectChatMessageAsync(string? quickMessage = null)
    {
        if (projectChatSendInProgress ||
            !state.HasOpenProject ||
            string.IsNullOrWhiteSpace(projectChatSelectedPeerEmail))
        {
            return;
        }

        string projectId = state.Project.Cloud.ServerProjectId.Trim();
        string body = StudioProjectChatRules.CleanBody(
            quickMessage ?? projectChatComposer.Text);
        if (string.IsNullOrWhiteSpace(body) && string.IsNullOrWhiteSpace(projectChatAttachmentPath))
        {
            projectChatStatusText.Text = "Мессеж эсвэл файл оруулна уу.";
            return;
        }

        projectChatSendInProgress = true;
        projectChatStatusText.Text = "Мессеж илгээж байна...";
        try
        {
            StudioProjectChatResponse response = await account.SendProjectChatMessageAsync(
                projectId,
                body,
                string.IsNullOrWhiteSpace(projectChatAttachmentPath)
                    ? null
                    : projectChatAttachmentPath,
                peerEmail: projectChatSelectedPeerEmail);
            if (quickMessage is null)
                projectChatComposer.Clear();
            projectChatAttachmentPath = "";
            UpdateProjectChatAttachmentText();
            projectChatRenderKey = "";
            RenderProjectChat(
                response,
                ResolveProjectChatConversations(response),
                scrollToEnd: true);
            projectChatStatusText.Text = $"{response.Messages.Count} мессеж";
        }
        catch (Exception exception) when (
            exception is StudioAccountException or HttpRequestException or IOException)
        {
            projectChatStatusText.Text = "Мессеж илгээгдсэнгүй: " + exception.Message;
        }
        finally
        {
            projectChatSendInProgress = false;
            FocusProjectChatComposer();
        }
    }

    private void OpenProjectChatEmojiMenu()
    {
        IReadOnlyList<string> choices = projectChatEmojiChoices.Count > 0
            ? projectChatEmojiChoices
            : StudioEmojiCatalog.Choices;
        ContextMenu menu = BuildProjectChatEmojiPicker(
            choices,
            emoji => SendProjectChatMessageAsync(emoji));
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private static ContextMenu BuildProjectChatEmojiPicker(
        IReadOnlyList<string> choices,
        Func<string, Task> onSelected)
    {
        var panelFactory = new FrameworkElementFactory(typeof(WrapPanel));
        panelFactory.SetValue(WrapPanel.OrientationProperty, Orientation.Horizontal);
        var menu = new ContextMenu
        {
            Width = 252,
            Padding = new Thickness(6),
            ItemsPanel = new ItemsPanelTemplate(panelFactory),
        };
        foreach (string choice in choices.Distinct(StringComparer.Ordinal))
        {
            string emoji = choice;
            var item = new MenuItem
            {
                Header = StudioEmojiPresenter.Create(emoji, 25),
                ToolTip = emoji,
                Width = 40,
                Height = 40,
                Padding = new Thickness(7),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            item.Click += async (_, _) =>
            {
                menu.IsOpen = false;
                await onSelected(emoji);
            };
            menu.Items.Add(item);
        }

        return menu;
    }

    private async Task ReactToProjectChatMessageAsync(string messageId, string reaction)
    {
        if (!state.HasOpenProject ||
            string.IsNullOrWhiteSpace(messageId) ||
            string.IsNullOrWhiteSpace(projectChatSelectedPeerEmail))
        {
            return;
        }
        try
        {
            StudioProjectChatResponse response = await account.ReactToProjectChatMessageAsync(
                state.Project.Cloud.ServerProjectId,
                messageId,
                reaction,
                peerEmail: projectChatSelectedPeerEmail);
            projectChatRenderKey = "";
            RenderProjectChat(
                response,
                ResolveProjectChatConversations(response),
                scrollToEnd: false);
        }
        catch (Exception exception) when (
            exception is StudioAccountException or HttpRequestException)
        {
            projectChatStatusText.Text = "Reaction шинэчлэгдсэнгүй: " + exception.Message;
        }
    }

    private async Task SaveProjectChatAttachmentAsync(StudioProjectChatMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.AttachmentUrl) || message.AttachmentExpired)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Чатын хавсралтыг хадгалах",
            FileName = Path.GetFileName(message.AttachmentFileName),
            OverwritePrompt = true,
        };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            byte[]? bytes = await account.DownloadProjectChatAssetAsync(message.AttachmentUrl);
            if (bytes is null)
            {
                projectChatStatusText.Text = "Хавсралт олдсонгүй эсвэл хугацаа нь дууссан байна.";
                return;
            }
            await File.WriteAllBytesAsync(dialog.FileName, bytes);
            projectChatStatusText.Text = $"Хавсралт хадгалагдлаа: {dialog.FileName}";
        }
        catch (Exception exception) when (
            exception is StudioAccountException or HttpRequestException or IOException)
        {
            projectChatStatusText.Text = "Хавсралт хадгалагдсангүй: " + exception.Message;
        }
    }
}
