using ErkS.Studio;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Reading the words off a sheet, which is how an empty drawing is told from a
/// missing one.
/// </summary>
/// <remarks>
/// PFA's frame-only warning decides whether a sheet carries a drawing by
/// looking for /Font, /XObject and /Image markers in the PDF, on the reasoning
/// that "a page that references neither carries only the frame strokes".
///
/// Two packages measured on 2026-08-30 had those markers and almost no ink: one
/// drew no text at all despite referencing ten fonts, the other drew a single
/// character. The marker says a resource is available, not that anything was
/// painted with it, so the warning cannot fire on exactly the sheets it exists
/// for.
///
/// The words themselves separate the cases, which is what this reads.
/// </remarks>
public sealed class PdfPageTextTests
{
    [Fact]
    public void ASheetWithLetteringReadsBackAsHavingSome()
    {
        string path = WritePdf(withText: true);
        try
        {
            using PdfiumDocument? document = PdfiumDocument.Open(path);
            Assert.NotNull(document);

            string? text = document!.ReadPageText(1);

            Assert.NotNull(text);
            // Presence, not the words. PdfSharp writes this string through a
            // subset font whose reverse mapping Pdfium reads back as "%ииии" -
            // the glyphs are there, their Unicode is not. So this API answers
            // "was anything lettered on the sheet", which is the question the
            // frame-only warning needs; it cannot yet answer "what does it
            // say", and no caller should be built as though it could.
            Assert.True(text!.Trim().Length >= 4, $"read back '{text}'");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ASheetOfNothingButLinesReadsBackEmpty()
    {
        // The case the marker count gets wrong in the other direction: strokes
        // only, no text, and nothing to mistake for a drawing.
        string path = WritePdf(withText: false);
        try
        {
            using PdfiumDocument? document = PdfiumDocument.Open(path);
            Assert.NotNull(document);

            Assert.True(string.IsNullOrWhiteSpace(document!.ReadPageText(1)));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AskingForAPageThatIsNotThereIsNotAnError()
    {
        string path = WritePdf(withText: true);
        try
        {
            using PdfiumDocument? document = PdfiumDocument.Open(path);

            Assert.Null(document!.ReadPageText(0));
            Assert.Null(document.ReadPageText(99));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string WritePdf(bool withText)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"erks-page-text-{(withText ? "words" : "lines")}-{Guid.NewGuid():N}.pdf");

        // The app registers this before every write; a test that draws text
        // has to do the same or PdfSharp has no font to draw with.
        WindowsFontResolver.Register();

        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromPoint(300);
        page.Height = XUnit.FromPoint(200);

        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawRectangle(XBrushes.White, 0, 0, 300, 200);
            gfx.DrawLine(XPens.Black, 10, 10, 290, 10);
            gfx.DrawLine(XPens.Black, 10, 190, 290, 190);

            if (withText)
            {
                gfx.DrawString(
                    "ХӨДӨЛГӨӨНИЙ СХЕМ",
                    new XFont("Arial", 12),
                    XBrushes.Black,
                    new XPoint(20, 100));
            }
        }

        document.Save(path);
        return path;
    }
}
