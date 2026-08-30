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
    public void TheLabelStillFitsInWhatTheServerStores()
    {
        // The server truncates these at 80 characters
        // (Program.RecordActivationHost). It was 40 until SRV measured this
        // build's shape: "Demo V0.001.30+<40 hex sha>" is 55 characters, and a
        // 40-character bound would have cut fifteen characters off the sha -
        // storing one build's label under another build's name, which is worse
        // than storing nothing.
        //
        // 80 leaves 25 characters of headroom, and all of it is in the label:
        // the commit suffix is a fixed 41. So this guards the one thing that
        // can still grow. A release label longer than 39 characters would be
        // truncated in silence on the server; here it fails out loud instead.
        Assert.True(
            StudioHost.Version.Length <= 80,
            $"The version label is {StudioHost.Version.Length} characters and the licence "
            + "server stores 80. Past that it truncates silently, so two builds can be "
            + "recorded under the same name. Shorten the release label, or agree a longer "
            + "bound with SRV first.");
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
