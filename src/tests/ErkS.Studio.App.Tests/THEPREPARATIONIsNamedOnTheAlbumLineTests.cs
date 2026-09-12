using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Bringing the images down to the album's density is said out loud.
///
/// 🔴 THE FIRST BUILD COSTS TWICE AND BOTH HALVES MUST BE VISIBLE. It redraws
/// because the project changed AND it prepares every image for the first time. With
/// only the first named, the album looks mysteriously slow once and is never
/// explained; with neither named, «it was slow» has no answer but a stopwatch.
///
/// 🔴 AND THE FALLBACK CANNOT BE SILENT. An image that could not be prepared goes in
/// at source size deliberately - one bad render must not cost a 46-page album - but
/// an album quietly heavier than its own rule is how a two-gigabyte file ships
/// without anybody deciding to.
/// </summary>
public sealed class THEPREPARATIONIsNamedOnTheAlbumLineTests
{
    [Fact]
    public void THECountAndTheDensityBothAppear()
    {
        var record = new AlbumDrawRecord();
        record.Record(drew: true, AlbumRebuildReason.FingerprintChanged.ToString(), Now);
        record.RecordRasterPreparation(26, 0, 0d, Now);

        string sentence = StudioAlbumDrawSentence.For(record);

        Assert.Contains("26", sentence);

        // The density is the rule's, read from the rule - a typed «300» here would
        // keep agreeing after somebody changed it.
        Assert.Contains(
            AlbumRasterRule.DotsPerInch.ToString("0", System.Globalization.CultureInfo.CurrentCulture),
            sentence);
        Assert.Contains("Бэлтгэл", sentence);
        Assert.Contains("дахин зурагдсан", sentence);
    }

    [Fact]
    public void ANIMAGEThatWentInATSOURCESizeIsNAMED()
    {
        var record = new AlbumDrawRecord();
        record.Record(drew: true, AlbumRebuildReason.FingerprintChanged.ToString(), Now);
        record.RecordRasterPreparation(25, 1, 0d, Now);

        string sentence = StudioAlbumDrawSentence.For(record);

        Assert.Contains("эх хэмжээгээр", sentence);
        Assert.Contains("25", sentence);
        Assert.Contains("1 зураг бэлтгэгдээгүй", sentence);
    }

    [Fact]
    public void AFIXEDImageSTOPSBeingReportedAsUnprepared()
    {
        // 🔴 THIS NUMBER DESCRIBES THE ALBUM ON DISK, NOT HISTORY - the opposite
        // lifetime from the prepared count beside it. A locked file that has since
        // been prepared must stop being announced, or the line keeps making a
        // statement about an album that no longer exists.
        var record = new AlbumDrawRecord();
        record.RecordRasterPreparation(25, 1, 0d, Now);
        Assert.Contains("эх хэмжээгээр", StudioAlbumDrawSentence.For(record));

        record.RecordRasterPreparation(1, 0, 0d, Now.AddMinutes(5));

        Assert.DoesNotContain("эх хэмжээгээр", StudioAlbumDrawSentence.For(record));
        Assert.Equal(0, record.LastUnpreparedImageCount);
    }

    [Fact]
    public void APASSWithNothingToSayStillClearsASTALEFallback()
    {
        // 🔴 THE CASE THAT SEPARATES THE TWO LIFETIMES. Prepared nothing, failed
        // nothing - and yet there is a stored «2 at source size» to retract, because
        // those two images are no longer in the album at all. Written as its own test
        // because the clearing case where something WAS prepared passes either way,
        // and sabotage on the record could not tell the difference from it.
        var record = new AlbumDrawRecord();
        record.RecordRasterPreparation(26, 2, 0d, Now);

        record.RecordRasterPreparation(0, 0, 0d, Now.AddMinutes(5));

        Assert.Equal(0, record.LastUnpreparedImageCount);
        Assert.Equal(26, record.LastPreparedImageCount);
        Assert.DoesNotContain("эх хэмжээгээр", StudioAlbumDrawSentence.For(record));
    }

    [Fact]
    public void AREUSEONLYPassDoesNOTEraseThatItWasPrepared()
    {
        // «Everything was already prepared» is the ordinary answer and it arrives on
        // the very next build. If it cleared the count, the evidence that Studio ever
        // reduced anything would last until the owner touched the project again.
        var record = new AlbumDrawRecord();
        record.RecordRasterPreparation(26, 0, 0d, Now);
        record.RecordRasterPreparation(0, 0, 0d, Now.AddMinutes(5));

        Assert.Equal(26, record.LastPreparedImageCount);
        Assert.Equal(Now, record.LastPreparedAtUtc);
    }

