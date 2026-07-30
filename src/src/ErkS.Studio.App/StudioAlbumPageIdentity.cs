using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ErkS.Studio;

internal static class StudioAlbumPageIdentity
{
    public static string Create(
        string ownerEmail,
        string sourceKey,
        string nativeSheetId,
        int nativePageNumber)
    {
        string owner = (ownerEmail ?? "").Trim().ToLowerInvariant();
        string source = (sourceKey ?? "").Trim().ToLowerInvariant();
        string nativeSheet = (nativeSheetId ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(owner) ||
            string.IsNullOrWhiteSpace(source) ||
            string.IsNullOrWhiteSpace(nativeSheet) ||
            nativePageNumber < 1)
        {
            throw new InvalidDataException(
                "A source album page requires immutable cloud owner/source and native sheet/page identity.");
        }

        string identity = string.Join(
            "\u001f",
            owner,
            source,
            nativeSheet,
            nativePageNumber.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return "album-page:" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)))
                .ToLowerInvariant();
    }
}
