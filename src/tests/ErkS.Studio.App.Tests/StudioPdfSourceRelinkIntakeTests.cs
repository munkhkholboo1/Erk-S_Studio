using ErkS.Platform.Core;
using ErkS.Studio;
using PdfSharp.Pdf;

namespace ErkS.Studio.App.Tests;

public sealed class StudioPdfSourceRelinkIntakeTests
{
    [Fact]
    public void ImportAndRescan_MakesRelinkedPdfPagesImmediatelyAvailable()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "erks-studio-pdf-relink-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string nativePath = Path.Combine(root, "source.pdf");
            WritePdf(nativePath, pageCount: 2);
            var source = new ProjectDesignSource
            {
                Id = "pdf-source",
                Kind = DesignSourceKind.Pdf,
                Name = "Relinked PDF",
                NativeDocumentPath = nativePath,
                NativeDocumentTitle = Path.GetFileName(nativePath),
                InboxFolder = Path.Combine(root, "inbox"),
            };
            var project = new ProjectWorkspace
            {
                ProjectId = "project-1",
                Sources = [source],
            };
            var library = new SheetLibrary();
            using var intake = new SheetIntakeService(library);
            intake.WatchFolder(
                source.InboxFolder,
                source.Id,
                project.ProjectId,
                scanExisting: false);

            StudioPdfSourceRelinkIntakeResult result =
                StudioPdfSourceRelinkIntake.ImportAndRescan(
                    project,
                    source,
                    intake);

            Assert.Equal(2, result.Import.PageCount);
            Assert.Equal(2, library.VerifiedSnapshot().Count);
            Assert.All(
                library.VerifiedSnapshot(),
                sheet => Assert.Equal(source.Id, sheet.SourceId));
            Assert.Equal(0, result.Scan.ErrorCount);
            Assert.Equal(0, result.Scan.RejectedPackageCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static void WritePdf(string path, int pageCount)
    {
        using var document = new PdfDocument();
        for (int index = 0; index < pageCount; index++)
            document.AddPage();
        document.Save(path);
    }
}
