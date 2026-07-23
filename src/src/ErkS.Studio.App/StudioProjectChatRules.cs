using System.IO;

namespace ErkS.Studio;

internal static class StudioProjectChatRules
{
    public const int MaxBodyLength = 2000;
    public const long MaxAttachmentBytes = 15L * 1024L * 1024L;
    public const int AttachmentLifetimeHours = 24;

    public const string AttachmentDialogFilter =
        "Чатын файл|*.png;*.jpg;*.jpeg;*.webp;*.gif;*.pdf;*.txt;*.csv;*.zip;*.7z;*.rar;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.dwg;*.dxf;*.rvt;*.rfa;*.ifc|" +
        "Бүх файл|*.*";

    public static IReadOnlyList<string> Reactions { get; } =
    [
        "🔥",
        ":erks:",
        "👍",
        "✅",
        "🙌",
        "👏",
        "😂",
        "😊",
        "😎",
        "❤️",
        "😍",
        "🤔",
        "👌",
        "👀",
        "🎉",
        "💯",
        "🤝",
        "🙏",
        "✨",
        "💪",
        "🚀",
    ];

    public static IReadOnlySet<string> AllowedExtensions { get; } = new HashSet<string>(
        [
            ".jpg", ".jpeg", ".png", ".webp", ".gif",
            ".pdf", ".txt", ".csv", ".zip", ".7z", ".rar",
            ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx",
            ".dwg", ".dxf", ".rvt", ".rfa", ".ifc",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static StudioProjectChatAttachmentValidation ValidateAttachment(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return new StudioProjectChatAttachmentValidation(false, "Файл олдсонгүй.", "");

        string extension = Path.GetExtension(path);
        if (!AllowedExtensions.Contains(extension))
            return new StudioProjectChatAttachmentValidation(false, "Энэ төрлийн файл чатад оруулах боломжгүй байна.", "");

        long length;
        try
        {
            length = new FileInfo(path).Length;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return new StudioProjectChatAttachmentValidation(false, "Файлыг уншиж чадсангүй.", "");
        }
        if (length <= 0)
            return new StudioProjectChatAttachmentValidation(false, "Хоосон файл илгээх боломжгүй.", "");
        if (length > MaxAttachmentBytes)
            return new StudioProjectChatAttachmentValidation(false, "Файл 15 MB-аас их байна.", "");

        return new StudioProjectChatAttachmentValidation(true, "", ContentType(extension));
    }

    public static string CleanBody(string? value)
    {
        string text = (value ?? "")
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        while (text.Contains("\n\n\n", StringComparison.Ordinal))
            text = text.Replace("\n\n\n", "\n\n", StringComparison.Ordinal);
        return text.Length <= MaxBodyLength ? text : text[..MaxBodyLength];
    }

    private static string ContentType(string extension) => extension.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        ".txt" => "text/plain",
        ".csv" => "text/csv",
        ".zip" => "application/zip",
        ".7z" => "application/x-7z-compressed",
        ".rar" => "application/vnd.rar",
        ".doc" => "application/msword",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xls" => "application/vnd.ms-excel",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".ppt" => "application/vnd.ms-powerpoint",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".dwg" => "application/acad",
        ".dxf" => "application/dxf",
        ".ifc" => "application/x-step",
        _ => "application/octet-stream",
    };
}

internal readonly record struct StudioProjectChatAttachmentValidation(
    bool IsValid,
    string Message,
    string ContentType);
