# Implementation: contracts-and-dto

## What was implemented
Added the `ExpeditionListArchive`-owned narrow blob-storage contract and its supporting DTO, exactly as specified in the task context. These are the first step in decoupling `ExpeditionListArchive` from a direct dependency on `IBlobStorageService` (owned by the `FileStorage` module): a FileStorage-owned adapter implementing `IExpeditionListArchiveBlobStore` will be added in a later task and registered via DI, so `ExpeditionListArchive` handlers can depend on this interface instead of reaching across the module boundary.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/ExpeditionBlobItem.cs` — new DTO class (per project convention, DTOs are classes, never records) mirroring the shape of `Anela.Heblo.Domain.Features.FileStorage.BlobItemInfo` but owned by `ExpeditionListArchive`.
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs` — new interface exposing exactly the three operations `ExpeditionListArchive` needs (`DownloadAsync`, `ListBlobsAsync`, `ListVirtualDirectoriesAsync`), to be implemented by a FileStorage-owned adapter.

Both files match the task context's code verbatim, and match the existing style of sibling files in the same `Contracts/` folder (`ExpeditionListItemDto.cs`, `ITemporaryFileAccessor.cs` — file-scoped namespace, no additional usings needed).

## Tests
None required by this task — these are pure contract/DTO declarations with no behavior. No handlers reference them yet (that's a later task), so there is nothing to unit test at this step.

## How to verify
```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Result: Build succeeded, 0 errors, 135 pre-existing warnings (none introduced by the two new files).

## Notes
No deviations from the task context. Left the two `[ ]` step-4 commit commands from the task context unexecuted as a separate git commit — this orchestrator's staging/commit flow commits the whole round (impl + review artifacts + source) together per its own convention (`chore(feat-4231): impl+review for contracts-and-dto r1`), rather than the task file's suggested standalone `feat(...)` commit message.

## PR Summary
Adds the `IExpeditionListArchiveBlobStore` contract and `ExpeditionBlobItem` DTO to `ExpeditionListArchive.Contracts`, the first step toward removing `ExpeditionListArchive`'s direct dependency on `FileStorage`'s `IBlobStorageService`. No existing code changes yet — this is additive only, with zero behavior change.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/ExpeditionBlobItem.cs` — new DTO
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/Contracts/IExpeditionListArchiveBlobStore.cs` — new interface

## Status
DONE
