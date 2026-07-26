using System.Security.Cryptography;
using System.Text;
using System.IO;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal static class StudioAlbumComponentIdentity
{
    private const string AlbumSliceMarker = "|album-slice|";

    public const string SourceComponentKind = "Source";
    public const string GeneratedComponentKind = "Generated";
    public const string SiteContextComponentKind =
        ProjectSiteContextEditingPolicy.SiteContextComponentKind;
    public const string AtdSourceKey = "foundation-atd";
    public const string VisualizationSourceKey = "visualizations";

    public static string SourceCode(string ownerEmail, string sourceKey)
    {
        string owner = (ownerEmail ?? "").Trim().ToLowerInvariant();
        string key = (sourceKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(key))
            throw new InvalidDataException("A source component requires an owner and source key.");
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(owner));
        return $"source:{Convert.ToHexString(hash)[..16].ToLowerInvariant()}:{key}";
    }

    public static string SourceSliceCode(
        string ownerEmail,
        string sourceKey,
        string sectionKey,
        string sequenceKey)
    {
        string section = (sectionKey ?? "").Trim();
        string sequence = (sequenceKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(section) &&
            string.IsNullOrWhiteSpace(sequence))
        {
            return SourceCode(ownerEmail, sourceKey);
        }

        return SourceCode(ownerEmail, sourceKey) +
            AlbumSliceMarker +
            EncodeSliceValue(section) +
            "." +
            EncodeSliceValue(sequence);
    }

    public static string SourceBuildingCode(
        string ownerEmail,
        string sourceKey,
        string sectionKey) =>
        SourceSliceCode(ownerEmail, sourceKey, sectionKey, "");

    public static string BaseSourceCode(string code)
    {
        string normalized = (code ?? "").Trim();
        int markerIndex = normalized.IndexOf(
            AlbumSliceMarker,
            StringComparison.OrdinalIgnoreCase);
        return markerIndex < 0 ? normalized : normalized[..markerIndex];
    }

    public static bool TryGetSourceSlice(
        string code,
        out string sectionKey,
        out string sequenceKey)
    {
        sectionKey = "";
        sequenceKey = "";
        string normalized = (code ?? "").Trim();
        int markerIndex = normalized.IndexOf(
            AlbumSliceMarker,
            StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0 || !IsOwnedSourceCode(normalized[..markerIndex]))
            return false;

        string payload = normalized[(markerIndex + AlbumSliceMarker.Length)..];
        int separatorIndex = payload.IndexOf('.');
        if (separatorIndex < 0)
            return false;

        try
        {
            sectionKey = DecodeSliceValue(payload[..separatorIndex]);
            sequenceKey = DecodeSliceValue(payload[(separatorIndex + 1)..]);
            return !string.IsNullOrWhiteSpace(sectionKey) ||
                !string.IsNullOrWhiteSpace(sequenceKey);
        }
        catch (FormatException)
        {
            sectionKey = "";
            sequenceKey = "";
            return false;
        }
    }

    public static bool TryGetBuildingSectionKey(
        string code,
        out string sectionKey)
    {
        bool parsed = TryGetSourceSlice(code, out sectionKey, out _);
        return parsed &&
            (sectionKey.StartsWith("studio-building:", StringComparison.OrdinalIgnoreCase) ||
             sectionKey.StartsWith("package-building:", StringComparison.OrdinalIgnoreCase));
    }

    public static bool IsOwnedSourceCode(string code)
    {
        string[] parts = BaseSourceCode(code).Split(':', 3);
        return parts.Length == 3 &&
            parts[0].Equals("source", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Length == 16 &&
            parts[1].All(Uri.IsHexDigit) &&
            !string.IsNullOrWhiteSpace(parts[2]);
    }

    private static string EncodeSliceValue(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static string DecodeSliceValue(string value)
    {
        string normalized = value.Replace('-', '+').Replace('_', '/');
        normalized += (normalized.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => "",
        };
        return Encoding.UTF8.GetString(Convert.FromBase64String(normalized));
    }
}
