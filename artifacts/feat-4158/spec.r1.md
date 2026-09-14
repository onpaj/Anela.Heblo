# Specification: Bank staleness warning logged twice per scheduled job run

## Summary
Scheduled bank-import jobs (`ComgateCzkImportJob`, `ShoptetPayImportJob`, and any other subclass of `BankImportJobBase`) currently emit the "watermark is stale" warning twice for the same import run: once from `BankImportJobBase.ResolveDateFromAsync` and once from `ImportBankStatementHandler.Handle`, both evaluating the identical `daysBehind > StaleWarningDays` condition against the same `BankImportState`. This duplication causes false double-alerts in log-based monitoring and misplaces a policy decision (is the watermark stale?) inside a handler whose job is to execute an import for a given date range. The fix removes the redundant check from the handler while preserving staleness detection on the manual-API trigger path, which does not go through the job and would otherwise lose the warning entirely.

## Background
`BankImportJobBase.ExecuteAsync` is the entry point for all scheduled bank-import recurring jobs. Before invoking `IMediator.Send(new ImportBankStatementRequest(...))`, it calls `ResolveDateFromAsync`, which computes `dateFrom` from the stored watermark and, when the gap since the last successful import (`span`) exceeds `_options.StaleWarningDays` (and is not already past `_options.MaxBackfillDays`, which logs an Error instead), logs a Warning.

`ImportBankStatementHandler.Handle` is the MediatR handler invoked both by the job (`BankImportJobBase.ExecuteAsync`) and by the manual API trigger (`BankStatementsController`, confirmed at `backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs:48`, which constructs and sends `ImportBankStatementRequest` directly). The handler independently loads `BankImportState` for the account and, if `LastValidImportDate` is stale by the same `StaleWarningDays` threshold, logs its own Warning with a differently-formatted message.

Because both the job and the handler read the same watermark state and apply the same threshold, every job-triggered run logs the staleness warning twice — once with job-specific context (`JobName`, computed date range) and once with handler-specific context (`AccountName`, `LastValidDate`). This breaks log-based alerting (double-counts alerts) and log aggregation (two message shapes for one condition). The handler's check is also the *only* path that provides this warning for the manual-trigger case, so it cannot simply be deleted without another change if staleness detection is still wanted for manual triggers — but per the issue's business framing, only the job-triggered duplication needs to be fixed.

## Functional Requirements

### FR-1: Remove the duplicate staleness check from `ImportBankStatementHandler.Handle`
Delete the `if (state.LastValidImportDate.HasValue) { ... daysBehind > _watermarkOptions.StaleWarningDays ... LogWarning(...) }` block (current lines 70–77) from `ImportBankStatementHandler.Handle`. The handler continues to load `state` (it is still needed later for `RecordSuccess`/`RecordFailure`/`UpsertAsync`) but no longer evaluates or logs staleness.

**Acceptance criteria:**
- A job-triggered import run (through `BankImportJobBase.ExecuteAsync`) that has a stale watermark logs the staleness warning exactly once, from `BankImportJobBase.ResolveDateFromAsync`, not from the handler.
- `ImportBankStatementHandler.Handle` no longer references `_watermarkOptions.StaleWarningDays` for a logging decision inside `Handle`.
- The handler's existing behavior (fetching statements, deduping, persisting, recording success/failure, returning `BankStatementImportResultDto`) is unchanged.

### FR-2: Remove the now-unused `_watermarkOptions` dependency if nothing else in the handler uses it
After FR-1, check whether `BankImportWatermarkOptions` (injected as `IOptions<BankImportWatermarkOptions> watermarkOptions` / stored as `_watermarkOptions`) is referenced anywhere else in `ImportBankStatementHandler`. If not, remove the constructor parameter, the field, and the assignment, and update the DI registration/call sites (including any test doubles/mocks that construct `ImportBankStatementHandler` directly) accordingly.

**Acceptance criteria:**
- No unused field/parameter remains in `ImportBankStatementHandler` if `BankImportWatermarkOptions` has no other use inside the class.
- If `BankImportWatermarkOptions` is still needed for another purpose in the handler, it is left in place and this requirement does not apply.
- All call sites constructing `ImportBankStatementHandler` (production DI and test setup) compile against the updated constructor signature.

### FR-3: Preserve exactly one staleness warning for the manual-API trigger path, OR explicitly accept its loss
`BankStatementsController` invokes `ImportBankStatementHandler` directly via MediatR, bypassing `BankImportJobBase` entirely. After FR-1, a manual-trigger import with a stale watermark logs **no** staleness warning at all (previously it logged exactly one, from the handler). Two acceptable resolutions:

