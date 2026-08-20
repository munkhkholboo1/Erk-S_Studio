namespace ErkS.Studio;

internal sealed record StudioPasswordVisibilityState(
    bool IsVisible,
    string ToggleLabel,
    string ToggleTooltip);

internal static class StudioPasswordVisibilityPolicy
{
    public static StudioPasswordVisibilityState Initial { get; } =
        Masked();

    public static StudioPasswordVisibilityState Toggle(
        StudioPasswordVisibilityState current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return current.IsVisible ? Masked() : Visible();
    }

    public static string CurrentPassword(
        StudioPasswordVisibilityState state,
        string? maskedPassword,
        string? visiblePassword)
    {
        ArgumentNullException.ThrowIfNull(state);
        return state.IsVisible
            ? visiblePassword ?? ""
            : maskedPassword ?? "";
    }

    private static StudioPasswordVisibilityState Masked() =>
        new(
            IsVisible: false,
            ToggleLabel: "Харах",
            ToggleTooltip: "Нууц үгийг харах");

    private static StudioPasswordVisibilityState Visible() =>
        new(
            IsVisible: true,
            ToggleLabel: "Нуух",
            ToggleTooltip: "Нууц үгийг нуух");
}
