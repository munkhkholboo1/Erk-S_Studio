using System.Text;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// What this machine calls itself must not depend on who is signed in.
///
/// 🔴 THE DEFECT THAT COST A REAL USER THEIR SEATED MACHINE. The registered key
/// fingerprint was installed by exactly one path, and that path returns at its
/// first line when there is no owner session:
///
///     seated (owner present)   canonical = KEY fingerprint  → stored by the server
///     reopened (no owner)      canonical = trait fingerprint → KEY never sent
///
/// Entering bot state erases the owner credential BY DESIGN, so a seated machine
/// reopening itself always took the second branch. The server looks its device
/// key up by canonical only, found nothing, and refused - and the machine could
/// never resume itself without an owner signing in first. The person described
/// it exactly: «анх үүсэхдээ сайхан шилжинэ … ахиж нээхэд шилжиж орж чадахгүй».
///
/// A fingerprint IDENTIFIES. Authorisation is the signature over the server's
/// nonce, which none of this touches - so reading the registration without
/// naming an account grants nobody anything.
/// </summary>
[Collection(StudioDataRootCollection.Name)]
public sealed class FingerprintDoesNotDependOnWhoIsSignedInTests : IDisposable
{
    private readonly string dataRoot = Path.Combine(
        Path.GetTempPath(), "erks-fingerprint-session-tests", Guid.NewGuid().ToString("N"));
    private readonly string? previousRoot =
        Environment.GetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT");

    public FingerprintDoesNotDependOnWhoIsSignedInTests()
    {
        Directory.CreateDirectory(dataRoot);
        Environment.SetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT", dataRoot);
    }

    [Fact]
    public void AREGISTEREDKeyIsFoundWithoutNamingAnAccount()
    {
        // The registration file is keyed by (fingerprint, account) because one
        // Windows user can sign in with two accounts. The QUESTION here is only
        // «is this machine's key known», and that has an answer without an
        // account - which is the whole fix.
        string fingerprint = new('A', 64);
        StudioDeviceKeyStore.MarkRegistered(fingerprint, "someone@erk-s.mn");

        Assert.True(StudioDeviceKeyStore.IsRegisteredToAnyAccount(fingerprint));

        // And it stays specific: a different key is not "registered" just
        // because some key is.
        Assert.False(StudioDeviceKeyStore.IsRegisteredToAnyAccount(new string('B', 64)));
    }

    [Fact]
    public void ANUnregisteredMachineSaysSO()
    {
        // The ordinary state of a machine that has never registered a key. The
        // trait fingerprint is then the right answer and must not be replaced.
        Assert.False(StudioDeviceKeyStore.IsRegisteredToAnyAccount(new string('C', 64)));
        Assert.False(StudioDeviceKeyStore.IsRegisteredToAnyAccount(""));
    }

    [Fact]
    public void THEAdoptionRunsBeforeAnythingAsksWhoThisDeviceIs()
    {
        // 🔴 THE ORDER IS THE RULE. A seated machine puts its lock screen up at
        // start-up and the PIN behind it resumes the seat - if the fingerprint
        // has not been adopted by then, that resume asks the server about a
        // device it has never heard of.
        string shell = ReadAppSource("ShellView.cs");

        int adopt = shell.IndexOf("AdoptRegisteredDeviceKeyFingerprint();", StringComparison.Ordinal);
        int lockScreen = shell.IndexOf("InstallBotLockIfSeated();", StringComparison.Ordinal);

        Assert.True(adopt > 0, "the fingerprint is never adopted at start-up");
        Assert.True(lockScreen > adopt, "the seat's lock screen must come up AFTER the adoption");
    }

    [Fact]
    public void ADOPTIONDoesNotAskWhoIsSignedIn()
    {
        // The single assertion that holds the whole defect shut. If anybody
        // ever reintroduces an account dependency here, a seated machine goes
        // straight back to sending a fingerprint the server was never told
        // about - and it will look like a server problem again.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string body = MethodBody(source, "private static void AdoptRegisteredDeviceKeyFingerprint()");

        Assert.DoesNotContain("account.", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Email", body, StringComparison.Ordinal);
        Assert.DoesNotContain("IsSignedIn", body, StringComparison.Ordinal);

        // The positive control: it must really be doing the adoption, or the
        // three absences above are true of an empty method.
        Assert.Contains("StudioDeviceIdentity.UseRegisteredKeyFingerprint(", body, StringComparison.Ordinal);
        Assert.Contains("IsRegisteredToAnyAccount(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void THEOldSessionBoundPathIsStillTheONEThatRegisters()
    {
        // Adoption reads what registration wrote. Registering still needs an
        // account - the server records the key against one - and that is
        // correct. Only the READING was wrong to require it.
        string source = ReadAppSource("ShellView.BotSeat.cs");
        string register = MethodBody(source, "private async Task<bool> EnsureDeviceKeyRegisteredAsync()");

        Assert.Contains("account.Current?.Email", register, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start > 0, signature + " was not found");
        int end = source.IndexOf("\n    }", start, StringComparison.Ordinal);
        Assert.True(end > start, "the end of " + signature + " was not found");
        return source[start..end];
    }

    private static string ReadAppSource(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(
                directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8);
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT", previousRoot);
        try
        {
            if (Directory.Exists(dataRoot))
                Directory.Delete(dataRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
