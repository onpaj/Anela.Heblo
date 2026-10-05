## Module
FileStorage

## Finding
`AzureBlobStorageService.DownloadFromUrlAsync()` logs `fileUrl` at full fidelity in two places:

- Line 31: `_logger.LogInformation("Starting download from URL: {FileUrl}", fileUrl);`
- Line 70: `_logger.LogError(ex, "Error downloading from URL {FileUrl} ...", fileUrl, containerName);`

File: `backend/src/Adapters/Anela.Heblo.Adapters.Azure/Features/FileStorage/AzureBlobStorageService.cs`

`DownloadFromUrlHandler` (line 45 of `backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`) explicitly strips query parameters before logging via `RedactUrl()`:

```csharp
var redactedUrl = RedactUrl(request.FileUrl);
```

That method removes the query string, preventing auth tokens (e.g. Shoptet API keys, SAS tokens passed as query parameters) from reaching structured logs or Application Insights. The adapter undermines this intent by writing the unredacted URL to the same log sink on every call, both on success (LogInformation) and on error (LogError).

## Why it matters
Secrets in URLs are a well-known security risk. Application Insights retains structured log data; leaking `token=...` or `api_key=...` query parameters into that store is a GDPR/security concern and defeats the handler's deliberate redaction. The pattern is also inconsistent: the handler redacts, the adapter leaks.

## Suggested fix
Apply the same stripping in the adapter's two log calls. A one-liner using `UriBuilder` with `Query = null` is sufficient, matching the handler's existing `RedactUrl()` static helper. Since `AzureBlobStorageService` already depends on `ILogger`, the fix is minimal:

```csharp
var redactedUrl = TryRedactUrl(fileUrl);
// ...
_logger.LogInformation("Starting download from URL: {FileUrl}", redactedUrl);
```

where `TryRedactUrl` strips the query string (same approach as `DownloadFromUrlHandler.RedactUrl`). A shared helper in `FileStorageConstants` or a static utility class avoids duplication between handler and adapter.

---
_Filed by daily arch-review routine on 2026-09-29._
