using System.IO;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ErkS.Studio;

/// <summary>
/// Single source of truth for the Erk-S CityGen visual style.
/// Every tool window gets its palette, fonts, spacing, and implicit
/// control styles from here so all windows share one look.
/// </summary>
internal static class StudioTheme
{
    // Palette
    public static Color WindowBackgroundColor { get; } = Color.FromRgb(18, 20, 23);
    public static Color PanelColor { get; } = Color.FromRgb(24, 27, 32);
    public static Color PanelAltColor { get; } = Color.FromRgb(32, 36, 42);
    public static Color InputColor { get; } = Color.FromRgb(20, 23, 27);
    public static Color BorderColor { get; } = Color.FromRgb(47, 52, 60);
    public static Color BorderHoverColor { get; } = Color.FromRgb(69, 76, 88);
    public static Color TextColor { get; } = Color.FromRgb(242, 244, 247);
    public static Color MutedTextColor { get; } = Color.FromRgb(164, 171, 182);
    public static Color FaintTextColor { get; } = Color.FromRgb(116, 124, 137);
    public static Color AccentColor { get; } = Color.FromRgb(35, 135, 244);
    public static Color AccentSoftColor { get; } = Color.FromRgb(169, 209, 255);
    public static Color ButtonColor { get; } = Color.FromRgb(38, 43, 50);
    public static Color ButtonDisabledColor { get; } = Color.FromRgb(30, 33, 38);
    public static Color SuccessColor { get; } = Color.FromRgb(54, 162, 105);
    public static Color WarningColor { get; } = Color.FromRgb(210, 155, 61);
    public static Color DangerColor { get; } = Color.FromRgb(198, 75, 85);

    public static SolidColorBrush WindowBackgroundBrush { get; } = Freeze(WindowBackgroundColor);
    public static SolidColorBrush PanelBrush { get; } = Freeze(PanelColor);
    public static SolidColorBrush PanelAltBrush { get; } = Freeze(PanelAltColor);
    public static SolidColorBrush InputBrush { get; } = Freeze(InputColor);
    public static SolidColorBrush BorderBrush { get; } = Freeze(BorderColor);
    public static SolidColorBrush BorderHoverBrush { get; } = Freeze(BorderHoverColor);
    public static SolidColorBrush TextBrush { get; } = Freeze(TextColor);
    public static SolidColorBrush MutedTextBrush { get; } = Freeze(MutedTextColor);
    public static SolidColorBrush FaintTextBrush { get; } = Freeze(FaintTextColor);
    public static SolidColorBrush AccentBrush { get; } = Freeze(AccentColor);
    public static SolidColorBrush AccentSoftBrush { get; } = Freeze(AccentSoftColor);
    public static SolidColorBrush ButtonBrush { get; } = Freeze(ButtonColor);
    public static SolidColorBrush ButtonDisabledBrush { get; } = Freeze(ButtonDisabledColor);
    public static SolidColorBrush SuccessBrush { get; } = Freeze(SuccessColor);
    public static SolidColorBrush WarningBrush { get; } = Freeze(WarningColor);
    public static SolidColorBrush DangerBrush { get; } = Freeze(DangerColor);

    // Typography and metrics
    public static FontFamily FontFamily { get; } = new("Segoe UI Variable Text");
    public const double FontSize = 13;
    public const double TitleFontSize = 22;
    public const double SectionFontSize = 13.5;
    public const double HintFontSize = 12;
    public const double CornerRadius = 6;
    public const double FormLabelWidth = 150;
    public const double SpaceXs = 6;
    public const double SpaceSm = 8;
    public const double SpaceMd = 12;
    public const double SpaceLg = 18;

    private static ResourceDictionary? sharedStyles;
    private static ImageSource? windowIcon;

    public static ResourceDictionary SharedStyles => sharedStyles ??= BuildStyles();

