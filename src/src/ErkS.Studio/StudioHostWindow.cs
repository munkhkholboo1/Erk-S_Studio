using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;
using System.Windows.Threading;

namespace ErkS.Studio;

/// <summary>
/// Thin host window: owns the process and the window frame, hosts the
/// hot-reloadable app view, and provides DevUpdate (Ctrl+U) so development
/// never needs a program restart. Deliberately has no dependency on the app
/// module's theme or services.
/// </summary>
internal sealed class StudioHostWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private static readonly Brush BarBackground = new SolidColorBrush(Color.FromRgb(22, 25, 29));
    private static readonly Brush BarBorder = new SolidColorBrush(Color.FromRgb(47, 52, 60));
    private static readonly Brush BarText = new SolidColorBrush(Color.FromRgb(164, 171, 182));
    private static readonly Brush HostBackground = new SolidColorBrush(Color.FromRgb(18, 20, 23));
    private static readonly Brush ChromeHover = new SolidColorBrush(Color.FromRgb(40, 44, 51));
    private static readonly Brush ChromePressed = new SolidColorBrush(Color.FromRgb(54, 59, 68));
    private static readonly Brush CloseHover = new SolidColorBrush(Color.FromRgb(196, 43, 28));
    private static readonly FontFamily GlyphFont = new("Segoe MDL2 Assets");

    private readonly StudioRuntime runtime = new();
    private readonly Grid contentHost = new();
    private readonly TextBlock devStatusText = new();
    private readonly CheckBox autoReloadCheck = new()
    {
        Content = "Auto",
        Foreground = BarText,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 0, 0),
        ToolTip = "App модулийн шинэ билд гарнгуут автоматаар ачаалах",
    };

    private LoadedStudioModule? loaded;
    private FileSystemWatcher? devWatcher;
    private DispatcherTimer? devReloadTimer;
    private bool reloadInProgress;
    private bool staticFallback;
    private HwndSource? windowSource;
    private readonly TextBlock maximizeGlyph = new()
    {
        Text = "\uE922",
        FontFamily = GlyphFont,
        FontSize = 10,
        Foreground = Brushes.White,
    };

    public StudioHostWindow()
    {
        staticFallback = runtime.PreferStaticModule;
        Title = "Erk-S Studio";
        Width = 1280;
        Height = 820;
        MinWidth = 980;
        MinHeight = 640;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = HostBackground;
        Icon = TryLoadWindowIcon();
        UseLayoutRounding = true;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.CanResize;
        WindowChrome.SetWindowChrome(this, new WindowChrome
        {
            CaptionHeight = 42,
            ResizeBorderThickness = new Thickness(6),
            GlassFrameThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            UseAeroCaptionButtons = false,
        });

        var root = new DockPanel();
        var titleBar = BuildTitleBar();
        DockPanel.SetDock(titleBar, Dock.Top);
        root.Children.Add(titleBar);
        if (runtime.IsDevMode && !runtime.PreferStaticModule)
            StartDevWatcher();

        root.Children.Add(contentHost);
        Content = root;
        StateChanged += (_, _) => maximizeGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        SourceInitialized += (_, _) => AttachWindowBoundsHook();

        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.U && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                args.Handled = true;
                _ = DevUpdateAsync();
            }
        };
        Closed += (_, _) =>
        {
            windowSource?.RemoveHook(WindowProc);
            windowSource = null;
            devWatcher?.Dispose();
            if (loaded is not null)
            {
                StudioRuntime.Retire(loaded);
            }
        };

        LoadAppModule();
    }

    private void AttachWindowBoundsHook()
    {
        var handle = new WindowInteropHelper(this).Handle;
        windowSource = HwndSource.FromHwnd(handle);
        windowSource?.AddHook(WindowProc);
    }

    private static IntPtr WindowProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmGetMinMaxInfo || lParam == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>(),
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return IntPtr.Zero;
        }

        var bounds = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        bounds.MaxPosition.X = monitorInfo.WorkArea.Left - monitorInfo.MonitorArea.Left;
        bounds.MaxPosition.Y = monitorInfo.WorkArea.Top - monitorInfo.MonitorArea.Top;
        bounds.MaxSize.X = monitorInfo.WorkArea.Right - monitorInfo.WorkArea.Left;
        bounds.MaxSize.Y = monitorInfo.WorkArea.Bottom - monitorInfo.WorkArea.Top;
        bounds.MaxTrackSize = bounds.MaxSize;
        Marshal.StructureToPtr(bounds, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect MonitorArea;
        public NativeRect WorkArea;
        public uint Flags;
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

    private UIElement BuildTitleBar()
    {
        var bar = new DockPanel
        {
            Height = 42,
            LastChildFill = true,
        };

        var windowControls = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
        };
        var minimize = CreateChromeButton("\uE921", "Багасгах");
        minimize.Click += (_, _) => WindowState = WindowState.Minimized;
        var maximize = CreateChromeButton(maximizeGlyph, "Томруулах / сэргээх");
        maximize.Click += (_, _) => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
        var close = CreateChromeButton("\uE8BB", "Хаах", CloseHover);
        close.Click += (_, _) => Close();
        windowControls.Children.Add(minimize);
        windowControls.Children.Add(maximize);
        windowControls.Children.Add(close);
        DockPanel.SetDock(windowControls, Dock.Right);
        bar.Children.Add(windowControls);

        if (runtime.IsDevMode)
        {
            var devTools = BuildDevTools();
            DockPanel.SetDock(devTools, Dock.Right);
            bar.Children.Add(devTools);
        }

        var identity = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 0, 0),
        };
        var icon = Icon;
        if (icon is not null)
        {
            identity.Children.Add(new Border
            {
                Width = 23,
                Height = 23,
                Margin = new Thickness(0, 0, 9, 0),
                Padding = new Thickness(4),
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromRgb(242, 244, 247)),
                Child = new Image { Source = icon, Stretch = Stretch.Uniform },
            });
        }
        identity.Children.Add(new TextBlock
        {
            Text = "Erk-S Studio",
            FontFamily = new FontFamily("Segoe UI Variable Text"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
        });
        bar.Children.Add(identity);

        return new Border
        {
            Background = BarBackground,
            BorderBrush = BarBorder,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = bar,
        };
    }

    private UIElement BuildDevTools()
    {
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        panel.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(35, 67, 101)),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 2, 6, 2),
            Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock
            {
                Text = "DEV",
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(173, 211, 255)),
            },
        });

        var updateButton = CreateChromeButton("\uE72C", "DevUpdate (Ctrl+U)", ChromeHover, 34, 30);
        updateButton.Click += (_, _) => _ = DevUpdateAsync();
        panel.Children.Add(updateButton);

        autoReloadCheck.Content = "Auto";
        autoReloadCheck.FontSize = 11;
        autoReloadCheck.VerticalAlignment = VerticalAlignment.Center;
        autoReloadCheck.Margin = new Thickness(6, 0, 8, 0);
        autoReloadCheck.IsEnabled = !runtime.PreferStaticModule;
        if (runtime.PreferStaticModule)
            autoReloadCheck.ToolTip = "Application Control policy mode-д Auto reload ашиглахгүй.";
        WindowChrome.SetIsHitTestVisibleInChrome(autoReloadCheck, true);
        panel.Children.Add(autoReloadCheck);

        devStatusText.Foreground = BarText;
        devStatusText.FontSize = 11;
        devStatusText.VerticalAlignment = VerticalAlignment.Center;
        devStatusText.Width = 240;
        devStatusText.TextTrimming = TextTrimming.CharacterEllipsis;
        panel.Children.Add(devStatusText);
        return panel;
    }

    private static Button CreateChromeButton(
        string glyph,
        string tooltip,
        Brush? hover = null,
        double width = 46,
        double height = 42) => CreateChromeButton(new TextBlock
        {
            Text = glyph,
            FontFamily = GlyphFont,
            FontSize = 10,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        }, tooltip, hover, width, height);

    private static Button CreateChromeButton(
        UIElement content,
        string tooltip,
        Brush? hover = null,
        double width = 46,
        double height = 42)
    {
        var button = new Button
        {
            Width = width,
            Height = height,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Content = content,
            ToolTip = tooltip,
            Focusable = false,
        };
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border), "ChromeButtonBackground");
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding(nameof(Button.Background))
        {
            RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent),
        });
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        border.AppendChild(presenter);
        template.VisualTree = border;
        var hoverTrigger = new Trigger { Property = IsMouseOverProperty, Value = true };
        hoverTrigger.Setters.Add(new Setter(Border.BackgroundProperty, hover ?? ChromeHover, "ChromeButtonBackground"));
        template.Triggers.Add(hoverTrigger);
        var pressedTrigger = new Trigger { Property = Button.IsPressedProperty, Value = true };
        pressedTrigger.Setters.Add(new Setter(Border.BackgroundProperty, ChromePressed, "ChromeButtonBackground"));
        template.Triggers.Add(pressedTrigger);
        button.Template = template;
        WindowChrome.SetIsHitTestVisibleInChrome(button, true);
        return button;
    }

    private void LoadAppModule()
    {
        try
        {
            var previous = loaded;
            LoadedStudioModule? fresh = null;
            UIElement view;
            try
            {
                fresh = staticFallback ? runtime.LoadStaticModule() : runtime.LoadModule();
                view = fresh.Module.CreateRootView();
            }
            catch (Exception exception) when (
                !staticFallback &&
                (IsApplicationControlBlock(exception) || exception is FileNotFoundException))
            {
                if (fresh is not null)
                {
                    StudioRuntime.Retire(fresh);
                }

                staticFallback = true;
                fresh = runtime.LoadStaticModule();
                view = fresh.Module.CreateRootView();
            }

            contentHost.Children.Clear();
            contentHost.Children.Add(view);
            loaded = fresh;

            if (previous is not null)
            {
                StudioRuntime.Retire(previous);
            }

            var mode = fresh.IsStaticFallback ? " · policy fallback" : "";
            SetDevStatus($"App v{fresh.Module.Version} · ачаалсан {fresh.LoadedAt:HH:mm:ss}{mode}");
        }
        catch (Exception exception)
        {
            ShowLoadError(exception.Message);
        }
    }

    private async Task DevUpdateAsync()
    {
        if (!runtime.IsDevMode || reloadInProgress)
        {
            return;
        }

        reloadInProgress = true;
        try
        {
            SetDevStatus("Build хийж байна...");
            if (staticFallback)
            {
                var (staticSuccess, staticOutput, executablePath) =
                    await runtime.DevBuildSingleFileHostAsync();
                if (!staticSuccess || executablePath is null)
                {
                    SetDevStatus($"Build амжилтгүй: {FirstBuildError(staticOutput)}");
                    return;
                }

                Process.Start(new ProcessStartInfo(executablePath)
                {
                    WorkingDirectory = Path.GetDirectoryName(executablePath)!,
                    UseShellExecute = true,
                });
                Application.Current.Shutdown();
                return;
            }

            var (success, output) = await runtime.DevBuildAsync();
            if (!success)
            {
                SetDevStatus($"Build амжилтгүй: {FirstBuildError(output)}");
                return;
            }

            staticFallback = false;
            LoadAppModule();
        }
        finally
        {
            reloadInProgress = false;
        }
    }

    private static string FirstBuildError(string output)
    {
        return output
            .Split('\n')
            .FirstOrDefault(line => line.Contains("error", StringComparison.OrdinalIgnoreCase))?
            .Trim() ?? "build failed";
    }

    private static bool IsApplicationControlBlock(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current.HResult == unchecked((int)0x800711C7) ||
                current.Message.Contains("Application Control policy", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private void StartDevWatcher()
    {
        try
        {
            Directory.CreateDirectory(runtime.AppSourceDirectory);
            devReloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            devReloadTimer.Tick += (_, _) =>
            {
                devReloadTimer.Stop();
                if (autoReloadCheck.IsChecked == true && !reloadInProgress)
                {
                    LoadAppModule();
                }
            };

            devWatcher = new FileSystemWatcher(runtime.AppSourceDirectory)
            {
                Filter = "ErkS.Studio.App.dll",
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
                EnableRaisingEvents = true,
            };
            devWatcher.Changed += (_, _) => QueueAutoReload();
            devWatcher.Created += (_, _) => QueueAutoReload();
            devWatcher.Renamed += (_, _) => QueueAutoReload();
        }
        catch
        {
            // Dev watcher is best-effort; manual Ctrl+U always works.
        }
    }

    private void QueueAutoReload()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            devReloadTimer?.Stop();
            devReloadTimer?.Start();
        }));
    }

    private void ShowLoadError(string message)
    {
        var panel = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 560,
        };
        panel.Children.Add(new TextBlock
        {
            Text = "App модулийг ачаалж чадсангүй",
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 8),
        });
        panel.Children.Add(new TextBlock
        {
            Text = message,
            Foreground = BarText,
            TextWrapping = TextWrapping.Wrap,
        });
        if (runtime.IsDevMode)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "Засаад Ctrl+U дарж дахин ачаална уу.",
                Foreground = BarText,
                Margin = new Thickness(0, 8, 0, 0),
            });
        }

        contentHost.Children.Clear();
        contentHost.Children.Add(panel);
        SetDevStatus("Ачаалал амжилтгүй.");
    }

    private void SetDevStatus(string message)
    {
        devStatusText.Text = message;
    }
}
