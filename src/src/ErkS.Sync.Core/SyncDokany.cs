using System.Diagnostics;

namespace ErkS.Sync.Core;

/// <summary>
/// Dokany - the open-source (LGPL/MIT) file-system driver Erk-S Central mounts a member's central with.
/// One reading of what is installed, for SYN's status (Twin) and Studio alike; Studio installs the
/// pinned release when a central needs it (Erk-S Sync / owner, 2026-10-27: «энэ үйлдлээ өөрөө хийгээд
/// найдвартай ажиллагаатай болго»).
/// </summary>
public static class SyncDokany
{
    /// <summary>The release SYN's DokanNet 2.3.0.3 runs on (any 2.3+ of the 2.x line).</summary>
    public const string PinnedVersion = "2.3.1.1000";

    public const string MsiUrl = "https://github.com/dokan-dev/dokany/releases/download/v2.3.1.1000/Dokan_x64.msi";

    /// <summary>The official Dokan_x64.msi v2.3.1.1000 (9 269 248 bytes), signed by LEOSAC (Certum EV).</summary>
    public const string MsiSha256 = "69FF8CB37BFEC3A75921C85FFD1C6370B50A9EC4ECEF2CF3A009D488DCBF5465";

    public const string MsiPublisher = "LEOSAC";

    /// <summary>The installed driver library's version («2.3.1.1000»), or "" when Dokany is not installed.</summary>
    public static string InstalledVersion()
    {
        try
        {
            string library = Path.Combine(Environment.SystemDirectory, "dokan2.dll");
            if (!File.Exists(library))
                return "";
            FileVersionInfo info = FileVersionInfo.GetVersionInfo(library);
            return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}.{info.FilePrivatePart}";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return "";
        }
    }

    /// <summary>Whether a central here needs Dokany installed (or a newer one): none, or older than 2.3.</summary>
    public static bool NeedsInstall(string installed) =>
        !Version.TryParse(installed, out Version? version) || version < new Version(2, 3);
}
