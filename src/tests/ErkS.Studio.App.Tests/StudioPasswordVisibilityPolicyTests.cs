using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

public sealed class StudioPasswordVisibilityPolicyTests
{
    [Fact]
    public void ToggleSwitchesBetweenMaskedAndVisiblePasswordWithoutChangingValue()
    {
        StudioPasswordVisibilityState masked =
            StudioPasswordVisibilityPolicy.Initial;

        Assert.False(masked.IsVisible);
        Assert.Equal("Харах", masked.ToggleLabel);
        Assert.Equal(
            "secret-123",
            StudioPasswordVisibilityPolicy.CurrentPassword(
                masked,
                maskedPassword: "secret-123",
                visiblePassword: ""));

        StudioPasswordVisibilityState visible =
            StudioPasswordVisibilityPolicy.Toggle(masked);

        Assert.True(visible.IsVisible);
        Assert.Equal("Нуух", visible.ToggleLabel);
        Assert.Equal(
            "secret-123",
            StudioPasswordVisibilityPolicy.CurrentPassword(
                visible,
                maskedPassword: "secret-123",
                visiblePassword: "secret-123"));

        Assert.Equal(masked, StudioPasswordVisibilityPolicy.Toggle(visible));
    }
}
