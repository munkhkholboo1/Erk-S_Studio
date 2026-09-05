using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A source registered through a bot seat belongs to the SEAT. SRV empties
/// registeredBy and custodianEmail on those rows so that no person-shaped field
/// carries a machine identifier.
///
/// Three places on this side read those fields as the answer to "who owns
/// this", and each failed differently once they went empty: the album registry
/// DROPPED the source, the reconciler MERGED two seats into one, and the
/// details panel would have shown a label with nothing after it.
/// </summary>
public sealed class StudioBotOwnedSourceTests
{
    private static ProjectCloudSourceReference BotOwned(
        string sourceKey = "arch-model",
        string botId = "bot_7f3a91c4e85b4d2f") =>
        new()
        {
            SourceId = "src-" + botId,
            SourceKey = sourceKey,
            SourceOwnerKind = "Bot",
            SourceOwnerRef = botId,
            RegisteredBy = "",
            CustodianEmail = "",
            OwnerEmail = "",
            Status = "Registered",
        };

    [Fact]
    public void ASeatsSourceREACHESTheAlbumRegistry()
    {
        // The worst of the three, because it loses the whole package rather
        // than one field: the projection kept only sources with a non-empty
        // person owner, so everything a seat produced was filtered out before
        // album ordering ever saw it. Nothing reported a loss - the list was
        // simply shorter.
        IReadOnlyList<StudioCloudSourcePackage> projected =
            StudioSharedSourceProjection.Create([BotOwned()]);

        Assert.Single(projected);
        Assert.Equal("Bot", projected[0].SourceOwnerKind);
        Assert.Equal("bot_7f3a91c4e85b4d2f", projected[0].SourceOwnerRef);
        // registeredBy stays honestly empty: no person registered it.
        Assert.Equal("", projected[0].RegisteredBy);
    }

    [Fact]
    public void ASourceWithNoOwnerAtAllIsStillDropped()
    {
        // The filter is loosened for seats, not removed. A row naming no owner
        // of any kind is the case it was written for.
        Assert.Empty(StudioSharedSourceProjection.Create([
            new ProjectCloudSourceReference { SourceKey = "arch-model" },
        ]));
    }

    [Fact]
    public void TwoSEATSKeepTheirOwnSourcesUnderOneSourceKey()
    {
        // The reconciler groups by owner + source key and keeps ONE row per
        // group. Keyed on registeredBy, both seats landed in the same empty
        // bucket and one of the two disappeared from the list silently.
        IReadOnlyList<StudioCloudSourcePackage> packages =
            StudioSharedSourceProjection.Create([
                BotOwned(botId: "bot_aaa"),
                BotOwned(botId: "bot_bbb"),
            ]);

        IReadOnlyList<StudioCloudSourcePackage> canonical =
            StudioCloudSourcePackageReconciliation.ActiveCanonical(packages);

        Assert.Equal(2, canonical.Count);
    }

    [Fact]
    public void TheSameSEATStillCollapsesToItsLatestRow()
    {
        // And the reason the grouping exists is untouched: one owner, one key,
        // one surviving row.
        IReadOnlyList<StudioCloudSourcePackage> packages =
            StudioSharedSourceProjection.Create([BotOwned(), BotOwned()]);

        Assert.Single(StudioCloudSourcePackageReconciliation.ActiveCanonical(packages));
    }

    [Fact]
    public void APersonsSourceIsUnaffectedByAnyOfThis()
    {
        var person = new ProjectCloudSourceReference
        {
            SourceId = "src-1",
            SourceKey = "arch-model",
            RegisteredBy = "owner@example.com",
            Status = "Registered",
        };

        IReadOnlyList<StudioCloudSourcePackage> projected =
            StudioSharedSourceProjection.Create([person]);

        Assert.Single(projected);
        Assert.Equal("owner@example.com", projected[0].RegisteredBy);
        Assert.Single(StudioCloudSourcePackageReconciliation.ActiveCanonical(projected));
    }

    [Fact]
    public void TheOwnerLineNamesTheKindOfOwner_NeverTheBotId()
    {
        // A botId on the owner line is the same defect as an email there: a
        // machine identifier where a reader expects a party. And the seat's
        // NAME is not reachable from here - listing seats needs organisation
        // management rights, which a plain member and a seated machine both
        // lack - so the honest line says what kind of owner it is.
        string label = StudioSourceOwnerLabel.Describe(BotOwned());

        Assert.Equal(StudioSourceOwnerLabel.UnnamedSeat, label);
        Assert.DoesNotContain("bot_", label, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheSeatsRealNameIsUsedWhereverTheCallerHoldsIt()
    {
        // The bot-seat dialog does hold it. One rule, so the two screens cannot
        // drift into naming the same seat differently.
        Assert.Equal(
            "Архитектор-1",
            StudioSourceOwnerLabel.Describe(BotOwned(), seatDisplayName: "Архитектор-1"));
    }

    [Fact]
    public void APersonIsStillShownByEmail_AndNothingIsShownForNoOwner()
    {
        Assert.Equal(
            "owner@example.com",
            StudioSourceOwnerLabel.Describe(new ProjectCloudSourceReference
            {
                RegisteredBy = "Owner@Example.com",
            }));
        Assert.Equal(
            StudioSourceOwnerLabel.Nobody,
            StudioSourceOwnerLabel.Describe(new ProjectCloudSourceReference()));
    }

    [Fact]
    public void AnOwnerKindFromANewerServerSaysSo_RatherThanNamingSomebody()
    {
        // The row carries an email as well. Showing it would be the confident
        // wrong answer; the reader is told the build cannot read the record.
        string label = StudioSourceOwnerLabel.Describe(new ProjectCloudSourceReference
        {
            SourceOwnerKind = "Cooperative",
            SourceOwnerRef = "coop-1",
            RegisteredBy = "owner@example.com",
        });

        Assert.Equal(StudioSourceOwnerLabel.UnreadableKind, label);
        Assert.DoesNotContain("@", label);
    }
}
