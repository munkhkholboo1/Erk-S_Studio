using System.Text;
using ErkS.Platform.Core;

Console.OutputEncoding = Encoding.UTF8;
if (args.Length != 2 || !string.Equals(args[0], "migrate", StringComparison.OrdinalIgnoreCase))
{
    Console.Error.WriteLine("Usage: ErkS.Studio.ProjectTool migrate <legacy.erksalbum>");
    return 2;
}

var legacyPath = Path.GetFullPath(args[1]);
if (!File.Exists(legacyPath))
{
    Console.Error.WriteLine($"Legacy project not found: {legacyPath}");
    return 3;
}

var result = LegacyAlbumProjectImporter.Import(legacyPath, persist: true);
Console.WriteLine($"Project: {result.Project.Code} - {result.Project.Name}");
Console.WriteLine($"Manifest: {result.ProjectPath}");
Console.WriteLine($"Album: {result.AlbumPath}");
Console.WriteLine($"Legacy preserved: {legacyPath}");
return 0;
