using System.Text.Json;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

public sealed class AlbumBuildIntegrityTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-album-integrity-" + Guid.NewGuid().ToString("N"));

    public AlbumBuildIntegrityTests()
    {
        Directory.CreateDirectory(workDirectory);
    }

    [Fact]
    public void MissingConfiguredOrLegacySheet_ProducesControlledFailure()
    {
        var configured = new AlbumProject { Name = "Configured missing" };
        configured.Album.Pages.Add(new AlbumPageDefinition { SheetKey = "missing-source|sheet" });

        AlbumBuildException configuredFailure = Assert.Throws<AlbumBuildException>(() =>
            new AlbumBuilder(new RecordingWriter()).Build(
                configured,
                new SheetLibrary(),
                Path.Combine(workDirectory, "configured.pdf")));

        Assert.Contains("missing or unverified", configuredFailure.Message, StringComparison.OrdinalIgnoreCase);

        var legacy = new AlbumProject { Name = "Legacy missing" };
        legacy.Album.Pages.Clear();
        legacy.Album.Sections.Add(new AlbumSection
        {
            Title = "Missing",
            SheetKeys = ["missing-source|legacy"],
        });
        AlbumBuildException legacyFailure = Assert.Throws<AlbumBuildException>(() =>
            new AlbumBuilder(new RecordingWriter()).Build(
                legacy,
                new SheetLibrary(),
                Path.Combine(workDirectory, "legacy.pdf")));
        Assert.Contains("missing or unverified", legacyFailure.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WriterFailureOrMissingOutput_NeverReplacesCanonicalAlbum()
    {
        string canonicalPath = Path.Combine(workDirectory, "canonical.pdf");
        byte[] previous = [9, 8, 7, 6];
        File.WriteAllBytes(canonicalPath, previous);
        var project = new AlbumProject { Name = "Atomic" };

        AlbumBuildException composeFailure = Assert.Throws<AlbumBuildException>(() =>
            new AlbumBuilder(new RecordingWriter(writeOutput: true, throwAfterWrite: true)).Build(
                project,
                new SheetLibrary(),
                canonicalPath));

        Assert.Contains("writer failure", composeFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(previous, File.ReadAllBytes(canonicalPath));
        Assert.Empty(Directory.EnumerateFiles(workDirectory, "*.tmp.pdf"));

        AlbumBuildException noOutputFailure = Assert.Throws<AlbumBuildException>(() =>
            new AlbumBuilder(new RecordingWriter(writeOutput: false)).Build(
                project,
                new SheetLibrary(),
                canonicalPath));
        Assert.Contains("did not produce", noOutputFailure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(previous, File.ReadAllBytes(canonicalPath));
    }

    [Fact]
    public void SuccessfulWriterAtomicallyReplacesCanonicalAndReturnsPublicPath()
    {
        string canonicalPath = Path.Combine(workDirectory, "success.pdf");
        File.WriteAllBytes(canonicalPath, [1]);
        var writer = new RecordingWriter(writeOutput: true);

        AlbumBuildResult result = new AlbumBuilder(writer).Build(
            new AlbumProject { Name = "Success" },
            new SheetLibrary(),
            canonicalPath);

        Assert.Equal(canonicalPath, result.OutputPath);
        Assert.Equal([4, 5, 6], File.ReadAllBytes(canonicalPath));
        Assert.NotEqual(canonicalPath, writer.ReceivedOutputPath);
        Assert.EndsWith(".tmp.pdf", writer.ReceivedOutputPath, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(writer.ReceivedOutputPath));
    }

    [Theory]
    [InlineData("package-id")]
    [InlineData("path")]
    [InlineData("hash")]
    [InlineData("sheet-id")]
    public void PackageIdentityPathOrHashChangedAfterIntake_BlocksBuild(string mutation)
    {
        PackageContext package = CreatePackage(mutation);
        var library = new SheetLibrary();
        SheetPackageLoadResult intake = SheetPackageReader.Load(package.ManifestPath);
        library.Absorb(intake);
        SheetRecord record = Assert.Single(library.Snapshot());
        SheetPackageManifest manifest = LoadManifest(package.ManifestPath);

        if (mutation == "package-id")
        {
            manifest.PackageId = Guid.NewGuid();
        }
        else if (mutation == "path")
        {
            string alternatePath = Path.Combine(workDirectory, "alternate.pdf");
            File.Copy(package.PdfPath, alternatePath, overwrite: true);
            manifest.Sheets[0].PdfFileName = Path.GetFileName(alternatePath);
        }
        else if (mutation == "hash")
        {
            WritePdf(package.PdfPath, "changed but valid");
            manifest.Sheets[0].Sha256 = SheetPackageReader.ComputeSha256(package.PdfPath);
        }
        else
        {
            manifest.Sheets[0].SheetId = "different-sheet-id";
        }
        SaveManifest(package.ManifestPath, manifest);

        var project = new AlbumProject { Name = "Changed package" };
        project.Album.IncludeCover = false;
        project.Album.IncludeTableOfContents = false;
        project.Album.Pages.Add(new AlbumPageDefinition
        {
            SheetKey = record.Key,
            PageFormatId = PageFormatCatalog.SourceAsIsId,
            PlacementMode = PagePlacementMode.FullPage,
        });
        string outputPath = Path.Combine(workDirectory, mutation + "-album.pdf");

        AlbumBuildException failure = Assert.Throws<AlbumBuildException>(() =>
            new AlbumBuilder(new RecordingWriter()).Build(project, library, outputPath));

        Assert.Contains(mutation switch
        {
            "package-id" => "identity",
            "path" => "path changed",
            "hash" => "hash changed",
            _ => "entry is unavailable",
        }, failure.Message, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void CustomConfiguredAlbumBuildsDefinedAndUnsectionedRuns()
    {
        PackageContext package = CreatePackage("configured-runs");
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(package.ManifestPath));
        SheetRecord record = Assert.Single(library.Snapshot());
        var project = new AlbumProject { Name = "Configured runs" };
        project.Album.TemplateId = "custom-template";
        project.Album.Sections.Clear();
        var section = new AlbumSection { Title = "Defined" };
        project.Album.Sections.Add(section);
        project.Album.Pages.Add(new AlbumPageDefinition
        {
            SheetKey = record.Key,
            SectionId = section.Id,
        });
        project.Album.Pages.Add(new AlbumPageDefinition { SheetKey = record.Key });

        AlbumBuildRequest request = AlbumBuilder.CreateRequest(project, library);

        Assert.Equal(2, request.Sections.Count);
        Assert.Single(request.Sections[0].Pages);
        Assert.Single(request.Sections[1].Pages);

        project.Album.Sections.Clear();
        request = AlbumBuilder.CreateRequest(project, library);
        Assert.Single(request.Sections);
        Assert.Equal(2, request.Sections[0].Pages.Count);
        Assert.Equal("", request.Sections[0].Title);
    }

    [Fact]
    public void LegacyConfiguredSectionResolvesVerifiedSheet()
    {
        PackageContext package = CreatePackage("legacy-section");
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(package.ManifestPath));
        SheetRecord record = Assert.Single(library.Snapshot());
        var project = new AlbumProject { Name = "Legacy section" };
        project.Album.Pages.Clear();
        project.Album.Sections.Add(new AlbumSection
        {
            Title = "Legacy",
            SheetKeys = [record.Key],
        });

        AlbumBuildRequest request = AlbumBuilder.CreateRequest(project, library);

        AlbumBuildPage page = Assert.Single(Assert.Single(request.Sections).Pages);
        Assert.Equal(record.Key, page.Sheet.Key);
        Assert.Equal(PageFormatCatalog.SourceAsIsId, page.Definition.PageFormatId);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(workDirectory, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide the assertion that failed.
        }
    }

    private PackageContext CreatePackage(string name)
    {
        string pdfPath = Path.Combine(workDirectory, name + ".pdf");
        WritePdf(pdfPath, name);
        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                SourceId = "album-integrity-source",
                Application = SheetSourceApplication.Revit,
            },
            Sheets =
            [
                new SheetPackageEntry
                {
                    SheetId = "sheet-1",
                    Number = "A-01",
                    Name = "Integrity sheet",
                    WidthMm = 210,
                    HeightMm = 297,
                    ContentWidthMm = 210,
                    ContentHeightMm = 297,
                    PdfFileName = Path.GetFileName(pdfPath),
                    PageCount = 1,
                },
            ],
        };
        string manifestPath = SheetPackageWriter.Write(manifest, workDirectory, name);
        return new PackageContext(manifestPath, pdfPath);
    }

    private static void WritePdf(string path, string label)
    {
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(210);
        page.Height = XUnit.FromMillimeter(297);
        using XGraphics graphics = XGraphics.FromPdfPage(page);
        graphics.DrawRectangle(XPens.Black, 20, 20, 100, 100);
        graphics.DrawLine(XPens.Black, 20, 130, 20 + label.Length * 3, 130);
        document.Save(path);
    }

    private static SheetPackageManifest LoadManifest(string path) =>
        JsonSerializer.Deserialize<SheetPackageManifest>(
            File.ReadAllText(path),
            SheetPackageJson.Options)!;

    private static void SaveManifest(string path, SheetPackageManifest manifest) =>
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, SheetPackageJson.Options));

    private sealed record PackageContext(string ManifestPath, string PdfPath);

    private sealed class RecordingWriter(
        bool writeOutput = true,
        bool throwAfterWrite = false) : IAlbumPdfWriter
    {
        public string ReceivedOutputPath { get; private set; } = "";

        public AlbumBuildResult Compose(AlbumBuildRequest request, string outputPath)
        {
            ReceivedOutputPath = outputPath;
            if (writeOutput)
            {
                File.WriteAllBytes(outputPath, [4, 5, 6]);
            }
            if (throwAfterWrite)
            {
                throw new InvalidDataException("writer failure");
            }
            return new AlbumBuildResult
            {
                OutputPath = outputPath,
                SheetCount = request.Sections.Sum(section => section.Pages.Count),
                PageCount = 1,
            };
        }
    }
}
