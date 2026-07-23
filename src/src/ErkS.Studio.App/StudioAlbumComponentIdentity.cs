using System.Security.Cryptography;
using System.Text;
using System.IO;
using ErkS.Platform.Core;

namespace ErkS.Studio;

internal static class StudioAlbumComponentIdentity
{
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

    public static bool IsOwnedSourceCode(string code)
    {
        string[] parts = (code ?? "").Split(':', 3);
        return parts.Length == 3 &&
            parts[0].Equals("source", StringComparison.OrdinalIgnoreCase) &&
            parts[1].Length == 16 &&
            parts[1].All(Uri.IsHexDigit) &&
            !string.IsNullOrWhiteSpace(parts[2]);
    }
}
