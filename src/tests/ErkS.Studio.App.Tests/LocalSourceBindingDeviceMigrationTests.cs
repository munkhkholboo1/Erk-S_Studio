using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The device fingerprint moved to a shared platform form. A source bound
/// before that names this very machine by its older value, and refusing it
/// would silently stop taking in every delivery from every source bound
/// earlier - with nothing quarantined and nothing said.
/// </summary>
public sealed class LocalSourceBindingDeviceMigrationTests
{
    private const string Canonical = "canonical-fingerprint";
    private const string Legacy = "legacy-fingerprint";
    private const string Email = "designer@erks.mn";

    [Fact]
    public void TheCurrentFingerprint_Matches()
    {
        Assert.True(StudioLocalSourceBindingPolicy.MatchesBoundDevice(
            Canonical,
            Canonical,
            Legacy));
    }

    [Fact]
    public void TheOlderFingerprintForThisDevice_StillMatches()
    {
        Assert.True(StudioLocalSourceBindingPolicy.MatchesBoundDevice(
            Legacy,
            Canonical,
            Legacy));
    }

    [Fact]
    public void AnotherDevicesFingerprint_DoesNotMatch()
    {
        Assert.False(StudioLocalSourceBindingPolicy.MatchesBoundDevice(
            "some-other-machine",
            Canonical,
            Legacy));
    }

    [Fact]
    public void AnEmptyBinding_DoesNotMatch()
    {
        Assert.False(StudioLocalSourceBindingPolicy.MatchesBoundDevice("", Canonical, Legacy));
        Assert.False(StudioLocalSourceBindingPolicy.MatchesBoundDevice(null, Canonical, Legacy));
    }

    [Fact]
    public void AnOlderBinding_IsRewrittenToTheCurrentFingerprint()
    {
        var source = new ProjectDesignSource { Id = "source-1" };
        StudioLocalSourceBindingPolicy.Bind(source, Email, Legacy);

        bool migrated = StudioLocalSourceBindingPolicy.TryMigrateBinding(
            source,
            Email,
            Canonical,
            Legacy);

        Assert.True(migrated);
        Assert.Equal(Canonical, source.Metadata["local.bindingDeviceFingerprint"]);
        Assert.Equal(Email, source.Metadata["local.bindingAccountEmail"]);
    }

    [Fact]
    public void ACurrentBinding_IsLeftAlone()
    {
        var source = new ProjectDesignSource { Id = "source-1" };
        StudioLocalSourceBindingPolicy.Bind(source, Email, Canonical);

        Assert.False(StudioLocalSourceBindingPolicy.TryMigrateBinding(source, Email, Canonical, Legacy));
        Assert.Equal(Canonical, source.Metadata["local.bindingDeviceFingerprint"]);
    }

    [Fact]
    public void AnotherDevicesBinding_IsNotRewritten()
    {
        // Rewriting here would hand this machine a source it never owned.
        var source = new ProjectDesignSource { Id = "source-1" };
        StudioLocalSourceBindingPolicy.Bind(source, Email, "some-other-machine");

        Assert.False(StudioLocalSourceBindingPolicy.TryMigrateBinding(source, Email, Canonical, Legacy));
        Assert.Equal("some-other-machine", source.Metadata["local.bindingDeviceFingerprint"]);
    }
}
