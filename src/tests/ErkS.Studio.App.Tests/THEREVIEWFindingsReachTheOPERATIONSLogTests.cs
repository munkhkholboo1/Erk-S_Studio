using System.Text;
using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Every album build writes one «album_review» row to studio-operations.jsonl
/// so the Twin can raise the review findings as STU issues (Мастер,
/// 2026-10-20): outcome warning/completed, reasonCode «magadlal:1,4,4,6», the
/// count as the message, the findings themselves in the safe field
/// «reviewFindings».
/// </summary>
public sealed class THEREVIEWFindingsReachTheOPERATIONSLogTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "erks-review-log-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
        }
    }

    [Fact]
    public void FindingsBecomeAWarningRowWithTheirItemNumbers()
    {
        StudioAlbumReviewLogRow row = StudioAlbumReviewLogRow.From(
        [
            "Магадлал 1: ЕТ-ийн иж бүрдэлд дутуу хуудас — Картограмм / босоо төлөвлөлт.",
            "Масштаб зөрсөн: 03 …",
            "Магадлал 4: ТЭЗҮ-д К1 утга алга.",
            "Магадлал 4: ТЭЗҮ-д К2 утга алга.",
            "Магадлал 6: «ЗӨВШӨӨРӨЛЦСӨН» блокийн хүрээ байгаа ч хэсгийн инженерүүд бичигдээгүй.",
        ]);

        Assert.Equal("album_review", StudioAlbumReviewLogRow.Operation);
        Assert.Equal("warning", row.Outcome);
        Assert.Equal("magadlal:1,4,4,6", row.ReasonCode);
        Assert.Contains("4", row.Message, StringComparison.Ordinal);
        Assert.Equal(4, row.Details["reviewFindings"].Length);
    }

    [Fact]
    public void NoFindingsIsACompletedRow()
    {
        StudioAlbumReviewLogRow row = StudioAlbumReviewLogRow.From(["Масштаб зөрсөн: 03 …"]);

        Assert.Equal("completed", row.Outcome);
        Assert.Equal("magadlal:", row.ReasonCode);
        Assert.Empty(row.Details["reviewFindings"]);
    }

    [Fact]
    public void TheLogKeepsTheFindingsInTheSafeField()
    {
        var log = new StudioOperationDiagnosticLog(root);
        StudioAlbumReviewLogRow row = StudioAlbumReviewLogRow.From(
            Enumerable.Range(1, 12).Select(index => $"Магадлал 4: олдвол {index}.").ToList());

        Assert.True(log.TryWrite(new StudioOperationDiagnosticEvent(
            "op-1",
            StudioAlbumReviewLogRow.Operation,
            row.Outcome,
            row.ReasonCode,
            row.Message,
            "project-1",
            "",
            "",
            "",
            row.Details)));

        string line = File.ReadAllLines(log.LogPath, Encoding.UTF8).Single();
        using JsonDocument document = JsonDocument.Parse(line);
        JsonElement details = document.RootElement.GetProperty("details");
        Assert.Equal(12, details.GetProperty("reviewFindings").GetArrayLength());
        Assert.Equal("magadlal:4,4,4,4,4,4,4,4,4,4,4,4", document.RootElement.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public void EveryBuildThroughTheViewWritesTheRow()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string view = File.ReadAllText(Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App", "ShellView.cs"));

        int warnings = view.IndexOf("lastAlbumBuildWarnings = result.Warnings.ToList();", StringComparison.Ordinal);
        int row = view.IndexOf("RecordAlbumReview(result.Warnings);", StringComparison.Ordinal);
        Assert.True(warnings > 0 && row > warnings);
    }
}
