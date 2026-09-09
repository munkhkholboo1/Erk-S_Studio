using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A queued chore must not take the place of the answer.
///
/// 🔴 THE FLUSH SET THE STATUS LINE ITSELF, and it runs at every owner sign-in.
/// So «Эзэмшигчээр баталгаажлаа» appeared and was replaced in the same instant
/// by a bot-seat refusal about a machine that had left bot state days earlier -
/// and the person read the result as «I signed in as the owner and it came up as
/// the bot». On the ordinary sign-in the same collision ran the other way: the
/// refusal was overwritten by the sign-in message four lines later, so a seat
/// this machine could not release was invisible on the one screen whose
/// credential could have released it.
///
/// Neither is dropped now. The flush reports; each caller composes.
/// </summary>
public sealed class BotSeatFlushOutcomeTests
{
    [Fact]
    public void NOTHINGToSayAddsNothing()
    {
        Assert.True(BotSeatFlushOutcome.Nothing.IsSilent);
        Assert.Equal("", BotSeatFlushOutcome.Nothing.Clause());
        Assert.Equal("Нэвтэрлээ.", BotSeatFlushOutcome.Nothing.After("Нэвтэрлээ."));
    }

    [Fact]
    public void THEAnswerComesFIRSTAndTheChoreAfterIt()
    {
        var outcome = new BotSeatFlushOutcome(0, 0, ["«Erk-S» (сервер хариугүй)"]);

        string line = outcome.After("Эзэмшигчээр баталгаажлаа.");

        Assert.StartsWith("Эзэмшигчээр баталгаажлаа.", line, StringComparison.Ordinal);
        Assert.Contains("«Erk-S» (сервер хариугүй)", line, StringComparison.Ordinal);
    }

    [Fact]
    public void ASeatSTILLHeldIsNeverSwallowed()
    {
        // The other half of the same rule. A chore that fails in silence looks
        // exactly like one that worked, and this one holds a seat somebody is
        // paying for.
        var outcome = new BotSeatFlushOutcome(0, 0, ["«Erk-S» (сервер хариугүй)"]);

        Assert.False(outcome.IsSilent);
        Assert.NotEqual("", outcome.Clause());
        Assert.Contains("«Erk-S»", outcome.After(""), StringComparison.Ordinal);
    }

    [Fact]
    public void FREEDNOWAndWASAlreadyFreeAreDifferentSentences()
    {
        // Only the first is something this sign-in did. Saying both the same
        // way turns a confirmation into a claim.
        string released = new BotSeatFlushOutcome(2, 0, []).Clause();
        string alreadyFree = new BotSeatFlushOutcome(0, 2, []).Clause();
        string both = new BotSeatFlushOutcome(1, 1, []).Clause();

        Assert.NotEqual(released, alreadyFree);
        Assert.NotEqual(released, both);
        Assert.NotEqual(alreadyFree, both);
        foreach (string sentence in new[] { released, alreadyFree, both })
            Assert.NotEqual("", sentence);
    }

    [Fact]
    public void AFAILUREOutranksACountThatWentWell()
    {
        // A pass that freed one seat and could not free another has one thing
        // worth a person's attention, and it is not the one that worked.
        var mixed = new BotSeatFlushOutcome(1, 0, ["«Erk-S» (сервер хариугүй)"]);

        Assert.Contains("«Erk-S»", mixed.Clause(), StringComparison.Ordinal);
    }

    [Fact]
    public void EVERYCallerComposesRatherThanLettingTheChoreSpeak()
    {
        // 🔴 THE DEFECT WAS IN THE CALL SITES, so this is where it is held. The
        // flush no longer touches the status line at all; every place that runs
        // it decides what the person reads.
        string botSeat = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(botSeat, "private async Task<BotSeatFlushOutcome> FlushPendingBotSeatReleasesAsync()");

        Assert.DoesNotContain("SetStatus(", body, StringComparison.Ordinal);

        // The owner sign-in on a seated machine: the chore runs BEFORE the
        // sentence, and the sentence is the person's own answer with the chore
        // hung off it.
        string verify = MethodBody(botSeat, "private async Task VerifyOwnerOnSeatedDeviceAsync()");
        int flush = verify.IndexOf("await FlushPendingBotSeatReleasesAsync();", StringComparison.Ordinal);
        int say = verify.IndexOf("SetStatus(flushed.After(", StringComparison.Ordinal);
        Assert.True(flush > 0, "the seated owner sign-in no longer flushes");
        Assert.True(say > flush, "the confirmation must be said AFTER the chore, or the chore replaces it");

        // The ordinary sign-in carries the outcome past three awaits to the
        // sentence at the end, which used to overwrite it.
        string shell = ReadAppSource("ShellView.cs");
        string signIn = MethodBody(shell, "private async Task<bool> EnsureSignedInAsync()");
        Assert.Contains(
            "BotSeatFlushOutcome flushed = await FlushPendingBotSeatReleasesAsync();",
            signIn,
            StringComparison.Ordinal);
        Assert.Contains(
            "SetStatus(flushed.After(\"Cloud ERA бүртгэлээр нэвтэрлээ.\"));",
            signIn,
            StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return source[start..end];
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
