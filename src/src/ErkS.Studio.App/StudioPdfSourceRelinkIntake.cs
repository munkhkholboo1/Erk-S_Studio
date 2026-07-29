using ErkS.Platform.Core;
using ErkS.Platform.Pdf;
using System.IO;

namespace ErkS.Studio;

internal sealed record StudioPdfSourceRelinkIntakeResult(
    LocalPdfSheetPackageImportResult Import,
    SheetIntakeScanResult Scan);

/// <summary>
/// Makes an explicit PDF relink immediately visible in the source workspace.
/// The caller is responsible for admitting the source to the runtime watcher
/// first; this helper never bypasses account/device locality policy.
/// </summary>
internal static class StudioPdfSourceRelinkIntake
{
    public static StudioPdfSourceRelinkIntakeResult ImportAndRescan(
        ProjectWorkspace project,
        ProjectDesignSource source,
        SheetIntakeService intake)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(intake);
        if (source.Kind != DesignSourceKind.Pdf)
            throw new ArgumentException("Only PDF design sources can be imported.", nameof(source));

        LocalPdfSheetPackageImportResult imported =
            new LocalPdfSheetPackageImporter().Import(project, source);
        SheetIntakeScanResult scan = intake.RescanFolders([source.InboxFolder]);
        if (scan.ErrorCount > 0 || scan.RejectedPackageCount > 0)
        {
            throw new InvalidDataException(
                "Relinked PDF package was created but could not be verified by Studio intake.");
        }

        return new StudioPdfSourceRelinkIntakeResult(imported, scan);
    }
}
