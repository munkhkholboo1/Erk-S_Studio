using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace ErkS.Platform.Core.Tests;

public sealed class CanonicalTitleBlockRestampTests : IDisposable
{
    private readonly string root = Path.Combine(
        Path.GetTempPath(),
        "erks-titleblock-restamp-" + Guid.NewGuid().ToString("N"));

    public CanonicalTitleBlockRestampTests()
    {
        WindowsFontResolver.Register();
        Directory.CreateDirectory(root);
    }

    [Fact]
    public void RestampUpdatesEveryContributorPageWithoutTouchingGeneratedCovers()
    {
        string input = Path.Combine(root, "canonical.pdf");
        string output = Path.Combine(root, "restamped.pdf");
        WriteA3Pdf(input, 6);
        AlbumProject project = Project("Canonical project", "Canonical company");
        IReadOnlyList<AlbumComponentPdfSlot> components =
        [
            new("generated:cover", 0, [1]),
            new("generated:table-of-contents", 10, [2]),
            new("generated:site-context", 20, [3]),
            new("source:owner-a:general-plan", 30, [4]),
            new(
                ProjectCloudSyncMetadata.BuildingSubCoverComponentCodePrefix + "building-a",
                40,
                [5]),
            new("source:owner-b:revit", 50, [6]),
        ];
        PdfVectorDocumentProfile before = PdfVectorQualityInspector.Inspect(input);

        AlbumTitleBlockRestampResult result =
            PdfSharpAlbumWriter.RestampCanonicalTitleBlocks(
                input,
                project,
                components,
                output);

        Assert.Equal(6, result.PageCount);
        Assert.Equal([3, 4, 6], result.RestampedPages);
        string signature = PdfSharpAlbumWriter.ComputeCanonicalTitleBlockSignature(project);
        Assert.True(PdfSharpAlbumWriter.HasCanonicalTitleBlockSignature(output, signature));
        Assert.False(PdfSharpAlbumWriter.HasCanonicalTitleBlockSignature(input, signature));

        PdfVectorDocumentProfile after = PdfVectorQualityInspector.Inspect(output);
        Assert.Equal(before.Pages.Count, after.Pages.Count);
        foreach (int untouchedPage in new[] { 1, 2, 5 })
        {
            Assert.Equal(
                before.Pages[untouchedPage - 1].ContentSha256,
                after.Pages[untouchedPage - 1].ContentSha256);
        }
        foreach (int restampedPage in result.RestampedPages)
        {
            Assert.NotEqual(
                before.Pages[restampedPage - 1].ContentSha256,
                after.Pages[restampedPage - 1].ContentSha256);
            Assert.Equal(
                before.Pages[restampedPage - 1].MediaBoxWidthMm,
                after.Pages[restampedPage - 1].MediaBoxWidthMm,
                3);
            Assert.Equal(
                before.Pages[restampedPage - 1].MediaBoxHeightMm,
                after.Pages[restampedPage - 1].MediaBoxHeightMm,
                3);
        }
    }

    [Fact]
    public void SignatureChangesWhenCanonicalProjectOrCompanyChanges()
    {
        AlbumProject original = Project("Project A", "Company A");
        AlbumProject renamedProject = Project("Project B", "Company A");
        AlbumProject renamedCompany = Project("Project A", "Company B");

        string originalSignature =
            PdfSharpAlbumWriter.ComputeCanonicalTitleBlockSignature(original);

        Assert.NotEqual(
            originalSignature,
            PdfSharpAlbumWriter.ComputeCanonicalTitleBlockSignature(renamedProject));
        Assert.NotEqual(
            originalSignature,
            PdfSharpAlbumWriter.ComputeCanonicalTitleBlockSignature(renamedCompany));
    }

    private AlbumProject Project(string projectName, string companyName) => new()
    {
        ProjectFolder = root,
        Name = projectName,
        ClientName = "Client",
        InitiationBasis = new ProjectInitiationBasis
        {
            ClientType = ProjectClientTypes.Organization,
            ClientName = "Client organization",
            ClientRepresentativeName = "Client representative",
        },
        Company = new CompanyProfile
        {
            Name = companyName,
            DisplayName = companyName,
            DesignRepresentativeTitle = "Director",
            DesignRepresentativeName = "Design representative",
        },
        Participants =
        [
            new ProjectParticipant
            {
                FullName = "Project architect",
                Role = "Architect",
            },
        ],
    };

    private static void WriteA3Pdf(string path, int pageCount)
    {
        using var document = new PdfDocument();
        for (int index = 0; index < pageCount; index++)
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(420);
            page.Height = XUnit.FromMillimeter(297);
            using XGraphics graphics = XGraphics.FromPdfPage(page);
            graphics.DrawLine(
                XPens.Black,
                XUnit.FromMillimeter(20).Point,
                XUnit.FromMillimeter(20 + index).Point,
                XUnit.FromMillimeter(400).Point,
                XUnit.FromMillimeter(20 + index).Point);
        }
        document.Save(path);
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}
