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

/// <summary>
/// A component the shared album does not have yet takes its place between the
/// components it stands between in this device's build (Батнасан, 2026-10-20:
/// the shared album numbers its components 0, 100, 200, and the new Revit 01
/// with its policy order 5 000 printed after the general plan).
/// </summary>
public sealed class ANEWComponentTakesItsPlaceBetweenItsNEIGHBOURSTests
{
    private static StudioCloudAlbumSection Section(string code, int order) => new() { Code = code, Order = order };

    [Fact]
    public void TheRevitOneGoesBetweenTheCoverAndTheGeneralPlan()
    {
        StudioCloudAlbumSection cover = Section("cover", 0);
        StudioCloudAlbumSection one = Section("revit-01", 5_000);
        StudioCloudAlbumSection generalPlan = Section("et", 200);
        StudioCloudAlbumSection sets = Section("revit-sheets", 800_079);
        var existing = new HashSet<string> { "cover", "et" };

        StudioCanonicalAlbumComponentSlotPolicy.PlaceNewComponents(
            [cover, one, generalPlan, sets],
            item => existing.Contains(item.Code));

        Assert.Equal(["cover", "revit-01", "et", "revit-sheets"],
            new[] { cover, one, generalPlan, sets }.OrderBy(item => item.Order).Select(item => item.Code));
        Assert.Equal(0, cover.Order);
        Assert.Equal(200, generalPlan.Order);
    }

    [Fact]
    public void SeveralNewOnesShareTheGapInOrder()
    {
        StudioCloudAlbumSection a = Section("a", 0);
        StudioCloudAlbumSection n1 = Section("n1", 900);
        StudioCloudAlbumSection n2 = Section("n2", 1);
        StudioCloudAlbumSection b = Section("b", 300);

        StudioCanonicalAlbumComponentSlotPolicy.PlaceNewComponents([a, n1, n2, b], item => item.Code is "a" or "b");

        Assert.Equal([100, 200], new[] { n1.Order, n2.Order });
    }

    [Fact]
    public void AnAlbumWithNothingSharedKeepsThePolicyOrders()
    {
        StudioCloudAlbumSection a = Section("a", 5_000);
        StudioCloudAlbumSection b = Section("b", 100_000);

        StudioCanonicalAlbumComponentSlotPolicy.PlaceNewComponents([a, b], _ => false);

        Assert.Equal([5_000, 100_000], new[] { a.Order, b.Order });
    }

    [Fact]
    public void TheManifestIsPlacedBeforeItIsOrdered()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App")))
            directory = directory.Parent;
        string components = File.ReadAllText(Path.Combine(directory!.FullName, "src", "src", "ErkS.Studio.App", "ShellView.AlbumComponents.cs"));

        int place = components.IndexOf("StudioCanonicalAlbumComponentSlotPolicy.PlaceNewComponents(", StringComparison.Ordinal);
        int order = components.IndexOf("List<StudioCloudAlbumSection> manifest = merged.Values", StringComparison.Ordinal);
        Assert.True(place > 0 && order > place);
    }
}
