using System.IO;
using System.Text.Json;

namespace ErkS.Studio;

/// <summary>
/// WHO WAS HERE LAST, and nothing more.
///
/// 🔴 AN E-MAIL IS AN IDENTIFIER, NOT A CREDENTIAL. That distinction is the
/// whole licence for this file to exist: it holds the address and the name a
/// person is known by, so the sign-in can greet them instead of interrogating
/// them, and it holds NO password, NO token and NO activation. Signing in still
/// costs exactly what it cost before - the password - because that is the part
/// that proves anything.
///
/// The record belongs to the DEVICE, not to the account. Handing a machine to a
/// seat erases the owner's credential, and that rule is untouched here: there is
/// nothing in this file for it to erase. What follows from device ownership is
/// that a seated machine may be a SHARED machine, so a way to remove this must
/// exist and be visible where the name is shown.
/// </summary>
internal sealed record StudioRememberedProfile
{
    /// <summary>The address to put in the form. An identifier.</summary>
    public required string Email { get; init; }

    /// <summary>
    /// What the server called them, verbatim.
    ///
    /// Stored raw rather than as the label a screen would print: «Миний
    /// бүртгэл» is a presentation decision, and a decision written into storage
    /// cannot be revised without a migration.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// The server this identity belongs to. Kept WITH the address rather than
    /// beside it: the same address on two servers is two accounts, and offering
    /// one where the other is configured pre-fills a form with a stranger.
    /// </summary>
    public required string ServerUrl { get; init; }

    public required DateTimeOffset RememberedAtUtc { get; init; }
}

/// <summary>
/// The one remembered profile on this machine.
///
/// One, not a list: a switcher between several accounts is a bigger thing than
/// «who signed in last», and building the list first would settle that design
/// by accident.
/// </summary>
internal static class StudioRememberedProfiles
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private static readonly object Gate = new();

    /// <summary>
    /// Its own file, deliberately NOT account.json.
    ///
    /// account.json is deleted by signing out and again by handing the machine
    /// to a seat - which is correct, it carries the session. A profile kept
    /// inside it would be forgotten by exactly the two events after which
    /// remembering is worth something.
    /// </summary>
    public static string StorePath => Path.Combine(
        StudioAccountService.AccountDataRoot,
        "remembered-profile.json");

    public static StudioRememberedProfile? Read()
    {
        lock (Gate)
        {
            try
            {
                if (!File.Exists(StorePath))
                    return null;
                StudioRememberedProfile? value = JsonSerializer.Deserialize<StudioRememberedProfile>(
                    File.ReadAllText(StorePath),
                    JsonOptions);
                if (value is null ||
                    string.IsNullOrWhiteSpace(value.Email) ||
                    string.IsNullOrWhiteSpace(value.ServerUrl))
                {
                    // Half a profile is not one. Without an address there is
                    // nobody to greet, and without a server there is no telling
                    // whose account the address names.
                    return null;
                }
                return value;
            }
            catch (Exception exception) when (
                exception is IOException or JsonException or UnauthorizedAccessException
                    or NotSupportedException)
            {
                // Unreadable means «ask for everything», which is what the form
                // did before this file existed. Nothing is lost but a greeting.
                return null;
            }
        }
    }

    /// <summary>
    /// Remembers who just signed in. Silent on failure: a greeting that could
    /// not be stored is not worth failing a successful sign-in for.
    /// </summary>
    public static void Remember(string serverUrl, string email, string displayName)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(serverUrl))
            return;
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(StorePath)!);
                File.WriteAllText(
                    StorePath,
                    JsonSerializer.Serialize(
                        new StudioRememberedProfile
                        {
                            Email = email.Trim(),
                            DisplayName = (displayName ?? "").Trim(),
                            ServerUrl = serverUrl.Trim(),
                            RememberedAtUtc = DateTimeOffset.UtcNow,
                        },
                        JsonOptions));
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or JsonException)
            {
            }
        }
    }

    /// <summary>
    /// Removes it. NOT tidy-up and NOT optional: a seated machine can be a
    /// shared machine, and somebody who leaves must be able to take their name
    /// off the screen the next person will see.
    /// </summary>
    public static void Forget()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(StorePath))
                    File.Delete(StorePath);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }
}

/// <summary>What the sign-in window asks for.</summary>
internal enum LoginPromptMode
{
    /// <summary>Address and password. The form as it always was.</summary>
    AskForEverything,

    /// <summary>A name, a letter avatar, and the password. Nothing else.</summary>
    AskOnlyForPassword,
}

/// <summary>
/// Whether the sign-in window greets somebody or asks who they are.
///
/// A function of the profile on disk and the server actually configured, so the
/// answer can be stated in a test rather than found by signing in - the third
/// rule pulled out of a method that builds controls, for the reason the first
/// two were.
/// </summary>
internal static class StudioLoginPrompt
{
    public static LoginPromptMode Mode(StudioRememberedProfile? remembered, string serverUrl) =>
        Recognised(remembered, serverUrl) is null
            ? LoginPromptMode.AskForEverything
            : LoginPromptMode.AskOnlyForPassword;

    /// <summary>
    /// The profile this form may greet, or null.
    ///
    /// The server has to match. The same address on a different server is a
    /// different account, and greeting it by name would have the form assert
    /// something it has not got.
    /// </summary>
    public static StudioRememberedProfile? Recognised(
        StudioRememberedProfile? remembered,
        string serverUrl)
    {
        if (remembered is null || string.IsNullOrWhiteSpace(remembered.Email))
            return null;
        if (!Same(remembered.ServerUrl, serverUrl))
            return null;
        return remembered;
    }

    private static bool Same(string left, string right) =>
        (left ?? "").Trim().TrimEnd('/')
            .Equals((right ?? "").Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// How an account is written on screen. One copy, because the letter avatar and
/// the name are drawn in two windows now, and a second copy of a rule is how the
/// two drift apart.
/// </summary>
internal static class StudioAccountDisplay
{
    /// <summary>
    /// Up to two initials, or the product's own letters when there is no name
    /// to take them from.
    /// </summary>
    public static string Initials(string displayName)
    {
        string[] words = (displayName ?? "")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
            return "ES";
        return string.Concat(words.Take(2).Select(word => char.ToUpperInvariant(word[0])));
    }

    /// <summary>
    /// What to call somebody whose name the server never gave, or gave as their
    /// own address. Printing the address where the name goes tells nobody
    /// anything they cannot already see.
    /// </summary>
    public static string NameOrFallback(string displayName, string email, string fallback)
    {
        string trimmed = (displayName ?? "").Trim();
        return string.IsNullOrWhiteSpace(trimmed) ||
               trimmed.Equals((email ?? "").Trim(), StringComparison.OrdinalIgnoreCase)
            ? fallback
            : trimmed;
    }
}
