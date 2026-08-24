namespace ErkS.Platform.Core.Tests;

/// <summary>
/// Sending a company's scans up to the server.
///
/// People uploaded certificates into their own Studio before organisations
/// could carry them. The scans exist - on one machine, in one company library,
/// invisible to every colleague on the same project. Sending them up spares
/// whoever did that work from doing it twice.
///
/// The whole difficulty is not sending the same scan twice. The server takes
/// uploads from the website as well and caps each category at five, so a
/// person who uploads by hand and then syncs would otherwise watch their own
/// certificate arrive again and eat a slot.
/// </summary>
public sealed class CompanyDocumentUploadPlanTests
{
    private static ProjectFileReference Local(string hash) => new()
    {
        Sha256 = hash,
        IsAvailable = true,
        RelativePath = $"documents/{hash}.pdf",
        OriginalFileName = $"{hash}.pdf",
        ContentType = "application/pdf",
    };

    [Fact]
    public void AScanTheServerDoesNotHaveIsSent()
    {
        IReadOnlyList<CompanyDocumentUpload> uploads = CompanyDocumentUploadPlan.Decide(
            [Local("aaa")],
            [],
            serverHashes: [],
            canManage: true);

        CompanyDocumentUpload upload = Assert.Single(uploads);
        Assert.Equal(ProjectDocumentCategories.CompanyRegistrationCertificate, upload.Category);
    }

    [Fact]
    public void AScanTheServerAlreadyHasIsNotSentAgain()
    {
        // The website can upload too. Somebody doing it by hand and then
        // syncing must not end up with two.
        Assert.Empty(CompanyDocumentUploadPlan.Decide(
            [Local("aaa")],
            [],
            serverHashes: ["AAA"],
            canManage: true));
    }

    [Fact]
    public void TheSameScanFiledUnderBothCategoriesIsSentOnce()
    {
        // Category is not part of the comparison: the same file filed
        // differently is still the same file, and the second copy would spend
        // one of the five slots for nothing.
        IReadOnlyList<CompanyDocumentUpload> uploads = CompanyDocumentUploadPlan.Decide(
            [Local("aaa")],
            [Local("aaa")],
            serverHashes: [],
            canManage: true);

        Assert.Single(uploads);
    }

    [Fact]
    public void NobodyWithoutWriteAccessSendsAnything()
    {
        // Working on a project that uses a company is no reason to be able to
        // change that company's papers.
        Assert.Empty(CompanyDocumentUploadPlan.Decide(
            [Local("aaa")],
            [Local("bbb")],
            serverHashes: [],
            canManage: false));
    }

    [Fact]
    public void AScanWithNoFingerprintStaysWhereItIs()
    {
        // Without a hash it cannot be compared against what the server holds,
        // so sending it risks a duplicate nobody could detect afterwards.
        Assert.Empty(CompanyDocumentUploadPlan.Decide(
            [Local("")],
            [],
            serverHashes: [],
            canManage: true));
    }

    [Fact]
    public void ADocumentThisDeviceDoesNotActuallyHaveIsNotSent()
    {
        // A cloud placeholder is a record of a file, not the file.
        ProjectFileReference placeholder = Local("aaa");
        placeholder.IsAvailable = false;
        placeholder.IsCloudPlaceholder = true;

        Assert.Empty(CompanyDocumentUploadPlan.Decide(
            [placeholder],
            [],
            serverHashes: [],
            canManage: true));
    }

    [Fact]
    public void TwoDifferentScansBothGo()
    {
        Assert.Equal(2, CompanyDocumentUploadPlan.Decide(
            [Local("aaa")],
            [Local("bbb")],
            serverHashes: [],
            canManage: true).Count);
    }

    [Fact]
    public void EachCategoryKeepsItsOwn()
    {
        IReadOnlyList<CompanyDocumentUpload> uploads = CompanyDocumentUploadPlan.Decide(
            [Local("aaa")],
            [Local("bbb")],
            serverHashes: [],
            canManage: true);

        Assert.Equal(
            ProjectDocumentCategories.CompanyRegistrationCertificate,
            uploads.Single(upload => upload.Document.Sha256 == "aaa").Category);
        Assert.Equal(
            ProjectDocumentCategories.CompanyDesignLicense,
            uploads.Single(upload => upload.Document.Sha256 == "bbb").Category);
    }

    [Fact]
    public void RunningTwiceSendsNothingTheSecondTime()
    {
        // The second run sees its own work on the server.
        IReadOnlyList<CompanyDocumentUpload> first = CompanyDocumentUploadPlan.Decide(
            [Local("aaa")],
            [],
            serverHashes: [],
            canManage: true);

        Assert.Empty(CompanyDocumentUploadPlan.Decide(
            [Local("aaa")],
            [],
            serverHashes: first.Select(upload => upload.Document.Sha256),
            canManage: true));
    }
}
