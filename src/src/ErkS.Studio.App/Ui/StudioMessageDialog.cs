using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ErkS.Studio;

internal static class StudioMessageDialog
{
    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title = "Erk-S Studio",
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new MessageWindow(message, title, buttons, image);
        if (owner is not null && owner.IsVisible)
            dialog.Owner = owner;
        _ = dialog.ShowDialog();
        return dialog.Result;
    }

    private sealed class MessageWindow : Window
    {
        private readonly MessageBoxButton buttons;

        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        public MessageWindow(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
        {
            this.buttons = buttons;
            Title = string.IsNullOrWhiteSpace(title) ? "Erk-S Studio" : title;
            Width = 520;
            MinWidth = 420;
            MaxWidth = 680;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 620;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            StudioTheme.Apply(this);
            Content = BuildContent(message, image);
            PreviewKeyDown += (_, args) =>
            {
                if (args.Key != Key.Escape)
                    return;
                args.Handled = true;
                Result = CancelResult();
                Close();
            };
        }

        private UIElement BuildContent(string message, MessageBoxImage image)
        {
            var root = new DockPanel { Margin = new Thickness(22, 20, 22, 18) };
            var actions = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 18, 0, 0),
            };
            AddButtons(actions);
            DockPanel.SetDock(actions, Dock.Bottom);
            root.Children.Add(actions);

            var content = new StackPanel();
            var heading = StudioWidgets.CreateTitle(Title);
            heading.FontSize = 18;
            content.Children.Add(heading);
            if (image != MessageBoxImage.None)
            {
                content.Children.Add(new TextBlock
                {
                    Text = ImageLabel(image),
                    Foreground = ImageBrush(image),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8),
                });
            }
            content.Children.Add(new TextBlock
            {
                Text = message,
                Foreground = StudioTheme.TextBrush,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
            });
            root.Children.Add(content);
            return root;
        }

        private void AddButtons(Panel actions)
        {
            switch (buttons)
            {
                case MessageBoxButton.OKCancel:
                    actions.Children.Add(CreateButton("Болих", MessageBoxResult.Cancel));
                    actions.Children.Add(CreateButton("За", MessageBoxResult.OK, primary: true));
                    break;
                case MessageBoxButton.YesNo:
                    actions.Children.Add(CreateButton("Үгүй", MessageBoxResult.No));
                    actions.Children.Add(CreateButton("Тийм", MessageBoxResult.Yes, primary: true));
                    break;
                case MessageBoxButton.YesNoCancel:
                    actions.Children.Add(CreateButton("Болих", MessageBoxResult.Cancel));
                    actions.Children.Add(CreateButton("Үгүй", MessageBoxResult.No));
                    actions.Children.Add(CreateButton("Тийм", MessageBoxResult.Yes, primary: true));
                    break;
                default:
                    actions.Children.Add(CreateButton("За", MessageBoxResult.OK, primary: true));
                    break;
            }
        }

        private Button CreateButton(string text, MessageBoxResult result, bool primary = false)
        {
            Button button = primary
                ? StudioWidgets.CreatePrimaryButton(text)
                : StudioWidgets.CreateButton(text);
            button.IsDefault = primary;
            button.Click += (_, _) =>
            {
                Result = result;
                Close();
            };
            return button;
        }

        private MessageBoxResult CancelResult() => buttons switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            MessageBoxButton.OK => MessageBoxResult.OK,
            _ => MessageBoxResult.Cancel,
        };

        private static string ImageLabel(MessageBoxImage image) => image switch
        {
            MessageBoxImage.Warning => "АНХААРУУЛГА",
            MessageBoxImage.Error => "АЛДАА",
            MessageBoxImage.Question => "БАТАЛГААЖУУЛАХ",
            _ => "МЭДЭЭЛЭЛ",
        };

        private static System.Windows.Media.Brush ImageBrush(MessageBoxImage image) => image switch
        {
            MessageBoxImage.Warning => StudioTheme.WarningBrush,
            MessageBoxImage.Error => StudioTheme.DangerBrush,
            MessageBoxImage.Question => StudioTheme.AccentSoftBrush,
            _ => StudioTheme.MutedTextBrush,
        };
    }
}
