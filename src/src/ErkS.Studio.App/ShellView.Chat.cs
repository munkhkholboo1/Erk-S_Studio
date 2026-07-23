using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace ErkS.Studio;

internal sealed partial class ShellView
{
    private readonly StackPanel projectChatMessagesPanel = new();
    private readonly TextBlock projectChatStatusText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock projectChatParticipantsText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBlock projectChatAttachmentText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
    };
    private readonly TextBox projectChatComposer = new()
    {
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        MinHeight = 64,
        MaxHeight = 130,
        MaxLength = StudioProjectChatRules.MaxBodyLength,
    };
    private readonly ScrollViewer projectChatScrollHost = new()
    {
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
    private CancellationTokenSource? projectChatLoadCancellation;
    private string projectChatAttachmentPath = "";
    private string projectChatBoundProjectId = "";
    private string projectChatRenderKey = "";
    private bool projectChatRefreshInProgress;
    private bool projectChatSendInProgress;

    private UIElement BuildProjectChatPage()
    {
        var root = new DockPanel
        {
            Margin = new Thickness(30, 26, 30, 22),
            Background = StudioTheme.WindowBackgroundBrush,
        };

        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
        var refresh = StudioWidgets.CreateGlyphButton("\uE72C", "Чатыг шинэчлэх");
        refresh.Click += async (_, _) => await RefreshProjectChatAsync(silent: false);
        DockPanel.SetDock(refresh, Dock.Right);
        header.Children.Add(refresh);

        var heading = new StackPanel();
        heading.Children.Add(StudioWidgets.CreateTitle("Төслийн чат"));
        heading.Children.Add(projectChatParticipantsText);
        header.Children.Add(heading);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var composer = new StackPanel();
        composer.Children.Add(projectChatComposer);
        projectChatAttachmentText.Margin = new Thickness(0, 6, 0, 6);
        composer.Children.Add(projectChatAttachmentText);

        var composerActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
        var attach = StudioWidgets.CreateGlyphTextButton("\uE723", "Файл", "Чатад файл хавсаргах");
        attach.Click += (_, _) => ChooseProjectChatAttachment();
        var removeAttachment = StudioWidgets.CreateGlyphButton("\uE711", "Хавсралтыг арилгах");
        removeAttachment.Click += (_, _) =>
        {
            projectChatAttachmentPath = "";
            UpdateProjectChatAttachmentText();
        };
        var send = StudioWidgets.CreateGlyphTextButton(
            "\uE724",
            "Илгээх",
            "Төслийн багт мессеж илгээх",
            primary: true);
        send.Click += async (_, _) => await SendProjectChatMessageAsync();
        composerActions.Children.Add(attach);
        composerActions.Children.Add(removeAttachment);
        composerActions.Children.Add(send);
        composer.Children.Add(composerActions);
        composer.Children.Add(projectChatStatusText);

        var composerCard = StudioWidgets.CreateCard(composer);
        composerCard.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(composerCard, Dock.Bottom);
        root.Children.Add(composerCard);

        projectChatMessagesPanel.Margin = new Thickness(0, 0, 8, 0);
        projectChatScrollHost.Content = projectChatMessagesPanel;
        root.Children.Add(projectChatScrollHost);
        UpdateProjectChatAttachmentText();
        return root;
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
        projectChatRenderKey = "";
        projectChatMessagesPanel.Children.Clear();
        projectChatComposer.Clear();
        projectChatAttachmentPath = "";
        projectChatParticipantsText.Text = "";
        projectChatStatusText.Text = string.IsNullOrWhiteSpace(projectId)
            ? "Cloud ERA-д холбогдсон төсөлд чат ашиглана."
            : "Чатыг уншиж байна...";
        UpdateProjectChatAttachmentText();
    }

    private async Task RefreshProjectChatAsync(bool silent)
    {
        if (projectChatRefreshInProgress || !state.HasOpenProject)
            return;

        string projectId = state.Project.Cloud.ServerProjectId.Trim();
        if (string.IsNullOrWhiteSpace(projectId))
        {
            projectChatStatusText.Text = "Cloud ERA-д холбогдсон төсөлд чат ашиглана.";
            return;
        }
        if (!account.IsSignedIn)
        {
            projectChatStatusText.Text = "Төслийн чатыг нээхийн тулд Cloud ERA бүртгэлээр нэвтэрнэ үү.";
            return;
        }

        projectChatRefreshInProgress = true;
        projectChatLoadCancellation?.Cancel();
        projectChatLoadCancellation?.Dispose();
        projectChatLoadCancellation = new CancellationTokenSource();
        CancellationToken cancellationToken = projectChatLoadCancellation.Token;
        if (!silent)
            projectChatStatusText.Text = "Чатыг шинэчилж байна...";

        try
        {
            StudioProjectChatResponse response = await account.GetProjectChatAsync(
                projectId,
                cancellationToken: cancellationToken);
            if (cancellationToken.IsCancellationRequested ||
                !projectId.Equals(state.Project.Cloud.ServerProjectId, StringComparison.Ordinal))
            {
                return;
            }

            RenderProjectChat(response, scrollToEnd: !silent);
            projectChatStatusText.Text = response.IsValid
                ? $"{response.Messages.Count} мессеж"
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

    private void RenderProjectChat(StudioProjectChatResponse response, bool scrollToEnd)
    {
        projectChatParticipantsText.Text = response.Participants.Count == 0
            ? "Төслийн идэвхтэй оролцогчдын чат"
            : string.Join(
                "  ·  ",
                response.Participants.Select(item =>
                    string.IsNullOrWhiteSpace(item.DisplayName) ? item.Email : item.DisplayName));

        string renderKey = string.Join(
            "|",
            response.Messages.Select(message =>
                message.MessageId + ":" +
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
                "Одоогоор мессеж алга. Төслийн оролцогчид энд мэдээлэл солилцоно."));
            return;
        }

        foreach (StudioProjectChatMessage message in response.Messages)
            projectChatMessagesPanel.Children.Add(BuildProjectChatMessage(message, response.ReactionChoices));

        if (scrollToEnd || !hadMessages)
            dispatcher.BeginInvoke(new Action(() => projectChatScrollHost.ScrollToEnd()));
    }

    private UIElement BuildProjectChatMessage(
        StudioProjectChatMessage message,
        IReadOnlyList<string> reactionChoices)
    {
        var body = new StackPanel();
        var author = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(message.AuthorDisplayName)
                ? message.AuthorEmail
                : message.AuthorDisplayName,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
        };
        body.Children.Add(author);
        if (!string.IsNullOrWhiteSpace(message.AuthorRoleLabel))
            body.Children.Add(StudioWidgets.CreateHint(message.AuthorRoleLabel));
        if (!string.IsNullOrWhiteSpace(message.Body))
        {
            body.Children.Add(new TextBlock
            {
                Text = message.Body,
                Foreground = StudioTheme.TextBrush,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 7, 0, 0),
            });
        }
        if (!string.IsNullOrWhiteSpace(message.AttachmentFileName))
        {
            var attachment = StudioWidgets.CreateGlyphTextButton(
                "\uE8A5",
                message.AttachmentFileName,
                message.AttachmentExpired ? "Хавсралтын хугацаа дууссан" : "Хавсралтыг хадгалах");
            attachment.IsEnabled = !message.AttachmentExpired;
            attachment.Margin = new Thickness(0, 8, 0, 0);
            attachment.HorizontalAlignment = HorizontalAlignment.Left;
            attachment.Click += async (_, _) => await SaveProjectChatAttachmentAsync(message);
            body.Children.Add(attachment);
        }

        var reactions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (StudioProjectChatReaction reaction in message.Reactions.Where(item => item.Count > 0))
        {
            var reactionButton = StudioWidgets.CreateInlineButton($"{reaction.Reaction} {reaction.Count}");
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

        var addReaction = StudioWidgets.CreateInlineButton("+");
        addReaction.ToolTip = "Reaction нэмэх";
        var menu = new ContextMenu();
        IEnumerable<string> choices = reactionChoices.Count > 0
            ? reactionChoices
            : StudioProjectChatRules.Reactions;
        foreach (string choice in choices)
        {
            var item = new MenuItem { Header = choice };
            string reactionValue = choice;
            item.Click += async (_, _) => await ReactToProjectChatMessageAsync(
                message.MessageId,
                reactionValue);
            menu.Items.Add(item);
        }
        addReaction.Click += (_, _) =>
        {
            menu.PlacementTarget = addReaction;
            menu.IsOpen = true;
        };
        reactions.Children.Add(addReaction);
        body.Children.Add(reactions);

        body.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(message.DisplayTime)
                ? message.CreatedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                : message.DisplayTime,
            FontSize = StudioTheme.HintFontSize,
            Foreground = StudioTheme.FaintTextBrush,
            Margin = new Thickness(0, 7, 0, 0),
        });

        var card = StudioWidgets.CreateCard(body);
        card.MaxWidth = 680;
        card.HorizontalAlignment = message.IsMine
            ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        if (message.IsMine)
            card.Background = new SolidColorBrush(Color.FromRgb(28, 54, 82));
        return card;
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
    }

    private void UpdateProjectChatAttachmentText()
    {
        projectChatAttachmentText.Text = string.IsNullOrWhiteSpace(projectChatAttachmentPath)
            ? "Хавсралтгүй"
            : $"Хавсралт: {Path.GetFileName(projectChatAttachmentPath)}";
    }

    private async Task SendProjectChatMessageAsync()
    {
        if (projectChatSendInProgress || !state.HasOpenProject)
            return;

        string projectId = state.Project.Cloud.ServerProjectId.Trim();
        string body = StudioProjectChatRules.CleanBody(projectChatComposer.Text);
        if (string.IsNullOrWhiteSpace(projectId))
        {
            projectChatStatusText.Text = "Cloud ERA-д холбогдсон төсөлд чат ашиглана.";
            return;
        }
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
                    : projectChatAttachmentPath);
            projectChatComposer.Clear();
            projectChatAttachmentPath = "";
            UpdateProjectChatAttachmentText();
            projectChatRenderKey = "";
            RenderProjectChat(response, scrollToEnd: true);
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
        }
    }

    private async Task ReactToProjectChatMessageAsync(string messageId, string reaction)
    {
        if (!state.HasOpenProject || string.IsNullOrWhiteSpace(messageId))
            return;
        try
        {
            StudioProjectChatResponse response = await account.ReactToProjectChatMessageAsync(
                state.Project.Cloud.ServerProjectId,
                messageId,
                reaction);
            projectChatRenderKey = "";
            RenderProjectChat(response, scrollToEnd: false);
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
