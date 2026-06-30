# FR-6 Implementation: ListVirtualDirectoriesAsync — Trailing-Slash Trimming

**Round:** r1  
**Date:** 2026-06-23  
**Branch:** feature/3286-Coverage-Gap-Filestorage-Azureblobstorageservice-B

## What was done

Added two unit tests to `AzureBlobStorageServiceTests` covering `ListVirtualDirectoriesAsync`:

1. `ListVirtualDirectoriesAsync_TrimsTrailingSlash_FromPrefixes` — verifies that prefixes returned by `GetBlobsByHierarchyAsync` with trailing slashes (e.g. `"invoices/"`, `"reports/"`) are trimmed to bare names (`"invoices"`, `"reports"`) in the result list.

2. `ListVirtualDirectoriesAsync_EmptyContainer_ReturnsEmptyList` — verifies that when `GetBlobsByHierarchyAsync` returns no items, the method returns an empty list.

## File modified

`backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

Tests inserted after the FR-5 test block, before the "UploadAsync — container cache behaviour" section.

## Approach

- Used `BlobsModelFactory.BlobHierarchyItem("prefix/", null)` to create items where `IsPrefix == true`.
- Mocked `GetBlobsByHierarchyAsync` with `It.IsAny<>()` for all five parameters (BlobTraits, BlobStates, delimiter string, prefix string, CancellationToken).
- Reused the existing `CreateAsyncPageable<T>` helper already present in the file.
- `_mockBlobServiceClient` is the shared field from the test class constructor.

## Test results

```
Passed: 2
Failed: 0
Total time: ~1.7s
```

Both tests pass against the existing `AzureBlobStorageService` implementation.
