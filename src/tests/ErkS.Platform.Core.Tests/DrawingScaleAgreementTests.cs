namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Checking a drawing against the scale its slot asks for.
///
/// The reference album says a sheet is М1:1500; the page carries whatever it
/// was plotted at. Without the requirement recorded on the slot there is
/// nothing to compare against, and a sheet returned at the wrong scale is the
/// one nobody catches.
///
/// The trap this was written around: a slot spells it «М1:1500» while a page's
/// scale has already been normalised to «1:1500». Compared as they come, they
/// disagree on every sheet - and a warning that fires on everything is read as
/// noise and then not read at all.
/// </summary>
public sealed class DrawingScaleAgreementTests
{
    [Theory]
    [InlineData("М1:1500", "1:1500")]
    [InlineData("М1:1500", "М1:1500")]
    [InlineData("M1:1500", "1:1500")]
    [InlineData("М1:1500", " 1 : 1500 ")]
    [InlineData("М1:1500", "1/1500")]
    public void TheSameScaleWrittenDifferentWaysStillAgrees(string required, string actual)
    {
        Assert.True(DrawingScaleAgreement.Agrees(required, actual));
        Assert.Null(DrawingScaleAgreement.Describe("ЕТ-03", required, actual));
    }

    [Fact]
    public void ADrawingAtTheWrongScaleIsReported()
    {
        // The whole reason the requirement is recorded.
        string? notice = DrawingScaleAgreement.Describe("ЕТ-03", "М1:1500", "1:500");

        Assert.NotNull(notice);
        Assert.Contains("ЕТ-03", notice);
        Assert.Contains("1:500", notice);
        Assert.Contains("М1:1500", notice);
    }

    [Theory]
    [InlineData("", "1:500")]
    [InlineData(null, "1:500")]
    public void ASlotThatPrescribesNothingAsksNothing(string? required, string actual)
    {
        // Most slots in the two older templates prescribe no scale, and the
        // standard they follow does not fix one.
        Assert.True(DrawingScaleAgreement.Agrees(required, actual));
    }

    [Theory]
    [InlineData("М1:1500", "")]
    [InlineData("М1:1500", null)]
    public void APageThatStatesNothingIsNotAccused(string required, string? actual)
    {
        // A drawing without a scale in its title block is ordinary. Treating
        // silence as a mismatch would accuse the majority of sheets.
        Assert.True(DrawingScaleAgreement.Agrees(required, actual));
    }

    [Fact]
    public void NeitherSideSpeakingIsNotAProblem()
    {
        Assert.True(DrawingScaleAgreement.Agrees("", ""));
    }

    [Fact]
    public void TheNoticeNamesTheSheetSoItCanBeFound()
    {
        // A count would say a set is wrong without saying which sheet.
        Assert.Contains("ЕТ-09", DrawingScaleAgreement.Describe("ЕТ-09", "М1:16000", "1:1500")!);
    }

    [Fact]
    public void ASheetWithNoNumberStillGetsANotice()
    {
        string? notice = DrawingScaleAgreement.Describe("", "М1:1500", "1:500");

        Assert.NotNull(notice);
        Assert.StartsWith("Хуудас", notice);
    }
}
