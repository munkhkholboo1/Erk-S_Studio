using ErkS.Platform.Core;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// A machine must recognise a source it bound itself, in every form it has ever
/// signed with.
///
/// 🔴 THE OWNER'S OWN WORK WAS REFUSED ON THE COMPUTER THAT MADE IT. They said
/// it plainly: «би бүх эх үүсвэрийг энэ комноос оруулсан … программ өөрөө
/// андуурч байна». One of their sources carries a fingerprint their machine no
/// longer answers with, and the sync reported it as «энэ төхөөрөмж дээр бэлдэх
/// боломжгүй» - about their own device.
///
/// 🔴 NOTHING OVERWROTE IT. Both writers of a binding are deliberate acts - the
/// person adding a source, and the person relinking one - and the backfill path
/// refuses outright when a binding already exists. What changed was the machine:
/// the canonical fingerprint depends on its MODE. Adopt a registered device key
/// (which trying the machine as a seat does) and canonical becomes the key form;
/// run without one and it is the trait form. The machine stopped recognising a
/// name it had used itself.
/// </summary>
[Collection(StudioDeviceIdentityCollection.Name)]
public sealed class AMachineRecognisesItsOwnBindingTests : IDisposable
{
    public AMachineRecognisesItsOwnBindingTests() =>
        StudioDeviceIdentity.ForgetRegisteredKeyFingerprint();

    [Fact]
    public void THEOLDSALTFormIsStillOURSAfterAKeyIsAdopted()
    {
        // 🔴 THE OWNER'S ACTUAL CASE, AND THE VALUE IN THEIR FILE IS THIS ONE.
        // The orphaned source carries f3f4aa2a… - computed here, on their
        // machine, it is this device's OLDER-SALT trait form. Not another
        // computer, not a bot: a name this very machine used to sign with.
        //
        // It was accepted while the machine ran without a device key, because
        // the pair it offered was (trait, old-salt). Adopting a key - which
        // trying the machine as a seat does - made the pair (key, trait) and
        // pushed the old-salt form OUT of the accepted set. The person's own
        // work became unrecognisable on the machine that made it.
        var source = new ProjectDesignSource { Id = "src-1", Name = "Revit" };
        StudioLocalSourceBindingPolicy.Bind(
            source,
            "munkhkholboo@gmail.com",
            StudioDeviceIdentity.TraitBasedFingerprints.Legacy);

        StudioDeviceIdentity.UseRegisteredKeyFingerprint(new string('F', 64));

        Assert.True(
            StudioLocalSourceBindingPolicy.IsLocal(
                source,
                "munkhkholboo@gmail.com",
                StudioDeviceIdentity.Fingerprint,
                hasVerifiedPayload: true),
            "adopting a device key orphaned a source bound under this machine's own older name");
    }

    [Fact]
    public void AKEYThisMachineNoLongerHoldsIsCORRECTLYRefused()
    {
        // 🔴 THE LIMIT, AND IT IS DELIBERATE. A binding naming a device key this
        // machine cannot present any more is NOT accepted - and that is right.
        // The key is proof the machine holds; without it, the only evidence is a
        // value written in the project file, which any machine could carry.
        // Accepting it would make the binding self-certifying.
        //
        // Written down so the limit is a decision rather than an oversight: the
        // recovery for that state is re-registering the key or relinking, not a
        // wider match.
        string key = new('F', 64);
        StudioDeviceIdentity.UseRegisteredKeyFingerprint(key);
        var source = new ProjectDesignSource { Id = "src-1", Name = "Revit" };
        StudioLocalSourceBindingPolicy.Bind(
            source, "munkhkholboo@gmail.com", StudioDeviceIdentity.Fingerprint);

        StudioDeviceIdentity.ForgetRegisteredKeyFingerprint();

        Assert.False(
            StudioLocalSourceBindingPolicy.IsLocal(
                source,
                "munkhkholboo@gmail.com",
                StudioDeviceIdentity.Fingerprint,
                hasVerifiedPayload: true));
    }

