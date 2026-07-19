namespace ErkS.Studio.App.Tests;

public sealed class StudioRegisteredPersonNameResolverTests
{
    [Fact]
    public void StructuredProfileFieldsOverrideReversedLegacyDisplayName()
    {
        StudioRegisteredPersonName result = StudioRegisteredPersonNameResolver.Resolve(
            familyName: "Энхбаатар",
            givenName: "Мөнххолбоо",
            displayName: "Мөнххолбоо Энхбаатар",
            displayNameUsesCanonicalProfileOrder: false);

        Assert.Equal("Энхбаатар", result.FamilyName);
        Assert.Equal("Мөнххолбоо", result.GivenName);
        Assert.Equal("Энхбаатар Мөнххолбоо", result.DisplayName);
    }

    [Fact]
    public void LegacyServerCanonicalDisplayNameIsSplitOnlyAtCompatibilityBoundary()
    {
        StudioRegisteredPersonName result = StudioRegisteredPersonNameResolver.Resolve(
            familyName: "",
            givenName: "",
            displayName: "Энхбаатар Мөнххолбоо",
            displayNameUsesCanonicalProfileOrder: true);

        Assert.Equal("Энхбаатар", result.FamilyName);
        Assert.Equal("Мөнххолбоо", result.GivenName);
    }

    [Fact]
    public void ParticipantSnapshotWordOrderIsNotGuessed()
    {
        StudioRegisteredPersonName result = StudioRegisteredPersonNameResolver.Resolve(
            familyName: "",
            givenName: "",
            displayName: "Мөнххолбоо Энхбаатар",
            displayNameUsesCanonicalProfileOrder: false);

        Assert.Empty(result.FamilyName);
        Assert.Empty(result.GivenName);
        Assert.Equal("Мөнххолбоо Энхбаатар", result.DisplayName);
    }
}
