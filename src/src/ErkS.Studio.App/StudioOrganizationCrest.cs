using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErkS.Studio;

/// <summary>
/// How an organization is shown before anything else is known about it: its
/// logo when it has one, and otherwise a mark made from its name, so a company
/// without an uploaded logo still reads as itself rather than as a blank.
/// </summary>
internal static class StudioOrganizationCrest
{
    /// <summary>
    /// One or two letters taken from the organization's name. Two words give
    /// two letters, which tells "Танан цамхаг" from "Танан констракшн".
    /// </summary>
    public static string Initials(string? name)
    {
        string[] words = (name ?? "")
            .Replace('"', ' ')
            .Replace('«', ' ')
            .Replace('»', ' ')
            .Split([' ', '-', '_', '.'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => char.IsLetterOrDigit(word[0]))
            // A legal form is not what tells one company from another.
            .Where(word => !IsLegalForm(word))
            .ToArray();
        return words.Length switch
        {
            0 => "—",
            1 => words[0][..1].ToUpperInvariant(),
            _ => (words[0][..1] + words[1][..1]).ToUpperInvariant(),
        };
    }

    /// <summary>
    /// The crest for a card: the initials mark, with the logo drawn over it
    /// when one exists. Both are added, so a logo simply covers the mark.
    /// </summary>
    public static void AppendTo(
        FrameworkElementFactory host,
        string initialsPath,
        string logoSourcePath,
        double size = 84d,
        string? initialsVisibilityPath = null)
    {
        var badge = new FrameworkElementFactory(typeof(Border));
        badge.SetValue(FrameworkElement.WidthProperty, size);
        badge.SetValue(FrameworkElement.HeightProperty, size);
        badge.SetValue(Border.CornerRadiusProperty, new CornerRadius(size / 2));
        badge.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)));
        badge.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        badge.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        // An organization with a logo shows only the logo. The badge used to be
        // drawn underneath it and its circle showed around the artwork.
        if (initialsVisibilityPath is not null)
        {
            badge.SetBinding(
                UIElement.VisibilityProperty,
                new System.Windows.Data.Binding(initialsVisibilityPath));
        }

        var initials = new FrameworkElementFactory(typeof(TextBlock));
        initials.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(initialsPath));
        initials.SetValue(TextBlock.FontSizeProperty, Math.Max(11d, size * 0.36));
        initials.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        initials.SetValue(TextBlock.ForegroundProperty, StudioTheme.TextBrush);
        initials.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        initials.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        badge.AppendChild(initials);
        host.AppendChild(badge);

        var logo = new FrameworkElementFactory(typeof(Image));
        logo.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(logoSourcePath));
        logo.SetValue(Image.StretchProperty, Stretch.Uniform);
        logo.SetValue(FrameworkElement.MarginProperty, new Thickness(Math.Max(4d, size * 0.22)));
        host.AppendChild(logo);
    }

    private static bool IsLegalForm(string word)
    {
        string value = word.Trim().ToUpperInvariant();
        return value is "ХХК" or "ХК" or "ТББ" or "ЛЛСИ" or "LLC" or "LTD" or "CO" or "INC";
    }
}
