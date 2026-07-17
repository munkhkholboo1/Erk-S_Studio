using System.Text.Json.Serialization;

namespace ErkS.Platform.Core;

/// <summary>
/// A registered source of design sheets. Native DWG/RVT files remain at the
/// source; Studio receives only PDF renders and their manifest metadata.
/// </summary>
public sealed class ProjectDesignSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public DesignSourceKind Kind { get; set; } = DesignSourceKind.Folder;

    public string Name { get; set; } = "";

    public string ApplicationVersion { get; set; } = "";

    public string NativeDocumentTitle { get; set; } = "";

    public string NativeDocumentPath { get; set; } = "";

    /// <summary>Folder where PDF plus manifest packages are received.</summary>
    public string InboxFolder { get; set; } = "";

    public Guid? StageId { get; set; }

    public Guid? WorkPackageId { get; set; }

    public string OwnerOrganizationName { get; set; } = "";

    public string Status { get; set; } = DesignSourceStatuses.WaitingForConnection;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastPackageAtUtc { get; set; }

    /// <summary>
    /// Version 1 folder migration uses old sheet keys so existing album section
    /// references continue to resolve.
    /// </summary>
    public bool UseLegacySheetKeys { get; set; }

    public Dictionary<string, string> Metadata { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Kind.ToString() : Name;
}

public enum DesignSourceKind
{
    Revit,
    AutoCad,
    CityGen,
    Pdf,
    Folder,
}

public static class DesignSourceStatuses
{
    public const string WaitingForConnection = "WaitingForConnection";
    public const string Connected = "Connected";
    public const string Receiving = "Receiving";
    public const string Error = "Error";
}
