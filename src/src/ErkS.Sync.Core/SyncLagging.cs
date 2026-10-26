namespace ErkS.Sync.Core;

/// <summary>
/// A member who has not updated or not come online for long does not shape anyone else's folder
/// (owner, 2026-10-26: «программаа шинэчлээгүй онлайн болоогүй удсан гишүүн бусад гишүүдийн
/// ажиллагаанд нөлөөлөх учиргүй»): its index is no source of files here - nothing is fetched from it,
/// nothing kept beside as a conflict - until it is back and current. The server keeps the facts
/// (last seen, client version); this is where they are judged.
/// </summary>
public static class SyncLagging
{
    /// <summary>Not seen by the server for this long: its copies are of another day.</summary>
    public static readonly TimeSpan LongOffline = TimeSpan.FromHours(24);

    public static bool IsLagging(SyncServerDevice device, DateTimeOffset now, string minimumClientVersion = "") =>
        now - device.LastSeenAtUtc > LongOffline ||
        (minimumClientVersion.Length > 0 && device.ClientVersion.Length > 0 && Compare(device.ClientVersion, minimumClientVersion) < 0);

    /// <summary>This SYN's release from the install beside it (.erks-install.json, «v0.001.108» → «0.001.108»); empty in a dev build.</summary>
    public static string InstalledRelease(string directory)
    {
        try
        {
            string path = Path.Combine(directory, ".erks-install.json");
            if (!File.Exists(path))
                return "";
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.TryGetProperty("version", out var version)
                ? (version.GetString() ?? "").Trim().TrimStart('v', 'V')
                : "";
        }
        catch (Exception exception) when (exception is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            return "";
        }
    }

    /// <summary>Release versions by their numbers.</summary>
    public static int Compare(string left, string right)
    {
        long[] a = Numbers(left), b = Numbers(right);
        for (int index = 0; index < Math.Max(a.Length, b.Length); index++)
        {
            long x = index < a.Length ? a[index] : 0, y = index < b.Length ? b[index] : 0;
            if (x != y)
                return x.CompareTo(y);
        }
        return 0;
    }

    private static long[] Numbers(string version) =>
        version.Trim().TrimStart('v', 'V').Split('.', '-', '+').Select(part => long.TryParse(part, out long n) ? n : 0).ToArray();
}
