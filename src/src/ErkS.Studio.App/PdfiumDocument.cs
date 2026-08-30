using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ErkS.Studio;

/// <summary>
/// One PDF held open by Studio's own engine.
///
/// The document is read into memory once and kept: an album is tens of
/// megabytes and every page of it is looked at, so re-reading the file per page
/// costs more than holding it. Pages are rendered on demand and the images are
/// frozen, which is what lets a viewer draw forty sheets without forty
/// rasterisations in flight.
/// </summary>
internal sealed class PdfiumDocument : IDisposable
{
    private static readonly object LibraryGate = new();
    private static bool libraryReady;

    private readonly object gate = new();
    private readonly Dictionary<PageKey, BitmapSource> rendered = [];

    /// <summary>Least recently rendered first; what Retain evicts from.</summary>
    private readonly List<PageKey> order = [];
    private GCHandle pinnedBytes;
    private IntPtr document;
    private bool disposed;

    private PdfiumDocument(byte[] bytes, IntPtr document)
    {
        pinnedBytes = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        this.document = document;
        PageCount = PdfiumInterop.GetPageCount(document);
    }

    public int PageCount { get; }

    /// <summary>
    /// Opens a PDF. Returns null rather than throwing when the file cannot be
    /// read or is not a PDF the engine accepts - a viewer says so on screen,
    /// it does not fall over.
    /// </summary>
    public static PdfiumDocument? Open(string path)
    {
        EnsureLibrary();
        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            GCHandle handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            IntPtr loaded;
            try
            {
                loaded = PdfiumInterop.LoadMemDocument(
                    handle.AddrOfPinnedObject(),
                    (nuint)bytes.LongLength,
                    null);
            }
            finally
            {
                handle.Free();
            }

            if (loaded == IntPtr.Zero)
                return null;

            return new PdfiumDocument(bytes, loaded);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or OutOfMemoryException)
        {
            return null;
        }
    }

    /// <summary>The page's height divided by its width.</summary>
    /// <summary>
    /// The page's own width in PDF points, or zero when it cannot be read.
    /// </summary>
    /// <remarks>
    /// Callers need this to decide how many pixels 300 DPI is for this
    /// <summary>
    /// The text drawn on a page, or null when it carries none.
    /// </summary>
    /// <remarks>
    /// A content probe that counts font references cannot tell a sheet whose
    /// drawing is missing from one whose frame simply has lettering on it. The
    /// words themselves can: "Erk-S Standard" is the frame, a building's label
    /// is the drawing.
    /// </remarks>
    public string? ReadPageText(int pageNumber)
    {
        lock (gate)
        {
            if (disposed || pageNumber < 1 || pageNumber > PageCount)
                return null;

            IntPtr page = PdfiumInterop.LoadPage(document, pageNumber - 1);
            if (page == IntPtr.Zero)
                return null;

            IntPtr textPage = IntPtr.Zero;
            try
            {
                textPage = PdfiumInterop.TextLoadPage(page);
                if (textPage == IntPtr.Zero)
                    return null;

                int count = PdfiumInterop.TextCountChars(textPage);
                if (count <= 0)
                    return null;

                // One extra for the terminator Pdfium writes.
                var buffer = new char[count + 1];
                int copied = PdfiumInterop.TextGetText(textPage, 0, count, buffer);
                return copied <= 0 ? null : new string(buffer, 0, Math.Min(copied, count));
            }
            finally
            {
                if (textPage != IntPtr.Zero)
                    PdfiumInterop.TextClosePage(textPage);
                PdfiumInterop.ClosePage(page);
            }
        }
    }

    /// particular sheet. Zero rather than a guessed A1: a page whose size is
    /// unknown is a reason to rasterise conservatively.
    /// </remarks>
    public double GetPageWidthPoints(int pageNumber)
    {
        lock (gate)
        {
            if (disposed || pageNumber < 1 || pageNumber > PageCount)
                return 0;

            IntPtr page = PdfiumInterop.LoadPage(document, pageNumber - 1);
            if (page == IntPtr.Zero)
                return 0;

            try
            {
                float width = PdfiumInterop.GetPageWidth(page);
                return width > 0 ? width : 0;
            }
            finally
            {
                PdfiumInterop.ClosePage(page);
            }
        }
    }

    public double GetPageAspect(int pageNumber)
    {
        lock (gate)
        {
            if (disposed || pageNumber < 1 || pageNumber > PageCount)
                return 297d / 420d;

            IntPtr page = PdfiumInterop.LoadPage(document, pageNumber - 1);
            if (page == IntPtr.Zero)
                return 297d / 420d;

            try
            {
                float width = PdfiumInterop.GetPageWidth(page);
                float height = PdfiumInterop.GetPageHeight(page);
                return width <= 0 ? 297d / 420d : height / width;
            }
            finally
            {
                PdfiumInterop.ClosePage(page);
            }
        }
    }

    /// <summary>
    /// One page as a frozen image of the given pixel width, rendered once and
    /// kept. The page is drawn onto white first: a PDF page has no background
    /// of its own, and an unfilled bitmap would come out black.
    /// </summary>
    public BitmapSource? RenderPage(int pageNumber, int pixelWidth)
    {
        if (pixelWidth <= 0)
            return null;

        var key = new PageKey(pageNumber, pixelWidth);
        lock (gate)
        {
            if (disposed || pageNumber < 1 || pageNumber > PageCount)
                return null;
            if (rendered.TryGetValue(key, out BitmapSource? cached))
                return cached;

            IntPtr page = PdfiumInterop.LoadPage(document, pageNumber - 1);
            if (page == IntPtr.Zero)
                return null;

            try
            {
                float width = PdfiumInterop.GetPageWidth(page);
                float height = PdfiumInterop.GetPageHeight(page);
                double aspect = width <= 0 ? 297d / 420d : height / width;
                int pixelHeight = Math.Max(1, (int)Math.Round(pixelWidth * aspect));

                int stride = pixelWidth * 4;
                var buffer = new byte[(long)stride * pixelHeight];
                GCHandle pixels = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                try
                {
                    IntPtr bitmap = PdfiumInterop.BitmapCreate(
                        pixelWidth,
                        pixelHeight,
                        PdfiumInterop.FPDFBitmapBgra,
                        pixels.AddrOfPinnedObject(),
                        stride);
                    if (bitmap == IntPtr.Zero)
                        return null;

                    try
                    {
                        PdfiumInterop.BitmapFillRect(
                            bitmap,
                            0,
                            0,
                            pixelWidth,
                            pixelHeight,
                            0xFFFFFFFF);
                        PdfiumInterop.RenderPageBitmap(
                            bitmap,
                            page,
                            0,
                            0,
                            pixelWidth,
                            pixelHeight,
                            0,
                            PdfiumInterop.FpdfAnnot | PdfiumInterop.FpdfLcdText);
                    }
                    finally
                    {
                        PdfiumInterop.BitmapDestroy(bitmap);
                    }

                    BitmapSource image = BitmapSource.Create(
                        pixelWidth,
                        pixelHeight,
                        96,
                        96,
                        PixelFormats.Bgra32,
                        palette: null,
                        buffer,
                        stride);
                    image.Freeze();
                    Retain(key, image);
                    return image;
                }
                finally
                {
                    pixels.Free();
                }
            }
            finally
            {
                PdfiumInterop.ClosePage(page);
            }
        }
    }

    /// <summary>
    /// PDFium is initialised once for the process. It is never destroyed:
    /// tearing it down while another document is open would take that document
    /// with it, and the process ending releases it anyway.
    /// </summary>
    private static void EnsureLibrary()
    {
        lock (LibraryGate)
        {
            if (libraryReady)
                return;

            // The app module is loaded shadow-copied by the development host,
            // where the runtime's own probing of runtimes/<rid>/native does not
            // apply. The library is therefore found the same way in every
            // build: beside this assembly, then under its runtimes folder.
            NativeLibrary.SetDllImportResolver(
                typeof(PdfiumDocument).Assembly,
                ResolvePdfium);
            PdfiumInterop.InitLibrary();
            libraryReady = true;
        }
    }

    private static IntPtr ResolvePdfium(
        string libraryName,
        System.Reflection.Assembly assembly,
        DllImportSearchPath? searchPath)
    {
        if (!libraryName.Equals("pdfium", StringComparison.OrdinalIgnoreCase))
            return IntPtr.Zero;

        string root = Path.GetDirectoryName(assembly.Location) ?? "";
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X86 => "win-x86",
            Architecture.Arm64 => "win-arm64",
            _ => "win-x64",
        };
        string[] candidates =
        [
            Path.Combine(root, "pdfium.dll"),
            Path.Combine(root, "runtimes", architecture, "native", "pdfium.dll"),
            Path.Combine(AppContext.BaseDirectory, "pdfium.dll"),
            Path.Combine(AppContext.BaseDirectory, "runtimes", architecture, "native", "pdfium.dll"),
        ];
        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out IntPtr handle))
                return handle;
        }

        return IntPtr.Zero;
    }

    /// <summary>
    /// Keeps the newest images and lets older ones go.
    /// </summary>
    /// <remarks>
    /// Every rendered page used to be kept for the life of the document. That
    /// was affordable while every render was 2400 pixels wide - about 20 MB a
    /// page. At 300 DPI an A1 sheet is roughly 9900 x 7000, which is 278 MB,
    /// and the preview now renders a page at several widths as someone zooms.
    /// Keeping them all would run a machine out of memory reading one album.
    ///
    /// A small budget rather than a count: the images differ in size by two
    /// orders of magnitude, so "keep four" means something very different for
    /// four thumbnails than for four full-resolution sheets.
    ///
    /// Called with the lock held.
    /// </remarks>
    private void Retain(PageKey key, BitmapSource image)
    {
        const long budgetBytes = 700L * 1024 * 1024;

        rendered[key] = image;
        order.Remove(key);
        order.Add(key);

        long held = order.Sum(entry => Bytes(rendered[entry]));
        while (order.Count > 1 && held > budgetBytes)
        {
            PageKey oldest = order[0];
            order.RemoveAt(0);
            if (rendered.Remove(oldest, out BitmapSource? dropped))
                held -= Bytes(dropped);
        }
    }

    private static long Bytes(BitmapSource image) =>
        (long)image.PixelWidth * image.PixelHeight * 4;

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;
            rendered.Clear();
            order.Clear();
            if (document != IntPtr.Zero)
            {
                PdfiumInterop.CloseDocument(document);
                document = IntPtr.Zero;
            }
            if (pinnedBytes.IsAllocated)
                pinnedBytes.Free();
        }
    }

    private readonly record struct PageKey(int PageNumber, int PixelWidth);
}
