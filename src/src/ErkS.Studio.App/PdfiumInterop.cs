using System.Runtime.InteropServices;

namespace ErkS.Studio;

/// <summary>
/// Studio's own binding to PDFium.
///
/// PDFium is the engine the browser was already rendering these albums with.
/// Binding to it directly rather than through somebody else's wrapper is what
/// keeps the surface ours: the calls Studio needs - render a page, render part
/// of a page, read the text and find a phrase in it - are added here when they
/// are needed instead of waited for.
///
/// Only the entry points Studio actually calls are declared. PDFium's own
/// header is the contract; every signature here matches it exactly, because a
/// mismatched one corrupts the stack rather than failing cleanly.
/// </summary>
internal static class PdfiumInterop
{
    private const string Library = "pdfium";

    /// <summary>Bitmap format: 32 bits per pixel, blue/green/red/alpha.</summary>
    public const int FPDFBitmapBgra = 4;

    /// <summary>Render flag: draw annotations that belong to the document.</summary>
    public const int FpdfAnnot = 0x01;

    /// <summary>Render flag: render for a screen rather than for print.</summary>
    public const int FpdfLcdText = 0x02;

    [DllImport(Library, EntryPoint = "FPDF_InitLibrary", CharSet = CharSet.Ansi)]
    public static extern void InitLibrary();

    [DllImport(Library, EntryPoint = "FPDF_DestroyLibrary", CharSet = CharSet.Ansi)]
    public static extern void DestroyLibrary();

    [DllImport(Library, EntryPoint = "FPDF_LoadMemDocument64", CharSet = CharSet.Ansi)]
    public static extern IntPtr LoadMemDocument(
        IntPtr dataBuffer,
        nuint size,
        [MarshalAs(UnmanagedType.LPStr)] string? password);

    [DllImport(Library, EntryPoint = "FPDF_CloseDocument", CharSet = CharSet.Ansi)]
    public static extern void CloseDocument(IntPtr document);

    [DllImport(Library, EntryPoint = "FPDF_GetPageCount", CharSet = CharSet.Ansi)]
    public static extern int GetPageCount(IntPtr document);

    [DllImport(Library, EntryPoint = "FPDF_LoadPage", CharSet = CharSet.Ansi)]
    public static extern IntPtr LoadPage(IntPtr document, int pageIndex);

    [DllImport(Library, EntryPoint = "FPDF_ClosePage", CharSet = CharSet.Ansi)]
    public static extern void ClosePage(IntPtr page);

    [DllImport(Library, EntryPoint = "FPDF_GetPageWidthF", CharSet = CharSet.Ansi)]
    public static extern float GetPageWidth(IntPtr page);

    [DllImport(Library, EntryPoint = "FPDF_GetPageHeightF", CharSet = CharSet.Ansi)]
    public static extern float GetPageHeight(IntPtr page);

    [DllImport(Library, EntryPoint = "FPDFBitmap_CreateEx", CharSet = CharSet.Ansi)]
    public static extern IntPtr BitmapCreate(
        int width,
        int height,
        int format,
        IntPtr firstScan,
        int stride);

    [DllImport(Library, EntryPoint = "FPDFBitmap_FillRect", CharSet = CharSet.Ansi)]
    public static extern void BitmapFillRect(
        IntPtr bitmap,
        int left,
        int top,
        int width,
        int height,
        uint color);

    [DllImport(Library, EntryPoint = "FPDFBitmap_Destroy", CharSet = CharSet.Ansi)]
    public static extern void BitmapDestroy(IntPtr bitmap);

    [DllImport(Library, EntryPoint = "FPDF_RenderPageBitmap", CharSet = CharSet.Ansi)]
    public static extern void RenderPageBitmap(
        IntPtr bitmap,
        IntPtr page,
        int startX,
        int startY,
        int sizeX,
        int sizeY,
        int rotate,
        int flags);

    [DllImport(Library, EntryPoint = "FPDF_GetLastError", CharSet = CharSet.Ansi)]
    public static extern uint GetLastError();

    // Text. Not called yet by the viewer; declared because searching a drawing
    // is the reason this engine was taken in the first place.

    [DllImport(Library, EntryPoint = "FPDFText_LoadPage", CharSet = CharSet.Ansi)]
    public static extern IntPtr TextLoadPage(IntPtr page);

    [DllImport(Library, EntryPoint = "FPDFText_ClosePage", CharSet = CharSet.Ansi)]
    public static extern void TextClosePage(IntPtr textPage);

    [DllImport(Library, EntryPoint = "FPDFText_CountChars", CharSet = CharSet.Ansi)]
    public static extern int TextCountChars(IntPtr textPage);

    /// <summary>
    /// Copies UTF-16 characters out of a text page. Added to answer a question
    /// no count could: whether the text on a sheet is the frame's own lettering
    /// or the drawing's labels. A page can carry a font reference and still be
    /// empty of drawing, which is what a content probe counting markers cannot
    /// tell apart.
    ///
    /// The buffer is ushort, not char. This import declares CharSet.Ansi, under
    /// which char[] marshals one byte per element - and Pdfium writes two. The
    /// first version of this overran the buffer and crashed the test host on
    /// every run, while the run still printed "Passed!" for the tests it had
    /// finished. UTF-16 units, converted here, leave nothing for the character
    /// set to decide.
    /// </summary>
    [DllImport(Library, EntryPoint = "FPDFText_GetText", CharSet = CharSet.Ansi)]
    public static extern int TextGetText(
        IntPtr textPage,
        int startIndex,
        int count,
        [Out] ushort[] result);
}
