using System.Globalization;
using System.Text.Json;

namespace ErkS.Platform.Core;

/// <summary>
/// What a read of the published catalogue produced: either the units and the
/// version they belong to, or the reason it produced neither.
/// </summary>
/// <param name="ProblemMn">
/// Empty on success. Names WHAT was wrong and WHERE, because the alternative -
/// an empty list - is indistinguishable from "this sum has no bags" and from
/// "nothing downloaded yet", and a reader who cannot tell those apart looks for
/// the fault on whichever side they guess.
/// </param>
/// <param name="SkippedRollUpRows">
/// Rows dropped for having a code that is not 3, 5 or 7 digits: national and
/// regional totals, which the catalogue publishes alongside the units. Reported
/// rather than merely dropped - a count that suddenly doubles means the source
/// changed shape.
/// </param>
/// <param name="UnitsPropertyName">
/// Which property the rows were found under. Reported because the envelope is
/// an assumption (see <see cref="AdministrativeUnitDocument"/>) and an
/// assumption that reports itself can be checked the day the route ships.
/// </param>
public sealed record AdministrativeUnitDocumentRead(
    IReadOnlyList<AdministrativeUnit> Units,
    DateTimeOffset? AsOfUtc,
    string ProblemMn,
    int SkippedRollUpRows = 0,
    string UnitsPropertyName = "")
{
    public bool IsUsable => ProblemMn.Length == 0 && Units.Count > 0;

    public static AdministrativeUnitDocumentRead Failed(string problemMn) =>
        new([], null, problemMn);
}

/// <summary>
/// The ONE place that knows the shape of the published catalogue - the bytes the
/// server sends and the bytes the cache keeps, which are deliberately the same
/// bytes.
///
/// 🔴 THE ENVELOPE IS ASSUMED, THE ROWS ARE NOT. SRV published real rows in
/// `_shared/mongolia-admin-divisions-contract-2026-09-06.json` - every field
/// name below was copied from it - but the contract never says what wraps them,
/// and the route was not live to be measured when this was written. So the
/// wrapper is found by SHAPE rather than by an agreed name, and a wrapper this
/// reader cannot recognise fails LOUDLY, with a sentence naming what was looked
/// for. An empty list would have been the quiet version of the same event, and
/// quiet is exactly what sent somebody to the wrong side of the wire yesterday.
///
/// THREE DELIBERATE STRICTNESSES, each a refusal to half-read an authoritative
/// list:
///
///   * A malformed row rejects the WHOLE document. Half a catalogue is a list
///     with places missing from it, and nothing downstream can tell which.
///   * A `parentUnitCode` that disagrees with the code prefix rejects it too.
///     The catalogue publishes both so that two independent statements exist;
///     a disagreement is a finding, and choosing one of them silently is how a
///     restore starts failing for reasons nobody can see.
///   * A code that is not 3, 5 or 7 digits is a TOTAL - «Улсын дүн» - so it is
///     skipped, counted, and reported.
/// </summary>
public static class AdministrativeUnitDocument
{
    /// <summary>
    /// Read the catalogue out of JSON. Never throws on bad content: a reason in
    /// the reader's language is the product here, and an exception crossing this
    /// boundary would only be turned back into a sentence by whoever caught it,
    /// with less to say than this has.
    /// </summary>
    public static AdministrativeUnitDocumentRead Read(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return AdministrativeUnitDocumentRead.Failed("Серверийн хариу хоосон байна.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException error)
        {
            return AdministrativeUnitDocumentRead.Failed(
                "Серверийн хариу JSON биш байна: " + error.Message);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind == JsonValueKind.Array)
            {
                return AdministrativeUnitDocumentRead.Failed(
                    "Серверийн хариу нүцгэн массив байна. Каталогийн хувилбар " +
                    "(asOfUtc) хамт ирэх ёстой — хувилбаргүй жагсаалт нь аль " +
                    "өдрийнх болохыг хэлж чадахгүй.");
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return AdministrativeUnitDocumentRead.Failed(
                    "Серверийн хариу объект биш байна (" + root.ValueKind + ").");
            }

            if (!TryFindRows(root, out JsonElement rows, out string rowsProperty))
            {
                return AdministrativeUnitDocumentRead.Failed(
                    "Серверийн хариунаас нэгжийн жагсаалт олдсонгүй. " +
                    "«unitCode» талбартай объектуудын массив хайсан.");
            }

            if (!TryReadAsOfUtc(root, out DateTimeOffset asOfUtc, out string asOfProblem))
                return AdministrativeUnitDocumentRead.Failed(asOfProblem);

            return ReadRows(rows, asOfUtc, rowsProperty);
        }
    }

    /// <summary>
    /// Finds the rows by SHAPE - the first array whose first element carries a
    /// `unitCode` - rather than by an agreed property name.
    ///
    /// Searching by name would mean guessing between `units`, `divisions`,
    /// `items` and whatever the route actually uses, and a guess that misses
    /// looks precisely like an empty catalogue. The name that DID match is
    /// carried back out, so the assumption can be checked against the real route
    /// rather than believed.
    /// </summary>
    private static bool TryFindRows(JsonElement root, out JsonElement rows, out string propertyName)
    {
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Array)
                continue;

