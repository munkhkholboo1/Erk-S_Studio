using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using ErkS.Platform.Pdf;
using Microsoft.Win32;

namespace ErkS.Studio;

/// <summary>
/// The Erk-S PDF Sheet Splitter in Studio (owner, 2026-10-29: «студиод Erk-S Sheet Splitter г нэмэх цаг
/// болсон»): the album is saved whole or cut into A3 pieces, and how it cuts can be shown over the album
/// preview - a switch, off when not wanted.
/// </summary>
internal sealed partial class ShellView
{
    private readonly CheckBox albumSplitViewCheck = new()
    {
        Content = "Хуваалт харах",
        ToolTip = "Альбумын том хуудас A3-аар хэрхэн хуваагдахыг (хэсэг бүрийн хүрээ, дугаар, нийлүүлэх шугам) урьдчилан харахад харуулна.",
        Margin = new Thickness(8, 0, 0, 0),
        VerticalAlignment = VerticalAlignment.Center,
    };

    private string? shownAlbumPdfPath;
    private int shownAlbumPdfPage = 1;

    private static readonly string AlbumViewPreferencePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Erk-S Studio",
        "album-view.json");

    private static readonly string SplitPreviewRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Erk-S Studio",
        "split-preview");

    private sealed record AlbumViewPreference(bool SplitView);

    private Button CreateAlbumPdfSaveButton()
    {
        var save = StudioWidgets.CreateButton("PDF хадгалах");
        save.ToolTip = "Альбумыг бүтэн форматаар эсвэл A3-аар хуваасан PDF болгон хадгална.";
        var menu = new ContextMenu();
        var whole = new MenuItem { Header = "Бүтэн форматаар" };
        whole.Click += (_, _) => SaveAlbumPdf(split: false, path: null);
        var split = new MenuItem { Header = "A3-аар хуваасан" };
        split.Click += (_, _) => SaveAlbumPdf(split: true, path: null);
        menu.Items.Add(whole);
        menu.Items.Add(split);
        save.ContextMenu = menu;
        save.Click += (_, _) =>
        {
            menu.PlacementTarget = save;
            menu.IsOpen = true;
        };

        albumSplitViewCheck.IsChecked = LoadAlbumViewPreference().SplitView;
        albumSplitViewCheck.Checked += (_, _) => SetAlbumSplitView(true);
        albumSplitViewCheck.Unchecked += (_, _) => SetAlbumSplitView(false);
        return save;
    }

    private bool AlbumSplitViewOn => albumSplitViewCheck.IsChecked == true;

    private void SetAlbumSplitView(bool on)
    {
        if (albumSplitViewCheck.IsChecked != on)
        {
            albumSplitViewCheck.IsChecked = on; // raises the handler, which comes back here
            return;
        }
        SaveAlbumViewPreference(new AlbumViewPreference(on));
        if (!string.IsNullOrWhiteSpace(shownAlbumPdfPath) && File.Exists(shownAlbumPdfPath))
            ShowAlbumPdfPage(shownAlbumPdfPath, shownAlbumPdfPage);
    }

    /// <summary>The album whole, or cut into A3 pieces, to <paramref name="path"/> (asked for when null). Null on cancel.</summary>
    private SheetSplitResult? SaveAlbumPdf(bool split, string? path)
    {
        string? album = ResolveAlbumPreviewPath();
        if (string.IsNullOrWhiteSpace(album) || !File.Exists(album))
        {
            SetStatus("Хадгалах альбум алга. Эхлээд альбумаа угсарна уу.");
            return null;
        }
        if (path is null)
        {
            string baseName = state.HasOpenProject ? state.Project.Identity.Code : Path.GetFileNameWithoutExtension(album);
            var dialog = new SaveFileDialog
            {
                Title = split ? "Альбумыг A3-аар хувааж хадгалах" : "Альбумыг бүтэн форматаар хадгалах",
                Filter = "PDF|*.pdf",
                DefaultExt = ".pdf",
                FileName = SafeFileName(baseName + (split ? "_A3" : "")) + ".pdf",
            };
            if (dialog.ShowDialog(Window.GetWindow(Root)) != true)
                return null;
            path = dialog.FileName;
        }

        try
        {
            SheetSplitResult result;
            if (split)
            {
                result = AlbumSheetSplitWriter.Split(album, path, drawJoinGuides: true);
                SetStatus($"Альбум A3-аар хуваагдаж хадгалагдлаа: {result.SourcePages} хуудас → {result.OutputPages} хуудас ({result.SplitPages} том хуудас хуваагдсан) - {path}");
            }
            else
            {
                if (string.Equals(Path.GetFullPath(album), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Эх альбум дээр дарж бичихгүй — өөр файлд хадгална.");
                File.Copy(album, path, overwrite: true);
                int pages = BridgePdfPageCount(path);
                result = new SheetSplitResult(pages, pages, 0);
                SetStatus($"Альбум бүтэн форматаар хадгалагдлаа ({pages} хуудас) - {path}");
            }
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or PdfSharp.PdfSharpException)
        {
            SetStatus("Альбум хадгалагдсангүй: " + exception.Message);
            return null;
        }
    }

    /// <summary>The PDF the preview shows: the album itself, or - with «Хуваалт харах» on - a copy with the cuts drawn.</summary>
    private async Task<string> ResolveShownAlbumPdfAsync(string pdfPath)
    {
        if (!AlbumSplitViewOn)
            return pdfPath;
        try
        {
            var file = new FileInfo(pdfPath);
            string key = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
                $"{file.FullName.ToLowerInvariant()}|{file.LastWriteTimeUtc.Ticks}|{file.Length}|split-v1")))[..24];
            string preview = Path.Combine(SplitPreviewRoot, key + ".pdf");
            if (!File.Exists(preview))
            {
                Directory.CreateDirectory(SplitPreviewRoot);
                string temporary = preview + ".tmp";
                await Task.Run(() => AlbumSheetSplitWriter.WriteSplitPreview(pdfPath, temporary));
                File.Move(temporary, preview, overwrite: true);
                TidySplitPreviews(keep: preview);
            }
            return preview;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or PdfSharp.PdfSharpException)
        {
            SetStatus("Хуваалтыг харуулж чадсангүй: " + exception.Message);
            return pdfPath;
        }
    }

    /// <summary>Only the newest few previews are kept: each album rebuild makes a new one.</summary>
    private static void TidySplitPreviews(string keep)
    {
        try
        {
            foreach (FileInfo old in new DirectoryInfo(SplitPreviewRoot).EnumerateFiles("*.pdf")
                         .OrderByDescending(file => file.LastWriteTimeUtc)
                         .Skip(4)
                         .Where(file => !string.Equals(file.FullName, keep, StringComparison.OrdinalIgnoreCase)))
            {
                old.Delete();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static AlbumViewPreference LoadAlbumViewPreference()
    {
        try
        {
            return File.Exists(AlbumViewPreferencePath)
                ? JsonSerializer.Deserialize<AlbumViewPreference>(File.ReadAllText(AlbumViewPreferencePath)) ?? new(false)
                : new(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(false);
        }
    }

    private static void SaveAlbumViewPreference(AlbumViewPreference preference)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AlbumViewPreferencePath)!);
            File.WriteAllText(AlbumViewPreferencePath, JsonSerializer.Serialize(preference));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Bridge «album-save»: format=full|split path=&lt;.pdf&gt;.</summary>
    private StudioBridgeResponse BridgeAlbumSave(StudioBridgeRequest request)
    {
        string? path = request.Arg("path");
        string format = (request.Arg("format") ?? "full").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(path) || format is not ("full" or "split"))
            return StudioBridgeProtocol.Refuse(request.Id, "bad_request", "format=full|split path=<.pdf> заана уу.");
        SheetSplitResult? result = SaveAlbumPdf(format == "split", Path.GetFullPath(path));
        return result is null
            ? StudioBridgeProtocol.Refuse(request.Id, "save_failed", statusText.Text)
            : StudioBridgeProtocol.Accept(request.Id, new { path = Path.GetFullPath(path), format, result.SourcePages, result.OutputPages, result.SplitPages }, statusText.Text);
    }

    /// <summary>Bridge «album-split-view»: on=true|false sets «Хуваалт харах»; without it, says what it is.</summary>
    private StudioBridgeResponse BridgeAlbumSplitView(StudioBridgeRequest request)
    {
        string? on = request.Arg("on");
        if (on is not null)
        {
            if (!bool.TryParse(on, out bool value))
                return StudioBridgeProtocol.Refuse(request.Id, "bad_request", "on=true|false заана уу.");
            SetAlbumSplitView(value);
        }
        return StudioBridgeProtocol.Accept(
            request.Id,
            new { on = AlbumSplitViewOn },
            AlbumSplitViewOn ? "Хуваалт харагдаж байна." : "Хуваалт харагдахгүй.");
    }
}
