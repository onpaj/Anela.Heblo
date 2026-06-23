# FR-1: blobName derived from URL path filename — impl artifact

## Task

Add a test verifying that `DownloadFromUrlAsync` uses `Path.GetFileName(new Uri(fileUrl).LocalPath)` as the blob name when no explicit `blobName` argument is supplied.

## Changes

**File modified:**
`backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

**Test added:**
`DownloadFromUrlAsync_UrlWithFilename_UsesBlobNameFromPath` — inserted after the error-propagation section, before the container-cache section.

## Approach

- Uses `StubHttpMessageHandler(HttpStatusCode.OK, "test content")` to simulate a successful HTTP download.
- Creates a fresh `HttpClient` per test and wires it into `_mockHttpClientFactory` so the test is independent of the shared constructor setup.
- Calls `SetupContainerAndBlobClient` (shared helper) to capture all blob names passed to `GetBlobClient`.
- Asserts `Assert.Contains("report.pdf", capturedBlobNames)` — verifies the production code extracts the filename from the URL path rather than generating a GUID.

## Test result

Passed: 1, Failed: 0 (verified with `dotnet test --no-build --filter FullyQualifiedName~DownloadFromUrlAsync_UrlWithFilename_UsesBlobNameFromPath`).

## Commit

`d94af23` — `test(filestorage): FR-1 blobName derived from URL path filename`
