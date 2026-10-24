namespace Anela.Heblo.Application.Features.Smartsupp.UseCases.SendMessage;

public static class SmartsuppAttachmentPolicy
{
    public const int MaxFiles = 5;
    public const long MaxFileBytes = 10 * 1024 * 1024;

    private const string GenericBinaryType = "application/octet-stream";

    private static readonly IReadOnlyDictionary<string, string> FileTypesByExtension =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".png"] = "image/png",
            [".gif"] = "image/gif",
            [".webp"] = "image/webp",
            [".pdf"] = "application/pdf",
            [".doc"] = "application/msword",
            [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".xls"] = "application/vnd.ms-excel",
            [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".txt"] = "text/plain",
        };

    /// <summary>
    /// Resolves the file type sent to Smartsupp from the extension. Returns false when the extension
    /// is not allowed or a specific declared type contradicts it.
    /// </summary>
    public static bool TryResolveFileType(string fileName, string? declaredType, out string fileType)
    {
        fileType = string.Empty;

        var extension = Path.GetExtension(fileName ?? string.Empty);
        if (!FileTypesByExtension.TryGetValue(extension, out var mappedType))
            return false;

        var isGenericDeclaredType = string.IsNullOrEmpty(declaredType)
            || string.Equals(declaredType, GenericBinaryType, StringComparison.OrdinalIgnoreCase);
        if (!isGenericDeclaredType && !string.Equals(declaredType, mappedType, StringComparison.OrdinalIgnoreCase))
            return false;

        fileType = mappedType;
        return true;
    }
}
