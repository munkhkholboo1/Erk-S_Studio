using System.IO;
using System.Threading;
using System.Text.Json;

namespace ErkS.Studio;

/// <summary>
/// One refusal the server gave, kept where the person who hit it can read it.
///
/// 🔴 THE FACT THAT WENT MISSING WHEN SOMEBODY LOST THEIR SEATED MACHINE. Studio
/// asked for the PIN, the PIN was right, the resume failed, and the seat
/// disappeared. Five hypotheses were built and measured against the server's
/// stored data and every one of them fell. What was missing was never a theory:
/// it was WHICH REFUSAL ARRIVED - a fact that existed for a few milliseconds on
/// one machine and then nowhere, because the server keeps no log of it either.
/// </summary>
internal sealed record StudioBoundaryRefusal
{
    /// <summary>
    /// The route, as a SYMBOL - «projects/{id}/albums/{id}/revisions», never the
    /// URL itself.
    ///
    /// Ids in the path would make this file a list of what the person is working
    /// on, which is not what a diagnosis needs. The symbol answers «which door
    /// refused», and that is the whole question.
    /// </summary>
    public required string Route { get; init; }

    /// <summary>
    /// The server's own code, verbatim - INCLUDING one this build has never
    /// heard of.
    ///
    /// A reader that writes «unknown» for a code it does not recognise destroys
    /// the only part of the answer that was new. The server can add words; this
    /// file is how the next one arrives legible.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>The HTTP status. All that is left when a refusal carries no code at all.</summary>
    public required int Status { get; init; }

    /// <summary>The server's sentence, for the person rather than the diagnosis.</summary>
    public required string Message { get; init; }

    public required DateTimeOffset AtUtc { get; init; }

    /// <summary>
    /// How many times in a row this same route answered with this same code.
    ///
    /// «Once, while the network was down» and «every single time» lead to
    /// different places, and only a count separates them.
    /// </summary>
    public int Attempts { get; init; }

    /// <summary>
    /// One extra fact the caller thought a diagnosis would need, or empty.
    ///
    /// 🔴 A FINGERPRINT GOES IN HERE AS EIGHT CHARACTERS AND NO MORE. Eight is
    /// enough to tell «the same one as last time» from «a different one», which
    /// is the whole question a fingerprint raises, and useless for anything
    /// else. The value itself identifies the device and is not the client's to
    /// spread through a file.
    /// </summary>
    public string Detail { get; init; } = "";
}

/// <summary>
/// Every boundary refusal this machine met, in one bounded list.
///
/// 🔴 WRITTEN WHERE THE REFUSAL IS BORN, NOT WHERE IT IS CAUGHT. Studio catches
/// StudioAccountException in seventeen places and three of them show the
/// server's own sentence; none reads the code. Editing seventeen call sites to
/// cooperate is the «rule written, caller never asks» shape that produced this
/// week's defects, so the record is taken at the one place every refusal passes
/// through instead, and no caller has to remember anything.
///
/// Bounded, because a diagnosis needs the last few and a person's disk does not
/// need the rest. Cleared PER ROUTE on success, because a working route erasing
/// a broken route's evidence is how the answer disappears again.
/// </summary>
internal static class StudioBoundaryRefusals
{
    /// <summary>
    /// How many refusals are kept. Enough to show a pattern across a session,
    /// small enough that a person can read the file.
    /// </summary>
    public const int Capacity = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly object Gate = new();

    /// <summary>
    /// Whether anything is recorded, once that has been established.
    ///
    /// 🔴 THIS SITS ON THE HOT PATH OF EVERY SINGLE API CALL. Forgetting is
    /// bound to success, which is the right rule and also means a Cloud Sync
    /// making fifty requests would read this file fifty times to discover
    /// fifty times that there is nothing to forget. On the ordinary run - a
    /// machine where nothing is failing - that is the entire cost of the
    /// mechanism, paid for no reason.
    ///
    /// Null until the first read settles it. A second Studio writing the file
    /// could make this stale, and the cost of that is a diagnostic note living
    /// slightly too long, which is why a cheap flag is enough here and would not
    /// be if this guarded anything a person relies on.
    /// </summary>
    private static bool? anythingRecorded;

