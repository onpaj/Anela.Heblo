# Specification: Fix running-invoice-import-jobs filter always returning empty

## Summary
`GetRunningInvoiceImportJobsHandler` is supposed to surface currently running/queued invoice import Hangfire jobs to the frontend, but its name filter checks for a substring (`"InvoiceImport"`) that never appears in the real job display name (`"Import faktur: {description}"`). As a result the endpoint always returns an empty list in production, even while an import is actively running. This is a small, targeted bug fix: correct the filter predicate and align the unit tests with the real production display name.

## Background
Invoice import jobs are executed via `IInvoiceImportService.ImportInvoicesAsync`, which is decorated with `[DisplayName("Import faktur: {0}")]` (`backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs:37`). Every code path that starts an import — the manual `EnqueueImportInvoicesHandler` (`backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/EnqueueImportInvoices/EnqueueImportInvoicesHandler.cs:40-41`) and the two recurring jobs `DailyInvoiceImportCzkJob` / `DailyInvoiceImportEurJob` — ultimately calls this same method, so in Hangfire the job's display name is always resolved by `HangfireBackgroundWorker.GetJobDisplayName` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs:179-229`) to the `[DisplayName]` attribute value with `{0}` substituted by the description argument, e.g. `"Import faktur: denní import CZK za 12.07.2026"` or `"Import faktur: faktura 12345"`.

`GetRunningInvoiceImportJobsHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs:51-55`) filters `runningJobs.Concat(pendingJobs)` on `job.JobName.Contains("InvoiceImport", StringComparison.OrdinalIgnoreCase)`. Because the real display name is the Czech string `"Import faktur: ..."`, it never contains the substring `"InvoiceImport"`, so the filter always evaluates to false and the handler silently returns `[]`. The unit tests in `GetRunningInvoiceImportJobsHandlerTests` mock `JobName` as `"InvoiceImportJob.Run"`, which happens to satisfy the broken filter — masking the bug and giving false confidence that the feature works.

The consequence is user-visible: `InvoicesController`'s `/api/invoices/import/running-jobs` endpoint (backed by this handler) is polled every 5 seconds by the frontend's `useRunningInvoiceImportJobs` hook, which drives `InvoiceImportRunningIndicator` / `InvoiceImportJobTracker`. These components never show a running job, so operators get no feedback that a (potentially multi-minute) import is in progress or queued.

## Functional Requirements

### FR-1: Filter must match the real production job display name
`GetRunningInvoiceImportJobsHandler` must correctly identify invoice-import jobs among all running/pending Hangfire jobs, using the actual display name format produced by `[DisplayName("Import faktur: {0}")]` on `InvoiceImportService.ImportInvoicesAsync`.

