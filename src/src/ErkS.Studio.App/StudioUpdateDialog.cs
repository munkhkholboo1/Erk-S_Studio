using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;

namespace ErkS.Studio;

internal sealed class StudioUpdateDialog : Window
{
    private readonly StudioUpdateService updateService;
    private readonly StudioUpdateLatestResponse update;
    private readonly TextBlock statusText = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ProgressBar progressBar = new() { Height = 8, Minimum = 0, Maximum = 100, Visibility = Visibility.Collapsed };
    private readonly Button installButton;
    private readonly Button cancelButton;
    private readonly CancellationTokenSource cancellation = new();
    private bool installing;
    private bool allowClose;

    public StudioUpdateDialog(StudioUpdateService updateService, StudioUpdateLatestResponse update)
    {
        this.updateService = updateService;
        this.update = update;
        Title = "Erk-S Studio шинэчлэлт";
        Width = 590;
        Height = 430;
        MinWidth = 520;
        MinHeight = 380;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResize;
        installButton = StudioWidgets.CreatePrimaryButton("Татаж суулгах");
        cancelButton = StudioWidgets.CreateButton("Дараа");
        StudioTheme.Apply(this);
        Content = BuildContent();
        Closing += OnClosing;
    }

    private UIElement BuildContent()
    {
        var root = new DockPanel { Margin = new Thickness(24) };
        var actions = new WrapPanel
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        cancelButton.IsCancel = true;
        cancelButton.Click += (_, _) =>
        {
            if (installing)
                cancellation.Cancel();
            else
                DialogResult = false;
        };
        installButton.IsDefault = true;
        installButton.Click += async (_, _) => await DownloadAndInstallAsync();
        actions.Children.Add(cancelButton);
        actions.Children.Add(installButton);
        DockPanel.SetDock(actions, Dock.Bottom);
        root.Children.Add(actions);

        statusText.Foreground = StudioTheme.MutedTextBrush;
        statusText.Margin = new Thickness(0, 10, 0, 0);
        DockPanel.SetDock(statusText, Dock.Bottom);
        root.Children.Add(statusText);
        progressBar.Margin = new Thickness(0, 14, 0, 0);
        DockPanel.SetDock(progressBar, Dock.Bottom);
        root.Children.Add(progressBar);

        var content = new StackPanel();
        content.Children.Add(StudioWidgets.CreateTitle($"Erk-S Studio {update.Version}"));
        content.Children.Add(StudioWidgets.CreateHint(
            "Шинэ хувилбарыг татаж шалгасны дараа Studio хаагдаж, суулгалт дуусаад автоматаар дахин нээгдэнэ."));
        content.Children.Add(new Border { Height = 18 });
        content.Children.Add(StudioWidgets.CreateSectionHeader("Шинэ хувилбарын мэдээлэл"));
        content.Children.Add(new Border
        {
            Background = StudioTheme.PanelBrush,
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(StudioTheme.CornerRadius),
            Padding = new Thickness(14),
            Margin = new Thickness(0, 4, 0, 0),
            Child = new ScrollViewer
            {
                MaxHeight = 160,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(update.ReleaseNotes)
                        ? "Тогтвортой ажиллагаа болон бүтээгдэхүүний шинэчлэлт."
                        : update.ReleaseNotes,
                    Foreground = StudioTheme.TextBrush,
                    TextWrapping = TextWrapping.Wrap,
                },
            },
        });
        if (update.IsRequired)
        {
            content.Children.Add(new TextBlock
            {
                Text = "Энэ нь зайлшгүй шаардлагатай шинэчлэлт байна.",
                Foreground = StudioTheme.WarningBrush,
                Margin = new Thickness(0, 12, 0, 0),
            });
        }
        root.Children.Add(content);
        return root;
    }

    private async Task DownloadAndInstallAsync()
    {
        if (installing)
            return;

        installing = true;
        installButton.IsEnabled = false;
        cancelButton.Content = "Цуцлах";
        progressBar.Visibility = Visibility.Visible;
        var progress = new Progress<StudioUpdateProgress>(value =>
        {
            progressBar.IsIndeterminate = !value.Percent.HasValue;
            if (value.Percent.HasValue)
                progressBar.Value = value.Percent.Value;
            statusText.Foreground = StudioTheme.MutedTextBrush;
            statusText.Text = value.Message;
        });

        try
        {
            string installerPath = await updateService.DownloadAsync(update, progress, cancellation.Token);
            statusText.Text = "Studio-г хааж шинэчлэлтийг суулгаж байна...";
            StudioUpdateService.LaunchInstaller(installerPath);
            allowClose = true;
            DialogResult = true;
            Application.Current.Shutdown();
        }
        catch (OperationCanceledException)
        {
            statusText.Text = "Шинэчлэлт цуцлагдлаа.";
        }
        catch (Exception exception) when (
            exception is HttpRequestException or IOException or InvalidDataException or Win32Exception or InvalidOperationException)
        {
            statusText.Foreground = StudioTheme.DangerBrush;
            statusText.Text = "Шинэчлэлт суусангүй: " + exception.Message;
        }
        finally
        {
            installing = false;
            installButton.IsEnabled = true;
            cancelButton.Content = "Хаах";
            progressBar.IsIndeterminate = false;
        }
    }

    private void OnClosing(object? sender, CancelEventArgs args)
    {
        if (!installing || allowClose)
        {
            cancellation.Dispose();
            return;
        }

        cancellation.Cancel();
        args.Cancel = true;
    }
}
