using System.Reflection;
using System.Text;
using ErkS.Platform.Core;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The contribution snapshot - the album rebuilt for upload - copies the
/// album definition field by field. The front matter was left out and the
/// shared album's 01 printed blank (Batnasan, 2026-10-20); a field added to
/// AlbumDefinition later would be lost the same way, so every one is checked.
/// </summary>
public sealed class THECONTRIBUTIONSnapshotCarriesTheWholeAlbumTests
{
    [Fact]
    public void EveryAlbumDefinitionPropertyIsCopied()
    {
        string view = ReadView("ShellView.AlbumComponents.cs");
        int start = view.IndexOf("buildProject.Album = new AlbumDefinition", StringComparison.Ordinal);
        Assert.True(start >= 0, "the snapshot copy moved; check this test with it");
        string block = view[start..view.IndexOf("};", start, StringComparison.Ordinal)];

        foreach (PropertyInfo property in typeof(AlbumDefinition).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!property.CanWrite)
                continue;
            Assert.Contains(property.Name + " =", block, StringComparison.Ordinal);
        }
    }

    private static string ReadView(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate, Encoding.UTF8).Replace("\r\n", "\n");
            directory = directory.Parent;
        }

        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }
}
