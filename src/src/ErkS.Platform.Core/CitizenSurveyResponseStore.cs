using System.Text;
using System.Text.Json;

namespace ErkS.Platform.Core;

/// <summary>The answers of one survey, as they sit on disk.</summary>
public sealed class CitizenSurveyResponseDocument
{
    public string SurveyId { get; set; } = "";

    /// <summary>The watermark the next collection continues from.</summary>
    public string Cursor { get; set; } = "";

    public DateTimeOffset? CollectedAtUtc { get; set; }

    public List<CitizenSurveyResponse> Responses { get; set; } = [];
}

/// <summary>
/// Reads and writes a survey's answers.
///
/// 🔴 THEIR OWN FILE, BESIDE THE PROJECT, FOR THE SAME REASON THE ALBUM HAS ONE. A
/// consultation is thousands of rows; held inside the project file every ordinary save
/// would rewrite them, and the project file is opened on every screen in Studio.
///
/// 🔴 AND A MERGE THAT NEVER LOSES ONE. Collection is incremental - the server is asked
/// for what is new since a cursor - so the same response can arrive twice and must be
/// counted once. Merging BY THE RESPONSE ID rather than appending is what makes a repeated
/// page harmless; appending would inflate every number on the results page and there
/// would be nothing on screen to reveal it.
///
/// ⚠ AND IT ONLY EVER ADDS. A citizen's answer is not ours to remove - the owner's own
/// rule for their material, and here it binds harder, because the person has gone home
/// and cannot be asked again.
/// </summary>
public static class CitizenSurveyResponseStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
    };

    public static IReadOnlyList<CitizenSurveyResponse> Load(
        string? projectPath,
        ProjectCitizenSurvey? survey)
    {
        return LoadDocument(projectPath, survey).Responses;
    }

    public static CitizenSurveyResponseDocument LoadDocument(
        string? projectPath,
        ProjectCitizenSurvey? survey)
    {
        string? path = ResolvePath(projectPath, survey);
        if (path is null || !File.Exists(path))
            return new CitizenSurveyResponseDocument { SurveyId = survey?.Id ?? "" };

        try
        {
            CitizenSurveyResponseDocument? document =
                JsonSerializer.Deserialize<CitizenSurveyResponseDocument>(
                    File.ReadAllText(path, Encoding.UTF8), Options);
            if (document is null)
                return new CitizenSurveyResponseDocument { SurveyId = survey?.Id ?? "" };

            document.Responses ??= [];
            return document;
        }
        catch (Exception exception) when (
            exception is JsonException or IOException or UnauthorizedAccessException)
        {
            // ⚠ AN UNREADABLE FILE IS NOT AN EMPTY ONE, AND THE DIFFERENCE MATTERS HERE.
            // Returning nothing would show «0 оролцсон хүн» over answers that are still on
            // disk, and somebody could reasonably conclude the consultation failed. The
            // throw is left to the caller, which has a status line to say so on.
            throw new InvalidDataException(
                $"Санал асуулгын хариуг уншиж чадсангүй: {path} — {exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// Adds what arrived, keeping every answer already held.
    ///
    /// Returns how many were genuinely new, so the caller can say so rather than claiming
    /// a number it did not check.
    /// </summary>
    public static int Merge(
        CitizenSurveyResponseDocument document,
        IEnumerable<CitizenSurveyResponse>? arriving,
        string? cursor)
    {
        ArgumentNullException.ThrowIfNull(document);
        document.Responses ??= [];

        var known = new HashSet<string>(
            document.Responses.Select(response => response.Id),
            StringComparer.OrdinalIgnoreCase);

        var added = 0;
        foreach (CitizenSurveyResponse response in arriving ?? [])
        {
            if (response is null || string.IsNullOrWhiteSpace(response.Id))
                continue;
            if (!known.Add(response.Id))
                continue;
            document.Responses.Add(response);
            added++;
        }

        if (!string.IsNullOrWhiteSpace(cursor))
            document.Cursor = cursor.Trim();
        document.CollectedAtUtc = DateTimeOffset.UtcNow;
        return added;
    }

    public static void Save(
        string? projectPath,
        ProjectCitizenSurvey? survey,
        CitizenSurveyResponseDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        string? path = ResolvePath(projectPath, survey);
        if (path is null)
            return;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(document, Options), Encoding.UTF8);
    }

    /// <summary>What a clear-out moved, and where it put it.</summary>
    /// <param name="ArchivePath">Empty when there was nothing to move.</param>
    public sealed record CitizenSurveyClearOutcome(int Removed, string ArchivePath);

    /// <summary>
    /// Sets the collected answers aside so a trial run does not become part of a
    /// consultation's result.
    ///
    /// 🔴 IT MOVES, IT DOES NOT DELETE, AND THAT IS NOT TIMIDITY. Everything else in
    /// this feature can be recomputed; what a person submitted cannot. The owner asked for
    /// a way to clear test answers before going live, which is a real need - but the same
    /// button will sit there when the answers are real, and one mis-click would end a
    /// consultation with nothing to show for it. A rename costs a file on disk and makes
    /// the worst outcome recoverable.
    ///
    /// ⚠ THE CURSOR SURVIVES ON PURPOSE. It is the watermark of what has already been
    /// collected, so keeping it means cleared answers do NOT come back on the next fetch -
    /// which is exactly what clearing a trial is for. Resetting it would quietly refill
    /// the survey with the very responses somebody just removed.
    ///
    /// ⚠ AND THE SERVER STILL HAS ITS OWN COPY. This clears what the project holds;
    /// re-importing the same file puts them straight back. The caller says so - a clear
    /// that looked total but was not would be worse than no clear at all.
    /// </summary>
    public static CitizenSurveyClearOutcome Clear(
        string? projectPath,
        ProjectCitizenSurvey? survey,
        DateTimeOffset stampUtc)
    {
        string? path = ResolvePath(projectPath, survey);
        if (path is null || !File.Exists(path))
            return new CitizenSurveyClearOutcome(0, "");

        CitizenSurveyResponseDocument held = LoadDocument(projectPath, survey);
        if (held.Responses.Count == 0)
            return new CitizenSurveyClearOutcome(0, "");

        string archive = Path.Combine(
            Path.GetDirectoryName(path)!,
            Path.GetFileNameWithoutExtension(path) +
            stampUtc.ToLocalTime().ToString(".yyyy-MM-dd-HHmmss") + ".cleared.json");

        File.Move(path, archive, overwrite: false);

        Save(projectPath, survey, new CitizenSurveyResponseDocument
        {
            SurveyId = held.SurveyId,
            Cursor = held.Cursor,
            CollectedAtUtc = held.CollectedAtUtc,
        });

        return new CitizenSurveyClearOutcome(held.Responses.Count, archive);
    }

    private static string? ResolvePath(string? projectPath, ProjectCitizenSurvey? survey)
    {
        if (survey is null ||
            string.IsNullOrWhiteSpace(projectPath) ||
            string.IsNullOrWhiteSpace(survey.ResponsesRelativePath))
        {
            return null;
        }

        return ProjectWorkspacePaths.ResolveInsideProject(
            projectPath, survey.ResponsesRelativePath);
    }
}
