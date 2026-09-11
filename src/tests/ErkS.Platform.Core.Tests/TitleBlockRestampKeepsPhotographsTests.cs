using System.Text;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Re-stamping the canonical title blocks must not re-encode the photographs.
///
/// 🔴 A 2.0 GB FILE TURNED UP IN THE OWNER'S PROJECT FOLDER. The same album is
/// 30.8 MB where the main writer produced it - 185 images, every one DCTDecode,
/// the largest 1.77 MB. The title-block copy beside it held 32 objects over a
/// megabyte, the biggest a 7680 x 4875 DeviceRGB image at 90 MB with FlateDecode:
/// the owner's renders, decoded out of JPEG and stored raw.
///
/// One product, two paths: the main writer IMPORTS pages, which copies their
/// streams; this one opens the document for modification and saves it again.
/// This test asks what that costs, on a file small enough to reason about.
/// </summary>
public sealed class TitleBlockRestampKeepsPhotographsTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(), "erks-restamp-tests", Guid.NewGuid().ToString("N"));

    public TitleBlockRestampKeepsPhotographsTests() => Directory.CreateDirectory(root);

    [Fact]
    public void APHOTOGRAPHSurvivesTheRestampWithoutBeingReEncoded()
    {
        // 🔴 THE MEASUREMENT, ON OUR OWN FILE. Nothing here is the owner's data:
        // a JPEG is written, drawn into a page, and the product's own re-stamp is
        // run over it with no page selected - so the only thing under test is
        // what opening and saving the document does to an image it did not touch.
        string album = Path.Combine(root, "album.pdf");
        WriteAlbumWithAPhotograph(album);

        long before = new FileInfo(album).Length;
        int jpegBefore = Occurrences(album, "/DCTDecode");

        string restamped = Path.Combine(root, "restamped.pdf");
        PdfSharpAlbumWriter.RestampCanonicalTitleBlocks(
            album,
            new AlbumProject { Name = "Restamp" },
            [],
            restamped);

        long after = new FileInfo(restamped).Length;
        int jpegAfter = Occurrences(restamped, "/DCTDecode");

        // The photograph must still be a photograph. Losing the JPEG filter is
        // what turns 30 MB into 2 GB on a real album.
        Assert.True(
            jpegBefore > 0,
            "the fixture did not produce a JPEG image, so this measures nothing");
        Assert.True(
            jpegAfter >= jpegBefore,
            $"the re-stamp decoded {jpegBefore - jpegAfter} JPEG image(s): " +
            $"{before} bytes became {after}");

        // And the file must not balloon. Some growth is normal - the document is
        // rewritten - but a multiple is the defect this test exists for.
        Assert.True(
            after < before * 2,
            $"the re-stamp grew the file from {before} to {after} bytes");
    }

    [Fact]
    public void APNGBecomesRAWRGBWhileAJpegStaysAJpeg()
    {
        // 🔴 THE STRUCTURAL HALF OF THE 2 GB QUESTION. What PDFsharp stores depends
        // on the FILE FORMAT, not on how large the frame is:
        //
        //   .jpg  →  the JPEG stream is embedded as it is, DCTDecode
        //   .png  →  decoded to raw RGB and stored FlateDecode
        //
        // On a 7680 x 4875 render the second path is 112 MB raw, ~90 MB flated -
        // which is exactly the object found in the owner's album.
        //
        // 🔴 TWO OF MY OWN EXPLANATIONS DIED GETTING HERE. First «the re-stamp
        // re-encodes» - measured, false. Then «PDFsharp cannot open PNG at all»,
        // from a 16x16 fixture it happened to refuse - false too: a full-size PNG
        // opens perfectly. A fixture small enough to be a special case will make
        // one of you, and it nearly made two.
        string jpegAlbum = Path.Combine(root, "as-jpeg.pdf");
        string pngAlbum = Path.Combine(root, "as-png.pdf");
        WriteAlbumWithAPhotograph(jpegAlbum);
        WriteAlbumWithAPng(pngAlbum);

        Assert.True(Occurrences(jpegAlbum, "/DCTDecode") > 0, "the JPEG was not passed through");
        Assert.Equal(0, Occurrences(pngAlbum, "/DCTDecode"));
        Assert.True(
            Occurrences(pngAlbum, "/FlateDecode") > 0,
            "the PNG did not take the raw-RGB path this test exists to name");
    }

    [Fact]
    public void ABASELINEJpegIsPassedThroughUntouched()
    {
        // The other half, and the one that holds: a JPEG reaches the page as a
        // JPEG. Whatever produced the owner's raw images, it was not this path
        // with an ordinary photograph.
        string album = Path.Combine(root, "as-jpeg.pdf");
        WriteAlbumWithAPhotograph(album);

        Assert.True(Occurrences(album, "/DCTDecode") > 0, "the JPEG was not passed through");
    }

    private void WriteAlbumWithAPng(string path)
    {
        string pngPath = Path.Combine(root, "render.png");
        File.WriteAllBytes(pngPath, BuildTruecolourPng(64, 64));

        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(420);
        page.Height = XUnit.FromMillimeter(297);
        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        using (XImage image = XImage.FromFile(pngPath))
        {
            gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
        }

        document.Save(path);
    }

    /// <summary>A valid truecolour PNG of any size, built here so the fixture is
    /// not a curiosity: proper IHDR, a zlib-compressed IDAT and IEND.</summary>
    private static byte[] BuildTruecolourPng(int width, int height)
    {
        var raw = new List<byte>();
        for (int y = 0; y < height; y++)
        {
            raw.Add(0);
            for (int x = 0; x < width; x++)
            {
                raw.Add((byte)((x * 4) % 256));
                raw.Add((byte)((y * 4) % 256));
                raw.Add(128);
            }
        }

        using var idat = new MemoryStream();
        using (var deflate = new System.IO.Compression.ZLibStream(
            idat, System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(raw.ToArray());
        }

        var png = new List<byte> { 137, 80, 78, 71, 13, 10, 26, 10 };
        var header = new List<byte>();
        header.AddRange(BigEndian(width));
        header.AddRange(BigEndian(height));
        header.AddRange(new byte[] { 8, 2, 0, 0, 0 });
        png.AddRange(PngChunk("IHDR"u8.ToArray(), header.ToArray()));
        png.AddRange(PngChunk("IDAT"u8.ToArray(), idat.ToArray()));
        png.AddRange(PngChunk("IEND"u8.ToArray(), []));
        return png.ToArray();
    }

    private static byte[] PngChunk(byte[] tag, byte[] data)
    {
        var body = tag.Concat(data).ToArray();
        return BigEndian(data.Length)
            .Concat(body)
            .Concat(BigEndian(unchecked((int)Crc32(body))))
            .ToArray();
    }

    /// <summary>PNG's CRC-32, written out rather than pulled in: one package
    /// reference for four lines is a dependency somebody has to justify later.</summary>
    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static byte[] BigEndian(int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return bytes;
    }

    private static int Occurrences(string path, string needle)
    {
        string raw = Encoding.Latin1.GetString(File.ReadAllBytes(path));
        return raw.Split(needle).Length - 1;
    }

    private void WriteAlbumWithAPhotograph(string path)
    {
        // A real JPEG, small but genuinely DCT-encoded.
        string jpegPath = Path.Combine(root, "render.jpg");
        File.WriteAllBytes(jpegPath, Convert.FromBase64String(TinyJpegBase64));

        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(420);
        page.Height = XUnit.FromMillimeter(297);
        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        using (XImage image = XImage.FromFile(jpegPath))
        {
            // Drawn large, the way a visualisation page uses a render.
            gfx.DrawImage(image, 0, 0, page.Width.Point, page.Height.Point);
        }

        document.Save(path);
    }

    /// <summary>A 16x16 JPEG. Base64 so the test carries its own fixture.</summary>
    private const string TinyJpegBase64 =
        "/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0a" +
        "HBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIy" +
        "MjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAAQABADASIA" +
        "AhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQA" +
        "AAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3" +
        "ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWm" +
        "p6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEA" +
        "AwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSEx" +
        "BhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElK" +
        "U1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3" +
        "uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iii" +
        "gD//2Q==";

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
