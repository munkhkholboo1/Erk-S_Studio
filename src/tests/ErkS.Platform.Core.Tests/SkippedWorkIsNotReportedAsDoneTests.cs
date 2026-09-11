using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// A step that did not run must not report a number.
///
/// 🔴 THIS COST A PERSON A MORNING, AND THE LINE WAS WORSE THAN WRONG. Reading
/// every source package takes five and a half seconds, so a cheap survey decides
/// whether the expensive read is worth running. When it said no, the read was
/// skipped - and the report was built from
/// <c>SourceRefreshOutcome.Completed(Sources.Count, 0)</c>, producing
/// «Эх үүсвэр: 3 шалгав, 0 өөрчлөгдсөн».
///
/// The owner had changed exactly three sources. The project had exactly three.
/// The number matched by coincidence, so the sentence did not merely misreport -
/// it CONFIRMED what they already believed: that Studio had looked at their
/// three files and found nothing in them. They worked on that assumption while
/// their changes sat unsent.
/// </summary>
public sealed class SkippedWorkIsNotReportedAsDoneTests
{
    [Fact]
    public void ASKIPPEDSourceReadNeverSaysItCHECKEDAnything()
    {
        // 🔴 THE ASSERTION THE MISSING ONE WOULD HAVE BEEN. «шалгав» is the
        // claim that work happened; a step that was skipped may not make it.
        AlbumRefreshStepResult skipped = AlbumRefreshReport.SourcesNotChecked(
            "хүлээгдэж буй шинэ багц алга тул уншаагүй.");

        Assert.Equal(AlbumRefreshStepOutcome.Skipped, skipped.Outcome);
        Assert.DoesNotContain("шалгав", skipped.DetailMn, StringComparison.Ordinal);

        // And it says what to do next, because «nothing happened» without a
        // reason is what sent somebody looking at their own files.
        Assert.Contains("уншаагүй", skipped.DetailMn, StringComparison.Ordinal);
    }

    [Fact]
    public void ASKIPPEDStepCarriesNOCount()
    {
        // The factory takes no number at all. A count that cannot be supplied
        // cannot be mistaken for one that was measured - the fix is the
        // signature, not the wording.
        AlbumRefreshStepResult skipped =
            AlbumRefreshReport.SourcesNotChecked("ямар ч шалтгаанаар.");

        foreach (char digit in "0123456789")
            Assert.DoesNotContain(digit.ToString(), skipped.DetailMn, StringComparison.Ordinal);
    }

    [Fact]
    public void AREADThatRANStillCarriesItsNumbers()
    {
        // The positive control. «No numbers on a skipped step» is also true of a
        // report that never counts anything, and the counted case is the one the
        // person reads on every ordinary press.
        AlbumRefreshStepResult read = AlbumRefreshReport.SourcesRead(3, 0);

        Assert.Equal(AlbumRefreshStepOutcome.NothingToDo, read.Outcome);
        Assert.Contains("3 шалгав", read.DetailMn, StringComparison.Ordinal);
        Assert.Contains("0 өөрчлөгдсөн", read.DetailMn, StringComparison.Ordinal);
    }

    [Fact]
    public void ALOCALProjectIsToldItHasNOCloudRatherThanAnUnchangedOne()
    {
        // 🔴 THE SAME SHAPE, TWICE MORE. An unlinked project reported «Таны
        // оруулга: өгөх шинэ хэсэг байсангүй» and «Үүлэн альбом: өөрчлөгдөөгүй,
        // дахин татаагүй» - both describing attempts against a cloud that does
        // not exist for this project. Found by sweeping the other three report
        // lines for the same defect rather than fixing the one that was
        // reported.
        AlbumRefreshStepResult contribution = AlbumRefreshReport.SkippedForLocalProject(
            AlbumRefreshStep.SendOwnContribution);
        AlbumRefreshStepResult cloud = AlbumRefreshReport.SkippedForLocalProject(
            AlbumRefreshStep.FetchCloudUpdates);

        Assert.Equal(AlbumRefreshStepOutcome.Skipped, contribution.Outcome);
        Assert.Equal(AlbumRefreshStepOutcome.Skipped, cloud.Outcome);
        Assert.Contains("холбогдоогүй", contribution.DetailMn, StringComparison.Ordinal);
        Assert.Contains("холбогдоогүй", cloud.DetailMn, StringComparison.Ordinal);

        // Neither claims the cloud was consulted and found unchanged.
        Assert.DoesNotContain("өөрчлөгдөөгүй", cloud.DetailMn, StringComparison.Ordinal);
    }

    [Fact]
    public void SKIPPEDIsItsOWNOutcomeAndNotFoldedIntoNOTHINGTODO()
    {
        // «Not attempted» and «attempted, nothing to do» lead a person to
        // different next actions, which is the only thing this report is for.
        // Folding one into the other is what produced the defect.
        Assert.NotEqual(AlbumRefreshStepOutcome.NothingToDo, AlbumRefreshStepOutcome.Skipped);
        Assert.NotEqual(AlbumRefreshStepOutcome.NotReached, AlbumRefreshStepOutcome.Skipped);
        Assert.NotEqual(AlbumRefreshStepOutcome.Done, AlbumRefreshStepOutcome.Skipped);
    }
}
