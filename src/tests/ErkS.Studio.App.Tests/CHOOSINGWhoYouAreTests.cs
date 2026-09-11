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
    public void AMACHINEThatRemembersNOBODYOffersNothingToPretendWith()
    {
        // No person, no seat: an empty list. A row invented here would be a name
        // nobody can authenticate as.
        Assert.Empty(StudioProfileChoices.For("", "", actingAsBot: false));
    }
}
