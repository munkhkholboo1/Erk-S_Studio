namespace ErkS.Studio;

/// <summary>
/// Who this machine is working as, for deciding which sources and payloads it
/// owns and receives.
///
/// The seat and the session are separate because they stop being the same
/// thing once a device holds an organization's bot seat: a person signing in
/// with their own account to look at their own projects must not silently stop
/// the machine receiving the seat's deliveries. Folding the two together is
/// what made every ownership check follow whoever happened to be signed in.
///
/// While nothing has claimed a seat, the seat is the session — which is every
/// case that exists today, so behaviour is unchanged until a seat is set.
/// </summary>
internal readonly record struct StudioRuntimeIdentity(
    string SeatEmail,
    string DeviceFingerprint,
    string SessionEmail)
{
    public static readonly StudioRuntimeIdentity None = new("", "", "");

    /// <summary>
    /// Who this machine owns and receives as. An empty seat means it follows
    /// the signed-in person, which is every machine today. The seat is stored
    /// as an override rather than a copy of the session so that "is there a
    /// seat" stays a fact instead of being inferred from the two matching —
    /// they match constantly by coincidence.
    /// </summary>
    public string OwnerEmail =>
        string.IsNullOrEmpty(SeatEmail) ? SessionEmail : SeatEmail;

    public bool HasSeat => !string.IsNullOrEmpty(SeatEmail);

    /// <summary>The ordinary case: this machine works as whoever is signed in.</summary>
    public static StudioRuntimeIdentity ForSession(
        string? sessionEmail,
        string? deviceFingerprint) =>
        new("", Normalize(deviceFingerprint), Normalize(sessionEmail));

    /// <summary>
    /// The machine keeps receiving for <paramref name="seatEmail"/> whoever is
    /// signed in. Used by the device seat; the session still decides what the
    /// person may do, only never what the machine owns.
    /// </summary>
    public StudioRuntimeIdentity WithSeat(string? seatEmail) =>
        this with { SeatEmail = Normalize(seatEmail) };

    private static string Normalize(string? value) =>
        (value ?? "").Trim().ToLowerInvariant();
}
