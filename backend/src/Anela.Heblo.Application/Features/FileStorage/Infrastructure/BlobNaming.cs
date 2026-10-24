namespace Anela.Heblo.Application.Features.FileStorage.Infrastructure;

internal static class BlobNaming
{
    public static string ResolveBlobName(string? requestedName, string fileUrl, string? responseMediaType)
    {
        if (!string.IsNullOrEmpty(requestedName))
        {
            return requestedName;
        }

        var blobName = Path.GetFileName(new Uri(fileUrl).LocalPath);
        if (string.IsNullOrEmpty(blobName))
        {
            blobName = $"downloaded-file-{Guid.NewGuid()}{GetExtensionFromContentType(responseMediaType)}";
        }

        return blobName;
    }

    public static string GetExtensionFromContentType(string? mediaType)
    {
        return mediaType switch
        {
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            "image/gif" => ".gif",
            "image/webp" => ".webp",
            "application/pdf" => ".pdf",
            "text/plain" => ".txt",
            "application/json" => ".json",
            "application/xml" => ".xml",
            _ => ".bin"
        };
    }

    public static string GetContentTypeFromExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".pdf" => "application/pdf",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            _ => "application/octet-stream"
        };
    }
}
