# Specification: Extract shared ExecuteAsync into DqtJobBase (DataQuality)

## Summary
Five scheduled DQT job classes (`InvoiceDqtJob`, `ProductPairingDqtJob`, `StockWriteBackDqtJob`, `LotStockReconciliationDqtJob`, `PriceComparisonDqtJob`) duplicate the same ~15-line `ExecuteAsync` body. Extract the shared orchestration into an abstract `DqtJobBase` so only the test type, date offset, runner and metadata vary per job. Pure refactor: no behaviour change.

## Background
Each `ExecuteAsync`: check `IRecurringJobStatusChecker.IsJobEnabledAsync(Metadata.JobName)` (log + return if disabled) -> compute target date from `TimeProvider` (today, or yesterday) -> log "Starting {JobName} for {Date}" -> `DqtRun.Start(type, date, date, DqtTriggerType.Scheduled, utcNow)` -> `AddAsync` + `SaveChangesAsync` -> `runner.RunAsync(run.Id, ct)`.
Variation: Invoice and StockWriteBack use yesterday; ProductPairing, LotStock, PriceComparison use today. Invoice uses `IInvoiceDqtJobRunner`; the other four use `IDriftDqtJobRunner`.

## Functional Requirements

### FR-1: Shared base class
Add `DqtJobBase : IRecurringJob` in `Features/DataQuality/Infrastructure/Jobs/` owning ctor dependencies (`IDqtRunRepository`, `IRecurringJobStatusChecker`, `TimeProvider`, `ILogger`) and the single `ExecuteAsync` implementation.
**Acceptance criteria:**
- Base declares abstract `Metadata`, `TestType`, `GetTargetDate(DateOnly today)` and `RunAsync(Guid runId, CancellationToken)`.
- Order of operations, log messages and persisted `DqtRun` values are identical to current behaviour.

### FR-2: Concrete jobs reduced to variation
Each of the five jobs derives from the base and defines only Metadata, TestType, date rule and runner delegation.
**Acceptance criteria:**
- Concrete `ExecuteAsync` bodies are removed.
- Public class names, namespaces, constructor parameter lists (types and order), `JobName`s and cron expressions are unchanged, so DI registration (via `IRecurringJob` discovery) and existing tests compile unmodified.
- Invoice job keeps `IInvoiceDqtJobRunner`; the four drift jobs keep `IDriftDqtJobRunner`.

### FR-3: Behavioural parity verified
**Acceptance criteria:**
- Existing tests `InvoiceDqtJobTests`, `ProductPairingDqtJobTests`, `StockWriteBackDqtJobTests`, `LotStockReconciliationDqtJobTests` pass unchanged.
- `PriceComparisonDqtJob` has no test file today; add one (disabled -> skip; enabled -> run persisted with PriceComparison type and today's date, runner invoked).
- `DataQualityModuleTests` pass.

## Non-Functional Requirements
### NFR-1: Performance
No change.
### NFR-2: Security
No change.
### NFR-3: Compatibility
Logger category: currently `ILogger<ConcreteJob>`. Base should accept `ILogger` (non-generic) with concrete ctors still taking `ILogger<ConcreteJob>`, preserving log categories.

## Data Model
Unchanged (`DqtRun`, `DqtTestType`, `DqtTriggerType`).

## API / Interface Design
No API, OpenAPI or frontend change. `IRecurringJob` contract unchanged.

## Dependencies
`IDqtRunRepository`, `IRecurringJobStatusChecker`, `TimeProvider`, `IInvoiceDqtJobRunner`, `IDriftDqtJobRunner`.

## Out of Scope
Merging runner interfaces; changing schedules, trigger type, retry logic; any behavioural change; docs/processes changes beyond a `verified_at` bump if CI process-doc check requires it.

## Open Questions
None.

## Status: COMPLETE
