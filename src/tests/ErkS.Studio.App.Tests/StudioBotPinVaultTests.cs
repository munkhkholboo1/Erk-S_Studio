using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioBotPinVaultTests
{
    private const string Bot = "bot_7f3a91c4e85b4d2f";

    [Fact]
    public void TheRightPinOpensTheCredential()
    {
        StudioBotPinVault.SealedBotCredential sealedCredential =
            StudioBotPinVault.Seal(Bot, "0000", "bot-token-value");

        Assert.Equal("bot-token-value", StudioBotPinVault.TryOpen(Bot, "0000", sealedCredential));
    }

    [Fact]
    public void SimplePinsAreAccepted()
    {
        // Standing instruction, not an omission: refusing 0000 and 1234 is the
        // reflex the user ruled out. A named test so tightening it means
        // deleting an assertion rather than adding a condition.
        Assert.True(StudioBotPinVault.IsWellFormedPin("0000"));
        Assert.True(StudioBotPinVault.IsWellFormedPin("1234"));
        Assert.False(StudioBotPinVault.IsWellFormedPin("123"));
        Assert.False(StudioBotPinVault.IsWellFormedPin("12a4"));
        Assert.False(StudioBotPinVault.IsWellFormedPin(null));
    }

    [Fact]
    public void AWrongPinReturnsNothingRatherThanThrowing()
    {
        StudioBotPinVault.SealedBotCredential sealedCredential =
            StudioBotPinVault.Seal(Bot, "1234", "bot-token-value");

        Assert.Null(StudioBotPinVault.TryOpen(Bot, "1235", sealedCredential));
    }

    [Fact]
    public void ARecordPointedAtAnotherSeatDoesNotOpen()
    {
        // The bot id is authenticated alongside the payload, so editing the
        // record to name another seat fails to open instead of handing over
        // the wrong seat's credential.
        StudioBotPinVault.SealedBotCredential sealedCredential =
            StudioBotPinVault.Seal(Bot, "1234", "bot-token-value");

        Assert.Null(StudioBotPinVault.TryOpen("bot_c21e0a9b7d4f6e31", "1234", sealedCredential));
    }

    [Fact]
    public void TwoSeatsSharingAPinDoNotLookAlikeOnDisk()
    {
        // With ten thousand possible values, two identical blobs would be most
        // of the way to guessing the PIN.
        StudioBotPinVault.SealedBotCredential first =
            StudioBotPinVault.Seal(Bot, "1234", "same-credential");
        StudioBotPinVault.SealedBotCredential second =
            StudioBotPinVault.Seal(Bot, "1234", "same-credential");

        Assert.NotEqual(first.Blob, second.Blob);
    }

    [Fact]
    public void ATamperedBlobDoesNotOpen()
    {
        StudioBotPinVault.SealedBotCredential sealedCredential =
            StudioBotPinVault.Seal(Bot, "1234", "bot-token-value");
        byte[] tampered = [.. sealedCredential.Blob];
        tampered[^1] ^= 0xFF;

        Assert.Null(StudioBotPinVault.TryOpen(
            Bot,
            "1234",
            new StudioBotPinVault.SealedBotCredential(tampered)));
    }
}
