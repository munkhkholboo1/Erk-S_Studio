using System.Globalization;
using System.Security.Cryptography;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Pdf;

public sealed class PdfVectorDocumentProfile
{
    public required IReadOnlyList<PdfVectorPageProfile> Pages { get; init; }

    public string ToGoldenText() => string.Join(
        Environment.NewLine,
        Pages.Select((page, index) => page.ToGoldenText(index + 1)));
}

public sealed class PdfVectorPageProfile
{
    public required double WidthMm { get; init; }
    public required double HeightMm { get; init; }
    public required double MediaBoxWidthMm { get; init; }
    public required double MediaBoxHeightMm { get; init; }
    public required double CropBoxWidthMm { get; init; }
    public required double CropBoxHeightMm { get; init; }
    public required IReadOnlyList<string> Operators { get; init; }
    public required IReadOnlyList<PdfVectorOperatorProfile> OperatorDetails { get; init; }
    public required IReadOnlyList<PdfVectorXObjectProfile> XObjects { get; init; }
    public required string ContentSha256 { get; init; }

    public bool HasTextOperators => Operators.Any(value => value is "Tj" or "TJ" or "'" or "\"");

    public bool HasPathPaintingOperators => Operators.Any(value =>
        value is "S" or "s" or "f" or "F" or "f*" or "B" or "B*" or "b" or "b*");

    public int ImageXObjectCount => XObjects.Count(item => item.Kind == PdfVectorXObjectKind.Image);

    public int FormXObjectCount => XObjects.Count(item => item.Kind == PdfVectorXObjectKind.Form);

    public string OperatorSignature => string.Join(' ', Operators);

    internal string ToGoldenText(int pageNumber)
    {
        var histogram = Operators
            .GroupBy(value => value, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => $"{group.Key}:{group.Count()}");
        var forms = XObjects
            .Where(item => item.Kind == PdfVectorXObjectKind.Form)
            .Select(item => $"{Number(item.WidthMm)}x{Number(item.HeightMm)}")
            .OrderBy(value => value, StringComparer.Ordinal);
        return string.Join(
            Environment.NewLine,
            $"PAGE {pageNumber} {Number(WidthMm)}x{Number(HeightMm)} " +
            $"MEDIA {Number(MediaBoxWidthMm)}x{Number(MediaBoxHeightMm)} " +
            $"CROP {Number(CropBoxWidthMm)}x{Number(CropBoxHeightMm)}",
            $"OPS {string.Join(',', histogram)}",
            $"XOBJECT IMAGE:{ImageXObjectCount} FORM:{FormXObjectCount} BBOX:{string.Join(',', forms)}",
            $"CONTENT {ContentSha256}");
    }

    private static string Number(double value) =>
        Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}

public sealed class PdfVectorOperatorProfile
{
    public required string Name { get; init; }
    public required IReadOnlyList<double> NumericOperands { get; init; }
}

public sealed class PdfVectorXObjectProfile
{
    public required PdfVectorXObjectKind Kind { get; init; }
    public double WidthMm { get; init; }
    public double HeightMm { get; init; }
}

public enum PdfVectorXObjectKind
{
    Other,
    Form,
    Image,
}

/// <summary>
/// Inspects PDF structure without rendering it. Golden tests use this to catch
/// page-box drift and accidental full-page raster fallbacks.
/// </summary>
public static class PdfVectorQualityInspector
{
    private const double MillimetersPerPoint = 25.4 / 72.0;

    public static PdfVectorDocumentProfile Inspect(string path)
    {
        using var document = PdfReader.Open(path, PdfDocumentOpenMode.Import);
        return new PdfVectorDocumentProfile
        {
            Pages = document.Pages
                .Cast<PdfPage>()
                .Select(InspectPage)
                .ToList(),
        };
    }

    private static PdfVectorPageProfile InspectPage(PdfPage page)
    {
        CSequence content = ContentReader.ReadContent(page);
        var operatorDetails = EnumerateOperators(content)
            .Select(CreateOperatorProfile)
            .ToList();
        var xObjects = new List<PdfVectorXObjectProfile>();
        var visited = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        InspectResources(page.Elements.GetDictionary("/Resources"), xObjects, visited);
        PdfRectangle mediaBox = page.MediaBoxReadOnly;
        PdfRectangle cropBox = page.EffectiveCropBoxReadOnly;

        return new PdfVectorPageProfile
        {
            WidthMm = page.Width.Millimeter,
            HeightMm = page.Height.Millimeter,
            MediaBoxWidthMm = ToMillimeters(mediaBox.Width),
            MediaBoxHeightMm = ToMillimeters(mediaBox.Height),
            CropBoxWidthMm = ToMillimeters(cropBox.Width),
            CropBoxHeightMm = ToMillimeters(cropBox.Height),
            Operators = operatorDetails.Select(item => item.Name).ToList(),
            OperatorDetails = operatorDetails,
            XObjects = xObjects,
            ContentSha256 = Convert.ToHexString(SHA256.HashData(content.ToContent())).ToLowerInvariant(),
        };
    }

    private static PdfVectorOperatorProfile CreateOperatorProfile(COperator operation) => new()
    {
        Name = operation.Name,
        NumericOperands = operation.Operands
            .Select(operand => operand switch
            {
                CInteger integer => (double)integer.Value,
                CReal real => real.Value,
                _ => double.NaN,
            })
            .ToList(),
    };

    private static IEnumerable<COperator> EnumerateOperators(CSequence sequence)
    {
        foreach (CObject item in sequence)
        {
            if (item is COperator operation)
            {
                yield return operation;
            }
            else if (item is CSequence nested)
            {
                foreach (COperator nestedOperation in EnumerateOperators(nested))
                {
                    yield return nestedOperation;
                }
            }
        }
    }

    private static void InspectResources(
        PdfDictionary? resources,
        ICollection<PdfVectorXObjectProfile> profiles,
        ISet<PdfDictionary> visited)
    {
        if (resources is null || !visited.Add(resources))
        {
            return;
        }

        PdfDictionary? xObjects = resources.Elements.GetDictionary("/XObject");
        if (xObjects is null)
        {
            return;
        }

        foreach (string key in xObjects.Elements.Keys)
        {
            PdfItem? item = xObjects.Elements[key];
            if (item is PdfReference reference)
            {
                item = reference.Value;
            }
            if (item is not PdfDictionary dictionary)
            {
                continue;
            }

            string subtype = dictionary.Elements.GetName("/Subtype");
            if (string.Equals(subtype, "/Image", StringComparison.Ordinal))
            {
                profiles.Add(new PdfVectorXObjectProfile { Kind = PdfVectorXObjectKind.Image });
                continue;
            }
            if (string.Equals(subtype, "/Form", StringComparison.Ordinal))
            {
                PdfRectangle bounds = dictionary.Elements.GetRectangle("/BBox");
                profiles.Add(new PdfVectorXObjectProfile
                {
                    Kind = PdfVectorXObjectKind.Form,
                    WidthMm = ToMillimeters(bounds.Width),
                    HeightMm = ToMillimeters(bounds.Height),
                });
                InspectResources(dictionary.Elements.GetDictionary("/Resources"), profiles, visited);
                continue;
            }

            profiles.Add(new PdfVectorXObjectProfile { Kind = PdfVectorXObjectKind.Other });
        }
    }

    private static double ToMillimeters(double points) => points * MillimetersPerPoint;
}
