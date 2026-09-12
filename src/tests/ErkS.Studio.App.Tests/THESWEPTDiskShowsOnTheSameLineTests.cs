using System.Globalization;
using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// What the sweep deleted is visible, on the same line as the album decision.
///
/// 🔴 THIS IS THE ONLY PLACE A DELETION EVER SHOWS. The sweep removes the owner's
/// own copies - nothing else in the product deletes their files - and until this
/// line the disk simply got smaller with no explanation. If one day it takes
/// something it should not have, this record is the whole of the evidence.
///
/// 🔴 SO THE TEST THAT MATTERS IS THE ONE ABOUT FORGETTING. Sweeps run on every
/// reconciling build and nearly all of them find nothing; a record that let the
/// next quiet sweep clear the numbers would keep the trace of a deletion for
/// minutes, which is the same as not keeping it.
/// </summary>
public sealed class THESWEPTDiskShowsOnTheSameLineTests
{
    [Fact]
    public void THECountAndTheSizeBothAppear()
    {
        var record = new AlbumDrawRecord();
        record.Record(drew: true, AlbumRebuildReason.FingerprintChanged.ToString(), Now);
        record.RecordStoreSweep(3, 7L * 1024 * 1024, "", Now);

        string sentence = StudioAlbumDrawSentence.For(record);

        Assert.Contains("3", sentence);
        Assert.Contains("7 МБ", sentence);
        Assert.Contains("Цэвэрлэгээ", sentence);

        // The album's own answer is still there - the clause is an addition, not a
        // replacement, or the line stops answering the question it was built for.
        Assert.Contains("дахин зурагдсан", sentence);
    }

    [Fact]
    public void ALATERSweepThatRemovedNothingDoesNOTEraseTheTrace()
    {
        // 🔴 THE FORGETTING HAZARD, WHICH IS THE REAL ONE. «Nothing to remove» is
        // the ordinary answer and it arrives on the very next build. If it cleared
        // the numbers, a deletion would be observable only until the owner touched
        // the project again.
        var record = new AlbumDrawRecord();
        record.RecordStoreSweep(4, 120L * 1024 * 1024, "", Now);
        record.RecordStoreSweep(0, 0, "", Now.AddMinutes(5));

        Assert.Equal(4, record.LastSweepRemovedCount);
        Assert.Equal(120L * 1024 * 1024, record.LastSweepRemovedBytes);
    }

    [Fact]
    public void AREFUSALIsShownAndThenClearedByAHealthySweep()
    {
        // 🔴 A REFUSAL THAT NOBODY READS IS A SILENT NO-OP WITH EXTRA STEPS - the
        // rule in Core gives its refusals a sentence for exactly that reason, and
        // keeping that sentence out of the one line the owner reads would undo it.
        //
        // 🔴 AND IT MUST EXPIRE, unlike the removal numbers. A refusal describes the
        // project as it stands now; yesterday's shown today is a confident false
        // statement. This is the opposite lifetime from the numbers above, which is
        // why the two are stored apart.
        var record = new AlbumDrawRecord();
        record.Record(drew: false, AlbumRebuildReason.NothingChanged.ToString(), Now);
        record.RecordStoreSweep(0, 0, VisualizationStoreCleanup.NothingReferencedMn, Now);

        string refused = StudioAlbumDrawSentence.For(record);
        Assert.Contains(VisualizationStoreCleanup.NothingReferencedMn, refused);
        Assert.Contains("ДАХИН ЗУРАГДААГҮЙ", refused);

        record.RecordStoreSweep(0, 0, "", Now.AddMinutes(5));
        Assert.DoesNotContain(
            VisualizationStoreCleanup.NothingReferencedMn,
            StudioAlbumDrawSentence.For(record));
    }

    [Fact]
    public void ADELETIONBeforeAnyAlbumDecisionIsStillShown()
    {
        // 🔴 FOUND BY A TEST THAT SIMPLY FORGOT TO RECORD A DECISION. The sweep
        // runs inside CreateAlbumBuildProject, and a preview calls that without ever
        // going through the draw decision - so a first-ever build can delete files
        // while DecidedAtUtc is still null. The sentence used to return «no decision
        // yet» and stop, which would have hidden the deletion behind a rule that has
        // nothing to do with deletions.
        var record = new AlbumDrawRecord();
        record.RecordStoreSweep(2, 3L * 1024 * 1024, "", Now);

        string sentence = StudioAlbumDrawSentence.For(record);

        Assert.Contains(StudioAlbumDrawSentence.NeverDecidedMn, sentence);
        Assert.Contains("2", sentence);
        Assert.Contains("3 МБ", sentence);
    }

