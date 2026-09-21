# Implementation: migrate-get-dates-handler

## What was implemented

Migrated `GetExpeditionDatesHandler` from the legacy `IBlobStorageService` (from
`Anela.Heblo.Domain.Features.FileStorage`) to the module-scoped
`IExpeditionListArchiveBlobStore` abstraction, matching the pattern already
applied to `DownloadExpeditionListHandler`, `ReprintExpeditionListHandler`,
and `GetExpeditionListsByDateHandler` in earlier tasks of this feature.

The handler's field/parameter is now `IExpeditionListArchiveBlobStore
_blobStore` instead of `IBlobStorageService _blobStorageService`, the call
site (`_blobStore.ListVirtualDirectoriesAsync(...)`) is otherwise unchanged,
and the `using Anela.Heblo.Domain.Features.FileStorage;` import was removed
in favor of `using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;`.
All filtering (`IsValidDatePrefix`), sorting (`OrderByDescending` with
`StringComparer.Ordinal`), and pagination (`Skip`/`Take`) logic is
byte-for-byte unchanged.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs` — swapped the blob-store dependency from `IBlobStorageService` to `IExpeditionListArchiveBlobStore`; no behavioral change.
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs` — updated the mock type from `Mock<IBlobStorageService>` to `Mock<IExpeditionListArchiveBlobStore>` and the corresponding `using` statements; test bodies and assertions unchanged.

## Tests

`GetExpeditionDatesHandlerTests.cs` (5 tests, all unchanged in intent, only the mock type updated):
- `Handle_ReturnsDatesSortedDescending` — verifies descending ordinal sort of valid date prefixes.
- `Handle_PaginatesCorrectly` — verifies page/pageSize slicing over 25 prefixes.
- `Handle_EmptyContainer_ReturnsEmptyList` — verifies empty-list behavior.
- `Handle_CallsListVirtualDirectoriesOnce_AndNeverCallsListBlobs` — verifies the handler uses `ListVirtualDirectoriesAsync` exactly once and never calls `ListBlobsAsync`.
- `Handle_FiltersOutNonDatePrefixes` — verifies non-date and structurally-invalid-date prefixes are filtered out.

## How to verify

```bash
cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionDatesHandlerTests"
```

Result: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5, Duration: 13 ms`.

## Notes

No deviations from the task-context spec. The task-context file's Step 1 and
Step 2 code blocks were applied as full-file replacements exactly as given,
and Step 3's verification command was run and confirmed passing. This is a
mechanical, isolated migration — no other file in the codebase references
this handler's constructor, so no call-site or DI-registration changes were
needed (DI registration was already handled by the earlier `adapter-and-di`
task in this feature).

## PR Summary
Migrated `GetExpeditionDatesHandler` off the legacy `IBlobStorageService` onto the module's own `IExpeditionListArchiveBlobStore`, continuing the per-handler migration started in earlier tasks of this feature. Purely a dependency swap — filtering, sorting, and pagination logic is unchanged, and the existing 5-test suite was updated to mock the new interface and confirmed passing.

### Changes
- `backend/src/Anela.Heblo.Application/Features/ExpeditionListArchive/UseCases/GetExpeditionDates/GetExpeditionDatesHandler.cs` — constructor now takes `IExpeditionListArchiveBlobStore` instead of `IBlobStorageService`
- `backend/test/Anela.Heblo.Tests/ExpeditionListArchive/GetExpeditionDatesHandlerTests.cs` — mock updated to `Mock<IExpeditionListArchiveBlobStore>`

## Status
DONE
