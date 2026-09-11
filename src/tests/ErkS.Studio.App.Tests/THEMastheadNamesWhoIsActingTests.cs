using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The top of the window says who the work is going out as.
///
/// 🔴 THE OWNER WORKED FOR HOURS BELIEVING THEY WERE THEMSELVES. The masthead read
/// the same in both states and the account button carried THEIR name, while a
/// status line below said the machine was in bot state and the server refused
/// them as a seat. They asked for it directly - «дээд талын логоны ард бот уу?
/// үндсэн хэрэглэгч үү гэдгийг ялгадаг болгочих» - and stated the rule behind it:
/// «бот төлөвт байгаа юм бол бот шиг харагдах ёстой. гарсан бол гарсан.»
/// </summary>
public sealed class THEMastheadNamesWhoIsActingTests
{
    [Fact]
    public void ASEATEDMachineShowsTheBOTSNameAndItsMark()
    {
        StudioActingIdentity acting = StudioActingIdentityBadge.Of(
            seatedAsBot: true,
            botDisplayName: "Зураг оруулагч",
            ownerDisplayName: "Энхбаатар Мөнххолбоо");

        Assert.True(acting.IsBot);
        Assert.Equal("Зураг оруулагч", acting.Name);
        Assert.Equal(StudioActingIdentityBadge.BotMark, acting.Mark);
    }

    [Fact]
    public void ASEATEDMachineNEVERShowsTheOwnersName()
    {
        // 🔴 THE EXACT CONFUSION THAT COST THE DAY. An owner can be signed in on a
        // seated machine - that is what the passport entry is for - and the name
        // in the corner still belongs to whoever the work goes out as.
        StudioActingIdentity acting = StudioActingIdentityBadge.Of(
            seatedAsBot: true,
            botDisplayName: "Зураг оруулагч",
            ownerDisplayName: "Энхбаатар Мөнххолбоо");

        Assert.DoesNotContain("Энхбаатар", acting.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void ANUNSEATEDMachineShowsThePERSONAndNoMark()
    {
        StudioActingIdentity acting = StudioActingIdentityBadge.Of(
            seatedAsBot: false,
            botDisplayName: "Зураг оруулагч",
            ownerDisplayName: "Энхбаатар Мөнххолбоо");

        Assert.False(acting.IsBot);
        Assert.Equal("Энхбаатар Мөнххолбоо", acting.Name);
        Assert.Equal("", acting.Mark);
    }

    [Fact]
    public void ALEFTOVERBotNameIsNotShownOnceTheMachineIsOut()
    {
        // «гарсан бол гарсан» - there is no mixed state. A name left over from the
        // seat must not appear beside the person who signed in.
        StudioActingIdentity acting = StudioActingIdentityBadge.Of(
            seatedAsBot: false,
            botDisplayName: "Зураг оруулагч",
            ownerDisplayName: "Энхбаатар Мөнххолбоо");

        Assert.DoesNotContain("Зураг", acting.Name, StringComparison.Ordinal);
        Assert.Empty(acting.Mark);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ANAMEThatIsMissingIsNAMEDAsMissing(string? nothing)
    {
        // 🔴 A BLANK READS AS «NOBODY», WHICH IS A DIFFERENT AND WRONG ANSWER. The
        // owner's rule on identity is that a missing name is a gap to close, and
        // filling it with an address is not closing it - so it is said as what it
        // is, in both states.
        Assert.Equal(
            StudioActingIdentityBadge.Unknown,
            StudioActingIdentityBadge.Of(true, nothing, "Эзэн").Name);
        Assert.Equal(
            StudioActingIdentityBadge.Unknown,
            StudioActingIdentityBadge.Of(false, "Бот", nothing).Name);
    }

    [Fact]
    public void THEMastheadAndTheAccountLineReadTheSAMEValue()
    {
        // 🔴 THEY DISAGREED, AND THAT IS THE WHOLE DEFECT. The account line
        // already knew who was acting; the masthead said «CLOUD ERA» in every
        // state. Two answers to one question is how a person ends up trusting
        // the wrong one - and the corner won.
        string body = MethodBody(ReadAppSource("ShellView.cs"), "private void UpdateAccountUi()");

        int kind = body.IndexOf("StudioAccountIdentityLine.For(", StringComparison.Ordinal);
        int masthead = body.IndexOf("StudioActingIdentityBadge.Of(", StringComparison.Ordinal);
        Assert.True(kind > 0, "the account line no longer resolves an identity kind");
        Assert.True(masthead > kind, "the masthead must be driven by that same value");
        Assert.Contains("mastheadIdentityText.Text = acting.IsBot", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEMastheadStopsSayingCLOUDERAOnASeatedMachine()
    {
        // The branch that matters: a machine acting as a seat must not show the
        // neutral wording that hid the state for hours.
        string body = MethodBody(ReadAppSource("ShellView.cs"), "private void UpdateAccountUi()");
        int at = body.IndexOf("mastheadIdentityText.Text = acting.IsBot", StringComparison.Ordinal);
        Assert.True(at > 0, "the masthead is no longer set from the acting identity");

        string branch = body[at..body.IndexOf(";", at, StringComparison.Ordinal)];
        Assert.Contains("acting.Mark", branch, StringComparison.Ordinal);
        Assert.Contains("acting.Name", branch, StringComparison.Ordinal);
    }

    [Fact]
    public void THEMarkIsShortEnoughToSitInACorner()
    {
        // It shares the masthead with the product name; a sentence there would
        // push the thing it qualifies off the edge.
        Assert.True(
            StudioActingIdentityBadge.BotMark.Length <= 6,
            "the bot mark has grown into a sentence: " + StudioActingIdentityBadge.BotMark);
    }

    [Fact]
    public void NOSeatSurfacePrintsANADDRESSWhereANameBelongs()
    {
        // 🔴 DERIVED OVER THE SURFACES, NOT PINNED TO ONE LINE. The address
        // fallback was in three places and a mutation found the one with no test:
        // the seated machine's own status line still printed AccountEmail after
        // the other two were fixed. The owner's rule is about the QUESTION - «what
        // is this person called» - so it is asked of every place that answers it.
        //
        // The server now sends an empty name rather than an address for accounts
        // that have none, so these branches fire more often than they used to.
        string[] surfaces =
        [
            "ShellView.BotSeat.cs",
            "BotSeatDialogs.cs",
            "ShellView.cs",
        ];

        var checkedLines = 0;
        foreach (string file in surfaces)
        {
            string source = ReadAppSource(file).Replace("\r\n", "\n");
            for (int at = source.IndexOf("гишүүн: ", StringComparison.Ordinal);
                at >= 0;
                at = source.IndexOf("гишүүн: ", at + 1, StringComparison.Ordinal))
            {
                checkedLines++;
                int stop = source.IndexOf(";", at, StringComparison.Ordinal);
                string sentence = stop > at ? source[at..stop] : source[at..];
                Assert.DoesNotContain("Email", sentence, StringComparison.Ordinal);
            }
        }

        // The instrument: a scan that found no such sentence proves nothing.
        Assert.True(checkedLines >= 2, "the scan found only " + checkedLines + " member sentences");
    }

    private static string MethodBody(string source, string signature)
    {
        string normalised = source.Replace("\r\n", "\n");
        int start = normalised.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = normalised.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return normalised[start..end];
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, System.Text.Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
