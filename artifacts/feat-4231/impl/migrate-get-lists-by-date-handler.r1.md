# Implementation: migrate-get-lists-by-date-handler

## What was implemented
Migrated `GetExpeditionListsByDateHandler` from the generic `IBlobStorageService` (FileStorage domain) to the ExpeditionListArchive-owned `IExpeditionListArchiveBlobStore` narrow contract, following the same pattern already applied to `DownloadExpeditionListHandler` and `ReprintExpeditionListHandler` in earlier tasks of this feature. Updated the corresponding unit tests to mock the new interface and use `ExpeditionBlobItem` instead of `BlobItemInfo`.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs` — constructor now takes `IExpeditionListArchiveBlobStore` (field renamed `_blobStore`), removed the `Anela.Heblo.Domain.Features.FileStorage` using; `Handle` calls `_blobStore.ListBlobsAsync(...)`.
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` — mock changed from `Mock<IBlobStorageService>` to `Mock<IExpeditionListArchiveBlobStore>`, test data changed from `BlobItemInfo` to `ExpeditionBlobItem`, added `using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;`, removed the `Anela.Heblo.Domain.Features.FileStorage` using.

## Tests
`GetExpeditionListsByDateHandlerTests` — 3 test methods (7 cases via `[Theory]`):
- `Handle_ReturnsItemsForDate` — happy path, multiple PDF blobs mapped to DTOs.
- `Handle_FiltersPdfFilesOnly` — non-PDF blobs excluded from results.
- `Handle_ReturnsFailure_WhenDateIsInvalid` (Theory, 5 cases) — invalid/empty/null date short-circuits with `ErrorCodes.InvalidFormat` and never calls the blob store.

## How to verify
```
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionListsByDateHandlerTests"
```
Result: `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7`.

## Notes
No deviations from the task context — the handler and test file were replaced exactly as specified. `ExpeditionBlobItem` and `BlobItemInfo` have identical shape, so no further mapping changes were needed inside the `.Select(...)` projection.

## PR Summary
Continues the ExpeditionListArchive → FileStorage module-boundary migration: `GetExpeditionListsByDateHandler` now depends on the module's own narrow `IExpeditionListArchiveBlobStore` contract instead of reaching directly into `Anela.Heblo.Domain.Features.FileStorage`'s `IBlobStorageService`. This is the third of four handlers being migrated in this feature.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionListsByDate/GetExpeditionListsByDateHandler.cs` — use `IExpeditionListArchiveBlobStore` instead of `IBlobStorageService`
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionListsByDateHandlerTests.cs` — updated mocks/types to match

## Status
DONE
