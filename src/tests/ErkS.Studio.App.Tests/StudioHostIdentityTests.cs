using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// What Studio tells a licence server it is.
/// </summary>
/// <remarks>
/// SRV added hostApplication and hostVersion on 2026-08-30 and made an empty
/// hostApplication the measurable signal for "this client has not migrated".
/// That only works while a migrated client actually fills it, and these are
/// defaults on a base class - the kind of thing a later edit removes without
/// anything failing.
/// </remarks>
public sealed class StudioHostIdentityTests
{
    [Fact]
    public void EveryDeviceBoundRequestSaysWhichProgramIsAsking()
    {
        // Defaulted on the base rather than set per call site, so a request
        // type added later carries it without anyone remembering to.
        Assert.Equal("Studio", new StudioLicenseActivateRequest().HostApplication);
        Assert.Equal("Studio", new StudioLicenseValidateRequest().HostApplication);
        Assert.Equal("Studio", new StudioSessionRequest().HostApplication);
    }

    [Fact]
    public void TheHostVersionIsNotEmptyAndIsTheOneStudioAlreadySent()
    {
        // appVersion and hostVersion are the same string on purpose: the server
        // keeps the legacy field alongside the new one, and a client that sent
        // two different answers to "what version are you" would be worse than
        // one that had not migrated at all.
        var request = new StudioLicenseActivateRequest();

        Assert.False(string.IsNullOrWhiteSpace(request.HostVersion));
        Assert.Equal(StudioHost.Version, request.HostVersion);
    }

    [Fact]
    public void TheVersionIsSentAsStudioReportsItRatherThanTidied()
    {
        // Under SRV's read-do-not-compute rule the label travels whole. It
        // carries the commit SourceLink appends, and on a release build it is
        // "Demo V0.001.55" - a space and all. Normalising it here would hand
        // the server a guess in place of what this build is actually called,
        // and would break the -dev suffix Studio uses to decide it is a
        // development build.
        Assert.Equal(
            StudioHost.Version,
            System.Reflection.CustomAttributeExtensions
                .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                    typeof(StudioHost).Assembly)
                ?.InformationalVersion);
    }

    [Fact]
    public void BothFieldsSurviveSerialisationUnderTheNamesTheServerReads()
    {
        // The server reads hostApplication and hostVersion. The DTO is
        // serialised with web defaults, so the casing is not something this
        // file can assume - it is checked.
        string json = JsonSerializer.Serialize(
            new StudioLicenseActivateRequest(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;

        Assert.Equal("Studio", root.GetProperty("hostApplication").GetString());
        Assert.Equal(StudioHost.Version, root.GetProperty("hostVersion").GetString());

        // The field it joins rather than replaces.
        Assert.True(root.TryGetProperty("appVersion", out _));
    }
}
