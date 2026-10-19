using ErkS.Platform.Contracts;
using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Text.Json.Nodes;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The names in a working sheet's corner table - Архитектор, Гүйцэтгэсэн,
/// Шалгасан - as the drawing's author wrote them.
///
/// 🔴 THEY HAD NO WAY IN, AND THE GAP WAS SILENT. Studio already draws the
/// corner table over a Blueprint sheet's reserved zone, and already has the
/// three roles (<see cref="AlbumPageRoleCodes"/>). What it did not have was the
/// names Revit holds: a sheet's role cells were filled from an assignment made
/// inside Studio, or else from the company's standing signers. A project
/// migrated from Revit has no Studio assignments yet, so every sheet showed the
/// company's names in place of the people who actually drew and checked it -
/// a table that looks complete and is wrong. Measured on the Batnasan project
/// by PFR on 2026-10-19 (Э.Мөнххолбоо, М.Будбаясгалан, М.Насанжаргал).
///
/// The order this file holds: what the owner assigned in Studio, then what the
/// sheet itself says, then the company default. The owner's choice is never
/// overwritten by a re-export.
/// </summary>
public sealed class ASHEETCarriesItsAuthorsIntoStudioTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-platform-tests",
        Guid.NewGuid().ToString("N"));

    public ASHEETCarriesItsAuthorsIntoStudioTests()
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
    public void RolesTravelInOrderAndVerifyLosslessly()
    {
        string manifestPath = WritePackage("roles");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        SheetPackageEntry entry = Assert.Single(result.Manifest!.Sheets);
        Assert.Equal(
            [
                (SheetRoleCodes.Architect, "Э.Мөнххолбоо"),
                (SheetRoleCodes.PreparedBy, "М.Будбаясгалан"),
                (SheetRoleCodes.CheckedBy, "М.Насанжаргал"),
            ],
            entry.Roles.Select(role => (role.RoleCode, role.FullName)));
    }

    [Fact]
    public void RolesAreRefusedBelowTheSchemaThatIntroducedThem()
    {
        // An older Studio drops a JSON field it does not know, without a word -
        // the names would vanish and the corner table would quietly show the
        // company's signers. Requiring the new schema turns that into a refusal
        // the producer sees.
        string manifestPath = WritePackage("old-schema");
        RewriteManifest(manifestPath, manifest => manifest["schemaVersion"] = 6);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains(
                    SheetPackageManifest.FirstSheetRolesSchemaVersion.ToString(),
                    StringComparison.Ordinal) &&
                issue.Contains("role", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnUnknownRoleCodeIsRefusedAndNamed()
    {
        string manifestPath = WritePackage("unknown-role");
        RewriteFirstRole(manifestPath, role => role["roleCode"] = "Director");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains("Director", StringComparison.Ordinal) &&
                issue.Contains("unsupported", StringComparison.Ordinal));
    }

    [Fact]
    public void ARoleNamedTwiceOnOneSheetIsRefused()
    {
        // Two architects on one sheet would leave the table to pick one - and
        // picking silently is the same loss as dropping.
        string manifestPath = WritePackage("duplicate-role");
        RewriteManifest(manifestPath, manifest =>
        {
            JsonArray roles = manifest["sheets"]!.AsArray()[0]!["roles"]!.AsArray();
            roles[1]!["roleCode"] = SheetRoleCodes.Architect;
        });

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains(SheetRoleCodes.Architect, StringComparison.Ordinal) &&
                issue.Contains("twice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ARoleWithNoNameIsRefused()
    {
        string manifestPath = WritePackage("blank-name");
        RewriteFirstRole(manifestPath, role => role["fullName"] = "  ");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(
            result.Issues,
            issue => issue.Contains("name", StringComparison.OrdinalIgnoreCase) &&
                issue.Contains(SheetRoleCodes.Architect, StringComparison.Ordinal));
    }

    [Fact]
    public void TheWireRoleCodesAREStudiosPageRoleCodes()
    {
        // One vocabulary, two places it is spelled. If the album's role codes
        // and the package's ever drifted, a sheet's Architect would match no
        // cell and fall through to the company default - silently.
        Assert.Equal(SheetRoleCodes.All, AlbumPageRoleCodes.All);
    }

    [Fact]
    public void WithNoStudioAssignmentTheSHEETSNamesBeatTheCompanyDefault()
    {
        AlbumProject project = ProjectWithCompanySigners();

        IReadOnlyList<string> names =
            PdfSharpAlbumWriter.ResolveCanonicalHorizontalWorkingTitleBlockNames(
                project,
                roleAssignments: null,
                sourceRoles: RevitRoles());

        Assert.Equal(
            ["Э.Мөнххолбоо", "М.Будбаясгалан", "М.Насанжаргал"],
            names);
    }

    [Fact]
    public void AnAssignmentMadeInSTUDIOBeatsWhatTheSheetSays()
    {
        // The owner's choice is authoritative: re-exporting from Revit must not
        // undo a name the owner set on this page.
        AlbumProject project = ProjectWithCompanySigners();
        AlbumPageRoleAssignment[] assignments =
        [
            new()
            {
                RoleCode = AlbumPageRoleCodes.CheckedBy,
                FullName = "Б.Эзнийсонголт",
            },
        ];

        IReadOnlyList<string> names =
            PdfSharpAlbumWriter.ResolveCanonicalHorizontalWorkingTitleBlockNames(
                project,
                assignments,
                RevitRoles());

        Assert.Equal("Б.Эзнийсонголт", names[2]);
        // The other two still come from the sheet.
        Assert.Equal("М.Будбаясгалан", names[1]);
    }

    [Fact]
    public void WithNothingFromTheSheetTheCOMPANYDefaultStillFillsTheTable()
    {
        // Positive control for the old behaviour: a sheet that carries no roles
        // - every package written before this field existed - is unchanged.
        AlbumProject project = ProjectWithCompanySigners();

        IReadOnlyList<string> names =
            PdfSharpAlbumWriter.ResolveCanonicalHorizontalWorkingTitleBlockNames(
                project,
                roleAssignments: null,
                sourceRoles: []);

        Assert.Equal("С.Компанийнгүйцэтгэгч", names[1]);
        Assert.Equal("Д.Компанийншалгагч", names[2]);
    }

    [Fact]
    public void ThePublishedExampleIsWHATTHISWRITERProduces()
    {
        // PFR writes these manifests in another repository. The example it
        // matches against is regenerated here so that it is Studio's output, not
        // a description of it. The package id is new per export and the payload
        // hash belongs to the sample PDF, so both are blanked on each side.
        string manifestPath = WritePackage("published-example");
        string produced = Blank(File.ReadAllText(manifestPath));
        string published = Blank(
            SharedContractCopies.Read(SharedContractCopies.SheetRolesManifest));

        Assert.Equal(published, produced, ignoreLineEndingDifferences: true);
    }

    private static string Blank(string manifestJson)
    {
        JsonObject manifest = JsonNode.Parse(manifestJson)!.AsObject();
        manifest["packageId"] = "00000000-0000-0000-0000-000000000000";
        manifest["exportedAtUtc"] = "2026-10-19T00:00:00+00:00";
        foreach (JsonNode? sheet in manifest["sheets"]!.AsArray())
        {
            sheet!.AsObject()["sha256"] = "";
        }
        return manifest.ToJsonString(SheetPackageJson.Options);
    }

    private static IReadOnlyList<SheetRoleSignature> RevitRoles() =>
    [
        new() { RoleCode = SheetRoleCodes.Architect, FullName = "Э.Мөнххолбоо" },
        new() { RoleCode = SheetRoleCodes.PreparedBy, FullName = "М.Будбаясгалан" },
        new() { RoleCode = SheetRoleCodes.CheckedBy, FullName = "М.Насанжаргал" },
    ];

    private static AlbumProject ProjectWithCompanySigners() => new()
    {
        Company = new CompanyProfile
        {
            Signers =
            [
                new CompanySigner { Role = "Гүйцэтгэсэн", FullName = "С.Компанийнгүйцэтгэгч" },
                new CompanySigner { Role = "Шалгасан", FullName = "Д.Компанийншалгагч" },
            ],
        },
    };

    private string WritePackage(string folderName)
    {
        string directory = Path.Combine(workDirectory, folderName);
        Directory.CreateDirectory(directory);
        const string fileName = "BA-11.pdf";
        WriteMinimalPdf(Path.Combine(directory, fileName));

        var manifest = new SheetPackageManifest
        {
            SchemaVersion = SheetPackageManifest.FirstSheetRolesSchemaVersion,
            Source = new SheetPackageSource
            {
                SourceId = "batnasan-revit",
                Application = SheetSourceApplication.Revit,
                DocumentPath = @"D:\Cloud Work\Batnasan Apartment\RVT\Arch\Arch_BN_SHEET V2.rvt",
                DocumentTitle = "Arch_BN_SHEET V2",
            },
            Sheets =
            {
                new SheetPackageEntry
                {
                    SheetId = "ba-11",
                    Number = "БА-11",
                    Name = "1-р давхрын төлөвлөлт",
                    WidthMm = 420,
                    HeightMm = 297,
                    PdfFileName = fileName,
                    PdfPageNumber = 1,
                    Roles = [.. RevitRoles()],
                },
            },
        };

        return SheetPackageWriter.Write(manifest, directory, "batnasan");
    }

    private static void WriteMinimalPdf(string path)
    {
        using var document = new PdfDocument();
        PdfPage page = document.AddPage();
        page.Width = XUnit.FromMillimeter(420);
        page.Height = XUnit.FromMillimeter(297);
        using (XGraphics gfx = XGraphics.FromPdfPage(page))
        {
            gfx.DrawString("БА-11", new XFont("Arial", 12), XBrushes.Black, new XPoint(40, 40));
        }
        document.Save(path);
    }

    private static void RewriteFirstRole(string manifestPath, Action<JsonObject> change) =>
        RewriteManifest(
            manifestPath,
            manifest => change(manifest["sheets"]!.AsArray()[0]!["roles"]!.AsArray()[0]!.AsObject()));

    private static void RewriteManifest(string manifestPath, Action<JsonObject> change)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        change(manifest);
        File.WriteAllText(manifestPath, manifest.ToJsonString(SheetPackageJson.Options));
    }
}
