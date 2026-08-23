using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErkS.Platform.Core;

namespace ErkS.Studio.Tests;

public sealed class StudioDeviceIdentityMigrationTests
{
    [Fact]
    public void BuildFingerprints_UsesCanonicalAndStudioLegacySalts()
    {
        StudioDeviceFingerprints fingerprints = StudioDeviceIdentity.BuildFingerprints(
            machineName: "studio-machine",
            userName: "studio-user",
            machineGuid: "machine-guid",
            sid: "user-sid");

        Assert.Equal(
            Hash("Erk-S device v1|studio-machine|studio-user|machine-guid|user-sid"),
            fingerprints.Canonical);
        Assert.Equal(
            Hash("Erk-S Studio device v1|studio-machine|studio-user|machine-guid|user-sid"),
            fingerprints.Legacy);
        Assert.NotEqual(fingerprints.Canonical, fingerprints.Legacy);
    }

    [Fact]
    public void ValidateStoredFingerprint_AcceptsCanonicalWithoutRewrite()
    {
        StudioDeviceFingerprintValidation validation =
            StudioDeviceIdentity.ValidateStoredFingerprint(
                StudioDeviceIdentity.Fingerprints.Canonical);

        Assert.True(validation.IsValid);
        Assert.False(validation.RequiresCanonicalRewrite);
    }

    [Fact]
    public void ValidateStoredFingerprint_AcceptsLegacyAndRequestsCanonicalRewrite()
    {
        StudioDeviceFingerprintValidation validation =
            StudioDeviceIdentity.ValidateStoredFingerprint(
                StudioDeviceIdentity.Fingerprints.Legacy);

        Assert.True(validation.IsValid);
        Assert.True(validation.RequiresCanonicalRewrite);
        Assert.True(StudioDeviceIdentity.TryMigrateStoredFingerprint(
            StudioDeviceIdentity.Fingerprints.Legacy,
            out string migratedFingerprint));
        Assert.Equal(
            StudioDeviceIdentity.Fingerprints.Canonical,
            migratedFingerprint);
    }

    [Fact]
    public void LegacyCompanionGrant_RemainsUsableUntilLazyRewrite()
    {
        DateTimeOffset checkedAtUtc = DateTimeOffset.UtcNow;
        string legacy = StudioDeviceIdentity.Fingerprints.Legacy;

        StudioCompanionEntitlement? entitlement = StudioCompanionPolicy.ReadStoredGrant(
            studioCompanion: true,
            companionExpiresAtUtc: checkedAtUtc.AddDays(30),
            checkedAtUtc: checkedAtUtc,
            storedDeviceFingerprint: legacy,
            currentDeviceFingerprint:
                StudioDeviceIdentity.FingerprintForStoredGrant(legacy));

        Assert.NotNull(entitlement);
        Assert.True(entitlement!.StudioCompanion);
    }

    [Theory]
    [InlineData("")]
    [InlineData("another-device")]
    public void ValidateStoredFingerprint_RejectsUnknownDevice(string storedFingerprint)
    {
        StudioDeviceFingerprintValidation validation =
            StudioDeviceIdentity.ValidateStoredFingerprint(storedFingerprint);

        Assert.False(validation.IsValid);
        Assert.False(validation.RequiresCanonicalRewrite);
    }

    [Fact]
    public void DeviceBoundRequests_SerializeCanonicalAndLegacyFingerprints()
    {
        StudioDeviceBoundRequest[] requests =
        {
            new StudioLicenseActivateRequest(),
            new StudioLicenseValidateRequest(),
            new StudioSessionRequest(),
            new StudioSessionRefreshRequest(),
        };

        foreach (StudioDeviceBoundRequest request in requests)
        {
            request.DeviceFingerprint = "canonical";
            request.LegacyDeviceFingerprint = "legacy";
            string json = JsonSerializer.Serialize(
                request,
                request.GetType(),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            using JsonDocument document = JsonDocument.Parse(json);

            Assert.Equal(
                "canonical",
                document.RootElement.GetProperty("deviceFingerprint").GetString());
            Assert.Equal(
                "legacy",
                document.RootElement.GetProperty("legacyDeviceFingerprint").GetString());
        }
    }

    private static string Hash(string material) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material)))
            .ToLowerInvariant();
}
