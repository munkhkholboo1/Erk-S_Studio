using ErkS.Platform.Contracts;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A sun sheet arrived at 20 MB with about 19 000 image objects (9 500 of them soft masks) and
/// made every album build, preview and sync slow (Мастер, 2026-10-31). A received sheet of 5 MB
/// or more is named with its size and image count, so its source (CGA, PFA) can lighten it.
/// Reported, never refused.
/// </summary>
public sealed class AHEAVYSheetIsNamedSoItsSourceCanLightenItTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "erks-heavy-" + Guid.NewGuid().ToString("N"));

    public AHEAVYSheetIsNamedSoItsSourceCanLightenItTests() => Directory.CreateDirectory(folder);

    [Fact]
    public void ASheetOverTheLimitIsNamedWithItsSizeAndImages()
    {
        string heavy = Pdf("heavy.pdf", images: 40, padBytes: HeavySheetSurvey.LimitBytes + 1000);
        string light = Pdf("light.pdf", images: 2, padBytes: 0);

        IReadOnlyList<string> notices = HeavySheetSurvey.Review(Request(("03", "НАРНЫ ЭЭВЭРЛЭЛТ", heavy), ("04", "ЕТ", light)));

        string notice = Assert.Single(notices);
        Assert.StartsWith(HeavySheetSurvey.Prefix, notice, StringComparison.Ordinal);
        Assert.Contains("03", notice, StringComparison.Ordinal);
        Assert.Contains("МБ", notice, StringComparison.Ordinal);
        Assert.Contains("40 зураг", notice, StringComparison.Ordinal);
        Assert.Contains("эх үүсвэр", notice, StringComparison.Ordinal);
    }

    [Fact]
    public void OneFileOfSeveralSheetsIsNamedOnce()
    {
        string heavy = Pdf("shared.pdf", images: 1, padBytes: HeavySheetSurvey.LimitBytes + 1000);

        Assert.Single(HeavySheetSurvey.Review(Request(("01", "A", heavy), ("02", "B", heavy))));
    }

    /// <summary>Image XObjects written directly; the size is reached with a stream of padding.</summary>
    private string Pdf(string name, int images, long padBytes)
    {
        string path = Path.Combine(folder, name);
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        using (XGraphics gfx = XGraphics.FromPdfPage(page))
            gfx.DrawLine(XPens.Black, 0, 0, 10, 10);
        var holder = new PdfArray(document);
        for (int index = 0; index < images; index++)
        {
            var image = new PdfDictionary(document);
            image.Elements.SetName("/Type", "/XObject");
            image.Elements.SetName("/Subtype", "/Image");
            image.Elements.SetInteger("/Width", 1);
            image.Elements.SetInteger("/Height", 1);
            image.Elements.SetName("/ColorSpace", "/DeviceGray");
            image.Elements.SetInteger("/BitsPerComponent", 8);
            image.CreateStream([(byte)index]);
            document.Internals.AddObject(image);
            holder.Elements.Add(image.Reference!);
        }
        document.Internals.AddObject(holder);
        document.Info.Elements.SetReference("/ErkSImages", holder);
        if (padBytes > 0)
        {
            var pad = new PdfDictionary(document);
            var bytes = new byte[padBytes];
            new Random(7).NextBytes(bytes);
            pad.CreateStream(bytes);
            document.Internals.AddObject(pad);
            document.Info.Elements.SetReference("/ErkSPad", pad);
        }
        document.Save(path);
        return path;
    }

    private static AlbumBuildRequest Request(params (string Number, string Title, string Path)[] sheets) => new()
    {
        Project = new AlbumProject(),
        Sections =
        [
            new AlbumBuildSection
            {
                Title = "",
                Pages = sheets.Select(sheet => new AlbumBuildPage
                {
                    Sheet = new SheetRecord
                    {
                        Key = "src|" + sheet.Number,
                        SourceId = "src",
                        SourceIdentity = "src",
                        Entry = new SheetPackageEntry { SheetId = sheet.Number, Number = sheet.Number, Name = sheet.Title },
                        Source = new SheetPackageSource(),
                        PackageId = Guid.Empty,
                        ManifestPath = "",
                        PdfPath = sheet.Path,
                        SourceSheetIndex = 0,
                        ExportedAtUtc = DateTimeOffset.UtcNow,
                        IsVerified = true,
                    },
                    Definition = new AlbumPageDefinition(),
                    Format = PageFormatCatalog.DefaultWorkingDrawing,
                }).ToList(),
            },
        ],
    };

    public void Dispose()
    {
        try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
    }
}
