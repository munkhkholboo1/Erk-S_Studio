using System.IO;
using System.Text.Json;

namespace ErkS.Studio;

/// <summary>
/// A seat this machine has left locally but has not yet been released on the
/// server.
///
/// Leaving bot state clears the local seat FIRST, so a machine is never held
/// hostage by an unreachable server. That trade has a cost: the seat can stay
/// occupied server-side while nothing on this device points at it any more, and
/// an occupied seat that nobody can see is the same defect moved one layer over.
/// This is the note that keeps it visible until it is really gone.
/// </summary>
internal sealed record PendingBotSeatRelease
{
    public required string OrganizationId { get; init; }

    public required string BotId { get; init; }

    public required string DisplayName { get; init; }

    public required string DeviceFingerprint { get; init; }

    public required DateTimeOffset LeftAtUtc { get; init; }

    /// <summary>Why the release did not reach the server, in the server's own words.</summary>
    public string LastFailure { get; init; } = "";

    /// <summary>
    /// The server's own error CODE for that refusal, beside the sentence.
    ///
    /// 🔴 THE SENTENCE ALONE COULD NOT ANSWER THE ONE QUESTION ASKED OF IT. The
    /// code is what decides whether this entry is dropped; the message is for a
    /// person to read. With only the message on file, working out why an entry
    /// had survived four days meant guessing which of the server's refusals had
    /// produced that wording - and the guess was wrong.
    /// </summary>
    public string LastFailureCode { get; init; } = "";

    /// <summary>
    /// When the last attempt ran, and how many there have been.
    ///
    /// Absent, not zero, when none has: an entry written and never retried is a
    /// different thing from one retried nine times, and «0» would let the first
    /// pass for the second.
    /// </summary>
    public DateTimeOffset? LastAttemptUtc { get; init; }

    public int AttemptCount { get; init; }
}

/// <summary>
/// What one pass over the queued seat releases did.
///
/// 🔴 A BACKGROUND CHORE WAS WRITING OVER THE ANSWER TO WHAT THE PERSON HAD
/// JUST DONE. The flush set the status line itself, and it runs at every owner
/// sign-in - so «Эзэмшигчээр баталгаажлаа» was replaced, in the instant it
/// appeared, by a bot-seat refusal about a machine that had left bot state days
/// earlier. From the outside that is «I signed in as the owner and it came up as
/// the bot», and the confirmation the person was waiting for never showed.
///
/// So the flush REPORTS and the caller composes: the answer to the action comes
/// first, the chore is a clause after it. Neither is dropped - a chore that
/// fails in silence looks exactly like one that worked.
/// </summary>
internal sealed record BotSeatFlushOutcome(
    int Released,
    int AlreadyFree,
    IReadOnlyList<string> StillHeld)
{
    public static readonly BotSeatFlushOutcome Nothing = new(0, 0, []);

    public bool IsSilent => Released == 0 && AlreadyFree == 0 && StillHeld.Count == 0;

    /// <summary>
    /// What to add after the sentence answering what the person did. Empty when
    /// there is nothing to add.
    /// </summary>
    public string Clause()
    {
        if (StillHeld.Count > 0)
            return "Цуцлагдаагүй ботын суудал: " + string.Join(", ", StillHeld);
        if (Released == 0 && AlreadyFree == 0)
            return "";

        // Said separately, because «freed just now» and «was already free» are
        // different facts and only the first is something this sign-in did.
        // Both end the same way: nothing is left pending.
        if (AlreadyFree == 0)
            return $"Өмнө цуцлагдаагүй {Released} ботын суудал серверт чөлөөлөгдлөө.";
        if (Released == 0)
            return $"Ботын {AlreadyFree} суудал серверт аль хэдийн чөлөөтэй байсныг баталж, хүлээгдэж байсан бүртгэлийг цэвэрлэлээ.";
        return $"Ботын суудал: {Released} чөлөөлөгдлөө, {AlreadyFree} аль хэдийн чөлөөтэй байв.";
    }

    /// <summary>
    /// The answer to what the person did, with the chore's clause after it.
    ///
    /// The ORDER is the rule: what they asked for is what they are waiting to
    /// read, and a queued retry must not be able to take its place.
    /// </summary>
    public string After(string answer)
    {
        string clause = Clause();
        if (string.IsNullOrWhiteSpace(clause))
            return answer;
        return string.IsNullOrWhiteSpace(answer) ? clause : answer + "  ·  " + clause;
    }
}

