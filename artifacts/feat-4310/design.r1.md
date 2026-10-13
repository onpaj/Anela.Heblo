# Design: FileStorage DownloadFromUrlResponse nullability fix

## Component Design

### `DownloadFromUrlResponse` (backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlResponse.cs`)
- **Responsibility**: MediatR response DTO returned by `DownloadFromUrlHandler.Handle()` and surfaced verbatim by `FileStorageController.DownloadFromUrl` (`ActionResult<DownloadFromUrlResponse>`).
- **Change**: `BlobUrl`, `BlobName`, `ContainerName` change from non-nullable `string` (with the `= null!` null-forgiving initializer) to nullable `string?` (no initializer needed). `FileSizeBytes` (`long`) is unchanged. Inherited `Success`, `ErrorCode`, `Params` (from `BaseResponse`) are unchanged.
- **Invariant to preserve**: on `Success == true`, all three properties are always non-null (set by `DownloadFromUrlHandler.Handle()`'s success branch, lines 72–79 — untouched by this change). On `Success == false`, all three are `null` (set implicitly by `Failure()`'s object initializer omitting them — also untouched). Consumers must check `Success` before treating these as non-null; this was already true in practice and is now true in the type system as well.

### `DownloadFromUrlHandler` (backend/src/Anela.Heblo.Application/Features/FileStorage/UseCases/DownloadFromUrl/DownloadFromUrlHandler.cs`)
- **Responsibility**: unchanged. No code change to `Handle()` or `Failure()` — this design fix only relaxes the type that `Failure()`'s object initializer targets, letting its existing (already-correct) behavior compile without the null-forgiving operator.

### `FileStorageController` (backend/src/Anela.Heblo.API/Controllers/FileStorageController.cs)
- **Responsibility**: unchanged. It forwards `DownloadFromUrlResponse` as-is; it does not read `BlobUrl`/`BlobName`/`ContainerName` itself, so no controller code change is required.

### Generated TypeScript client (`frontend/src/api/generated/api-client.ts`)
- **Responsibility**: regenerated (not hand-edited) via the project's standard OpenAPI client generation step (`docs/development/api-client-generation.md`), so the corresponding interface types `blobUrl`, `blobName`, `containerName` as optional/nullable, matching the corrected backend contract.

## Data Schemas

### `DownloadFromUrlResponse` — C# shape (after fix)
```csharp
public class DownloadFromUrlResponse : BaseResponse
{
    public string? BlobUrl { get; set; }
    public string? BlobName { get; set; }
    public string? ContainerName { get; set; }
    public long FileSizeBytes { get; set; }
}
```

### JSON wire shape (unchanged bytes, corrected schema annotation)

Success response:
```json
{
  "success": true,
  "blobUrl": "https://mockstorageaccount.blob.core.windows.net/documents/test-document.pdf",
  "blobName": "test-document.pdf",
  "containerName": "documents",
  "fileSizeBytes": 1024
}
```

Failure response (any of the three `Handle()` catch paths — timeout, HTTP error, unexpected exception):
```json
{
  "success": false,
  "errorCode": "FileDownloadFailed",
  "params": {
    "fileUrl": "https://example.com/export.csv",
    "cause": "http-status",
    "attemptCount": "1",
    "elapsedMs": "842",
    "error": "404 Not Found"
  },
  "blobUrl": null,
  "blobName": null,
  "containerName": null,
  "fileSizeBytes": 0
}
```
No change to which fields are present on the wire — `System.Text.Json` already serializes an unset reference-type property as JSON `null` regardless of the C# nullable annotation. Only the generated OpenAPI schema's `nullable`/`required` metadata for these three fields changes, which is what makes the TypeScript client's types (FR-4) match actual runtime behavior.
