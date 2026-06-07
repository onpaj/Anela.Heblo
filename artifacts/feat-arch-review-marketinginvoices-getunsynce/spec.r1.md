# Specification: Resolve Unimplemented Sync Scaffolding in MarketingInvoices

## Summary
The `MarketingInvoices` module contains three pieces of scaffolding (`GetUnsyncedAsync`, `IsSynced`, `ErrorMessage`) that exist solely to support a sync workflow that was never implemented. This specification covers resolving the dead scaffolding by either implementing the missing sync workflow (Option A) or removing the scaffolding until it is needed (Option B). The decision between the two options depends on whether the accounting-system sync is on the near-term roadmap.

## Background
A daily architecture review on 2026-05-26 surfaced three related YAGNI violations in `Domain/Features/MarketingInvoices/`:

1. **`IImportedMarketingTransactionRepository.GetUnsyncedAsync`** — declared on the domain interface, has a concrete implementation, but has zero callers across the entire `src/` tree.
2. **`ImportedMarketingTransaction.IsSynced`** — set to `false` on insert at `MarketingInvoiceImportService.cs:74` and never updated to `true` anywhere in the codebase. Every row in the database is permanently `IsSynced = false`.
3. **`ImportedMarketingTransaction.ErrorMessage`** — `text` column configured at `ImportedMarketingTransactionConfiguration.cs:53`, but no application code writes to it. Errors are logged via `ILogger` and counted on `result.Failed`, but never persisted on the entity. Every row has `ErrorMessage = NULL`.

The scaffolding misleads readers into assuming an active sync workflow exists, inflates the domain surface area, and adds an always-NULL `text` column to the database. Per the YAGNI principle, speculative infrastructure should be removed until the consuming feature is actively being built.

## Functional Requirements

The functional requirements depend on the resolution direction. Both options are specified so the architect can act on the chosen one.

### Option B (default — remove scaffolding)

#### FR-B1: Remove `GetUnsyncedAsync` from the repository interface and implementation
Delete the method from `IImportedMarketingTransactionRepository` (`Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs:7`) and from its concrete implementation in the persistence layer.

**Acceptance criteria:**
- `IImportedMarketingTransactionRepository` no longer declares `GetUnsyncedAsync`.
- The concrete repository implementation no longer defines `GetUnsyncedAsync`.
- `dotnet build` succeeds.
- A repo-wide search for `GetUnsyncedAsync` returns zero hits.

#### FR-B2: Remove `IsSynced` and `ErrorMessage` properties from `ImportedMarketingTransaction`
Delete both properties from `Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs` (lines 14 and 15).

**Acceptance criteria:**
- `ImportedMarketingTransaction` no longer declares `IsSynced` or `ErrorMessage`.
- `MarketingInvoiceImportService.cs:74` (the insert path) no longer references `IsSynced`.
- `dotnet build` succeeds.
- A repo-wide search for `IsSynced` and `ErrorMessage` in the MarketingInvoices module returns zero hits.

#### FR-B3: Remove `IsSynced` and `ErrorMessage` column mappings from EF configuration
Remove the property configurations for `IsSynced` and `ErrorMessage` from `ImportedMarketingTransactionConfiguration.cs` (around line 53 and the corresponding `IsSynced` configuration).

**Acceptance criteria:**
- The configuration class no longer references the removed properties.
- `dotnet build` succeeds.

#### FR-B4: Add an EF Core migration to drop the `IsSynced` and `ErrorMessage` columns
Create a new EF Core migration that drops the `IsSynced` and `ErrorMessage` columns from the `ImportedMarketingTransactions` table.

**Acceptance criteria:**
- Migration is generated via `dotnet ef migrations add` with a descriptive name (e.g., `RemoveUnusedSyncColumnsFromImportedMarketingTransactions`).
- `Up` drops both columns; `Down` re-adds them with the original types (`bool` default `false`; `text` nullable) to preserve reversibility.
- Migration applies cleanly against a database in the current head state.
- Migration is documented in the PR description for the manual deploy step (database migrations are not automated).

#### FR-B5: Verify no callers exist for the import-service "Failed" path that depended on the entity's error storage
Confirm that removing `ErrorMessage` does not regress any caller that reads the error text. (Brief states only `ILogger` and `result.Failed` are used; verify.)

**Acceptance criteria:**
- A repo-wide search confirms no code reads `ImportedMarketingTransaction.ErrorMessage`.
- Existing unit tests for `MarketingInvoiceImportService` continue to pass.

### Option A (alternative — implement the sync workflow)

