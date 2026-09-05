using ErkS.Platform.Core;
using ErkS.Platform.Pdf;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Renders the 2026 concept cover to a file so it can be measured back the same
/// way the original DWG was measured.
///
/// Every other test of this sheet reads SOURCE - what the writer is written to
/// draw. That catches a rule that is never called and misses a rule that is
/// called with the wrong number. The only way to know the decisions actually
/// reached the page is to take the page apart again.
/// </summary>
public sealed class ConceptCover2026RenderProbe
{
    [Fact]
    public void RenderTheSheetForMeasurement()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "erks-concept-cover-2026-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string probePath = Environment.GetEnvironmentVariable("ERKS_CONCEPT_COVER_PROBE");

        try
        {
            var project = new AlbumProject
            {
                Name = "ХЭМЖИЛТИЙН ТӨСӨЛ",
                ProjectFolder = directory,
                ConceptCoverStyle = AlbumConceptCoverStyles.Sheet2026,
                Company = new CompanyProfile
                {
                    Name = "Зураг төслийн байгууллага",
                    RegisteredCity = "Улаанбаатар хот",
                    DirectorTitle = "Захирал",
                    DirectorName = "Д.Тулга",
                },
                InitiationBasis = new ProjectInitiationBasis
                {
                    SiteAddress = "Улаанбаатар хот, Баянгол дүүрэг, 29-р хороо",
                    ClientType = ProjectClientTypes.Citizen,
                    ClientName = "Ч.Эрдэнэтунгалаг",
                },
                Album = BuildingArchitectureConceptAlbumTemplate.CreateDefinition("Загвар"),
            };
            project.ApprovalWorkflow.ConceptDesign.IsConfigured = true;
            project.ApprovalWorkflow.ConceptDesign.ApprovedBy.Add(new ProjectApprovalEntry
            {
                OrganizationName = "Нийслэлийн Ерөнхий архитектор",
                PositionTitle = "Ерөнхий архитектор",
                PersonName = "Б.Батцолмон",
            });
            foreach (string body in new[]
                     {
                         "Нийслэлийн онцгой байдлын газар",
                         "Нийслэлийн эрүүл мэндийн газар",
                         "Онцгой байдлын ерөнхий газар",
                     })
            {
                project.ApprovalWorkflow.ConceptDesign.ConcurredBy.Add(new ProjectApprovalEntry
                {
                    OrganizationName = body,
                    PositionTitle = "Дарга",
                    PersonName = "Тодорхойлогдоогүй",
                });
            }

            string outputPath = Path.Combine(directory, "concept-cover-2026.pdf");
            new AlbumBuilder(new PdfSharpAlbumWriter()).Build(project, new SheetLibrary(), outputPath);

            Assert.True(File.Exists(outputPath));
            if (!string.IsNullOrWhiteSpace(probePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(probePath)!);
                File.Copy(outputPath, probePath, overwrite: true);
            }
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
