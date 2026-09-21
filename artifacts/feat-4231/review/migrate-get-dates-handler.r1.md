# Code Review: migrate-get-dates-handler

## Summary

The implementation migrates `GetExpeditionDatesHandler` from the legacy
`IBlobStorageService` to `IExpeditionListArchiveBlobStore`, exactly matching
the task-context's Step 1 and Step 2 full-file replacements. All
filtering/sorting/pagination logic is untouched, and the 5-test suite passes.

## Review Result: PASS

### task: migrate-get-dates-handler
**Status:** PASS

Verified:
- `GetExpeditionDatesHandler.cs` matches the task-context Step 1 code block byte-for-byte: constructor now takes `IExpeditionListArchiveBlobStore`, field renamed to `_blobStore`, call site updated, `using Anela.Heblo.Domain.Features.FileStorage;` removed and replaced with `using Anela.Heblo.Application.Features.ExpeditionListArchive.Contracts;`. `IsValidDatePrefix`, `OrderByDescending(..., StringComparer.Ordinal)`, and `Skip`/`Take` pagination logic are unchanged.
- `GetExpeditionDatesHandlerTests.cs` matches the task-context Step 2 code block byte-for-byte: mock type updated to `Mock<IExpeditionListArchiveBlobStore>`, all 5 test bodies and assertions unchanged in intent.
- `IExpeditionListArchiveBlobStore` is already registered in DI (from the earlier `adapter-and-di` task in this feature — confirmed via `ExpeditionListArchiveModule.cs` and its use in the already-migrated `DownloadExpeditionListHandler`, `ReprintExpeditionListHandler`, and `GetExpeditionListsByDateHandler`), so no additional DI wiring was needed for this handler.
- Test run: `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetExpeditionDatesHandlerTests"` → `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`.
- No other file in the codebase references this handler's constructor directly, so no call-site breakage.

## Docs to Update
(none — this is an internal dependency swap within an already-documented migration; no public behavior, CLI, or docs changed)

## Overall Notes
Clean, isolated, mechanical migration consistent with the pattern already established by the three prior handler migrations in this feature (`migrate-download-handler`, `migrate-reprint-handler`, `migrate-get-lists-by-date-handler`). No concerns.
