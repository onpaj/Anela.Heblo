# Architecture Review: Fix running-invoice-import-jobs filter always returning empty

## Skip Design: true

## Architectural Fit Assessment
This is a single-line predicate bug inside `GetRunningInvoiceImportJobsHandler.Handle` (`backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs:51-55`), fully contained within the Invoices module's own vertical slice. It does not touch `contracts/`, does not cross module boundaries, does not change `BackgroundJobInfo` (a shared Xcc type consumed read-only here), and does not alter the `/api/invoices/import/running-jobs` request/response shape. This is squarely inside the pattern this codebase already uses for MediatR handlers: read from an injected `IBackgroundWorker` abstraction, filter/shape, optionally cache, return. No new component, dependency, or cross-cutting concern is introduced. I verified the causal chain directly in code:

- `InvoiceImportService.ImportInvoicesAsync` (`backend/src/Anela.Heblo.Application/Features/Invoices/Services/InvoiceImportService.cs:37`) carries `[DisplayName("Import faktur: {0}")]`.
- All three job-launch paths funnel through this same method: `EnqueueImportInvoicesHandler.cs:41`, `DailyInvoiceImportCzkJob.cs:60`, `DailyInvoiceImportEurJob.cs:60`.
- `HangfireBackgroundWorker.GetJobDisplayName` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireBackgroundWorker.cs:196-229`) reads the job-parameter `DisplayName` first, then falls back to the `[DisplayName]` attribute with `{0}` substituted — for these jobs this always yields `"Import faktur: <description>"`, never a string containing `"InvoiceImport"`.
- The current filter (`GetRunningInvoiceImportJobsHandler.cs:53-54`) checks `job.JobName.Contains("InvoiceImport", ...)`, which can never match. The endpoint is confirmed dead-on-arrival in production.
- The existing test file (`backend/test/Anela.Heblo.Tests/Features/Invoices/GetRunningInvoiceImportJobsHandlerTests.cs`) mocks `JobName` as `"InvoiceImportJob.Run"` (lines 44, 49, 70, 95, 118), which satisfies the broken filter but does not reflect any string ever produced in production — this is why the bug shipped with green tests.

No architectural conflict, no scope creep needed. The spec's recommendation to fix the predicate in place (rather than plumbing the raw method/type name through `BackgroundJobInfo`/`IBackgroundWorker`) is the correct call for a bug fix of this size — it keeps the change to one file of production code plus its test file.

## Proposed Architecture

### Component Overview
No new components. Existing flow, unchanged shape:

```
InvoicesController (GET /api/invoices/import/running-jobs)
        │
        ▼
GetRunningInvoiceImportJobsHandler.Handle   <-- ONLY file with logic change
        │  calls
        ▼
IBackgroundWorker (HangfireBackgroundWorker)
        │  reads Hangfire storage, resolves JobName via
        │  [DisplayName("Import faktur: {0}")] on InvoiceImportService.ImportInvoicesAsync
        ▼
