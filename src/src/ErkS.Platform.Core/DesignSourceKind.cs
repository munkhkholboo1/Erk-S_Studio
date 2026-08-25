namespace ErkS.Platform.Core;

/// <summary>
/// What kind of thing a design source is, for the reader rather than for the
/// machine.
/// </summary>
/// <remarks>
/// The source list had every entry looking the same, and a Revit model, a set
/// of AutoCAD sheets and a folder of renders read as one undifferentiated pile
/// - "ач холбогдолгүй сүргүй хачин жагсаалт", as the user put it. Kind is the
/// first thing someone wants to know about a row, so it is the first thing the
/// row shows.
/// </remarks>
public enum DesignSourceCategory
{
    /// <summary>Nothing in the record says what this is.</summary>
    Unknown,

    /// <summary>A Revit model delivering sheets.</summary>
    Revit,

    /// <summary>An AutoCAD drawing delivering sheets.</summary>
    AutoCad,

    /// <summary>Rendered images placed by Studio itself.</summary>
    Visualization,

    /// <summary>A CityGen site model.</summary>
    CityGen,

    /// <summary>Pages taken straight from a PDF.</summary>
    Pdf,

    /// <summary>A colleague's source, held in the cloud and read-only here.</summary>
    Cloud,
}

/// <summary>
/// Reads a source's kind from what its record actually says.
/// </summary>
public static class DesignSourceCategories
{
    /// <param name="application">
    /// The producing application as the package reported it - "Revit",
    /// "AutoCAD", or whatever a future one calls itself.
    /// </param>
    /// <param name="isVisualization">The Studio-managed image source.</param>
    /// <param name="hasLocalPayload">
    /// False when nothing of this source is on this device, which is what makes
    /// it somebody else's.
    /// </param>
    public static DesignSourceCategory Classify(
        string? application,
        bool isVisualization,
        bool hasLocalPayload)
    {
        if (isVisualization)
            return DesignSourceCategory.Visualization;

        // The application is asked first even for a cloud-held source: knowing
        // it is a Revit model says more than knowing it is somebody else's, and
        // the row shows whose it is in its group heading anyway.
        string name = (application ?? "").Trim();
        if (name.Contains("revit", StringComparison.OrdinalIgnoreCase))
            return DesignSourceCategory.Revit;
        if (name.Contains("autocad", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("acad", StringComparison.OrdinalIgnoreCase))
        {
            return DesignSourceCategory.AutoCad;
        }

        if (name.Contains("citygen", StringComparison.OrdinalIgnoreCase))
            return DesignSourceCategory.CityGen;
        if (name.Contains("pdf", StringComparison.OrdinalIgnoreCase))
            return DesignSourceCategory.Pdf;

        return hasLocalPayload ? DesignSourceCategory.Unknown : DesignSourceCategory.Cloud;
    }

    /// <summary>What the badge reads.</summary>
    public static string Label(DesignSourceCategory category) => category switch
    {
        DesignSourceCategory.Revit => "Revit",
        DesignSourceCategory.AutoCad => "AutoCAD",
        DesignSourceCategory.Visualization => "Харагдах байдал",
        DesignSourceCategory.CityGen => "CityGen",
        DesignSourceCategory.Pdf => "PDF",
        DesignSourceCategory.Cloud => "Үүлнээс",
        _ => "Эх үүсвэр",
    };
}
