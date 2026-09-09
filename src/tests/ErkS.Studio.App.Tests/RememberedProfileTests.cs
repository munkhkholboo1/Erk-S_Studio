using System.Reflection;
using System.Text;
using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// Remembering WHO was here, and nothing that would let anyone in.
///
/// 🔴 AN E-MAIL IS AN IDENTIFIER, NOT A CREDENTIAL - and that is the entire
/// licence for this feature. The password is still asked for in full every time.
/// What changes is that a person on their own machine is greeted by name instead
/// of being asked who they are, which the form did every single time because the
/// only thing that remembered them was account.json - deleted by signing out,
/// and deleted again by handing the machine to a seat.
///
/// The profile belongs to the DEVICE. A seated machine can be a shared machine,
/// so a way to remove it is not a convenience.
/// </summary>
[Collection(StudioDataRootCollection.Name)]
public sealed class RememberedProfileTests : IDisposable
{
    private readonly string dataRoot = Path.Combine(
        Path.GetTempPath(),
        "erks-remembered-profile-tests",
        Guid.NewGuid().ToString("N"));
    private readonly string? previousRoot =
        Environment.GetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT");

    public RememberedProfileTests()
    {
        Directory.CreateDirectory(dataRoot);
        Environment.SetEnvironmentVariable("ERKS_STUDIO_DATA_ROOT", dataRoot);
    }

    /// <summary>
    /// Every field this record is ALLOWED to have. Named, so that adding a
    /// fifth is a decision somebody makes here in the open rather than a line
    /// that slips into storage - which is the way a password gets stored.
    /// </summary>
    private static readonly string[] Identifying =
        ["Email", "DisplayName", "ServerUrl", "RememberedAtUtc"];

    private static readonly string[] NeverStored =
        ["password", "token", "secret", "credential", "activation", "pin", "key", "hash"];

    [Fact]
    public void THERecordCanHoldNothingBUTIdentification()
    {
        // 🔴 THE ONE RULE THAT MAKES THIS SAFE, AND IT IS A RULE ABOUT THE
        // SHAPE, not about today's callers. A field added later would be
        // written to disk by code that already exists, so the guard has to be
        // on the type rather than on the writer.
        string[] actual = typeof(StudioRememberedProfile)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != "EqualityContract")
            .Select(property => property.Name)
            .ToArray();

        Assert.NotEmpty(actual);
        foreach (string name in actual)
        {
            Assert.True(
                Identifying.Contains(name, StringComparer.Ordinal),
                $"«{name}» is stored on this device and was never declared as identification");
        }

        // And the allow-list is itself held to the rule, so widening it does not
        // quietly become the way round it.
        foreach (string name in Identifying)
        {
            foreach (string forbidden in NeverStored)
            {
                Assert.False(
                    name.Contains(forbidden, StringComparison.OrdinalIgnoreCase),
                    $"«{name}» names something that must never be stored on this device");
            }
        }
    }

    [Fact]
    public void WHATReachesTheDiskIsTheAddressTheNameAndTheServer()
    {
        StudioRememberedProfiles.Remember("https://era.erk-s.mn", "gerlee@erk-s.mn", "Гэрлээ Б.");

        string raw = File.ReadAllText(StudioRememberedProfiles.StorePath, Encoding.UTF8);
        using JsonDocument document = JsonDocument.Parse(raw);
        string[] keys = document.RootElement.EnumerateObject()
            .Select(property => property.Name)
            .ToArray();

        Assert.Equal(
            ["displayName", "email", "rememberedAtUtc", "serverUrl"],
            keys.Order());
    }

    [Fact]
    public void ITSurvivesTheTwoThingsThatEraseAnAccount()
    {
        // Signing out deletes account.json, and so does handing this machine to
        // a seat. Those are exactly the two moments after which remembering is
        // worth something - so the profile cannot live in that file, and this
        // is the property, not a promise about today's code.
        StudioRememberedProfiles.Remember("https://era.erk-s.mn", "gerlee@erk-s.mn", "Гэрлээ Б.");
        string accountJson = Path.Combine(dataRoot, "account.json");
        File.WriteAllText(accountJson, "{}");

        Assert.NotEqual(accountJson, StudioRememberedProfiles.StorePath);
        File.Delete(accountJson);

        StudioRememberedProfile? kept = StudioRememberedProfiles.Read();
        Assert.NotNull(kept);
        Assert.Equal("gerlee@erk-s.mn", kept.Email);
        Assert.Equal("Гэрлээ Б.", kept.DisplayName);
    }

    [Fact]
    public void FORGETTINGLeavesNothingBehind()
    {
        StudioRememberedProfiles.Remember("https://era.erk-s.mn", "gerlee@erk-s.mn", "Гэрлээ Б.");
        Assert.NotNull(StudioRememberedProfiles.Read());

        StudioRememberedProfiles.Forget();

        Assert.Null(StudioRememberedProfiles.Read());
        Assert.False(File.Exists(StudioRememberedProfiles.StorePath));
    }

    [Fact]
    public void FORGETTINGWhenThereIsNothingToForgetIsNotAnError()
    {
        StudioRememberedProfiles.Forget();
        Assert.Null(StudioRememberedProfiles.Read());
    }

    [Fact]
    public void ANamelessSignInIsStillRemembered()
    {
        // The server does not always send a display name. The address is the
        // part that identifies, so the profile is worth keeping without it -
        // the greeting falls back to a label.
        StudioRememberedProfiles.Remember("https://era.erk-s.mn", "gerlee@erk-s.mn", "");

        StudioRememberedProfile? kept = StudioRememberedProfiles.Read();
        Assert.NotNull(kept);
        Assert.Equal("", kept.DisplayName);
    }

    [Fact]
    public void ASignInWithNoAddressRemembersNOTHING()
    {
        // Half a profile is not one, and an entry with no address would greet
        // nobody while still holding a name on a machine somebody else uses.
        StudioRememberedProfiles.Remember("https://era.erk-s.mn", "   ", "Гэрлээ Б.");

        Assert.Null(StudioRememberedProfiles.Read());
        Assert.False(File.Exists(StudioRememberedProfiles.StorePath));
    }

    [Fact]
    public void ARewrittenProfileREPLACESTheOldOne()
    {
        // One profile, not a list. Somebody signing in after somebody else must
        // not leave the first person's name on this machine.
        StudioRememberedProfiles.Remember("https://era.erk-s.mn", "gerlee@erk-s.mn", "Гэрлээ Б.");
        StudioRememberedProfiles.Remember("https://era.erk-s.mn", "otgon@erk-s.mn", "Отгон Д.");

        StudioRememberedProfile? kept = StudioRememberedProfiles.Read();
        Assert.NotNull(kept);
        Assert.Equal("otgon@erk-s.mn", kept.Email);
        Assert.Equal("Отгон Д.", kept.DisplayName);
    }

    [Fact]
    public void ANUnreadableProfileASKSForEverythingRatherThanGuessing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(StudioRememberedProfiles.StorePath)!);
        File.WriteAllText(StudioRememberedProfiles.StorePath, "{ this is not json");

        Assert.Null(StudioRememberedProfiles.Read());
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
