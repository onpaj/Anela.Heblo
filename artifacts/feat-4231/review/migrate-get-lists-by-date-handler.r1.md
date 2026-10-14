# Code Review: migrate-get-lists-by-date-handler

## Summary
The handler and its tests were updated exactly as specified in the task context: `GetExpeditionListsByDateHandler` now depends on `IExpeditionListArchiveBlobStore` instead of the FileStorage-owned `IBlobStorageService`, and the test suite mocks the new interface using `ExpeditionBlobItem`. The change is consistent with the pattern already applied in `DownloadExpeditionListHandler` and `ReprintExpeditionListHandler` in earlier tasks of this feature. Tests were run and pass.

## Review Result: PASS

### task: migrate-get-lists-by-date-handler
**Status:** PASS

Verified:
- `GetExpeditionListsByDateHandler.cs` constructor takes `IExpeditionListArchiveBlobStore`, field renamed `_blobStore`, `Anela.Heblo.Domain.Features.FileStorage` using removed, `Handle` calls `_blobStore.ListBlobsAsync(...)` — matches the task-context spec verbatim.
- `GetExpeditionListsByDateHandlerTests.cs` mocks `IExpeditionListArchiveBlobStore`, uses `ExpeditionBlobItem` test data, adds the `Contracts` namespace using, removes the `FileStorage` domain using — matches spec verbatim.
- No leftover references to `IBlobStorageService` or `BlobItemInfo` remain in either file.
- Naming (`_blobStore`) is consistent with the sibling handlers already migrated (`DownloadExpeditionListHandler`, `ReprintExpeditionListHandler`).
- `dotnet test --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests"` → `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7`.
- Acceptance criteria from the task context (Steps 1-4) are all satisfied; commit was made on the current branch.

## Docs to Update
(None — this is an internal module-boundary refactor with no change to public behavior, CLI, or configuration.)

## Overall Notes
This is the third of four handlers being migrated to the narrow `IExpeditionListArchiveBlobStore` contract in this feature. Remaining: `migrate-get-dates-handler`, then `module-boundary-guard` and `final-verification`.

**Status:** PASS
