using System.Net;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The three ways a seat can end - the owner released it, the seat was deleted,
/// the device was handed back - are three different things for the person in
/// front of the screen. Collapsing them into one sentence is the same defect as
/// reading a 204 as "алдаа", which cost two hours of a user's evening.
/// </summary>
public sealed class BotSeatErrorDescriptionTests
{
    private static StudioAccountException FromServer(
        HttpStatusCode status,
        string code,
        string message) =>
        new(message, status, code, "", null, "", "", "");

    [Theory]
    [InlineData("Эзэмшигч энэ төхөөрөмжийг суудлаас чөлөөлсөн байна.")]
    [InlineData("Энэ суудал устгагдсан байна.")]
    [InlineData("Энэ төхөөрөмж эзэмшигчид буцаагдсан байна.")]
    public void EachWayASeatEndsKeepsItsOwnSentence(string serverSentence)
    {
        string described = BotSeatErrors.Describe(
            FromServer(HttpStatusCode.Forbidden, "bot_state_released_remotely", serverSentence),
            "Ботын төлөв уншигдсангүй.");

        Assert.Equal(serverSentence, described);
    }

    [Fact]
    public void ASeatThatNoLongerExistsIsNotReportedAsAnOutOfDateServer()
    {
        // The defect. Every 404 used to be rewritten as "this server does not
        // support bots yet", so "your seat is gone" sent the owner to look at
        // the server version instead of at the seat.
        string described = BotSeatErrors.Describe(
            FromServer(HttpStatusCode.NotFound, "bot_state_not_found", "Энэ төхөөрөмжид суудал алга."),
            "Ботын төлөв уншигдсангүй.");

        Assert.Equal("Энэ төхөөрөмжид суудал алга.", described);
        Assert.DoesNotContain("дэмжихгүй", described);
    }

    [Fact]
    public void AMissingRouteStillReadsAsAServerThatIsBehind()
    {
        // A route that is not there cannot name a reason, and that silence is
        // exactly what tells them apart.
        string described = BotSeatErrors.Describe(
            FromServer(HttpStatusCode.NotFound, "", "Not Found"),
            "Ботын төлөв уншигдсангүй.");

        Assert.Contains("дэмжихгүй", described);
    }

    [Fact]
    public void AKeyThatWasNeverRegisteredKeepsTheServersOwnWords()
    {
        string described = BotSeatErrors.Describe(
            FromServer(
                HttpStatusCode.Conflict,
                "bot_state_device_key_required",
                "Энэ төхөөрөмжийн түлхүүр бүртгэгдээгүй байна."),
            "Ботын төлөв уншигдсангүй.");

        Assert.Equal("Энэ төхөөрөмжийн түлхүүр бүртгэгдээгүй байна.", described);
    }

    [Fact]
    public void SomethingThatIsNotTheServerFallsBackWithItsOwnDetail()
    {
        string described = BotSeatErrors.Describe(
            new IOException("The network path was not found."),
            "Ботын төлөв уншигдсангүй.");

        Assert.StartsWith("Ботын төлөв уншигдсангүй.", described);
        Assert.Contains("network path", described);
    }
}
