using System.IO;
using System.Text.Json;

namespace ErkS.Studio;

/// <summary>
/// What this machine is seated as, kept beside the account metadata.
///
/// Separate from the account file on purpose: the seat is a property of the
/// DEVICE and the session is whoever is at it right now. Folding the two
/// together is what made every ownership check follow the signed-in person,
/// and a machine holding an organisation's seat must keep receiving for that
/// seat while an employee signs in with their own account.
/// </summary>
internal sealed class StudioBotDeviceState
{
    public string BotId { get; set; } = "";
    public string OrganizationId { get; set; } = "";
    public string DisplayName { get; set; } = "";

    /// <summary>
    /// The address this machine owns and receives as. The seat's internal
    /// e-mail when it has one; otherwise the bot id, which is unique and works
    /// the same way - the comparison is a string match, not a mailbox.
    /// </summary>
    public string SeatIdentity { get; set; } = "";

    public DateTimeOffset EnteredAtUtc { get; set; }
    public string EnteredByEmail { get; set; } = "";

    public bool IsSeated => !string.IsNullOrWhiteSpace(BotId);

    public static string ResolveSeatIdentity(string botId, string internalEmail) =>
        string.IsNullOrWhiteSpace(internalEmail)
            ? (botId ?? "").Trim().ToLowerInvariant()
            : internalEmail.Trim().ToLowerInvariant();
}

internal static class StudioBotDeviceStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string StatePath => Path.Combine(
        StudioAccountService.AccountDataRoot,
        "bot-device-state.json");

    /// <summary>
    /// Reads the seat, or null when this machine holds none. A file that cannot
    /// be read is treated as no seat and is NOT deleted: an unreadable seat is
    /// a problem to look at, not one to silently resolve by unseating a machine
    /// somebody handed over.
    /// </summary>
    public static StudioBotDeviceState? Read()
    {
        try
        {
            if (!File.Exists(StatePath))
                return null;
            StudioBotDeviceState? value = JsonSerializer.Deserialize<StudioBotDeviceState>(
                File.ReadAllText(StatePath),
                JsonOptions);
            return value is { IsSeated: true } ? value : null;
        }
        catch (Exception exception) when (
            exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Write(StudioBotDeviceState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        Directory.CreateDirectory(Path.GetDirectoryName(StatePath)!);
        string temporaryPath = StatePath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temporaryPath, StatePath, overwrite: true);
    }

    /// <summary>
    /// Clears the seat. Throws rather than swallowing, because leaving bot
    /// state has the same shape as entering it: a half-done transition is worse
    /// than a reported failure.
    /// </summary>
    public static void Clear()
    {
        if (File.Exists(StatePath))
        {
            File.Delete(StatePath);
        }
    }
}
