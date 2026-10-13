# Code Review: migrate-reprint-handler

## Summary
The implementation matches the task-context spec verbatim: `ReprintExpeditionListHandler` and its
DI factory now depend on `IExpeditionListArchiveBlobStore` instead of the shared `IBlobStorageService`,
with only the type/name and the removed `using` changed. Behavior, error handling, and cleanup logic
are byte-for-byte unchanged, and the test suite was updated consistently and passes.

## Review Result: PASS

### task: migrate-reprint-handler
**Status:** PASS

Checks performed:
- **Spec compliance** — `ReprintExpeditionListHandler.cs`, `ExpeditionListArchiveModule.cs`, and
  `ReprintExpeditionListHandlerTests.cs` diffs match the task-context's full-file replacements exactly
  (verified via `git diff` against the spec's code blocks).
- **Architecture adherence** — removes the cross-module-boundary dependency on
  `Anela.Heblo.Domain.Features.FileStorage.IBlobStorageService`, replacing it with the module-local
  `IExpeditionListArchiveBlobStore` adapter registered by the earlier `adapter-and-di` task, consistent
  with this issue's arch-review finding and the pattern already established by `migrate-download-handler`.
- **Completeness** — all 3 files listed in the task-context were modified; all 5 existing test methods
  were updated to mock the new interface; no test cases were dropped.
- **Correctness** — `dotnet build` of the Application project succeeds with 0 errors; `dotnet test`
  filtered to `ReprintExpeditionListHandlerTests` reports `Passed! - Failed: 0, Passed: 5, Skipped: 0,
  Total: 5`. The keyed `"cups"` `IPrintQueueSink` fallback resolution, try/finally temp-file cleanup, and
  `BlobPathValidator` validation are untouched.
- **Documentation** — no public behavior, CLI, or environment variable changes; nothing to update.

## Docs to Update
(none — internal refactor only, no doc-worthy behavior change)

## Overall Notes
No concerns. Ready for the next task in the plan (`migrate-get-lists-by-date-handler`).
