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
