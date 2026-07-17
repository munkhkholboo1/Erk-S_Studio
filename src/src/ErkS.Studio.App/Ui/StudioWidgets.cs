using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ErkS.Studio;

/// <summary>
/// Shared factory helpers so every window builds identical-looking pieces
/// (titles, section headers, cards, form rows, buttons, status bars).
/// </summary>
internal static class StudioWidgets
{
    private static readonly FontFamily GlyphFont = new("Segoe MDL2 Assets");

    /// <summary>Resolves an icon asset shipped next to this assembly (Assets folder).</summary>
    public static string GetAssetPath(string assetName)
    {
        return System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", assetName);
    }

    public static TextBlock CreateTitle(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = StudioTheme.TitleFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, StudioTheme.SpaceSm),
        };
    }

    public static TextBlock CreateSectionHeader(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = StudioTheme.SectionFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = StudioTheme.AccentSoftBrush,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, StudioTheme.SpaceSm, 0, StudioTheme.SpaceXs),
        };
    }

    public static TextBlock CreateText(string text)
    {
        return new TextBlock
        {
            Text = text,
            Foreground = StudioTheme.TextBrush,
            TextWrapping = TextWrapping.Wrap,
        };
    }

    public static TextBlock CreateHint(string text)
    {
        return new TextBlock
        {
            Text = text,
            FontSize = StudioTheme.HintFontSize,
            Foreground = StudioTheme.MutedTextBrush,
            TextWrapping = TextWrapping.Wrap,
        };
    }

    /// <summary>Panel card with the shared background, border, and padding.</summary>
    public static Border CreateCard(UIElement child)
    {
        return new Border
        {
            Background = StudioTheme.PanelBrush,
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(StudioTheme.CornerRadius),
            Padding = new Thickness(StudioTheme.SpaceLg),
            Margin = new Thickness(0, 0, 0, StudioTheme.SpaceSm),
            Child = child,
        };
    }

    public static Button CreateButton(string text)
    {
        return new Button
        {
            Content = text,
            Margin = new Thickness(0, 0, StudioTheme.SpaceSm, StudioTheme.SpaceSm),
        };
    }

    public static Button CreatePrimaryButton(string text)
    {
        var button = CreateButton(text);
        button.Background = StudioTheme.AccentBrush;
        button.BorderBrush = StudioTheme.AccentBrush;
        button.Foreground = Brushes.White;
        button.FontWeight = FontWeights.SemiBold;
        return button;
    }

    public static Button CreateDangerButton(string text)
    {
        var button = CreateButton(text);
        button.Background = StudioTheme.DangerBrush;
        button.BorderBrush = StudioTheme.DangerBrush;
        button.Foreground = Brushes.White;
        return button;
    }

    /// <summary>
    /// Icon-only command button with a tooltip. Falls back to the text when the
    /// icon asset is missing so the command is never invisible.
    /// </summary>
    public static Button CreateIconButton(string iconAsset, string tooltip, string fallbackText)
    {
        var button = new Button
        {
            ToolTip = tooltip,
            MinWidth = 34,
            MinHeight = 30,
            Padding = new Thickness(4, 2, 4, 2),
            Margin = new Thickness(0, 0, 4, 4),
        };
        var image = SvgIconLoader.TryLoad(GetAssetPath(iconAsset));
        button.Content = image is null
            ? fallbackText
            : new Image { Source = image, Width = 18, Height = 18 };
        return button;
    }

    /// <summary>Icon + short label button for commands where an icon alone is ambiguous.</summary>
    public static Button CreateIconTextButton(string iconAsset, string text, string? tooltip = null)
    {
        var button = new Button
        {
            ToolTip = tooltip ?? text,
            MinHeight = 28,
            Padding = new Thickness(8, 2, 10, 2),
            Margin = new Thickness(0, 0, 4, 4),
        };
        var image = SvgIconLoader.TryLoad(GetAssetPath(iconAsset));
        if (image is null)
        {
            button.Content = text;
            return button;
        }

        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new Image
        {
            Source = image,
            Width = 15,
            Height = 15,
            Margin = new Thickness(0, 0, 6, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
        });
        button.Content = stack;
        return button;
    }

    public static Button CreateGlyphButton(string glyph, string tooltip)
    {
        return new Button
        {
            ToolTip = tooltip,
            Width = 36,
            Height = 34,
            Padding = new Thickness(0),
            Margin = new Thickness(0, 0, StudioTheme.SpaceSm, StudioTheme.SpaceSm),
            Content = new TextBlock
            {
                Text = glyph,
                FontFamily = GlyphFont,
                FontSize = 15,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
    }

    public static Button CreateGlyphTextButton(string glyph, string text, string? tooltip = null, bool primary = false)
    {
        var button = new Button
        {
            ToolTip = tooltip ?? text,
            Padding = new Thickness(12, 6, 14, 6),
            Margin = new Thickness(0, 0, StudioTheme.SpaceSm, StudioTheme.SpaceSm),
        };
        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = glyph,
            FontFamily = GlyphFont,
            FontSize = 14,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        stack.Children.Add(new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        });
        button.Content = stack;
        if (primary)
        {
            button.Background = StudioTheme.AccentBrush;
            button.BorderBrush = StudioTheme.AccentBrush;
            button.Foreground = Brushes.White;
        }
        return button;
    }

    /// <summary>Compact inline button for rows (Set, ...): smaller padding.</summary>
    public static Button CreateInlineButton(string text)
    {
        return new Button
        {
            Content = text,
            MinHeight = 24,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0),
        };
    }

    /// <summary>
    /// Label + control row on a two-column grid, so all forms align identically.
    /// </summary>
    public static Grid CreateFormRow(string labelText, UIElement control, double labelWidth = StudioTheme.FormLabelWidth)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, StudioTheme.SpaceXs) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var label = new TextBlock
        {
            Text = labelText,
            Foreground = StudioTheme.MutedTextBrush,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, StudioTheme.SpaceSm, 0),
        };
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        Grid.SetColumn((FrameworkElement)control, 1);
        row.Children.Add(control);
        return row;
    }

    /// <summary>Thin status strip for the bottom of tool windows.</summary>
    public static Border CreateStatusBar(TextBlock statusText)
    {
        statusText.Foreground = StudioTheme.MutedTextBrush;
        statusText.FontSize = StudioTheme.HintFontSize;
        statusText.TextWrapping = TextWrapping.Wrap;
        statusText.Margin = new Thickness(0);
        return new Border
        {
            BorderBrush = StudioTheme.BorderBrush,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(StudioTheme.SpaceMd, 4, StudioTheme.SpaceMd, 4),
            Child = statusText,
        };
    }

    public static ScrollViewer CreateScrollHost(UIElement content)
    {
        return new ScrollViewer
        {
            Content = content,
            Background = Brushes.Transparent,
            Foreground = StudioTheme.TextBrush,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            VerticalContentAlignment = VerticalAlignment.Top,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }
}
