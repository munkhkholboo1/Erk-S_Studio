using System.Text.Json;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A general plan the project holds rather than one it points at.
///
/// It is kept the way a delivered page is kept, and for the same reason: a
/// board that merely remembered where a file used to be would break the moment
/// the drawing it came from was renamed, and the user would find out while
/// assembling a submission.
/// </summary>
public sealed class BoardPlanImportTests : IDisposable
{
    private readonly string workDirectory =
        Path.Combine(Path.GetTempPath(), "erks-platform-tests", Guid.NewGuid().ToString("N"));
    private readonly string projectPath;

    public BoardPlanImportTests()
    {
        Directory.CreateDirectory(workDirectory);
        projectPath = Path.Combine(workDirectory, "project.erksproj");
        File.WriteAllText(projectPath, "{}");
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
    public void APlanIsCopiedIntoTheProject()
    {
        var series = new ProjectBoardSeries();
        string source = WritePlan("plan.erks-citygen-board.json", "Erin_MP_ZZ_VL.dwg");

        BoardPlanImportResult result = BoardPlanImportService.Import(series, projectPath, source);

        Assert.True(result.Succeeded, string.Join("; ", result.Issues));
        Assert.True(result.Created);
        ProjectBoardPlanAsset asset = result.Asset!;
        Assert.Equal("Erin_MP_ZZ_VL", asset.Title);
        Assert.Equal(source, asset.SourcePath);
        Assert.True(File.Exists(
            ProjectWorkspacePaths.ResolveInsideProject(projectPath, asset.RelativePath)));
    }

    [Fact]
    public void TheCopySurvivesTheOriginalBeingTakenAway()
    {
        // The whole point. A renamed or moved drawing must not cost a board the
        // plan it shows.
        var series = new ProjectBoardSeries();
        string source = WritePlan("plan.erks-citygen-board.json", "plan.dwg");
        BoardPlanImportResult result = BoardPlanImportService.Import(series, projectPath, source);

        File.Delete(source);

        Assert.True(File.Exists(ProjectWorkspacePaths.ResolveInsideProject(
            projectPath,
            result.Asset!.RelativePath)));
    }

    [Fact]
    public void ImportingTheSameFileAgainChangesNothing()
    {
        var series = new ProjectBoardSeries();
        string source = WritePlan("plan.erks-citygen-board.json", "plan.dwg");
        BoardPlanImportResult first = BoardPlanImportService.Import(series, projectPath, source);

        BoardPlanImportResult again = BoardPlanImportService.Import(series, projectPath, source);

        Assert.True(again.Unchanged);
        Assert.Same(first.Asset, again.Asset);
        Assert.Single(series.Plans);
    }

    [Fact]
    public void ANewerExportRefreshesThePlanInPlace()
    {
        // Kept under the same identity on purpose: every card citing this plan
        // gets the new drawing without any of them being touched.
        var series = new ProjectBoardSeries();
        string source = WritePlan("plan.erks-citygen-board.json", "plan.dwg", objects: 1);
        BoardPlanImportResult first = BoardPlanImportService.Import(series, projectPath, source);
        string firstId = first.Asset!.Id;
        string firstPath = first.Asset.RelativePath;

        WritePlan("plan.erks-citygen-board.json", "plan.dwg", objects: 3);
        BoardPlanImportResult refreshed = BoardPlanImportService.Import(series, projectPath, source);

        Assert.True(refreshed.Refreshed);
        Assert.Equal(firstId, refreshed.Asset!.Id);
        Assert.NotEqual(firstPath, refreshed.Asset.RelativePath);
        Assert.Equal(3, refreshed.Asset.ObjectCount);
        Assert.Single(series.Plans);
    }

    [Fact]
    public void ACardKeepsPointingAtARefreshedPlan()
    {
        var series = new ProjectBoardSeries();
        string source = WritePlan("plan.erks-citygen-board.json", "plan.dwg", objects: 1);
        BoardPlanImportResult first = BoardPlanImportService.Import(series, projectPath, source);
        var card = new BoardElement { PlanAssetId = first.Asset!.Id };

        WritePlan("plan.erks-citygen-board.json", "plan.dwg", objects: 5);
        BoardPlanImportService.Import(series, projectPath, source);

        Assert.Equal(5, series.FindPlan(card)!.ObjectCount);
    }

    [Fact]
    public void AFileThatIsNotTheContractIsRefusedBeforeItIsCopied()
    {
        // A project should not end up holding something that fails every time a
        // board is built, long after the moment the reason was obvious.
        var series = new ProjectBoardSeries();
        string source = Path.Combine(workDirectory, "wrong.erks-citygen-board.json");
        File.WriteAllText(source, """{ "schema": "something.else", "schemaVersion": 1 }""");

        BoardPlanImportResult result = BoardPlanImportService.Import(series, projectPath, source);

        Assert.False(result.Succeeded);
        Assert.NotEmpty(result.Issues);
        Assert.Empty(series.Plans);
        Assert.False(Directory.Exists(Path.Combine(
            ProjectWorkspacePaths.GetProjectFolder(projectPath),
            "foundation",
            "documents",
            ProjectDocumentCategories.BoardPlan)));
    }

    [Fact]
    public void AMissingFileIsRefused()
    {
        var series = new ProjectBoardSeries();

        BoardPlanImportResult result = BoardPlanImportService.Import(
            series,
            projectPath,
            Path.Combine(workDirectory, "gone.erks-citygen-board.json"));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void TwoDifferentPlansBothLive()
    {
        var series = new ProjectBoardSeries();

        BoardPlanImportService.Import(
            series, projectPath, WritePlan("a.erks-citygen-board.json", "north.dwg"));
        BoardPlanImportService.Import(
            series, projectPath, WritePlan("b.erks-citygen-board.json", "south.dwg"));

        Assert.Equal(2, series.Plans.Count);
        Assert.Equal(2, series.Plans.Select(plan => plan.Id).Distinct().Count());
    }

    [Fact]
    public void TheStoreIsKeptToWhatTheBoardsStillCite()
    {
        var series = new ProjectBoardSeries();
        string source = WritePlan("plan.erks-citygen-board.json", "plan.dwg", objects: 1);
        BoardPlanImportService.Import(series, projectPath, source);
        WritePlan("plan.erks-citygen-board.json", "plan.dwg", objects: 4);
        BoardPlanImportService.Import(series, projectPath, source);

        // The refresh wrote a second file and left the first behind.
        int removed = BoardPlanStorageMaintenance.RemoveUnreferencedFiles(series, projectPath);

        Assert.Equal(1, removed);
        Assert.True(File.Exists(ProjectWorkspacePaths.ResolveInsideProject(
            projectPath,
            series.Plans[0].RelativePath)));
    }

    [Fact]
    public void APlanCardIsNoLongerAPlaceholder()
    {
        var card = new BoardElement { PlanAssetId = "abc" };

        card.Normalize();

        Assert.False(card.IsPlaceholder);
    }

    private string WritePlan(string name, string sourceDocument, int objects = 1)
    {
        var manifest = new CityGenBoardManifest
        {
            Schema = CityGenGraphicBoardContract.Schema,
            SchemaVersion = CityGenGraphicBoardContract.CurrentSchemaVersion,
            Units = CityGenGraphicBoardContract.ExpectedUnits,
            CoordinateSpace = CityGenGraphicBoardContract.ExpectedCoordinateSpace,
            NorthAngleSource = CityGenGraphicBoardContract.NorthAssumed,
            Origin = new CityGenBoardOrigin { IsDefined = true },
            Bbox = [0, 0, 100, 80],
            SourceDocument = sourceDocument,
            Objects = Enumerable.Range(0, objects).Select(index => new CityGenBoardObject
            {
                Id = "shape-" + index,
                Flow = "LAWN",
                Category = "PlannedGreenArea",
                Material = "grass",
                IsClosed = true,
                Vertices =
                [
                    new CityGenBoardVertex { X = index, Y = 0 },
                    new CityGenBoardVertex { X = index + 10, Y = 0 },
                    new CityGenBoardVertex { X = index + 10, Y = 10 },
                ],
            }).ToList(),
        };
        manifest.ObjectCount = manifest.Objects.Count;

        string path = Path.Combine(workDirectory, name);
        File.WriteAllText(
            path,
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return path;
    }
}