    public static string StorePath => Path.Combine(
        StudioAccountService.AccountDataRoot,
        "boundary-refusals.json");

    public static IReadOnlyList<StudioBoundaryRefusal> Read()
    {
        lock (Gate)
            return ReadUnlocked();
    }

    /// <summary>
    /// The most recent refusal on <paramref name="route"/>, or null if that
    /// route is not currently failing.
    ///
    /// 🔴 RECENCY IS POSITION, NOT THE TIMESTAMP. The first version compared
    /// AtUtc and a test caught it going the wrong way: the system clock on
    /// Windows advances in steps of about fifteen milliseconds, so two refusals
    /// in the same instant carry the SAME time, «later than» is false for the
    /// newer one, and the older answer wins. The list is appended to, so the
    /// last match IS the newest - and it stays right however coarse the clock.
    /// The timestamp is for the person reading the file.
    /// </summary>
    public static StudioBoundaryRefusal? Latest(string route)
    {
        string wanted = (route ?? "").Trim();
        lock (Gate)
        {
            StudioBoundaryRefusal? best = null;
            foreach (StudioBoundaryRefusal refusal in ReadUnlocked())
            {
                if (refusal.Route.Equals(wanted, StringComparison.Ordinal))
                    best = refusal;
            }
            return best;
        }
    }

    /// <summary>
    /// Records this refusal, counting a repeat of the same code on the same
    /// route rather than filling the list with copies of one failure.
    /// </summary>
    public static void Note(
        string route,
        string code,
        int status,
        string message,
        string detail = "")
    {
        lock (Gate)
        {
            try
            {
                string symbol = (route ?? "").Trim();
                string named = (code ?? "").Trim();
                var kept = new List<StudioBoundaryRefusal>();
                int attempts = 1;

                IReadOnlyList<StudioBoundaryRefusal> onDisk = ReadUnlocked(out bool trustworthy);
                if (!trustworthy)
                {
                    // Losing this note costs a diagnosis. Writing over a file we
                    // could not read costs every diagnosis before it.
                    return;
                }

                foreach (StudioBoundaryRefusal existing in onDisk)
                {
                    if (existing.Route.Equals(symbol, StringComparison.Ordinal) &&
                        existing.Code.Equals(named, StringComparison.Ordinal))
                    {
                        attempts = existing.Attempts + 1;
                        continue;
                    }
                    kept.Add(existing);
                }

                kept.Add(new StudioBoundaryRefusal
                {
                    Route = symbol,
                    Code = named,
                    Status = status,
                    Message = StudioOperationDiagnosticLog.SanitizeMessage(message),
                    AtUtc = DateTimeOffset.UtcNow,
                    Attempts = attempts,
                    Detail = (detail ?? "").Trim(),
                });

                // The oldest go first: a diagnosis is about what is failing now.
                while (kept.Count > Capacity)
                    kept.RemoveAt(0);

                Write(kept);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException
                    or NotSupportedException)
            {
                // The call already failed and was reported. Losing the note
                // costs a diagnosis, not the operation.
            }
        }
    }

    /// <summary>
    /// Attaches one extra fact to the refusal just recorded.
    ///
    /// The funnel records every refusal without being asked, which is the whole
    /// point of it - but it cannot know what a PARTICULAR caller would need to
    /// tell the two failures apart. The seat resume is the case that taught us
    /// this: the question there is «did this machine send the same fingerprint
    /// as last time», and the funnel has never heard of a fingerprint.
    ///
    /// Annotates the most recent entry, because the caller runs on the refusal
    /// it just received, on the UI thread, with nothing between. It changes no
    /// count: the funnel did the recording and this only adds to it.
    /// </summary>
    public static void AnnotateLatest(string detail)
    {
        string extra = (detail ?? "").Trim();
        if (extra.Length == 0)
            return;

        lock (Gate)
        {
            try
            {
                var stored = new List<StudioBoundaryRefusal>(ReadUnlocked(out bool trustworthy));
                if (!trustworthy || stored.Count == 0)
                    return;
                stored[^1] = stored[^1] with { Detail = extra };
                Write(stored);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException
                    or NotSupportedException)
            {
            }
        }
    }

