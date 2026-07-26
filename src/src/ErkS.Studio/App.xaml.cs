using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ErkS.Studio;

public partial class App : Application
{
    private const string ReleaseSmokeTestArgument = "--release-smoke-test";
    private const string ReleaseSmokeOutputArgument = "--release-smoke-output=";
    private const string ReleaseUpdateHoldArgument = "--release-update-hold-test";
    private const string ReleaseUpdateReadyArgument = "--release-update-ready=";
    private const string ReleaseSmokeFailureLog = "release-smoke-failure.log";
    private static readonly SmokeRenderScenario[] ReleaseSmokeScenarios =
    [
        new("desktop-100", 1366, 768, 96),
        new("desktop-150", 1920, 1080, 144),
        new("desktop-200", 2560, 1440, 192),
    ];

    protected override void OnStartup(StartupEventArgs args)
    {
        base.OnStartup(args);
        if (args.Args.Any(argument => string.Equals(
                argument,
                ReleaseUpdateHoldArgument,
                StringComparison.OrdinalIgnoreCase)))
        {
            RunReleaseUpdateHoldTest(args.Args);
            return;
        }

        if (args.Args.Any(argument => string.Equals(
                argument,
                ReleaseSmokeTestArgument,
                StringComparison.OrdinalIgnoreCase)))
        {
            Shutdown(RunReleaseSmokeTest(args.Args));
            return;
        }

        var window = new StudioHostWindow();
        MainWindow = window;
        window.Show();
    }

    private void RunReleaseUpdateHoldTest(IReadOnlyList<string> arguments)
    {
        string readyPath = GetArgumentValue(arguments, ReleaseUpdateReadyArgument);
        if (readyPath.Length == 0)
        {
            Shutdown(2);
            return;
        }

        var window = new Window
        {
            Title = "Erk-S Studio update acceptance",
            Width = 360,
            Height = 140,
            Left = -10000,
            Top = -10000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowInTaskbar = false,
            Content = new System.Windows.Controls.TextBlock
            {
                Margin = new Thickness(24),
                Text = "Waiting for the signed updater...",
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            },
        };
        window.Loaded += (_, _) =>
        {
            string? directory = Path.GetDirectoryName(readyPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(
                readyPath,
                $"{Environment.ProcessId}{Environment.NewLine}{AppContext.BaseDirectory}");
        };

        MainWindow = window;
        window.Show();
    }

    private static int RunReleaseSmokeTest(IReadOnlyList<string> arguments)
    {
        LoadedStudioModule? loaded = null;
        try
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            var runtime = new StudioRuntime();
            if (runtime.IsDevMode)
            {
                throw new InvalidOperationException(
                    "The installed product unexpectedly contains a development root marker.");
            }

            loaded = runtime.LoadStaticModule();
            UIElement root = loaded.Module.CreateRootView()
                ?? throw new InvalidOperationException(
                    "The production app module did not create a root view.");
            string outputDirectory = GetSmokeOutputDirectory(arguments);
            if (outputDirectory.Length > 0)
            {
                Directory.CreateDirectory(outputDirectory);
            }

            var results = new List<object>();
            foreach (SmokeRenderScenario scenario in ReleaseSmokeScenarios)
            {
                SmokeRenderResult result = RenderSmokeScenario(root, scenario, outputDirectory);
                results.Add(new
                {
                    scenario.Name,
                    scenario.PixelWidth,
                    scenario.PixelHeight,
                    scenario.Dpi,
                    result.LogicalWidth,
                    result.LogicalHeight,
                    result.SampledPixels,
                    result.VisiblePixels,
                    result.DistinctColorBuckets,
                    result.OutputPath,
                });
            }

            if (outputDirectory.Length > 0)
            {
                File.WriteAllText(
                    Path.Combine(outputDirectory, "manifest.json"),
                    JsonSerializer.Serialize(
                        new
                        {
                            generatedAtUtc = DateTimeOffset.UtcNow,
                            scenarios = results,
                        },
                        new JsonSerializerOptions { WriteIndented = true }));
            }

            return 0;
        }
        catch (Exception exception)
        {
            try
            {
                File.WriteAllText(
                    Path.Combine(AppContext.BaseDirectory, ReleaseSmokeFailureLog),
                    exception.ToString());
            }
            catch
            {
            }

            return 1;
        }
        finally
        {
            if (loaded is not null)
            {
                StudioRuntime.Retire(loaded);
            }
        }
    }

