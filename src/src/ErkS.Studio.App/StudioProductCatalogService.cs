using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace ErkS.Studio;

/// <summary>One Platform program as the site describes it.</summary>
internal sealed class StudioCatalogProduct
{
    public string ProductCode { get; set; } = "";
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kicker { get; set; } = "";
    public string Headline { get; set; } = "";
    public string Summary { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string ProductUrl { get; set; } = "";
    public string AvailabilityLabel { get; set; } = "";
    public bool IsFree { get; set; }
    public bool IsRoadmapOnly { get; set; }
    public bool SubscriptionAvailable { get; set; }
    public string[] Highlights { get; set; } = [];
}

internal sealed class StudioCatalogResponse
{
    public string SiteUrl { get; set; } = "";
    public string HeroImageUrl { get; set; } = "";
    public string PartnerImageUrl { get; set; } = "";
    public string PartnerUrl { get; set; } = "";
    public List<StudioCatalogProduct> Products { get; set; } = [];
}

/// <summary>What the site currently offers for one program.</summary>
internal sealed record StudioCatalogRelease(
    StudioCatalogProduct Product,
    string Version,
    string DownloadUrl)
{
    public bool IsAvailable => DownloadUrl.Length > 0;

    public string VersionLabel => Version.Length > 0
        ? "Хувилбар " + Version
        : Product.IsRoadmapOnly ? "Удахгүй нээгдэнэ" : "Хувилбар зарлагдаагүй";
}

/// <summary>The whole home page's worth of material, read from the site.</summary>
internal sealed record StudioCatalogSnapshot(
    string SiteUrl,
    string HeroImageUrl,
    string PartnerImageUrl,
    string PartnerUrl,
    IReadOnlyList<StudioCatalogRelease> Releases,
    bool CameFromSite)
{
    /// <summary>The program the Studio itself is, when the site names it.</summary>
    public StudioCatalogRelease? Featured => Releases.FirstOrDefault(item =>
        item.Product.ProductCode.Equals("ErkS.Studio", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// The programs of the Platform, read from the site that publishes them, so
/// the Studio's home page shows what the Platform actually is today rather
/// than what it was on the day this build was made. The built-in list is only
/// the floor the page stands on when the site cannot be reached.
/// </summary>
internal sealed class StudioProductCatalogService : IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    public string SiteUrl =>
        (Environment.GetEnvironmentVariable("ERKS_STUDIO_UPDATE_SERVER_URL")
            ?? StudioReleaseInfo.DefaultServerUrl).Trim().TrimEnd('/');

    /// <summary>
    /// The programs written into this build. Used when the site is unreachable,
    /// and as the order the page reads in either way.
    /// </summary>
    private IReadOnlyList<StudioCatalogProduct> BuiltIn =>
    [
        new()
        {
            ProductCode = "ErkS.Studio",
            Slug = "studio",
            Name = "Erk-S Studio",
            Kicker = "Cloud ERA document studio",
            Summary = "Зураг төслийн эх үүсвэрүүдийг нэг төсөлд зангидаж, стандарт PDF альбум болгоно.",
            ImageUrl = SiteUrl + "/assets/product-studio-v2.png",
            ProductUrl = SiteUrl + "/products/studio",
            AvailabilityLabel = "Demo",
            IsFree = true,
        },
        new()
        {
            ProductCode = "ErkS.Platform.AutoCAD",
            Slug = "platform-autocad",
            Name = "Erk-S Platform for AutoCAD",
            Kicker = "AutoCAD to Studio workflow",
            Summary = "AutoCAD layout-уудыг стандарт хуудас болгон Studio PDF альбумтай холбоно.",
            ImageUrl = SiteUrl + "/assets/product-platform-autocad.png",
            ProductUrl = SiteUrl + "/products/platform-autocad",
            AvailabilityLabel = "Available now",
        },
        new()
        {
            ProductCode = "ErkS.CityGen.AutoCAD",
            Slug = "citygen-autocad",
            Name = "Erk-S CityGen for AutoCAD",
            Kicker = "Urban drafting automation",
            Summary = "RoadGen, ZoneGen, LandGen, UrbanGen — хот төлөвлөлтийн давтагддаг ажлыг AutoCAD дотор.",
            ImageUrl = SiteUrl + "/assets/product-citygen-autocad.png",
            ProductUrl = SiteUrl + "/products/citygen-autocad",
            AvailabilityLabel = "Available now",
        },
        new()
        {
            ProductCode = "ErkS.Platform.Revit",
            Slug = "platform-revit",
            Name = "Erk-S Platform for Revit",
            Kicker = "Revit automation",
            Summary = "Revit доторх өдөр тутмын зураг төслийн ажлыг нэг урсгалд оруулна.",
            ImageUrl = SiteUrl + "/assets/product-platform-revit.png",
            ProductUrl = SiteUrl + "/products/platform-revit",
            AvailabilityLabel = "Available now",
        },
        new()
        {
            ProductCode = "ErkS.CityGen.3dsMax",
            Slug = "citygen-3dsmax",
            Name = "Erk-S CityGen for 3ds Max",
            Kicker = "Visualization roadmap",
            Summary = "Хотын massing, визуал, presentation workflow-ийн дараагийн бүтээгдэхүүн.",
            ImageUrl = SiteUrl + "/assets/product-citygen-3dsmax.png",
            ProductUrl = SiteUrl + "/products/citygen-3dsmax",
            AvailabilityLabel = "Coming soon",
            IsRoadmapOnly = true,
        },
    ];

    public async Task<StudioCatalogSnapshot> ReadAsync(CancellationToken cancellationToken = default)
    {
        StudioCatalogResponse? site = await ReadCatalogAsync(cancellationToken).ConfigureAwait(true);
        IReadOnlyList<StudioCatalogProduct> products =
            site is not null && site.Products.Count > 0 ? site.Products : BuiltIn;

        StudioCatalogRelease[] releases = await Task.WhenAll(
            products.Select(product => ReadReleaseAsync(product, cancellationToken))).ConfigureAwait(true);

        return new StudioCatalogSnapshot(
            string.IsNullOrWhiteSpace(site?.SiteUrl) ? SiteUrl : site!.SiteUrl,
            string.IsNullOrWhiteSpace(site?.HeroImageUrl)
                ? SiteUrl + "/assets/hero-product.png"
                : site!.HeroImageUrl,
            string.IsNullOrWhiteSpace(site?.PartnerImageUrl)
                ? SiteUrl + "/assets/partner-rights-banner.png"
                : site!.PartnerImageUrl,
            string.IsNullOrWhiteSpace(site?.PartnerUrl) ? SiteUrl + "/partner" : site!.PartnerUrl,
            releases,
            site is not null);
    }

    /// <summary>
    /// The site's own catalogue. A server that predates this endpoint answers
    /// 404, and the built-in list stands in — an older server must not empty
    /// the home page.
    /// </summary>
    private async Task<StudioCatalogResponse?> ReadCatalogAsync(CancellationToken cancellationToken)
    {
        try
        {
            using HttpResponseMessage response = await httpClient
                .GetAsync(new Uri(SiteUrl + "/api/products/catalog"), cancellationToken)
                .ConfigureAwait(true);
            if (!response.IsSuccessStatusCode)
                return null;

            await using Stream body = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(true);
            return await JsonSerializer
                .DeserializeAsync<StudioCatalogResponse>(body, JsonOptions, cancellationToken)
                .ConfigureAwait(true);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return null;
        }
    }

    private async Task<StudioCatalogRelease> ReadReleaseAsync(
        StudioCatalogProduct product,
        CancellationToken cancellationToken)
    {
        if (product.IsRoadmapOnly)
            return new StudioCatalogRelease(product, "", "");

        try
        {
            var endpoint = new Uri(
                SiteUrl + "/api/installers/latest?productCode=" +
                Uri.EscapeDataString(product.ProductCode));
            using HttpResponseMessage response = await httpClient
                .GetAsync(endpoint, cancellationToken)
                .ConfigureAwait(true);
            response.EnsureSuccessStatusCode();
            await using Stream body = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(true);
            StudioUpdateLatestResponse? latest = await JsonSerializer
                .DeserializeAsync<StudioUpdateLatestResponse>(body, JsonOptions, cancellationToken)
                .ConfigureAwait(true);
            return new StudioCatalogRelease(
                product,
                (latest?.Version ?? "").Trim(),
                AbsoluteUrl(latest?.DownloadUrl ?? ""));
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or JsonException or UriFormatException)
        {
            return new StudioCatalogRelease(product, "", "");
        }
    }

    private string AbsoluteUrl(string value)
    {
        string url = value.Trim();
        if (url.Length == 0)
            return "";

        if (Uri.TryCreate(url, UriKind.Absolute, out Uri? absolute))
            return absolute.Scheme is "https" or "http" ? absolute.ToString() : "";

        return SiteUrl + "/" + url.TrimStart('/');
    }

    public void Dispose() => httpClient.Dispose();
}