            JsonElement.ArrayEnumerator items = property.Value.EnumerateArray();
            if (!items.MoveNext())
                continue;
            if (items.Current.ValueKind != JsonValueKind.Object)
                continue;
            if (!items.Current.TryGetProperty("unitCode", out _))
                continue;

            rows = property.Value;
            propertyName = property.Name;
            return true;
        }

        rows = default;
        propertyName = "";
        return false;
    }

    /// <summary>
    /// The catalogue's version. Two spellings are accepted because the contract
    /// itself uses both - `asOfUtc` on the bundled copy, `catalogueAsOfUtc` on a
    /// stored project - and the route was not live to settle which one it sends.
    ///
    /// Its ABSENCE is fatal rather than tolerated. A catalogue with no version
    /// makes "chosen from a three-month-old offline copy" and "chosen against
    /// live data" the same sentence, which is the one thing both sides agreed
    /// must never happen.
    /// </summary>
    private static bool TryReadAsOfUtc(JsonElement root, out DateTimeOffset asOfUtc, out string problemMn)
    {
        asOfUtc = default;
        foreach (JsonProperty property in root.EnumerateObject())
        {
            if (!property.NameEquals("asOfUtc") && !property.NameEquals("catalogueAsOfUtc"))
                continue;
            if (property.Value.ValueKind != JsonValueKind.String)
                continue;

            if (!DateTimeOffset.TryParse(
                    property.Value.GetString(),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                    out asOfUtc))
            {
                problemMn = "Каталогийн хувилбарын огноо уншигдсангүй: «" +
                    property.Value.GetString() + "».";
                return false;
            }

            problemMn = "";
            return true;
        }

        problemMn =
            "Серверийн хариунд каталогийн хувилбар (asOfUtc) алга. Хувилбаргүй " +
            "жагсаалтаас сонгосон хаяг нь шинэ өгөгдлөөс сонгосонтой ялгагдахгүй болно.";
        return false;
    }

    private static AdministrativeUnitDocumentRead ReadRows(
        JsonElement rows,
        DateTimeOffset asOfUtc,
        string rowsProperty)
    {
        List<AdministrativeUnit> units = [];
        HashSet<string> seenCodes = new(StringComparer.Ordinal);
        int skipped = 0;
        int index = -1;

        foreach (JsonElement row in rows.EnumerateArray())
        {
            index++;
            if (row.ValueKind != JsonValueKind.Object)
                return Reject(index, "объект биш байна");

            string unitCode = ReadText(row, "unitCode");
            if (unitCode.Length == 0)
                return Reject(index, "«unitCode» алга");

            if (!AdministrativeUnits.IsSelectableUnit(unitCode))
            {
                // A total, not a place. Counted, so that a change in how many of
                // them arrive is visible rather than absorbed.
                skipped++;
                continue;
            }

            string nameMn = ReadText(row, "nameMn");
            if (nameMn.Length == 0)
                return Reject(index, "«nameMn» алга (код " + unitCode + ")");

            // The code is the key everything downstream matches on - a restore,
            // a roster suggestion, a grouping. Two rows sharing one would put the
            // same place in a picker twice and let a restore land on either.
            if (!seenCodes.Add(unitCode))
                return Reject(index, "код «" + unitCode + "» давхардсан");

            string parentUnitCode = ReadText(row, "parentUnitCode");
            string derivedParent = AdministrativeUnits.ParentCodeOf(unitCode);
            if (!parentUnitCode.Equals(derivedParent, StringComparison.Ordinal))
            {
                return AdministrativeUnitDocumentRead.Failed(
                    "Каталогийн мөр өөртэйгөө зөрчилдөж байна: код «" + unitCode +
                    "»-ийн эцэг «" + derivedParent + "» байх ёстой атал «" +
                    parentUnitCode + "» гэж бичигдсэн байна.");
            }

            units.Add(new AdministrativeUnit(
                unitCode,
                ReadText(row, "level"),
                parentUnitCode,
                nameMn,
                ReadText(row, "childPickerLabelMn")));
        }

        if (units.Count == 0)
        {
            return AdministrativeUnitDocumentRead.Failed(
                "Серверийн хариунд сонгох боломжтой нэгж алга (" + skipped +
                " нийлбэр мөр л ирсэн).");
        }

        return new AdministrativeUnitDocumentRead(units, asOfUtc, "", skipped, rowsProperty);

        static AdministrativeUnitDocumentRead Reject(int index, string whatIsWrong) =>
            AdministrativeUnitDocumentRead.Failed(
                "Каталогийн " + (index + 1) + "-р мөр уншигдсангүй: " + whatIsWrong + ". " +
                "Хагас уншсан каталог нь барилга байж болох газрыг чимээгүй " +
                "хасна, тиймээс бүтнээр нь татгалзав.");
    }

    private static string ReadText(JsonElement row, string propertyName) =>
        row.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? (value.GetString() ?? "").Trim()
            : "";
}
