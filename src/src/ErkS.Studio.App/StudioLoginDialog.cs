using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace ErkS.Studio;

internal sealed class StudioLoginDialog : Window
{
    private readonly StudioAccountService account;
    private readonly TextBox serverBox = new();
    private readonly TextBox emailBox = new();
    private readonly PasswordBox passwordBox = new();
    private readonly TextBlock statusText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button loginButton;

    public StudioLoginDialog(StudioAccountService account)
    {
        this.account = account;
        Title = "Erk-S Studio - Нэвтрэх";
        Width = 560;
        Height = 430;
        MinWidth = 500;
        MinHeight = 390;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        serverBox.Text = account.SuggestedServerUrl;
        emailBox.Text = account.SuggestedEmail;
        loginButton = StudioWidgets.CreatePrimaryButton("Нэвтрэх");
        StudioTheme.Apply(this);
        Content = BuildContent();
        Loaded += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(emailBox.Text))
                emailBox.Focus();
            else
                passwordBox.Focus();
        };
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(22) };
        var actions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0),
        };
        var register = StudioWidgets.CreateButton("Бүртгэл үүсгэх");
        register.Click += (_, _) => account.OpenAccountRegistration();
        var cancel = StudioWidgets.CreateButton("Болих");
        cancel.IsCancel = true;
        cancel.Click += (_, _) => DialogResult = false;
        loginButton.IsDefault = true;
        loginButton.Click += async (_, _) => await SignInAsync();
        actions.Children.Add(register);
        actions.Children.Add(cancel);
        actions.Children.Add(loginButton);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        statusText.Foreground = StudioTheme.DangerBrush;
        statusText.Margin = new Thickness(0, 12, 0, 0);
        DockPanel.SetDock(statusText, Dock.Bottom);
        root.Children.Add(statusText);

        var form = new StackPanel { MaxWidth = 620 };
        form.Children.Add(StudioWidgets.CreateTitle("Erk-S Studio бүртгэл"));
        form.Children.Add(StudioWidgets.CreateHint(
            "Cloud ERA төсөл нь бүртгэлтэй хэрэглэгчид хуваарилагдана. Энэ төхөөрөмж дээр идэвхтэй Erk-S Studio лиценз шаардана."));
        form.Children.Add(new Border { Height = 14 });
        if (StudioReleaseInfo.IsDevelopmentBuild)
            form.Children.Add(StudioWidgets.CreateFormRow("Server", serverBox));
        form.Children.Add(StudioWidgets.CreateFormRow("И-мэйл", emailBox));
        form.Children.Add(StudioWidgets.CreateFormRow("Нууц үг", passwordBox));
        form.Children.Add(new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(10),
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5),
            Child = StudioWidgets.CreateHint(
                "Нууц үг хадгалагдахгүй. Лицензийн activation Windows Credential Manager-д хадгалагдаж, дараагийн нээлтээр session автоматаар сэргээнэ."),
        });
        root.Children.Add(form);
        return root;
    }

    private async Task SignInAsync()
    {
        loginButton.IsEnabled = false;
        statusText.Foreground = StudioTheme.MutedTextBrush;
        statusText.Text = "Лиценз болон бүртгэлийг шалгаж байна...";
        try
        {
            await account.SignInAsync(serverBox.Text, emailBox.Text, passwordBox.Password);
            passwordBox.Clear();
            DialogResult = true;
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException or Win32Exception)
        {
            statusText.Foreground = StudioTheme.DangerBrush;
            statusText.Text = exception is TaskCanceledException
                ? "Cloud ERA үйлчилгээ хариу өгөх хугацаа хэтэрлээ."
                : exception.Message;
            passwordBox.SelectAll();
            passwordBox.Focus();
        }
        finally
        {
            loginButton.IsEnabled = true;
        }
    }
}
