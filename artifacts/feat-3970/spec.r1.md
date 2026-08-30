# Specification: Remove Hangfire `[AutomaticRetry]` Leak from ProductPairingDqtJob

## Summary
`ProductPairingDqtJob` (Application layer, `DataQuality` module) currently imports `Hangfire` and applies a Hangfire-specific `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` attribute directly to its `ExecuteAsync` method. The other three DQT jobs in the same module (`InvoiceDqtJob`, `StockWriteBackDqtJob`, `LotStockReconciliationDqtJob`) carry no Hangfire references at all. This spec covers bringing `ProductPairingDqtJob` in line with its three module siblings by removing the Hangfire import and attribute, while explicitly preserving the job's other behavior and documenting the resulting change in retry semantics.

## Background
Recurring jobs in this codebase live under `Anela.Heblo.Application/Features/{Module}/Infrastructure/Jobs/` and implement `IRecurringJob` (`Anela.Heblo.Domain.Features.BackgroundJobs`). Hangfire discovers and schedules them via `HangfireJobRegistrationHelper.RegisterOrUpdate` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/HangfireJobRegistrationHelper.cs`), which calls `RecurringJob.AddOrUpdate<TJob>(...)`.

Within the `DataQuality` module specifically, `ProductPairingDqtJob` is the only one of four DQT jobs that references Hangfire types. All four jobs share an identical execution shape: check `IRecurringJobStatusChecker`, create and persist a `DqtRun` via `IDqtRunRepository`, then delegate the actual comparison work to a runner service (`IDriftDqtJobRunner` / `IInvoiceDqtJobRunner` / etc.). There is nothing about `ProductPairingDqtJob`'s logic that structurally differs from its siblings or that would justify a uniquely different retry policy — the `[AutomaticRetry(Attempts = 0, ...)]` attribute appears to be either copy-pasted from an unrelated job (e.g. `ProductExportDownloadJob`, which legitimately needs zero retries because it produces a downloadable file to a Hangfire storage path) or added ad hoc without corresponding intent recorded elsewhere in this module.

Note for the architecture phase: this same `[AutomaticRetry]`-on-Application-layer-job pattern is used deliberately elsewhere in the codebase (e.g. `PlaudPollingJob`, `MindMapUpdateJob`, `BreakInsertionJob`, `ProductExportDownloadJob`, `GenerateArticleJob`), including at least one test (`ProductExportDownloadJobTests.Job_HasAutomaticRetryAttribute_WithZeroAttempts`) that asserts the attribute's presence via reflection. No architecture test (`ModuleBoundariesTests`) currently forbids Hangfire references from the Application layer's `Infrastructure/Jobs` folders. This spec deliberately does **not** propose changing that wider, established convention — see Out of Scope.

## Functional Requirements

### FR-1: Remove the Hangfire attribute and import from `ProductPairingDqtJob`
Remove `using Hangfire;` and the `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` attribute from `backend/src/Anela.Heblo.Application/Features/DataQuality/Infrastructure/Jobs/ProductPairingDqtJob.cs`. No other code in the class changes — constructor, `Metadata`, and the body of `ExecuteAsync` remain byte-for-byte identical.

**Acceptance criteria:**
- `ProductPairingDqtJob.cs` contains no `using Hangfire;` and no `[AutomaticRetry(...)]` attribute.
- `ProductPairingDqtJob.cs` is otherwise unchanged (same constructor signature, same `Metadata`, same `ExecuteAsync` body/logic).
- The class still implements `IRecurringJob` and still compiles and registers with Hangfire exactly as before (via `HangfireJobRegistrationHelper.RegisterOrUpdate`, unchanged).

### FR-2: Module-level consistency
After the change, all four DQT jobs (`InvoiceDqtJob`, `StockWriteBackDqtJob`, `LotStockReconciliationDqtJob`, `ProductPairingDqtJob`) have no Hangfire references and no Hangfire-specific attributes — they are structurally identical in this respect.

**Acceptance criteria:**
- `grep -rn "Hangfire" backend/src/Anela.Heblo.Application/Features/DataQuality/` returns no matches.

### FR-3: Existing tests continue to pass unmodified
`ProductPairingDqtJobTests` (`backend/test/Anela.Heblo.Tests/Features/DataQuality/ProductPairingDqtJobTests.cs`) does not assert on the Hangfire attribute and requires no changes.

**Acceptance criteria:**
- All four existing tests in `ProductPairingDqtJobTests` pass unmodified after the change.

## Non-Functional Requirements

### NFR-1: Behavioral consequence — retry semantics change (accepted)
Removing `[AutomaticRetry(Attempts = 0, OnAttemptsExceeded = AttemptsExceededAction.Fail)]` means `daily-product-pairing-dqt` no longer explicitly disables Hangfire's automatic retries. With no attribute present and no matching `GlobalJobFilters.Filters` override registered in `Program.cs` (confirmed: none exists for `AutomaticRetryAttempts`), the job falls back to Hangfire's built-in default of up to 10 automatic retries with increasing delay on failure, instead of failing immediately after the first failed attempt.

This is the same retry behavior the other three DQT jobs already run under today, so it is a net *increase in consistency*, not a novel risk being introduced into the module. It is explicitly accepted as part of this fix (see Open Questions for the one item worth a human sanity check before merge).

### NFR-2: No behavior change beyond retry semantics
The job's success-path logic (status check, `DqtRun` creation/persistence, delegation to `IDriftDqtJobRunner`) must be unchanged. This is a structural/attribute-only change.

## Data Model
No data model changes. `DqtRun`, `DqtTestType`, `DqtTriggerType`, `DqtRunStatus` are unaffected.

## API / Interface Design
No public interface, contract, or endpoint changes. `IRecurringJob`, `RecurringJobMetadata`, and the job's constructor signature are unchanged. The Hangfire recurring job name (`daily-product-pairing-dqt`), cron expression, and registration mechanism (`HangfireJobRegistrationHelper.RegisterOrUpdate`) are unaffected — Hangfire discovers and schedules the job the same way regardless of whether `[AutomaticRetry]` is present on the invoked method.

## Dependencies
- `Hangfire` NuGet package reference on `Anela.Heblo.Application.csproj` — **not removed** by this change, since five other Application-layer job classes in unrelated modules still use `[AutomaticRetry]` directly (see Background / Out of Scope). Only the `using Hangfire;` import inside `ProductPairingDqtJob.cs` itself is removed.

## Out of Scope
- Removing `[AutomaticRetry]` / `using Hangfire;` from the five other Application-layer job classes that use the same pattern outside the `DataQuality` module (`PlaudPollingJob`, `MindMapUpdateJob`, `BreakInsertionJob`, `ProductExportDownloadJob`, `GenerateArticleJob`). That is a separate, much larger, repo-wide architectural change and is not what this issue was filed against.
- Introducing a registration-time mechanism (e.g. a custom `JobFilterAttribute` wired up via `GlobalJobFilters.Filters.Add(...)`, or a thin infrastructure-layer wrapper type) to express per-job retry policy outside the Application layer. Given the wider codebase's established convention of attribute-on-job-class, and that this change removes the only Hangfire reference in this module rather than relocating it, no replacement mechanism is needed for this specific job.
- Any change to the `Hangfire` package reference on `Anela.Heblo.Application.csproj`.
- Any change to the `daily-product-pairing-dqt` cron schedule, job name, or enablement flag.

## Open Questions
None. (Note for a human reviewer, not a blocking question: this change intentionally lets `daily-product-pairing-dqt` retry up to 10 times on failure instead of failing immediately, matching its three DQT siblings. If there was an undocumented operational reason `ProductPairingDqtJob` specifically needed zero retries — e.g. a known non-idempotency in `IDriftDqtJobRunner.RunAsync` under Hangfire retry — flag it before merge. No such reason was found in the code: all four DQT jobs share the identical "persist `DqtRun` then delegate to a runner" shape, so retries are equally (non-)safe across all four.)

## Status: COMPLETE
