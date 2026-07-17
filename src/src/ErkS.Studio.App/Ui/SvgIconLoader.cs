using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace ErkS.Studio;

internal static class SvgIconLoader
{
    private static readonly XNamespace SvgNamespace = "http://www.w3.org/2000/svg";

    public static ImageSource? TryLoad(string? svgPath)
    {
        if (string.IsNullOrWhiteSpace(svgPath) || !File.Exists(svgPath))
        {
            return null;
        }

        try
        {
            var document = XDocument.Load(svgPath);
            var root = document.Root;
            if (root is null)
            {
                return null;
            }

            var viewBox = ReadViewBox(root);
            var group = new DrawingGroup();

            AddDrawings(root, group, Brushes.White);
            return RenderIcon(group, viewBox ?? group.Bounds);
        }
        catch
        {
            return CreateFallbackIcon();
        }
    }

    private static ImageSource RenderIcon(DrawingGroup logoDrawing, Rect sourceBounds)
    {
        const int size = 32;
        const double padding = 2;

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var target = new Rect(padding, padding, size - (padding * 2), size - (padding * 2));
            var scale = Math.Min(target.Width / sourceBounds.Width, target.Height / sourceBounds.Height);
            var offsetX = target.X + ((target.Width - (sourceBounds.Width * scale)) / 2);
            var offsetY = target.Y + ((target.Height - (sourceBounds.Height * scale)) / 2);

            context.PushTransform(new TranslateTransform(offsetX, offsetY));
            context.PushTransform(new ScaleTransform(scale, scale));
            context.PushTransform(new TranslateTransform(-sourceBounds.X, -sourceBounds.Y));
            context.DrawDrawing(logoDrawing);
            context.Pop();
            context.Pop();
            context.Pop();
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static ImageSource CreateFallbackIcon()
    {
        const int size = 32;
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            var text = new FormattedText(
                "S",
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"),
                24,
                Brushes.White,
                1);

            context.DrawText(text, new Point((size - text.Width) / 2, (size - text.Height) / 2));
        }

        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    private static void AddDrawings(XElement element, DrawingGroup group, Brush inheritedBrush)
    {
        var brush = ReadFillBrush(element) ?? inheritedBrush;

        foreach (var child in element.Elements())
        {
            if (child.Name == SvgNamespace + "path")
            {
                AddPath(child, group, brush);
                continue;
            }

            if (child.Name == SvgNamespace + "circle")
            {
                AddCircle(child, group, brush);
                continue;
            }

            AddDrawings(child, group, brush);
        }
    }

    private static void AddPath(XElement element, DrawingGroup group, Brush brush)
    {
        var data = (string?)element.Attribute("d");
        if (string.IsNullOrWhiteSpace(data))
        {
            return;
        }

        var geometry = Geometry.Parse(data);
        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        group.Children.Add(new GeometryDrawing(brush, null, geometry));
    }

    private static void AddCircle(XElement element, DrawingGroup group, Brush brush)
    {
        var centerX = ReadDouble(element, "cx");
        var centerY = ReadDouble(element, "cy");
        var radius = ReadDouble(element, "r");
        if (!centerX.HasValue || !centerY.HasValue || !radius.HasValue)
        {
            return;
        }

        var geometry = new EllipseGeometry(
            new Point(centerX.Value, centerY.Value),
            radius.Value,
            radius.Value);

        if (geometry.CanFreeze)
        {
            geometry.Freeze();
        }

        group.Children.Add(new GeometryDrawing(brush, null, geometry));
    }

    private static Rect? ReadViewBox(XElement root)
    {
        var raw = ((string?)root.Attribute("viewBox"))?.Trim();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var parts = raw
            .Split([' ', ','], StringSplitOptions.RemoveEmptyEntries)
            .Select(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
                ? value
                : double.NaN)
            .ToArray();

        return parts.Length == 4 && parts.All(value => !double.IsNaN(value))
            ? new Rect(parts[0], parts[1], parts[2], parts[3])
            : null;
    }

    private static Brush? ReadFillBrush(XElement element)
    {
        var fill = ((string?)element.Attribute("fill"))?.Trim();
        if (string.Equals(fill, "none", StringComparison.OrdinalIgnoreCase))
        {
            return Brushes.Transparent;
        }

        if (string.IsNullOrWhiteSpace(fill) || string.Equals(fill, "currentColor", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        try
        {
            var converted = ColorConverter.ConvertFromString(fill);
            if (converted is not Color color)
            {
                return null;
            }

            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze)
            {
                brush.Freeze();
            }

            return brush;
        }
        catch
        {
            return null;
        }
    }

    private static double? ReadDouble(XElement element, string attributeName)
    {
        var raw = (string?)element.Attribute(attributeName);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
