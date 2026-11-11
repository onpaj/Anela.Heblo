# Design: Download missing-blob handling

## Component Design
- `AzureBlobStorageService.DownloadAsync`: `catch (RequestFailedException ex) when (ex.Status == 404)` -> log at Warning, `throw new FileNotFoundException($"Blob '{blobName}' not found in container '{containerName}'.", blobName, ex)`. Generic `catch (Exception)` retained for others (log error, rethrow). Order: 404 clause first.
- `DownloadExpeditionListResponse`: add `public static DownloadExpeditionListResponse NotFound() => new() { Success = false, ErrorCode = ErrorCodes.BlobNotFound };`
- `DownloadExpeditionListHandler.Handle`: wrap `DownloadAsync` in `try { ... } catch (FileNotFoundException) { return DownloadExpeditionListResponse.NotFound(); }`.
- Controller `Download`: on failure return 404 for `BlobNotFound`, 400 otherwise (prefer reuse of base status mapping).

## Data Schemas
Failure body (existing `BaseResponse` shape): `{ "success": false, "errorCode": 1805, "params": null }`. No schema change.
