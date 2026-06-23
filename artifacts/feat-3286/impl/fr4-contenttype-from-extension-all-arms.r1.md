# FR-4 Implementation: GetContentTypeFromExtension — All Arms

## What was implemented

Deleted the placeholder test `GetContentTypeFromExtension_DifferentExtensions_ShouldReturnCorrectContentType` which had no meaningful assertions (only `Assert.NotEmpty` on strings that could never be empty).

Replaced it with two real tests that exercise `GetContentTypeFromExtension` indirectly via the production flow in `DownloadFromUrlAsync`:

### `DownloadFromUrlAsync_NoResponseContentType_InfersContentTypeFromExtension` (Theory — 15 cases)

Exercises all 15 switch arms of `GetContentTypeFromExtension`:
- `.jpg` / `.jpeg` → `image/jpeg`
- `.png` → `image/png`
- `.gif` → `image/gif`
- `.webp` → `image/webp`
- `.pdf` → `application/pdf`
- `.txt` → `text/plain`
- `.json` → `application/json`
- `.xml` → `application/xml`
- `.zip` → `application/zip`
- `.doc` → `application/msword`
- `.docx` → `application/vnd.openxmlformats-officedocument.wordprocessingml.document`
- `.xls` → `application/vnd.ms-excel`
- `.xlsx` → `application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`
- `.unknown` → `application/octet-stream` (default arm)

Uses `BuildNoContentTypeHandler()` which returns a `ByteArrayContent` response with no `Content-Type` header, forcing the `null` branch at line 59 of `AzureBlobStorageService.cs`:
```csharp
var contentType = response.Content.Headers.ContentType?.MediaType ?? GetContentTypeFromExtension(blobName);
```

Asserts that `BlobClient.UploadAsync` is called with `BlobUploadOptions.HttpHeaders.ContentType == expectedContentType`.

### `DownloadFromUrlAsync_NoResponseContentType_UppercaseExtension_InfersContentType` (Fact)

Verifies that `GetContentTypeFromExtension` lowercases the extension before matching (`.ToLowerInvariant()` in production code). Passes `PHOTO.JPG` as blob name and asserts `image/jpeg` is used.

## Test results

- FR-4 tests only: **16 passed, 0 failed** (15 theory + 1 fact)
- Full `AzureBlobStorageServiceTests` class: **48 passed, 0 failed** (no regressions)

## Key technical details

- `BuildNoContentTypeHandler()` uses `ByteArrayContent` (not `StringContent`) to avoid the automatic `Content-Type: text/plain` header that `StringContent` sets
- Tests pass explicit `blobName` to `DownloadFromUrlAsync` (3rd parameter, optional `string? blobName = null`) so the extension is known and controllable
- `SetupContainerAndBlobClient` captures `BlobUploadOptions` correctly — the blob mock is set up with `It.IsAny<BlobUploadOptions>()` which allows `Verify` with `It.Is<BlobUploadOptions>` to assert the specific content type

## Files changed

- `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`: deleted 15-line placeholder, added 64 lines of real tests
