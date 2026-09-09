using System.ComponentModel;
using System.Net.Http;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace ErkS.Studio;

internal sealed class StudioLoginDialog : Window
{
    private readonly StudioAccountService account;
    private readonly TextBox serverBox = new();
    private readonly TextBox emailBox = new();
    private readonly PasswordBox passwordBox = new();
    private readonly TextBox visiblePasswordBox = new()
    {
        Visibility = Visibility.Collapsed,
    };
    private readonly TextBlock statusText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly Button loginButton;
    private readonly Button passwordVisibilityButton;
    private StudioPasswordVisibilityState passwordVisibility =
        StudioPasswordVisibilityPolicy.Initial;

    // 🔴 THE FORM ASKED A PERSON WHO THEY WERE EVERY SINGLE TIME, on their own
    // machine, because the only thing that remembered them was account.json -
    // and signing out deletes it, as does handing the machine to a seat. So the
    // address was remembered in exactly the case where it was already on screen
    // and forgotten in every case where it would have helped.
    //
    // What is remembered is an IDENTIFIER, never a credential: the password is
    // still asked for in full. See StudioRememberedProfile.
    private readonly StudioRememberedProfile? recognised;
    private readonly StackPanel greetingPanel = new();
    private readonly Grid identityRow;
    private readonly TextBlock greetingName = new();
    private readonly TextBlock greetingEmail = new();
    private readonly TextBlock greetingInitials = new();
    private LoginPromptMode promptMode;

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
        recognised = StudioLoginPrompt.Recognised(
            StudioRememberedProfiles.Read(),
            serverBox.Text);
        promptMode = StudioLoginPrompt.Mode(recognised, serverBox.Text);

        // The greeting and the address come from ONE record. Reading the name
        // from the profile and the address from somewhere else is how a form
        // ends up signing in as somebody other than the person it greeted.
        emailBox.Text = recognised?.Email ?? account.SuggestedEmail;
        loginButton = StudioWidgets.CreatePrimaryButton("Нэвтрэх");
        passwordVisibilityButton = StudioWidgets.CreateInlineButton(
            passwordVisibility.ToggleLabel);
        passwordVisibilityButton.MinWidth = 68;
        passwordVisibilityButton.Margin = new Thickness(8, 0, 0, 0);
        passwordVisibilityButton.Click += (_, _) => TogglePasswordVisibility();
        UpdatePasswordVisibilityButton();
        identityRow = StudioWidgets.CreateFormRow("И-мэйл", emailBox);
        StudioTheme.Apply(this);
        Content = BuildContent();
        ApplyPromptMode();
        Loaded += (_, _) =>
        {
            if (promptMode == LoginPromptMode.AskForEverything &&
                string.IsNullOrWhiteSpace(emailBox.Text))
            {
                emailBox.Focus();
            }
            else
            {
                passwordBox.Focus();
            }
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
        form.Children.Add(BuildGreeting());
        form.Children.Add(identityRow);
        form.Children.Add(StudioWidgets.CreateFormRow(
            "Нууц үг",
            BuildPasswordEditor()));
        form.Children.Add(new Border
        {
            Margin = new Thickness(0, 14, 0, 0),
            Padding = new Thickness(10),
            Background = StudioTheme.PanelBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            Child = StudioWidgets.CreateHint(
                "Нууц үг хадгалагдахгүй. Лицензийн activation Windows Credential Manager-д хадгалагдаж, дараагийн нээлтээр session автоматаар сэргээнэ."),
        });
        root.Children.Add(form);
        return root;
    }