    private static SmokeRenderResult RenderSmokeScenario(
        UIElement root,
        SmokeRenderScenario scenario,
        string outputDirectory)
    {
        double logicalWidth = scenario.PixelWidth * 96d / scenario.Dpi;
        double logicalHeight = scenario.PixelHeight * 96d / scenario.Dpi;
        var logicalSize = new Size(logicalWidth, logicalHeight);
        root.Measure(logicalSize);
        root.Arrange(new Rect(logicalSize));
        root.UpdateLayout();
        if (root.RenderSize.Width <= 0 || root.RenderSize.Height <= 0)
        {
            throw new InvalidOperationException(
                $"The production root view did not complete the {scenario.Name} layout pass.");
        }

        var bitmap = new RenderTargetBitmap(
            scenario.PixelWidth,
            scenario.PixelHeight,
            scenario.Dpi,
            scenario.Dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(root);

        (int sampledPixels, int visiblePixels, int distinctColorBuckets) =
            MeasureRenderedPixels(bitmap);
        if (visiblePixels < sampledPixels / 4)
        {
            throw new InvalidOperationException(
                $"The {scenario.Name} render is mostly transparent " +
                $"({visiblePixels}/{sampledPixels} visible samples).");
        }
        if (distinctColorBuckets < 4)
        {
            throw new InvalidOperationException(
                $"The {scenario.Name} render appears blank " +
                $"({distinctColorBuckets} sampled color buckets).");
        }

        string outputPath = "";
        if (outputDirectory.Length > 0)
        {
            outputPath = Path.Combine(outputDirectory, scenario.Name + ".png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using FileStream stream = File.Create(outputPath);
            encoder.Save(stream);
        }

        return new SmokeRenderResult(
            logicalWidth,
            logicalHeight,
            sampledPixels,
            visiblePixels,
            distinctColorBuckets,
            outputPath);
    }

    private static (int SampledPixels, int VisiblePixels, int DistinctColorBuckets)
        MeasureRenderedPixels(BitmapSource bitmap)
    {
        int stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var buckets = new HashSet<int>();
        int sampled = 0;
        int visible = 0;
        const int sampleStep = 8;

        for (int y = 0; y < bitmap.PixelHeight; y += sampleStep)
        {
            int row = y * stride;
            for (int x = 0; x < bitmap.PixelWidth; x += sampleStep)
            {
                int index = row + (x * 4);
                byte blue = pixels[index];
                byte green = pixels[index + 1];
                byte red = pixels[index + 2];
                byte alpha = pixels[index + 3];
                sampled++;
                if (alpha > 16)
                {
                    visible++;
                    buckets.Add(((red >> 4) << 8) | ((green >> 4) << 4) | (blue >> 4));
                }
            }
        }

        return (sampled, visible, buckets.Count);
    }

    private static string GetSmokeOutputDirectory(IEnumerable<string> arguments)
    {
        string value = GetArgumentValue(arguments, ReleaseSmokeOutputArgument);
        return value.Length == 0 ? "" : Path.GetFullPath(value);
    }

    private static string GetArgumentValue(
        IEnumerable<string> arguments,
        string argumentPrefix)
    {
        string? argument = arguments.FirstOrDefault(value =>
            value.StartsWith(argumentPrefix, StringComparison.OrdinalIgnoreCase));
        if (argument is null)
        {
            return "";
        }

        return argument[argumentPrefix.Length..].Trim().Trim('"');
    }

    private sealed record SmokeRenderScenario(
        string Name,
        int PixelWidth,
        int PixelHeight,
        double Dpi);

    private sealed record SmokeRenderResult(
        double LogicalWidth,
        double LogicalHeight,
        int SampledPixels,
        int VisiblePixels,
        int DistinctColorBuckets,
        string OutputPath);
}
