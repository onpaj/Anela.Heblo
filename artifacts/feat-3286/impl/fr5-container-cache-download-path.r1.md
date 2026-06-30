# FR-5 Implementation: Container Cache — Download Path

## Summary

Added test `DownloadFromUrlAsync_CalledTwice_SameContainer_CallsCreateIfNotExistsOnce` to verify that `GetOrCreateContainerAsync` only calls `CreateIfNotExistsAsync` once when `DownloadFromUrlAsync` is invoked twice for the same container.

## Test file

`backend/test/Anela.Heblo.Tests/Features/FileStorage/AzureBlobStorageServiceTests.cs`

## Key findings

- Production code (`GetOrCreateContainerAsync`) uses `ConcurrentDictionary.TryAdd` to gate `CreateIfNotExistsAsync`. First call adds the key and creates; subsequent calls for the same container skip creation.
- Production code calls the 2-argument overload: `CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: cancellationToken)`.
- Existing cache tests (`UploadAsync_CalledMultipleTimes_*`) use the 4-argument Moq setup/verify form (`PublicAccessType, IDictionary, BlobContainerEncryptionScopeOptions, CancellationToken`) with `It.IsAny<>` — Moq matches these regardless of which SDK overload is actually called at runtime. The FR-5 test follows the same pattern.
- A fresh `AzureBlobStorageService` instance is constructed in the test body (not the shared `_service`) so the cache starts empty.

## Test result

Passed (1/1).

## Commit

`test(filestorage): FR-5 container cache reached via download path`
