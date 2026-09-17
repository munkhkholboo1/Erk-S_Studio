using System.Text;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A delivery the scan drops is NAMED, instead of vanishing.
///
/// 🔴 THE APP ALREADY COUNTED EXACTLY WHAT IT THREW AWAY, AND TOLD NOBODY. A manifest
/// found in a watched folder whose source id disagrees with the folder's registration is
/// counted into SheetIntakeScanResult.SkippedForeignManifestCount and dropped - not
/// absorbed, not rejected, no error, no status line, no quarantine entry. The number
/// existed and had no reader, so the only symptom reaching the owner was «Хүлээн авсан:
/// 0 sheet» with no reason attached.
///
/// 🔴 AND ONE OF THOSE COUNTERS CARRIES A COMMENT CLAIMING THE OPPOSITE. Beside
/// UnattributedManifestCount the file says «the compatibility is deliberate, the silence
/// was not — a package landing in the wrong inbox is adopted here, so the count makes
/// that visible». It made it visible to nothing. That is today's recurring shape once
/// more: a sentence asserting a property nothing holds.
///
/// ⚠ THIS IS AN INSTRUMENT, NOT THE CURE. It does not decide which source owns a
/// delivery and it moves no files. What it does is turn an invisible failure into a
/// number the owner can read, which is also what tells us WHICH failure this is - a
/// non-zero count means manifests are being dropped on an id disagreement, a zero count
/// with an empty library means the folder was never scanned at all. One reading, two
/// hypotheses separated.
/// </summary>
public sealed class THESILENTLYDroppedDeliveryIsNamedTests
{
    [Fact]
    public void THEDROPPEDCountsReachTheOwnersStatusLine()
    {
        // 🔴 CODE ONLY, AND SABOTAGE IS WHY. A first version read the whole method, and
        // a mutation that deleted UnattributedManifestCount from BOTH the condition and
        // the message SURVIVED - because the comment above the report still names it. A
        // Contains satisfied by a comment asserts nothing about the product at all.
        string body = CodeOnly(ReportingBody());

        Assert.Contains("SkippedForeignManifestCount", body, StringComparison.Ordinal);
        Assert.Contains("UnattributedManifestCount", body, StringComparison.Ordinal);
        Assert.Contains("SetStatus", body, StringComparison.Ordinal);

        // Both counts reach the SENTENCE, not merely the condition: a report that tested
        // two numbers and printed one would leave the other invisible, which is the exact
        // state being repaired.
        Assert.Contains("{scan.SkippedForeignManifestCount}", body, StringComparison.Ordinal);
        Assert.Contains("{scan.UnattributedManifestCount}", body, StringComparison.Ordinal);
    }

    [Fact]
    public void ADROPPEDDeliveryIsReportedEvenWhenTheScanHadNoERRORS()
    {
        // 🔴 THE WHOLE POINT: these scans end with ErrorCount == 0. A manifest skipped as
        // foreign is not an error by the intake's own reckoning, so a report hung off the
        // error branch would never fire - which is precisely the state this replaces.
        string compact = Compact(ReportingBody());

        Assert.Contains("if(scan.ErrorCount>0)", compact, StringComparison.Ordinal);

        // The report must not sit inside the error branch.
        int errorBranch = compact.IndexOf("if(scan.ErrorCount>0)", StringComparison.Ordinal);
        int elseBranch = compact.IndexOf("else", errorBranch, StringComparison.Ordinal);
        int report = compact.IndexOf("scan.SkippedForeignManifestCount>0", StringComparison.Ordinal);

        Assert.True(elseBranch > errorBranch, "the error branch has no else");
        Assert.True(report > elseBranch, "the dropped-delivery report belongs on the no-error path");
    }

    [Fact]
    public void THEREFRESHStillHappensAndIsNotSwallowedByTheNewBranch()
    {
        // ⚠ THE REGRESSION THIS COULD HAVE CAUSED, ASSERTED. Hanging the new report off
        // the existing else-if chain would have made a scan that BOTH hydrated something
        // and dropped something stop refreshing the workspace - the new message arriving
        // and the screen going stale behind it.
        string compact = Compact(CodeOnly(ReportingBody()));

        Assert.Contains("SilentlyHydratedManifestCount>0", compact, StringComparison.Ordinal);
        Assert.Contains("RefreshSourceWorkspace()", compact, StringComparison.Ordinal);

        // The refresh is reached independently of whether anything was dropped.
        int report = compact.IndexOf("scan.SkippedForeignManifestCount>0", StringComparison.Ordinal);
        int refresh = compact.IndexOf("SilentlyHydratedManifestCount>0", StringComparison.Ordinal);
        Assert.True(refresh > report, "the refresh check follows the report rather than replacing it");

        // 🔴 THE BAN IS ON THE REFRESH, NOT ON THE REPORT, AND A FIRST VERSION HAD IT
        // THE WRONG WAY ROUND. Chaining is only harmful where it SWALLOWS something, and
        // the thing that can be swallowed is the refresh - so the mutation that actually
        // causes the damage writes «else if» in front of the hydrated check. Banning it in
        // front of the report instead let that mutation through untouched.
        Assert.DoesNotContain(
            "elseif(scan.SilentlyHydratedManifestCount", compact, StringComparison.Ordinal);
    }

