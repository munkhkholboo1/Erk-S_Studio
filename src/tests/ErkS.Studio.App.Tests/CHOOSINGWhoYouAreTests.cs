using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// One list, and the device's state follows from what you picked.
///
/// 🔴 THE OWNER REPLACED THREE BUTTONS WITH THIS. «Ер нь яах гэж ийм тусдаа товч
/// хийгээд байгаа юм бэ? Би зүгээр л өөрийнхөө профайлыг сонгоод тэр дээрээ
/// пасспортоо хийгээд бот төлвөөс бүрэн чөлөөлөгдөж үндсэн эзэмшигчээрээ
/// нэвтэрчихмээр байнашт» · «бот бол пин кодоо хийнэ. эзэмшигч бол пассаа хийнэ».
///
/// The day's trap - «leave» refused because you had not managed, «manage» refused
/// because you had not left - cannot form here: there is no separate management
/// step, so there is no precondition on it to block the way out.
/// </summary>
public sealed class CHOOSINGWhoYouAreTests
{
    [Fact]
    public void AMACHINEWithNoSeatOffersONLYThePerson()
    {
        // «бот үүсгээгүй бол байхгүй» - an empty row inviting somebody to become
        // a bot they never made is the «manage» step wearing another name.
        IReadOnlyList<StudioProfileChoice> rows =
            StudioProfileChoices.For("Энхбаатар Мөнххолбоо", botName: "", actingAsBot: false);

        StudioProfileChoice only = Assert.Single(rows);
        Assert.Equal("Энхбаатар Мөнххолбоо", only.Name);
        Assert.Equal(StudioProfileCredential.Passport, only.Credential);
        Assert.True(only.IsCurrent);
    }

    [Fact]
    public void ASEATShowsBesideThePersonAndASKSForAPIN()
    {
        IReadOnlyList<StudioProfileChoice> rows =
            StudioProfileChoices.For("Энхбаатар Мөнххолбоо", "Зураг оруулагч", actingAsBot: false);

        Assert.Equal(2, rows.Count);
        Assert.Equal(StudioProfileCredential.Passport, rows[0].Credential);
        Assert.Equal(StudioProfileCredential.Pin, rows[1].Credential);
        Assert.Equal(StudioProfileChoices.BotKind, rows[1].Kind);
        Assert.Empty(rows[0].Kind);
    }

    [Fact]
    public void THEPERSONIsOfferedFROMINSIDEBotStateTOO()
    {
        // 🔴 THIS IS THE WAY OUT, AND IT MUST NOT DEPEND ON ANYTHING THE SEAT CAN
        // TAKE AWAY. The machine that lost a whole day had its exit hidden because
        // a local record had gone; the person's own row is drawn from what the
        // DEVICE remembers about them, which outlives the seat.
        IReadOnlyList<StudioProfileChoice> rows =
            StudioProfileChoices.For("Энхбаатар Мөнххолбоо", "Зураг оруулагч", actingAsBot: true);

        StudioProfileChoice person = rows[0];
        Assert.Equal(StudioProfileCredential.Passport, person.Credential);
        Assert.False(person.IsCurrent);
        Assert.True(StudioProfileChoices.IsASwitch(person));
    }

    [Fact]
    public void WHOEVERIsActingIsMarkedASCurrentAndSwitchingToThemDoesNOTHING()
    {
        // Pressing your own name must not ask for a credential and must not run a
        // switch. A no-op that ends the session and rebuilds it is how somebody
        // loses their place for touching their own row.
        IReadOnlyList<StudioProfileChoice> asBot =
            StudioProfileChoices.For("Эзэн", "Зураг оруулагч", actingAsBot: true);

        Assert.False(asBot[0].IsCurrent);
        Assert.True(asBot[1].IsCurrent);
        Assert.False(StudioProfileChoices.IsASwitch(asBot[1]));
        Assert.True(StudioProfileChoices.IsASwitch(asBot[0]));
    }

    [Fact]
    public void APINNeverStandsForTheOWNER()
    {
        // 🔴 THE TWO-TIER RULE THE PLATFORM HAS HELD SINCE 2026-09-03: the owner
        // is a full credential, the seat is a PIN. A list that let four digits
        // choose the owner's row would hand a shared office machine the licence.
        foreach (bool acting in new[] { true, false })
        {
            foreach (StudioProfileChoice row in
                StudioProfileChoices.For("Эзэн", "Зураг оруулагч", acting))
            {
                if (row.Kind.Length == 0)
                    Assert.Equal(StudioProfileCredential.Passport, row.Credential);
                else
                    Assert.Equal(StudioProfileCredential.Pin, row.Credential);
            }
        }
    }

