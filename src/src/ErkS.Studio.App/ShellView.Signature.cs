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
        return names.Count == 0
            ? []
            : [new TitleBlockSignature(names, signaturePng, StudioSignatureImage.ReadHeightMm(signaturePng))];
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

    /// <summary>
    /// The three corner-table rows a signature lands in, at their real size:
    /// the working table (6 mm rows), the vertical one (5 mm, signature before
    /// the name) and the concept table (7 mm). The preview draws the chosen
    /// height in all three by the same rule the album uses.
    /// </summary>
    private static readonly (string Title, (string Text, double WidthMm, bool Signature)[] Cells, double HeightMm)[] SignaturePreviewRows =
    [
        ("Ажлын зураг — хэвтээ хүснэгт", [("Архитектор", 20, false), ("NAME", 25, false), ("", 25, true)], 6),
        ("Ажлын зураг — босоо хүснэгт", [("Архитектор", 25, false), ("", 23.7, true), ("NAME", 21.3, false)], 5),
        ("Загвар зураг", [("Архитектор", 26, false), ("NAME", 34, false), ("", 30, true)], 7),
    ];

    private const double SignaturePreviewPixelsPerMm = 5;
    private const double PointsPerMm = 72d / 25.4;

    /// <summary>
    /// The preview rows - the dialog's and the bridge's «signature preview=» -
    /// drawn by the same placement rule the album uses.
    /// </summary>
    private static void BuildSignaturePreview(StackPanel target, BitmapImage? bitmap, double? heightMm, string printedName)
    {
        target.Children.Clear();
        foreach (var row in SignaturePreviewRows)
        {
            target.Children.Add(new TextBlock
            {
                Text = row.Title,
                FontSize = 11,
                Foreground = Brushes.DimGray,
                Margin = new Thickness(0, 6, 0, 2),
            });
            var canvas = new Canvas
            {
                Width = row.Cells.Sum(cell => cell.WidthMm) * SignaturePreviewPixelsPerMm,
                Height = row.HeightMm * SignaturePreviewPixelsPerMm,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 8),
            };
            double x = 0;
            foreach (var cell in row.Cells)
            {
                double width = cell.WidthMm * SignaturePreviewPixelsPerMm;
                var frame = new System.Windows.Shapes.Rectangle
                {
                    Width = width,
                    Height = canvas.Height,
                    Stroke = Brushes.Black,
                    StrokeThickness = 0.7,
                };
                Canvas.SetLeft(frame, x);
                canvas.Children.Add(frame);
                string text = cell.Text == "NAME" ? printedName : cell.Text;
                if (text.Length > 0)
                {
                    var label = new TextBlock
                    {
                        Text = text,
                        FontSize = 2.2 * SignaturePreviewPixelsPerMm,
                        Foreground = Brushes.Black,
                        Width = width,
                        TextAlignment = cell.Text == "NAME" ? TextAlignment.Center : TextAlignment.Left,
                        Padding = new Thickness(3, 0, 3, 0),
                    };
                    Canvas.SetLeft(label, x);
                    Canvas.SetTop(label, (canvas.Height - label.FontSize * 1.33) / 2);
                    canvas.Children.Add(label);
                }
                if (cell.Signature && bitmap is not null)
                {
                    PdfSharp.Drawing.XRect placed = TitleBlockSignatureStamper.Fit(
                        bitmap.PixelWidth,
                        bitmap.PixelHeight,
                        new PdfSharp.Drawing.XRect(0, 0, cell.WidthMm * PointsPerMm, row.HeightMm * PointsPerMm),
                        heightMm);
                    var image = new Image
                    {
                        Source = bitmap,
                        Width = placed.Width / PointsPerMm * SignaturePreviewPixelsPerMm,
                        Height = placed.Height / PointsPerMm * SignaturePreviewPixelsPerMm,
                        Stretch = Stretch.Fill,
                    };
                    Canvas.SetLeft(image, x + placed.Left / PointsPerMm * SignaturePreviewPixelsPerMm);
                    Canvas.SetTop(image, placed.Top / PointsPerMm * SignaturePreviewPixelsPerMm);
                    Panel.SetZIndex(image, 10);
                    canvas.Children.Add(image);
                }
                x += width;
            }
            target.Children.Add(canvas);
        }
    }

    /// <summary>Renders the preview to a PNG file without saving any setting.</summary>
    private void RenderSignaturePreview(string path, double? heightMm)
    {
        StudioAccountSession? session = account.Current;
        string printedName = session is null
            ? "Э.Нэр"
            : StudioSignatureImage.NamesOf(session.FamilyName, session.GivenName, session.DisplayName).FirstOrDefault() ?? "Э.Нэр";
        BitmapImage? bitmap = signaturePng is null ? null : ToBitmap(signaturePng);
        var stack = new StackPanel();
        BuildSignaturePreview(stack, bitmap, heightMm ?? StudioSignatureImage.ReadHeightMm(signaturePng), printedName);
        var host = new Border { Background = Brushes.White, Padding = new Thickness(10, 4, 10, 10), Child = stack };
        host.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        host.Arrange(new Rect(host.DesiredSize));
        host.UpdateLayout();
        var render = new RenderTargetBitmap(
            (int)Math.Ceiling(host.ActualWidth * 2), (int)Math.Ceiling(host.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
        render.Render(host);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(render));
        using FileStream file = File.Create(path);
        encoder.Save(file);
    }

    private void ShowSignatureDialog()
    {
        StudioAccountSession? session = account.Current;
        if (session is null || ActingAsBot)
            return;
        _ = CurrentTitleBlockSignatures();
        IReadOnlyList<string> names = StudioSignatureImage.NamesOf(
            session.FamilyName, session.GivenName, session.DisplayName);
        string printedName = names.Count > 0 ? names[0] : "Э.Нэр";

        var window = new Window
        {
            Title = "Гарын үсэг",
            Owner = Window.GetWindow(Root),
            Width = 520,
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
                : "Альбумын булангийн хүснэгтэд таны нэр («" + printedName + "») гарсан нүдний «Гарын үсэг» хэсэгт " +
                  "энэ зураг тавигдана. Зураг таны бүртгэлд хадгалагдана; өөр хүнд харагдахгүй.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = StudioTheme.TextBrush,
            Margin = new Thickness(0, 0, 0, 12),
        });

        var previewStack = new StackPanel();
        panel.Children.Add(new Border
        {
            Background = Brushes.White,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 4, 10, 10),
            Child = previewStack,
        });

        var sizeLabel = new TextBlock { Foreground = StudioTheme.TextBrush, Margin = new Thickness(0, 12, 0, 4) };
        panel.Children.Add(sizeLabel);
        var slider = new Slider
        {
            Minimum = TitleBlockSignature.MinHeightMm,
            Maximum = 15,
            TickFrequency = 0.5,
            IsSnapToTickEnabled = true,
            SmallChange = 0.5,
            LargeChange = 1,
        };
        panel.Children.Add(slider);
        panel.Children.Add(new TextBlock
        {
            Text = "Гараар зурдаг хэмжээгээрээ нэг удаа тохируулна — бүх хүснэгтэд яг энэ өндрөөр, нүдний голд тавигдана.",
            Foreground = StudioTheme.MutedTextBrush,
            FontSize = 11.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0),
        });
        var status = new TextBlock
        {
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 12),
        };
        panel.Children.Add(status);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        Button saveSize = StudioWidgets.CreatePrimaryButton("Хэмжээг хадгалах");
        Button choose = StudioWidgets.CreateButton("Зураг сонгох");
        Button remove = StudioWidgets.CreateButton("Устгах");
        Button close = StudioWidgets.CreateButton("Хаах");
        foreach (Button button in new[] { choose, remove, close })
            button.Margin = new Thickness(8, 0, 0, 0);
        buttons.Children.Add(saveSize);
        buttons.Children.Add(choose);
        buttons.Children.Add(remove);
        buttons.Children.Add(close);
        panel.Children.Add(buttons);
        window.Content = panel;

        BitmapImage? bitmap = null;
        double? savedHeight = null;
        bool loading = false;

        // Until a height is chosen the signature is fitted inside the working
        // table's cell; the slider starts from what that looks like.
        double FittedHeightMm()
        {
            if (bitmap is null)
                return 5;
            PdfSharp.Drawing.XRect placed = TitleBlockSignatureStamper.Fit(
                bitmap.PixelWidth, bitmap.PixelHeight,
                new PdfSharp.Drawing.XRect(0, 0, 25 * PointsPerMm, 6 * PointsPerMm), null);
            return Math.Max(TitleBlockSignature.MinHeightMm, Math.Round(placed.Height / PointsPerMm * 2) / 2);
        }

        void DrawPreview()
        {
            BuildSignaturePreview(previewStack, bitmap, bitmap is null ? null : slider.Value, printedName);
            double widthMm = bitmap is null || bitmap.PixelHeight == 0
                ? 0
                : slider.Value * bitmap.PixelWidth / bitmap.PixelHeight;
            sizeLabel.Text = bitmap is null
                ? "Хэмжээ"
                : $"Өндөр: {slider.Value:0.0} мм   ·   өргөн ≈ {widthMm:0} мм";
            saveSize.IsEnabled = bitmap is not null &&
                (savedHeight is null || Math.Abs(savedHeight.Value - slider.Value) > 0.01);
        }

        void Show()
        {
            loading = true;
            bitmap = signaturePng is null ? null : ToBitmap(signaturePng);
            savedHeight = StudioSignatureImage.ReadHeightMm(signaturePng);
            slider.Value = savedHeight ?? FittedHeightMm();
            slider.IsEnabled = bitmap is not null;
            remove.IsEnabled = bitmap is not null;
            status.Text = bitmap is null
                ? "Гарын үсэг хадгалагдаагүй байна."
                : savedHeight is null
                    ? "Одоо нүдэндээ багтаж тавигдаж байна. Хэмжээгээ тохируулаад «Хэмжээг хадгалах» дарна уу."
                    : "Хадгалагдсан хэмжээ: " + savedHeight.Value.ToString("0.0") + " мм.";
            loading = false;
            DrawPreview();
        }

        slider.ValueChanged += (_, _) =>
        {
            if (!loading)
                DrawPreview();
        };
        saveSize.Click += async (_, _) =>
        {
            saveSize.IsEnabled = choose.IsEnabled = remove.IsEnabled = false;
            status.Text = "Хадгалж байна…";
            string message = await SaveSignatureHeightAsync(slider.Value);
            choose.IsEnabled = true;
            Show();
            status.Text = message;
        };
        choose.Click += async (_, _) =>
        {
            var dialog = new OpenFileDialog
            {
                Title = "Гарын үсгийн зураг",
                Filter = "Зураг (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
                CheckFileExists = true,
            };
            if (dialog.ShowDialog(window) != true)
                return;
            choose.IsEnabled = remove.IsEnabled = saveSize.IsEnabled = false;
            status.Text = "Хадгалж байна…";
            string message = await SaveSignatureFromFileAsync(dialog.FileName);
            choose.IsEnabled = true;
            Show();
            status.Text = message;
        };
        remove.Click += async (_, _) =>
        {
            choose.IsEnabled = remove.IsEnabled = saveSize.IsEnabled = false;
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
    /// «Зураг сонгох»: keeps the file with the account, then signs the album on
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
        // A new picture keeps the height already chosen.
        _ = CurrentTitleBlockSignatures();
        if (StudioSignatureImage.ReadHeightMm(signaturePng) is { } keptHeight)
            png = StudioSignatureImage.WithHeightMm(png, keptHeight);
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

    /// <summary>
    /// «Хэмжээг хадгалах»: the chosen height goes into the PNG and with it to
    /// the account, then the album on screen is signed again at that size.
    /// The bridge's «signature height=…» ends here too.
    /// </summary>
    private async Task<string> SaveSignatureHeightAsync(double heightMm)
    {
        StudioAccountSession? session = account.Current;
        if (session is null || ActingAsBot)
            return "Өөрийн бүртгэлээр нэвтэрнэ үү.";
        _ = CurrentTitleBlockSignatures();
        if (signaturePng is null)
            return "Эхлээд гарын үсгийн зургаа оруулна уу.";
        double height = Math.Clamp(heightMm, TitleBlockSignature.MinHeightMm, TitleBlockSignature.MaxHeightMm);
        byte[] png = StudioSignatureImage.WithHeightMm(signaturePng, height);
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
        return $"Хэмжээ хадгалагдлаа: {height:0.0} мм. Альбум шинэчлэгдлээ.";
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
        string? previewPath = request.Arg("preview");
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            _ = CurrentTitleBlockSignatures();
            double? tryMm = double.TryParse((request.Arg("try") ?? "").Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double parsed) && parsed > 0 ? parsed : null;
            RenderSignaturePreview(previewPath.Trim(), tryMm);
            return StudioBridgeProtocol.Accept(request.Id, new { preview = previewPath.Trim(), tryMm }, "Урьдчилсан харагдац (хадгалаагүй).");
        }
        string? heightText = request.Arg("height");
        double heightMm = 0;
        if (!string.IsNullOrWhiteSpace(heightText) &&
            !double.TryParse(heightText.Trim(), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out heightMm))
        {
            return StudioBridgeProtocol.Refuse(request.Id, "bad_request", "height=<мм> тоо байна.");
        }
        string message = "";
        if (!string.IsNullOrWhiteSpace(file))
            message = await SaveSignatureFromFileAsync(file.Trim());
        else if (remove)
            message = await RemoveSignatureAsync();
        else if (heightMm <= 0)
            await RefreshSignatureFromAccountAsync(session, force: true);
        if (heightMm > 0)
            message = await SaveSignatureHeightAsync(heightMm);
        _ = CurrentTitleBlockSignatures();
        return StudioBridgeProtocol.Accept(
            request.Id,
            new
            {
                hasSignature = signaturePng is not null,
                bytes = signaturePng?.Length ?? 0,
                heightMm = StudioSignatureImage.ReadHeightMm(signaturePng),
                names = StudioSignatureImage.NamesOf(session.FamilyName, session.GivenName, session.DisplayName),
                album = lastAlbumPath,
            },
            message);
    }
}
