using System.Text.Json;
using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

public sealed class SheetPackageSecurityTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-sheet-package-security-tests",
        Guid.NewGuid().ToString("N"));

    public SheetPackageSecurityTests()
    {
        WindowsFontResolver.Register();
        Directory.CreateDirectory(workDirectory);
    }

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
    public void TamperedPdf_DoesNotReplaceVerifiedSheet()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PackageFiles original = WritePackage("original", ["A1"], SheetPackageScope.Delta, now.AddMinutes(-1));
        PackageFiles tampered = WritePackage("tampered", ["A1"], SheetPackageScope.Delta, now);
        File.AppendAllText(tampered.PdfPaths[0], "tampered");
        var library = new SheetLibrary();

        library.Absorb(SheetPackageReader.Load(original.ManifestPath));
        SheetLibraryChange change = library.Absorb(SheetPackageReader.Load(tampered.ManifestPath));

        SheetRecord retained = Assert.Single(library.Snapshot());
        Assert.Equal(original.PackageId, retained.PackageId);
        Assert.True(retained.IsVerified);
        Assert.False(change.HasChanges);
    }

    [Fact]
    public void MissingPdf_DoesNotReplaceVerifiedSheet()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PackageFiles original = WritePackage("original", ["A1"], SheetPackageScope.Delta, now.AddMinutes(-1));
        PackageFiles missing = WritePackage("missing", ["A1"], SheetPackageScope.Delta, now);
        File.Delete(missing.PdfPaths[0]);
        var library = new SheetLibrary();

        library.Absorb(SheetPackageReader.Load(original.ManifestPath));
        SheetLibraryChange change = library.Absorb(SheetPackageReader.Load(missing.ManifestPath));

        SheetRecord retained = Assert.Single(library.Snapshot());
        Assert.Equal(original.PackageId, retained.PackageId);
        Assert.True(retained.IsVerified);
        Assert.False(change.HasChanges);
    }

    [Fact]
    public void InvalidManifest_DoesNotModifyLibrary()
    {
        PackageFiles original = WritePackage(
            "original",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        string invalidManifest = Path.Combine(workDirectory, "invalid.erks-sheets.json");
        File.WriteAllText(invalidManifest, "{ not-json");
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(original.ManifestPath));

        SheetLibraryChange change = library.Absorb(SheetPackageReader.Load(invalidManifest));

        SheetRecord retained = Assert.Single(library.Snapshot());
        Assert.Equal(original.PackageId, retained.PackageId);
        Assert.False(change.HasChanges);
    }

    [Fact]
    public void InvalidFullSnapshot_DoesNotDeleteOrReplaceExistingSheets()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        PackageFiles original = WritePackage(
            "original-full",
            ["A1", "A2"],
            SheetPackageScope.FullSnapshot,
            now.AddMinutes(-1));
        PackageFiles invalid = WritePackage(
            "invalid-full",
            ["A1"],
            SheetPackageScope.FullSnapshot,
            now);
        File.AppendAllText(invalid.PdfPaths[0], "tampered");
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(original.ManifestPath));

        SheetLibraryChange change = library.Absorb(SheetPackageReader.Load(invalid.ManifestPath));

        Assert.False(change.HasChanges);
        Assert.Equal(2, library.Snapshot().Count);
        Assert.All(library.Snapshot(), record =>
        {
            Assert.Equal(original.PackageId, record.PackageId);
            Assert.True(record.IsVerified);
        });
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("..\\outside.pdf")]
    [InlineData("sub/../../outside.pdf")]
    [InlineData("file:///C:/private/document.pdf")]
    public void RelativeTraversalOrUri_IsRejected(string untrustedPath)
    {
        PackageFiles package = WritePackage("path", ["A1"], SheetPackageScope.Delta, DateTimeOffset.UtcNow);
        SetPdfPath(package.ManifestPath, untrustedPath, package.PdfPaths[0]);

        SheetPackageLoadResult result = SheetPackageReader.Load(package.ManifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, IsUnsafePathIssue);
    }

    [Fact]
    public void AbsoluteWindowsPath_IsRejectedEvenWhenFileExistsAndHashMatches()
    {
        PackageFiles package = WritePackage("absolute", ["A1"], SheetPackageScope.Delta, DateTimeOffset.UtcNow);
        SetPdfPath(package.ManifestPath, Path.GetFullPath(package.PdfPaths[0]), package.PdfPaths[0]);

        SheetPackageLoadResult result = SheetPackageReader.Load(package.ManifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, IsUnsafePathIssue);
    }

    [Theory]
    [InlineData("\\\\server\\share\\document.pdf")]
    [InlineData("/etc/document.pdf")]
    public void UncOrUnixAbsolutePath_IsRejected(string untrustedPath)
    {
        PackageFiles package = WritePackage("rooted", ["A1"], SheetPackageScope.Delta, DateTimeOffset.UtcNow);
        SetPdfPath(package.ManifestPath, untrustedPath, package.PdfPaths[0]);

        SheetPackageLoadResult result = SheetPackageReader.Load(package.ManifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, IsUnsafePathIssue);
    }

    [Fact]
    public void NullBytePath_IsRejectedWithoutThrowing()
    {
        PackageFiles package = WritePackage("null-byte", ["A1"], SheetPackageScope.Delta, DateTimeOffset.UtcNow);
        SetPdfPath(package.ManifestPath, "sheet\0.pdf", package.PdfPaths[0]);

        Exception? exception = Record.Exception(() => SheetPackageReader.Load(package.ManifestPath));
        SheetPackageLoadResult result = SheetPackageReader.Load(package.ManifestPath);

        Assert.Null(exception);
        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, IsUnsafePathIssue);
    }

    [Fact]
    public void ValidAllowedSubfolder_IsAccepted()
    {
        string packageFolder = Path.Combine(workDirectory, "subfolder");
        string pdfFolder = Path.Combine(packageFolder, "pdf");
        Directory.CreateDirectory(pdfFolder);
        string pdfPath = Path.Combine(pdfFolder, "A1.pdf");
        WriteVectorPdf(pdfPath, "A1", 420, 297);
        var manifest = CreateManifest([CreateEntry("A1", "pdf/A1.pdf", 420, 297)]);
        string manifestPath = SheetPackageWriter.Write(manifest, packageFolder, "subfolder");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
    }

    [Fact]
    public void DuplicateSheetIdOrFilename_IsRejected()
    {
        string packageFolder = Path.Combine(workDirectory, "duplicates");
        Directory.CreateDirectory(packageFolder);
        WriteVectorPdf(Path.Combine(packageFolder, "shared.pdf"), "Shared", 420, 297);
        var manifest = CreateManifest(
        [
            CreateEntry("A1", "shared.pdf", 420, 297),
            CreateEntry("A1", "shared.pdf", 420, 297),
        ]);
        manifest.SchemaVersion = 4;
        string hash = SheetPackageReader.ComputeSha256(Path.Combine(packageFolder, "shared.pdf"));
        foreach (SheetPackageEntry entry in manifest.Sheets)
        {
            entry.Sha256 = hash;
        }
        string manifestPath = Path.Combine(packageFolder, "duplicates" + SheetPackageManifest.ManifestSuffix);
        SaveManifest(manifestPath, manifest);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("duplicate sheet id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Issues, issue => issue.Contains("duplicate PDF filename", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SchemaFive_AllowsDistinctPagesOfOneSharedPdf()
    {
        string packageFolder = Path.Combine(workDirectory, "shared-pages");
        Directory.CreateDirectory(packageFolder);
        string pdfPath = Path.Combine(packageFolder, "layouts.pdf");
        WriteMultiPageVectorPdf(pdfPath, 2, 420, 297);
        var first = CreateEntry("A1", "layouts.pdf", 420, 297);
        first.PdfPageNumber = 1;
        var second = CreateEntry("A2", "layouts.pdf", 420, 297);
        second.PdfPageNumber = 2;
        SheetPackageManifest manifest = CreateManifest([first, second]);
        manifest.SchemaVersion = SheetPackageManifest.CurrentSchemaVersion;

        string manifestPath = SheetPackageWriter.Write(manifest, packageFolder, "shared-pages");
        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.True(result.IsLossless, string.Join(" | ", result.Issues));
        Assert.Equal([1, 2], result.Manifest!.Sheets.Select(sheet => sheet.PdfPageNumber));
        Assert.All(result.Manifest.Sheets, sheet => Assert.Equal("layouts.pdf", sheet.PdfFileName));
    }

    [Fact]
    public void SchemaFive_RejectsPageReferenceOutsideSharedPdf()
    {
        string packageFolder = Path.Combine(workDirectory, "shared-page-range");
        Directory.CreateDirectory(packageFolder);
        string pdfPath = Path.Combine(packageFolder, "layouts.pdf");
        WriteMultiPageVectorPdf(pdfPath, 2, 420, 297);
        SheetPackageEntry entry = CreateEntry("A3", "layouts.pdf", 420, 297);
        entry.PdfPageNumber = 3;
        SheetPackageManifest manifest = CreateManifest([entry]);
        manifest.SchemaVersion = SheetPackageManifest.CurrentSchemaVersion;
        entry.Sha256 = SheetPackageReader.ComputeSha256(pdfPath);
        string manifestPath = Path.Combine(
            packageFolder,
            "shared-page-range" + SheetPackageManifest.ManifestSuffix);
        SaveManifest(manifestPath, manifest);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue =>
            issue.Contains("outside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PdfDimensionsAndPageCountMustMatchManifest()
    {
        PackageFiles package = WritePackage("geometry", ["A1"], SheetPackageScope.Delta, DateTimeOffset.UtcNow);
        SheetPackageManifest manifest = LoadManifest(package.ManifestPath);
        manifest.Sheets[0].WidthMm = 297;
        manifest.Sheets[0].HeightMm = 210;
        manifest.Sheets[0].PageCount = 2;
        SaveManifest(package.ManifestPath, manifest);

        SheetPackageLoadResult result = SheetPackageReader.Load(package.ManifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("PDF page size", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Issues, issue => issue.Contains("page count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PathTraversalPackage_DoesNotModifyLibrary()
    {
        PackageFiles valid = WritePackage(
            "valid",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        PackageFiles malicious = WritePackage(
            "malicious",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        string outsidePdf = Path.Combine(workDirectory, "outside.pdf");
        File.Copy(malicious.PdfPaths[0], outsidePdf, overwrite: true);
        SetPdfPath(malicious.ManifestPath, "../outside.pdf", outsidePdf);
        var library = new SheetLibrary();
        library.Absorb(SheetPackageReader.Load(valid.ManifestPath));

        SheetPackageLoadResult result = SheetPackageReader.Load(malicious.ManifestPath);
        SheetLibraryChange change = library.Absorb(result);

        Assert.False(result.IsLossless);
        Assert.False(change.HasChanges);
        Assert.Equal(valid.PackageId, Assert.Single(library.Snapshot()).PackageId);
    }

    [Fact]
    public void RejectedPackage_IsAuditedWithoutMovingOrDeletingSourceFiles()
    {
        PackageFiles package = WritePackage(
            "quarantine",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        File.AppendAllText(package.PdfPaths[0], "tampered");
        var library = new SheetLibrary();
        using var intake = new SheetIntakeService(library);
        string packageFolder = Path.GetDirectoryName(package.ManifestPath)!;

        intake.WatchFolder(packageFolder);
        SheetIntakeScanResult scan = intake.Rescan();

        Assert.Empty(library.Snapshot());
        Assert.True(scan.ErrorCount >= 1);
        Assert.Contains(intake.RejectedPackages, rejected =>
            string.Equals(rejected.ManifestPath, package.ManifestPath, StringComparison.OrdinalIgnoreCase) &&
            rejected.Issues.Any(issue => issue.Contains("hash mismatch", StringComparison.OrdinalIgnoreCase)));
        string auditPath = Path.Combine(packageFolder, ".erks-quarantine", "rejected-packages.jsonl");
        Assert.True(File.Exists(auditPath));
        Assert.Contains("hash mismatch", File.ReadAllText(auditPath), StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(package.ManifestPath));
        Assert.True(File.Exists(package.PdfPaths[0]));
    }

    [Fact]
    public void InvalidPackage_DoesNotModifyAlbumPagesOrCloudSyncMetadata()
    {
        PackageFiles package = WritePackage(
            "project-boundary",
            ["A1"],
            SheetPackageScope.FullSnapshot,
            DateTimeOffset.UtcNow);
        File.AppendAllText(package.PdfPaths[0], "tampered");
        SheetPackageLoadResult rejected = SheetPackageReader.Load(package.ManifestPath);
        var source = new ProjectDesignSource
        {
            Id = "security-source",
            Name = "Revit source",
            Status = DesignSourceStatuses.WaitingForConnection,
        };
        var project = new ProjectWorkspace { Sources = [source] };
        var album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");
        album.Pages.Add(new AlbumPageDefinition { SheetKey = "security-source|existing" });
        var library = new SheetLibrary();

        ProjectPackageReconciliationResult? result = ProjectPackageReconciliationService.Apply(
            project,
            album,
            library,
            rejected);

        Assert.Null(result);
        Assert.Equal(["security-source|existing"], album.Pages.Select(page => page.SheetKey));
        Assert.Equal(DesignSourceStatuses.WaitingForConnection, source.Status);
        Assert.Null(source.LastPackageAtUtc);
        Assert.Empty(ProjectCloudSyncMetadata.SourcePackages(project));
    }

    [Fact]
    public void ValidPackage_ReconcilesAlbumAndCloudMetadataAfterVerifiedAbsorb()
    {
        PackageFiles package = WritePackage(
            "valid-project-boundary",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        SheetPackageLoadResult verified = SheetPackageReader.Load(package.ManifestPath);
        var library = new SheetLibrary();
        library.Absorb(verified);
        var source = new ProjectDesignSource
        {
            Id = "security-source",
            Name = "Revit source",
        };
        var project = new ProjectWorkspace { Sources = [source] };
        var album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");

        ProjectPackageReconciliationResult? result = ProjectPackageReconciliationService.Apply(
            project,
            album,
            library,
            verified);

        Assert.NotNull(result);
        Assert.Equal(DesignSourceStatuses.Connected, source.Status);
        Assert.Equal(verified.Manifest!.ExportedAtUtc, source.LastPackageAtUtc);
        Assert.Equal("security-source|a1", Assert.Single(album.Pages).SheetKey);
        Assert.Single(ProjectCloudSyncMetadata.SourcePackages(project));
    }

    [Fact]
    public void RequiredManifestMetadataViolations_AreRejected()
    {
        var mutations = new (string Name, Action<SheetPackageManifest> Mutate)[]
        {
            ("schema-zero", manifest => manifest.SchemaVersion = 0),
            ("future-schema", manifest => manifest.SchemaVersion = SheetPackageManifest.CurrentSchemaVersion + 1),
            ("empty-package-id", manifest => manifest.PackageId = Guid.Empty),
            ("empty-source-id", manifest => manifest.Source.SourceId = ""),
            ("empty-sheet-id", manifest => manifest.Sheets[0].SheetId = ""),
            ("empty-filename", manifest => manifest.Sheets[0].PdfFileName = ""),
            ("zero-page-count", manifest => manifest.Sheets[0].PageCount = 0),
            ("zero-width", manifest => manifest.Sheets[0].WidthMm = 0),
            ("zero-height", manifest => manifest.Sheets[0].HeightMm = 0),
        };

        foreach ((string name, Action<SheetPackageManifest> mutate) in mutations)
        {
            PackageFiles package = WritePackage(name, ["A1"], SheetPackageScope.Delta, DateTimeOffset.UtcNow);
            SheetPackageManifest manifest = LoadManifest(package.ManifestPath);
            mutate(manifest);
            SaveManifest(package.ManifestPath, manifest);

            SheetPackageLoadResult result = SheetPackageReader.Load(package.ManifestPath);

            Assert.False(result.IsLossless, name);
            var library = new SheetLibrary();
            Assert.True(library.Absorb(result).Rejected);
            Assert.Empty(library.Snapshot());
        }

        PackageFiles empty = WritePackage(
            "empty-delta",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        SheetPackageManifest emptyManifest = LoadManifest(empty.ManifestPath);
        emptyManifest.Sheets.Clear();
        SaveManifest(empty.ManifestPath, emptyManifest);
        Assert.False(SheetPackageReader.Load(empty.ManifestPath).IsLossless);
    }

    [Fact]
    public void NullLegacyCollections_AreNormalizedThenRejectedWithoutThrowing()
    {
        string manifestPath = Path.Combine(workDirectory, "null-collections" + SheetPackageManifest.ManifestSuffix);
        File.WriteAllText(
            manifestPath,
            """
            {
              "schemaVersion": 4,
              "packageId": "00000000-0000-0000-0000-000000000001",
              "source": null,
              "packageScope": "Delta",
              "sheets": null
            }
            """);

        Exception? exception = Record.Exception(() => SheetPackageReader.Load(manifestPath));
        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.Null(exception);
        Assert.False(result.IsLossless);
        Assert.NotNull(result.Manifest!.Source);
        Assert.NotNull(result.Manifest.Sheets);
    }

    [Fact]
    public void JsonNullManifest_IsRejectedWithoutThrowing()
    {
        string manifestPath = Path.Combine(workDirectory, "json-null" + SheetPackageManifest.ManifestSuffix);
        File.WriteAllText(manifestPath, "null");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("null", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NonPdfExtensionAndMalformedPdf_AreRejected()
    {
        PackageFiles extensionPackage = WritePackage(
            "wrong-extension",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        string disguisedPath = Path.Combine(Path.GetDirectoryName(extensionPackage.ManifestPath)!, "drawing.txt");
        File.Copy(extensionPackage.PdfPaths[0], disguisedPath);
        SetPdfPath(extensionPackage.ManifestPath, "drawing.txt", disguisedPath);

        SheetPackageLoadResult extensionResult = SheetPackageReader.Load(extensionPackage.ManifestPath);

        Assert.False(extensionResult.IsLossless);
        Assert.Contains(extensionResult.Issues, issue => issue.Contains("not a PDF", StringComparison.OrdinalIgnoreCase));

        PackageFiles malformedPackage = WritePackage(
            "malformed-pdf",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        File.WriteAllText(malformedPackage.PdfPaths[0], "not a PDF stream");
        SheetPackageManifest malformedManifest = LoadManifest(malformedPackage.ManifestPath);
        malformedManifest.Sheets[0].Sha256 = SheetPackageReader.ComputeSha256(malformedPackage.PdfPaths[0]);
        SaveManifest(malformedPackage.ManifestPath, malformedManifest);

        SheetPackageLoadResult malformedResult = SheetPackageReader.Load(malformedPackage.ManifestPath);

        Assert.False(malformedResult.IsLossless);
        Assert.Contains(malformedResult.Issues, issue =>
            issue.Contains("structure", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FormatAndCleanDrawingMetadataMismatches_AreRejected()
    {
        PackageFiles package = WritePackage(
            "format-mismatch",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        SheetPackageManifest manifest = LoadManifest(package.ManifestPath);
        PageFormatSpec format = CreateFormat();
        manifest.Sheets[0].Format = format;
        manifest.Sheets[0].PageFormatId = "different-format";
        manifest.Sheets[0].WidthMm = 400;
        manifest.Sheets[0].IsCleanDrawingSpace = true;
        manifest.Sheets[0].ContentWidthMm = 399;
        manifest.Sheets[0].ContentHeightMm = 250;
        SaveManifest(package.ManifestPath, manifest);

        SheetPackageLoadResult result = SheetPackageReader.Load(package.ManifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("format id", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Issues, issue => issue.Contains("manifest page size", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Issues, issue => issue.Contains("drawing area", StringComparison.OrdinalIgnoreCase));

        manifest.Sheets[0].Format = null;
        SaveManifest(package.ManifestPath, manifest);
        SheetPackageLoadResult missingFormat = SheetPackageReader.Load(package.ManifestPath);
        Assert.Contains(missingFormat.Issues, issue =>
            issue.Contains("requires an inline format", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LockedPdf_IsRejectedAsUnverifiableInsteadOfThrowing()
    {
        PackageFiles package = WritePackage(
            "locked-pdf",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        using FileStream lockStream = File.Open(
            package.PdfPaths[0],
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        Exception? exception = Record.Exception(() => SheetPackageReader.Load(package.ManifestPath));
        SheetPackageLoadResult result = SheetPackageReader.Load(package.ManifestPath);

        Assert.Null(exception);
        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue =>
            issue.Contains("could not be verified", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void EmptyOrInvalidRootPath_IsRejectedWithoutThrowing()
    {
        Assert.False(SheetPackagePathSecurity.TryResolvePackageFile(
            workDirectory,
            " ",
            out _,
            out string emptyIssue));
        Assert.Contains("unsafe PDF path", emptyIssue, StringComparison.OrdinalIgnoreCase);

        Exception? exception = Record.Exception(() => SheetPackagePathSecurity.TryResolvePackageFile(
            "root\0folder",
            "sheet.pdf",
            out _,
            out _));
        bool accepted = SheetPackagePathSecurity.TryResolvePackageFile(
            "root\0folder",
            "sheet.pdf",
            out _,
            out string invalidRootIssue);

        Assert.Null(exception);
        Assert.False(accepted);
        Assert.Contains("unsafe PDF path", invalidRootIssue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReparsePointInsidePackage_IsRejectedWhenPlatformCanCreateIt()
    {
        string root = Path.Combine(workDirectory, "reparse-root");
        string outside = Path.Combine(workDirectory, "reparse-outside");
        string link = Path.Combine(root, "linked");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        WriteVectorPdf(Path.Combine(outside, "sheet.pdf"), "Linked", 420, 297);
        try
        {
            Directory.CreateSymbolicLink(link, outside);
        }
        catch (Exception exception) when (
            exception is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return;
        }

        bool childAccepted = SheetPackagePathSecurity.TryResolvePackageFile(
            root,
            "linked/sheet.pdf",
            out _,
            out string childIssue);
        bool rootAccepted = SheetPackagePathSecurity.TryResolvePackageFile(
            link,
            "sheet.pdf",
            out _,
            out string rootIssue);

        Assert.False(childAccepted);
        Assert.False(rootAccepted);
        Assert.Contains("reparse", childIssue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reparse", rootIssue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolvedPathValidatorRejectsParentSiblingAndDifferentRoot()
    {
        string root = Path.Combine(workDirectory, "resolved-root");
        string inside = Path.Combine(root, "sub", "sheet.pdf");
        string parent = Path.GetDirectoryName(root)!;
        string sibling = Path.Combine(parent, "resolved-sibling", "sheet.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(inside)!);

        Assert.True(SheetPackagePathSecurity.TryValidateResolvedPackagePath(
            root,
            inside,
            out string insideIssue));
        Assert.Equal("", insideIssue);
        Assert.False(SheetPackagePathSecurity.TryValidateResolvedPackagePath(
            root,
            parent,
            out string parentIssue));
        Assert.False(SheetPackagePathSecurity.TryValidateResolvedPackagePath(
            root,
            sibling,
            out string siblingIssue));
        Assert.Contains("outside", parentIssue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("outside", siblingIssue, StringComparison.OrdinalIgnoreCase);

        if (OperatingSystem.IsWindows())
        {
            string currentRoot = Path.GetPathRoot(root)!;
            string otherRoot = currentRoot.StartsWith("C", StringComparison.OrdinalIgnoreCase)
                ? @"D:\"
                : @"C:\";
            Assert.False(SheetPackagePathSecurity.TryValidateResolvedPackagePath(
                root,
                Path.Combine(otherRoot, "outside", "sheet.pdf"),
                out string rootedIssue));
            Assert.Contains("outside", rootedIssue, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ResolvedPathValidatorRejectsKnownWindowsRootAndChildJunctions()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string rootJunction = @"C:\Documents and Settings";
        string childJunction = @"C:\Users\All Users";
        if (!Directory.Exists(rootJunction) || !Directory.Exists(childJunction) ||
            (File.GetAttributes(rootJunction) & FileAttributes.ReparsePoint) == 0 ||
            (File.GetAttributes(childJunction) & FileAttributes.ReparsePoint) == 0)
        {
            return;
        }

        Assert.False(SheetPackagePathSecurity.TryValidateResolvedPackagePath(
            rootJunction,
            Path.Combine(rootJunction, "sheet.pdf"),
            out string rootIssue));
        Assert.False(SheetPackagePathSecurity.TryValidateResolvedPackagePath(
            @"C:\Users",
            Path.Combine(childJunction, "sheet.pdf"),
            out string childIssue));
        Assert.False(SheetPackagePathSecurity.TryResolvePackageFile(
            rootJunction,
            "sheet.pdf",
            out _,
            out string resolvedIssue));
        Assert.Contains("reparse", rootIssue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reparse", childIssue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reparse", resolvedIssue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackageChangedAfterIntake_DoesNotReachProjectReconciliation()
    {
        PackageFiles package = WritePackage(
            "reconciliation-toctou",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        SheetPackageLoadResult originallyVerified = SheetPackageReader.Load(package.ManifestPath);
        var library = new SheetLibrary();
        library.Absorb(originallyVerified);
        var source = new ProjectDesignSource { Id = "security-source" };
        var project = new ProjectWorkspace { Sources = [source] };
        var album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");
        File.AppendAllText(package.PdfPaths[0], "changed after intake");

        ProjectPackageReconciliationResult? result = ProjectPackageReconciliationService.Apply(
            project,
            album,
            library,
            originallyVerified);

        Assert.Null(result);
        Assert.Empty(album.Pages);
        Assert.Null(source.LastPackageAtUtc);
        Assert.Empty(ProjectCloudSyncMetadata.SourcePackages(project));
    }

    [Fact]
    public void UnchangedPackage_ReusesVerifiedFilesDuringProjectReconciliation()
    {
        PackageFiles package = WritePackage(
            "reconciliation-dirty-check",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        SheetPackageLoadResult verified = SheetPackageReader.Load(package.ManifestPath);
        var library = new SheetLibrary();
        library.Absorb(verified);
        var source = new ProjectDesignSource { Id = "security-source" };
        var project = new ProjectWorkspace { Sources = [source] };
        var album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");
        using FileStream pdfLock = File.Open(
            package.PdfPaths[0],
            FileMode.Open,
            FileAccess.ReadWrite,
            FileShare.None);

        ProjectPackageReconciliationResult? result = ProjectPackageReconciliationService.Apply(
            project,
            album,
            library,
            verified);

        Assert.NotNull(result);
        Assert.Equal("security-source|a1", Assert.Single(album.Pages).SheetKey);
        Assert.Single(ProjectCloudSyncMetadata.SourcePackages(project));
    }

    [Fact]
    public void ValidFullSnapshot_ReconciliationRemovesOnlyDeletedSourcePages()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-1);
        PackageFiles initial = WritePackage(
            "reconcile-full-initial",
            ["A1", "A2"],
            SheetPackageScope.FullSnapshot,
            start);
        var library = new SheetLibrary();
        SheetPackageLoadResult initialResult = SheetPackageReader.Load(initial.ManifestPath);
        library.Absorb(initialResult);
        var source = new ProjectDesignSource
        {
            Id = "security-source",
            NativeDocumentTitle = "Keep title.rvt",
            NativeDocumentPath = @"D:\keep\title.rvt",
        };
        var project = new ProjectWorkspace { Sources = [source] };
        var album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");
        Assert.NotNull(ProjectPackageReconciliationService.Apply(project, album, library, initialResult));
        Assert.Equal(2, album.Pages.Count);
        AlbumSection section = album.Sections.First();
        section.SheetKeys.AddRange(["security-source|a1", "security-source|a2", "other-source|x1"]);

        PackageFiles current = WritePackage(
            "reconcile-full-current",
            ["A1"],
            SheetPackageScope.FullSnapshot,
            start.AddMinutes(1));
        SheetPackageLoadResult currentResult = SheetPackageReader.Load(current.ManifestPath);
        library.Absorb(currentResult);

        ProjectPackageReconciliationResult? reconciliation = ProjectPackageReconciliationService.Apply(
            project,
            album,
            library,
            currentResult);

        Assert.NotNull(reconciliation);
        Assert.Equal(1, reconciliation.RemovedAlbumPageCount);
        Assert.Equal(["security-source|a1"], album.Pages.Select(page => page.SheetKey));
        Assert.Contains("security-source|a1", section.SheetKeys);
        Assert.DoesNotContain("security-source|a2", section.SheetKeys);
        Assert.Contains("other-source|x1", section.SheetKeys);
        Assert.Equal("Keep title.rvt", source.NativeDocumentTitle);
        Assert.Equal(@"D:\keep\title.rvt", source.NativeDocumentPath);
    }

    [Fact]
    public void ValidPackageForUnknownSourceOrUnabsorbedLibrary_DoesNotMutateProject()
    {
        PackageFiles package = WritePackage(
            "unknown-source",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        SheetPackageLoadResult verified = SheetPackageReader.Load(package.ManifestPath);
        var album = new AlbumDefinition { TemplateId = "custom" };
        var missingSourceProject = new ProjectWorkspace();

        Assert.Null(ProjectPackageReconciliationService.Apply(
            missingSourceProject,
            album,
            new SheetLibrary(),
            verified));

        var source = new ProjectDesignSource { Id = "security-source" };
        var knownSourceProject = new ProjectWorkspace { Sources = [source] };
        Assert.Null(ProjectPackageReconciliationService.Apply(
            knownSourceProject,
            album,
            new SheetLibrary(),
            verified));
        Assert.Equal(DesignSourceStatuses.WaitingForConnection, source.Status);
        Assert.Empty(ProjectCloudSyncMetadata.SourcePackages(knownSourceProject));
    }

    [Fact]
    public void PackageForAnotherProject_DoesNotReachProjectReconciliation()
    {
        PackageFiles package = WritePackage(
            "foreign-project",
            ["A1"],
            SheetPackageScope.Delta,
            DateTimeOffset.UtcNow);
        SheetPackageManifest manifest = LoadManifest(package.ManifestPath);
        manifest.ProjectId = "project-other";
        SaveManifest(package.ManifestPath, manifest);
        SheetPackageLoadResult verified = SheetPackageReader.Load(package.ManifestPath);
        var library = new SheetLibrary();
        library.Absorb(verified);
        var source = new ProjectDesignSource { Id = "security-source" };
        var project = new ProjectWorkspace
        {
            ProjectId = "project-current",
            Sources = [source],
        };
        var album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Concept");

        ProjectPackageReconciliationResult? result = ProjectPackageReconciliationService.Apply(
            project,
            album,
            library,
            verified);

        Assert.Null(result);
        Assert.Empty(album.Pages);
        Assert.Equal(DesignSourceStatuses.WaitingForConnection, source.Status);
        Assert.Empty(ProjectCloudSyncMetadata.SourcePackages(project));
    }

    [Fact]
    public void SheetLibrary_QueriesAuthoritativeStateAndClearAreDeterministic()
    {
        PackageFiles package = WritePackage(
            "library-query",
            ["A1"],
            SheetPackageScope.FullSnapshot,
            DateTimeOffset.UtcNow);
        SheetPackageLoadResult verified = SheetPackageReader.Load(package.ManifestPath);
        var library = new SheetLibrary();
        int changedCount = 0;
        library.Changed += () => changedCount++;

        SheetLibraryChange change = library.Absorb(verified);
        SheetRecord record = Assert.Single(library.VerifiedSnapshot());

        Assert.True(change.FullSnapshotApplied);
        Assert.Same(record, library.Find(record.Key));
        Assert.Same(record, library.FindVerified(record.Key));
        Assert.Null(library.Find("missing"));
        Assert.Null(library.FindVerified("missing"));
        Assert.True(library.IsCurrentAuthoritativeSnapshot(verified.Manifest!));
        Assert.False(library.IsCurrentAuthoritativeSnapshot(new SheetPackageManifest
        {
            Source = verified.Manifest!.Source,
            PackageScope = SheetPackageScope.Delta,
        }));
        Assert.Equal(1, changedCount);

        library.Clear();

        Assert.Empty(library.Snapshot());
        Assert.Empty(library.VerifiedSnapshot());
        Assert.False(library.IsCurrentAuthoritativeSnapshot(verified.Manifest));
        Assert.Equal(2, changedCount);
    }

    [Fact]
    public void StaleFullSnapshotAndOlderDeltaCannotResurrectDeletedSheet()
    {
        DateTimeOffset start = DateTimeOffset.UtcNow.AddMinutes(-2);
        PackageFiles oldFull = WritePackage(
            "stale-old-full",
            ["A1", "A2"],
            SheetPackageScope.FullSnapshot,
            start);
        PackageFiles currentFull = WritePackage(
            "stale-current-full",
            ["A1"],
            SheetPackageScope.FullSnapshot,
            start.AddMinutes(1));
        PackageFiles oldDelta = WritePackage(
            "stale-old-delta",
            ["A2"],
            SheetPackageScope.Delta,
            start.AddSeconds(30));
        var library = new SheetLibrary();
        SheetPackageLoadResult oldFullResult = SheetPackageReader.Load(oldFull.ManifestPath);
        SheetPackageLoadResult currentFullResult = SheetPackageReader.Load(currentFull.ManifestPath);
        library.Absorb(oldFullResult);
        library.Absorb(currentFullResult);

        SheetLibraryChange staleFullChange = library.Absorb(oldFullResult);
        SheetLibraryChange staleDeltaChange = library.Absorb(SheetPackageReader.Load(oldDelta.ManifestPath));
        SheetLibraryChange replayChange = library.Absorb(currentFullResult);

        Assert.True(staleFullChange.StaleSnapshotIgnored);
        Assert.False(staleFullChange.HasChanges);
        Assert.False(staleDeltaChange.HasChanges);
        Assert.False(replayChange.StaleSnapshotIgnored);
        Assert.False(replayChange.HasChanges);
        Assert.Equal("A1", Assert.Single(library.Snapshot()).Entry.SheetId);
        Assert.True(library.IsCurrentAuthoritativeSnapshot(currentFullResult.Manifest!));
        Assert.False(library.IsCurrentAuthoritativeSnapshot(oldFullResult.Manifest!));
    }

    [Fact]
    public void EqualTimestampFullSnapshotsUsePackageIdAsStableTieBreaker()
    {
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        PackageFiles low = WritePackage(
            "tie-low",
            ["A1"],
            SheetPackageScope.FullSnapshot,
            timestamp);
        PackageFiles high = WritePackage(
            "tie-high",
            ["A2"],
            SheetPackageScope.FullSnapshot,
            timestamp);
        SheetPackageManifest lowManifest = LoadManifest(low.ManifestPath);
        lowManifest.PackageId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        SaveManifest(low.ManifestPath, lowManifest);
        SheetPackageManifest highManifest = LoadManifest(high.ManifestPath);
        highManifest.PackageId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");
        SaveManifest(high.ManifestPath, highManifest);
        var library = new SheetLibrary();
        SheetPackageLoadResult lowResult = SheetPackageReader.Load(low.ManifestPath);
        SheetPackageLoadResult highResult = SheetPackageReader.Load(high.ManifestPath);

        library.Absorb(lowResult);
        SheetLibraryChange highChange = library.Absorb(highResult);
        SheetLibraryChange lowReplay = library.Absorb(lowResult);

        Assert.True(highChange.FullSnapshotApplied);
        Assert.True(lowReplay.StaleSnapshotIgnored);
        Assert.Equal("A2", Assert.Single(library.Snapshot()).Entry.SheetId);
    }

    [Fact]
    public void WriterRejectsUnsafeNamesAndAtomicallyKeepsPreviousManifestOnValidationFailure()
    {
        string folder = Path.Combine(workDirectory, "writer-security");
        Directory.CreateDirectory(folder);
        string pdfPath = Path.Combine(folder, "sheet.pdf");
        WriteVectorPdf(pdfPath, "Writer", 420, 297);
        SheetPackageManifest valid = CreateManifest([CreateEntry("A1", "sheet.pdf", 420, 297)]);

        Assert.Throws<ArgumentException>(() =>
            SheetPackageWriter.Write(valid, folder, "../escape"));

        valid.Sheets[0].PdfFileName = "../outside.pdf";
        Assert.Throws<InvalidDataException>(() =>
            SheetPackageWriter.Write(valid, folder, "unsafe-path"));
        Assert.False(File.Exists(Path.Combine(folder, "unsafe-path" + SheetPackageManifest.ManifestSuffix)));

        string publishedPath = Path.Combine(folder, "atomic" + SheetPackageManifest.ManifestSuffix);
        File.WriteAllText(publishedPath, "previous manifest");
        SheetPackageManifest duplicate = CreateManifest(
        [
            CreateEntry("A1", "sheet.pdf", 420, 297),
            CreateEntry("A1", "sheet.pdf", 420, 297),
        ]);

        Assert.Throws<InvalidDataException>(() =>
            SheetPackageWriter.Write(duplicate, folder, "atomic"));
        Assert.Equal("previous manifest", File.ReadAllText(publishedPath));
        Assert.Empty(Directory.EnumerateFiles(folder, "*.tmp"));
    }

    private PackageFiles WritePackage(
        string folderName,
        IReadOnlyList<string> sheetIds,
        SheetPackageScope scope,
        DateTimeOffset exportedAtUtc)
    {
        string packageFolder = Path.Combine(workDirectory, folderName);
        Directory.CreateDirectory(packageFolder);
        var entries = new List<SheetPackageEntry>();
        var pdfPaths = new List<string>();
        foreach (string sheetId in sheetIds)
        {
            string fileName = sheetId + ".pdf";
            string pdfPath = Path.Combine(packageFolder, fileName);
            WriteVectorPdf(pdfPath, sheetId, 420, 297);
            pdfPaths.Add(pdfPath);
            entries.Add(CreateEntry(sheetId, fileName, 420, 297));
        }

        SheetPackageManifest manifest = CreateManifest(entries);
        manifest.PackageScope = scope;
        manifest.ExportedAtUtc = exportedAtUtc;
        string manifestPath = SheetPackageWriter.Write(manifest, packageFolder, folderName);
        return new PackageFiles(manifestPath, pdfPaths, manifest.PackageId);
    }

    private static SheetPackageManifest CreateManifest(IEnumerable<SheetPackageEntry> entries) => new()
    {
        Source = new SheetPackageSource
        {
            SourceId = "security-source",
            Application = SheetSourceApplication.Revit,
            ApplicationVersion = "2026",
            DocumentPath = @"C:\authoring\security.rvt",
            DocumentTitle = "security.rvt",
        },
        Sheets = entries.ToList(),
    };

    private static SheetPackageEntry CreateEntry(
        string sheetId,
        string pdfFileName,
        double widthMm,
        double heightMm) => new()
        {
            SheetId = sheetId,
            Number = sheetId,
            Name = "Security sheet " + sheetId,
            WidthMm = widthMm,
            HeightMm = heightMm,
            ContentWidthMm = widthMm,
            ContentHeightMm = heightMm,
            PdfFileName = pdfFileName,
            PageCount = 1,
        };

    private static void SetPdfPath(string manifestPath, string pdfFileName, string hashSourcePath)
    {
        SheetPackageManifest manifest = LoadManifest(manifestPath);
        manifest.Sheets[0].PdfFileName = pdfFileName;
        manifest.Sheets[0].Sha256 = SheetPackageReader.ComputeSha256(hashSourcePath);
        SaveManifest(manifestPath, manifest);
    }

    private static SheetPackageManifest LoadManifest(string manifestPath) =>
        JsonSerializer.Deserialize<SheetPackageManifest>(
            File.ReadAllText(manifestPath),
            SheetPackageJson.Options)!;

    private static void SaveManifest(string manifestPath, SheetPackageManifest manifest) =>
        File.WriteAllText(
            manifestPath,
            JsonSerializer.Serialize(manifest, SheetPackageJson.Options));

    private static bool IsUnsafePathIssue(string issue) =>
        issue.Contains("unsafe PDF path", StringComparison.OrdinalIgnoreCase);

    private static PageFormatSpec CreateFormat()
    {
        var format = new PageFormatSpec
        {
            Id = "security-a3",
            Name = "Security A3",
            Mode = "Concept",
            Code = "A3",
            Orientation = "LANDSCAPE",
            BindEdge = "LEFT",
            WidthMm = 420,
            HeightMm = 297,
            DrawingArea = new PageRectSpec { X = 15, Y = 14, Width = 400, Height = 250 },
            SheetTitleArea = new PageRectSpec { X = 15, Y = 5, Width = 400, Height = 9 },
            TitleBlockArea = new PageRectSpec { X = 231, Y = 264, Width = 184, Height = 28 },
        };
        format.GeometryHash = PageFormatSpecGeometry.ComputeHash(format);
        return format;
    }

    private static void WriteVectorPdf(
        string path,
        string label,
        double widthMm,
        double heightMm)
    {
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(widthMm);
        page.Height = XUnit.FromMillimeter(heightMm);
        using XGraphics gfx = XGraphics.FromPdfPage(page);
        gfx.DrawLine(new XPen(XColors.Black, 0.5), 20, 20, page.Width.Point - 20, page.Height.Point - 20);
        gfx.DrawString(
            label,
            new XFont("Arial", 12),
            XBrushes.Black,
            new XRect(0, 0, page.Width.Point, page.Height.Point),
            XStringFormats.Center);
        document.Save(path);
    }

    private static void WriteMultiPageVectorPdf(
        string path,
        int pageCount,
        double widthMm,
        double heightMm)
    {
        using var document = new PdfDocument();
        for (int index = 1; index <= pageCount; index++)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(widthMm);
            page.Height = XUnit.FromMillimeter(heightMm);
            using XGraphics gfx = XGraphics.FromPdfPage(page);
            gfx.DrawLine(new XPen(XColors.Black, 0.5), 20, 20 + index, page.Width.Point - 20, page.Height.Point - 20);
        }
        document.Save(path);
    }

    private sealed record PackageFiles(
        string ManifestPath,
        IReadOnlyList<string> PdfPaths,
        Guid PackageId);
}