    /// <summary>
    /// Applies the theme to a non-window root element (hot-reloadable views):
    /// fonts, inherited foreground, and the implicit control styles.
    /// </summary>
    public static void ApplyToRoot(FrameworkElement root)
    {
        root.UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(root, TextFormattingMode.Display);
        root.SetValue(System.Windows.Documents.TextElement.FontFamilyProperty, FontFamily);
        root.SetValue(System.Windows.Documents.TextElement.FontSizeProperty, FontSize);
        root.SetValue(System.Windows.Documents.TextElement.ForegroundProperty, TextBrush);
        if (!root.Resources.MergedDictionaries.Contains(SharedStyles))
        {
            root.Resources.MergedDictionaries.Add(SharedStyles);
        }
    }

    /// <summary>
    /// Applies the shared chrome (background, fonts, implicit control styles)
    /// to a tool window. Call once from the window constructor.
    /// </summary>
    public static void Apply(Window window)
    {
        window.UseLayoutRounding = true;
        window.SnapsToDevicePixels = true;
        TextOptions.SetTextFormattingMode(window, TextFormattingMode.Display);
        window.FontFamily = FontFamily;
        window.FontSize = FontSize;
        window.Background = WindowBackgroundBrush;
        window.Foreground = TextBrush;
        window.Icon ??= windowIcon ??= TryLoadWindowIcon();
        if (!window.Resources.MergedDictionaries.Contains(SharedStyles))
        {
            window.Resources.MergedDictionaries.Add(SharedStyles);
        }
    }

