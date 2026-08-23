using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A delivery that arrives while the project is closed waits in the inbox. The
/// project has to be able to say so, or it looks exactly like a drawing that
/// was never sent.
/// </summary>
public sealed class SourceInboxScannerTests : IDisposable
{
    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));

    public SourceInboxScannerTests()
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
    public void NothingRecordedYet_CountsEveryDelivery()
    {
        var first = DateTimeOffset.UtcNow.AddHours(-2);
        WriteDelivery("d1", first);
        WriteDelivery("d2", first.AddHours(1));

        PendingSourcePackageSurvey survey = SourceInboxScanner.Survey(workDirectory, "", null);

        Assert.True(survey.Any);
        Assert.Equal(2, survey.Count);
        Assert.Equal(first.AddHours(1), survey.NewestExportedAtUtc);
    }

    [Fact]
    public void TheDeliveryAlreadyTakenIn_IsNotWaiting()
    {
        var exportedAt = DateTimeOffset.UtcNow.AddHours(-1);
        Guid packageId = WriteDelivery("d1", exportedAt);

        PendingSourcePackageSurvey survey = SourceInboxScanner.Survey(
            workDirectory,
            packageId.ToString("N"),
            exportedAt);

        Assert.False(survey.Any);
        Assert.Null(survey.NewestExportedAtUtc);
    }

    [Fact]
    public void ADeliveryOlderThanTheOneHeld_IsNotWaiting()
    {
        // A superseded export left in the folder is history, not a backlog.
        var held = DateTimeOffset.UtcNow.AddHours(-1);
        WriteDelivery("older", held.AddHours(-1));
        Guid current = WriteDelivery("current", held);

        PendingSourcePackageSurvey survey = SourceInboxScanner.Survey(
            workDirectory,
            current.ToString("N"),
            held);

        Assert.False(survey.Any);
    }

    [Fact]
    public void ANewerDelivery_IsWaitingEvenBesideOlderOnes()
    {
        var held = DateTimeOffset.UtcNow.AddHours(-2);
        Guid current = WriteDelivery("current", held);
        WriteDelivery("older", held.AddHours(-1));
        var newerAt = held.AddHours(1);
        WriteDelivery("newer", newerAt);

        PendingSourcePackageSurvey survey = SourceInboxScanner.Survey(
            workDirectory,
            current.ToString("N"),
            held);

        Assert.Equal(1, survey.Count);
        Assert.Equal(newerAt, survey.NewestExportedAtUtc);
    }

    [Fact]
    public void AnUnreadableManifest_IsLeftToIntake()
    {
        // Intake rejects and quarantines bad input with a reason; guessing here
        // would only produce a second, vaguer complaint.
        Directory.CreateDirectory(Path.Combine(workDirectory, "broken"));
        File.WriteAllText(
            Path.Combine(workDirectory, "broken", "x" + SheetPackageManifest.ManifestSuffix),
            "{ this is not json");

        PendingSourcePackageSurvey survey = SourceInboxScanner.Survey(workDirectory, "", null);

        Assert.False(survey.Any);
    }

    [Fact]
    public void AMissingFolder_IsNotAnError()
    {
        Assert.False(SourceInboxScanner
            .Survey(Path.Combine(workDirectory, "never-created"), "", null)
            .Any);
        Assert.False(SourceInboxScanner.Survey("", "", null).Any);
    }

    private Guid WriteDelivery(string folderName, DateTimeOffset exportedAtUtc)
    {
        string directory = Path.Combine(workDirectory, folderName);
        Directory.CreateDirectory(directory);
        const string fileName = "sheet.pdf";
        WriteMinimalPdf(Path.Combine(directory, fileName));
        var manifest = new SheetPackageManifest
        {
            Source = new SheetPackageSource
            {
                SourceId = "source-1",
                Application = SheetSourceApplication.AutoCad,
                DocumentPath = @"C:\sample\test.dwg",
                DocumentTitle = "test",
            },
            PackageScope = SheetPackageScope.FullSnapshot,
            ExportedAtUtc = exportedAtUtc,
        };
        manifest.Sheets.Add(new SheetPackageEntry
        {
            SheetId = folderName,
            Number = "00",
            Name = folderName,
            WidthMm = 420,
            HeightMm = 297,
            PdfFileName = fileName,
        });
        SheetPackageWriter.Write(manifest, directory, folderName);
        return manifest.PackageId;
    }

    private static void WriteMinimalPdf(string path)
    {
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(420);
        page.Height = XUnit.FromMillimeter(297);
        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawRectangle(new XPen(XColors.Black, 1), 10, 10, 100, 100);
        }
        document.Save(path);
    }
}
