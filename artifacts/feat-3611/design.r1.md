# Design: Fix running-invoice-import-jobs filter always returning empty

## Component Design

**`GetRunningInvoiceImportJobsHandler`** (`backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs`) is the sole component affected. Its responsibility is unchanged: given the current set of running and pending Hangfire jobs (via `IBackgroundWorker`), return only those that are invoice-import jobs, using the optional in-memory cache to avoid hitting `IBackgroundWorker` on every call within `HangfireOptions.RunningJobsCacheSeconds`.

The only change is the filter contract used to decide whether a `BackgroundJobInfo` represents an invoice-import job:

- **Old (broken) contract:** `job.JobName.Contains("InvoiceImport", StringComparison.OrdinalIgnoreCase)` — never matches because the real display name is `"Import faktur: {description}"`.
- **New contract:** `job.JobName != null && job.JobName.StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase)` — matches the actual display name produced by `[DisplayName("Import faktur: {0}")]` on `InvoiceImportService.ImportInvoicesAsync`, is null-safe, and is case-insensitive.

The inline comment above the filter must be updated to describe this new predicate (it currently describes the old, incorrect logic). Everything else in the handler — dependency on `IBackgroundWorker`, caching behavior, catch-and-return-empty-list error handling, and logging — is unchanged.

No other component (`InvoicesController`, `IBackgroundWorker`/`HangfireBackgroundWorker`, `BackgroundJobInfo`, frontend `useRunningInvoiceImportJobs`) is touched.

## Data Schemas

N/A — no changes to entities, DTOs, persistence, or the API request/response shape. `GET /api/invoices/import/running-jobs` continues to return `IList<BackgroundJobInfo>` unchanged; only the server-side predicate that selects which jobs populate that list is corrected.