    private static ImageSource? TryLoadWindowIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "logo-erks.ico");
            return File.Exists(iconPath)
                ? BitmapFrame.Create(new Uri(iconPath, UriKind.Absolute))
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static SolidColorBrush Freeze(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static string Hex(Color color)
    {
        return $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    }

    private static ResourceDictionary BuildStyles()
    {
        var xaml = StylesXaml
            .Replace("@@WindowBg@@", Hex(WindowBackgroundColor))
            .Replace("@@PanelAlt@@", Hex(PanelAltColor))
            .Replace("@@Panel@@", Hex(PanelColor))
            .Replace("@@Input@@", Hex(InputColor))
            .Replace("@@BorderHover@@", Hex(BorderHoverColor))
            .Replace("@@Border@@", Hex(BorderColor))
            .Replace("@@Text@@", Hex(TextColor))
            .Replace("@@Muted@@", Hex(MutedTextColor))
            .Replace("@@AccentSoft@@", Hex(AccentSoftColor))
            .Replace("@@Accent@@", Hex(AccentColor))
            .Replace("@@Button@@", Hex(ButtonColor));

        return (ResourceDictionary)XamlReader.Parse(xaml);
    }

    private const string StylesXaml = """
<ResourceDictionary
    xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">

  <Style TargetType="{x:Type Button}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Background" Value="@@Button@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="MinHeight" Value="34"/>
    <Setter Property="Padding" Value="12,6"/>
    <Setter Property="HorizontalContentAlignment" Value="Center"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Focusable" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type Button}">
          <Grid>
            <Border x:Name="Bd"
                    Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}"
                    CornerRadius="5"
                    SnapsToDevicePixels="True"/>
            <Border x:Name="HoverOverlay" Background="#FFFFFF" Opacity="0" CornerRadius="5" IsHitTestVisible="False"/>
            <ContentPresenter Margin="{TemplateBinding Padding}"
                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                              RecognizesAccessKey="True"
                              SnapsToDevicePixels="{TemplateBinding SnapsToDevicePixels}"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="HoverOverlay" Property="Opacity" Value="0.08"/>
            </Trigger>
            <Trigger Property="IsPressed" Value="True">
              <Setter TargetName="HoverOverlay" Property="Opacity" Value="0.16"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="@@Accent@@"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type ToggleButton}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Background" Value="@@Button@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="MinHeight" Value="32"/>
    <Setter Property="Padding" Value="10,5"/>
    <Setter Property="HorizontalContentAlignment" Value="Center"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type ToggleButton}">
          <Grid>
            <Border x:Name="Bd"
                    Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}"
                    CornerRadius="5"
                    SnapsToDevicePixels="True"/>
            <Border x:Name="HoverOverlay" Background="#FFFFFF" Opacity="0" CornerRadius="5" IsHitTestVisible="False"/>
            <ContentPresenter Margin="{TemplateBinding Padding}"
                              HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                              VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                              RecognizesAccessKey="True"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="HoverOverlay" Property="Opacity" Value="0.08"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="@@Accent@@"/>
              <Setter TargetName="Bd" Property="BorderBrush" Value="@@Accent@@"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type TextBox}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Background" Value="@@Input@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CaretBrush" Value="@@Text@@"/>
    <Setter Property="SelectionBrush" Value="@@Accent@@"/>
    <Setter Property="MinHeight" Value="32"/>
    <Setter Property="Padding" Value="9,5"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type TextBox}">
          <Border x:Name="Bd"
                  Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}"
                  CornerRadius="5"
                  SnapsToDevicePixels="True">
            <ScrollViewer x:Name="PART_ContentHost"
                          Margin="{TemplateBinding Padding}"
                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                          Focusable="False"
                          HorizontalScrollBarVisibility="Hidden"
                          VerticalScrollBarVisibility="Hidden"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="@@BorderHover@@"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="@@Accent@@"/>
            </Trigger>
            <Trigger Property="IsReadOnly" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="@@Panel@@"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type PasswordBox}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Background" Value="@@Input@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="CaretBrush" Value="@@Text@@"/>
    <Setter Property="SelectionBrush" Value="@@Accent@@"/>
    <Setter Property="MinHeight" Value="32"/>
    <Setter Property="Padding" Value="9,5"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type PasswordBox}">
          <Border x:Name="Bd"
                  Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}"
                  CornerRadius="5"
                  SnapsToDevicePixels="True">
            <ScrollViewer x:Name="PART_ContentHost"
                          Margin="{TemplateBinding Padding}"
                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                          Focusable="False"
                          HorizontalScrollBarVisibility="Hidden"
                          VerticalScrollBarVisibility="Hidden"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="@@BorderHover@@"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocusWithin" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="@@Accent@@"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style x:Key="CityGenComboBoxToggle" TargetType="{x:Type ToggleButton}">
    <Setter Property="Focusable" Value="False"/>
    <Setter Property="ClickMode" Value="Press"/>
    <Setter Property="OverridesDefaultStyle" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type ToggleButton}">
          <Border x:Name="Bd"
                  Background="@@Input@@"
                  BorderBrush="@@Border@@"
                  BorderThickness="1"
                  CornerRadius="5"
                  SnapsToDevicePixels="True">
            <Path x:Name="Arrow"
                  HorizontalAlignment="Right"
                  VerticalAlignment="Center"
                  Margin="0,0,9,0"
                  Data="M 0 0 L 4 4 L 8 0"
                  Stroke="@@Muted@@"
                  StrokeThickness="1.4"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="@@BorderHover@@"/>
            </Trigger>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="@@Accent@@"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type ComboBox}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Background" Value="@@Input@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="MinHeight" Value="32"/>
    <Setter Property="Padding" Value="9,5"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="ScrollViewer.CanContentScroll" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type ComboBox}">
          <Grid>
            <ToggleButton x:Name="ToggleButton"
                          Style="{StaticResource CityGenComboBoxToggle}"
                          IsChecked="{Binding IsDropDownOpen, Mode=TwoWay, RelativeSource={RelativeSource TemplatedParent}}"/>
            <ContentPresenter x:Name="ContentSite"
                              IsHitTestVisible="False"
                              Content="{TemplateBinding SelectionBoxItem}"
                              ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                              ContentTemplateSelector="{TemplateBinding ItemTemplateSelector}"
                              Margin="8,0,24,0"
                              VerticalAlignment="Center"
                              HorizontalAlignment="Left"/>
            <TextBox x:Name="PART_EditableTextBox"
                     Visibility="Hidden"
                     HorizontalAlignment="Stretch"
                     VerticalAlignment="Center"
                     Margin="2,0,22,0"
                     Focusable="True"
                     Background="Transparent"
                     BorderThickness="0"
                     Foreground="@@Text@@"
                     CaretBrush="@@Text@@"
                     IsReadOnly="{TemplateBinding IsReadOnly}">
              <TextBox.Template>
                <ControlTemplate TargetType="{x:Type TextBox}">
                  <ScrollViewer x:Name="PART_ContentHost"
                                Margin="6,0,0,0"
                                VerticalAlignment="Center"
                                Focusable="False"
                                HorizontalScrollBarVisibility="Hidden"
                                VerticalScrollBarVisibility="Hidden"/>
                </ControlTemplate>
              </TextBox.Template>
            </TextBox>
            <Popup x:Name="PART_Popup"
                   Placement="Bottom"
                   IsOpen="{TemplateBinding IsDropDownOpen}"
                   AllowsTransparency="True"
                   PopupAnimation="Fade"
                   Focusable="False">
              <Border MinWidth="{TemplateBinding ActualWidth}"
                      MaxHeight="{TemplateBinding MaxDropDownHeight}"
                      Background="@@Panel@@"
                      BorderBrush="@@Border@@"
                      BorderThickness="1"
                      CornerRadius="5"
                      Margin="0,2,0,0"
                      SnapsToDevicePixels="True">
                <ScrollViewer SnapsToDevicePixels="True">
                  <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Contained"/>
                </ScrollViewer>
              </Border>
            </Popup>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsEditable" Value="True">
              <Setter TargetName="PART_EditableTextBox" Property="Visibility" Value="Visible"/>
              <Setter TargetName="ContentSite" Property="Visibility" Value="Hidden"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type ComboBoxItem}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Padding" Value="8,4"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type ComboBoxItem}">
          <Border x:Name="Bd" Background="Transparent" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="@@PanelAlt@@"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#404E99D9"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type CheckBox}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type CheckBox}">
          <StackPanel Orientation="Horizontal" Background="Transparent">
            <Border x:Name="Box"
                    Width="15"
                    Height="15"
                    CornerRadius="4"
                    BorderThickness="1"
                    BorderBrush="@@Border@@"
                    Background="@@Input@@"
                    VerticalAlignment="Center"
                    SnapsToDevicePixels="True">
              <Path x:Name="Check"
                    Data="M 3 7.5 L 6 10.5 L 11.5 4"
                    Stroke="#FFFFFF"
                    StrokeThickness="1.7"
                    StrokeStartLineCap="Round"
                    StrokeEndLineCap="Round"
                    Visibility="Collapsed"/>
            </Border>
            <ContentPresenter Margin="7,0,0,0"
                              VerticalAlignment="Center"
                              RecognizesAccessKey="True"/>
          </StackPanel>
          <ControlTemplate.Triggers>
            <Trigger Property="IsChecked" Value="True">
              <Setter TargetName="Box" Property="Background" Value="@@Accent@@"/>
              <Setter TargetName="Box" Property="BorderBrush" Value="@@Accent@@"/>
              <Setter TargetName="Check" Property="Visibility" Value="Visible"/>
            </Trigger>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Box" Property="BorderBrush" Value="@@Accent@@"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type TabControl}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Background" Value="@@WindowBg@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="0"/>
    <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
    <Setter Property="VerticalContentAlignment" Value="Stretch"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type TabControl}">
          <Grid KeyboardNavigation.TabNavigation="Local" SnapsToDevicePixels="True">
            <Grid.RowDefinitions>
              <RowDefinition Height="Auto"/>
              <RowDefinition Height="*"/>
            </Grid.RowDefinitions>
            <Border Grid.Row="0"
                    Background="@@Panel@@"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="1,1,1,0">
              <TabPanel x:Name="HeaderPanel"
                        IsItemsHost="True"
                        KeyboardNavigation.TabIndex="1"/>
            </Border>
            <Border Grid.Row="1"
                    Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}"
                    Padding="{TemplateBinding Padding}">
              <ContentPresenter x:Name="PART_SelectedContentHost"
                                ContentSource="SelectedContent"
                                HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                SnapsToDevicePixels="{TemplateBinding SnapsToDevicePixels}"/>
            </Border>
          </Grid>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type TabItem}">
    <Setter Property="Foreground" Value="@@Muted@@"/>
    <Setter Property="Background" Value="@@Panel@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="Padding" Value="12,6"/>
    <Setter Property="HorizontalContentAlignment" Value="Center"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type TabItem}">
          <Grid SnapsToDevicePixels="True">
            <Border x:Name="Bd"
                    Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="0,0,1,0"/>
            <Border x:Name="AccentLine"
                    Height="2"
                    VerticalAlignment="Top"
                    Background="@@Accent@@"
                    Opacity="0"/>
            <ContentPresenter ContentSource="Header"
                              Margin="{TemplateBinding Padding}"
                              HorizontalAlignment="Center"
                              VerticalAlignment="Center"
                              RecognizesAccessKey="True"/>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="@@PanelAlt@@"/>
              <Setter Property="Foreground" Value="@@Text@@"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="@@WindowBg@@"/>
              <Setter TargetName="AccentLine" Property="Opacity" Value="1"/>
              <Setter Property="Foreground" Value="@@Text@@"/>
            </Trigger>
            <Trigger Property="IsKeyboardFocused" Value="True">
              <Setter TargetName="Bd" Property="BorderBrush" Value="@@Accent@@"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type ScrollBar}">
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Width" Value="8"/>
    <Setter Property="MinWidth" Value="8"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type ScrollBar}">
          <Grid Background="Transparent">
            <Track x:Name="PART_Track" IsDirectionReversed="True">
              <Track.DecreaseRepeatButton>
                <RepeatButton Command="ScrollBar.PageUpCommand" Opacity="0" Focusable="False" IsTabStop="False"/>
              </Track.DecreaseRepeatButton>
              <Track.Thumb>
                <Thumb>
                  <Thumb.Template>
                    <ControlTemplate TargetType="{x:Type Thumb}">
                      <Border x:Name="ThumbBd" Background="@@Border@@" CornerRadius="4" Margin="2"/>
                      <ControlTemplate.Triggers>
                        <Trigger Property="IsMouseOver" Value="True">
                          <Setter TargetName="ThumbBd" Property="Background" Value="@@BorderHover@@"/>
                        </Trigger>
                      </ControlTemplate.Triggers>
                    </ControlTemplate>
                  </Thumb.Template>
                </Thumb>
              </Track.Thumb>
              <Track.IncreaseRepeatButton>
                <RepeatButton Command="ScrollBar.PageDownCommand" Opacity="0" Focusable="False" IsTabStop="False"/>
              </Track.IncreaseRepeatButton>
            </Track>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="Orientation" Value="Horizontal">
              <Setter TargetName="PART_Track" Property="IsDirectionReversed" Value="False"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
    <Style.Triggers>
      <Trigger Property="Orientation" Value="Horizontal">
        <Setter Property="Width" Value="Auto"/>
        <Setter Property="Height" Value="8"/>
        <Setter Property="MinHeight" Value="8"/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType="{x:Type ListBox}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Background" Value="@@Input@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="2"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
  </Style>

  <Style TargetType="{x:Type ListView}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Background" Value="@@Input@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="2"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
  </Style>

  <Style TargetType="{x:Type ListViewItem}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Background" Value="Transparent"/>
    <Setter Property="Padding" Value="8,6"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Style.Triggers>
      <Trigger Property="IsMouseOver" Value="True">
        <Setter Property="Background" Value="@@PanelAlt@@"/>
      </Trigger>
      <Trigger Property="IsSelected" Value="True">
        <Setter Property="Background" Value="#302387F4"/>
      </Trigger>
      <Trigger Property="IsEnabled" Value="False">
        <Setter Property="Opacity" Value="0.5"/>
      </Trigger>
    </Style.Triggers>
  </Style>

  <Style TargetType="{x:Type GridViewColumnHeader}">
    <Setter Property="Foreground" Value="@@Muted@@"/>
    <Setter Property="Background" Value="@@Panel@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="BorderThickness" Value="0,0,0,1"/>
    <Setter Property="Padding" Value="10,7"/>
    <Setter Property="MinHeight" Value="34"/>
    <Setter Property="HorizontalContentAlignment" Value="Left"/>
    <Setter Property="VerticalContentAlignment" Value="Center"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type GridViewColumnHeader}">
          <Grid SnapsToDevicePixels="True">
            <Border x:Name="Bd"
                    Background="{TemplateBinding Background}"
                    BorderBrush="{TemplateBinding BorderBrush}"
                    BorderThickness="{TemplateBinding BorderThickness}">
              <ContentPresenter Margin="{TemplateBinding Padding}"
                                HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                                RecognizesAccessKey="True"/>
            </Border>
            <Thumb x:Name="PART_HeaderGripper"
                   Width="5"
                   HorizontalAlignment="Right"
                   Background="Transparent"
                   Cursor="SizeWE">
              <Thumb.Template>
                <ControlTemplate TargetType="{x:Type Thumb}">
                  <Border Background="Transparent"/>
                </ControlTemplate>
              </Thumb.Template>
            </Thumb>
          </Grid>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="@@Button@@"/>
              <Setter Property="Foreground" Value="@@Text@@"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type ListBoxItem}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Padding" Value="8,6"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type ListBoxItem}">
          <Border x:Name="Bd" Background="Transparent" CornerRadius="5" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
            <ContentPresenter/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsMouseOver" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="@@PanelAlt@@"/>
            </Trigger>
            <Trigger Property="IsSelected" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="#302387F4"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type ToolTip}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Background" Value="@@PanelAlt@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="8,5"/>
    <Setter Property="MaxWidth" Value="360"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type ToolTip}">
          <Border Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}"
                  CornerRadius="4"
                  Padding="{TemplateBinding Padding}"
                  SnapsToDevicePixels="True">
            <ContentPresenter/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type ContextMenu}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Background" Value="@@Panel@@"/>
    <Setter Property="BorderBrush" Value="@@Border@@"/>
    <Setter Property="BorderThickness" Value="1"/>
    <Setter Property="Padding" Value="4"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type ContextMenu}">
          <Border Background="{TemplateBinding Background}"
                  BorderBrush="{TemplateBinding BorderBrush}"
                  BorderThickness="{TemplateBinding BorderThickness}"
                  CornerRadius="4"
                  Padding="{TemplateBinding Padding}"
                  SnapsToDevicePixels="True">
            <StackPanel IsItemsHost="True" KeyboardNavigation.DirectionalNavigation="Cycle"/>
          </Border>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type MenuItem}">
    <Setter Property="Foreground" Value="@@Text@@"/>
    <Setter Property="Padding" Value="10,5"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
    <Setter Property="Template">
      <Setter.Value>
        <ControlTemplate TargetType="{x:Type MenuItem}">
          <Border x:Name="Bd" Background="Transparent" CornerRadius="3" Padding="{TemplateBinding Padding}" SnapsToDevicePixels="True">
            <ContentPresenter ContentSource="Header" RecognizesAccessKey="True"/>
          </Border>
          <ControlTemplate.Triggers>
            <Trigger Property="IsHighlighted" Value="True">
              <Setter TargetName="Bd" Property="Background" Value="@@PanelAlt@@"/>
            </Trigger>
            <Trigger Property="IsEnabled" Value="False">
              <Setter Property="Opacity" Value="0.5"/>
            </Trigger>
          </ControlTemplate.Triggers>
        </ControlTemplate>
      </Setter.Value>
    </Setter>
  </Style>

  <Style TargetType="{x:Type Separator}">
    <Setter Property="Background" Value="@@Border@@"/>
    <Setter Property="Height" Value="1"/>
    <Setter Property="Margin" Value="0,4"/>
    <Setter Property="SnapsToDevicePixels" Value="True"/>
  </Style>

</ResourceDictionary>
""";
}
