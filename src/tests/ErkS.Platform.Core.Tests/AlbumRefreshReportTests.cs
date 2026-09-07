using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The report's only job is to be impossible to misread. These tests are aimed
/// at the sentences a person actually acts on.
/// </summary>
public sealed class AlbumRefreshReportTests
{
    [Fact]
    public void AFAILEDFetchCannotProduceAFinishedSentence()
    {
        // 🔴 THE ONE THAT MATTERS. The old behaviour a person could hit: press
        // sync, have the fetch fail, and be told the album was up to date. The
        // closing line is DERIVED, so no call site can write "дууслаа" over it.
        AlbumRefreshReport report = AlbumRefreshReport.Create(
            [
                AlbumRefreshReport.SourcesRead(12, 3),
                AlbumRefreshReport.ContributionSent(3),
                AlbumRefreshReport.CloudFetchFailed("сүлжээ алга"),
                AlbumRefreshReport.PagesRecomposed(40, 0),
            ],
            AlbumOnScreen.LocalPreview);

        Assert.False(report.IsComplete);
        Assert.DoesNotContain("дууслаа", report.ClosingLineMn, StringComparison.Ordinal);
        Assert.Contains("татаж чадсангүй", report.ClosingLineMn, StringComparison.Ordinal);
    }

    [Fact]
    public void APartialRunNamesBOTHHalvesInOneSentence()
    {
        // Master's exact shape: «таны оруулга өгөгдсөн, бусдынхыг татаж
        // чадсангүй». Reporting only the failure erases work that really did
        // reach the cloud, and the person re-sends it.
        AlbumRefreshReport report = AlbumRefreshReport.Create(
            [
                AlbumRefreshReport.SourcesRead(12, 3),
                AlbumRefreshReport.ContributionSent(3),
                AlbumRefreshReport.CloudFetchFailed("сүлжээ алга"),
                AlbumRefreshReport.PagesRecomposed(40, 0),
            ],
            AlbumOnScreen.LocalPreview);

        Assert.Contains("өгөгдсөн", report.ClosingLineMn, StringComparison.Ordinal);
        Assert.Contains("татаж чадсангүй", report.ClosingLineMn, StringComparison.Ordinal);
    }

    [Fact]
    public void STOPPINGAtStepOneMustNOTClaimAnythingWasSent()
    {
        // Steps 1-2 failing means nothing left this machine. A report that
        // still mentions sending would send the person looking for their
        // contribution in the cloud.
        AlbumRefreshReport report = AlbumRefreshReport.Create(
            [AlbumRefreshReport.SourcesFailed("файл нээгдсэнгүй")],
            AlbumOnScreen.LocalPreview);

        Assert.True(report.StoppedEarly);
        Assert.Equal(
            AlbumRefreshStepOutcome.NotReached,
            report.OutcomeOf(AlbumRefreshStep.SendOwnContribution));
        Assert.Contains("юу ч илгээгээгүй", report.ClosingLineMn, StringComparison.Ordinal);
    }

