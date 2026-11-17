using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Owner, 2026-11-18: «студиод png бэлдсэн гарын үсэг булангийн хүснэгтэд
/// байршуулдаг логик нэмээрэй … хийсэн альбум дээр миний нэртэй хэсэг байвал
/// гарын үсэг байршдаг болгоорой». The corner table leaves in each page where a
/// printed name's signature belongs; the holder's device signs only that name.
/// </summary>
public sealed class APREPAREDSignatureSignsOnlyItsHoldersNameTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "erks-signature-" + Guid.NewGuid().ToString("N"));

    public APREPAREDSignatureSignsOnlyItsHoldersNameTests() => Directory.CreateDirectory(folder);

    public void Dispose()
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void ANameIsTheSamePersonWhateverItsSpacingOrCase()
    {
        var signature = new TitleBlockSignature(["Э.Мөнххолбоо"], Png(60, 20));

        Assert.True(signature.Signs("Э.Мөнххолбоо"));
        Assert.True(signature.Signs("Э. Мөнххолбоо"));
        Assert.True(signature.Signs("э.мөнххолбоо"));
        Assert.False(signature.Signs("М.Баасанжав"));
        Assert.False(signature.Signs("Мөнххолбоо"));
        Assert.False(signature.Signs(""));
    }

    [Fact]
    public void THEWORKINGCornerTableLeavesTheSignatureCellOfEachName()
    {
        string path = Path.Combine(folder, "table.pdf");
        using (var document = new PdfDocument())
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(420);
            page.Height = XUnit.FromMillimeter(297);
            using (XGraphics gfx = XGraphics.FromPdfPage(page))
            {
                DrawHorizontalTable(gfx, new XRect(Mm(245), Mm(258), Mm(170), Mm(34)));
            }
            document.Save(path);
        }

        using PdfDocument read = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        IReadOnlyList<(string Name, XRect Cell)> slots = TitleBlockSignatureSlots.Read(read.Pages[0]);
        Assert.Equal(["Э.Мөнххолбоо", "М.Будбаясгалан", "М.Баасанжав"], slots.Select(slot => slot.Name));
        // Architect's signature cell: columns 70 → 95, rows 15 → 21 of the table.
        Assert.Equal(Mm(245 + 70), slots[0].Cell.Left, 2);
        Assert.Equal(Mm(258 + 15), slots[0].Cell.Top, 2);
        Assert.Equal(Mm(25), slots[0].Cell.Width, 2);
        Assert.Equal(Mm(6), slots[0].Cell.Height, 2);
    }

    [Fact]
    public void ASIGNEDCopySignsTheHoldersNameOnlyEvenAfterThePageIsMergedElsewhere()
    {
        string built = Path.Combine(folder, "built.pdf");
        using (var document = new PdfDocument())
        {
            PdfPage page = document.AddPage();
            using (XGraphics gfx = XGraphics.FromPdfPage(page))
            {
                TitleBlockSignatureSlots.Record(gfx, "Э.Мөнххолбоо", new XRect(100, 100, 70, 17));
                TitleBlockSignatureSlots.Record(gfx, "М.Баасанжав", new XRect(100, 117, 70, 17));
            }
            document.Save(built);
        }

        // The server merges component pages into the shared album by import.
        string merged = Path.Combine(folder, "merged.pdf");
        using (PdfDocument source = PdfReader.Open(built, PdfDocumentOpenMode.Import))
        using (var output = new PdfDocument())
        {
            output.AddPage(source.Pages[0]);
            output.Save(merged);
        }

        string signed = Path.Combine(folder, "signed.pdf");
        TitleBlockSignatureStampResult result = TitleBlockSignatureStamper.Stamp(
            merged,
            signed,
            [new TitleBlockSignature(["Э.Мөнххолбоо"], Png(60, 20))]);

        Assert.Equal(1, result.Stamped);
        Assert.Equal(0, ImageCount(merged));
        Assert.Equal(1, ImageCount(signed));
        // Transparent around the stroke - not printed on a black box.
        Assert.True(SignatureHasSoftMask(signed));
    }

    private static bool SignatureHasSoftMask(string path)
    {
        using PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        PdfDictionary? xobjects = document.Pages[0].Resources.Elements.GetDictionary("/XObject");
        return xobjects!.Elements.Values
            .Select(value => value is PdfSharp.Pdf.Advanced.PdfReference reference ? reference.Value : value)
            .OfType<PdfDictionary>()
            .Any(item => item.Elements.ContainsKey("/SMask"));
    }

    [Fact]
    public void PAGESThatShareOneResourceDictionaryKeepTheirOwnNames()
    {
        string built = Path.Combine(folder, "shared.pdf");
        using (var document = new PdfDocument())
        {
            PdfPage first = document.AddPage();
            PdfPage second = document.AddPage();
            // A source PDF that gives every sheet one resource dictionary.
            var shared = new PdfDictionary(document);
            document.Internals.AddObject(shared);
            first.Elements["/Resources"] = shared.Reference;
            second.Elements["/Resources"] = shared.Reference;
            using (XGraphics gfx = XGraphics.FromPdfPage(first))
                TitleBlockSignatureSlots.Record(gfx, "Э.Мөнххолбоо", new XRect(100, 100, 70, 17));
            using (XGraphics gfx = XGraphics.FromPdfPage(second))
                TitleBlockSignatureSlots.Record(gfx, "М.Баасанжав", new XRect(100, 100, 70, 17));
            document.Save(built);
        }
        string merged = Path.Combine(folder, "shared-merged.pdf");
        using (PdfDocument source = PdfReader.Open(built, PdfDocumentOpenMode.Import))
        using (var output = new PdfDocument())
        {
            output.AddPage(source.Pages[1]);
            output.AddPage(source.Pages[0]);
            output.Save(merged);
        }

        using PdfDocument read = PdfReader.Open(merged, PdfDocumentOpenMode.Import);
        Assert.Equal(["М.Баасанжав"], TitleBlockSignatureSlots.Read(read.Pages[0]).Select(slot => slot.Name));
        Assert.Equal(["Э.Мөнххолбоо"], TitleBlockSignatureSlots.Read(read.Pages[1]).Select(slot => slot.Name));
    }

    [Fact]
    public void WITHOUTASignatureTheAlbumIsLeftAsItWas()
    {
        string built = Path.Combine(folder, "plain.pdf");
        using (var document = new PdfDocument())
        {
            PdfPage page = document.AddPage();
            using (XGraphics gfx = XGraphics.FromPdfPage(page))
                TitleBlockSignatureSlots.Record(gfx, "Э.Мөнххолбоо", new XRect(100, 100, 70, 17));
            document.Save(built);
        }

        TitleBlockSignatureStampResult result = TitleBlockSignatureStamper.Stamp(
            built, Path.Combine(folder, "out.pdf"), []);

        Assert.Equal(0, result.Stamped);
        Assert.Equal(0, ImageCount(Path.Combine(folder, "out.pdf")));
    }

    [Fact]
    public void ASignatureIsFittedInsideItsCellKeepingItsProportions()
    {
        using var stream = new MemoryStream(Png(300, 100));
        using XImage image = XImage.FromStream(stream);
        XRect cell = new(0, 0, 70, 17);

        XRect placed = TitleBlockSignatureStamper.Fit(image, cell);

        Assert.True(placed.Left >= cell.Left && placed.Right <= cell.Right);
        Assert.True(placed.Top >= cell.Top && placed.Bottom <= cell.Bottom);
        Assert.Equal(3d, placed.Width / placed.Height, 3);
    }

    private static void DrawHorizontalTable(XGraphics gfx, XRect rect)
    {
        WindowsFontResolver.Register();
        var project = new AlbumProject
        {
            Name = "ТӨСӨЛ",
            Company = new CompanyProfile { Name = "\"Арт констракшн\" ХХК" },
        };
        var buildPage = new AlbumBuildPage
        {
            Sheet = new SheetRecord
            {
                Key = "k",
                SourceId = "s",
                SourceIdentity = "s",
                Entry = new SheetPackageEntry
                {

                    Roles =
                    [
                        new SheetRoleSignature { RoleCode = SheetRoleCodes.Architect, FullName = "Э.Мөнххолбоо" },
                        new SheetRoleSignature { RoleCode = SheetRoleCodes.PreparedBy, FullName = "М.Будбаясгалан" },
                        new SheetRoleSignature { RoleCode = SheetRoleCodes.CheckedBy, FullName = "М.Баасанжав" },
                    ],
                },
                Source = new SheetPackageSource(),
                PackageId = Guid.NewGuid(),
                ManifestPath = "",
                PdfPath = "",
                SourceSheetIndex = 0,
                ExportedAtUtc = DateTimeOffset.UtcNow,
                IsVerified = true,
            },
            Definition = new AlbumPageDefinition(),
            Format = new PageFormatDefinition(),
        };
        MethodInfo draw = typeof(PdfSharpAlbumWriter).GetMethod(
            "DrawCanonicalHorizontalWorkingTitleBlockV2",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        draw.Invoke(null, [gfx, rect, project, buildPage, new XPen(XColors.Black, 1), new XPen(XColors.Black, 0.3)]);
    }

    private static int ImageCount(string path)
    {
        using PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        PdfDictionary? xobjects = document.Pages[0].Resources.Elements.GetDictionary("/XObject");
        return xobjects?.Elements.Values
            .Select(value => value is PdfSharp.Pdf.Advanced.PdfReference reference ? reference.Value : value)
            .OfType<PdfDictionary>()
            .Count(item => item.Elements.GetName("/Subtype") == "/Image") ?? 0;
    }

    private static double Mm(double mm) => mm * 72d / 25.4;

    /// <summary>A real, minimal RGBA PNG: transparent with a dark stroke.</summary>
    internal static byte[] Png(int width, int height)
    {
        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            for (int x = 0; x < width; x++)
            {
                bool ink = Math.Abs(y - height / 2) <= 1;
                raw.Write(ink ? [20, 20, 60, 255] : [0, 0, 0, 0]);
            }
        }
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            raw.WriteTo(zlib);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void Chunk(Stream png, string type, byte[] data)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        png.Write(length);
        byte[] typed = [.. System.Text.Encoding.ASCII.GetBytes(type), .. data];
        png.Write(typed);
        byte[] crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typed));
        png.Write(crc);
    }

    private static uint Crc32(byte[] bytes)
    {
        uint crc = 0xffffffff;
        foreach (byte b in bytes)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
                crc = (crc & 1) != 0 ? 0xedb88320 ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }
}