- **(a) Preferred, per the issue's suggested fix:** Add a staleness check to the manual-trigger path only, without reintroducing it into the handler — e.g. in `GetBankStatementListRequestValidator` (if it runs before/around the manual trigger and has access to the watermark) or in `BankStatementsController` itself, using the same `BankImportWatermarkOptions.StaleWarningDays` threshold and the same watermark source (`IBankImportStateRepository`). This keeps the check as a single, explicit decision made by whichever caller-context actually needs it, never duplicated.
- **(b) Accept the gap:** Do not add a replacement check anywhere; the manual-trigger path silently loses staleness warning logging. This is acceptable only if the manual-trigger path is not relied on for staleness monitoring (it is an ad hoc/manual operation, not a scheduled unattended run).

This specification defers the choice between (a) and (b) to the architect/planner — see Open Questions. The **minimum required change is FR-1**; FR-3 is a follow-on scope decision.

**Acceptance criteria (only if (a) is chosen):**
- A manual-trigger import (via `BankStatementsController` / `POST` endpoint that constructs `ImportBankStatementRequest` directly) with a stale watermark still logs exactly one staleness warning, sourced from the new location, not from the handler.
- The new check is not duplicated: it exists in exactly one place for the manual-trigger path, and the handler still does not perform it.

## Non-Functional Requirements

### NFR-1: No behavior change to import execution, watermark state, or return values
Removing the log statement (and, if applicable, the unused options field) must not alter `dateFrom`/`dateTo` handling, statement dedup, persistence, `BankImportState.RecordSuccess`/`RecordFailure`, or the `BankStatementImportResultDto` returned to callers. This is a logging-only / dead-dependency-removal change to the handler.

### NFR-2: Log message consistency
The remaining single staleness warning (from `BankImportJobBase.ResolveDateFromAsync`, and optionally the new manual-trigger check under FR-3a) should not need to change its message format as part of this fix — FR-1 removes a message, it does not need to introduce a new unified one, unless FR-3a is implemented, in which case its message should be clearly distinguishable in intent from the job's (e.g. explicitly noting "manual trigger") to aid log triage.

## Data Model
No data model changes. `BankImportState` (with `LastValidImportDate`) and `BankImportWatermarkOptions` (with `StaleWarningDays`, `MaxBackfillDays`) are read, not modified, by this fix.

## API / Interface Design
No public API surface changes. `ImportBankStatementHandler`'s constructor signature changes only if FR-2 applies (removal of `IOptions<BankImportWatermarkOptions>` parameter) — this is an internal DI-resolved type, not a public contract, so it does not affect the generated OpenAPI client. If FR-3a is implemented via `BankStatementsController`, no new endpoint is added — the existing manual-import endpoint gains internal logging logic only, no request/response shape change.

## Dependencies
- `BankImportJobBase` (`backend/src/Anela.Heblo.Application/Features/Bank/Infrastructure/Jobs/BankImportJobBase.cs`) — unchanged; remains the sole staleness-warning source for job-triggered runs.
- `ImportBankStatementHandler` (`backend/src/Anela.Heblo.Application/Features/Bank/UseCases/ImportBankStatement/ImportBankStatementHandler.cs`) — modified per FR-1/FR-2.
- `BankStatementsController` (`backend/src/Anela.Heblo.API/Controllers/BankStatementsController.cs`) — only touched if FR-3a is chosen.
- `GetBankStatementListRequestValidator` (`backend/src/Anela.Heblo.Application/Features/Bank/Validators/GetBankStatementListRequestValidator.cs`) — only touched if FR-3a is chosen and this is the chosen location.
- Existing test `Handle_LogsStaleWarning_WhenWatermarkIsStale` in `backend/test/Anela.Heblo.Tests/Features/Bank/ImportBankStatementHandlerTests.cs` (~line 370) asserts the handler's current staleness-logging behavior and must be updated or removed as part of this change; a corresponding new test may be needed if FR-3a is implemented.
- `BankImportJobBaseTests` (`backend/test/Anela.Heblo.Tests/Features/Bank/Infrastructure/Jobs/BankImportJobBaseTests.cs`) already covers the job-side staleness warning and needs no change.

## Out of Scope
- Changing `StaleWarningDays` or `MaxBackfillDays` threshold values or semantics.
- Changing the job-side (`BankImportJobBase`) warning message format.
- Any change to the `MaxBackfillDays` / clamping (Error-level) logging path — only the Warning-level staleness duplication is in scope.
- Broader alerting/monitoring configuration changes (e.g. adjusting log-based alert rules) — this fix addresses the duplicate emission at the source, not downstream alert deduplication.

## Open Questions

1. **Should FR-3 option (a) or (b) be implemented?** The issue's "Suggested fix" section mentions option (a) as a possibility ("If the manual-API trigger path also needs the warning, move the check...") but does not mandate it, and frames the core fix as simply removing the handler's duplicate check. Recommendation: default to **(b)** (accept the gap) for this fix, since the issue title and "Why it matters" section are scoped specifically to the job-triggered double-logging problem, and the manual-trigger path is an operator-initiated action, not unattended scheduled monitoring — a human running it can observe the result directly rather than relying on log-based alerting. The architect should confirm or override this in `arch-review.r1.md`.

## Status: HAS_QUESTIONS
