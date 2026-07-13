# Code Review: Fix running-invoice-import-jobs filter always returning empty

## Summary
The implementation correctly replaces the broken `Contains("InvoiceImport", ...)` predicate with `StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase)` in `GetRunningInvoiceImportJobsHandler`, matching the real Hangfire display name produced by `InvoiceImportService.ImportInvoicesAsync`'s `[DisplayName]` attribute. The stale comment was updated, and all five unit tests were rewritten with realistic job names, including the required regression case for the old broken string. Verified directly against the diff and by independently re-running the affected test suite — all pass.

## Review Result: PASS

### task: fix-running-invoice-import-jobs-filter
**Status:** PASS

## Docs to Update
None.

## Overall Notes
- Verified `git show HEAD` for both files: the handler change (`backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs:50-55`) is exactly the predicate/comment change specified in the task context and arch-review — `StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase)`, null-safe via `job.JobName != null &&`, case-insensitive, and leaves caching, error handling (catch-and-return-empty-list), and logging untouched (FR-1 fully satisfied).
- Verified `GetRunningInvoiceImportJobsHandlerTests.cs` in full: `Handle_FiltersToInvoiceImportJobsOnly` now uses `"Import faktur: faktura 12345"` / `"Import faktur: denní import CZK za 12.07.2026"` for included jobs, and `"Daily Invoice DQT Check"`, `"MetaAds Invoice Import"`, and `"InvoiceImportJob.Run"` for excluded jobs (the last being the required regression guard against the old broken-filter-only string), asserting `HaveCount(2)` with IDs `{"r1", "p1"}`. The remaining four tests (`Handle_CacheHit_DoesNotCallWorkerSecondTime`, `Handle_CacheDisabled_CallsWorkerOnEveryInvocation`, `Handle_CacheDisabled_DoesNotWriteToCache`, `Handle_WorkerThrows_ReturnsEmptyListAndDoesNotCache`) were updated/left as specified in the task context (FR-2 fully satisfied).
- Independently ran `dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetRunningInvoiceImportJobsHandlerTests"` in the working directory: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`.
- Scope is surgical: only the two files named in the task context were modified (confirmed via `git show --stat` equivalent diff output); no changes to `BackgroundJobInfo`, `IBackgroundWorker`, `HangfireBackgroundWorker`, or the controller/endpoint contract, consistent with the arch-review's Option 1 decision and Out-of-Scope section.
- Working tree is otherwise clean except for the pipeline's own `artifacts/feat-3611/state.json`, which is unrelated to this task's code changes.
