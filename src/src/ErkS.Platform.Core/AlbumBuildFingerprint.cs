using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ErkS.Platform.Contracts;

namespace ErkS.Platform.Core;

/// <summary>
/// What the album would be drawn from, as one value.
///
/// 🔴 THE PRODUCT REBUILT AN ALBUM THAT HAD NOT CHANGED, OVER AND OVER. A timer
/// fired every 1.5 seconds after any activity, opening the album page rebuilt it,
/// and nothing consulted the album already sitting on disk - so a project with 26
/// renders spent ten minutes and 9.7 GB redrawing pages that were already correct.
/// The owner named the rule: «нэгэнт үүсчихсэн бүх өөрчлөлтүүдээ хүлээгээд авчихсан
/// төсөл дахин дахин үүсээд байх ямар хэрэг байна вэ?»
///
/// So the question «has anything changed» needs an answer that is cheap to compute
/// and survives a restart. This is that answer.
///
/// 🔴 DERIVED, NEVER LISTED. It hashes the WHOLE build input rather than a set of
/// fields somebody chose, because the interesting failure is the one nobody
/// thought of: a new source kind, a new album setting, a new page role. A listed
/// fingerprint goes stale silently and the album stops updating - which is a worse
/// defect than the one being fixed, and a harder one to see.
/// </summary>
public static class AlbumBuildFingerprint
{
    /// <summary>
    /// The fingerprint of everything <see cref="AlbumBuilder"/> reads from the
    /// project. Two equal values mean the same album would be produced.
    ///
    /// The sheet PDFs themselves are covered through the project: a new package
    /// from a plugin rewrites its source's recorded content hash, which is part of
    /// what is serialised here. That is the owner's «Studio руу илгээх» trigger,
    /// and it arrives in this value without being named.
    /// </summary>
    public static string Of(AlbumProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(project, SheetPackageJson.Options);
        return Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
    }

    /// <summary>
    /// The fingerprint of a project plus the sheets a build would consume.
    ///
    /// The sheet identities are included separately because the library is the
    /// builder's SECOND argument: a project can name a source whose sheets have
    /// not been read yet, and those two states must not share a fingerprint or the
    /// album would be declared current while a page is still missing.
    /// </summary>
    public static string Of(AlbumProject project, IEnumerable<string> sheetIdentities)
    {
        ArgumentNullException.ThrowIfNull(project);
        var text = new StringBuilder(Of(project));
        foreach (string identity in (sheetIdentities ?? [])
            .Select(value => (value ?? "").Trim())
            .Where(value => value.Length > 0)
            .Order(StringComparer.Ordinal))
        {
            // Ordered, because the library's own order is not a fact about the
            // album: the same sheets arriving in a different order draw the same
            // pages, and a fingerprint that disagreed would rebuild for nothing.
            text.Append('\n').Append(identity);
        }

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
    }
}
