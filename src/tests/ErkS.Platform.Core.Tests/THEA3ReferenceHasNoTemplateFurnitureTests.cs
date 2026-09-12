using System.Text.Json;
using ErkS.Platform.Core;

namespace ErkS.Platform.Core.Tests;

/// <summary>
/// The A3 reference carries no instruction notes, no watermark and no red object.
///
/// ⚠ PRECISELY: 74 of the 75 objects record a colour and none is red; the one that
/// records none is the page outline, which is not drawn. The first phrasing of this line
/// said «nothing red» flatly and was one object too strong - the assertion below found
/// that, which is the whole argument for writing it as an assertion.
///
/// 🔴 WRITTEN TO HOLD A COMMENT THAT COULD NOT BE CHECKED. The cover writer carries a
/// paragraph saying «the four instruction notes, the ҮЛГЭРЧИЛСЭН ЗАГВАР watermark and the
/// red pointer circles are NOT drawn». The 2026-09-12 reconciliation counted all 75
/// objects of the sheet this method actually draws and found none of those things - so
/// the paragraph is about some other drawing, and it was reading as a checked statement
/// about this one.
///
/// 🔴 AND A MARKER IN PROSE WOULD HAVE BEEN THE SAME MISTAKE ONE LEVEL UP. Today this
/// repository found a comment claiming «a test holds this condition» where no such test
/// existed, and the lesson taken was: name the test, and prove it exists first. Writing
/// «the A3 measurement contains none of it» as a second unchecked sentence would have
/// earned the same correction. So the absence is asserted, from the measurement, here.
///
/// ⚠ AN ABSENCE NEEDS A POSITIVE CONTROL, because a search that read nothing would
/// report «no red objects» just as confidently. Every assertion below is preceded by one
/// that must find something.
/// </summary>
public sealed class THEA3ReferenceHasNoTemplateFurnitureTests
{
    /// <summary>AutoCAD colour 1. The paragraph's «red pointer circles» would be this.</summary>
    private const int Red = 1;

    [Fact]
    public void NOTHINGOnTheA3SheetIsRED()
    {
        JsonElement sheet = Measurement();

        // The positive control first: the objects were actually read, and they do carry
        // a colour. Without this, an empty read reports the same conclusion.
        int counted = 0;
        var colours = new HashSet<int>();
        var unrecorded = new List<int>();
        foreach (string list in new[] { "sheetTexts", "tableTexts", "lines" })
        {
            foreach (JsonElement item in sheet.GetProperty(list).EnumerateArray())
            {
                counted++;
                JsonElement colour = item.GetProperty("colorCode");
                if (colour.ValueKind == JsonValueKind.Number)
                    colours.Add(colour.GetInt32());
                else
                    unrecorded.Add(item.GetProperty("index").GetInt32());
            }
        }

        Assert.Equal(75, counted);
        Assert.NotEmpty(colours);

        // No recorded colour is red. 44 objects are colour 0 and 30 are colour 7.
        Assert.DoesNotContain(Red, colours);

        // 🔴 AND ONE OBJECT HAS NO COLOUR RECORDED, WHICH A FIRST VERSION OF THIS TEST
        // CRASHED ON AND A FIRST VERSION OF THE PROSE CLAIMED AWAY. «Nothing on the sheet
        // is red» was one object too strong: index 0 carries colorCode AND lineweightCode
        // both null - a ByLayer boundary the extractor did not resolve.
        //
        // Naming it makes the claim stronger rather than weaker: the single object whose
        // colour is unknown is the PAGE OUTLINE, 420 x 297 at the origin, which the
        // reconciliation had already classified as the CAD sheet boundary and not
        // content. So the unknown sits on the one object that is not drawn either way.
        Assert.Equal([0], unrecorded);

        JsonElement outline = sheet.GetProperty("lines").EnumerateArray()
            .Single(line => line.GetProperty("index").GetInt32() == 0);
        Assert.Equal(420d, outline.GetProperty("boundingBoxMm").GetProperty("widthMm").GetDouble());
        Assert.Equal(297d, outline.GetProperty("boundingBoxMm").GetProperty("heightMm").GetDouble());
    }

    [Fact]
    public void THEREIsNoWATERMARKAndNoInstructionNote()
    {
        JsonElement sheet = Measurement();

        var texts = new List<string>();
        foreach (string list in new[] { "sheetTexts", "tableTexts" })
        {
            foreach (JsonElement item in sheet.GetProperty(list).EnumerateArray())
                texts.Add(item.GetProperty("text").GetString() ?? "");
        }

        // Positive control: the texts were read, and one the sheet certainly has is
        // present. An empty list would pass every «does not contain» below.
        Assert.Equal(43, texts.Count);
        Assert.Contains(texts, text => text.Contains("ЗӨВШИЛЦСӨН", StringComparison.Ordinal));

        Assert.DoesNotContain(texts, text => text.Contains("ҮЛГЭРЧИЛСЭН", StringComparison.Ordinal));
        Assert.DoesNotContain(texts, text => text.Contains("ЗАГВАР ЗУРАГ", StringComparison.Ordinal)
            && !text.StartsWith("/", StringComparison.Ordinal));
    }

    [Fact]
    public void THECountsCrossCheckSoTheReconciliationHadNoBlindSpot()
    {
        // 🔴 THE COMPLETENESS CONTROL FOR THE WHOLE RECONCILIATION. Two independent
        // counts of the same sheet agree: the census by entity type, and the three lists
        // the reconciliation walked. Without this, «every object was compared» would be
        // a claim about a number nobody checked - and a list that silently held 60 of 75
        // would have produced a confident, wrong «reconciled».
        JsonElement sheet = Measurement();
        JsonElement census = sheet.GetProperty("objectCensus");

        int byType = 0;
        foreach (JsonProperty kind in census.EnumerateObject())
            byType += kind.Value.GetInt32();

        int byList =
            sheet.GetProperty("sheetTexts").GetArrayLength() +
            sheet.GetProperty("tableTexts").GetArrayLength() +
            sheet.GetProperty("lines").GetArrayLength();

        Assert.Equal(sheet.GetProperty("objectTotal").GetInt32(), byType);
        Assert.Equal(byType, byList);
        Assert.Equal(75, byList);

        // The texts split the same way: MTEXT plus TEXT is the two text lists together.
        Assert.Equal(
            census.GetProperty("MTEXT").GetInt32() + census.GetProperty("TEXT").GetInt32(),
            sheet.GetProperty("sheetTexts").GetArrayLength() +
            sheet.GetProperty("tableTexts").GetArrayLength());
    }

    /// <summary>
    /// The measurement, from the copy that travels with these tests.
    ///
    /// ⚠ THE VENDORED COPY, NOT the original in the other repository - and this file
    /// exists partly because that copy was missing until 2026-09-12, while product
    /// constants had already been taken from it.
    /// </summary>
    private static JsonElement Measurement() =>
        JsonDocument.Parse(SharedContractCopies.Read(SharedContractCopies.ConceptCoverA3Full))
            .RootElement;
}