    /// <summary>
    /// Forgets what this route said, because it has stopped saying it.
    ///
    /// 🔴 PER ROUTE, NOT WHOLESALE. A note that outlives its cause is read by
    /// the next person as a live failure, so success must clear - but a
    /// successful project fetch says nothing about an album upload that is
    /// still refusing, and clearing everything would erase the evidence of the
    /// failure somebody is actually chasing.
    /// </summary>
    public static void Cleared(string route)
    {
        string wanted = (route ?? "").Trim();
        lock (Gate)
        {
            // Nothing has ever been recorded, so there is nothing to forget
            // and no reason to touch the disk. See anythingRecorded.
            if (anythingRecorded == false)
                return;

            try
            {
                // No trustworthiness check here, and a mutation proved it would be
                // dead weight: an unreadable file yields an empty list, so nothing
                // is filtered, so the count below is unchanged and this returns
                // without writing. The protection Note() needs is already here by
                // construction - and a guard no test can justify is a guard that
                // will be trusted for the wrong reason later.
                IReadOnlyList<StudioBoundaryRefusal> stored = ReadUnlocked();
                var kept = new List<StudioBoundaryRefusal>();
                foreach (StudioBoundaryRefusal refusal in stored)
                {
                    if (!refusal.Route.Equals(wanted, StringComparison.Ordinal))
                        kept.Add(refusal);
                }

                if (kept.Count == stored.Count)
                    return;

                Write(kept);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException
                    or NotSupportedException)
            {
            }
        }
    }

    /// <summary>Forgets everything. For a person clearing their own diagnostics.</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(StorePath))
                    File.Delete(StorePath);
                anythingRecorded = false;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // The file is still there, so the flag must not claim otherwise:
                // a wrong «nothing recorded» would make every later success skip
                // the disk and leave a solved failure on screen forever.
                anythingRecorded = null;
            }
        }
    }

    private static void Write(List<StudioBoundaryRefusal> refusals)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
        anythingRecorded = refusals.Count > 0;
        File.WriteAllText(StorePath, JsonSerializer.Serialize(refusals, JsonOptions));
    }

    /// <summary>
    /// What is on disk, and whether that answer can be trusted.
    ///
    /// 🔴 «NO FILE» AND «A FILE I COULD NOT READ» USED TO BE THE SAME EMPTY LIST,
    /// AND A WRITER ON TOP OF THAT LOSES EVERYTHING. Note() reads, appends and
    /// writes the whole list back - so one transient read failure turned a file of
    /// twenty recorded refusals into a file of one. The record that exists to
    /// explain a failure would erase itself under exactly the conditions that
    /// produce failures: a busy disk.
    ///
    /// The read is retried first, because the cause is transient by nature - a
    /// file written moments ago and briefly held open elsewhere. Only if it still
    /// cannot be read does it report itself untrustworthy, and callers that were
    /// about to write then leave the file alone.
    /// </summary>
    private static IReadOnlyList<StudioBoundaryRefusal> ReadUnlocked(out bool trustworthy)
    {
        trustworthy = true;
        if (!File.Exists(StorePath))
        {
            anythingRecorded = false;
            return [];
        }

        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                List<StudioBoundaryRefusal> stored =
                    JsonSerializer.Deserialize<List<StudioBoundaryRefusal>>(
                        File.ReadAllText(StorePath), JsonOptions) ?? [];
                anythingRecorded = stored.Count > 0;
                return stored;
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
                // Somebody else has it open for an instant. Worth another look.
                Thread.Sleep(15 * (attempt + 1));
            }
            catch (Exception exception) when (
                exception is JsonException or NotSupportedException)
            {
                // Not transient, and not ours to repair. The bytes are still the
                // only record of what happened, so they are not overwritten.
                break;
            }
        }

        trustworthy = false;
        return [];
    }

    private static IReadOnlyList<StudioBoundaryRefusal> ReadUnlocked() =>
        ReadUnlocked(out _);
}
