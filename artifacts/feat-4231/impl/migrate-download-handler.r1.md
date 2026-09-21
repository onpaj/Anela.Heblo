# Implementation: migrate-download-handler

## What was implemented

Migrated `DownloadExpeditionListHandler` off the shared `Anela.Heblo.Domain.Features.FileStorage.IBlobStorageService`
abstraction and onto the module-local `IExpeditionListArchiveBlobStore` adapter introduced by the
`adapter-and-di` task. The field/parameter was renamed (`_blobStorageService` → `_blobStore`) and the
`using Anela.Heblo.Domain.Features.FileStorage;` import was removed. No other logic changed — validation via
`BlobPathValidator.IsValid`, the download call, and the response shape are all unchanged.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs` — constructor now takes `IExpeditionListArchiveBlobStore`; call site updated to `_blobStore.DownloadAsync(...)`.
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs` — mock type switched from `Mock<IBlobStorageService>` to `Mock<IExpeditionListArchiveBlobStore>`, `using Anela.Heblo.Domain.Features.FileStorage;` removed.

## Tests

- `DownloadExpeditionListHandlerTests.Handle_ValidBlobPath_ReturnsBlobStream` — verifies a successful download returns the stream, content type and file name.
- `DownloadExpeditionListHandlerTests.Handle_InvalidBlobPath_ReturnsFailure` (Theory, 5 cases) — verifies invalid/traversal/malformed blob paths fail validation and never reach the blob store.

## How to verify

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~DownloadExpeditionListHandlerTests"
```

Result: `Passed! - Failed: 0, Passed: 6, Skipped: 0, Total: 6`.

## Notes

Followed the task-context file verbatim — full-file replacements as specified, no deviations.

## PR Summary
Migrated `DownloadExpeditionListHandler` from the shared `IBlobStorageService` to the module-scoped `IExpeditionListArchiveBlobStore` adapter, continuing the ExpeditionListArchive module's move away from the shared FileStorage domain abstraction. Behavior is unchanged; only the storage dependency and its test double were swapped.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/DownloadExpeditionList/DownloadExpeditionListHandler.cs` — use `IExpeditionListArchiveBlobStore` instead of `IBlobStorageService`
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/DownloadExpeditionListHandlerTests.cs` — mock updated to `IExpeditionListArchiveBlobStore`

## Status
DONE
