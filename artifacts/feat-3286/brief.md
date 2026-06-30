## Module / File
backend/src/Anela.Heblo.Application/Features/FileStorage/Services/AzureBlobStorageService.cs

## Coverage
Line coverage: 39.5% (filter threshold: 60%)

## What's not tested
`DownloadFromUrlAsync` has a nested fallback chain for blobName generation that is entirely untested:
1. If `blobName` is provided → use it
2. If not provided → extract filename from the URL path via `Path.GetFileName`
3. If the URL has no filename component → call `GetExtensionFromContentType` and generate a GUID-based name

`GetExtensionFromContentType` (8-arm switch expression) and `GetContentTypeFromExtension` (13-arm switch expression) are both private helper methods that map MIME types to file extensions and vice versa. No test covers any arm of either switch, including the `_ => .bin` / `_ => application/octet-stream` fallback arms.

`GetOrCreateContainerAsync` uses a `ConcurrentDictionary` to cache container creation — the "already seen" path (no `CreateIfNotExistsAsync` call) is not tested.

`ListVirtualDirectoriesAsync` strips the trailing `/` from blob hierarchy prefixes — this string operation has no test.

## Why it matters
The blobName generation path is exercised whenever a file is downloaded from an external URL with no explicit name (e.g. from Shoptet product images). A broken fallback would either throw a `UriFormatException` or store files under empty or malformed names. Wrong content-type inference causes files to be served with incorrect MIME types, breaking browser display for images and PDFs.

## Suggested approach
Unit tests with a mocked `BlobServiceClient`/`IHttpClientFactory`:
- URL with filename in path → blobName extracted correctly
- URL without filename, response has `image/jpeg` content-type → blob gets `.jpg` extension
- URL without filename, unknown content-type → blob gets `.bin` extension
- `GetContentTypeFromExtension` for `.pdf`, `.png`, unknown extension → correct MIME types
- `GetOrCreateContainerAsync` called twice for same container → `CreateIfNotExistsAsync` called only once
Estimated effort: ~2–3 hours (Azure SDK mocking is the main complexity).

---
_Filed by weekly coverage-gap routine on 2026-06-22. Based on CI run #27941952679 (9463aa5983b2a6d201782725aeeaaba777d8c07d)._