    [Fact]
    public void ASourceBoundUnderTheTRAITFormIsStillOURSAfterAdoptingAKey()
    {
        // The same failure from the other direction, which is the one a person
        // meets when they try the seat AFTER doing ordinary work.
        var source = new ProjectDesignSource { Id = "src-1", Name = "AutoCAD" };
        StudioLocalSourceBindingPolicy.Bind(
            source, "munkhkholboo@gmail.com", StudioDeviceIdentity.Fingerprint);

        StudioDeviceIdentity.UseRegisteredKeyFingerprint(new string('F', 64));

        Assert.True(
            StudioLocalSourceBindingPolicy.IsLocal(
                source,
                "munkhkholboo@gmail.com",
                StudioDeviceIdentity.Fingerprint,
                hasVerifiedPayload: true),
            "adopting a device key orphaned work bound before it");
    }

    [Fact]
    public void ANOTHERMachinesBindingIsStillREFUSED()
    {
        // 🔴 THE NEGATIVE CONTROL, AND THE WHOLE REASON THIS IS SAFE. Widening
        // acceptance must add forms of THIS device and nothing else: a binding
        // from a genuinely different computer stays refused, or the fix would
        // have handed every machine everybody's work.
        var source = new ProjectDesignSource { Id = "src-1", Name = "Revit" };
        StudioLocalSourceBindingPolicy.Bind(
            source, "munkhkholboo@gmail.com", new string('A', 64));

        Assert.False(
            StudioLocalSourceBindingPolicy.IsLocal(
                source,
                "munkhkholboo@gmail.com",
                StudioDeviceIdentity.Fingerprint,
                hasVerifiedPayload: true));
    }

    [Fact]
    public void ANOTHERACCOUNTSBindingIsStillRefusedOnThisMachine()
    {
        // The account half of the binding is untouched by this change, and has
        // to stay that way: two people share a machine far more often than one
        // person's machine changes its name.
        var source = new ProjectDesignSource { Id = "src-1", Name = "Revit" };
        StudioLocalSourceBindingPolicy.Bind(
            source, "somebody.else@erk-s.mn", StudioDeviceIdentity.Fingerprint);

        Assert.False(
            StudioLocalSourceBindingPolicy.IsLocal(
                source,
                "munkhkholboo@gmail.com",
                StudioDeviceIdentity.Fingerprint,
                hasVerifiedPayload: true));
    }

    [Fact]
    public void EVERYFormInTheSetIsAFormOFTHISDevice()
    {
        // Said as an assertion rather than left to the comment: the set is the
        // registered key plus this machine's own two trait forms, and nothing
        // else may join it.
        string key = new('F', 64);
        StudioDeviceIdentity.UseRegisteredKeyFingerprint(key);

        IReadOnlyList<string> forms = StudioDeviceIdentity.AllFormsOfThisDevice;

        // Compared case-insensitively: the set normalises to upper case and the
        // trait pair is stored lower, which is a difference in spelling and not
        // in identity.
        Assert.Contains(key, forms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            StudioDeviceIdentity.TraitBasedFingerprints.Canonical, forms, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(
            StudioDeviceIdentity.TraitBasedFingerprints.Legacy, forms, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(3, forms.Count);

        // Without a key the set is the two trait forms - never empty, or a
        // machine would recognise nothing at all.
        StudioDeviceIdentity.ForgetRegisteredKeyFingerprint();
        Assert.Equal(2, StudioDeviceIdentity.AllFormsOfThisDevice.Count);
    }

    [Fact]
    public void ANEXPLICITLYNamedLegacyValueStillWins()
    {
        // The overload that names a legacy fingerprint is used by the migration
        // path, which is asking a different question - «is this THAT value» -
        // and must not be widened into «is this any of ours».
        var source = new ProjectDesignSource { Id = "src-1", Name = "Revit" };
        StudioLocalSourceBindingPolicy.Bind(
            source, "munkhkholboo@gmail.com", new string('B', 64));

        Assert.True(StudioLocalSourceBindingPolicy.MatchesBoundDevice(
            new string('B', 64), StudioDeviceIdentity.Fingerprint, new string('B', 64)));
        Assert.False(StudioLocalSourceBindingPolicy.MatchesBoundDevice(
            new string('B', 64), StudioDeviceIdentity.Fingerprint, new string('C', 64)));
    }

    public void Dispose() => StudioDeviceIdentity.ForgetRegisteredKeyFingerprint();
}
