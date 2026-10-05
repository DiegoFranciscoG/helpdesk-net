using System.Text;

namespace Helpdesk.Domain.Attachments;

/// <summary>Resultado de inspeccionar un archivo subido.</summary>
public sealed record AttachmentInspection(bool IsValid, string? ErrorCode, string? ErrorMessage, string SafeFileName, string? ContentType)
{
    public static AttachmentInspection Rejected(string code, string message, string safeFileName) => new(false, code, message, safeFileName, null);
}

/// <summary>
/// Reglas de adjuntos (R10, OWASP File Upload Cheat Sheet): lista blanca de extensiones, firma binaria
/// coherente con la extensión, tamaño máximo y nombre saneado. El tipo MIME guardado es el detectado,
/// nunca el que declara el navegador.
/// </summary>
public static class AttachmentRules
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int MaxPerTicket = 10;
    public const int MaxFileNameLength = 255;

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".pdf"] = "application/pdf",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".txt"] = "text/plain; charset=utf-8",
    };

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static IReadOnlyCollection<string> AllowedExtensions => ContentTypes.Keys;

    public static AttachmentInspection Inspect(string? originalFileName, ReadOnlySpan<byte> content)
    {
        var safeName = SanitizeFileName(originalFileName);

        if (content.IsEmpty)
        {
            return AttachmentInspection.Rejected("attachment_empty", "El archivo está vacío.", safeName);
        }

        if (content.Length > MaxBytes)
        {
            return AttachmentInspection.Rejected("attachment_too_large", $"El archivo supera el máximo de {MaxBytes / (1024 * 1024)} MB.", safeName);
        }

        var extension = Path.GetExtension(safeName);
        if (!ContentTypes.TryGetValue(extension, out var contentType))
        {
            return AttachmentInspection.Rejected(
                "attachment_type_not_allowed",
                $"Tipo de archivo no permitido. Se aceptan: {string.Join(", ", AllowedExtensions)}.",
                safeName);
        }

        var signatureMatches = extension.ToUpperInvariant() switch
        {
            ".PDF" => content.StartsWith(PdfSignature),
            ".PNG" => content.StartsWith(PngSignature),
            ".JPG" or ".JPEG" => content.StartsWith(JpegSignature),
            ".TXT" => IsPlainUtf8Text(content),
            _ => false,
        };

        return signatureMatches
            ? new AttachmentInspection(true, null, null, safeName, contentType)
            : AttachmentInspection.Rejected(
                "attachment_signature_mismatch",
                "El contenido del archivo no corresponde a su extensión.",
                safeName);
    }

    /// <summary>
    /// Deja solo el nombre (sin rutas), quita caracteres de control y reservados, recorta puntos y espacios
    /// y limita la longitud conservando la extensión. Nunca se usa como nombre en disco.
    /// </summary>
    public static string SanitizeFileName(string? fileName)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..];

        var builder = new StringBuilder(name.Length);
        foreach (var c in name.Normalize(NormalizationForm.FormC))
        {
            builder.Append(char.IsControl(c) || "<>:\"|?*".Contains(c, StringComparison.Ordinal) ? '_' : c);
        }

        var cleaned = builder.ToString().Trim().Trim('.').Trim();
        if (cleaned.Length == 0)
        {
            cleaned = "archivo";
        }

        if (cleaned.Length > MaxFileNameLength)
        {
            var extension = Path.GetExtension(cleaned);
            extension = extension.Length > 16 ? string.Empty : extension;
            cleaned = cleaned[..(MaxFileNameLength - extension.Length)] + extension;
        }

        return cleaned;
    }

    private static bool IsPlainUtf8Text(ReadOnlySpan<byte> content)
    {
        if (content.Contains((byte)0))
        {
            return false;
        }

        try
        {
            _ = StrictUtf8.GetCharCount(content);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
