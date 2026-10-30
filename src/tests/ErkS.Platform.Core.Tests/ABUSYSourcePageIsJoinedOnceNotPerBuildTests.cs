using System.Diagnostics;
using System.Text;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// An AutoCAD sheet writes a content stream per object (a CGA sun sheet: 9 352), and PDFsharp
/// joins them quadratically on every import - 306 of the 331 s of an Erin refresh that changed
/// nothing (profiled 2026-10-31). The streams are joined once, linearly, into a cached copy.
/// </summary>
public sealed class ABUSYSourcePageIsJoinedOnceNotPerBuildTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "erks-flat-" + Guid.NewGuid().ToString("N"));
    private readonly string previousCache = SourcePdfContentFlattener.CacheDirectory;

    public ABUSYSourcePageIsJoinedOnceNotPerBuildTests()
    {
        Directory.CreateDirectory(directory);
        SourcePdfContentFlattener.CacheDirectory = Path.Combine(directory, "cache");
    }

    [Fact]
    public void ABusyPageBecomesOneStreamWithTheSameOperatorsInOrder()
    {
        string source = Write(streams: 3000);

        string resolved = SourcePdfContentFlattener.Resolve(source);

        Assert.NotEqual(source, resolved);
        using PdfDocument flat = PdfReader.Open(resolved, PdfDocumentOpenMode.Import);
        PdfContents contents = flat.Pages[0].Contents;
        Assert.Single(contents.Elements);
        string text = Encoding.ASCII.GetString(
            ((PdfDictionary)((PdfReference)contents.Elements[0]).Value).Stream.UnfilteredValue);
        Assert.Equal(3000, text.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
        Assert.StartsWith("0 0 m 1 1 l S", text, StringComparison.Ordinal);
        Assert.Contains("2999 0 m 1 1 l S", text, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuietPageIsOpenedAsItIs()
    {
        string source = Write(streams: 3);

        Assert.Equal(source, SourcePdfContentFlattener.Resolve(source));
    }

    [Fact]
    public void TheJoinedCopyIsReusedAndTheSourceIsNeverTouched()
    {
        string source = Write(streams: 500);
        byte[] before = File.ReadAllBytes(source);

        string first = SourcePdfContentFlattener.Resolve(source);
        DateTime written = File.GetLastWriteTimeUtc(first);
        string second = SourcePdfContentFlattener.Resolve(source);

        Assert.Equal(first, second);
        Assert.Equal(written, File.GetLastWriteTimeUtc(second));
        Assert.Equal(before, File.ReadAllBytes(source));
    }

    [Fact]
    public void ImportingTheJoinedCopyIsFast()
    {
        string source = Write(streams: 6000);
        string resolved = SourcePdfContentFlattener.Resolve(source);

        var watch = Stopwatch.StartNew();
        using (var target = new PdfDocument())
        using (XPdfForm form = XPdfForm.FromFile(resolved))
        using (XGraphics gfx = XGraphics.FromPdfPage(target.AddPage()))
            gfx.DrawImage(form, 0, 0, 100, 100);
        watch.Stop();

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"import took {watch.Elapsed}");
    }

    private string Write(int streams)
    {
        string path = Path.Combine(directory, $"busy-{streams}-{Guid.NewGuid():N}.pdf");
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Contents.Elements.Clear();
        for (int index = 0; index < streams; index++)
        {
            PdfContent content = page.Contents.AppendContent();
            content.CreateStream(Encoding.ASCII.GetBytes($"{index} 0 m 1 1 l S"));
        }
        document.Save(path);
        return path;
    }

    public void Dispose()
    {
        SourcePdfContentFlattener.CacheDirectory = previousCache;
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
    }
}
