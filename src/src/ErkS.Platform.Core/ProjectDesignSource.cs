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

    /// <summary>
    /// Stable sheet ids that remain visible in the source workspace but are
    /// intentionally omitted from this project's album. The native/source PDF
    /// and its package manifest are never modified.
    /// </summary>
    public List<string> InactiveSheetIds { get; set; } = [];

    /// <summary>
    /// Project-owned album placement retained while a source sheet is
    /// inactive. Optional so projects saved before this feature remain
    /// readable.
    /// </summary>
    public List<ProjectInactiveSourceSheetState> InactiveSheetStates { get; set; } = [];

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Kind.ToString() : Name;

    public bool IsSheetActive(string? sheetId) =>
        string.IsNullOrWhiteSpace(sheetId) ||
        !(InactiveSheetIds ?? []).Contains(sheetId.Trim(), StringComparer.OrdinalIgnoreCase);

    public void SetSheetActive(string? sheetId, bool active)
    {
        string normalized = sheetId?.Trim() ?? "";
        if (normalized.Length == 0)
        {
            return;
        }

        InactiveSheetIds ??= [];
        InactiveSheetIds.RemoveAll(existing =>
            string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
        if (!active)
        {
            InactiveSheetIds.Add(normalized);
        }
    }

    public void NormalizeSheetActivityState()
    {
        InactiveSheetIds = (InactiveSheetIds ?? [])
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var inactiveIds = InactiveSheetIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        InactiveSheetStates = (InactiveSheetStates ?? [])
            .Where(state => state is not null)
            .Select(state =>
            {
                state.Normalize();
                return state;
            })
            .Where(state =>
                state.SheetId.Length > 0 &&
                inactiveIds.Contains(state.SheetId))
            .GroupBy(state => state.SheetId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();
    }

    public void StoreInactiveSheetState(ProjectInactiveSourceSheetState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        state.Normalize();
        if (state.SheetId.Length == 0)
        {
            return;
        }

        InactiveSheetStates ??= [];
        InactiveSheetStates.RemoveAll(existing =>
            string.Equals(existing.SheetId, state.SheetId, StringComparison.OrdinalIgnoreCase));
        InactiveSheetStates.Add(state);
    }

    public ProjectInactiveSourceSheetState? TakeInactiveSheetState(string? sheetId)
    {
        string normalized = sheetId?.Trim() ?? "";
        if (normalized.Length == 0 || InactiveSheetStates is null)
        {
            return null;
        }

        int index = InactiveSheetStates.FindLastIndex(state =>
            string.Equals(state.SheetId, normalized, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            return null;
        }

        ProjectInactiveSourceSheetState state = InactiveSheetStates[index];
        InactiveSheetStates.RemoveAt(index);
        state.Normalize();
        return state;
    }

    public void RemoveSheetActivityState(string? sheetId)
    {
        string normalized = sheetId?.Trim() ?? "";
        if (normalized.Length == 0)
        {
            return;
        }

        InactiveSheetIds ??= [];
        InactiveSheetIds.RemoveAll(existing =>
            string.Equals(existing, normalized, StringComparison.OrdinalIgnoreCase));
        InactiveSheetStates ??= [];
        InactiveSheetStates.RemoveAll(existing =>
            string.Equals(existing.SheetId, normalized, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class ProjectInactiveSourceSheetState
{
    public string SheetId { get; set; } = "";

    public string SheetKey { get; set; } = "";

    public string BuildingGroupId { get; set; } = "";

    public List<ProjectInactiveAlbumPageState> AlbumPages { get; set; } = [];

    public List<ProjectInactiveAlbumSectionPlacement> SectionPlacements { get; set; } = [];

    public void Normalize()
    {
        SheetId = SheetId?.Trim() ?? "";
        SheetKey = SheetKey?.Trim() ?? "";
        BuildingGroupId = BuildingGroupId?.Trim() ?? "";
        AlbumPages ??= [];
        SectionPlacements ??= [];
        foreach (ProjectInactiveAlbumPageState pageState in AlbumPages)
        {
            pageState.AlbumIndex = Math.Max(0, pageState.AlbumIndex);
            pageState.Page ??= new AlbumPageDefinition();
            pageState.Page.SheetKey = string.IsNullOrWhiteSpace(pageState.Page.SheetKey)
                ? SheetKey
                : pageState.Page.SheetKey.Trim();
        }
        foreach (ProjectInactiveAlbumSectionPlacement placement in SectionPlacements)
        {
            placement.SheetIndex = Math.Max(0, placement.SheetIndex);
        }
    }
}

public sealed class ProjectInactiveAlbumPageState
{
    public int AlbumIndex { get; set; }

    public AlbumPageDefinition Page { get; set; } = new();
}

public sealed class ProjectInactiveAlbumSectionPlacement
{
    public Guid SectionId { get; set; }

    public int SheetIndex { get; set; }
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
