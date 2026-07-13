# Implementation: fix-running-invoice-import-jobs-filter

## What was implemented
Fixed the job-name filter in `GetRunningInvoiceImportJobsHandler` that always returned an empty list in production. The predicate previously checked `job.JobName.Contains("InvoiceImport", ...)`, but the real Hangfire display name (from `[DisplayName("Import faktur: {0}")]` on `InvoiceImportService.ImportInvoicesAsync`) is always of the form `"Import faktur: <description>"`, which never contains that substring. Changed the predicate to `job.JobName.StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase)` and updated the stale inline comment. Updated the handler's unit tests to use realistic job names matching production, including a regression test asserting the old broken string (`"InvoiceImportJob.Run"`) is correctly excluded.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs` — corrected filter predicate and comment
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetRunningInvoiceImportJobsHandlerTests.cs` — updated mocked job names to `"Import faktur: ..."` format across all 5 tests; added a regression case (`"InvoiceImportJob.Run"` and other unrelated names) proving they are excluded

## Tests
`GetRunningInvoiceImportJobsHandlerTests` — 5 tests covering: filtering to only invoice-import jobs (including the old-broken-string regression case), cache hit behavior, cache-disabled behavior (always calls worker, never writes to cache), and worker-exception handling (returns empty list, does not cache).

## How to verify
```bash
cd backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetRunningInvoiceImportJobsHandlerTests"
```
Result: build succeeds (0 errors), all 5 tests pass.

## Notes
The developer subagent that started this task wrote the test-file changes but stalled before applying the handler fix or committing (it appears to have gotten stuck waiting on a long-running test invocation). The orchestrating session completed the remaining step (the handler predicate/comment fix), ran `dotnet build`, `dotnet format`, and the affected test suite, and made the commit. No deviations from the plan — same fix, same test approach as specified in `task-context/fix-running-invoice-import-jobs-filter.md`.

## PR Summary
`GetRunningInvoiceImportJobsHandler`'s job-name filter checked for a substring (`"InvoiceImport"`) that never appears in the real production Hangfire display name (`"Import faktur: ..."`), so `/api/invoices/import/running-jobs` always returned an empty list and the frontend's running-import indicator never showed anything. This fixes the predicate to match the real display-name prefix and updates the unit tests — which previously used a synthetic job name that happened to satisfy the broken filter — to use realistic names, with a regression test guarding against reintroducing an arbitrary-substring match.

### Changes
- `GetRunningInvoiceImportJobsHandler.cs` — `Contains("InvoiceImport", ...)` → `StartsWith("Import faktur:", ...)`
- `GetRunningInvoiceImportJobsHandlerTests.cs` — realistic job names + regression test

## Status
DONE