    [Fact]
    public void AMISSINGStepIsRecordedAsNOTREACHEDRatherThanOmitted()
    {
        // 🔴 ABSENCE MUST NOT READ AS "NOTHING TO DO". A step left out of the
        // list would simply not print, and a reader counting four lines would
        // find three and assume the fourth had no work. Every step always has a
        // line.
        AlbumRefreshReport report = AlbumRefreshReport.Create(
            [AlbumRefreshReport.SourcesRead(5, 0)],
            AlbumOnScreen.None);

        Assert.Equal(4, report.Steps.Count);
        Assert.All(
            report.Steps.Where(step => step.Step != AlbumRefreshStep.ReadOwnSources),
            step => Assert.Equal(AlbumRefreshStepOutcome.NotReached, step.Outcome));
        Assert.All(report.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step.DetailMn)));
    }

    [Fact]
    public void NOTHINGTODOIsNotTheSameAsNOTREACHED()
    {
        // Both did no work; only one is a problem. A run where there was
        // genuinely nothing to send is COMPLETE.
        AlbumRefreshReport complete = AlbumRefreshReport.Create(
            [
                AlbumRefreshReport.SourcesRead(12, 0),
                AlbumRefreshReport.ContributionSent(0),
                AlbumRefreshReport.CloudFetched(false, ""),
                AlbumRefreshReport.PagesRecomposed(40, 0),
            ],
            AlbumOnScreen.CloudCanonical);

        Assert.True(complete.IsComplete);
        Assert.False(complete.StoppedEarly);
        Assert.Contains("дууслаа", complete.ClosingLineMn, StringComparison.Ordinal);
    }

    [Fact]
    public void THEUnchangedOrderStillCarriesITSNUMBERS()
    {
        // "Дараалал өөрчлөгдсөнгүй" with no number is indistinguishable from a
        // step that never ran - and a stale page order surviving unnoticed is
        // exactly the defect a user reported today.
        string line = AlbumRefreshReport.PagesRecomposed(40, 0).DetailMn;

        Assert.Contains("40", line, StringComparison.Ordinal);
        Assert.Contains("шалгав", line, StringComparison.Ordinal);
    }

    [Fact]
    public void THEReportAlwaysSaysWHICHAlbumIsOnScreen()
    {
        // The screen can show this device's own build or the server's assembly,
        // and they differ exactly when something is wrong or still in flight.
        foreach (AlbumOnScreen shown in new[]
        {
            AlbumOnScreen.None,
            AlbumOnScreen.LocalPreview,
            AlbumOnScreen.CloudCanonical,
        })
        {
            AlbumRefreshReport report = AlbumRefreshReport.Create(
                [AlbumRefreshReport.SourcesRead(1, 0)],
                shown);

            Assert.False(string.IsNullOrWhiteSpace(report.AlbumOnScreenLineMn));
            Assert.Contains(report.AlbumOnScreenLineMn, report.ComposeMn(), StringComparison.Ordinal);
        }

        Assert.NotEqual(
            AlbumRefreshReport.Create([], AlbumOnScreen.LocalPreview).AlbumOnScreenLineMn,
            AlbumRefreshReport.Create([], AlbumOnScreen.CloudCanonical).AlbumOnScreenLineMn);
    }

    [Fact]
    public void EVERYStepPrintsALineInTheComposedReport()
    {
        AlbumRefreshReport report = AlbumRefreshReport.Create(
            [
                AlbumRefreshReport.SourcesRead(12, 3),
                AlbumRefreshReport.ContributionSent(3),
                AlbumRefreshReport.CloudFetched(true, "r-42"),
                AlbumRefreshReport.PagesRecomposed(40, 6),
            ],
            AlbumOnScreen.CloudCanonical);

        string text = report.ComposeMn();

        Assert.Contains("12", text, StringComparison.Ordinal);
        Assert.Contains("r-42", text, StringComparison.Ordinal);
        Assert.Contains("6", text, StringComparison.Ordinal);
        Assert.Contains(report.ClosingLineMn, text, StringComparison.Ordinal);
    }

    [Fact]
    public void ARunThatDidEVERYTHINGIsTheONLYWayToReachComplete()
    {
        // The positive control for IsComplete: it must be reachable, or every
        // "not complete" assertion above is vacuous.
        Assert.True(AlbumRefreshReport.Create(
            [
                AlbumRefreshReport.SourcesRead(12, 3),
                AlbumRefreshReport.ContributionSent(3),
                AlbumRefreshReport.CloudFetched(true, "r-42"),
                AlbumRefreshReport.PagesRecomposed(40, 6),
            ],
            AlbumOnScreen.CloudCanonical).IsComplete);

        // ...and a single failure anywhere takes it away.
        foreach (AlbumRefreshStepResult failure in new[]
        {
            AlbumRefreshReport.SourcesFailed("x"),
            AlbumRefreshReport.ContributionFailed("x"),
            AlbumRefreshReport.CloudFetchFailed("x"),
            AlbumRefreshReport.RecomposeFailed("x"),
        })
        {
            var steps = new List<AlbumRefreshStepResult>
            {
                AlbumRefreshReport.SourcesRead(12, 3),
                AlbumRefreshReport.ContributionSent(3),
                AlbumRefreshReport.CloudFetched(true, "r-42"),
                AlbumRefreshReport.PagesRecomposed(40, 6),
            };
            steps.RemoveAll(step => step.Step == failure.Step);
            steps.Add(failure);

            AlbumRefreshReport report = AlbumRefreshReport.Create(steps, AlbumOnScreen.CloudCanonical);
            Assert.False(report.IsComplete, failure.Step + " must break completeness");
            Assert.DoesNotContain("дууслаа", report.ClosingLineMn, StringComparison.Ordinal);
        }
    }
}
