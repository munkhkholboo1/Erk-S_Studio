using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// Shown when Studio may not open: the account behind it holds no active
/// Platform or CityGen licence, or this device has been away from the server
/// too long to keep vouching for one. It offers the three ways out — check
/// again, obtain a licence, or close Studio — and nothing else.
/// </summary>
internal sealed class StudioCompanionRequiredDialog : Window
{
    private readonly string subscribeUrl;

    public StudioCompanionRequiredDialog(StudioCompanionDecision decision, string serverUrl)
    {
        ArgumentNullException.ThrowIfNull(decision);
        subscribeUrl = BuildSubscribeUrl(serverUrl);
        Title = "Erk-S Studio — лиценз шаардлагатай";
        Width = 560;
        Height = 340;
        MinWidth = 480;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        StudioTheme.Apply(this);
        Content = BuildContent(decision);
    }

    /// <summary>True when the user asked to check the licence again.</summary>
    public bool RetryRequested => DialogResult == true;

    private UIElement BuildContent(StudioCompanionDecision decision)
    {
        var root = new DockPanel { Margin = new Thickness(24) };

        Button retryButton = StudioWidgets.CreatePrimaryButton(
            decision.NeedsOnlineCheck ? "Нэвтэрч шалгах" : "Дахин шалгах");
        retryButton.IsDefault = true;
        retryButton.Click += (_, _) => DialogResult = true;

        Button subscribeButton = StudioWidgets.CreateButton("Лиценз авах");
        subscribeButton.Click += (_, _) => OpenSubscribePage();

        Button closeButton = StudioWidgets.CreateButton("Studio хаах");
        closeButton.IsCancel = true;
        closeButton.Click += (_, _) => DialogResult = false;

        var actions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        actions.Children.Add(closeButton);
        actions.Children.Add(subscribeButton);
        actions.Children.Add(retryButton);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        var content = new StackPanel();
        content.Children.Add(StudioWidgets.CreateTitle("Идэвхтэй лиценз шаардлагатай"));
        content.Children.Add(StudioWidgets.CreateHint(Explain(decision)));
        content.Children.Add(new Border { Height = 14 });
        content.Children.Add(new TextBlock
        {
            Text =
                "Erk-S Studio нь Platform эсвэл CityGen программын аль нэг идэвхтэй " +
                "лицензтэй бүртгэлд үнэ төлбөргүй дагалдана.",
            Foreground = StudioTheme.MutedTextBrush,
            FontSize = StudioTheme.HintFontSize,
            TextWrapping = TextWrapping.Wrap,
        });
        root.Children.Add(content);
        return root;
    }

    private static string Explain(StudioCompanionDecision decision) => decision.Outcome switch
    {
        StudioCompanionOutcome.BlockedNoLicense =>
            "Энэ бүртгэлд идэвхтэй Platform эсвэл CityGen лиценз алга байна. " +
            "Лиценз авсны дараа Studio нээгдэнэ.",
        StudioCompanionOutcome.BlockedGraceExpired =>
            "Энэ төхөөрөмж сервертэй холбогдоогүй хугацаа хэтэрсэн тул лицензээ " +
            "дахин шалгуулах шаардлагатай. Интернэтэд холбогдоод дахин оролдоно уу.",
        _ =>
            "Studio нээхийн тулд эхлээд бүртгэлээрээ нэвтэрч лицензээ шалгуулна уу.",
    };

    private static string BuildSubscribeUrl(string serverUrl)
    {
        string root = string.IsNullOrWhiteSpace(serverUrl)
            ? StudioReleaseInfo.DefaultServerUrl
            : serverUrl.Trim();
        return root.TrimEnd('/') + "/subscribe";
    }

    private void OpenSubscribePage()
    {
        if (!Uri.TryCreate(subscribeUrl, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
        }
        catch (Exception exception) when (
            exception is System.ComponentModel.Win32Exception or InvalidOperationException or System.IO.IOException)
        {
        }
    }
}
