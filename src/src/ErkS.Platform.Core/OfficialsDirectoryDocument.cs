using System.Text.Json;

namespace ErkS.Platform.Core;

/// <summary>
/// One row the reader would not keep, and why.
/// </summary>
/// <param name="RowNumber">Its place in the file, counting from one - the number
/// a person looking at their own list can find.</param>
/// <param name="ReasonMn">What was wrong, in the reader's language.</param>
public sealed record OfficialsDirectoryRejection(int RowNumber, string ReasonMn);

/// <summary>
/// What came out of an officials file: the rows kept, and every row refused.
/// </summary>
/// <param name="Entries">The officials that will actually answer a lookup.</param>
/// <param name="AsOfUtc">Which version this is, from whoever issued it.</param>
/// <param name="ProblemMn">
/// Why the whole document is unusable; empty when it is usable. A document-level
/// failure and a rejected row are different events: the first means nothing was
/// read, the second means this row was not.
/// </param>
/// <param name="Rejected">
/// 🔴 THE REFUSED ROWS TRAVEL WITH THE RESULT, NOT INTO A LOG. Somebody who
/// loads thirty-six rows and works with thirty-five must learn it from their own
/// screen: a log is a place the person holding the file will never look, and the
/// loss is otherwise perfectly silent - every lookup simply answers «nobody».
/// </param>
public sealed record OfficialsDirectoryRead(
    IReadOnlyList<OfficialsDirectoryEntry> Entries,
    DateTimeOffset? AsOfUtc,
    string ProblemMn,
    IReadOnlyList<OfficialsDirectoryRejection> Rejected)
{
    public bool IsUsable => ProblemMn.Length == 0;

    public static OfficialsDirectoryRead Failed(string problemMn) =>
        new([], null, problemMn, []);

    /// <summary>
    /// A sentence naming what was lost, or empty when nothing was. Built here so
    /// every screen says it the same way.
    /// </summary>
    public string LossMn =>
        Rejected.Count == 0
            ? ""
            : $"{Rejected.Count} мөр уншигдсангүй: " +
                string.Join(
                    "; ",
                    Rejected.Select(row => $"{row.RowNumber}-р мөр — {row.ReasonMn}"));
}

/// <summary>
/// The ONE place that knows the shape of an officials file.
///
/// 🔴 THE MECHANISM COMES FROM <see cref="AdministrativeUnitDocument"/>, THE
/// RULES DO NOT. That reader was the model for reading an authoritative list
/// without half-reading it; borrowing its DECISIONS would be a different thing,
/// and two of them are wrong here:
///
///   * IT REJECTS THE WHOLE DOCUMENT FOR ONE BAD ROW. Right for a published
///     catalogue of places, where a gap is invisible downstream. Wrong for a
///     list somebody maintains by hand: a typo on row 17 would lock them out of
///     the other thirty-five until they found it. Here the row is refused, the
///     rest are kept, and the loss is REPORTED - which is the same protection
///     by a different route.
///   * IT REJECTS DUPLICATE CODES. Wrong here by design: several officials serve
///     one district, and the owner named TWO from a single office.
///
///   * IT FINDS THE ENVELOPE BY SHAPE. That was forced - the route was not live
///     to be measured. This format is defined here, so the wrapper is named and
///     a file without it fails loudly rather than being sniffed.
/// </summary>
public static class OfficialsDirectoryDocument
{
    /// <summary>The property the rows live under.</summary>
    public const string OfficialsProperty = "officials";

