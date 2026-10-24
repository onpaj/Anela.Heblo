# Specification: ExpeditionListArchive download returns structured failure for missing blob

## Summary
`GET /api/expedition-list-archive/download/{*blobPath}` currently returns an unstructured HTTP 500 when the requested archived PDF does not exist in blob storage. The download use case must treat a missing blob as a normal client-side condition and return a structured failure (`Success=false`, `ErrorCode=BlobNotFound`).

## Background
`DownloadExpeditionListHandler` validates the path via `BlobPathValidator` and then calls `IExpeditionListArchiveBlobStore.DownloadAsync`. The Azure SDK throws `RequestFailedException` (Status 404) for a missing blob; `AzureBlobStorageService.DownloadAsync` logs and rethrows it. Nothing catches it, so MVC returns 500, indistinguishable from a backend crash. `ErrorCodes.BlobNotFound = 1805` already exists (HTTP 404 attribute, FileStorage 18XX range). `ReprintExpeditionListHandler` intentionally propagates blob exceptions and must remain unchanged.

## Functional Requirements

### FR-1: Missing blob yields structured failure
When the blob for a syntactically valid path does not exist, the handler returns a `DownloadExpeditionListResponse` with `Success=false`, `ErrorCode=ErrorCodes.BlobNotFound`, `Stream=null`.
**Acceptance criteria:**
- Handler test: store mock throws the "not found" signal; result is `Success=false`, `ErrorCode==BlobNotFound`, `Stream==null`.
- Other (non-not-found) exceptions still propagate.
- Invalid-path behaviour (`InvalidBlobPath`) unchanged.

### FR-2: HTTP mapping
The download endpoint returns a non-500 client error with the response body containing the error code. The controller currently returns `BadRequest(response)` for any failure. Because `BlobNotFound` is annotated 404 and the module convention is `HandleResponse`-style mapping, the controller should map failures through the ErrorCode status (404 for BlobNotFound, 400 for InvalidBlobPath).
**Acceptance criteria:**
- Missing blob -> HTTP 404 with JSON body `{success:false, errorCode:1805}`.
- Invalid path -> HTTP 400 as before.
- Successful download still returns the file.

### FR-3: Not-found signal respects layering
The Application project has no Azure SDK reference, so the handler cannot catch `RequestFailedException`. The Azure adapter translates a 404 into a provider-neutral `FileNotFoundException`, documented on `IBlobStorageService.DownloadAsync` and `IExpeditionListArchiveBlobStore.DownloadAsync`.
**Acceptance criteria:**
- `AzureBlobStorageService.DownloadAsync` throws `FileNotFoundException` (inner = original exception) when `RequestFailedException.Status == 404`; other errors rethrown unchanged.
- No other production caller of `IBlobStorageService.DownloadAsync` regresses (verified by grep: only the ExpeditionListArchive adapter calls it).

## Non-Functional Requirements
### NFR-1: Performance
No additional round trips (no `ExistsAsync` pre-check; avoids TOCTOU race).
### NFR-2: Security
Path validation still runs first. Error body does not leak storage details.

## Data Model
None.

## API / Interface Design
Same endpoint; new failure response for missing blob (404, `errorCode: 1805`). OpenAPI shape unchanged (no new fields), so no client regeneration needed.

## Dependencies
`ErrorCodes.BlobNotFound` (existing), `Azure.Storage.Blobs` in the Azure adapter project only.

## Out of Scope
- Reprint handler behaviour.
- Frontend changes (existing generic error handling applies).
- Adding methods to `IExpeditionListArchiveBlobStore`.

## Open Questions
None.

## Status: COMPLETE
