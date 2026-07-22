using System.IO;
using System.Reflection;

namespace ErkS.Studio;

internal static class SiteContextMapRuntimeAsset
{
    internal const string FileName = "site-context-map.html";

    private static readonly object ExtractionGate = new();

    internal static string EnsureExtracted(string? destinationFolder = null)
    {
        byte[] content = ReadEmbeddedContent();
        string folder = destinationFolder ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ErkS",
            "Studio",
            "WebRuntime");
        string destinationPath = Path.Combine(Path.GetFullPath(folder), FileName);

        lock (ExtractionGate)
        {
            Directory.CreateDirectory(folder);
            if (File.Exists(destinationPath) &&
                File.ReadAllBytes(destinationPath).AsSpan().SequenceEqual(content))
            {
                return destinationPath;
            }

            string temporaryPath = destinationPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllBytes(temporaryPath, content);
                File.Move(temporaryPath, destinationPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                    File.Delete(temporaryPath);
            }
        }

        return destinationPath;
    }

    internal static string ReadEmbeddedHtml()
    {
        return System.Text.Encoding.UTF8.GetString(ReadEmbeddedContent());
    }

    private static byte[] ReadEmbeddedContent()
    {
        Assembly assembly = typeof(SiteContextMapRuntimeAsset).Assembly;
        string? resourceName = assembly
            .GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(
                ".Assets.site-context-map.html",
                StringComparison.OrdinalIgnoreCase));
        if (resourceName is null)
            throw new InvalidOperationException("Studio map runtime resource is missing.");

        using Stream stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Studio map runtime resource could not be opened.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
