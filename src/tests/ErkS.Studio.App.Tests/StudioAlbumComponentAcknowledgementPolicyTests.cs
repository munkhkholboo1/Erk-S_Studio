namespace ErkS.Studio.App.Tests;

public sealed class StudioAlbumComponentAcknowledgementPolicyTests
{
    [Fact]
    public void UnrenderedLiveSubCoverIsDeferredWhileOtherMissingComponentsCanProceed()
    {
        const string subCoverCode =
            "generated:building-sub-cover:studio-building:school";
        const string removedDocumentCode =
            "generated:project-document:approved-atd";

        StudioMissingAlbumComponentResolution resolution =
            StudioAlbumComponentAcknowledgementPolicy.ResolveMissingComponents(
                [subCoverCode, removedDocumentCode],
                code => code.Equals(
                    subCoverCode,
                    StringComparison.OrdinalIgnoreCase));

        Assert.Equal([subCoverCode], resolution.DeferredCodes);
        Assert.Equal([removedDocumentCode], resolution.RemovalCodes);
    }

    [Fact]
    public void DeferredSubCoverDoesNotAcknowledgeItsPendingClaim()
    {
        const string subCoverCode =
            "generated:building-sub-cover:studio-building:school";
        const string atdCode = "generated:approved-atd";
        var pending = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["school-cover"] = subCoverCode,
            ["atd"] = atdCode,
        };

        IReadOnlyList<string> confirmed =
            StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
                pending,
                [
                    Component(subCoverCode, "Generated"),
                    Component(atdCode, "Generated"),
                ],
                [new StudioAlbumComponentUpload(
                    atdCode,
                    "Approved ATD",
                    200,
                    "atd.pdf")],
                [subCoverCode]);

        Assert.Equal(["atd"], confirmed);
        Assert.DoesNotContain("school-cover", confirmed);
    }

    [Fact]
    public void ARemovalOfAComponentTheAlbumNoLongerHasIsDONE()
    {
        // 🔴 ERIN'S TWO REMOVALS OF 2026-08-20. Their components were already
        // gone from the canonical album, so the merge had nothing to remove,
        // submitted no removal - and acknowledgement demanded a submitted
        // removal. Every sync afterwards left them pending; R44 went up and
        // they stayed. Absent is the state a removal asks for.
        const string removed = "source:424d27cfbcb1bc9a:d0dbfe6831db46a49680471965dd7ace";
        var pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["removed-source"] = removed,
        };

        IReadOnlyList<string> confirmed =
            StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
                pending,
                [Component("generated:site-context:SiteContext", "Generated")],
                [],
                removalCodes: [removed]);

        Assert.Equal(["removed-source"], confirmed);
    }

    [Fact]
    public void TheMergePassesItsRemovalsToTheAcknowledgement()
    {
        string source = File.ReadAllText(Source("ShellView.AlbumComponents.cs"));
        string call = string.Concat(source.Where(c => !char.IsWhiteSpace(c)));

        Assert.Contains(
            "missingResolution.DeferredCodes,missingResolution.RemovalCodes,",
            call,
            StringComparison.Ordinal);
    }

    private static string Source(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, "src", "src", "ErkS.Studio.App", fileName);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        Assert.Fail(fileName + " was not found; this test reads it from source");
        return "";
    }

    [Fact]
    public void ARemovalStillPRESENTInTheAlbumIsNotDone()
    {
        const string removed = "source:424d27cfbcb1bc9a:d0dbfe6831db46a49680471965dd7ace";
        var pending = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["removed-source"] = removed,
        };

        IReadOnlyList<string> confirmed =
            StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
                pending,
                [Component(removed, "Source")],
                [],
                removalCodes: [removed]);

        Assert.Empty(confirmed);
    }

    [Fact]
    public void MissingRequestedSubCoverRemainsPending()
    {
        var pending = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase)
        {
            ["school-cover"] =
                "generated:building-sub-cover:studio-building:school",
        };

        IReadOnlyList<string> confirmed =
            StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
                pending,
                [],
                []);

        Assert.Empty(confirmed);
    }

    [Fact]
    public void VerifiedSubCoverIsAcknowledged()
    {
        const string code =
            "generated:building-sub-cover:studio-building:school";
        var pending = new Dictionary<string, string>
        {
            ["raw-school-cover"] = code,
        };

        IReadOnlyList<string> confirmed =
            StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
                pending,
                [Component(code, "Generated")],
                [new StudioAlbumComponentUpload(
                    code,
                    "Сургууль",
                    300,
                    "school-cover.pdf")]);

        Assert.Equal(["raw-school-cover"], confirmed);
    }

    [Fact]
    public void SubmittedRemovalIsAcknowledgedOnlyAfterComponentIsAbsent()
    {
        const string code =
            "generated:building-sub-cover:studio-building:deleted";
        var pending = new Dictionary<string, string>
        {
            [code] = code,
        };
        var removal = new StudioAlbumComponentUpload(
            code,
            "Deleted",
            300,
            "",
            Remove: true);

        Assert.Equal(
            [code],
            StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
                pending,
                [],
                [removal]));
        Assert.Empty(
            StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
                pending,
                [Component(code, "Generated")],
                [removal]));
    }

    [Fact]
    public void OwnedBaseRemovalIsAcknowledgedWhenEverySubmittedSliceIsAbsent()
    {
        const string owner = "architect@erks.local";
        const string sourceKey = "school-source";
        string baseCode =
            StudioAlbumComponentIdentity.SourceCode(owner, sourceKey);
        string floorPlans = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "studio-building:school",
            "floor-plans");
        string sections = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "studio-building:school",
            "sections");
        var pending = new Dictionary<string, string>
        {
            ["source-removal"] = baseCode,
        };

        IReadOnlyList<string> confirmed =
            StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
                pending,
                [],
                [
                    Removal(floorPlans),
                    Removal(sections),
                ]);

        Assert.Equal(["source-removal"], confirmed);
    }

    [Fact]
    public void OwnedBaseRemovalStaysPendingWhileAnySiblingSliceRemains()
    {
        const string owner = "architect@erks.local";
        const string sourceKey = "school-source";
        string baseCode =
            StudioAlbumComponentIdentity.SourceCode(owner, sourceKey);
        string floorPlans = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "studio-building:school",
            "floor-plans");
        string sections = StudioAlbumComponentIdentity.SourceSliceCode(
            owner,
            sourceKey,
            "studio-building:school",
            "sections");
        var pending = new Dictionary<string, string>
        {
            ["source-removal"] = baseCode,
        };

        IReadOnlyList<string> confirmed =
            StudioAlbumComponentAcknowledgementPolicy.ConfirmedPendingCodes(
                pending,
                [Component(sections, "Source")],
                [Removal(floorPlans)]);

        Assert.Empty(confirmed);
    }

    private static StudioAlbumComponentUpload Removal(string code) =>
        new(
            code,
            "Removed source slice",
            0,
            "",
            Remove: true);

    private static StudioCloudAlbumSection Component(
        string code,
        string kind) => new()
    {
        Code = code,
        Label = code,
        ComponentKind = kind,
        PageNumbers = [1],
        Status = "Available",
    };
}
