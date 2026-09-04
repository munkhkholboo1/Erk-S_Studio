using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioDeviceKeyPairTests
{
    [Fact]
    public void TheKeyIdIsDerivedFromThePublicKeyAndNothingElse()
    {
        // Self-certifying is the whole point: a peer checks that whoever it is
        // talking to owns the id, without asking a server. So it must come from
        // the key and be reproducible from the public half alone.
        StudioDeviceKeyPair device = StudioDeviceKeyPair.Create();

        Assert.Equal(16, device.DeviceKeyId.Length);
        Assert.Equal(device.DeviceKeyId, StudioDeviceKeyPair.DeriveKeyId(device.SigningPublicKey));
    }

    [Fact]
    public void TwoDevicesDoNotShareAKeyId()
    {
        Assert.NotEqual(
            StudioDeviceKeyPair.Create().DeviceKeyId,
            StudioDeviceKeyPair.Create().DeviceKeyId);
    }

    [Fact]
    public void OnlyTheHolderOfThePrivateKeyCanSignForAKeyId()
    {
        StudioDeviceKeyPair device = StudioDeviceKeyPair.Create();
        StudioDeviceKeyPair impostor = StudioDeviceKeyPair.Create();
        byte[] payload = Encoding.UTF8.GetBytes("snapshot 17");

        byte[] signature = device.Sign(payload);

        Assert.True(StudioDeviceKeyPair.Verify(device.SigningPublicKey, payload, signature));
        // The same signature checked against another device's key: this is what
        // stops a snapshot claiming a writer it does not own.
        Assert.False(StudioDeviceKeyPair.Verify(impostor.SigningPublicKey, payload, signature));
    }

    [Fact]
    public void AChangedPayloadFailsVerification()
    {
        StudioDeviceKeyPair device = StudioDeviceKeyPair.Create();
        byte[] signature = device.Sign(Encoding.UTF8.GetBytes("snapshot 17"));

        Assert.False(StudioDeviceKeyPair.Verify(
            device.SigningPublicKey,
            Encoding.UTF8.GetBytes("snapshot 18"),
            signature));
    }

    [Fact]
    public void TwoDevicesReachTheSameSharedKeyWithoutSendingIt()
    {
        // What the escrow ring rests on: a project key can be wrapped to a
        // member without the key itself travelling.
        StudioDeviceKeyPair owner = StudioDeviceKeyPair.Create();
        StudioDeviceKeyPair member = StudioDeviceKeyPair.Create();

        byte[] fromOwner = owner.DeriveSharedKey(member.AgreementPublicKey, "erks.project-key");
        byte[] fromMember = member.DeriveSharedKey(owner.AgreementPublicKey, "erks.project-key");

        Assert.Equal(fromOwner, fromMember);
        Assert.NotEmpty(fromOwner);
    }

    [Fact]
    public void ADifferentPurposeGivesADifferentKey()
    {
        // So a secret agreed for one use cannot be replayed as another.
        StudioDeviceKeyPair owner = StudioDeviceKeyPair.Create();
        StudioDeviceKeyPair member = StudioDeviceKeyPair.Create();

        Assert.NotEqual(
            owner.DeriveSharedKey(member.AgreementPublicKey, "erks.project-key"),
            owner.DeriveSharedKey(member.AgreementPublicKey, "erks.snapshot-key"));
    }
}
