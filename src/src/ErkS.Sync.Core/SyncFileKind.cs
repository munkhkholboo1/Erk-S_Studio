namespace ErkS.Sync.Core;

/// <summary>What a file in a project folder is to Erk-S (plan §4.6: SYN knows Erk-S content).</summary>
public enum SyncFileKind
{
    Attachment,
    Dwg,
    Dxf,
    RevitCentral,
    RevitCentralData,
    RevitModel,
    RevitFamily,
    Pdf,
    Image,
    ErkSCadDocument,
    SheetPackage,
    Office,
}

public static class SyncFileKinds
{
    public static SyncFileKind Of(string relativePath, bool isRevitCentral = false, bool insideRevitCentralBackup = false)
    {
        if (insideRevitCentralBackup)
            return SyncFileKind.RevitCentralData;
        string name = Path.GetFileName(relativePath);
        if (name.EndsWith(".erks-sheets.json", StringComparison.OrdinalIgnoreCase))
            return SyncFileKind.SheetPackage;
        return Path.GetExtension(name).ToLowerInvariant() switch
        {
            ".dwg" => SyncFileKind.Dwg,
            ".dxf" => SyncFileKind.Dxf,
            ".rvt" => isRevitCentral ? SyncFileKind.RevitCentral : SyncFileKind.RevitModel,
            ".rfa" or ".rte" => SyncFileKind.RevitFamily,
            ".pdf" => SyncFileKind.Pdf,
            ".png" or ".jpg" or ".jpeg" or ".tif" or ".tiff" or ".bmp" or ".webp" => SyncFileKind.Image,
            ".erks" => SyncFileKind.ErkSCadDocument,
            ".docx" or ".doc" or ".xlsx" or ".xls" or ".pptx" or ".ppt" => SyncFileKind.Office,
            _ => SyncFileKind.Attachment,
        };
    }
}
