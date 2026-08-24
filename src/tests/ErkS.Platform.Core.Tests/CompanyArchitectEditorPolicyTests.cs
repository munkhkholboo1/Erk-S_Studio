using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Empty means two different things in these boxes, and the difference is the
/// whole point: "nobody is appointed" is an answer, "this device was never
/// told" is not.
/// </summary>
public sealed class CompanyArchitectEditorPolicyTests
{
    [Fact]
    public void TypingANameOnAnUntoldProfileIsAnAppointment()
    {
        CompanyArchitectAppointment decision = CompanyArchitectEditorPolicy.Decide(
            known: false,
            typedTitle: "Ерөнхий архитектор",
            typedName: "Г.Энх-Амар");

        Assert.True(decision.Known);
        Assert.Equal("Г.Энх-Амар", decision.Name);
        Assert.Equal("Ерөнхий архитектор", decision.Title);
    }

    [Fact]
    public void SavingAnUntoldProfileWithBlankBoxesStaysUntold()
    {
        // The boxes are blank because this device does not know, not because
        // the user decided nobody holds the role. Calling it an answer would
        // clear an architect appointed on the website.
        CompanyArchitectAppointment decision = CompanyArchitectEditorPolicy.Decide(
            known: false,
            typedTitle: "",
            typedName: "");

        Assert.False(decision.Known);
    }

    [Fact]
    public void ClearingTheBoxesOnAKnownProfileIsAnAnswer()
    {
        // Here blank does mean nobody, and the server should be told so.
        CompanyArchitectAppointment decision = CompanyArchitectEditorPolicy.Decide(
            known: true,
            typedTitle: "",
            typedName: "");

        Assert.True(decision.Known);
        Assert.Equal("", decision.Name);
    }

    [Fact]
    public void WhitespaceIsNotAnAppointment()
    {
        CompanyArchitectAppointment decision = CompanyArchitectEditorPolicy.Decide(
            known: false,
            typedTitle: "   ",
            typedName: "\t");

        Assert.False(decision.Known);
    }

    [Fact]
    public void ATitleWithoutANameStillCountsAsSomeoneAnswering()
    {
        // Half-filled is a person mid-edit, not silence. Treating it as
        // silence would drop what they typed on the next save.
        CompanyArchitectAppointment decision = CompanyArchitectEditorPolicy.Decide(
            known: false,
            typedTitle: "Ерөнхий архитектор",
            typedName: "");

        Assert.True(decision.Known);
        Assert.Equal("Ерөнхий архитектор", decision.Title);
    }

    [Fact]
    public void AnArchitectMatchingTheDirectorIsNotTreatedAsResidue()
    {
        // One person can hold both roles. The pre-split residue also makes the
        // two equal, but guessing from the shape would delete a real
        // appointment, so the policy never looks at the director at all.
        CompanyArchitectAppointment decision = CompanyArchitectEditorPolicy.Decide(
            known: true,
            typedTitle: "Захирал",
            typedName: "О.Очир-Эрдэнэ");

        Assert.True(decision.Known);
        Assert.Equal("О.Очир-Эрдэнэ", decision.Name);
    }

    [Fact]
    public void TheHintSaysWhichOfTheTwoEmptiesThisIs()
    {
        string untold = CompanyArchitectEditorPolicy.Explain(known: false, storedName: "");
        string nobody = CompanyArchitectEditorPolicy.Explain(known: true, storedName: "");
        string appointed = CompanyArchitectEditorPolicy.Explain(known: true, storedName: "Г.Энх-Амар");

        Assert.NotEqual(untold, nobody);
        Assert.NotEqual(nobody, appointed);
        Assert.Contains("мэдэхгүй", untold);
        Assert.Contains("томилогдоогүй", nobody);
    }

    [Fact]
    public void TheHintDoesNotPromiseTheAlbumWillUseThisName()
    {
        // The album's architect line comes from the project team appointment,
        // not from this field, which nothing in the renderer reads. An earlier
        // draft of this text said the name would appear in the album - a
        // promise the software does not keep.
        string appointed = CompanyArchitectEditorPolicy.Explain(
            known: true,
            storedName: "Г.Энх-Амар");

        Assert.Contains("төслийн багаас", appointed);
        Assert.Contains("автоматаар орохгүй", appointed);
    }
}
