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
        if (record is null)
            return NeverDecidedMn;

        // 🔴 THE SWEEP CLAUSE IS APPENDED TO EVERY ANSWER, INCLUDING «NEVER
        // DECIDED». A test asking for a deletion on a record with no decision found
        // this: the sweep runs inside CreateAlbumBuildProject, which a preview calls
        // without ever going through the draw decision, so a first-ever build can
        // delete files while DecidedAtUtc is still null. Returning early there
        // would have made the deletion invisible - the notice hidden by a rule that
        // has nothing to do with it.
        return DecisionMn(record) + PreparationClauseMn(record) + SweepClauseMn(record);
    }

    /// <summary>
    /// What bringing the images down to the album's density did.
    ///
    /// 🔴 THE FIRST BUILD COSTS TWICE AND THE OWNER SHOULD SEE BOTH HALVES. It
    /// redraws because the project changed AND it prepares 26 images for the first
    /// time; naming only the first would make the album look mysteriously slow once
    /// and never again.
    ///
    /// 🔴 AND THE FALLBACK IS NAMED, NEVER SILENT. An image that could not be
    /// prepared goes in at source size on purpose - one bad render must not cost a
    /// 46-page album - but an album quietly heavier than its own rule is how a
    /// two-gigabyte file gets shipped without anybody deciding to.
    /// </summary>
    private static string PreparationClauseMn(AlbumDrawRecord record)
    {
        var clause = "";
        if (record.LastPreparedImageCount > 0)
        {
            string when = record.LastPreparedAtUtc is null
                ? ""
                : " (" + record.LastPreparedAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ")";
            clause = $" Бэлтгэл{when}: {record.LastPreparedImageCount} зураг " +
                $"{AlbumRasterRule.DotsPerInch:0} dpi-д бууруулсан.";
        }

        if (record.LastUnpreparedImageCount > 0)
        {
            clause += $" {record.LastUnpreparedImageCount} зураг бэлтгэгдээгүй тул " +
                "эх хэмжээгээр орсон.";
        }

        return clause;
    }

    /// <summary>What the album decision itself says.</summary>
    private static string DecisionMn(AlbumDrawRecord record)
    {
        if (record.DecidedAtUtc is null)
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
    /// What the store sweep did, on the same line as the album decision.
    ///
    /// 🔴 THE OWNER HAS TO BE ABLE TO WATCH THEIR DISK COME BACK. The sweep
    /// deletes their own copies - a thing no other part of the product does - and
    /// a deletion nobody can see is indistinguishable from a bug until the day it
    /// takes something it should not have. Then this line is the only record.
    ///
    /// 🔴 THE REFUSAL IS SHOWN TOO. <see cref="VisualizationStoreCleanup"/>
    /// gives its refusals a sentence precisely so they are not silent no-ops;
    /// keeping that sentence out of the one place the owner reads would undo it.
    /// </summary>
    private static string SweepClauseMn(AlbumDrawRecord record)
    {
        if (record.LastSweepRefusalMn.Length > 0)
            return " Цэвэрлэгээ: " + record.LastSweepRefusalMn;

        if (record.LastSweepRemovedCount <= 0)
            return "";

        string swept = record.LastSweptAtUtc is null
            ? ""
            : " (" + record.LastSweptAtUtc.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ")";
        return $" Цэвэрлэгээ{swept}: {record.LastSweepRemovedCount} өнчин хуулбар, " +
            $"{SizeMn(record.LastSweepRemovedBytes)} чөлөөлөгдсөн.";
    }

    /// <summary>
    /// A byte count the owner can read.
    ///
    /// 🔴 MEGABYTES ALONE WOULD PRINT «0 МБ» FOR A REAL DELETION. Renders are
    /// tens of megabytes, but the store also holds small files, and a line
    /// saying «1 өнчин хуулбар, 0 МБ» reads as «nothing happened» about a file
    /// that is gone. Both tails have to be legible, not just the common one.
    /// </summary>
    internal static string SizeMn(long bytes)
    {
        const long Megabyte = 1024L * 1024L;
        if (bytes >= Megabyte)
            return $"{bytes / (double)Megabyte:0.#} МБ";

        return $"{Math.Max(1, (bytes + 1023) / 1024)} КБ";
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
