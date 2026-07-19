using ErkS.Platform.Core;

namespace ErkS.Studio;

internal sealed record StudioRegisteredPersonName(
    string FamilyName,
    string GivenName,
    string DisplayName);

internal static class StudioRegisteredPersonNameResolver
{
    public static StudioRegisteredPersonName Resolve(
        string? familyName,
        string? givenName,
        string? displayName,
        bool displayNameUsesCanonicalProfileOrder)
    {
        string family = Normalize(familyName);
        string given = Normalize(givenName);
        string display = Normalize(displayName);
        if (string.IsNullOrWhiteSpace(family) &&
            string.IsNullOrWhiteSpace(given) &&
            displayNameUsesCanonicalProfileOrder)
        {
            (family, given) = SplitCanonicalProfileDisplayName(display);
        }

        return new StudioRegisteredPersonName(
            family,
            given,
            MongolianPersonNameFormatter.ForDisplay(family, given, display));
    }

    public static void Apply(
        StudioCloudParticipant participant,
        StudioRegisteredPersonName name)
    {
        participant.FamilyName = name.FamilyName;
        participant.GivenName = name.GivenName;
        participant.DisplayName = name.DisplayName;
    }

    public static void Apply(
        StudioCloudAccountLookupResponse account,
        StudioRegisteredPersonName name)
    {
        account.FamilyName = name.FamilyName;
        account.GivenName = name.GivenName;
        account.DisplayName = name.DisplayName;
    }

    private static (string FamilyName, string GivenName) SplitCanonicalProfileDisplayName(
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName) ||
            displayName.Contains('@', StringComparison.Ordinal) ||
            displayName.Contains('.', StringComparison.Ordinal))
        {
            return ("", "");
        }

        string[] parts = displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length < 2
            ? ("", "")
            : (parts[0], string.Join(" ", parts.Skip(1)));
    }

    private static string Normalize(string? value) => string.Join(
        " ",
        (value ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