    /// <summary>
    /// The name and letter avatar of whoever was here last, with the two ways
    /// out of it.
    ///
    /// Both ways are offered because they are different acts: signing in as
    /// somebody else on a machine that stays this person's, and taking this
    /// person's name off a machine that is somebody else's now. A seat can be a
    /// shared machine, so the second is not optional.
    /// </summary>
    private UIElement BuildGreeting()
    {
        var avatar = new Grid { Width = 40, Height = 40, VerticalAlignment = VerticalAlignment.Center };
        avatar.Children.Add(new System.Windows.Shapes.Ellipse
        {
            Width = 40,
            Height = 40,
            Fill = StudioTheme.PanelAltBrush,
            StrokeThickness = 0,
        });
        greetingInitials.HorizontalAlignment = HorizontalAlignment.Center;
        greetingInitials.VerticalAlignment = VerticalAlignment.Center;
        greetingInitials.Foreground = StudioTheme.TextBrush;
        greetingInitials.FontSize = 15;
        greetingInitials.FontWeight = FontWeights.SemiBold;
        avatar.Children.Add(greetingInitials);

        greetingName.Foreground = StudioTheme.TextBrush;
        greetingName.FontSize = 15;
        greetingName.FontWeight = FontWeights.SemiBold;
        greetingName.TextTrimming = TextTrimming.CharacterEllipsis;
        greetingEmail.Foreground = StudioTheme.MutedTextBrush;
        greetingEmail.TextTrimming = TextTrimming.CharacterEllipsis;

        var names = new StackPanel
        {
            Margin = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        names.Children.Add(greetingName);
        names.Children.Add(greetingEmail);

        var person = new DockPanel();
        DockPanel.SetDock(avatar, Dock.Left);
        person.Children.Add(avatar);
        person.Children.Add(names);

        Button another = StudioWidgets.CreateInlineButton("Өөр бүртгэлээр");
        another.ToolTip = "И-мэйл бичих хэлбэр рүү буцна. Санагдсан профайл хэвээр үлдэнэ.";
        another.Click += (_, _) => UseAnotherAccount();
        Button forget = StudioWidgets.CreateInlineButton("Санахаа болих");
        forget.Margin = new Thickness(10, 0, 0, 0);
        forget.ToolTip = "Энэ төхөөрөмжөөс энэ нэрийг устгана. Бүртгэлд хамаагүй.";
        forget.Click += (_, _) => ForgetRememberedProfile();

        var choices = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        choices.Children.Add(another);
        choices.Children.Add(forget);

        greetingPanel.Margin = new Thickness(0, 0, 0, 14);
        greetingPanel.Children.Add(new Border
        {
            Padding = new Thickness(12),
            Background = StudioTheme.PanelBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(5),
            Child = person,
        });
        greetingPanel.Children.Add(choices);
        return greetingPanel;
    }

    /// <summary>
    /// Shows one of the two forms. The address box is HIDDEN rather than
    /// removed while somebody is greeted - it is still what gets signed in
    /// with, and a second place holding the address is a second thing to keep
    /// in step.
    /// </summary>
    private void ApplyPromptMode()
    {
        bool greeting = promptMode == LoginPromptMode.AskOnlyForPassword && recognised is not null;
        greetingPanel.Visibility = greeting ? Visibility.Visible : Visibility.Collapsed;
        identityRow.Visibility = greeting ? Visibility.Collapsed : Visibility.Visible;
        if (!greeting)
            return;

        greetingName.Text = StudioAccountDisplay.NameOrFallback(
            recognised!.DisplayName,
            recognised.Email,
            "Миний бүртгэл");
        greetingEmail.Text = recognised.Email;
        greetingInitials.Text = StudioAccountDisplay.Initials(greetingName.Text);
        AutomationProperties.SetName(greetingPanel, greetingName.Text + " " + recognised.Email);
    }

    /// <summary>
    /// Back to the full form. The profile is NOT forgotten: signing in as
    /// somebody else once does not mean this machine has stopped being the
    /// first person's.
    /// </summary>
    private void UseAnotherAccount()
    {
        promptMode = LoginPromptMode.AskForEverything;
        emailBox.Text = "";
        ClearPassword();
        ApplyPromptMode();
        emailBox.Focus();
    }

    private void ForgetRememberedProfile()
    {
        StudioRememberedProfiles.Forget();
        promptMode = LoginPromptMode.AskForEverything;
        emailBox.Text = "";
        ClearPassword();
        ApplyPromptMode();
        statusText.Foreground = StudioTheme.MutedTextBrush;
        statusText.Text = "Санагдсан профайл энэ төхөөрөмжөөс устлаа.";
        emailBox.Focus();
    }

    private UIElement BuildPasswordEditor()
    {
        var editor = new Grid();
        editor.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });
        editor.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = GridLength.Auto,
        });

        Grid.SetColumn(passwordBox, 0);
        editor.Children.Add(passwordBox);
        Grid.SetColumn(visiblePasswordBox, 0);
        editor.Children.Add(visiblePasswordBox);
        Grid.SetColumn(passwordVisibilityButton, 1);
        editor.Children.Add(passwordVisibilityButton);
        return editor;
    }

    private void TogglePasswordVisibility()
    {
        StudioPasswordVisibilityState next =
            StudioPasswordVisibilityPolicy.Toggle(passwordVisibility);
        if (next.IsVisible)
        {
            visiblePasswordBox.Text = passwordBox.Password;
            passwordBox.Visibility = Visibility.Collapsed;
            visiblePasswordBox.Visibility = Visibility.Visible;
            visiblePasswordBox.Focus();
            visiblePasswordBox.CaretIndex = visiblePasswordBox.Text.Length;
        }
        else
        {
            passwordBox.Password = visiblePasswordBox.Text;
            visiblePasswordBox.Clear();
            visiblePasswordBox.Visibility = Visibility.Collapsed;
            passwordBox.Visibility = Visibility.Visible;
            passwordBox.Focus();
        }

        passwordVisibility = next;
        UpdatePasswordVisibilityButton();
    }

    private void UpdatePasswordVisibilityButton()
    {
        passwordVisibilityButton.Content = passwordVisibility.ToggleLabel;
        passwordVisibilityButton.ToolTip = passwordVisibility.ToggleTooltip;
        AutomationProperties.SetName(
            passwordVisibilityButton,
            passwordVisibility.ToggleTooltip);
    }

    private string CurrentPassword =>
        StudioPasswordVisibilityPolicy.CurrentPassword(
            passwordVisibility,
            passwordBox.Password,
            visiblePasswordBox.Text);

    private void SelectAndFocusPassword()
    {
        if (passwordVisibility.IsVisible)
        {
            visiblePasswordBox.SelectAll();
            visiblePasswordBox.Focus();
            return;
        }

        passwordBox.SelectAll();
        passwordBox.Focus();
    }

    private void ClearPassword()
    {
        passwordBox.Clear();
        visiblePasswordBox.Clear();
    }

    private async Task SignInAsync()
    {
        loginButton.IsEnabled = false;
        passwordVisibilityButton.IsEnabled = false;
        statusText.Foreground = StudioTheme.MutedTextBrush;
        statusText.Text = "Лиценз болон бүртгэлийг шалгаж байна...";
        try
        {
            await account.SignInAsync(
                serverBox.Text,
                emailBox.Text,
                CurrentPassword);
            ClearPassword();
            DialogResult = true;
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException or Win32Exception)
        {
            statusText.Foreground = StudioTheme.DangerBrush;
            statusText.Text = exception is TaskCanceledException
                ? "Cloud ERA үйлчилгээ хариу өгөх хугацаа хэтэрлээ."
                : exception.Message;
            SelectAndFocusPassword();
        }
        finally
        {
            loginButton.IsEnabled = true;
            passwordVisibilityButton.IsEnabled = true;
        }
    }
}
