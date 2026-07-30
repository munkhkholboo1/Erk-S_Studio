using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioCanonicalAlbumComponentSlotPolicyTests
{
    [Fact]
    public void ExistingCanonicalComponentKeepsServerAnnouncedSlot()
    {
        const string owner = "planner@example.com";
        const string sourceKey = "general-plan";
        string code = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "fixed:Ерөнхий төлөвлөгөө",
            "master-plan");
        var rendered = new StudioCloudAlbumSection
        {
            Code = code,
            OwnerEmail = owner,
            SourceKey = sourceKey,
            SectionKey = "fixed:Ерөнхий төлөвлөгөө",
            SequenceKey = "master-plan",
            Order = 100_800,
        };
        var server = new StudioCloudAlbumSection
        {
            Code = code,
            OwnerEmail = owner,
            SourceKey = sourceKey,
            SectionKey = "fixed:Ерөнхий төлөвлөгөө",
            SequenceKey = "master-plan",
            Order = 456_700,
        };

        int resolved = StudioCanonicalAlbumComponentSlotPolicy.ResolveOrder(
            new ProjectWorkspace(),
            rendered,
            [server]);

        Assert.Equal(456_700, resolved);
    }

    [Fact]
    public void NewUnsyncedComponentKeepsItsProvisionalOrder()
    {
        var rendered = new StudioCloudAlbumSection
        {
            Code = "source:new",
            Order = 123_400,
        };

        int resolved = StudioCanonicalAlbumComponentSlotPolicy.ResolveOrder(
            new ProjectWorkspace(),
            rendered,
            []);

        Assert.Equal(123_400, resolved);
    }

    [Fact]
    public void ImmutableOwnerSourceAndSliceKeepLegacyServerSlot()
    {
        const string owner = "planner@example.com";
        const string sourceKey = "general-plan";
        var rendered = new StudioCloudAlbumSection
        {
            Code = StudioAlbumComponentIdentity.SourceSliceCode(
                owner,
                sourceKey,
                "fixed:Ерөнхий төлөвлөгөө",
                "solar-study"),
            OwnerEmail = owner,
            SourceKey = sourceKey,
            SectionKey = "fixed:Ерөнхий төлөвлөгөө",
            SequenceKey = "solar-study",
            Order = 100_700,
        };
        var legacyServer = new StudioCloudAlbumSection
        {
            Code = "legacy:general-plan-solar",
            OwnerEmail = owner,
            SourceKey = sourceKey,
            SectionKey = "fixed:Ерөнхий төлөвлөгөө",
            SequenceKey = "solar-study",
            Order = 314_159,
        };

        int resolved = StudioCanonicalAlbumComponentSlotPolicy.ResolveOrder(
            new ProjectWorkspace(),
            rendered,
            [legacyServer]);

        Assert.Equal(314_159, resolved);
    }
}
