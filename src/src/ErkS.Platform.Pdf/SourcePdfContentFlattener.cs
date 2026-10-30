using System.Security.Cryptography;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Pdf;

/// <summary>
/// Opens a received sheet PDF as a form with each page's content in ONE stream.
/// </summary>
/// <remarks>
/// ⚠ PDFsharp's <c>PdfContents.CreateSingleContent</c>, which every <see cref="XPdfForm"/>
/// import runs, joins a page's content streams by copying the whole buffer once per stream -
/// quadratic. AutoCAD's PDF export writes a stream per object: a CGA sun sheet carries 9 352 of
/// them, and joining one took more than a minute; the Erin album spent 306 of its 331 s there on
/// a refresh that changed nothing (profiled 2026-10-31). The streams are joined here once, in
/// linear time, into a copy kept beside the other caches; the received package itself is never
/// touched (its hash is what intake verifies). A page with few streams is opened as it is.
/// </remarks>
public static class SourcePdfContentFlattener
{
    /// <summary>Pages with at most this many streams are left as they are - joining them is cheap.</summary>
    public const int StreamThreshold = 64;

    private static readonly object Gate = new();

    /// <summary>Where the joined copies are kept; set once by the host, defaults to %LOCALAPPDATA%.</summary>
    public static string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Erk-S Studio",
        "cache",
        "flat-pdf");

    public static XPdfForm OpenForm(string path) => XPdfForm.FromFile(Resolve(path));

    /// <summary>
    /// The path to import from: the source itself, or its joined copy when a page carries more
    /// than <see cref="StreamThreshold"/> content streams. Any failure falls back to the source.
    /// </summary>
    public static string Resolve(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return path;
            string cached = Path.Combine(CacheDirectory, Key(info) + ".pdf");
            if (File.Exists(cached))
                return cached;
            lock (Gate)
            {
                if (File.Exists(cached))
                    return cached;
                return TryWriteFlattened(path, cached) ? cached : path;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or PdfReaderException)
        {
            return path;
        }
    }

    /// <summary>Joins the content streams of every busy page; false when no page needed it.</summary>
    internal static bool TryWriteFlattened(string source, string destination)
    {
        using PdfDocument document = PdfReader.Open(source, PdfDocumentOpenMode.Modify);
        bool changed = false;
        foreach (PdfPage page in document.Pages)
            changed |= Flatten(page);
        if (!changed)
            return false;

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            document.Save(temporary);
            File.Move(temporary, destination, overwrite: true);
            Prune(Path.GetDirectoryName(destination)!, keep: destination);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
        return true;
    }

    /// <summary>One page's streams joined in order, separated by a newline (ISO 32000 7.8.2).</summary>
    internal static bool Flatten(PdfPage page)
    {
        PdfContents contents = page.Contents;
        if (contents.Elements.Count <= StreamThreshold)
            return false;

        using var joined = new MemoryStream();
        foreach (PdfItem item in contents.Elements)
        {
            PdfDictionary? stream = (item as PdfReference)?.Value as PdfDictionary ?? item as PdfDictionary;
            byte[]? bytes = stream?.Stream?.UnfilteredValue;
            if (bytes is null)
                continue;
            joined.Write(bytes);
            joined.WriteByte((byte)'\n');
        }

        contents.Elements.Clear();
        PdfContent single = contents.AppendContent();
        single.CreateStream(joined.ToArray());
        single.Compressed = true;
        return true;
    }

    /// <summary>Copies of superseded deliveries go after two weeks unread; a copy is only a cache.</summary>
    private static void Prune(string folder, string keep)
    {
        DateTime cutoff = DateTime.UtcNow.AddDays(-14);
        foreach (string file in Directory.EnumerateFiles(folder, "*.pdf"))
        {
            try
            {
                if (!string.Equals(file, keep, StringComparison.OrdinalIgnoreCase) &&
                    File.GetLastAccessTimeUtc(file) < cutoff &&
                    File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    /// <summary>The source's full path, size and write time - a changed file gets a new copy.</summary>
    private static string Key(FileInfo info)
    {
        string identity = string.Join(
            "|",
            info.FullName.ToUpperInvariant(),
            info.Length.ToString(System.Globalization.CultureInfo.InvariantCulture),
            info.LastWriteTimeUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))[..32];
    }
}
