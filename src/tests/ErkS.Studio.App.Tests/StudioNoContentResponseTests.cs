using System.Net;
using System.Net.Http.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Five bot routes answer 204 No Content - release a seat, unlock a PIN, report
/// a lockout, decline and cancel an invitation - and the client read every one
/// of them as if a body were coming. An empty body deserialises to null, null
/// threw "Cloud ERA server хоосон хариу өглөө.", and so the server did the work
/// while the person was told it had failed. Leaving bot state was one of them.
/// </summary>
public sealed class StudioNoContentResponseTests
{
    [Fact]
    public async Task A204IsSuccess_NotAnEmptyBodyToComplainAbout()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);

        // No exception is the whole assertion: this is the shape every one of
        // those five routes answers with.
        await StudioAccountService.ReadNoContentResponseAsync(response, CancellationToken.None);
    }

    [Fact]
    public async Task A200WithNothingInItIsAlsoDone()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(""),
        };

        await StudioAccountService.ReadNoContentResponseAsync(response, CancellationToken.None);
    }

    [Fact]
    public async Task AFailureStillFails_AndKeepsTheServersOwnWords()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                code = "bot_state_released_remotely",
                message = "Энэ суудал алсаас чөлөөлөгдсөн байна.",
            }),
        };

        StudioAccountException failure = await Assert.ThrowsAsync<StudioAccountException>(
            () => StudioAccountService.ReadNoContentResponseAsync(response, CancellationToken.None));

        // The reason has to survive: "it did not work" is not something anyone
        // can act on, and the code is what a caller branches on.
        Assert.Equal(HttpStatusCode.Conflict, failure.StatusCode);
        Assert.Equal("bot_state_released_remotely", failure.ErrorCode);
        Assert.Contains("чөлөөлөгдсөн", failure.Message);
    }

    [Fact]
    public async Task AFailureWithNoJsonBodyStillNamesTheStatus()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>gateway</html>"),
        };

        StudioAccountException failure = await Assert.ThrowsAsync<StudioAccountException>(
            () => StudioAccountService.ReadNoContentResponseAsync(response, CancellationToken.None));

        Assert.Contains("502", failure.Message);
    }
}
