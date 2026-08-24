namespace ErkS.Studio;

using System;

/// <summary>
/// One binary the server holds for an organisation, as fetched.
/// </summary>
public sealed record StudioDownloadedDocument(byte[] Bytes, string ContentType);

/// <summary>
/// The document formats an album page can actually draw.
///
/// The client's certificates were uploaded as images, and the album draws
/// images perfectly well - the renderer branches on the extension and hands
/// anything that is not a PDF to the image loader. So the list is not "PDF
/// only"; it is what the renderer can open.
///
/// The extension is written from the content type rather than taken from the
/// original file name, because the renderer decides how to open a file by
/// looking at its extension. A certificate saved as .pdf that is really a PNG
/// would draw nothing and say "Файл олдсонгүй", which is both wrong and
/// unhelpful.
/// </summary>
public static class StudioOrganizationDocumentFormats
{
    public const string Pdf = "application/pdf";
    public const string Png = "image/png";
    public const string Jpeg = "image/jpeg";

    public static bool CanDraw(string? contentType) =>
        Normalize(contentType) is Pdf or Png or Jpeg;

    /// <summary>
    /// The file extension to save this content under, or empty when the album
    /// cannot draw it.
    /// </summary>
    public static string Extension(string? contentType) =>
        Normalize(contentType) switch
        {
            Pdf => ".pdf",
            Png => ".png",
            Jpeg => ".jpg",
            _ => "",
        };

    private static string Normalize(string? contentType) =>
        (contentType ?? "").Trim().ToLowerInvariant();
}