IList<BackgroundJobInfo>  (JobName == "Import faktur: <description>")
```

The fix is a one-line predicate change; everything upstream (Hangfire, `HangfireBackgroundWorker`, `InvoiceImportService`) and downstream (`InvoicesController`, frontend `useRunningInvoiceImportJobs`) stays untouched.

### Key Design Decisions

#### Decision 1: Match on display-name prefix vs. expose underlying method/type name
**Options considered:**
1. `job.JobName.StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase)` — match the stable, known prefix of the rendered display name.
2. Extend `BackgroundJobInfo` (and `IBackgroundWorker`/`HangfireBackgroundWorker`) with a `MethodName`/`JobType` field populated before `GetJobDisplayName` renders the display string, then filter on that instead of the human-readable name.

**Chosen approach:** Option 1, per the spec.

**Rationale:** Option 2 is more robust against future `[DisplayName]` text changes, but it touches a shared Xcc type (`BackgroundJobInfo`) and the `IBackgroundWorker` contract/implementation — both used elsewhere and outside the Invoices module's ownership — for what is currently a single-consumer need. That's disproportionate to a bug fix scoped as "small, well-scoped." Option 1 confines the change to the one file that owns the bug, matches the project's "surgical changes" convention, and is easy to verify: the display-name format is already pinned by the `[DisplayName]` attribute and is not expected to change casually. The inherent coupling (filter prefix must track the attribute text) is real but minor, and the spec already calls for documenting it. I agree with deferring option 2 to a future issue rather than folding it into this fix.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Modify only:
- `backend/src/Anela.Heblo.Application/Features/Invoices/UseCases/GetRunningInvoiceImportJobs/GetRunningInvoiceImportJobsHandler.cs` — predicate change (lines 50-54) plus updating the stale comment on line 50.
- `backend/test/Anela.Heblo.Tests/Features/Invoices/GetRunningInvoiceImportJobsHandlerTests.cs` — update mocked `JobName` values and add one regression case.

### Interfaces and Contracts
No interface or contract changes. `GetRunningInvoiceImportJobsRequest`, `BackgroundJobInfo`, `IBackgroundWorker`, and the controller action all keep their current signatures. This is purely an internal predicate change inside one handler.

Concrete predicate change:
```csharp
// Filter for invoice import jobs based on the "Import faktur: {0}" DisplayName
// produced by InvoiceImportService.ImportInvoicesAsync (via HangfireBackgroundWorker.GetJobDisplayName).
// NOTE: keep this prefix in sync with the [DisplayName] attribute text if it ever changes.
var invoiceImportJobs = runningJobs
    .Concat(pendingJobs)
    .Where(job => job.JobName != null &&
                  job.JobName.StartsWith("Import faktur:", StringComparison.OrdinalIgnoreCase))
    .ToList();
```

Test updates (per FR-2 acceptance criteria): replace `"InvoiceImportJob.Run"` with realistic `"Import faktur: <description>"` values (e.g. `"Import faktur: faktura 12345"`, `"Import faktur: denní import CZK za 12.07.2026"`) in all five existing tests, keep unrelated job names realistic (e.g. `"Daily Invoice DQT Check"`, `"MetaAds Invoice Import"` — chosen deliberately because they contain "Invoice" and "Import" as substrings but must still be excluded, proving the fix isn't just re-matching a different loose substring), and add a case asserting `"InvoiceImportJob.Run"` (the old, now-provably-wrong string) is excluded as a regression guard.

### Data Flow
Unchanged. `InvoicesController` → `GetRunningInvoiceImportJobsHandler` → `IBackgroundWorker.GetRunningJobs()/GetPendingJobs()` → filter → optional `IMemoryCache` write → response. The only behavioral difference is which jobs survive the `.Where(...)` filter; caching, error handling (catch-and-return-empty-list), and logging are explicitly out of scope and must be left as-is per spec.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Filter prefix silently drifts out of sync if `[DisplayName("Import faktur: {0}")]` text is ever edited | Low | Inline code comment (above) links the two; no automated coupling is warranted for a single string literal — acceptable per spec's Dependencies section. |
| New test job names that loosely resemble "Import faktur:" could accidentally re-introduce a too-broad match | Low | Use `StartsWith` (anchored) rather than `Contains`, and include the specified regression test with unrelated names that share substrings but not the prefix. |
| None — no data migration, no API contract change, no cross-module impact | N/A | N/A |

## Specification Amendments
None. The spec's FR-1/FR-2 acceptance criteria are implementable exactly as written and align with the codebase's existing conventions (xUnit + Moq + FluentAssertions, per `docs/architecture/testing-strategy.md`). One clarification for the implementer: also update the stale inline comment on line 50 of the handler (`// Filter for invoice import jobs based on job name containing "InvoiceImport"`) since it describes the old, incorrect logic — this is a same-line, in-scope edit, not a separate concern.

## Prerequisites
None. No migrations, no config, no infrastructure changes. The fix is deployable standalone; existing `HangfireOptions.RunningJobsCacheSeconds` configuration and Hangfire setup require no changes.