internal static class StudioPendingBotSeatReleases
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly object Gate = new();

    public static string StorePath => Path.Combine(
        StudioAccountService.AccountDataRoot,
        "bot-seat-releases-pending.json");

    public static IReadOnlyList<PendingBotSeatRelease> Read()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(StorePath))
                    return [];
                return JsonSerializer.Deserialize<List<PendingBotSeatRelease>>(
                    File.ReadAllText(StorePath),
                    JsonOptions) ?? [];
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or UnauthorizedAccessException)
            {
                // An unreadable note is not an empty one. The caller is told so
                // it can say "there may be a seat still held" rather than the
                // more comfortable "there is none".
                return [];
            }
        }
    }

    /// <summary>
    /// Records a seat that is still held on the server. Returns false when the
    /// note itself could not be written - which the caller MUST report, because
    /// at that point the only remaining record of the seat is on a screen the
    /// user is looking at once.
    /// </summary>
    public static bool Record(PendingBotSeatRelease release)
    {
        ArgumentNullException.ThrowIfNull(release);
        lock (Gate)
        {
            try
            {
                List<PendingBotSeatRelease> all = [.. ReadUnlocked()
                    .Where(item => !Matches(item, release.OrganizationId, release.BotId))];
                all.Add(release);
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(StorePath, JsonSerializer.Serialize(all, JsonOptions));
                return true;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                return false;
            }
        }
    }

    public static void Forget(string organizationId, string botId)
    {
        lock (Gate)
        {
            try
            {
                List<PendingBotSeatRelease> all = [.. ReadUnlocked()
                    .Where(item => !Matches(item, organizationId, botId))];
                if (all.Count == 0)
                {
                    if (File.Exists(StorePath))
                        File.Delete(StorePath);
                    return;
                }
                File.WriteAllText(StorePath, JsonSerializer.Serialize(all, JsonOptions));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // Failing to forget costs one more retry, which is idempotent.
            }
        }
    }

    private static List<PendingBotSeatRelease> ReadUnlocked()
    {
        if (!File.Exists(StorePath))
            return [];
        return JsonSerializer.Deserialize<List<PendingBotSeatRelease>>(
            File.ReadAllText(StorePath),
            JsonOptions) ?? [];
    }

    /// <summary>
    /// Writes down what the latest retry met.
    ///
    /// 🔴 THE FIELD SAID «why the release did not reach the server» AND THEN
    /// NEVER MOVED AGAIN. It was filled once, by the attempt that created the
    /// entry, and every retry afterwards failed in silence - so four days and
    /// several sign-ins later the file still carried the first sentence, and
    /// there was no way to tell from it whether a retry had run at all. A
    /// diagnosis was made from that stale line, and it named the wrong cause.
    ///
    /// A note whose only reader is a person days later has to be written by the
    /// code that learns something, every time it learns it.
    /// </summary>
    public static void NoteAttempt(
        string organizationId,
        string botId,
        string failureCode,
        string failureMessage)
    {
        lock (Gate)
        {
            try
            {
                List<PendingBotSeatRelease> all = ReadUnlocked();
                int index = all.FindIndex(item => Matches(item, organizationId, botId));
                if (index < 0)
                    return;
                all[index] = all[index] with
                {
                    LastFailure = failureMessage ?? "",
                    LastFailureCode = failureCode ?? "",
                    LastAttemptUtc = DateTimeOffset.UtcNow,
                    AttemptCount = all[index].AttemptCount + 1,
                };
                File.WriteAllText(StorePath, JsonSerializer.Serialize(all, JsonOptions));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // The retry itself already failed and was reported. Losing the
                // note about it costs a diagnosis, not the seat.
            }
        }
    }

    private static bool Matches(PendingBotSeatRelease item, string organizationId, string botId) =>
        item.OrganizationId.Equals(organizationId ?? "", StringComparison.OrdinalIgnoreCase) &&
        item.BotId.Equals(botId ?? "", StringComparison.OrdinalIgnoreCase);
}
