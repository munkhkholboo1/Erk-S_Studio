using System.Security.Cryptography;
using ErkS.Platform.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf.IO;

namespace ErkS.Platform.Pdf;

public sealed record ProjectDocumentAssetInspection(
    string ContentType,
    int PageCount,
    long SizeBytes,
    string Sha256,
    int PixelWidth = 0,
    int PixelHeight = 0);

/// <summary>Validates a document before Studio copies it into an owned store.</summary>
public static class ProjectDocumentAssetInspector
{
    public const long MaxDocumentBytes = 100L * 1024L * 1024L;

    public static ProjectDocumentAssetInspection Inspect(string path)
    {
        string fullPath = Path.GetFullPath(path);
        var file = new FileInfo(fullPath);
        if (!file.Exists)
            throw new FileNotFoundException("Document file was not found.", fullPath);
        if (file.Length == 0)
            throw new InvalidDataException("The selected document is empty.");
        if (file.Length > MaxDocumentBytes)
            throw new InvalidDataException("Each document must be 100 MB or smaller.");

        string contentType;
        int pageCount;
        int pixelWidth = 0;
        int pixelHeight = 0;
        string extension = file.Extension.ToLowerInvariant();
        switch (extension)
        {
            case ".pdf":
                EnsureHeader(fullPath, "%PDF-"u8);
                using (var document = PdfReader.Open(fullPath, PdfDocumentOpenMode.Import))
                    pageCount = Math.Max(1, document.PageCount);
                contentType = "application/pdf";
                break;
            case ".png":
                EnsureHeader(fullPath, new byte[] { 0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a });
                using (XImage image = XImage.FromFile(fullPath))
                {
                    pixelWidth = image.PixelWidth;
                    pixelHeight = image.PixelHeight;
                }
                pageCount = 1;
                contentType = "image/png";
                break;
            case ".jpg":
            case ".jpeg":
                EnsureJpegHeader(fullPath);
                using (XImage image = XImage.FromFile(fullPath))
                {
                    pixelWidth = image.PixelWidth;
                    pixelHeight = image.PixelHeight;
                }
                pageCount = 1;
                contentType = "image/jpeg";
                break;
            default:
                throw new InvalidDataException("Only PDF, PNG and JPEG documents are supported.");
        }

        using FileStream hashStream = File.OpenRead(fullPath);
        string sha256 = Convert.ToHexString(SHA256.HashData(hashStream)).ToLowerInvariant();
        return new ProjectDocumentAssetInspection(
            contentType,
            pageCount,
            file.Length,
            sha256,
            pixelWidth,
            pixelHeight);
    }

    /// <summary>
    /// Re-inspects a project-owned copy and repairs metadata written by older
    /// Studio versions. The file itself is never changed.
    /// </summary>
    public static bool RefreshMetadata(ProjectFileReference document, string path)
    {
        ArgumentNullException.ThrowIfNull(document);
        ProjectDocumentAssetInspection inspection = Inspect(path);
        bool changed =
            !string.Equals(document.ContentType, inspection.ContentType, StringComparison.OrdinalIgnoreCase) ||
            document.PageCount != inspection.PageCount ||
            document.SizeBytes != inspection.SizeBytes ||
            !string.Equals(document.Sha256, inspection.Sha256, StringComparison.OrdinalIgnoreCase);

        document.ContentType = inspection.ContentType;
        document.PageCount = inspection.PageCount;
        document.SizeBytes = inspection.SizeBytes;
        document.Sha256 = inspection.Sha256;
        return changed;
    }

    private static void EnsureHeader(string path, ReadOnlySpan<byte> expected)
    {
        Span<byte> header = stackalloc byte[expected.Length];
        using FileStream stream = File.OpenRead(path);
        if (stream.Read(header) != header.Length || !header.SequenceEqual(expected))
            throw new InvalidDataException("The selected file content does not match its extension.");
    }

    private static void EnsureJpegHeader(string path)
    {
        Span<byte> header = stackalloc byte[3];
        using FileStream stream = File.OpenRead(path);
        if (stream.Read(header) != header.Length ||
            header[0] != 0xff || header[1] != 0xd8 || header[2] != 0xff)
            throw new InvalidDataException("The selected file content does not match its extension.");
    }
}
