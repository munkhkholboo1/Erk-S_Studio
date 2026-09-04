using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace ErkS.Studio;

/// <summary>
/// What a seated machine shows when Studio opens: the bot's tile and a PIN
/// box, the way a shared workstation opens into a guest account.
///
/// The tile is the main road and "sign in as the owner" is a small second line
/// - and that one asks for the full credential, not a PIN. The asymmetry is
/// not a preference: entering bot state erased the owner's credential, so
/// coming back cannot be anything but a fresh sign-in. If four digits restored
/// owner rights, four digits would open the whole organisation.
/// </summary>
internal sealed class BotLockScreen : Grid
{
    private readonly StudioBotDeviceState seat;
    private readonly PasswordBox pinBox = new()
    {
        MaxLength = 4,
        FontSize = 22,
        Width = 120,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Margin = new Thickness(0, 6, 0, 0),
    };
    private readonly TextBlock messageText = new()
    {
        Foreground = StudioTheme.MutedTextBrush,
        TextWrapping = TextWrapping.Wrap,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(0, 10, 0, 0),
        MaxWidth = 320,
    };
    private readonly Button unlockButton;

    /// <summary>Raised with the seat identity once the right PIN is entered.</summary>
    public event Action<string>? Unlocked;

    /// <summary>Raised when the person chooses to sign in as the owner instead.</summary>
    public event Action? OwnerSignInRequested;

    /// <summary>Raised when the attempts run out and the seat locks itself.</summary>
    public event Action? LockedOut;

    public BotLockScreen(StudioBotDeviceState seat)
    {
        this.seat = seat;
        Background = StudioTheme.WindowBackgroundBrush;

        unlockButton = StudioWidgets.CreatePrimaryButton("Нэвтрэх");
        unlockButton.IsDefault = true;
        unlockButton.Margin = new Thickness(0, 12, 0, 0);
        unlockButton.HorizontalAlignment = HorizontalAlignment.Center;
        unlockButton.Click += (_, _) => Attempt();
        pinBox.PasswordChanged += (_, _) =>
        {
            unlockButton.IsEnabled = StudioBotPinVault.IsWellFormedPin(pinBox.Password);
            if (unlockButton.IsEnabled)
                Attempt();
        };
        pinBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                Attempt();
        };

        var tile = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        tile.Children.Add(new TextBlock
        {
            Text = "🤖",
            FontSize = 44,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        tile.Children.Add(new TextBlock
        {
            Text = seat.DisplayName,
            FontSize = 19,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        });
        tile.Children.Add(new TextBlock
        {
            Text = "Энэ төхөөрөмж ботын суудал",
            Foreground = StudioTheme.MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var card = new Border
        {
            Background = StudioTheme.PanelBrush,
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(34, 28, 34, 26),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        panel.Children.Add(tile);
        panel.Children.Add(new TextBlock
        {
            Text = "ПИН",
            Foreground = StudioTheme.MutedTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
        });
        panel.Children.Add(pinBox);
        panel.Children.Add(unlockButton);
        panel.Children.Add(messageText);

        Button ownerLink = StudioWidgets.CreateInlineButton("Эзэмшигчээр нэвтрэх →");
        ownerLink.HorizontalAlignment = HorizontalAlignment.Center;
        ownerLink.Margin = new Thickness(0, 18, 0, 0);
        ownerLink.Click += (_, _) => OwnerSignInRequested?.Invoke();
        panel.Children.Add(ownerLink);

        card.Child = panel;
        Children.Add(card);

        if (seat.IsLocked)
        {
            ShowLocked();
        }
        else
        {
            unlockButton.IsEnabled = false;
            Loaded += (_, _) => pinBox.Focus();
        }
    }

    private void ShowLocked()
    {
        pinBox.IsEnabled = false;
        unlockButton.IsEnabled = false;
        messageText.Foreground = StudioTheme.DangerBrush;
        messageText.Text =
            "Энэ суудал буруу ПИН-ээр түгжигдсэн байна. Эзэмшигч алсаас тайлна.";
    }

    private void Attempt()
    {
        if (seat.IsLocked || !StudioBotPinVault.IsWellFormedPin(pinBox.Password))
            return;

        string? identity = seat.TryUnlock(pinBox.Password);
        if (identity is not null)
        {
            seat.FailedPinAttempts = 0;
            StudioBotDeviceStateStore.Write(seat);
            Unlocked?.Invoke(identity);
            return;
        }

        seat.FailedPinAttempts++;
        pinBox.Clear();
        if (seat.FailedPinAttempts >= StudioBotDeviceState.MaximumPinAttempts)
        {
            seat.LockedAtUtc = DateTimeOffset.UtcNow;
            StudioBotDeviceStateStore.Write(seat);
            ShowLocked();
            // The server counts nothing; it is told that this device locked
            // itself, and the owner clears it from their own account.
            LockedOut?.Invoke();
            return;
        }

        StudioBotDeviceStateStore.Write(seat);
        messageText.Foreground = StudioTheme.DangerBrush;
        messageText.Text =
            $"ПИН буруу байна. Үлдсэн оролдлого: " +
            $"{StudioBotDeviceState.MaximumPinAttempts - seat.FailedPinAttempts}.";
    }
}
