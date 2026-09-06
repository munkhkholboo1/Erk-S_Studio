using System.Net;
using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Getting a seat credential from nothing but the device's own key.
///
/// The gap both sides measured on 2026-09-05: the renewal route proves
/// possession of a token, and a machine that has just started holds none. So a
/// seated device could never get its first credential, and the release dialog
/// had to be reworded to stop promising something no code did. The routes went
/// live on 2026-09-07 and these are the three things that were waiting.
/// </summary>
[Collection(StudioDeviceIdentityCollection.Name)]
public sealed class StudioBotSessionIssueTests
{
    [Fact]
    public void THERetryRuleCannotBeGotWrongBecauseTheNonceNeverESCAPES()
    {
        // ⚠️ SRV destroys the nonce BEFORE deciding anything, so a 401 or 403
        // can never be retried with the same one - a retry has to start at
        // /challenge. That is a rule somebody has to remember only if the two
        // steps can be called separately.
        //
        // They cannot: there is one public method, it does both, and the nonce
        // is a local inside it. Checked on the source, because the guarantee is
        // the ABSENCE of a second entry point and no call can demonstrate that.
        string source = ReadAppSource("StudioAccountService.cs");

        Assert.Contains("public async Task<StudioCloudBotStateToken> IssueBotSessionAsync(", source, StringComparison.Ordinal);
        Assert.Contains("bot-state/challenge", source, StringComparison.Ordinal);
        Assert.Contains("bot-state/session", source, StringComparison.Ordinal);

        // No public way to hold a nonce and spend it later.
        Assert.DoesNotContain("public async Task<StudioCloudBotSessionChallenge>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("public StudioCloudBotSessionChallenge", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NEITHERStepCarriesAnAuthorizationHeader()
    {
        // The signature IS the credential. A header here would be a credential
        // the machine does not have - that is the whole situation being solved -
        // and adding one "just in case" would make the route look authenticated
        // to the next reader.
        string source = ReadAppSource("StudioAccountService.cs");
        int start = source.IndexOf("private async Task<TResponse> PostUnauthenticatedAsync<", StringComparison.Ordinal);
        Assert.True(start > 0, "the unauthenticated post helper was not found");
        string helper = source[start..(start + 900)];

        Assert.DoesNotContain("Authorization", helper, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", helper, StringComparison.Ordinal);
    }

    [Fact]
    public void WhatIsNOTSentIsAsMuchOfTheDesignAsWhatIs()
    {
        // The public key is not sent - the server has it from registration, and
        // accepting one would let a caller present a key of their own choosing,
        // after which the signature proves nothing.
        //
        // The bot id is not sent either: the device proves which MACHINE it is,
        // and which seat that machine holds is the server's own record. Nothing
        // the caller writes decides anything.
        string contracts = ReadAppSource("StudioCloudContracts.cs");
        int start = contracts.IndexOf("class StudioCloudBotSessionRequest", StringComparison.Ordinal);
        Assert.True(start > 0, "the session request DTO was not found");
        // Sliced to the NEXT type rather than to a brace. Cutting at the first
        // '}' stops inside `{ get; set; }` on the first property, which makes
        // every DoesNotContain below pass for the wrong reason; cutting at a
        // newline-plus-brace depends on the file's line endings, which are not
        // the same in every file in this repository.
        int end = contracts.IndexOf("internal sealed class", start + 10, StringComparison.Ordinal);
        string dto = end > start ? contracts[start..end] : contracts[start..];

        Assert.Contains("DeviceFingerprint", dto, StringComparison.Ordinal);
        Assert.Contains("Nonce", dto, StringComparison.Ordinal);
        Assert.Contains("Signature", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("PublicKey", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("BotId", dto, StringComparison.Ordinal);
        Assert.DoesNotContain("Pin", dto, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("YWJjZA==")]
    [InlineData("YWJjZA")]
    [InlineData("-_-_")]
    public void THENonceIsReadInEitherBase64Spelling(string nonce)
    {
        // base64url and base64 differ in two characters and the padding. A
        // reader that took only one of them would fail with a message about the
        // challenge being unreadable - true, and useless for finding out why.
        byte[] bytes = StudioAccountService.DecodeChallengeNonce(nonce);

        Assert.NotEmpty(bytes);
    }

    [Fact]
    public void ANonceThatIsMISSINGIsRefusedRatherThanSignedAsEmpty()
    {
        // Signing an empty payload would produce a valid signature over nothing
        // and the server would refuse it as a bad signature - sending the reader
        // to look at the key.
        Assert.Throws<FormatException>(() => StudioAccountService.DecodeChallengeNonce(""));
        Assert.Throws<FormatException>(() => StudioAccountService.DecodeChallengeNonce(null));
    }

    [Fact]
    public void ASEATEndingIsToldApartFromEveryOtherWayACallCanFail()
    {
        // 🔴 THIS PREDICATE DESTROYS LOCAL STATE, so it must not fire on an
        // ordinary failure. A token expiry and a bad signature are both 403 and
        // both leave the seat exactly where it was; wiping it would strand a
        // machine whose owner did nothing.
        Assert.True(BotSeatErrors.SeatIsGone(
            Server(HttpStatusCode.Conflict, "bot_state_released_remotely", "Эзэмшигч чөлөөлсөн.")));
        Assert.True(BotSeatErrors.SeatIsGone(
            Server(HttpStatusCode.NotFound, "bot_state_not_found", "Суудал алга.")));

        // 🔴 THE ONE A CODE-LIST WRITTEN FROM MEMORY MISSES. It lives under
        // `bot_session_*`, not `bot_state_*`, and comes from the token layer
        // rather than the seat layer - so enumerating the state codes leaves a
        // seat change looking like an ordinary failure, and the machine keeps a
        // seat the server has already ended.
        Assert.True(BotSeatErrors.SeatIsGone(
            Server(HttpStatusCode.Conflict, "bot_session_seat_changed", "Суудал өөр болсон.")));

        // 🔴 THE FIFTH, AND THE ONE MY OWN NOTE GOT WRONG. A note said all three
        // ways a seat ends arrive as bot_state_released_remotely; measured in
        // SRV's source, a DELETED seat arrives as bot_state_seat_unavailable
        // from a different branch of the same method. Enumerating from the note
        // would have left a machine holding a seat that no longer exists.
        Assert.True(BotSeatErrors.SeatIsGone(
            Server(HttpStatusCode.Conflict, "bot_state_seat_unavailable", "Суудал устгагдсан.")));

        Assert.False(BotSeatErrors.SeatIsGone(
            Server(HttpStatusCode.Forbidden, "bot_state_signature_invalid", "Гарын үсэг таарсангүй.")));
        Assert.False(BotSeatErrors.SeatIsGone(
            Server(HttpStatusCode.BadRequest, "bot_state_nonce_expired", "Сорилтын хугацаа дууссан.")));
        // 409 as well, and NOT a reason to wipe the seat: the seat is still
        // there, the machine simply cannot prove itself. Status alone would put
        // this in the same bucket as the two above.
        Assert.False(BotSeatErrors.SeatIsGone(
            Server(HttpStatusCode.Conflict, "bot_state_device_key_required", "Түлхүүр бүртгэгдээгүй.")));
        Assert.False(BotSeatErrors.SeatIsGone(new InvalidOperationException("сүлжээ")));

        // A 403 with no code at all is not a released seat either - silence is
        // not a reason, and acting on it would be guessing.
        Assert.False(BotSeatErrors.SeatIsGone(Server(HttpStatusCode.Forbidden, "", "Forbidden")));
    }

    [Fact]
    public void THEThreeReleaseReasonsKeepTheirOwnWordsWhileSharingOneACTION()
    {
        // What to SAY differs - the owner freed it, the seat was deleted, the
        // machine was handed back. What to DO is the same in all three, and
        // splitting the action per reason would be three chances to forget one.
        foreach (string sentence in new[]
                 {
                     "Эзэмшигч энэ төхөөрөмжийг суудлаас чөлөөлсөн байна.",
                     "Энэ суудал устгагдсан байна.",
                     "Энэ төхөөрөмж эзэмшигчид буцаагдсан байна.",
                 })
        {
            StudioAccountException failure =
                Server(HttpStatusCode.Conflict, "bot_state_released_remotely", sentence);

            Assert.True(BotSeatErrors.SeatIsGone(failure));
            Assert.Equal(sentence, BotSeatErrors.Describe(failure, "дуусгавар болсон"));
        }
    }

    [Fact]
    public void THEClientACTUALLYClearsTheSeatOnThatAnswer()
    {
        // The condition the release dialog's restored promise depends on. The
        // wording says the device leaves bot state by itself; if this handler
        // were missing, that would be the same empty promise the note warned
        // about - written once, deleted once, and now restored on the strength
        // of these lines existing.
        string shell = ReadAppSource("ShellView.BotSeat.cs");

        Assert.Contains("BotSeatErrors.SeatIsGone(released)", shell, StringComparison.Ordinal);
        int handler = shell.IndexOf("BotSeatErrors.SeatIsGone(released)", StringComparison.Ordinal);
        string body = shell[handler..(handler + 1400)];
        Assert.Contains("StudioBotDeviceStateStore.Clear();", body, StringComparison.Ordinal);
        Assert.Contains("account.UseBotToken(null);", body, StringComparison.Ordinal);
        Assert.Contains("ApplyDeviceSeat();", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEDialogPromiseAndTheHandlerMOVETogether()
    {
        // The two were separated for a week on purpose: the sentence promised
        // what no code did, so it was cut back and a note left saying what had
        // to exist first. Now both exist, and this is what stops them drifting
        // apart again - a promise with no handler, or a handler nobody is told
        // about.
        string dialogs = ReadAppSource("BotSeatDialogs.cs");
        string shell = ReadAppSource("ShellView.BotSeat.cs");

        Assert.Contains("төлөвөөс өөрөө гарна", dialogs, StringComparison.Ordinal);
        Assert.DoesNotContain("PENDING (STU+SRV)", dialogs, StringComparison.Ordinal);
        Assert.Contains("BotSeatErrors.SeatIsGone", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void AMachineWithNOKeyIsToldTheOWNERMustActRatherThanGettingANewKey()
    {
        // Minting a key here would send the server a fingerprint it has never
        // seen, and the answer would be "unknown device" instead of "this device
        // never registered a key". The first reads as a security event; only the
        // second is something the owner can put right.
        // The sentence is asked of its VALUE. An earlier version searched the
        // source and failed for a reason that had nothing to do with the
        // message: it is built by concatenation, so the words either side of a
        // line break never appear together in the file. That is the same trap
        // this codebase hit on the address-picker message, twice.
        Assert.Contains("дахин суулгах", StudioAccountService.NoDeviceKeyMessageMn, StringComparison.Ordinal);
        Assert.Contains("Эзэмшигч", StudioAccountService.NoDeviceKeyMessageMn, StringComparison.Ordinal);

        string source = ReadAppSource("StudioAccountService.cs");
        int start = source.IndexOf("public async Task<StudioCloudBotStateToken> IssueBotSessionAsync(", StringComparison.Ordinal);
        string method = source[start..(start + 2600)];

        Assert.Contains("StudioDeviceKeyStore.TryFingerprint()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("StudioDeviceKeyStore.Fingerprint()", method, StringComparison.Ordinal);
        Assert.DoesNotContain("StudioDeviceKeyStore.PublicKey()", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ANEXPIREDCredentialIsNOTASeatEndingAndNobodyIsTold()
    {
        // 🔴 TWO THINGS THAT LOOK ALIKE AND ASK FOR OPPOSITE RESPONSES. A seat
        // that ended needs a person; a credential that aged out needs one more
        // round trip and nobody's attention.
        //
        // The server's own sentence for the expired case used to read «Эзэмшигч
        // энэ төхөөрөмжийг дахин суудалжуулна» - it sent the person to their
        // licence owner for something the machine fixes by itself. Acting on
        // that wording would have put a lock screen in front of somebody whose
        // only offence was a month away, and given the owner nothing to do.
        StudioAccountException expired =
            Server(HttpStatusCode.Unauthorized, "bot_session_token_invalid", "Токен хүчингүй.");

        Assert.True(BotSeatErrors.CredentialExpired(expired));
        Assert.False(BotSeatErrors.SeatIsGone(expired));

        // And the reverse: none of the seat-gone codes is treated as a stale
        // credential, or the client would re-prove itself in a loop against a
        // seat that has ended.
        foreach (string gone in new[]
                 {
                     "bot_state_released_remotely",
                     "bot_state_seat_unavailable",
                     "bot_state_not_found",
                     "bot_session_seat_changed",
                 })
        {
            Assert.False(BotSeatErrors.CredentialExpired(
                Server(HttpStatusCode.Conflict, gone, "дууссан")));
        }

        // A device mismatch is 403 and is neither: the token belongs to another
        // machine, and re-proving would hand out a second one just as wrong.
        Assert.False(BotSeatErrors.CredentialExpired(
            Server(HttpStatusCode.Forbidden, "bot_session_device_mismatch", "Өөр төхөөрөмж.")));
        Assert.False(BotSeatErrors.SeatIsGone(
            Server(HttpStatusCode.Forbidden, "bot_session_device_mismatch", "Өөр төхөөрөмж.")));
    }

    [Fact]
    public void THEReproveHappensONCEAndAtTheLayerALLThreeRoutesShare()
    {
        // token, resume and pin/lockout share TryAuthorizeBot on the server, so
        // all three can answer 401. Putting the recovery at each call site would
        // be three chances to leave one out - and the one left out would be the
        // rarely-exercised lockout report, found by a user rather than by us.
        //
        // ONCE, and the retry is not itself retried: a second 401 means the
        // freshly issued credential was refused too, which is a real fault and
        // must surface rather than spin.
        string source = ReadAppSource("StudioAccountService.cs");

        Assert.Contains("BotSeatErrors.CredentialExpired(expired)", source, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(source, "BotSeatErrors.CredentialExpired(expired)"));
        Assert.Equal(2, Occurrences(source, "await ReproveAsync(cancellationToken)"));
        Assert.Contains("botToken = null;", source, StringComparison.Ordinal);

        // The re-prove goes through the issue flow, not the deleted renewal
        // route - a stale token cannot be used to ask for a fresh one.
        int reprove = source.IndexOf("private async Task ReproveAsync(", StringComparison.Ordinal);
        string body = source[reprove..(source.IndexOf("IssueBotSessionAsync(cancellationToken)", reprove, StringComparison.Ordinal) + 40)];
        Assert.DoesNotContain("bot-state/token", body, StringComparison.Ordinal);
    }

    [Fact]
    public void AMachineThatLOCKSItselfCanStillTellTheOwner()
    {
        // 🔴 THE PATH THAT COULD ONLY EVER FAIL. The lock screen is the first
        // thing a seated machine shows, and the lockout report fires when the
        // PINs run out - BEFORE anything has unlocked, so before any credential
        // existed. The old code refused with «Энэ төхөөрөмж ботын эрхээр
        // нэвтрээгүй байна»: true, unactionable, and it meant the owner was
        // never told. The remote unlock existed and the event it exists for
        // could not reach it.
        //
        // The fix is not a special case for the lockout route. Every
        // bot-authorised call now obtains a credential if it lacks one, which is
        // what the contract describes: the PIN gates what the PERSON sees, never
        // what the MACHINE may ask.
        string source = ReadAppSource("StudioAccountService.cs");

        Assert.DoesNotContain("RequireBotToken()", source, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(source, "await EnsureBotCredentialAsync(cancellationToken)"));

        // Both bot-authorised senders go through it - the lockout report is the
        // no-content one, which is the half a per-call-site fix would have left
        // out.
        int noContent = source.IndexOf("private async Task SendBotAuthorizedNoContentAsync<", StringComparison.Ordinal);
        Assert.True(noContent > 0, "the no-content sender was not found");
        Assert.Contains(
            "EnsureBotCredentialAsync",
            source[noContent..(noContent + 700)],
            StringComparison.Ordinal);

        // And the lock screen really does report through it.
        string shell = ReadAppSource("ShellView.BotSeat.cs");
        Assert.Contains("botLockScreen.LockedOut += async () => await ReportBotLockoutAsync();", shell, StringComparison.Ordinal);
    }

    [Fact]
    public void AKnownCredentialIsNotThrownAwayToGetAnotherOne()
    {
        // The other half: "obtain if missing" must not become "obtain every
        // time". Each issue costs two round trips and burns a challenge, and a
        // machine that re-proved itself on every call would look like grinding
        // to the rate limiter it is protected by.
        string source = ReadAppSource("StudioAccountService.cs");
        int start = source.IndexOf("private async Task<StudioCloudBotStateToken> EnsureBotCredentialAsync(", StringComparison.Ordinal);
        string body = source[start..(start + 400)];

        Assert.Contains("if (botToken is { AccessToken.Length: > 0 })", body, StringComparison.Ordinal);
        Assert.Contains("return botToken;", body, StringComparison.Ordinal);
    }

    private static int Occurrences(string text, string needle) => text.Split(needle).Length - 1;

    private static StudioAccountException Server(HttpStatusCode status, string code, string message) =>
        new(message, status, code, "", null, "", "", "");

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
