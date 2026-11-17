using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ErkS.Platform.Pdf;
using Microsoft.Win32;

namespace ErkS.Studio;

/// <summary>
/// The signed-in person's prepared signature in Studio: kept with their account,
/// copied to this device, and placed in the album shown here wherever a corner
/// table prints their name (owner, 2026-11-18).
/// </summary>
internal sealed partial class ShellView
{
    private byte[]? signaturePng;
    private string signatureEmail = "";
    private string signatureRequestedEmail = "";

    private static string SignatureCacheFolder =>
        Path.Combine(StudioAccountService.AccountDataRoot, "signature");

    /// <summary>
    /// The signatures this device may place: the signed-in person's own, and
    /// nobody else's. Empty when signed out, acting as a bot, or with none prepared.
    /// </summary>
    private IReadOnlyList<TitleBlockSignature> CurrentTitleBlockSignatures()
    {
        StudioAccountSession? session = account.Current;
        if (session is null || ActingAsBot)
            return [];
        if (!signatureEmail.Equals(session.Email, StringComparison.OrdinalIgnoreCase))
        {
            signaturePng = StudioSignatureImage.ReadCache(
                StudioSignatureImage.CachePath(SignatureCacheFolder, session.Email));
            signatureEmail = session.Email;
        }
        if (signaturePng is null)
            return [];
        IReadOnlyList<string> names = StudioSignatureImage.NamesOf(
            session.FamilyName, session.GivenName, session.DisplayName);
        return names.Count == 0 ? [] : [new TitleBlockSignature(names, signaturePng)];
    }

    /// <summary>Brings this device's copy in line with the account, once per sign-in.</summary>
    private async Task RefreshSignatureFromAccountAsync(StudioAccountSession session, bool force = false)
    {
        if (!force && signatureRequestedEmail.Equals(session.Email, StringComparison.OrdinalIgnoreCase))
            return;
        signatureRequestedEmail = session.Email;
        try
        {
            byte[]? png = await account.GetSignatureImageAsync();
            if (account.Current?.Email.Equals(session.Email, StringComparison.OrdinalIgnoreCase) != true)
                return;
            StoreSignatureLocally(session.Email, png);
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException or TaskCanceledException)
        {
            // Offline or refused: the device copy stands until the account answers.
            signatureRequestedEmail = "";
        }
    }

    private void StoreSignatureLocally(string email, byte[]? png)
    {
        StudioSignatureImage.WriteCache(StudioSignatureImage.CachePath(SignatureCacheFolder, email), png);
        signaturePng = png;
        signatureEmail = email;
    }

    /// <summary>
    /// Signs a built album in place for the person signed in on this device.
    /// Only the album Studio shows here - never the pages it uploads.
    /// </summary>
    private int StampHolderSignatureInPlace(string pdfPath)
    {
        IReadOnlyList<TitleBlockSignature> signatures = CurrentTitleBlockSignatures();
        if (signatures.Count == 0 || string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
            return 0;
        string signed = pdfPath + "." + Guid.NewGuid().ToString("N") + ".signed.tmp";
        try
        {
            TitleBlockSignatureStampResult result = TitleBlockSignatureStamper.Stamp(pdfPath, signed, signatures);
            if (result.Stamped > 0)
                File.Move(signed, pdfPath, overwrite: true);
            return result.Stamped;
        }
        finally
        {
            TryDeleteSignatureTemp(signed);
        }
    }

    private static string SignatureStampSuffix(IReadOnlyList<TitleBlockSignature> signatures) =>
        signatures.Count == 0
            ? ""
            : "-s" + string.Concat(signatures.Select(signature => signature.Key))[..8];

    private static void TryDeleteSignatureTemp(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    // ---- «Гарын үсэг» (account menu) -------------------------------------------

    private MenuItem BuildSignatureMenuItem()
    {
        var item = new MenuItem { Header = "Гарын үсэг…", IsEnabled = account.IsSignedIn && !ActingAsBot };
        item.Click += (_, _) => ShowSignatureDialog();
        return item;
    }

    private void ShowSignatureDialog()
    {
        StudioAccountSession? session = account.Current;
        if (session is null || ActingAsBot)
            return;
        _ = CurrentTitleBlockSignatures();
        IReadOnlyList<string> names = StudioSignatureImage.NamesOf(
            session.FamilyName, session.GivenName, session.DisplayName);

        var window = new Window
        {
            Title = "Гарын үсэг",
            Owner = Window.GetWindow(Root),
            Width = 460,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = StudioTheme.PanelBrush,
        };
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock
        {
            Text = names.Count == 0
                ? "Бүртгэлд овог, нэр бүрэн бичигдээгүй тул гарын үсгийг ямар нэрийн хажууд тавихыг тодорхойлж чадахгүй байна."
                : "Альбумын булангийн хүснэгтэд таны нэр («" + names[0] + "») гарсан нүдний «Гарын үсэг» хэсэгт " +
                  "энэ зураг тавигдана. Зураг таны бүртгэлд хадгалагдана; өөр хүнд харагдахгүй.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = StudioTheme.TextBrush,
            Margin = new Thickness(0, 0, 0, 12),
        });
        var preview = new Image { Height = 90, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 6) };
        var previewHost = new Border
        {
            Background = Brushes.White,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(8),
            Child = preview,
        };
        panel.Children.Add(previewHost);
        var status = new TextBlock
        {
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 12),
        };
        panel.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button choose = StudioWidgets.CreatePrimaryButton("PNG сонгох");
        Button remove = StudioWidgets.CreateButton("Устгах");
        Button close = StudioWidgets.CreateButton("Хаах");
        remove.Margin = new Thickness(8, 0, 0, 0);
        close.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(choose);
        buttons.Children.Add(remove);
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        window.Content = panel;

