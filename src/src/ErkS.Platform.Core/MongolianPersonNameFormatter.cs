using System.Globalization;

namespace ErkS.Platform.Core;

public static class MongolianPersonNameFormatter
{
    public static string ForDisplay(
        string? familyName,
        string? givenName,
        string? fallbackDisplayName = null)
    {
        string family = Normalize(familyName);
        string given = Normalize(givenName);
        string structured = string.Join(
            " ",
            new[] { family, given }.Where(value => !string.IsNullOrWhiteSpace(value)));
        return string.IsNullOrWhiteSpace(structured)
            ? Normalize(fallbackDisplayName)
            : structured;
    }

    public static string ForDocument(
        string? familyName,
        string? givenName,
        string? fallbackDisplayName = null)
    {
        string family = Normalize(familyName);
        string given = Normalize(givenName);
        if (!string.IsNullOrWhiteSpace(family) && !string.IsNullOrWhiteSpace(given))
        {
            string initial = StringInfo.GetNextTextElement(family).ToUpperInvariant();
            return $"{initial}.{given}";
        }

        // A legacy display label is kept intact when structured profile fields
        // are unavailable. Guessing its word order could swap family and given names.
        return ForDisplay(family, given, fallbackDisplayName);
    }

    public static string ForDocument(string? profileDisplayName)
    {
        string normalized = Normalize(profileDisplayName);
        if (string.IsNullOrWhiteSpace(normalized) ||
            normalized.Contains('@', StringComparison.Ordinal) ||
            normalized.Contains('.', StringComparison.Ordinal))
        {
            return normalized;
        }

        string[] parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return normalized;

        string familyName = parts[0];
        string givenName = string.Join(" ", parts.Skip(1));
        return ForDocument(familyName, givenName);
    }

    private static string Normalize(string? value) => string.Join(
        " ",
        (value ?? "")
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
