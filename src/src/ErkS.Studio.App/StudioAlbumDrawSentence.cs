using ErkS.Platform.Core;

namespace ErkS.Studio;

/// <summary>
/// What the last album decision says, in the owner's language.
///
/// 🔴 THIS EXISTS SO «IT WAS SLOW» HAS AN ANSWER THAT IS NOT A STOPWATCH. Until
/// now the only evidence that an album had been redrawn was how long it felt,
/// which gets worse on a faster machine and cannot be checked afterwards. The
/// owner reports a delay hours later; by then the record is the only thing left.
/// </summary>
internal static class StudioAlbumDrawSentence
{
    internal const string NeverDecidedMn = "Альбом зурах шийдвэр хараахан гараагүй байна.";

    public static string For(AlbumDrawRecord? record)
    {
        if (record?.DecidedAtUtc is null)
            return NeverDecidedMn;

        string reason = ReasonMn(record.ReasonCode);
        string when = record.DecidedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");

        if (!record.Drew)
        {
            // 🔴 THE SKIP IS THE SENTENCE WORTH HAVING. A build leaves a file
            // behind; a build correctly skipped leaves nothing at all, and this
            // line is the whole of the evidence that the rule ran.
            string lastDrew = record.LastDrewAtUtc is null
                ? ""
                : " Сүүлд " +
                    record.LastDrewAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") +
                    "-д зурагдсан.";
            return $"{when}: {reason} альбом ДАХИН ЗУРАГДААГҮЙ.{lastDrew}";
        }

        string took = record.LastDrewSeconds > 0
            ? $" {record.LastDrewSeconds:0.#} секунд."
            : "";
        return $"{when}: {reason} альбом дахин зурагдсан.{took}";
    }

    /// <summary>
    /// The stored code turned back into a sentence.
    ///
    /// 🔴 AN UNRECOGNISED CODE IS REPORTED AS ITSELF, NEVER GUESSED. Falling back
    /// to the first reason would put a confident wrong explanation on screen -
    /// and this record's only job is to be trustworthy about what happened.
    /// </summary>
    private static string ReasonMn(string? reasonCode)
    {
        string code = (reasonCode ?? "").Trim();
        if (code.Length == 0)
            return "шалтгаан тэмдэглэгдээгүй тул";

        return Enum.TryParse(code, ignoreCase: false, out AlbumRebuildReason reason) &&
            Enum.IsDefined(reason)
            ? StudioAlbumRebuildPolicy.DescribeMn(reason)
            : $"«{code}» гэсэн шалтгаанаар";
    }
}
