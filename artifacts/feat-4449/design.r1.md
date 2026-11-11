# Design: Remove HTTP download from IBlobStorageService

## Component Design

### IBlobStorageService (Domain)
Pure blob-store contract; `DownloadFromUrlAsync` removed. All other members and `BlobItemInfo` unchanged.

### AzureBlobStorageService (Adapter)
- Fields: `_blobServiceClient`, `_logger`, `_containerExists`.
- Constructor: `(BlobServiceClient blobServiceClient, ILogger<AzureBlobStorageService> logger)`.
- Removed: `DownloadFromUrlAsync`, `GetExtensionFromContentType`, `GetContentTypeFromExtension`, `IHttpClientFactory` field.
- Remove `using Anela.Heblo.Domain.Features.FileStorage` only if still unneeded (it is needed for `IBlobStorageService`/`BlobItemInfo`; keep).

### BlobNaming (Application/Features/FileStorage/Infrastructure, new, static)
```csharp
internal static class BlobNaming
{
    // request name -> URL path file name -> "downloaded-file-{guid}{ext}"
    public static string ResolveBlobName(string? requestedName, string fileUrl, string? responseMediaType);
    public static string GetExtensionFromContentType(string? mediaType);   // moved verbatim
    public static string GetContentTypeFromExtension(string fileName);      // moved verbatim
}
```

### DownloadFromUrlHandler (Application)
Per-attempt delegate:
```csharp
async ct =>
{
    Interlocked.Increment(ref attemptCount);
    return await DownloadAndStoreAsync(request, ct);
}

private async Task<string> DownloadAndStoreAsync(DownloadFromUrlRequest r, CancellationToken ct)
{
    var redacted = UrlRedactor.Redact(r.FileUrl);
    try
    {
        _logger.LogInformation("Starting download from URL: {FileUrl}", redacted);
        var client = _httpClientFactory.CreateClient(FileStorageModule.FileDownloadClientName);
        using var response = await client.GetAsync(r.FileUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync(ct);
        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var blobName = BlobNaming.ResolveBlobName(r.BlobName, r.FileUrl, mediaType);
        var contentType = mediaType ?? BlobNaming.GetContentTypeFromExtension(blobName);
        var blobUrl = await _blobStorageService.UploadAsync(stream, r.ContainerName, blobName, contentType, ct);
        _logger.LogInformation("Successfully downloaded from URL and uploaded to blob: {BlobUrl}", blobUrl);
        return blobUrl;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Error downloading from URL {FileUrl} and uploading to container {ContainerName}", redacted, r.ContainerName);
        throw;
    }
}
```
Handle(), HEAD probe, failure mapping unchanged.

### FileStorageModule
`public const string FileDownloadClientName = "FileDownload";` (no Domain reference; `using Anela.Heblo.Domain.Features.FileStorage;` removed only if unused).

## Data Schemas
No database, API or DTO changes. `DownloadFromUrlRequest/Response` unchanged.
