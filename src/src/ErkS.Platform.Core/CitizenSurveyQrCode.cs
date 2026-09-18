using QRCoder;

namespace ErkS.Platform.Core;

/// <summary>The QR image for a survey, and why there is none when there is none.</summary>
public sealed record CitizenSurveyQrImage(byte[] Png, string Refusal)
{
    public bool IsDrawn => Png.Length > 0;
}

/// <summary>
/// Turns a checked public link into the square that goes on paper.
///
/// 🔴 IT REFUSES BEFORE IT DRAWS. A QR is the one artefact in this feature that cannot
/// be corrected after the fact - printed, posted, handed out - so the link goes through
/// <see cref="CitizenSurveyPublicLink"/> first and a survey without a usable one gets a
/// reason, never a square. Drawing an unusable link would produce something that scans
/// perfectly and leads nowhere, which is strictly worse than no QR at all: it looks like
/// the work is done.
///
/// ⚠ ERROR CORRECTION Q, NOT L. These are printed on notice boards, photographed at an
/// angle, rained on and pinned over. Q recovers about a quarter of a damaged symbol and
/// costs only a slightly denser grid at this payload size - a URL is short. L would fit
/// more data nobody is sending.
///
/// ⚠ PngByteQRCode, NOT the System.Drawing renderer, because Core is a plain net9.0
/// library that must not acquire a Windows-only drawing dependency for one image.
/// </summary>
public static class CitizenSurveyQrCode
{
    /// <summary>Pixels per module. 10 keeps a printed square readable at album size.</summary>
    public const int PixelsPerModule = 10;

    public static CitizenSurveyQrImage For(string? code, string? formUrl)
    {
        CitizenSurveyLinkCheck link = CitizenSurveyPublicLink.Check(code, formUrl);
        if (!link.IsUsable)
            return new CitizenSurveyQrImage([], link.Refusal);

        using var generator = new QRCodeGenerator();
        using QRCodeData data = generator.CreateQrCode(
            (formUrl ?? "").Trim(), QRCodeGenerator.ECCLevel.Q);
        var png = new PngByteQRCode(data);
        return new CitizenSurveyQrImage(png.GetGraphic(PixelsPerModule), "");
    }
}
