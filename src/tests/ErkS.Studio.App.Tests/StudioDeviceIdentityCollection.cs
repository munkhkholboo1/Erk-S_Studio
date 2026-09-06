namespace ErkS.Studio.App.Tests;

/// <summary>
/// Test classes that read or change the device fingerprint.
///
/// 🔴 THE SAME SHAPE AS <see cref="StudioDataRootCollection"/>, and found the
/// same way - a failure that would not reproduce.
///
/// <c>StudioDeviceIdentity.Fingerprints</c> is not a pure function of the
/// machine: once a device key is registered it returns the KEY's hash, and the
/// registration is held in a mutable static that
/// <c>UseRegisteredKeyFingerprint</c> sets for the whole process. So a test that
/// registers a key changes what every other test sees, and one that asserts the
/// trait-based pair fails whenever the two happen to overlap.
///
/// It failed exactly once in a long day of full runs and passed three times in
/// isolation immediately afterwards - which is the signature of shared state
/// rather than of a bug in the code under test, and the reason to look for a
/// racing writer instead of re-running until green. The writer was
/// <c>StudioDeviceKeyStoreTests</c>.
///
/// A flaky gate is worse than a failing one: it teaches the reader that red is
/// noise. That lesson cost a red-test push earlier the same day.
/// </summary>
[CollectionDefinition(Name)]
public sealed class StudioDeviceIdentityCollection
{
    public const string Name = "studio-device-identity";
}
