using System.Text.Json;
using ErkS.Platform.Pdf;

if (args.Length != 1)
{
    Console.Error.WriteLine("Usage: ErkS.PackageAcceptance <manifest.erks-sheets.json>");
    return 64;
}

try
{
    SheetPackageAcceptanceReport report = SheetPackageAcceptanceValidator.Validate(args[0]);
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return report.IsAccepted ? 0 : 2;
}
catch (Exception exception) when (
    exception is ArgumentException or IOException or UnauthorizedAccessException or InvalidDataException)
{
    Console.Error.WriteLine(exception.Message);
    return 2;
}
