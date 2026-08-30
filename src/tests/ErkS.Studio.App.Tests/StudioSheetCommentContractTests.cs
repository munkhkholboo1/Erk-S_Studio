using System.Text.Json;
using ErkS.Studio;

namespace ErkS.Studio.App.Tests;

/// <summary>
/// The comment rules Studio keeps a copy of, and the reason the copy exists.
/// </summary>
/// <remarks>
/// The ONE integration audit filed this as a hand-made duplicate of the
/// server's constants and asked for it to come from the generate pipeline
/// instead. It cannot, yet: cloud-era-v1.openapi.json declares the comment
/// types but publishes none of the values - no enum for kind, status or shape,
/// no maxLength for the body - so a generated client has nothing to generate
/// from. That is the finding, and it belongs to the contract rather than to
/// this file.
///
/// So the copy stays and is held here instead, two ways. The values are pinned
/// against the server's SheetCommentRules, which is read for the comparison but
/// not depended on at build time; and the contract is checked for the day it
/// starts publishing them, because the useful moment to delete the copy is the
/// moment it becomes redundant, and nothing else would announce it.
/// </remarks>
public sealed class StudioSheetCommentContractTests
{
    [Fact]
    public void TheKindsStatusesAndShapesAreTheServersWordsForThem()
    {
        // Wire values, not display text: the server stores and compares these
        // strings. A rename here is a rename of stored data.
        Assert.Equal("Note", StudioSheetCommentRules.KindNote);
        Assert.Equal("ChangeRequired", StudioSheetCommentRules.KindChangeRequired);
        Assert.Equal("Approved", StudioSheetCommentRules.KindApproved);
        Assert.Equal("Open", StudioSheetCommentRules.StatusOpen);
        Assert.Equal("Resolved", StudioSheetCommentRules.StatusResolved);

        Assert.Equal(
            ["Cloud", "Rectangle", "Arrow", "Freehand", "Pin"],
            StudioSheetCommentRules.Shapes);

        // The order is part of it: the server returns comments by this ranking
        // and Studio re-sorts locally, so a different order here would show a
        // freshly written comment in one place and the reloaded one in another.
        Assert.Equal(
            ["ChangeRequired", "Note", "Approved"],
            StudioSheetCommentRules.Kinds);
    }

    [Fact]
    public void TheLimitsAreTheServersLimits()
    {
        Assert.Equal(4000, StudioSheetCommentRules.MaximumBodyLength);
        Assert.Equal(240, StudioSheetCommentRules.MaximumPageLabelLength);
        Assert.Equal(400, StudioSheetCommentRules.MaximumShapePoints);
    }

    [Fact]
    public void APastedBodyIsCleanedTheWayTheServerCleansIt()
    {
        // The drift that was found: the server collapses runs of blank lines
        // before storing, and Studio did not, so a pasted note was shown one
        // way and kept another.
        Assert.Equal(
            "нэг\n\nхоёр",
            StudioSheetCommentRules.CleanBody("  нэг\r\n\r\n\r\n\r\nхоёр  "));
    }

    [Fact]
    public void AnOverLongPageLabelIsCutToTheSameLengthTheServerCutsIt()
    {
        string label = StudioSheetCommentRules.CleanPageLabel(new string('x', 400));

        Assert.Equal(StudioSheetCommentRules.MaximumPageLabelLength, label.Length);
    }

    [Fact]
    public void AnOverLongMarkIsThinnedRatherThanCutOff()
    {
        // Cutting at the limit would keep the start of a cloud and lose its
        // return, so it would come back as an arc across the sheet. Thinning
        // keeps the whole mark at lower density - and keeps both ends, which is
        // what makes a closed shape still look closed.
        var drawn = Enumerable.Range(0, 900).ToList();

        IReadOnlyList<int> kept = StudioSheetCommentRules.Thin(drawn);

        Assert.Equal(StudioSheetCommentRules.MaximumShapePoints, kept.Count);
        Assert.Equal(0, kept[0]);
        Assert.Equal(899, kept[^1]);
    }

    [Fact]
    public void ThinningAnAlreadyShortMarkChangesNothing()
    {
        // What keeps Studio's thinning and the server's from compounding: the
        // server thins what it receives, and what it receives is already at or
        // under the limit, so its pass is a no-op.
        var drawn = Enumerable.Range(0, StudioSheetCommentRules.MaximumShapePoints).ToList();

        Assert.Equal(drawn, StudioSheetCommentRules.Thin(drawn));
    }

    [Fact]
    public void TheContractStillPublishesNoneOfThisAndTheCopyIsStillNeeded()
    {
        // Not a check that the contract is correct - a check on why the copy
        // above exists. The moment cloud-era-v1 starts declaring these values,
        // this fails and says to read them from the generated client instead.
        //
        // Without it the copy simply stays forever: nothing else in a build
        // would ever mention that it had become redundant.
        JsonElement schemas = ContractSchemas();

        foreach (string schemaName in new[]
                 {
                     "CloudEraSheetCommentDto",
                     "CloudEraSheetCommentCreateRequest",
                 })
        {
            Assert.True(
                schemas.TryGetProperty(schemaName, out JsonElement schema),
                $"'{schemaName}' is gone from cloud-era-v1.openapi.json. Comment rules are "
                + "copied from the server by hand because that contract publishes no values "
                + "for them - check whether the replacement schema does.");

            if (!schema.TryGetProperty("properties", out JsonElement properties))
                continue;

            foreach (string field in new[] { "kind", "status", "shape" })
            {
                if (!properties.TryGetProperty(field, out JsonElement property))
                    continue;

                Assert.False(
                    property.TryGetProperty("enum", out _),
                    $"cloud-era-v1 now declares the allowed values for '{field}' on "
                    + $"{schemaName}. StudioSheetCommentRules keeps a hand-written copy of "
                    + "them only because the contract did not - read them from the generated "
                    + "client and delete the copy.");
            }

            if (properties.TryGetProperty("body", out JsonElement body))
            {
                Assert.False(
                    body.TryGetProperty("maxLength", out _),
                    "cloud-era-v1 now declares the body length limit. "
                    + "StudioSheetCommentRules.MaximumBodyLength is a hand-written copy of "
                    + "it - take the generated one instead.");
            }
        }
    }

    private static JsonElement ContractSchemas()
    {
        string path = Path.Combine(
            TestRepository.FindRoot(),
            "src",
            "contracts",
            "cloud-era-v1.openapi.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement
            .GetProperty("components")
            .GetProperty("schemas")
            .Clone();
    }
}
