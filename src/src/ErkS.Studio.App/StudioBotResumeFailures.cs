using System.IO;
using System.Text.Json;

namespace ErkS.Studio;

/// <summary>
/// What the server said the last time this machine could not resume its seat.
///
/// 🔴 WRITTEN BECAUSE A REAL USER LOST THEIR MACHINE AND NOBODY COULD SAY WHY.
/// Studio asked for the PIN, the PIN was right, the resume failed, and the seat
/// disappeared. Five separate hypotheses were built and measured against the
/// server's stored data - misclassification, a stale queued release, a changed
/// fingerprint, a stale token, two sides disagreeing on a rule - and every one
/// of them fell. What was missing was never a theory. It was the one fact
/// nobody had: WHICH REFUSAL ARRIVED.
///
/// The server keeps no log of it either, so the answer existed for a few
/// milliseconds on one machine and then nowhere. This file is that fact,
/// written where the person who hit it can find it.
///
/// It records what a diagnosis needs and nothing a person owns: a refusal code,
/// a status, a time, an attempt count, and the first eight characters of the
/// fingerprint that was sent - enough to tell «the same one as last time» from
/// «a different one», which is the question a fingerprint raises, without
/// carrying the value itself.
/// </summary>
internal sealed record StudioBotResumeFailure
{
    /// <summary>
    /// The server's own code, verbatim - INCLUDING one this build has never
    /// heard of.
    ///
    /// A reader that writes «unknown» for a code it does not recognise destroys
    /// the only part of the answer that was new. The server can add words; this
    /// file is how the next one arrives legible.
    /// </summary>
    public required string Code { get; init; }

    /// <summary>The HTTP status. All that is left when a failure carries no code at all.</summary>
    public required int Status { get; init; }

    /// <summary>The server's sentence, for the person rather than the diagnosis.</summary>
    public required string Message { get; init; }

    public required DateTimeOffset AtUtc { get; init; }

    /// <summary>
    /// The first eight characters of the fingerprint this machine SENT.
    ///
    /// Eight is enough to compare two runs and useless for anything else. The
    /// question a fingerprint raises is «is this the same device as far as the
    /// server can tell», and that is answered by comparison, not by the value.
    /// </summary>
    public required string SentFingerprintPrefix { get; init; }

    public int Attempts { get; init; }
}

internal static class StudioBotResumeFailures
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly object Gate = new();

    public static string StorePath => Path.Combine(
        StudioAccountService.AccountDataRoot,
        "bot-resume-failure.json");

    public static StudioBotResumeFailure? Read()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(StorePath))
                    return null;
                return JsonSerializer.Deserialize<StudioBotResumeFailure>(
                    File.ReadAllText(StorePath), JsonOptions);
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or UnauthorizedAccessException
                    or NotSupportedException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Records this refusal, counting a repeat of the same code rather than
    /// replacing it.
    ///
    /// The count is what separates «it happened once while the network was
    /// down» from «it happens every single time», and those two lead to
    /// different places.
    /// </summary>
    public static void Note(string code, int status, string message, string sentFingerprint)
    {
        lock (Gate)
        {
            try
            {
                StudioBotResumeFailure? previous = ReadUnlocked();
                string trimmedCode = (code ?? "").Trim();
                int attempts = previous is not null &&
                    previous.Code.Equals(trimmedCode, StringComparison.Ordinal)
                    ? previous.Attempts + 1
                    : 1;

                string sent = (sentFingerprint ?? "").Trim();
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(
                    StorePath,
                    JsonSerializer.Serialize(
                        new StudioBotResumeFailure
                        {
                            Code = trimmedCode,
                            Status = status,
                            Message = (message ?? "").Trim(),
                            AtUtc = DateTimeOffset.UtcNow,
                            SentFingerprintPrefix = sent.Length <= 8 ? sent : sent[..8],
                            Attempts = attempts,
                        },
                        JsonOptions));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
                // The resume already failed and was reported. Losing the note
                // costs a diagnosis, not the seat.
            }
        }
    }

    /// <summary>
    /// Forgets it, because the thing it described has stopped happening.
    ///
    /// A note that outlives its cause is worse than none: the next person to
    /// look reads a solved failure as a live one. Same reason the queued-release
    /// note is dropped the moment the release lands.
    /// </summary>
    public static void Clear()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(StorePath))
                    File.Delete(StorePath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static StudioBotResumeFailure? ReadUnlocked()
    {
        if (!File.Exists(StorePath))
            return null;
        try
        {
            return JsonSerializer.Deserialize<StudioBotResumeFailure>(
                File.ReadAllText(StorePath), JsonOptions);
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
