using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The loaded copy of the catalogue, and the one fact it works out for itself.
/// </summary>
public sealed class AdministrativeUnitSnapshotTests
{
    private static readonly AdministrativeUnit Ulaanbaatar =
        new("511", AdministrativeUnits.Capital, "", "Улаанбаатар", "Дүүрэг");

    private static readonly AdministrativeUnit Baganuur =
        new("51101", AdministrativeUnits.District, "511", "Багануур", "Хороо");

    private static readonly AdministrativeUnit FirstKhoroo =
        new("5110151", AdministrativeUnits.Khoroo, "51101", "1-р хороо", "");

    /// <summary>
    /// A sum that names its child level and has nothing under it. Хатгал, Бэрх
    /// and Гурванбаян are in exactly this state in the published data, and they
    /// are why HasChildren cannot be read off the label.
    /// </summary>
    private static readonly AdministrativeUnit ChildlessSum =
        new("26702", AdministrativeUnits.Sum, "267", "Бэрх", "Баг");

    [Fact]
    public void TheTopLevelIsWhatHasNOParent()
    {
        var snapshot = new AdministrativeUnitSnapshot(
            [Ulaanbaatar, Baganuur, FirstKhoroo],
            new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero));

        Assert.Equal(["511"], snapshot.ChildrenOf("").Select(unit => unit.UnitCode));
        Assert.Equal(["51101"], snapshot.ChildrenOf("511").Select(unit => unit.UnitCode));
        Assert.Equal(["5110151"], snapshot.ChildrenOf("51101").Select(unit => unit.UnitCode));
        Assert.Empty(snapshot.ChildrenOf("5110151"));
        Assert.Empty(snapshot.ChildrenOf("нэгэн ийм код байхгүй"));
    }

    [Fact]
    public void HasChildrenComesFromTHEROWSNotFromTheLabel()
    {
        // THE FALSE EQUIVALENCE, measured away on real data by SRV and once held
        // on this side too: «Баг» as a child label does not mean bags exist. A
        // sum with none published is still a sum, and telling the reader its
        // catalogue failed to download would be a lie about complete data.
        var snapshot = new AdministrativeUnitSnapshot(
            [Ulaanbaatar, Baganuur, FirstKhoroo, ChildlessSum],
            asOfUtc: null);

        AdministrativeUnit childless = snapshot.ChildrenOf("267").Single();
        AdministrativeUnit bearing = snapshot.ChildrenOf("511").Single();

        Assert.Equal("Баг", childless.ChildPickerLabelMn);
        Assert.False(childless.HasChildren);
        Assert.True(bearing.HasChildren);
    }

    [Fact]
    public void AnEmptyCopyCARRIESItsReason()
    {
        // Empty is a state with several causes, and the cause is the whole of
        // what the reader needs. The list is what they have in common.
        var snapshot = AdministrativeUnitSnapshot.Empty("Сервер 404 гэж хариулав.");

        Assert.Equal(0, snapshot.Count);
        Assert.Null(snapshot.AsOfUtc);
        Assert.Equal("Сервер 404 гэж хариулав.", snapshot.UnavailableReasonMn);
        Assert.Empty(snapshot.ChildrenOf(""));
    }

    [Fact]
    public void ALoadedCopySaysNOTHINGIsWrong()
    {
        // The picker shows UnavailableReasonMn only while there is nothing to
        // choose from, so a loaded copy that still carried a reason would put a
        // stale complaint under a full list the moment that rule was relaxed.
        var snapshot = new AdministrativeUnitSnapshot([Ulaanbaatar], asOfUtc: null);

        Assert.Equal("", snapshot.UnavailableReasonMn);
        Assert.Equal(1, snapshot.Count);
    }

    [Fact]
    public void ThePickerRunsOnASnapshotUnchanged()
    {
        // The point of the interface, checked rather than asserted in prose: the
        // rules finished against fixtures work on the real loaded copy, and the
        // label still comes from the parent row.
        var snapshot = new AdministrativeUnitSnapshot(
            [Ulaanbaatar, Baganuur, FirstKhoroo],
            new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero));
        var picker = new AdministrativeUnitPicker(snapshot);

        picker.ChooseProvince(snapshot.ChildrenOf("").Single());
        picker.ChooseDistrict(snapshot.ChildrenOf("511").Single());
        picker.ChooseWard(snapshot.ChildrenOf("51101").Single());

        Assert.Equal("Дүүрэг", picker.DistrictChoices().LabelMn);
        Assert.Equal("Хороо", picker.WardChoices().LabelMn);
        Assert.Equal(
            new DateTimeOffset(2026, 9, 6, 8, 0, 0, TimeSpan.Zero),
            picker.ToLocation().CatalogueAsOfUtc);
    }
}
