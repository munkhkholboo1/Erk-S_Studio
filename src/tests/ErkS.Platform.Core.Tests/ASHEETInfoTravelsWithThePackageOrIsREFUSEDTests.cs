using ErkS.Platform.Contracts;
using ErkS.Platform.Pdf;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using System.Text.Json.Nodes;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// `erks-sheet-info` v1 - the general-plan sheets' information (legends with
/// quantities, the building schedule, the ТЭЗ summary, norm text) that CGA
/// writes beside its drawing and PFA packs with the sheets
/// (_shared/erks-sheet-info-contract-2026-10-19.md).
///
/// Every refusal here is named, never silent. The contract says so and the
/// reason is the one schema 7 learned: a reader that drops what it does not
/// know prints a legend with a row missing and nobody can tell.
/// </summary>
public sealed class ASHEETInfoTravelsWithThePackageOrIsREFUSEDTests : IDisposable
{
    private readonly string workDirectory = Path.Combine(
        Path.GetTempPath(),
        "erks-platform-tests",
        Guid.NewGuid().ToString("N"));

    public ASHEETInfoTravelsWithThePackageOrIsREFUSEDTests()
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

    // ---- the document --------------------------------------------------

    [Fact]
    public void CGAsPublishedSampleReadsWholly()
    {
        SheetInfoLoadResult result = SheetInfoReader.Load(
            SharedContractCopies.PathTo(SharedContractCopies.ErinSheetInfoSample));

        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        SheetInfoDocument info = result.Document!;
        Assert.Equal("Erin_MP_ZZ_VL.dwg", info.Document);
        Assert.Equal(116337.7, info.Site!.AreaM2);
        SheetInfoBuilding garages = Assert.Single(info.Buildings, building => building.No is null);
        Assert.Equal(14, garages.Count);
        Assert.Contains(info.Legends["master-plan"], row => row.Key == "building.planned");
        Assert.Contains(info.Legends["traffic-scheme"], row => row.Sample.Arrow == true);
        Assert.Contains(info.Summary, row => row.Key == "far" && row.Value == 2.72);
        Assert.Contains("23-04-07", info.Notes["solar-study"]);
        Assert.Null(Assert.Single(info.Basis, line => line.Key == "land").Text);
    }

    [Theory]
    [InlineData("$", "colour", "colour")]
    [InlineData("$.site", "elevation", "elevation")]
    [InlineData("$.legends.master-plan[0].sample", "hatchScale", "hatchScale")]
    public void AnUnknownMemberIsRefusedBYNAME(string where, string member, string named)
    {
        string path = WriteSample(document => Node(document, where)[member] = 1);

        SheetInfoLoadResult result = SheetInfoReader.Load(path);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains(named, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("stage", "Proposed", "stage")]
    [InlineData("kind", "pattern", "kind")]
    [InlineData("color", "red", "colour")]
    public void AnUnknownValueIsRefusedBYNAME(string field, string value, string named)
    {
        string path = WriteSample(document =>
        {
            JsonObject row = document["legends"]!["master-plan"]![0]!.AsObject();
            if (field == "stage")
                row["stage"] = value;
            else
                row["sample"]![field] = value;
        });

        SheetInfoLoadResult result = SheetInfoReader.Load(path);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains(named, StringComparison.OrdinalIgnoreCase) &&
                                                issue.Contains(value, StringComparison.Ordinal));
    }

