using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Erk-S CAD draws its diagrams on the GPU, so it has no vector original to
/// export. Measured by the Erk-S CAD session on 2026-09-27: the views come out
/// of <c>Viewport3DX.RenderBitmap</c>, 4961 px wide - A3 landscape (420 mm) at
/// 300 dpi - and each image already carries its own title box, legend, north
/// arrow and scale bar.
///
/// 🔴 UNTIL THIS FILE EXISTED THERE WAS NO WAY IN. The package reader refused
/// any payload whose extension was not <c>.pdf</c>, and the one accessor callers
/// had was named for a PDF. A raster diagram could not reach the portfolio at
/// all - not because anybody decided it should not, but because the only door
/// was cut for a different file.
///
/// The rules this file holds, each by its own test, are the ones that make a
/// raster page as provable as a vector one: the bytes are hashed like any
/// payload, the pixel count it declares is the pixel count it has, and the
/// places that expect a PDF are never handed an image instead.
/// </summary>
public sealed class ADIAGRAMArrivesAsARASTERPageOrIsREFUSEDTests : IDisposable
{
    private const string SourceId = "erks-cad-diagrams";

    /// <summary>A3 landscape at the album's 300 dpi rule, as Erk-S CAD reports it.</summary>
    private const int A3LandscapeWidthPixels = 4961;

    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-platform-tests",
        Guid.NewGuid().ToString("N"));

    public ADIAGRAMArrivesAsARASTERPageOrIsREFUSEDTests() =>
        Directory.CreateDirectory(workDirectory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void AnErkSCadPngDiagramVerifiesLosslesslyAndBecomesAPortfolioPage()
    {
        string manifestPath = WriteRasterPackage("arrives");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        SheetPackageEntry entry = Assert.Single(result.Manifest!.Sheets);
        Assert.Equal(SheetSourceApplication.ErkSCad, result.Manifest.Source.Application);
        Assert.True(SheetPayloadMediaTypes.IsRaster(entry.PayloadMediaType));

        (ProjectWorkspace project, string projectPath) = CreateProject();
        PortfolioSheetImportResult imported = PortfolioSheetImportService.Import(
            project,
            projectPath,
            result);

        Assert.Equal(1, imported.CreatedItemCount);
        ProjectPortfolioItem item = Assert.Single(project.Portfolio.Items);
        Assert.Equal(ProjectPortfolioItemKinds.CadPage, item.Kind);
        Assert.Equal("Насжилтын диаграм", item.Title);
        // The question the diagram answers travels in SheetDescription and
        // becomes the caption the page starts life with.
        Assert.Equal("Аль хэсэг нь хамгийн хуучин вэ?", item.Caption);
        // The pixel count is recorded because the raster budget needs it: a page
        // cannot be brought down to the album's density by a reader that has to
        // guess how dense it already is.
        Assert.Equal(A3LandscapeWidthPixels, item.SourceWidthPixels);
        Assert.Equal(3508, item.SourceHeightPixels);

        // The stored file is the PNG itself. The portfolio writer already picks
        // its drawing path by extension, so an image renders with no change
        // there - what was missing was only the way in.
        string storedPath = ProjectWorkspacePaths.ResolveInsideProject(
            projectPath,
            item.RelativePath);
        Assert.True(File.Exists(storedPath));
        Assert.Equal(".png", Path.GetExtension(storedPath), ignoreCase: true);
    }

    [Fact]
    public void AnUnknownPayloadMediaTypeIsRefusedAndNamed()
    {
        string manifestPath = WriteRasterPackage("unknown-media");
        RewriteEntry(manifestPath, entry => entry["payloadMediaType"] = "image/tiff");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        // Named, not swallowed. A media type this version does not know must not
        // fall back to PDF: that is how a reader ends up parsing a TIFF as a
        // page and reporting success.
        //
        // 🔴 "NAMES image/tiff" WAS NOT ENOUGH, AND SABOTAGE PROVED IT. With the
        // unsupported-type check deleted this test stayed green, because the
        // extension check then refused the same entry with its own message -
        // which also quotes the media type. One rule was under test and a
        // neighbour was answering for it. The word "unsupported" belongs to this
        // refusal alone.
        Assert.Contains(
            result.Issues,
            issue => issue.Contains("image/tiff", StringComparison.Ordinal) &&
                issue.Contains("unsupported", StringComparison.Ordinal));
    }

    [Fact]
    public void ARasterPayloadIsNeverHandedOutWhereAPdfIsExpected()
    {
        string manifestPath = WriteRasterPackage("not-a-pdf");
        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);
        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        SheetPackageEntry entry = Assert.Single(result.Manifest!.Sheets);

        // The album builder and the sheet library both ask for a PDF path by
        // name. Neither is changed by this work, so the guard cannot be "today's
        // callers only send PDFs" - the accessor itself refuses.
        Assert.False(result.TryGetVerifiedPdfPath(entry, out string pdfPath));
        Assert.Equal("", pdfPath);

        // The payload is reachable through the accessor that does not promise a
        // file type.
        Assert.True(result.TryGetVerifiedPayloadPath(entry, out string payloadPath));
        Assert.Equal(".png", Path.GetExtension(payloadPath), ignoreCase: true);
    }

    [Fact]
    public void DeclaredPixelSizeMustMatchTheFileItDescribes()
    {
        string manifestPath = WriteRasterPackage("wrong-pixels");
        RewriteEntry(manifestPath, entry => entry["payloadWidthPixels"] = 1234);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        // Both numbers appear: a declaration that disagrees with its file is
        // useless to whoever has to decide which one to trust.
        Assert.Contains(
            result.Issues,
            issue => issue.Contains("1234", StringComparison.Ordinal) &&
                issue.Contains(
                    A3LandscapeWidthPixels.ToString(),
                    StringComparison.Ordinal));
    }

    [Fact]
    public void ARasterPayloadIsRefusedBelowTheSchemaThatIntroducedIt()
    {
        string manifestPath = WriteRasterPackage("old-schema");
        RewriteManifest(manifestPath, manifest => manifest["schemaVersion"] = 5);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains(
                SheetPackageManifest.FirstRasterPayloadSchemaVersion.ToString(),
                StringComparison.Ordinal));
    }

    [Fact]
    public void ARasterPayloadCannotClaimToBeCleanDrawingSpace()
    {
        string manifestPath = WriteRasterPackage("clean-claim");
        RewriteEntry(manifestPath, entry => entry["isCleanDrawingSpace"] = true);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        // Clean drawing space means the frame, the title and the company table
        // are NOT in the payload, so Studio may draw its own. Erk-S CAD renders
        // all of that into the image; a page claiming otherwise would be framed
        // twice.
        //
        // 🔴 MATCHING ON "clean" ALONE PASSED WITH THIS RULE DELETED. A rule that
        // predates raster payloads - a clean drawing-space PDF must carry an
        // inline format - refuses the same entry and says "clean" too. The
        // sentence has to name the payload it is about.
        Assert.Contains(
            result.Issues,
            issue => issue.Contains("raster", StringComparison.OrdinalIgnoreCase) &&
                issue.Contains("clean", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ErkSCadIsANamedSourceApplicationRatherThanAnUnknownString()
    {
        string manifestPath = WriteRasterPackage("named-source");
        string json = File.ReadAllText(manifestPath);

        // The wire value the Erk-S CAD session said it will write, verbatim.
        Assert.Contains("\"ErkSCad\"", json, StringComparison.Ordinal);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);
        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        Assert.Equal(
            SheetSourceApplication.ErkSCad,
            result.Manifest!.Source.Application);
    }

    [Theory]
    [InlineData("erkscad")]
    [InlineData("ErksCad")]
    [InlineData("ERKSCAD")]
    public void TheSourceApplicationIsReadWithoutRegardToCase(string spelling)
    {
        // Written one exact way, read tolerantly. The producer is a separate
        // program in a separate repository: if the boundary only accepted the
        // casing this enum happens to declare, a capital letter moving in their
        // exporter would reject every package - and the failure would look like
        // a corrupt manifest rather than a spelling.
        string manifestPath = WriteRasterPackage("case-" + spelling.ToLowerInvariant());
        RewriteManifest(
            manifestPath,
            manifest => manifest["source"]!.AsObject()["application"] = spelling);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        Assert.Equal(
            SheetSourceApplication.ErkSCad,
            result.Manifest!.Source.Application);
    }

    [Fact]
    public void AnApplicationNameThisVersionDoesNotKnowFailsTheWholePackage()
    {
        string manifestPath = WriteRasterPackage("unknown-app");
        RewriteManifest(
            manifestPath,
            manifest => manifest["source"]!.AsObject()["application"] = "SomeFutureCad");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        // Measured rather than assumed: the manifest is deserialized with
        // JsonStringEnumConverter, so an unrecognised name throws and the load
        // reports it. It does NOT arrive as Manual (enum value 0), which is what
        // I first told the Erk-S CAD session - the failure is loud, and this
        // test is here so the next reader does not have to take my word for it.
        Assert.False(result.IsLossless);
        Assert.Null(result.Manifest);
    }

    [Fact]
    public void ThePublishedExampleIsWHATTHISWRITERProduces()
    {
        // 🔴 THE EXAMPLE IS THE CONTRACT, SO IT MUST NOT BE HAND-TYPED. Erk-S CAD
        // writes its manifests in another repository and said it would diff them
        // against whatever example I published. An example written by hand can
        // differ from what this reader accepts in exactly the ways that are
        // hardest to see - a field's casing, a number's shape, the spelling of an
        // enum - and the producer would be matching my prose instead of my code.
        //
        // So the published file is compared against a fresh run of the writer.
        // The payload hash is the one part that belongs to the sample PNG rather
        // than to the contract, and the sample image is not shipped, so it is
        // blanked on both sides instead of being asserted.
        string manifestPath = WriteRasterPackage("published-example");
        string produced = Blank(File.ReadAllText(manifestPath));
        string published = Blank(
            SharedContractCopies.Read(SharedContractCopies.ErkSCadDiagramManifest));

        Assert.Equal(published, produced, ignoreLineEndingDifferences: true);
    }

    /// <summary>
    /// Removes the two values that differ between runs: the package id, which is
    /// new per export by design, and the payload hash, which belongs to the
    /// sample image rather than to the contract.
    /// </summary>
    private static string Blank(string manifestJson)
    {
        JsonObject manifest = JsonNode.Parse(manifestJson)!.AsObject();
        manifest["packageId"] = "00000000-0000-0000-0000-000000000000";
        foreach (JsonNode? sheet in manifest["sheets"]!.AsArray())
        {
            sheet!.AsObject()["sha256"] = "";
        }
        return manifest.ToJsonString(SheetPackageJson.Options);
    }

    private string WriteRasterPackage(string folderName)
    {
        string directory = Path.Combine(workDirectory, folderName);
        Directory.CreateDirectory(directory);
        const int heightPixels = 3508;
        string fileName = "01-Насжилтын диаграм.png";
        WritePng(Path.Combine(directory, fileName), A3LandscapeWidthPixels, heightPixels);

        var manifest = new SheetPackageManifest
        {
            SchemaVersion = SheetPackageManifest.CurrentSchemaVersion,
            Source = new SheetPackageSource
            {
                SourceId = SourceId,
                Application = SheetSourceApplication.ErkSCad,
                DocumentPath = @"D:\Cloud Work\ZuunModXET\Erk-S\ZuunMod.erks",
                DocumentTitle = "ZuunMod",
            },
            ExportedAtUtc = new DateTimeOffset(2026, 9, 27, 8, 0, 0, TimeSpan.Zero),
            Sheets =
            {
                new SheetPackageEntry
                {
                    SheetId = "thematic-age",
                    Number = "01",
                    Name = "Насжилтын диаграм",
                    SheetDescription = "Аль хэсэг нь хамгийн хуучин вэ?",
                    ContentKind = "thematic-age",
                    Destination = SheetDestinations.Portfolio,
                    WidthMm = 420,
                    HeightMm = 420d * heightPixels / A3LandscapeWidthPixels,
                    PdfFileName = fileName,
                    PdfPageNumber = 1,
                    PayloadMediaType = SheetPayloadMediaTypes.Png,
                    PayloadWidthPixels = A3LandscapeWidthPixels,
                    PayloadHeightPixels = heightPixels,
                },
            },
        };

        return SheetPackageWriter.Write(manifest, directory, "erks-cad-diagrams");
    }

    private (ProjectWorkspace Project, string ProjectPath) CreateProject()
    {
        string projectFolder = Path.Combine(
            workDirectory,
            "project-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(projectFolder);
        var project = new ProjectWorkspace
        {
            Sources =
            [
                new ProjectDesignSource
                {
                    Id = SourceId,
                    Name = "Erk-S CAD диаграм",
                },
            ],
        };
        return (project, Path.Combine(projectFolder, "project.erksproj"));
    }

    private static void RewriteEntry(string manifestPath, Action<JsonObject> change) =>
        RewriteManifest(
            manifestPath,
            manifest => change(manifest["sheets"]!.AsArray()[0]!.AsObject()));

    private static void RewriteManifest(string manifestPath, Action<JsonObject> change)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        change(manifest);
        File.WriteAllText(manifestPath, manifest.ToJsonString(SheetPackageJson.Options));
    }

    /// <summary>
    /// A real PNG, not a stub with the right first eight bytes: a probe that
    /// only ever meets a hand-made header proves nothing about the files Erk-S
    /// CAD actually writes.
    /// </summary>
    private static void WritePng(string path, int width, int height)
    {
        using var file = new FileStream(path, FileMode.Create, FileAccess.Write);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        var header = new byte[13];
        BigEndian(header, 0, width);
        BigEndian(header, 4, height);
        header[8] = 8;  // bit depth
        header[9] = 2;  // truecolour
        WriteChunk(file, "IHDR", header);

        // One filter byte plus three channels per pixel, per row.
        var raw = new byte[height * (1 + (width * 3))];
        using (var compressed = new MemoryStream())
        {
            using (var deflate = new ZLibStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
            {
                deflate.Write(raw);
            }
            WriteChunk(file, "IDAT", compressed.ToArray());
        }

        WriteChunk(file, "IEND", []);
    }

    private static void WriteChunk(Stream target, string type, byte[] data)
    {
        var length = new byte[4];
        BigEndian(length, 0, data.Length);
        target.Write(length);

        var typeAndData = new byte[4 + data.Length];
        for (int index = 0; index < 4; index++)
        {
            typeAndData[index] = (byte)type[index];
        }
        data.CopyTo(typeAndData, 4);
        target.Write(typeAndData);

        var crc = new byte[4];
        BigEndian(crc, 0, unchecked((int)Crc32(typeAndData)));
        target.Write(crc);
    }

    private static void BigEndian(byte[] target, int offset, int value)
    {
        target[offset] = (byte)(value >> 24);
        target[offset + 1] = (byte)(value >> 16);
        target[offset + 2] = (byte)(value >> 8);
        target[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte value in data)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }
        return crc ^ 0xFFFFFFFFu;
    }
}
