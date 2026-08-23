using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The session's bearer token must never travel to any host other than the
/// session's own server, whatever URL the server hands back in a response.
/// </summary>
public sealed class StudioSameOriginUriTests
{
    private const string Server = "https://erk-s.mn";

    [Fact]
    public void RelativePath_ResolvesOnTheSessionServer()
    {
        Assert.True(StudioAccountService.TryBuildSameOriginUri(
            Server,
            "/api/cloud-era/v1/projects/p1/design-organization/logo",
            out Uri uri));
        Assert.Equal("https://erk-s.mn/api/cloud-era/v1/projects/p1/design-organization/logo", uri.ToString());
    }

    [Fact]
    public void AbsoluteUrlOnTheSameHost_IsAllowed()
    {
        Assert.True(StudioAccountService.TryBuildSameOriginUri(
            Server,
            "https://erk-s.mn/api/studio/profile/photo",
            out Uri uri));
        Assert.Equal("https://erk-s.mn/api/studio/profile/photo", uri.ToString());
    }

    [Fact]
    public void ForeignHost_IsRefused()
    {
        Assert.False(StudioAccountService.TryBuildSameOriginUri(
            Server,
            "https://attacker.example/steal",
            out _));
    }

    [Fact]
    public void ProtocolRelativeUrl_CannotEscapeTheServer()
    {
        Assert.True(StudioAccountService.TryBuildSameOriginUri(
            Server,
            "//attacker.example/steal",
            out Uri uri));
        Assert.Equal("erk-s.mn", uri.Host);
    }

    [Fact]
    public void DifferentPort_IsRefused()
    {
        Assert.False(StudioAccountService.TryBuildSameOriginUri(
            Server,
            "https://erk-s.mn:8443/api/studio/profile/photo",
            out _));
    }

    [Fact]
    public void SchemeDowngrade_IsRefused()
    {
        Assert.False(StudioAccountService.TryBuildSameOriginUri(
            Server,
            "http://erk-s.mn/api/studio/profile/photo",
            out _));
    }
}