    [Fact]
    public void ALegendForASheetThatDoesNotExistIsRefused()
    {
        string path = WriteSample(document => document["legends"]!["existing-condition"] = new JsonArray());

        SheetInfoLoadResult result = SheetInfoReader.Load(path);

        Assert.Contains(result.Issues, issue => issue.Contains("existing-condition", StringComparison.Ordinal) &&
                                                issue.Contains("legend", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ANoteForASheetThatDoesNotExistIsRefused()
    {
        string path = WriteSample(document => document["notes"]!["cover"] = "x");

        SheetInfoLoadResult result = SheetInfoReader.Load(path);

        Assert.Contains(result.Issues, issue => issue.Contains("cover", StringComparison.Ordinal) &&
                                                issue.Contains("note", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ABuildingCountedLessThanOnceIsRefused()
    {
        string path = WriteSample(document => document["buildings"]![0]!["count"] = 0);

        SheetInfoLoadResult result = SheetInfoReader.Load(path);

        Assert.Contains(result.Issues, issue => issue.Contains("count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AnotherSchemaOrANewerVersionIsRefused()
    {
        string other = WriteSample(document => document["schema"] = "erks-sheets", "other");
        string newer = WriteSample(document => document["schemaVersion"] = 2, "newer");

        Assert.Contains(SheetInfoReader.Load(other).Issues, issue => issue.Contains("erks-sheets", StringComparison.Ordinal));
        Assert.Contains(SheetInfoReader.Load(newer).Issues, issue => issue.Contains("version 2", StringComparison.Ordinal));
    }

    // ---- the package ---------------------------------------------------

    [Fact]
    public void TheInfoTravelsAndVerifiesAtSchema8()
    {
        string manifestPath = WritePackage("ok");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.True(result.IsLossless, string.Join("; ", result.Issues));
        Assert.Equal("Erin_MP_ZZ_VL.dwg", result.SheetInfo!.Document);
    }

    [Fact]
    public void InfoBelowSchema8IsRefused()
    {
        string manifestPath = WritePackage("old");
        RewriteManifest(manifestPath, manifest => manifest["schemaVersion"] = 7);

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.Null(result.SheetInfo);
        Assert.Contains(result.Issues, issue => issue.Contains("sheet info", StringComparison.OrdinalIgnoreCase) &&
                                                issue.Contains("8", StringComparison.Ordinal));
    }

    [Fact]
    public void InfoWhoseBytesChangedIsRefused()
    {
        string manifestPath = WritePackage("changed");
        string infoPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "Erin_MP_ZZ_VL.erks-sheet-info.json");
        File.AppendAllText(infoPath, " ");

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.Null(result.SheetInfo);
        Assert.Contains(result.Issues, issue => issue.Contains("sheet info", StringComparison.OrdinalIgnoreCase) &&
                                                issue.Contains("hash", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InfoThatIsMissingIsRefused()
    {
        string manifestPath = WritePackage("missing");
        File.Delete(Path.Combine(Path.GetDirectoryName(manifestPath)!, "Erin_MP_ZZ_VL.erks-sheet-info.json"));

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.Contains(result.Issues, issue => issue.Contains("sheet info", StringComparison.OrdinalIgnoreCase) &&
                                                issue.Contains("missing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void InfoThatFailsItsOwnRulesFailsThePackage()
    {
        // The writer refuses such a package itself, so the bad document is put
        // in afterwards, with its hash, as a producer bypassing the writer would.
        string manifestPath = WritePackage("invalid");
        string infoPath = Path.Combine(Path.GetDirectoryName(manifestPath)!, "Erin_MP_ZZ_VL.erks-sheet-info.json");
        JsonObject info = JsonNode.Parse(File.ReadAllText(infoPath))!.AsObject();
        info["buildings"]![0]!["count"] = 0;
        File.WriteAllText(infoPath, info.ToJsonString());
        RewriteManifest(manifestPath, manifest => manifest["sheetInfo"]!["sha256"] =
            SheetPackageReader.ComputeSha256(infoPath));

        SheetPackageLoadResult result = SheetPackageReader.Load(manifestPath);

        Assert.False(result.IsLossless);
        Assert.Contains(result.Issues, issue => issue.Contains("count", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void APackageWithoutInfoWritesNothingNew()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new SheetPackageManifest(), SheetPackageJson.Options);

        Assert.DoesNotContain("sheetInfo", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- helpers -------------------------------------------------------

    private string WriteSample(Action<JsonObject> change, string name = "sample")
    {
        JsonObject document = JsonNode.Parse(
            SharedContractCopies.Read(SharedContractCopies.ErinSheetInfoSample))!.AsObject();
        change(document);
        string path = Path.Combine(workDirectory, name + ".erks-sheet-info.json");
        File.WriteAllText(path, document.ToJsonString());
        return path;
    }

    private static JsonObject Node(JsonObject document, string where)
    {
        JsonNode node = document;
        foreach (string part in where.TrimStart('$').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            int bracket = part.IndexOf('[');
            node = bracket < 0
                ? node[part]!
                : node[part[..bracket]]![int.Parse(part[(bracket + 1)..^1])]!;
        }
        return node.AsObject();
    }

    private string WritePackage(string folderName, Action<JsonObject>? changeInfo = null)
    {
        string directory = Path.Combine(workDirectory, folderName);
        Directory.CreateDirectory(directory);
        const string fileName = "ET-08.pdf";
        using (var document = new PdfDocument())
        {
            PdfPage page = document.AddPage();
            page.Width = XUnit.FromMillimeter(594);
            page.Height = XUnit.FromMillimeter(420);
            using (XGraphics gfx = XGraphics.FromPdfPage(page))
                gfx.DrawString("ЕТ", new XFont("Arial", 12), XBrushes.Black, new XPoint(40, 40));
            document.Save(Path.Combine(directory, fileName));
        }

        JsonObject info = JsonNode.Parse(
            SharedContractCopies.Read(SharedContractCopies.ErinSheetInfoSample))!.AsObject();
        changeInfo?.Invoke(info);
        const string infoName = "Erin_MP_ZZ_VL.erks-sheet-info.json";
        string infoPath = Path.Combine(directory, infoName);
        File.WriteAllText(infoPath, info.ToJsonString());

        var manifest = new SheetPackageManifest
        {
            SchemaVersion = SheetPackageManifest.FirstSheetInfoSchemaVersion,
            Source = new SheetPackageSource
            {
                SourceId = "erin-cga",
                Application = SheetSourceApplication.AutoCad,
                DocumentPath = @"D:\Cloud Work\Nanuto LLC\Erin Apartment\ZZ\DWG\Erin_MP_ZZ_VL.dwg",
            },
            SheetInfo = new SheetInfoReference
            {
                File = infoName,
                Sha256 = SheetPackageReader.ComputeSha256(infoPath),
                SchemaVersion = 1,
            },
            Sheets =
            {
                new SheetPackageEntry
                {
                    SheetId = "et-08",
                    Number = "08",
                    TemplateSlotId = "master-plan",
                    WidthMm = 594,
                    HeightMm = 420,
                    PdfFileName = fileName,
                    PdfPageNumber = 1,
                },
            },
        };
        return SheetPackageWriter.Write(manifest, directory, "erin");
    }

    private static void RewriteManifest(string manifestPath, Action<JsonObject> change)
    {
        JsonObject manifest = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        change(manifest);
        File.WriteAllText(manifestPath, manifest.ToJsonString(SheetPackageJson.Options));
    }
}
