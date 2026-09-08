namespace ErkS.Studio;

/// <summary>Which side decides a refusal. A code may have both.</summary>
[Flags]
internal enum StudioSsoRefusalOrigin
{
    None = 0,

    /// <summary>The plugin decides it from the resting record, before any request.</summary>
    Reader = 1,

    /// <summary>The server decides it when the token is presented.</summary>
    Server = 2,
}

/// <summary>What the person should do next. The half a bare code never carries.</summary>
internal enum StudioSsoRefusalNextStep
{
    /// <summary>Nothing the person can do from here; show it and stop.</summary>
    Stop,

    InstallStudio,
    OpenStudio,
    SignInToStudio,
    EnterPin,
    UpdateThisProduct,

    /// <summary>Wait: the situation resolves without the person doing anything.</summary>
    Wait,

    /// <summary>Throw away any cached entitlement and read the record again.</summary>
    DropCachedEntitlement,
}

internal sealed record StudioSsoRefusal(
    string Code,
    StudioSsoRefusalOrigin Origin,
    int? HttpStatus,
    int? CheckOrder,
    StudioSsoRefusalNextStep NextStep,
    string MessageMn);

/// <summary>
/// The canon refusal vocabulary, in one place, so four products stop inventing
/// their own.
///
/// 🔴 THIS EXISTS BECAUSE PROSE ALREADY FAILED. The names were published in a
/// markdown table on 2026-09-09 and by the same afternoon three products had
/// three dictionaries: PFA wrote its own before the canon existed, CGM copied
/// PFA, and only PFR matched. A contract a person has to retype is a contract
/// that drifts - so this is generated into a file the readers can compare
/// against, exactly as the record vectors are.
///
/// 🔴 FIVE OF THESE NAMES BELONG TO BOTH SIDES, AND THAT IS DELIBERATE. The
/// reader decides «device mismatch» from a broken signature; the server decides
/// it from the token's device claim. They establish different facts and the
/// person is told the same thing either way, which is the point - one situation,
/// one sentence, whoever noticed it. The <see cref="StudioSsoRefusal.Origin"/>
/// flags record which side can raise each one so nobody has to guess.
///
/// The sentences for shared codes are the SERVER's own words, and a test holds
/// them to that. Copying them here without a check would be the same drift this
/// file exists to stop, one level up.
/// </summary>
internal static class StudioSsoRefusalCatalogue
{
    public static IReadOnlyList<StudioSsoRefusal> All { get; } =
    [
        // --- What a reader decides, in the order it must decide them. The order
        // --- is part of the contract: each step makes the fields below it mean
        // --- something, so a reader that reshuffles them answers with the wrong
        // --- one. PFA and CGM shared a dictionary, so they may share an order.
        new("sso_store_unavailable",
            StudioSsoRefusalOrigin.Reader,
            null,
            1,
            StudioSsoRefusalNextStep.Stop,
            "Windows-ийн итгэмжлэлийн санд хандаж чадсангүй. Энэ нь эрхийн " +
            "асуудал тул дахин суулгах нь тус болохгүй."),

        new("sso_studio_not_installed",
            StudioSsoRefusalOrigin.Reader | StudioSsoRefusalOrigin.Server,
            401,
            2,
            StudioSsoRefusalNextStep.InstallStudio,
            "Erk-S Studio суулгаж нэвтэрнэ үү. Энэ програм өөрөө нэвтрэхээ больсон."),

        new("sso_identity_unreadable",
            StudioSsoRefusalOrigin.Reader,
            null,
            3,
            StudioSsoRefusalNextStep.OpenStudio,
            "Энэ төхөөрөмжийн бүртгэл уншигдахгүй байна. Erk-S Studio-г нээвэл " +
            "дахин бичигдэнэ."),

        new("sso_studio_update_required",
            StudioSsoRefusalOrigin.Reader,
            null,
            4,
            StudioSsoRefusalNextStep.UpdateThisProduct,
            "Энэ төхөөрөмжийн бүртгэл шинэ хэлбэрээр бичигдсэн байна. " +
            "Энэ програмаа шинэчилнэ үү."),

        new("sso_device_mismatch",
            StudioSsoRefusalOrigin.Reader | StudioSsoRefusalOrigin.Server,
            401,
            5,
            StudioSsoRefusalNextStep.OpenStudio,
            "Энэ баталгаа өөр компьютерт олгогдсон байна. Энэ машин дээрээ " +
            "Studio-г нээж нэвтэрнэ үү."),

        new("sso_handoff_expired",
            StudioSsoRefusalOrigin.Reader | StudioSsoRefusalOrigin.Server,
            401,
            6,
            StudioSsoRefusalNextStep.OpenStudio,
            "Эрхийн баталгааны хугацаа дууссан байна. Erk-S Studio-г нэг удаа нээнэ үү."),

        new("sso_identity_not_signed_in",
            StudioSsoRefusalOrigin.Reader | StudioSsoRefusalOrigin.Server,
            401,
            7,
            StudioSsoRefusalNextStep.SignInToStudio,
            "Энэ компьютер дээр Erk-S Studio-д хэн ч нэвтрээгүй байна. " +
            "Studio-г нээж нэвтэрнэ үү."),

        new("sso_bot_state_locked",
            StudioSsoRefusalOrigin.Reader | StudioSsoRefusalOrigin.Server,
            409,
            7,
            StudioSsoRefusalNextStep.EnterPin,
            "Төхөөрөмж ботын төлөвт түгжээтэй байна. ПИН оруулж тайлна уу."),

        new("sso_handoff_token_missing",
            StudioSsoRefusalOrigin.Reader,
            null,
            8,
            StudioSsoRefusalNextStep.Wait,
            "Энэ төхөөрөмж таних тэмдэгтэй боловч эрхийн баталгаа хараахан " +
            "аваагүй байна. Erk-S Studio-д нэвтэрсний дараа авагдана."),

        // --- What only the server can decide. No check order: a reader reaches
        // --- these only after every step above has passed.
        new("sso_licence_inactive",
            StudioSsoRefusalOrigin.Server,
            409,
            null,
            StudioSsoRefusalNextStep.Stop,
            "Лиценз идэвхгүй эсвэл хугацаа нь дууссан байна."),

        new("sso_project_out_of_scope",
            StudioSsoRefusalOrigin.Server,
            403,
            null,
            StudioSsoRefusalNextStep.Stop,
            "Энэ идэвхжүүлэлт байгууллагын суудлаар хийгдсэн тул зөвхөн түүнд " +
            "томилогдсон төслүүдэд ажиллана."),

        new("sso_identity_generation_stale",
            StudioSsoRefusalOrigin.Server,
            409,
            null,
            StudioSsoRefusalNextStep.DropCachedEntitlement,
            "Таних тэмдэг шинэчлэгдсэн байна. Энэ хүсэлтийг бүү давт; хадгалсан " +
            "эрхээ хүчингүй болго. Дараагийн үйлдлийн үед бичлэгээс шинэ таних " +
            "тэмдгийг уншина."),
    ];

    /// <summary>
    /// The sentence for a code, or an empty string if this build has never heard
    /// of it.
    ///
    /// Empty rather than a stand-in: a made-up sentence for an unknown code
    /// would read as though the situation were understood.
    /// </summary>
    public static string MessageMn(string code)
    {
        string wanted = (code ?? "").Trim();
        foreach (StudioSsoRefusal refusal in All)
        {
            if (refusal.Code.Equals(wanted, StringComparison.OrdinalIgnoreCase))
                return refusal.MessageMn;
        }
        return "";
    }
}
