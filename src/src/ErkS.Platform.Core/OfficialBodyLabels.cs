namespace ErkS.Platform.Core;

/// <summary>
/// What each kind of body is CALLED on screen.
///
/// 🔴 SEPARATE FROM THE STORED VALUE, AND THAT IS THE WHOLE POINT. The file keeps
/// the enum's own name in one spelling, because a stored value with two accepted
/// spellings is two things to keep in step and the reader refuses what it does
/// not know. What a person READS is a different question with a different answer
/// in a different language, and letting the two be the same string is how a
/// rename of a label silently invalidates every file already written.
/// </summary>
public static class OfficialBodyLabels
{
    public static string Mongolian(OfficialBodyKind kind) => kind switch
    {
        OfficialBodyKind.EmergencyManagement => "Онцгой байдал",

        // «Эрүүл мэнд», not «эрүүл ахуй»: the product has said so since
        // 2026-09-06, and so did the owner - «эрүүл мэндийн яам». The other
        // phrase names the subject matter rather than the office.
        OfficialBodyKind.PublicHealth => "Эрүүл мэнд",
        OfficialBodyKind.UrbanPlanning => "Хот байгуулалт",

        // 🔴 NOT A DEFAULT. A kind with no label would print as a blank cell in a
        // chooser - selectable, indistinguishable, and impossible to report.
        _ => throw new ArgumentOutOfRangeException(
            nameof(kind),
            kind,
            "Энэ төрлийн байгууллагын нэр тодорхойлогдоогүй байна."),
    };
}