Recommended implementation (per the brief's suggested fix): change the predicate to match the literal, stable prefix of the display name:
```csharp
.Where(job => job.JobName != null &&
              job.JobName.StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase))
```
This is preferred over the brief's "better" alternative (exposing the underlying method name on `BackgroundJobInfo`) because it requires no change to `BackgroundJobInfo`, `IBackgroundWorker`, or `HangfireBackgroundWorker` — keeping the fix surgical and confined to the one handler that has the bug. See Open Questions for confirmation of this scope decision.

**Acceptance criteria:**
- A running or pending Hangfire job whose `JobName` is `"Import faktur: <any description>"` (matching what `HangfireBackgroundWorker.GetJobDisplayName` actually produces for `ImportInvoicesAsync`, including the case where `{0}` is substituted with any description string, e.g. `"faktura 12345"`, `"denní import CZK za 12.07.2026"`, or `"obecný import"`) is included in the result of `GetRunningInvoiceImportJobsHandler.Handle`.
- A job whose `JobName` is unrelated (e.g. `"Daily Invoice DQT Check"`, `"MetaAds Invoice Import"`, or any other background job's display name) is excluded from the result.
- The match is case-insensitive (matching the existing `OrdinalIgnoreCase` usage) and null-safe (a `null` `JobName` does not throw and is excluded).
- Existing caching, error-handling (catch-and-return-empty-list on worker exception), and logging behavior in the handler are unchanged.

### FR-2: Align unit tests with real production job naming
Update `GetRunningInvoiceImportJobsHandlerTests` so its mocked `JobName` values reflect the actual Hangfire display name format (`"Import faktur: ..."`) instead of the synthetic `"InvoiceImportJob.Run"` string, so the tests genuinely validate production behavior rather than accidentally passing against the buggy filter.

**Acceptance criteria:**
- `Handle_FiltersToInvoiceImportJobsOnly` uses job names of the form `"Import faktur: <description>"` for jobs expected to be included, and realistic unrelated names (not merely renamed variants that still coincidentally match) for jobs expected to be excluded.
- All other existing tests in the file (`Handle_CacheHit_DoesNotCallWorkerSecondTime`, `Handle_CacheDisabled_CallsWorkerOnEveryInvocation`, `Handle_CacheDisabled_DoesNotWriteToCache`, `Handle_WorkerThrows_ReturnsEmptyListAndDoesNotCache`) continue to pass with updated job names consistent with FR-1's matching rule.
- A new or extended test case asserts that a job name using the old broken-filter-only string (e.g. `"InvoiceImportJob.Run"`, which does *not* start with `"Import faktur:"`) is correctly excluded — guarding against regressing back to matching on an arbitrary unrelated substring.
- Test suite (`dotnet test` for the affected project, or at minimum the `GetRunningInvoiceImportJobsHandlerTests` class) passes.

## Non-Functional Requirements

### NFR-1: Performance
No change. The fix only alters a string-comparison predicate evaluated over an already-small in-memory list of running/pending jobs; no additional I/O or complexity is introduced. Existing `HangfireOptions.RunningJobsCacheSeconds`-based caching is untouched.

### NFR-2: Security
No change. No new inputs, no new attack surface. The fix does not touch authentication/authorization on the `/api/invoices/import/running-jobs` endpoint.

## Data Model
N/A — no changes to entities, DTOs, or persistence. `BackgroundJobInfo` (`backend/src/Anela.Heblo.Xcc/Services/BackgroundJobInfo.cs`) is unchanged.

## API / Interface Design
N/A — no changes to the endpoint contract. `GET /api/invoices/import/running-jobs` keeps its existing request/response shape (`IList<BackgroundJobInfo>`); only the server-side filtering logic that determines which jobs populate that list changes. No frontend changes are required — `useRunningInvoiceImportJobs`, `InvoiceImportRunningIndicator`, and `InvoiceImportJobTracker` will start receiving non-empty results once the backend filter is fixed, with no code changes needed on their side.

## Dependencies
- Hangfire (existing dependency, unchanged).
- No new external services or libraries.
- Depends on the `[DisplayName("Import faktur: {0}")]` attribute on `InvoiceImportService.ImportInvoicesAsync` remaining as-is; if that attribute's text is ever changed, the filter prefix in `GetRunningInvoiceImportJobsHandler` must be updated in lockstep (this coupling is inherent to the chosen fix and is called out for future maintainers, e.g. via a code comment).

## Out of Scope
- Refactoring `BackgroundJobInfo` / `IBackgroundWorker` / `HangfireBackgroundWorker` to expose the underlying job method name or type as a more robust, display-name-independent way to identify invoice import jobs (the brief's "better" alternative). This is a reasonable follow-up but is a larger architectural change than this bug fix warrants.
- Any changes to the `[DisplayName]` text itself, localization, or other job display names.
- Frontend changes — no frontend code is expected to require modification.
- Broader review of other places in the codebase that may filter/match Hangfire jobs by name.

## Open Questions
None — the brief's suggested fix (matching on the `"Import faktur:"` prefix) is adopted as the primary approach since it is minimal and scoped to the single handler with the bug; the "better" alternative of exposing job type/method name is explicitly deferred to Out of Scope per the "small, well-scoped bug fix" framing of this task.

## Status: COMPLETE
