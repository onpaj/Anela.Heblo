# Implementation: add-shared-test-helpers

## What was implemented
Added `using Azure;` directive and three private test helpers to AzureBlobStorageServiceTests.cs:
- `SetupContainerAndBlobClient` — wires mock BlobServiceClient/ContainerClient/BlobClient with Callback to capture blob names
- `BuildNoContentTypeHandler` — returns StubHttpMessageHandler with ByteArrayContent (no Content-Type header)
- `CreateAsyncPageable<T>` — wraps items into AsyncPageable<T> for mocking Azure SDK enumerable methods

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs` — added using Azure; and 3 private helpers

## Tests
No new tests in this task — helpers only. Build verified.

## How to verify
`dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` passes.

## Notes
ByteArrayContent is used (not StringContent) in BuildNoContentTypeHandler to ensure Content-Type header is null.

## PR Summary
Added shared test helpers to AzureBlobStorageServiceTests to support coverage-gap tests for blob name capture, content-type fallback, and virtual directory listing.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs` — added using Azure; and 3 private test helpers

## Status
DONE