    /// <summary>
    /// Read an officials file. Never throws on bad content: a reason in the
    /// reader's language is the product here.
    /// </summary>
    public static OfficialsDirectoryRead Read(string? json)
    {
        string text = (json ?? "").Trim();
        if (text.Length == 0)
            return OfficialsDirectoryRead.Failed("Албан тушаалтны файл хоосон байна.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text);
        }
        catch (JsonException exception)
        {
            return OfficialsDirectoryRead.Failed(
                "Албан тушаалтны файл уншигдсангүй: " + exception.Message);
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return OfficialsDirectoryRead.Failed(
                    "Албан тушаалтны файлын дээд түвшин объект байх ёстой, " +
                    $"«{OfficialsProperty}» талбартай.");
            }

            if (!document.RootElement.TryGetProperty(OfficialsProperty, out JsonElement rows) ||
                rows.ValueKind != JsonValueKind.Array)
            {
                return OfficialsDirectoryRead.Failed(
                    $"Албан тушаалтны файлд «{OfficialsProperty}» массив олдсонгүй.");
            }

            return ReadRows(rows, ReadAsOf(document.RootElement));
        }
    }

    private static OfficialsDirectoryRead ReadRows(JsonElement rows, DateTimeOffset? asOfUtc)
    {
        var kept = new List<OfficialsDirectoryEntry>();
        var rejected = new List<OfficialsDirectoryRejection>();
        var rowNumber = 0;

        foreach (JsonElement row in rows.EnumerateArray())
        {
            rowNumber++;

            if (row.ValueKind != JsonValueKind.Object)
            {
                rejected.Add(new OfficialsDirectoryRejection(rowNumber, "объект биш байна"));
                continue;
            }

            string unitCode = ReadText(row, "unitCode");
            if (unitCode.Length == 0)
            {
                rejected.Add(new OfficialsDirectoryRejection(rowNumber, "«unitCode» алга"));
                continue;
            }

            // 🔴 THE FIVE-DIGIT LEVEL AND NOTHING ELSE. A ward code here would be
            // stored under a key no lookup ever asks with - present in the file,
            // absent from every answer, and nothing anywhere would say so.
            if (!IsLookupLevel(unitCode))
            {
                rejected.Add(new OfficialsDirectoryRejection(
                    rowNumber,
                    $"«{unitCode}» нь сум/дүүргийн 5 оронтой код биш — энэ түвшинд хайлт хийгддэггүй"));
                continue;
            }

            string organizationName = ReadText(row, "organizationName");
            if (organizationName.Length == 0)
            {
                rejected.Add(new OfficialsDirectoryRejection(rowNumber, "«organizationName» алга"));
                continue;
            }

            string kindText = ReadText(row, "kind");
            if (kindText.Length == 0)
            {
                rejected.Add(new OfficialsDirectoryRejection(rowNumber, "«kind» алга"));
                continue;
            }

            // 🔴 AN UNKNOWN KIND IS REFUSED, NEVER PASSED THROUGH. Each kind
            // carries a table placement, which is a decision nobody can make from
            // a string in a file. Storing one would put an unplaceable official
            // in the directory and leave the sheet to discover it.
            if (!Enum.TryParse(kindText, ignoreCase: false, out OfficialBodyKind kind) ||
                !Enum.IsDefined(kind))
            {
                rejected.Add(new OfficialsDirectoryRejection(
                    rowNumber,
                    $"«{kindText}» гэсэн байгууллагын төрөл танигдсангүй"));
                continue;
            }

            var entry = new OfficialsDirectoryEntry
            {
                UnitCode = unitCode,
                Kind = kind,
                OrganizationName = organizationName,
                PositionTitle = ReadText(row, "positionTitle"),

                // An office whose occupant is unknown is a real state - the paper
                // form prints the office with a line to sign - so the name is not
                // required and its absence is not a rejection.
                PersonName = ReadText(row, "personName"),
            };
            entry.Normalize();
            kept.Add(entry);
        }

        return new OfficialsDirectoryRead(kept, asOfUtc, "", rejected);
    }

    /// <summary>
    /// Whether a code names the level officials are looked up by - a sum or a
    /// district. Deliberately NARROWER than
    /// <see cref="AdministrativeUnits.IsSelectableUnit"/>, which answers «is this
    /// a place» for all three levels; this answers «can this be found».
    /// </summary>
    public static bool IsLookupLevel(string? unitCode)
    {
        string code = (unitCode ?? "").Trim();
        return code.Length == 5 && code.All(char.IsDigit);
    }

    private static DateTimeOffset? ReadAsOf(JsonElement root) =>
        root.TryGetProperty("asOfUtc", out JsonElement value) &&
        value.ValueKind == JsonValueKind.String &&
        value.TryGetDateTimeOffset(out DateTimeOffset parsed)
            ? parsed
            : null;

    private static string ReadText(JsonElement row, string propertyName) =>
        row.TryGetProperty(propertyName, out JsonElement value) &&
        value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "").Trim()
            : "";
}
