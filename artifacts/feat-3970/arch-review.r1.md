# Architecture Review: Remove Hangfire `[AutomaticRetry]` Leak from ProductPairingDqtJob

## Skip Design: true
Pure backend attribute/import removal on one existing class. No new or changed API, UI, screen, or visual component.

## Architectural Fit Assessment
This aligns cleanly with existing patterns and requires no new abstractions. Verified directly against the codebase:

- All four DQT jobs live in `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/` and implement `IRecurringJob` (`Anela.Heblo.Domain.Features.BackgroundJobs.IRecurringJob`), with an identical shape: check `IRecurringJobStatusChecker.IsJobEnabledAsync`, build a `DqtRun` via `DqtRun.Start(...)`, persist it through `IDqtRunRepository`, then delegate to a per-test-type runner (`IDriftDqtJobRunner`, `IInvoiceDqtJobRunner`, etc.).
- `InvoiceDqtJob`, `StockWriteBackDqtJob`, `LotStockReconciliationDqtJob` — confirmed via direct read — have **no** `using Hangfire;` and **no** Hangfire attributes. `ProductPairingDqtJob` is the sole outlier in this module.
- Hangfire registration for all recurring jobs is centralized in `HangfireJobRegistrationHelper.RegisterOrUpdate` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs`), which calls `RecurringJob.AddOrUpdate<TJob>(jobName, job => job.ExecuteAsync(default), cron, options)` reflectively for any `IRecurringJob`. This mechanism is attribute-agnostic — it works identically whether or not the target method carries `[AutomaticRetry]`. Removing the attribute requires **no** change to this registration path.
- `Program.cs` registers exactly one `GlobalJobFilters.Filters.Add(...)` (`HangfireJobFailureTelemetryFilter`) plus one more (`HangfireJobActivityFilter` in `ServiceCollectionExtensions.AddHangfireServices`). Neither overrides Hangfire's built-in default retry count. Confirmed: no global override of `AutomaticRetryAttribute.Attempts` exists anywhere in the app. This means today, without a per-method attribute, a job gets Hangfire's built-in default (10 retries with increasing delay).

**Important scoping finding, not present in the original issue body:** `[AutomaticRetry]` directly on an Application-layer job class is **not** an isolated leak unique to this file — it is an established, repeated pattern elsewhere in this exact codebase:

| File | Module | Attribute |
|---|---|---|
| `Features/MeetingTasks/Infrastructure/Jobs/PlaudPollingJob.cs` | MeetingTasks | `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` |
| `Features/MindMaps/Infrastructure/Jobs/MindMapUpdateJob.cs` | MindMaps | `[AutomaticRetry(Attempts = 10)]` |
| `Features/Attendance/Infrastructure/Jobs/BreakInsertionJob.cs` | Attendance | `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` |
| `Features/Catalog/Infrastructure/Jobs/ProductExportDownloadJob.cs` | Catalog | `[AutomaticRetry(Attempts = 0)]` |
| `Features/Article/UseCases/Generate/GenerateArticleJob.cs` | Article | `[AutomaticRetry(Attempts = 0)]` |

`ProductExportDownloadJobTests.Job_HasAutomaticRetryAttribute_WithZeroAttempts` even asserts the attribute's presence via `GetCustomAttributesData()` reflection — this is a *tested, intentional* convention elsewhere, not dead weight. `ModuleBoundariesTests.cs` (the repo's architecture-fitness-test suite) scans attribute types among other referenced types per class and enforces a `forbiddenPrefixes` list per module, but has **no entry forbidding the `Hangfire` namespace** from Application-layer code — confirmed by reading the file; no rule is being violated today.

**Conclusion:** the correct scope for this fix is exactly what the issue asked for — bring `ProductPairingDqtJob` in line with its three DQT siblings in-module — and nothing more. Treating this as license to also purge the five other jobs, or to invent a new registration-time retry mechanism, would be a much larger, unrequested change that contradicts an established, tested, and currently-unforbidden codebase convention. The `spec.r1.md`'s Out of Scope section already reflects this correctly; this review confirms and grounds it in the actual code.

## Proposed Architecture

### Component Overview
No new components. One existing file is edited:

```
Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/
├── InvoiceDqtJob.cs                    (no Hangfire — unchanged)
├── StockWriteBackDqtJob.cs             (no Hangfire — unchanged)
├── LotStockReconciliationDqtJob.cs     (no Hangfire — unchanged)
└── ProductPairingDqtJob.cs             (Hangfire import + attribute REMOVED)
```

Hangfire registration (`HangfireJobRegistrationHelper`, `RecurringJobDiscoveryService`, `HangfireRecurringJobScheduler` — all in `Anela.Heblo.API/Infrastructure/Hangfire/`) is untouched; it already treats all `IRecurringJob` implementations uniformly regardless of attributes on their `ExecuteAsync` method.

### Key Design Decisions

#### Decision 1: Delete the attribute in place; do not relocate it to a registration-time filter or wrapper type
**Options considered:**
1. Delete `using Hangfire;` and `[AutomaticRetry(...)]` from `ProductPairingDqtJob` outright (matches the three siblings exactly).
2. Preserve the "no retry, fail fast" behavior by moving retry configuration to registration time — e.g. a custom `IApplyStateFilter`/`JobFilterAttribute` registered via `GlobalJobFilters.Filters.Add(...)` scoped to this one job, or a thin `API`/`Infrastructure`-layer wrapper type carrying the attribute that Hangfire invokes instead of the Application-layer class directly.
3. Leave `ProductPairingDqtJob` as-is and instead add `[AutomaticRetry(Attempts = 0, ...)]` to the other three DQT jobs for the opposite direction of consistency.

**Chosen approach:** Option 1.

**Rationale:**
- Option 2 invents a new pattern nothing else in the codebase uses for per-job retry policy. Every existing precedent (5 files, table above) configures retry via a direct attribute on the Application-layer job class — introducing a registration-time filter or wrapper type for this one job would itself be a fresh architectural inconsistency, not a fix for one. It also adds a new type and indirection for no functional gain, which conflicts with this project's "surgical changes" convention (no unrequested abstractions).
- Option 3 (add the attribute to the other three jobs instead) was considered and rejected: nothing in `InvoiceDqtJob`, `StockWriteBackDqtJob`, or `LotStockReconciliationDqtJob`'s logic needs zero-retry-fail-fast semantics, there is no evidence the original filed issue wants three files touched instead of one, and it moves in the opposite direction from what was actually requested (issue explicitly asks to *remove* the leak from `ProductPairingDqtJob`, treating it as the outlier).
- Option 1 is the literal, minimal fix requested by the issue, it produces zero new files/types, and it makes `ProductPairingDqtJob` byte-for-byte structurally identical to its three siblings with respect to Hangfire references (FR-2 in the spec).

#### Decision 2: Accept the retry-semantics change; do not attempt to preserve zero-retry behavior
**Options considered:**
1. Accept that the job moves from "0 retries, fail immediately" to Hangfire's default (10 retries with backoff) — same as its three siblings already run under.
2. Investigate and preserve the original zero-retry intent under a different mechanism (see Decision 1, Option 2, rejected above).

**Chosen approach:** Option 1.

**Rationale:** All four DQT jobs share the identical "persist a `DqtRun`, then delegate to a runner" execution shape (verified by reading all four source files). Nothing in `IDriftDqtJobRunner.RunAsync` vs. the other three runners' interfaces suggests `ProductPairingDqtJob` is uniquely unsafe to retry — if retries were unsafe for this job (e.g. because retrying would create duplicate `DqtRun` rows), they would be equally unsafe for the three siblings that already have no retry protection today. No code comment, commit history marker, or test documents an intentional reason for the zero-retry policy on this one job. Given that, disabling retries here reads as accidental (likely copy-pasted from an unrelated job like `ProductExportDownloadJob`, which legitimately needs `Attempts=0` because a failed run leaves a partial download artifact) rather than a deliberate choice for this specific job.

## Implementation Guidance

### Directory / Module Structure
No new files, no new directories. Single-file edit:
- `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs`

### Interfaces and Contracts
Unchanged: `IRecurringJob`, `RecurringJobMetadata`, `IDqtRunRepository`, `IDriftDqtJobRunner`, `IRecurringJobStatusChecker`, `TimeProvider`, `ILogger<ProductPairingDqtJob>`. The class's public shape (constructor signature, `Metadata` property, `ExecuteAsync` signature) does not change — only the attribute decorating `ExecuteAsync` and the now-unused `using Hangfire;` line are removed.

### Data Flow
Unaffected. Hangfire still discovers and schedules `daily-product-pairing-dqt` exactly as before via `HangfireJobRegistrationHelper.RegisterOrUpdate` → `RecurringJob.AddOrUpdate<ProductPairingDqtJob>(...)`. The only change is which state-transition filters Hangfire applies when a run of this specific job fails: previously the method-level `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` filter fired; after this change, no method-level filter is present, so Hangfire's built-in default `AutomaticRetryAttribute` (10 attempts) applies — the same as it already does for the other three DQT jobs today.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Unknown operational reason existed for zero-retry-fail-fast on this specific job (e.g. undocumented duplicate-`DqtRun`-on-retry concern) that this review did not surface from the code alone | Low | Spec's Open Questions section flags this explicitly for a human sanity check before merge; no code evidence found for it across any of the four DQT jobs, all of which share the identical persist-then-delegate shape |
| A reviewer misreads this fix as license to also strip `[AutomaticRetry]` from the five unrelated jobs listed above | Low | Spec's Out of Scope section and this review both explicitly call out and reject that broader change |
| `dotnet format` or an unused-`using` analyzer flags something unexpected after the import removal | Very Low | Standard `dotnet build` + `dotnet format` validation step (already required by project CLAUDE.md) catches this before merge |

## Specification Amendments
None. `spec.r1.md` already correctly scoped this to a single-file change with the retry-semantics consequence called out as accepted, not a defect; this review independently confirms that scoping against the actual codebase (registration mechanism, global filters, the five-file wider pattern, and the absence of any enforced architecture-fitness rule against it) rather than taking the issue's "Suggested fix" (registration-time `JobFilterAttribute`) at face value. The registration-time relocation suggested by the original issue text is not the recommended approach — see Decision 1 above.

## Prerequisites
None. No migrations, no config, no infrastructure changes. This can be implemented and merged independently of any other in-flight work.
