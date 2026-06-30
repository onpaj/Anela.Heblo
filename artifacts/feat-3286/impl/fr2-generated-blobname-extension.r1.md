# FR-2 Implementation: Generated BlobName Uses Prefix + Content-Type Extension

## What was implemented

Two xUnit tests were added to `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs` after the existing FR-1 test (`DownloadFromUrlAsync_UrlWithFilename_UsesBlobNameFromPath`).

These tests exercise the branch in `AzureBlobStorageService.DownloadFromUrlAsync` where `blobName` is `null` **and** `Path.GetFileName` of the URL path returns an empty string (URL ends with `/`), causing the production code to generate a name of the form `$"downloaded-file-{Guid.NewGuid()}{GetExtensionFromContentType(contentType)}"`.

## Test names

1. `DownloadFromUrlAsync_UrlWithNoFilename_KnownContentType_UsesPrefixAndExtension`
   - URL: `https://example.com/files/` (path ends with `/`, filename is empty)
   - Response `Content-Type`: `image/png`
   - Asserts: captured blob name starts with `"downloaded-file-"` and ends with `".png"`

2. `DownloadFromUrlAsync_UrlWithNoFilename_UnknownContentType_UsesBinExtension`
   - URL: `https://example.com/files/` (path ends with `/`, filename is empty)
   - Response `Content-Type`: `application/x-unknown`
   - Asserts: captured blob name starts with `"downloaded-file-"` and ends with `".bin"` (fallback extension)

## Pass / Fail results

Both tests **passed** on first run after build.

```
Passed!  - Failed: 0, Passed: 2, Skipped: 0, Total: 2, Duration: 3 ms
```

## Adaptations made

None required. The existing shared helper `SetupContainerAndBlobClient` (which captures blob names via a `Callback` on `GetBlobClient`) and the `StubHttpMessageHandler` constructor (with `overrideContent` named parameter) were both compatible with the test code as specified.

The URL `https://example.com/files/` (trailing slash) successfully triggers the generated-name branch without any URL adjustment.

## Commit

`test(filestorage): FR-2 generated blobName uses prefix + content-type extension`
