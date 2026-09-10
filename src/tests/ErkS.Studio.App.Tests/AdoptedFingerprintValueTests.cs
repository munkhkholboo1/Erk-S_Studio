using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// What the adopted fingerprint actually IS.
///
/// 🔴 SEPARATE CLASS ON PURPOSE. These assertions move StudioDeviceIdentity's
/// static registered fingerprint, and the first attempt to put them beside the
/// store tests turned another class red - a sibling that reads the same static
/// while xunit ran the two collections in parallel. The gate caught it before
/// it was committed.
///
/// Shared mutable state is a collection's business, and this file joins the one
/// that already owns it rather than inventing a second claim on it.
/// </summary>
[Collection(StudioDeviceIdentityCollection.Name)]
public sealed class AdoptedFingerprintValueTests
{
    [Fact]
    public void THEADOPTEDCanonicalIsTheKEYAndTheTraitStillRidesAlong()
    {
        // 🔴 THE MEASUREMENT THAT SETTLED THIS, AS A RULE. On the machine that
        // broke, the two values were genuinely different:
        //
        //     trait canonical   63b30cb2…   (what a machine with no session sends)
        //     stored by server  11188802EB… (what it was seated under)
        //
        // and BOTH exist on the server for the same computer, from different
        // days. So the canonical form has to become the KEY once adopted -
        // the server looks its device key up by canonical ONLY - while the
        // trait value keeps riding in the legacy slot, because the seat state
        // is matched on either and older rows carry the trait form.
        string key = new('E', 64);
        string traitBefore = StudioDeviceIdentity.TraitBasedFingerprints.Canonical;
        try
        {
            StudioDeviceIdentity.UseRegisteredKeyFingerprint(key);

            Assert.Equal(key, StudioDeviceIdentity.Fingerprint);
            Assert.Equal(key, StudioDeviceIdentity.Fingerprints.Canonical);

            // The trait form is not thrown away - it is what an older seat row
            // was written under, and dropping it would strand exactly the
            // machines this change exists to rescue.
            Assert.Equal(traitBefore, StudioDeviceIdentity.Fingerprints.Legacy);
        }
        finally
        {
            StudioDeviceIdentity.ForgetRegisteredKeyFingerprint();
        }

        // And with nothing adopted it falls back to the trait form, which is
        // the right answer for a machine that has never registered a key.
        Assert.Equal(traitBefore, StudioDeviceIdentity.Fingerprints.Canonical);
    }
}