        void Show()
        {
            preview.Source = signaturePng is null ? null : ToBitmap(signaturePng);
            status.Text = signaturePng is null ? "Гарын үсэг хадгалагдаагүй байна." : "Хадгалагдсан гарын үсэг.";
            remove.IsEnabled = signaturePng is not null;
        }

        choose.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Гарын үсгийн PNG",
                Filter = "Зураг (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(window) != true)
                return;
            choose.IsEnabled = remove.IsEnabled = false;
            status.Text = "Хадгалж байна…";
            string message = await SaveSignatureFromFileAsync(dialog.FileName);
            choose.IsEnabled = true;
            Show();
            status.Text = message;
        };
        remove.Click += async (_, _) =>
        {
            choose.IsEnabled = remove.IsEnabled = false;
            string message = await RemoveSignatureAsync();
            choose.IsEnabled = true;
            Show();
            status.Text = message;
        };
        close.Click += (_, _) => window.Close();
        Show();
        _ = RefreshSignatureFromAccountAsync(session, force: true).ContinueWith(
            _ => dispatcher.Invoke(Show),
            TaskScheduler.Default);
        window.ShowDialog();
    }

    /// <summary>
    /// «PNG сонгох»: keeps the file with the account, then signs the album on
    /// screen again. The bridge's «signature file=…» ends here too.
    /// </summary>
    private async Task<string> SaveSignatureFromFileAsync(string path)
    {
        StudioAccountSession? session = account.Current;
        if (session is null || ActingAsBot)
            return "Өөрийн бүртгэлээр нэвтэрнэ үү.";
        byte[] png;
        try
        {
            png = await File.ReadAllBytesAsync(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return "Файл уншигдсангүй: " + exception.Message;
        }
        try
        {
            png = StudioSignatureImage.Normalize(png);
        }
        catch (Exception exception) when (exception is NotSupportedException or InvalidDataException or FileFormatException or ArgumentException)
        {
            return "Зураг уншигдсангүй: " + exception.Message;
        }
        string? invalid = StudioSignatureImage.Validate(png);
        if (invalid is not null)
            return invalid;
        try
        {
            await account.UploadSignatureImageAsync(png);
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException)
        {
            return "Бүртгэлд хадгалагдсангүй: " + exception.Message;
        }
        StoreSignatureLocally(session.Email, png);
        RefreshShownAlbumAfterSignatureChange();
        return "Гарын үсэг бүртгэлд хадгалагдлаа. Альбумд таны нэртэй нүдэнд тавигдана.";
    }

    private async Task<string> RemoveSignatureAsync()
    {
        StudioAccountSession? session = account.Current;
        if (session is null || ActingAsBot)
            return "Өөрийн бүртгэлээр нэвтэрнэ үү.";
        try
        {
            await account.DeleteSignatureImageAsync();
        }
        catch (Exception exception) when (exception is StudioAccountException or HttpRequestException)
        {
            return "Устгагдсангүй: " + exception.Message;
        }
        StoreSignatureLocally(session.Email, null);
        RefreshShownAlbumAfterSignatureChange();
        return "Гарын үсэг устгагдлаа.";
    }

    private void RefreshShownAlbumAfterSignatureChange()
    {
        if (state.HasOpenProject)
            UpdateAlbum(silent: true, statusPrefix: "Гарын үсэг шинэчлэгдлээ");
    }

    private static BitmapImage ToBitmap(byte[] png)
    {
        using var stream = new MemoryStream(png, writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    // ---- bridge -----------------------------------------------------------------

    /// <summary>
    /// «signature»: the same functions as the dialog's buttons. No argument
    /// reports whether a signature is kept and which names it signs.
    /// </summary>
    private async Task<StudioBridgeResponse> BridgeSignatureAsync(StudioBridgeRequest request)
    {
        StudioAccountSession? session = account.Current;
        if (session is null || ActingAsBot)
            return StudioBridgeProtocol.Refuse(request.Id, "not_signed_in", "Өөрийн бүртгэлээр нэвтэрнэ үү.");
        string? file = request.Arg("file");
        bool remove = (request.Arg("remove") ?? "").Trim() is "1" or "true" or "yes";
        string message = "";
        if (!string.IsNullOrWhiteSpace(file))
            message = await SaveSignatureFromFileAsync(file.Trim());
        else if (remove)
            message = await RemoveSignatureAsync();
        else
            await RefreshSignatureFromAccountAsync(session, force: true);
        _ = CurrentTitleBlockSignatures();
        return StudioBridgeProtocol.Accept(
            request.Id,
            new
            {
                hasSignature = signaturePng is not null,
                bytes = signaturePng?.Length ?? 0,
                names = StudioSignatureImage.NamesOf(session.FamilyName, session.GivenName, session.DisplayName),
                album = lastAlbumPath,
            },
            message);
    }
}
