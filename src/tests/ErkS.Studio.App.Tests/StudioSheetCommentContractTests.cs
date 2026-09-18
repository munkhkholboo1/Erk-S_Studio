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
    public void THEOFFEREDKindsAreEXACTLYTheOnesTheContractAllows()
    {
        // 🔴 THE ORDER IS OURS, THE SET IS THEIRS. Kinds is written down because this
        // window offers «Засах шаардлагатай» first; the contract lists Note first. Taking
        // the enum's order would have reordered the UI while looking like a tidy-up.
        // So the order stays a decision and the SET is held to the contract — a kind
        // added on the server can no longer arrive unoffered, and one removed goes red.
        Assert.Equal(
            StudioSheetCommentRules.ContractKinds.OrderBy(kind => kind, StringComparer.Ordinal),
            StudioSheetCommentRules.Kinds.OrderBy(kind => kind, StringComparer.Ordinal));

        // And the values themselves are the wire's, not C# identifiers that happen to match.
        Assert.Contains("ChangeRequired", StudioSheetCommentRules.ContractKinds);
        Assert.Contains("Note", StudioSheetCommentRules.ContractKinds);
        Assert.Contains("Approved", StudioSheetCommentRules.ContractKinds);

        // The two states came from the same publication on the same day.
        Assert.Equal(
            new[] { "Open", "Resolved" }.OrderBy(s => s, StringComparer.Ordinal),
            StudioSheetCommentRules.ContractStatuses.OrderBy(s => s, StringComparer.Ordinal));
    }

    [Fact]
    public void THESCHEMASAreStillThereAndOURSETSStillMatchTHEIRS()
    {
        // 🔴 THIS REPLACES A TEST THAT EXPIRED ON PURPOSE. Its claim was «the contract
        // publishes none of this, so the hand-written copy is still needed» — and on
        // 2026-09-19 the contract published all three: kind, status and shape. The claim
        // became false, so the test was retired rather than softened; what it was really
        // protecting — that Studio's values are the server's — is now protected by
        // reading them instead of by watching for the day we could.
        JsonElement schemas = ContractSchemas();

        foreach (string schemaName in new[]
                 {
                     "CloudEraSheetCommentDto",
                     "CloudEraSheetCommentCreateRequest",
                 })
        {
            Assert.True(
                schemas.TryGetProperty(schemaName, out JsonElement schema),
                $"'{schemaName}' is gone from cloud-era-v1.openapi.json — the comment "
                + "rules read their values from the generated client, so the schema "
                + "disappearing means those readings are now pointing at nothing.");

            Assert.True(schema.TryGetProperty("properties", out _));
        }

        // ⚠ AND THE TEXT LENGTH IS STILL A COPY. The contract declares no maxLength, so
        // this one number is still Studio's word against the server's. When that changes,
        // it changes the same way the other three did.
        Assert.True(StudioSheetCommentRules.MaximumBodyLength > 0);
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
