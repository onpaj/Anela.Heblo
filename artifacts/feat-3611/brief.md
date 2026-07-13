## Module
Invoices

## Finding
`GetRunningInvoiceImportJobsHandler` (`backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs:51-55`) filters the running/pending job list with:

```csharp
.Where(job => job.JobName != null &&
              job.JobName.Contains("InvoiceImport", StringComparison.OrdinalIgnoreCase))
```

`BackgroundJobInfo.JobName` is populated by `HangfireBackgroundWorker.GetJobDisplayName` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs:179-229`). That method reads the `[DisplayName]` attribute from the job method before falling back to the method name. `InvoiceImportService.ImportInvoicesAsync` carries:

```csharp
[DisplayName("Import faktur: {0}")]   // line 37 of InvoiceImportService.cs
```

So in production, `JobName` is always `"Import faktur: "` (Czech). That string does **not** contain the substring `"InvoiceImport"`, so the filter is always false and the handler silently returns an empty list — even when import jobs are actively running or queued.

The unit tests in `GetRunningInvoiceImportJobsHandlerTests` use a synthetic name `"InvoiceImportJob.Run"` (line 43 and elsewhere), which does match the filter. This masks the mismatch: the tests pass but the feature is broken in production.

## Why it matters
The `/api/invoices/import/running-jobs` endpoint always returns `[]`. The frontend `InvoiceImportRunningIndicator` / `InvoiceImportJobTracker` components that poll this endpoint (`useRunningInvoiceImportJobs`, `refetchInterval: 5000`) will never show a running job, giving operators no visual signal during long batch imports.

## Suggested fix
Align the filter with the actual job display name. The simplest fix is to match on a substring that really appears in the Czech display name:

```csharp
.Where(job => job.JobName != null &&
              job.JobName.StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase))
```

Or — better — move the filter to the job's `Type`/method name (available inside `HangfireBackgroundWorker` before `GetJobDisplayName` strips it) and expose it on `BackgroundJobInfo`. A short-term alternative is also to add a second fallback prefix matching `"ImportInvoicesAsync"` for when no `DisplayName` attribute is found.

Update the unit tests to use `"Import faktur: ..."` as the mocked `JobName` so they validate real production behaviour.

---
_Filed by daily arch-review routine on 2026-07-13._
