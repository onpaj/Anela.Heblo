# Specification: Persist scheduled DQT run records before execution

## Summary
Add `SaveChangesAsync` calls in the three scheduled DQT job entry points (`InvoiceDqtJob`, `ProductPairingDqtJob`, `StockWriteBackDqtJob`) so that the `DqtRun` record is committed to the database immediately after `AddAsync`, before the job runner is invoked. This brings the scheduled path in line with the manual trigger path and ensures a durable audit trail even when the process crashes mid-execution.

## Background
Data Quality Tests (DQT) can be triggered two ways: manually via `RunDqtHandler` (handles a UI/API request) and on a nightly schedule via three `IHostedService`-style jobs. Each invocation creates a `DqtRun` aggregate (status `Running`) and hands the run ID to `_jobRunner.RunAsync`, which executes the test and updates the run record in its own `finally` block.

In the manual path (`RunDqtHandler.cs:40–42`), the handler calls `SaveChangesAsync` immediately after `AddAsync`, so the `Running` record exists in the database before the fire-and-forget execution begins. In the three scheduled jobs (`InvoiceDqtJob.cs:48–51`, `ProductPairingDqtJob.cs:48–50`, `StockWriteBackDqtJob.cs`), the `SaveChangesAsync` is omitted. The run is only persisted at the end of `RunAsync` because the job runner reuses the same EF context scope and saves in its `finally` block.

This works in the happy path but breaks under failure:
- A process crash or kill during execution skips the `finally` block, leaving **no database record** that the run was ever attempted.
- Operators investigating gaps in nightly run history cannot distinguish "never scheduled" from "crashed mid-run" without log archaeology.
- The manual and scheduled paths diverge in failure semantics despite executing the same underlying logic.

The fix is a one-line addition (`await _repository.SaveChangesAsync(cancellationToken);`) in each of the three job classes, immediately after `AddAsync` and before `_jobRunner.RunAsync`.

## Functional Requirements

### FR-1: Persist scheduled `InvoiceDqtJob` run record before execution
In `InvoiceDqtJob.ExecuteAsync`, after constructing the `DqtRun` aggregate and calling `_repository.AddAsync(run, cancellationToken)`, the job must call `_repository.SaveChangesAsync(cancellationToken)` before invoking `_jobRunner.RunAsync(run.Id, cancellationToken)`.

**Acceptance criteria:**
- The `DqtRun` row for an `IssuedInvoiceComparison` scheduled run is queryable from the database with `Status = Running` immediately after `ExecuteAsync` returns from `SaveChangesAsync`, before the runner finishes.
- If the process is killed between `SaveChangesAsync` and `_jobRunner.RunAsync` completing, a row with `TriggerType = Scheduled`, `TestType = IssuedInvoiceComparison`, and `Status = Running` remains in the database.
- Existing happy-path behaviour (run completes, status transitions to `Succeeded`/`Failed`) is preserved.

### FR-2: Persist scheduled `ProductPairingDqtJob` run record before execution
In `ProductPairingDqtJob.ExecuteAsync`, add the same `SaveChangesAsync` call between `AddAsync` and `_jobRunner.RunAsync`.

**Acceptance criteria:**
- A `DqtRun` row with `TestType = ProductPairing` (or the corresponding `DqtTestType` value used by this job), `TriggerType = Scheduled`, and `Status = Running` is committed before the runner is invoked.
- Crash between persistence and runner completion leaves the `Running` row visible to monitoring queries.

### FR-3: Persist scheduled `StockWriteBackDqtJob` run record before execution
In `StockWriteBackDqtJob.ExecuteAsync`, add the same `SaveChangesAsync` call between `AddAsync` and `_jobRunner.RunAsync`.

**Acceptance criteria:**
- A `DqtRun` row with the stock-write-back `TestType`, `TriggerType = Scheduled`, and `Status = Running` is committed before the runner is invoked.
- Crash between persistence and runner completion leaves the `Running` row visible to monitoring queries.

### FR-4: Behavioural parity between scheduled and manual triggers
After the fix, the manual (`RunDqtHandler`) and scheduled (three jobs) paths must exhibit identical persistence semantics: the `DqtRun` record is durably written in `Running` state before execution begins, regardless of trigger type.

**Acceptance criteria:**
- A code review of `RunDqtHandler.cs` and each of the three job files shows the same `AddAsync` → `SaveChangesAsync` → `RunAsync` sequence.
- An integration test (or equivalent) that simulates an unhandled exception during `RunAsync` confirms a `Running` (or runner-updated) row exists for both trigger types under failure.

### FR-5: Cancellation token propagation unchanged
The added `SaveChangesAsync` must use the same `cancellationToken` already in scope, matching the rest of the calls in the method.

**Acceptance criteria:**
- Cancelling the host shutdown token while the job is starting cancels the save in the same manner as the surrounding repository calls.

## Non-Functional Requirements

### NFR-1: Performance
The additional database round-trip per scheduled job execution is negligible (three nightly runs total). No measurable impact on job runtime or system load.

### NFR-2: Reliability / Data integrity
After this change, no scheduled DQT execution may begin without first producing a durable `DqtRun` audit row. This is the primary motivation for the fix.

### NFR-3: Backwards compatibility
No schema changes. No API changes. No configuration changes. Behaviour change is strictly additive (rows that previously appeared only after completion now appear at the start as well).

### NFR-4: Test coverage
The three modified job classes must retain at least their current unit-test coverage. New regression coverage should assert that `SaveChangesAsync` is invoked before `_jobRunner.RunAsync` in each scheduled job.

## Data Model
No changes. The fix uses the existing `DqtRun` aggregate, the existing `IDqtRunRepository` (with its existing `AddAsync` / `SaveChangesAsync` methods), and existing `DqtRunStatus` / `DqtTriggerType` / `DqtTestType` enums.

Relevant existing entity: `DqtRun` — created via `DqtRun.Start(testType, fromDate, toDate, triggerType)`; transitions through `Running` → `Succeeded` / `Failed` under control of the job runner.

## API / Interface Design
No public API or UI change. Internal sequence change only:

**Before (scheduled path):**
```
DqtRun.Start → AddAsync → RunAsync → (runner's finally) → SaveChangesAsync
```

**After (scheduled path, matches manual path):**
```
DqtRun.Start → AddAsync → SaveChangesAsync → RunAsync → (runner's finally) → SaveChangesAsync
```

No change to manual path; it already saves before invoking the runner.

## Dependencies
- `Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs.InvoiceDqtJob`
- `Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs.ProductPairingDqtJob`
- `Anela.Heblo.Application.Features.DataQuality.Infrastructure.Jobs.StockWriteBackDqtJob`
- `IDqtRunRepository` (existing — `AddAsync` and `SaveChangesAsync` already in use elsewhere)
- The DQT job runner (already shares the EF context scope with the job classes; behaviour unchanged)
- Existing unit-test project for the DataQuality feature slice

No new packages, no infrastructure changes.

## Out of Scope
- Refactoring the job runner to centralise persistence (e.g., having the runner itself perform the initial save). The brief and finding scope the fix to the three call sites; a runner-side refactor is a larger change requiring its own design.
- Introducing a transactional outbox or message-based audit trail.
- Adding alerting / monitoring on stuck `Running` rows (a separate operational concern).
- Changing the `DqtRun` aggregate, its state machine, or any enum values.
- Updating the manual `RunDqtHandler` (already correct).
- Adding new DQT test types or modifying business logic of existing tests.
- Backfilling or repairing historical run records.

## Open Questions
None.

## Status: COMPLETE