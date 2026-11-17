using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Pdf;

/// <summary>
/// A person's prepared signature (a PNG kept with their account) and the names
/// it signs: their own name as a corner table prints it.
/// </summary>
/// <remarks>
/// Owner, 2026-11-18: «бэлдсэн гарын үсэг миний бүртгэлд хадгалагдана. хийсэн
/// альбум дээр миний нэртэй хэсэг байвал гарын үсэг байршдаг болгоорой».
/// A signature is placed only beside a name that IS its holder's - never beside
/// a role, never by guessing who might be meant.
/// </remarks>
public sealed record TitleBlockSignature(IReadOnlyList<string> Names, byte[] Png)
{
    /// <summary>Whether the name printed in a cell is this signature's holder.</summary>
    public bool Signs(string? printedName)
    {
        string printed = Normalize(printedName);
        return printed.Length > 0 && (Names ?? []).Any(name => Normalize(name) == printed);
    }

    /// <summary>
    /// What makes two stamped albums differ: the picture and the names it signs.
    /// Part of a cached file's name, so a new signature is never served from a
    /// file stamped with the old one.
    /// </summary>
    public string Key
    {
        get
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            hash.AppendData(Png ?? []);
            foreach (string name in (Names ?? []).Select(Normalize).Where(n => n.Length > 0).Order(StringComparer.Ordinal))
                hash.AppendData(Encoding.UTF8.GetBytes("|" + name));
            return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        }
    }

    /// <summary>
    /// A name reduced to its letters: «Э.Мөнххолбоо», «Э. Мөнххолбоо» and
    /// «э.мөнххолбоо» are one person; spacing and the initial's dot are
    /// typography, not identity.
    /// </summary>
    public static string Normalize(string? name)
    {
        var builder = new StringBuilder();
        foreach (char c in (name ?? "").Normalize(NormalizationForm.FormC))
        {
            if (char.IsLetterOrDigit(c))
                builder.Append(char.ToLower(c, CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }
}

/// <summary>
/// Where a corner table printed a name and left its signature cell: recorded in
/// the page itself while the table is drawn, so whichever device later shows
/// the album can sign its own holder's names without knowing how the page was
/// laid out. Names only - the same names the table prints for anybody to read.
/// </summary>
/// <remarks>
/// Each cell rides on an EMPTY CONTENT STREAM of its own page. Kept on the page
/// dictionary it was dropped by an import (the server's merge, Studio's
/// composer copy only the page's standard keys); kept in the resources it was
/// shared - a source PDF commonly gives all its pages one resource dictionary,
/// so one sheet's names would have signed every other sheet. A page's content
/// streams are its own and an import copies them whole.
/// </remarks>
internal static class TitleBlockSignatureSlots
{
    public const string PageKey = "/ErkSSignatureSlots";

    /// <summary>Records a signature cell (in the drawing's coordinates) beside a printed name.</summary>
    public static void Record(XGraphics gfx, string? printedName, XRect cell)
    {
        string name = (printedName ?? "").Trim();
        PdfPage? page = gfx.PdfPage;
        if (page is null || name.Length == 0 || name == "-" || cell.Width <= 0 || cell.Height <= 0)
            return;
        // The page's coordinates, not the current drawing transform's.
        XMatrix transform = gfx.Transform;
        XPoint a = transform.Transform(new XPoint(cell.Left, cell.Top));
        XPoint b = transform.Transform(new XPoint(cell.Right, cell.Bottom));
        double left = Math.Min(a.X, b.X);
        double top = Math.Min(a.Y, b.Y);
        double width = Math.Abs(b.X - a.X);
        double height = Math.Abs(b.Y - a.Y);

        var slot = new PdfArray(page.Owner);
        slot.Elements.Add(new PdfString(name, PdfStringEncoding.Unicode));
        slot.Elements.Add(new PdfReal(Math.Round(left, 3)));
        slot.Elements.Add(new PdfReal(Math.Round(top, 3)));
        slot.Elements.Add(new PdfReal(Math.Round(width, 3)));
        slot.Elements.Add(new PdfReal(Math.Round(height, 3)));
        PdfSharp.Pdf.Advanced.PdfContent carrier = page.Contents.AppendContent();
        // A real (empty) stream: readers that join a page's streams expect one.
        carrier.CreateStream([]);
        carrier.Elements[PageKey] = slot;
    }

    /// <summary>Forgets a page's cells, before a restamp draws its table again.</summary>
    public static void Clear(PdfPage page)
    {
        PdfArray contents = page.Contents;
        for (int index = contents.Elements.Count - 1; index >= 0; index--)
        {
            if (Carrier(contents.Elements[index]) is { } carrier && carrier.Elements.ContainsKey(PageKey))
                contents.Elements.RemoveAt(index);
        }
    }

    public static IReadOnlyList<(string Name, XRect Cell)> Read(PdfPage page)
    {
        var result = new List<(string, XRect)>();
        if (!page.Elements.ContainsKey("/Contents"))
            return result;
        foreach (PdfItem item in page.Contents.Elements)
        {
            if (Carrier(item)?.Elements.GetArray(PageKey) is not { } slot || slot.Elements.Count < 5)
                continue;
            string name = slot.Elements.GetString(0);
            double[] numbers = new double[4];
            for (int n = 0; n < 4; n++)
                numbers[n] = slot.Elements.GetReal(n + 1);
            if (name.Length > 0 && numbers[2] > 0 && numbers[3] > 0)
                result.Add((name, new XRect(numbers[0], numbers[1], numbers[2], numbers[3])));
        }
        return result;
    }

    private static PdfDictionary? Carrier(PdfItem? item) =>
        (item is PdfSharp.Pdf.Advanced.PdfReference reference ? reference.Value : item) as PdfDictionary;
}

public sealed record TitleBlockSignatureStampResult(int PageCount, int Stamped);

/// <summary>
/// Places prepared signatures into an album's corner tables: every recorded
/// cell whose printed name is a signature's holder gets that signature.
/// </summary>
/// <remarks>
/// Applied to the album a person is SHOWN on their own device, never to the
/// pages Studio uploads: a signature leaves the device only inside a PDF its
/// holder made, and a shared album never depends on whose device rebuilt it.
/// </remarks>
public static class TitleBlockSignatureStamper
{
    private const double PointsPerMillimetre = 72d / 25.4;

    public static TitleBlockSignatureStampResult Stamp(
        string inputPdfPath,
        string outputPdfPath,
        IReadOnlyList<TitleBlockSignature> signatures)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPdfPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPdfPath);
        if (Path.GetFullPath(inputPdfPath).Equals(Path.GetFullPath(outputPdfPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Signature stamping requires a distinct output path.", nameof(outputPdfPath));

        List<TitleBlockSignature> usable = (signatures ?? [])
            .Where(signature => signature?.Png is { Length: > 0 } && (signature.Names ?? []).Count > 0)
            .ToList();
        int stamped = 0;
        using PdfDocument document = PdfReader.Open(inputPdfPath, PdfDocumentOpenMode.Modify);
        var images = new Dictionary<TitleBlockSignature, XImage>();
        var drawnImages = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        try
        {
            foreach (PdfPage page in document.Pages)
            {
                IReadOnlyList<(string Name, XRect Cell)> slots = TitleBlockSignatureSlots.Read(page);
                var matches = slots
                    .Select(slot => (slot.Cell, Signature: usable.FirstOrDefault(s => s.Signs(slot.Name))))
                    .Where(match => match.Signature is not null)
                    .ToList();
                if (matches.Count == 0)
                    continue;
                HashSet<PdfDictionary> before = PageImages(page);
                using (XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
                {
                    foreach ((XRect cell, TitleBlockSignature? signature) in matches)
                    {
                        if (!images.TryGetValue(signature!, out XImage? image))
                        {
                            image = XImage.FromStream(new MemoryStream(signature!.Png, writable: false));
                            images[signature] = image;
                        }
                        gfx.DrawImage(image, Fit(image, cell));
                        stamped++;
                    }
                }
                foreach (PdfDictionary drawn in PageImages(page).Where(image => !before.Contains(image)))
                    drawnImages.Add(drawn);
            }
            // PDFsharp's core build keeps a PNG's colours and drops its
            // transparency, which printed every signature on a black box.
            // The alpha goes back in as the image's soft mask.
            foreach (PdfDictionary drawn in drawnImages)
            {
                if (drawn.Elements.ContainsKey("/SMask"))
                    continue;
                TitleBlockSignature? owner = usable.FirstOrDefault(signature =>
                    PngAlpha.Size(signature.Png) is { } size &&
                    size.Width == drawn.Elements.GetInteger("/Width") &&
                    size.Height == drawn.Elements.GetInteger("/Height"));
                if (owner is null || PngAlpha.Read(owner.Png) is not { } alpha)
                    continue;
                var mask = new PdfDictionary(document);
                mask.Elements["/Type"] = new PdfName("/XObject");
                mask.Elements["/Subtype"] = new PdfName("/Image");
                mask.Elements["/Width"] = new PdfInteger(alpha.Width);
                mask.Elements["/Height"] = new PdfInteger(alpha.Height);
                mask.Elements["/ColorSpace"] = new PdfName("/DeviceGray");
                mask.Elements["/BitsPerComponent"] = new PdfInteger(8);
                mask.Elements["/Filter"] = new PdfName("/FlateDecode");
                mask.CreateStream(PngAlpha.Deflate(alpha.Values));
                document.Internals.AddObject(mask);
                drawn.Elements["/SMask"] = mask.Reference;
            }
            int pageCount = document.PageCount;
            document.Save(outputPdfPath);
            return new TitleBlockSignatureStampResult(pageCount, stamped);
        }
        finally
        {
            foreach (XImage image in images.Values)
                image.Dispose();
        }
    }

    private static HashSet<PdfDictionary> PageImages(PdfPage page)
    {
        var result = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (page.Resources.Elements.GetDictionary("/XObject") is not { } xobjects)
            return result;
        foreach (PdfItem item in xobjects.Elements.Values)
        {
            if ((item is PdfSharp.Pdf.Advanced.PdfReference reference ? reference.Value : item) is PdfDictionary image &&
                image.Elements.GetName("/Subtype") == "/Image")
            {
                result.Add(image);
            }
        }
        return result;
    }

    /// <summary>The picture fitted inside its cell, proportions kept, centred.</summary>
    internal static XRect Fit(XImage image, XRect cell)
    {
        double pad = 0.3 * PointsPerMillimetre;
        double maxWidth = Math.Max(1, cell.Width - pad * 2);
        double maxHeight = Math.Max(1, cell.Height - pad * 2);
        double ratio = image.PixelHeight > 0 ? (double)image.PixelWidth / image.PixelHeight : 3;
        double width = maxWidth;
        double height = width / ratio;
        if (height > maxHeight)
        {
            height = maxHeight;
            width = height * ratio;
        }
        return new XRect(
            cell.Left + (cell.Width - width) / 2,
            cell.Top + (cell.Height - height) / 2,
            width,
            height);
    }
}