#### FR-A1: Implement a use case that fetches unsynced transactions and posts them to the accounting system
Add a use case (MediatR handler) or hosted background service that periodically (or on-demand) reads transactions where `IsSynced = false`, posts them to the accounting system, and persists the sync outcome.

**Acceptance criteria:**
- A new command or background service exists that calls `GetUnsyncedAsync`.
- On successful post, `IsSynced` is set to `true` and the entity is persisted.
- On failure, `ErrorMessage` is set to a meaningful message and the entity is persisted; `IsSynced` remains `false` so the row is retried on the next run.
- The accounting-system call is encapsulated behind an interface (testability + swappability).
- Unit and integration tests cover the happy path, failure path, and idempotency.

#### FR-A2: Define retry and idempotency semantics
Specify whether failed transactions are retried indefinitely, capped at N attempts, or surfaced for manual intervention.

**Acceptance criteria:**
- Retry policy is documented and implemented.
- The accounting-system post is idempotent (or the workflow guards against double-posting).

> Option A requires significant additional design input (target accounting system, transport, auth, retry strategy, schedule) that is **not in the brief**. See Open Questions.

## Non-Functional Requirements

### NFR-1: Performance
- Option B: No runtime performance impact. Migration runtime depends on table row count; the columns are small (bool + text), so the drop should be fast. If the table is large (>1M rows), the migration should be reviewed for locking behavior.
- Option A: Sync workflow should batch transactions and avoid N+1 calls to the accounting system. Targets to be defined when the workflow is specified.

### NFR-2: Security
- No new auth surface in Option B.
- Option A introduces a credential dependency on the accounting system (out of scope here — see Open Questions).

### NFR-3: Reversibility
The Option B migration must include a working `Down` method so the columns can be restored if Option A is pursued later.

### NFR-4: Backward compatibility
There are no external consumers of `IsSynced`, `ErrorMessage`, or `GetUnsyncedAsync` (verified by the brief's grep finding). No API or client-facing contract is affected.

## Data Model

**Current state** (`ImportedMarketingTransaction`):
- `Id`, business fields, plus:
- `IsSynced : bool` (always `false`)
- `ErrorMessage : string?` (always `null`)

**After Option B:**
- `Id`, business fields only. `IsSynced` and `ErrorMessage` are removed from the entity and the database table.

**After Option A:**
- Same shape as current, but `IsSynced` is flipped to `true` on success and `ErrorMessage` is populated on failure.

## API / Interface Design

### Option B
- Domain interface `IImportedMarketingTransactionRepository` shrinks by one method (`GetUnsyncedAsync` removed).
- No HTTP API changes.
- No frontend changes.

### Option A
- New MediatR command or hosted service (internal — no HTTP endpoint required unless manual trigger is desired).
- No frontend changes unless an admin "trigger sync now" UI is desired (out of scope here — see Open Questions).

## Dependencies

### Option B
- EF Core migration infrastructure (already in place).
- Manual deployment step to apply the migration (per project facts: migrations are manual).

### Option A
- Accounting system API / SDK (unspecified in brief).
- Credentials and configuration for the accounting system.
- Background-service or scheduler infrastructure.

## Out of Scope

- Refactoring the `MarketingInvoiceImportService` itself beyond removing the `IsSynced = false` line on insert (Option B) or wiring it to the sync workflow (Option A).
- Reworking the import service's existing error-logging path (it already logs via `ILogger` and aggregates `result.Failed`).
- Any UI changes to surface sync status or error messages to users.
- Backfilling or migrating data in the dropped columns — they are always `false` / `NULL`, so there is no data to preserve.
- Choosing or integrating with a specific accounting system for Option A.

## Open Questions

1. **Which option should be implemented — A (build the sync workflow) or B (remove the scaffolding)?** The brief states "Option B is cheaper now; option A is the right call only when the sync workflow is actively being built." Is the accounting-system sync on the near-term roadmap, or should we remove the scaffolding now and re-add it when the sync work is actually scheduled?
2. **(If Option A)** Which accounting system is the sync target, and what is the transport (REST, SOAP, file drop, queue)?
3. **(If Option A)** What is the desired trigger model — scheduled background service, on-demand command, or both? And what schedule/cadence?
4. **(If Option A)** What are the retry semantics for failed posts — bounded retries, indefinite retry until success, dead-letter after N attempts?
5. **(If Option B)** Are there any pending PRs or in-flight work that already reference `GetUnsyncedAsync`, `IsSynced`, or `ErrorMessage` and would conflict with their removal? (Worth confirming before the migration is generated.)

## Status: HAS_QUESTIONS