    [Fact]
    public void THEHISTORICALSkipIsDELIBERATELYNotReported()
    {
        // ⚠ NOT EVERY SKIP IS A LOSS, AND SAYING SO KEEPS THE SIGNAL WORTH READING.
        // SkippedHistoricalManifestCount counts superseded deliveries the scan is MEANT
        // to pass over - 48 of them in the owner's folder - so reporting it would print a
        // large number on every ordinary scan and teach the reader to ignore the line the
        // real fault arrives on. Asserted as an absence so the exclusion is a decision
        // with a reason rather than an oversight somebody later «fixes».
        // ⚠ CODE ONLY. A first version banned the name across the whole method and went
        // red on its own explanation - the comment above the report NAMES this counter to
        // say why it is excluded, which is exactly the note a later reader needs. A banned
        // word fails both ways; the ban belongs on what executes.
        Assert.DoesNotContain(
            "SkippedHistoricalManifestCount", CodeOnly(ReportingBody()), StringComparison.Ordinal);

        // Positive control: the ban is looking at something. Without this, a CodeOnly that
        // returned "" would pass every exclusion in this file.
        Assert.Contains("SkippedForeignManifestCount", CodeOnly(ReportingBody()), StringComparison.Ordinal);
    }

    [Fact]
    public void BOTHScanReportersNameTheSameTHREELosses()
    {
        // 🔴 THE SAME LOSS WAS VISIBLE OR INVISIBLE DEPENDING ON HOW THE SCAN STARTED.
        // A scan can lose work three ways - dropped as foreign, adopted unattributed, or
        // rejected on verification - and each reporter named a DIFFERENT two of them. The
        // startup path (project open, which is what hydrates the library) omitted
        // rejections; the manual refresh omitted the foreign skip. A reader comparing the
        // two would conclude the quiet one had nothing to say.
        string startup = CodeOnly(ReportingBody());
        string manual = CodeOnly(SummaryBody());

        foreach (string counter in new[]
                 {
                     "SkippedForeignManifestCount",
                     "UnattributedManifestCount",
                     "RejectedPackageCount",
                 })
        {
            Assert.Contains(counter, startup, StringComparison.Ordinal);
            Assert.Contains(counter, manual, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ADARKSourceIsReportedSoTheZEROESMeanSomething()
    {
        // 🔴 THE HOLE IN THE COUNTS, CLOSED. All three counters are produced BY a
        // scan, and every scan walks the watcher table - so a source nothing watches
        // reports zero of everything, exactly like a healthy one. Without this line the
        // instrument shipped yesterday is quietly reassuring in the one case it was
        // built to catch.
        string startup = CodeOnly(ReportingBody());

        Assert.Contains("UnwatchedSourceNames()", startup, StringComparison.Ordinal);

        // Named, not merely counted: «1 source is not watched» with no name sends the
        // reader through every source they have.
        Assert.Contains("string.Join(", startup, StringComparison.Ordinal);

        // ⚠ REPORTED SEPARATELY FROM THE LOSS COUNTS, because it is a different fact -
        // not «something was lost» but «nothing was looked at». Folding it into the same
        // condition would make a dark source announce losses it cannot have measured.
        Assert.DoesNotContain(
            "dark.Count > 0 || scan.", startup, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "scan.SkippedForeignManifestCount > 0 || dark", startup, StringComparison.Ordinal);
    }

    /// <summary>The manual refresh's summary, which the Sources page shows.</summary>
    private static string SummaryBody()
    {
        string source = ReadAppSource("ShellView.Workspaces.cs")
            .Replace("\r\n", "\n");
        const string anchor = "private static string BuildSourceRefreshSummary(";
        int at = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at > 0, anchor + " was not found");
        int end = source.IndexOf("\n    }", at, StringComparison.Ordinal);
        Assert.True(end > at, "the end of the summary was not found");
        return source[at..end];
    }

    /// <summary>
    /// The method with its comments removed, so a ban applies to what executes.
    ///
    /// ⚠ The newline is named by its code point: every tool between here and the file
    /// has an opinion about a backslash, and two of them rewrote such a line into a real
    /// break earlier in this repository.
    /// </summary>
    private static string CodeOnly(string source)
    {
        var kept = new List<string>();
        foreach (string line in source.Split((char)10))
        {
            string bare = line.TrimEnd((char)13);
            if (bare.TrimStart().StartsWith("//", StringComparison.Ordinal))
                continue;
            int at = bare.IndexOf("//", StringComparison.Ordinal);
            kept.Add(at >= 0 ? bare[..at] : bare);
        }

        return string.Join((char)10, kept);
    }

    private static string Compact(string text) =>
        new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>
    /// The method that consumes a background scan result.
    ///
    /// ⚠ SOURCE-ANCHORED, AND THE LIMIT IS NAMED: this is a private async method on a
    /// WPF shell that needs an open project and a watcher to run. What is held here is
    /// that the counts reach a status line and that the refresh is not displaced. That
    /// the counts are PRODUCED correctly is held behaviourally in Core, by
    /// SheetPackageTests.Intake_CurrentSnapshotScan_SkipsHistoricalForeignSourceInOwnedInbox.
    /// </summary>
    private static string ReportingBody()
    {
        string source = ReadAppSource("ShellView.cs").Replace("\r\n", "\n");
        const string anchor = "private async Task RescanOpenedProjectPackagesAsync(";
        int at = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(at > 0, anchor + " was not found");
        int end = source.IndexOf("\n    }", at, StringComparison.Ordinal);
        Assert.True(end > at, "the end of the method was not found");
        return source[at..end];
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
