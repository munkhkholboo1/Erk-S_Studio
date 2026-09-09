using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Whether the sign-in window greets somebody or asks who they are.
///
/// The rule is a function of the profile on disk and the server actually
/// configured. It lives outside the window for the reason the account line and
/// the bot menu do: every version of this rule that lived inside a method
/// building WPF controls has been wrong at least once, and none of those was
/// caught by a test.
/// </summary>
public sealed class LoginPromptTests
{
    private static StudioRememberedProfile Profile(
        string email = "gerlee@erk-s.mn",
        string server = "https://era.erk-s.mn") => new()
        {
            Email = email,
            DisplayName = "Гэрлээ Б.",
            ServerUrl = server,
            RememberedAtUtc = new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero),
        };

    [Fact]
    public void AKnownPersonIsAskedONLYForTheirPassword()
    {
        Assert.Equal(
            LoginPromptMode.AskOnlyForPassword,
            StudioLoginPrompt.Mode(Profile(), "https://era.erk-s.mn"));
    }

    [Fact]
    public void ANEmptyMachineAsksWhoYouAre()
    {
        Assert.Equal(
            LoginPromptMode.AskForEverything,
            StudioLoginPrompt.Mode(null, "https://era.erk-s.mn"));
    }

    [Fact]
    public void THESameAddressOnANOTHERServerIsNotTheSamePerson()
    {
        // 🔴 GREETING IT BY NAME WOULD HAVE THE FORM ASSERT SOMETHING IT HAS
        // NOT GOT. One address can exist on two servers as two accounts, and a
        // form that pre-fills the wrong one signs somebody in as a stranger -
        // or, more often, fails and blames them for their own password.
        Assert.Equal(
            LoginPromptMode.AskForEverything,
            StudioLoginPrompt.Mode(Profile(server: "https://era.erk-s.mn"), "https://test.erk-s.mn"));
        Assert.Null(
            StudioLoginPrompt.Recognised(
                Profile(server: "https://era.erk-s.mn"), "https://test.erk-s.mn"));
    }

    [Fact]
    public void ATrailingSlashIsNotADifferentServer()
    {
        // The saved value and the configured one come from two places and one
        // of them is typed. Refusing to greet over a slash would look like the
        // machine had forgotten, with nothing on screen to explain it.
        Assert.Equal(
            LoginPromptMode.AskOnlyForPassword,
            StudioLoginPrompt.Mode(Profile(server: "https://era.erk-s.mn/"), "https://era.erk-s.mn"));
        Assert.Equal(
            LoginPromptMode.AskOnlyForPassword,
            StudioLoginPrompt.Mode(Profile(server: "HTTPS://ERA.ERK-S.MN"), "https://era.erk-s.mn"));
    }

    [Fact]
    public void APROFILEWithNoAddressGreetsNobody()
    {
        Assert.Null(StudioLoginPrompt.Recognised(Profile(email: "   "), "https://era.erk-s.mn"));
    }

    [Fact]
    public void THEGreetingAndTheAddressComeFromONERecord()
    {
        // 🔴 TAKING THE NAME FROM THE PROFILE AND THE ADDRESS FROM SOMEWHERE
        // ELSE IS HOW A FORM SIGNS IN AS SOMEBODY OTHER THAN THE PERSON IT
        // GREETED. The window fills the address box from the SAME record it
        // draws the name from, and only falls back when there is no record.
        string source = ReadAppSource("StudioLoginDialog.cs");

        Assert.Contains(
            "emailBox.Text = recognised?.Email ?? account.SuggestedEmail;",
            source,
            StringComparison.Ordinal);
        Assert.Contains("greetingEmail.Text = recognised.Email;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void BOTHWaysOutOfTheGreetingAreOfferedAndTheyAreDifferent()
    {
        // «Somebody else is signing in on my machine» and «this machine is not
        // mine any more» are different acts. Offering only the first leaves a
        // person's name on a shared seat with no way to take it off; offering
        // only the second makes signing in as a colleague destroy the greeting.
        string source = ReadAppSource("StudioLoginDialog.cs");

        Assert.Contains("Өөр бүртгэлээр", source, StringComparison.Ordinal);
        Assert.Contains("Санахаа болих", source, StringComparison.Ordinal);

        string another = MethodBody(source, "private void UseAnotherAccount()");
        string forget = MethodBody(source, "private void ForgetRememberedProfile()");

        // Signing in as somebody else does NOT forget the machine's person.
        Assert.DoesNotContain("StudioRememberedProfiles.Forget", another, StringComparison.Ordinal);
        Assert.Contains("StudioRememberedProfiles.Forget();", forget, StringComparison.Ordinal);

        // Both go back to the form that asks for an address, and both clear the
        // box - the point of either is that the next address is a new one.
        foreach (string body in new[] { another, forget })
        {
            Assert.Contains("LoginPromptMode.AskForEverything", body, StringComparison.Ordinal);
            Assert.Contains("emailBox.Text = \"\";", body, StringComparison.Ordinal);
            Assert.Contains("ApplyPromptMode();", body, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void THEAddressSurvivesASignOutBecauseItIsAskedForFromTheProfile()
    {
        // SuggestedEmail read account.json, which sign-out deletes - so the
        // suggestion existed in the one case where the address was already on
        // screen and was gone in every case where it would have helped.
        string source = ReadAppSource("StudioAccountService.cs");

        Assert.Contains(
            "StudioRememberedProfiles.Read()?.Email ?? \"\"",
            source,
            StringComparison.Ordinal);

        // And it is written where a sign-in has SUCCEEDED, beside the metadata,
        // not where one was attempted.
        int metadata = source.IndexOf("WriteMetadata(savedMetadata);", StringComparison.Ordinal);
        int remember = source.IndexOf("StudioRememberedProfiles.Remember(", StringComparison.Ordinal);
        Assert.True(metadata > 0 && remember > metadata, "the profile is not remembered on a successful sign-in");
    }

    [Fact]
    public void SOMEBODYAlreadySignedInDoesNotHaveToSignInAgainToBeRemembered()
    {
        // The person this was built for is signed in RIGHT NOW, on a machine
        // whose account.json already holds their address and name. A feature
        // that only starts working after their NEXT sign-in would look broken
        // to exactly the person who asked for it.
        //
        // Remembering sits in the one place both routes to a live session pass
        // through - the fresh sign-in and the restore of a saved one - so a
        // machine that is already signed in remembers at its next launch.
        string source = ReadAppSource("StudioAccountService.cs");
        string body = MethodBody(source, "    private void CommitSession(");

        Assert.Contains("StudioRememberedProfiles.Remember(", body, StringComparison.Ordinal);

        // Defined once, reached twice: three occurrences of the name in the file.
        Assert.Equal(3, source.Split("CommitSession(").Length - 1);
    }

    [Fact]
    public void THELetterAvatarHasONECopyOfItsRule()
    {
        // It is drawn in two windows now. A second copy is how two screens come
        // to show different initials for one person.
        Assert.Equal("ГБ", StudioAccountDisplay.Initials("Гэрлээ Б."));
        Assert.Equal("ES", StudioAccountDisplay.Initials(""));
        Assert.Equal("ES", StudioAccountDisplay.Initials(null!));

        string source = ReadAppSource("ShellView.cs");
        Assert.Contains(
            "StudioAccountDisplay.Initials(displayName);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SOMEBODYTheServerNeverNamedIsStillCalledSomething()
    {
        Assert.Equal(
            "Миний бүртгэл",
            StudioAccountDisplay.NameOrFallback("", "gerlee@erk-s.mn", "Миний бүртгэл"));

        // Printing the address where the name goes says nothing that is not
        // already on the line below it.
        Assert.Equal(
            "Миний бүртгэл",
            StudioAccountDisplay.NameOrFallback(
                "gerlee@erk-s.mn", "gerlee@erk-s.mn", "Миний бүртгэл"));
        Assert.Equal(
            "Гэрлээ Б.",
            StudioAccountDisplay.NameOrFallback("Гэрлээ Б.", "gerlee@erk-s.mn", "Миний бүртгэл"));
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
