# Implementation: adapter-and-di

## What was implemented

Created `ExpeditionListArchiveBlobStoreAdapter`, a FileStorage-owned adapter that implements
ExpeditionListArchive's `IExpeditionListArchiveBlobStore` contract by delegating to the existing
`IBlobStorageService`. Registered it as a Singleton DI binding in `FileStorageModule`, mirroring
the existing `KnowledgeBaseLeafletSourceAdapter` pattern used elsewhere in the codebase for the
same cross-module producer/consumer shape.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/ExpeditionListArchiveBlobStoreAdapter.cs` — new adapter class (internal sealed), implements `DownloadAsync`, `ListBlobsAsync` (maps `BlobItemInfo` → `ExpeditionBlobItem`), and `ListVirtualDirectoriesAsync` by delegating to `IBlobStorageService`.
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — added `using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;` and registered `services.AddSingleton<IExpeditionListArchiveBlobStore, ExpeditionListArchiveBlobStoreAdapter>();` just before `return services;`.

## Tests

No new tests required by the task context for this step (adapter is pure delegation with no
independent logic beyond straightforward DTO mapping). Compilation was used as the verification
step, as specified.

## How to verify

```bash
cd backend && dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
```
Build succeeded, 0 errors (pre-existing unrelated warnings only).

`dotnet format Anela.Heblo.sln --include <the two changed files> --verify-no-changes` also passed
with no changes needed.

## Notes

Implemented exactly per the task context's provided code snippets — no deviations. The adapter is
registered as `Singleton` to match `IBlobStorageService`'s own lifetime, per the task's explicit
instruction and inline comment.

## PR Summary
Added `ExpeditionListArchiveBlobStoreAdapter` in the FileStorage module, implementing
ExpeditionListArchive's `IExpeditionListArchiveBlobStore` consumer contract by delegating to the
existing `IBlobStorageService`. Registered the adapter as a Singleton DI binding in
`FileStorageModule`. This is the producer-side half of decoupling ExpeditionListArchive from a
direct dependency on FileStorage's `IBlobStorageService`.

### Changes
- `backend/src/Anela.Heblo.Application/Features/FileStorage/Infrastructure/ExpeditionListArchiveBlobStoreAdapter.cs` — new adapter
- `backend/src/Anela.Heblo.Application/Features/FileStorage/FileStorageModule.cs` — DI registration

## Status
DONE
