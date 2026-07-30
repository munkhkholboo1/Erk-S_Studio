using ErkS.Platform.Core;

namespace ErkS.Studio;

internal static class StudioSourcePurpose
{
    public static string Normalize(string? value)
    {
        string normalized = value?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(normalized))
            return "";
        return Enum.TryParse(
            normalized,
            ignoreCase: true,
            out ProjectDesignSourcePurpose purpose)
            ? purpose.ToString()
            : "";
    }
}