    [Fact]
    public void AREFUSALWinsOverStaleNumbers()
    {
        // Both can be set at once: a sweep removed files on Monday and refuses
        // today. The refusal is the live statement and must not be hidden behind
        // Monday's success - the owner is looking at this line to find out why
        // their disk is not coming back. No album decision is recorded here on
        // purpose, for the same reason as the test above.
        var record = new AlbumDrawRecord();
        record.RecordStoreSweep(2, 50L * 1024 * 1024, "", Now);
        record.RecordStoreSweep(0, 0, VisualizationStoreCleanup.NothingReferencedMn, Now);

        string sentence = StudioAlbumDrawSentence.For(record);

        Assert.Contains(VisualizationStoreCleanup.NothingReferencedMn, sentence);
        Assert.DoesNotContain("50 МБ", sentence);
    }

    [Fact]
    public void ASWEEPThatNeverRanSaysNOTHINGAtAll()
    {
        // 🔴 THE ORDINARY LINE MUST NOT GROW A LIMB THAT SAYS «0». Most projects
        // will never have an orphan; a clause reading «0 өнчин хуулбар» on every
        // build would train the owner to stop reading the line that matters.
        var record = new AlbumDrawRecord();
        record.Record(drew: false, AlbumRebuildReason.NothingChanged.ToString(), Now);

        Assert.DoesNotContain("Цэвэрлэгээ", StudioAlbumDrawSentence.For(record));
    }

    [Fact]
    public void ACLONEDRecordCarriesTheTrace()
    {
        // 🔴 THE RECORD IS CLONED ON THE WAY INTO A BUILD SNAPSHOT, so a field
        // left out of Clone is a field that exists in the file and is missing
        // wherever it is read from a copy - the quietest way for the only evidence
        // of a deletion to disappear. Asserted on the OBJECT rather than field by
        // field, so a field added later without a clone line still has to answer.
        var record = new AlbumDrawRecord();
        record.Record(drew: true, AlbumRebuildReason.FingerprintChanged.ToString(), Now);
        record.RecordStoreSweep(5, 9L * 1024 * 1024, "", Now);

        AlbumDrawRecord copy = record.Clone();

        Assert.Equal(
            StudioAlbumDrawSentence.For(record),
            StudioAlbumDrawSentence.For(copy));
        Assert.Equal(record.LastSweptAtUtc, copy.LastSweptAtUtc);

        record.RecordStoreSweep(0, 0, VisualizationStoreCleanup.NothingReferencedMn, Now);
        AlbumDrawRecord refusedCopy = record.Clone();
        Assert.Equal(
            VisualizationStoreCleanup.NothingReferencedMn,
            refusedCopy.LastSweepRefusalMn);
    }

    [Theory]
    [InlineData(0L, "1 КБ")]
    [InlineData(1L, "1 КБ")]
    [InlineData(4096L, "4 КБ")]
    [InlineData(1024L * 1024, "1 МБ")]
    [InlineData(68L * 1024 * 1024, "68 МБ")]
    public void SMALLRemovalsAreLegibleToo(long bytes, string expected)
    {
        // 🔴 MEGABYTES ALONE WOULD PRINT «0 МБ» FOR A FILE THAT IS GONE. Renders are
        // tens of megabytes, so the common tail is safe and the rare one is not:
        // «1 өнчин хуулбар, 0 МБ» reads as «nothing happened». A deletion this line
        // exists to witness must not be able to describe itself as nothing.
        Assert.Equal(expected, StudioAlbumDrawSentence.SizeMn(bytes));
    }

    [Fact]
    public void AFRACTIONALSizeIsWrittenInTheReadersCulture()
    {
        // The decimal separator comes from the machine, not from this file - the
        // same trap a «96.4 секунд» expectation fell into earlier.
        string expected = (1.5d).ToString("0.#", CultureInfo.CurrentCulture) + " МБ";

        Assert.Equal(expected, StudioAlbumDrawSentence.SizeMn(1536L * 1024));
    }

    /// <summary>
    /// A fixed instant. The record's job is to say WHEN, so a test that used the
    /// real clock would be asserting against the thing under test.
    /// </summary>
    private static DateTimeOffset Now { get; } =
        new(2026, 9, 12, 14, 30, 0, TimeSpan.Zero);
}