    [Fact]
    public void ANAMEThatIsMissingIsNAMEDHereToo()
    {
        // One naming rule across the product - the same constant the masthead and
        // the seat table use. Two vocabularies for one question is the shape that
        // has cost this project four separate defects.
        IReadOnlyList<StudioProfileChoice> rows =
            StudioProfileChoices.For("Эзэн", "   ", actingAsBot: false);

        Assert.Single(rows);

        IReadOnlyList<StudioProfileChoice> named =
            StudioProfileChoices.For("   ", "Зураг оруулагч", actingAsBot: true);
        Assert.Single(named);
        Assert.Equal(StudioProfileChoices.BotKind, named[0].Kind);
    }

    [Fact]
    public void THELISTCanNeverGrowPastTWORows()
    {
        // 🔴 THE LIMIT IS ASSERTED, NOT ASSUMED. «At most two» is true because a
        // machine remembers ONE person and holds at most ONE seat - both facts
        // about today's design, not laws. If a third row ever becomes possible,
        // the screen that lays these out must not be the place it is discovered:
        // a list built for two would simply look wrong, quietly, on somebody's
        // machine. This goes red first.
        foreach (string owner in new[] { "", "   ", "Эзэн" })
        {
            foreach (string bot in new[] { "", "   ", "Зураг оруулагч" })
            {
                foreach (bool acting in new[] { true, false })
                {
                    IReadOnlyList<StudioProfileChoice> rows =
                        StudioProfileChoices.For(owner, bot, acting);

                    Assert.True(
                        rows.Count <= 2,
                        "the profile list produced " + rows.Count + " rows");

                    // And never two of the same kind: one person, one seat.
                    Assert.True(rows.Count(row => row.Kind.Length == 0) <= 1);
                    Assert.True(rows.Count(row => row.Kind.Length > 0) <= 1);

                    // Exactly one row can be the current identity - two would
                    // mean the window is claiming to be two people at once.
                    Assert.True(rows.Count(row => row.IsCurrent) <= 1);
                }
            }
        }
    }

    [Fact]
    public void THEMenuOffersTheProfilesBEFOREAnythingElse()
    {
        // Choosing who you are is the whole interaction; the device-state entries
        // are what the owner is replacing, so they cannot sit above it.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"), "private void PopulateAccountMenu(ContextMenu menu)");

        int profiles = body.IndexOf("BuildProfileChoiceItems()", StringComparison.Ordinal);
        int botEntries = body.IndexOf("BuildBotMenuItems()", StringComparison.Ordinal);
        Assert.True(profiles > 0, "the account menu no longer offers the profiles");
        Assert.True(botEntries > profiles, "the profiles must come first");
    }

    [Fact]
    public void EACHRowRunsTheSwitchITSCredentialImplies()
    {
        // 🔴 THE PIN MUST NEVER REACH THE OWNER'S ROUTE. Four digits opening a
        // full owner session would hand a shared office machine the licence - the
        // two-tier rule this platform has held since 2026-09-03.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"), "private IEnumerable<MenuItem> BuildProfileChoiceItems()");

        int pin = body.IndexOf("credential == StudioProfileCredential.Pin", StringComparison.Ordinal);
        Assert.True(pin > 0, "the row no longer branches on what it asks for");

        string branch = body[pin..];
        int enterSeat = branch.IndexOf("EnterBotStateAsync()", StringComparison.Ordinal);
        int ownerRoute = branch.IndexOf("VerifyOwnerOnSeatedDeviceAsync()", StringComparison.Ordinal);
        Assert.True(enterSeat > 0, "the PIN row no longer enters the seat");
        Assert.True(ownerRoute > enterSeat, "the passport row must be the other branch");
    }

    [Fact]
    public void THERowYouALREADYAreIsNotAButton()
    {
        // Shown, so the list answers «who am I» as well as «who could I be» - but
        // pressing it must not end the session and rebuild it.
        string body = MethodBody(
            ReadAppSource("ShellView.cs"), "private IEnumerable<MenuItem> BuildProfileChoiceItems()");

        Assert.Contains("IsEnabled = StudioProfileChoices.IsASwitch(choice)", body, StringComparison.Ordinal);
        Assert.Contains("IsChecked = choice.IsCurrent", body, StringComparison.Ordinal);
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

    [Fact]
    public void AMACHINEThatRemembersNOBODYOffersNothingToPretendWith()
    {
        // No person, no seat: an empty list. A row invented here would be a name
        // nobody can authenticate as.
        Assert.Empty(StudioProfileChoices.For("", "", actingAsBot: false));
    }
}