    [Fact]
    public void THEPreparationSecondsAreSaidSEPARATELYFromTheDrawing()
    {
        // 🔴 ONE FIGURE WOULD MAKE A ONE-OFF COST LOOK PERMANENT. The first build
        // of a set of renders pays for preparation and will never pay again; every
        // build pays for drawing. Added together, «the album took two minutes» cannot
        // be acted on - and the owner's report arrives hours later, when the only
        // other evidence is a file timestamp.
        var record = new AlbumDrawRecord();
        record.Record(drew: true, AlbumRebuildReason.FingerprintChanged.ToString(), Now);
        record.RecordDrawFinished(Now, 96.4d);
        record.RecordRasterPreparation(26, 0, 41.2d, Now);

        string sentence = StudioAlbumDrawSentence.For(record);

        string drew = (96.4d).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
        string prepared = (41.2d).ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);
        Assert.Contains(drew + " секунд", sentence);
        Assert.Contains(prepared + " секунд", sentence);
        Assert.NotEqual(drew, prepared);
    }

    [Fact]
    public void AREUSEONLYPassDoesNotEraseHOWLONGItTook()
    {
        // The seconds are history in exactly the way the count beside them is: the
        // next build reuses everything and has no duration worth reporting.
        var record = new AlbumDrawRecord();
        record.RecordRasterPreparation(26, 0, 41.2d, Now);
        record.RecordRasterPreparation(0, 0, 0d, Now.AddMinutes(5));

        Assert.Equal(41.2d, record.LastPreparedSeconds);
    }

    [Fact]
    public void ACLONEDRecordCarriesTheSecondsToo()
    {
        var record = new AlbumDrawRecord();
        record.RecordRasterPreparation(26, 0, 41.2d, Now);

        Assert.Equal(41.2d, record.Clone().LastPreparedSeconds);
    }

    [Fact]
    public void APROJECTThatNeverPreparedAnythingSaysNOTHING()
    {
        // 🔴 NO LIMB THAT READS «0». Most albums will be built from images already
        // within the rule; a clause on every line would train the owner to stop
        // reading the line that matters.
        var record = new AlbumDrawRecord();
        record.Record(drew: false, AlbumRebuildReason.NothingChanged.ToString(), Now);

        Assert.DoesNotContain("Бэлтгэл", StudioAlbumDrawSentence.For(record));
        Assert.DoesNotContain("эх хэмжээгээр", StudioAlbumDrawSentence.For(record));
    }

    [Fact]
    public void PREPARATIONBeforeAnyAlbumDecisionIsStillShown()
    {
        // Preparation happens inside CreateAlbumBuildProject, which a preview calls
        // without going through the draw decision - so the very first pass can
        // prepare 26 images while DecidedAtUtc is still null. Its own assertion,
        // because the clause is separate from the sweep's and could be moved inside
        // the decision branch by a later edit.
        var record = new AlbumDrawRecord();
        record.RecordRasterPreparation(26, 2, 0d, Now);

        string sentence = StudioAlbumDrawSentence.For(record);

        Assert.Contains(StudioAlbumDrawSentence.NeverDecidedMn, sentence);
        Assert.Contains("26", sentence);
        Assert.Contains("эх хэмжээгээр", sentence);
    }

    [Fact]
    public void ACLONEDRecordCarriesThePreparationToo()
    {
        // The record is cloned into build snapshots, and a field missing from Clone
        // is a field that exists in the file and vanishes wherever it is read from a
        // copy. Asserted through the sentence, so a field added later without a clone
        // line still has to answer.
        var record = new AlbumDrawRecord();
        record.Record(drew: true, AlbumRebuildReason.FingerprintChanged.ToString(), Now);
        record.RecordRasterPreparation(26, 3, 0d, Now);

        AlbumDrawRecord copy = record.Clone();

        Assert.Equal(StudioAlbumDrawSentence.For(record), StudioAlbumDrawSentence.For(copy));
        Assert.Equal(record.LastPreparedAtUtc, copy.LastPreparedAtUtc);
        Assert.Equal(3, copy.LastUnpreparedImageCount);
    }

    [Fact]
    public void THESWEEPAndThePREPARATIONBothFitOnTheOneLine()
    {
        // 🔴 TWO THINGS HAPPEN ON A RECONCILING BUILD AND THE OWNER READS ONE LINE.
        // Either clause written as a replacement rather than an addition would hide
        // the other, and which one got hidden would depend on the order they were
        // added in.
        var record = new AlbumDrawRecord();
        record.Record(drew: true, AlbumRebuildReason.LinkedSourceMoved.ToString(), Now);
        record.RecordRasterPreparation(26, 0, 0d, Now);
        record.RecordStoreSweep(4, 120L * 1024 * 1024, "", Now);

        string sentence = StudioAlbumDrawSentence.For(record);

        Assert.Contains("Бэлтгэл", sentence);
        Assert.Contains("Цэвэрлэгээ", sentence);
        Assert.Contains("120 МБ", sentence);
        Assert.Contains("дахин зурагдсан", sentence);
    }

    private static DateTimeOffset Now { get; } =
        new(2026, 9, 12, 14, 30, 0, TimeSpan.Zero);
}
