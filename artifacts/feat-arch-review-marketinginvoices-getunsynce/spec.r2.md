# Specification: Remove Unimplemented Sync Scaffolding from MarketingInvoices

## Summary
Three pieces of scaffolding in the `MarketingInvoices` module (`IImportedMarketingTransactionRepository.GetUnsyncedAsync`, `ImportedMarketingTransaction.IsSynced`, `ImportedMarketingTransaction.ErrorMessage`) were added to support a FlexiBee sync workflow that was never implemented and is explicitly deferred to "Future Work" in epic #609. This spec removes the scaffolding — deleting the interface method, dropping the two entity properties, removing their EF Core configuration, and adding a migration to drop the `IsSynced` and `ErrorMessage` columns.

## Background
A daily architecture review on 2026-05-26 surfaced three related YAGNI violations in `Domain/Features/MarketingInvoices/`:

1. **`IImportedMarketingTransactionRepository.GetUnsyncedAsync`** (`Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs:7`) — declared on the domain interface with a concrete implementation, zero callers across the entire `src/` tree.
2. **`ImportedMarketingTransaction.IsSynced`** (`Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs:14`) — set to `false` on insert at `MarketingInvoiceImportService.cs:74` and never updated to `true` anywhere in the codebase. Every row in the database is permanently `IsSynced = false`.
3. **`ImportedMarketingTransaction.ErrorMessage`** (`Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs:15`) — `text` column configured at `ImportedMarketingTransactionConfiguration.cs:53`, but no application code writes to it. Errors are logged via `ILogger` and counted on `result.Failed`, never persisted on the entity. Every row has `ErrorMessage = NULL`.

The original shared-core design (`docs/superpowers/specs/2026-04-14-marketing-invoice-import-shared-core-design.md:50`) explicitly defers the sync to a "future FlexiBee phase," and epic #609 lists "FlexiBee received invoice creation from stored transactions" under **Future Work (not in scope)**. No open issue, PR, or plan currently schedules that work.

When the FlexiBee sync is eventually built, it will almost certainly follow the richer `IssuedInvoice` precedent (`SyncSucceeded` / `SyncFailed` API, sync-history rows, `LastSyncTime`, structured `ErrorType` distinguishing transient from permanent failures) rather than the single `bool IsSynced` + `text ErrorMessage` pair currently in place. Removing the speculative scaffolding now is strictly cheaper than carrying it and is unlikely to be reused as-is.

## Functional Requirements

### FR-1: Remove `GetUnsyncedAsync` from the repository interface and implementation
Delete the method from `IImportedMarketingTransactionRepository` (`Domain/Features/MarketingInvoices/IImportedMarketingTransactionRepository.cs:7`) and from its concrete implementation in the persistence layer.

**Acceptance criteria:**
- `IImportedMarketingTransactionRepository` no longer declares `GetUnsyncedAsync`.
- The concrete repository implementation no longer defines `GetUnsyncedAsync`.
- `dotnet build` succeeds.
- A repo-wide search for `GetUnsyncedAsync` returns zero hits.

### FR-2: Remove `IsSynced` and `ErrorMessage` properties from `ImportedMarketingTransaction`
Delete both properties from `Domain/Features/MarketingInvoices/ImportedMarketingTransaction.cs` (lines 14 and 15) and remove any assignments to them.

**Acceptance criteria:**
- `ImportedMarketingTransaction` no longer declares `IsSynced` or `ErrorMessage`.
- `MarketingInvoiceImportService.cs:74` (the insert path) no longer sets `IsSynced = false`.
- `dotnet build` succeeds.
- A repo-wide search for `IsSynced` and `ErrorMessage` in the MarketingInvoices module returns zero hits.

### FR-3: Remove `IsSynced` and `ErrorMessage` column mappings from EF configuration
Remove the property configurations for both columns from `ImportedMarketingTransactionConfiguration.cs` (`IsSynced` mapping and the `ErrorMessage` `text` column at approximately line 53).

**Acceptance criteria:**
- The configuration class no longer references the removed properties.
- `dotnet build` succeeds.
- A repo-wide search across the `MarketingInvoices` configuration for `IsSynced` and `ErrorMessage` returns zero hits.

### FR-4: Add an EF Core migration to drop the `IsSynced` and `ErrorMessage` columns
Create a new EF Core migration that drops the `IsSynced` and `ErrorMessage` columns from the `ImportedMarketingTransactions` table.

**Acceptance criteria:**
- Migration is generated via `dotnet ef migrations add` with a descriptive name (e.g., `RemoveUnusedSyncColumnsFromImportedMarketingTransactions`).
- `Up` drops both columns.
- `Down` re-adds them with the original types (`bool` not null, default `false`; `text` nullable) to preserve reversibility.
- Migration applies cleanly against a database in the current head state.
- Migration is documented in the PR description, since database migrations are applied manually in this project (per project facts).

### FR-5: Verify no callers depend on the entity-level error storage
Confirm that removing `ErrorMessage` does not regress any caller that reads the error text. The brief states only `ILogger` and `result.Failed` are consumed; verify before removal.

**Acceptance criteria:**
- A repo-wide search confirms no code reads `ImportedMarketingTransaction.ErrorMessage` or `ImportedMarketingTransaction.IsSynced`.
- Existing unit tests for `MarketingInvoiceImportService` continue to pass.

### FR-6: Sequence this work behind two open PRs that touch the same files
Two open PRs modify the same interface / service touched by this spec and must be merged first:

- **PR #1771** (`feat-arch-review-marketinginvoices-iimportedm`) — edits `IImportedMarketingTransactionRepository.AddAsync` and the concrete repository (same interface and class FR-1 modifies).
- **PR #1766** (`feat-arch-review-marketinginvoices-marketingt`) — removes `MarketingTransaction.Platform` and touches `MarketingInvoiceImportService.cs` near the entity-construction block where `IsSynced = false` is set (same area FR-2 modifies).

PR #616 (the long-running shared-core PR that originally introduced these fields) is irrelevant to current head and does not need to be sequenced.

**Acceptance criteria:**
- Before generating the EF migration, this branch is rebased onto `main` after #1771 and #1766 have merged (or onto an `main` that already contains them), so the migration is generated against the latest schema snapshot.
- No merge conflicts remain in `IImportedMarketingTransactionRepository.cs`, the concrete repository, `ImportedMarketingTransaction.cs`, `ImportedMarketingTransactionConfiguration.cs`, or `MarketingInvoiceImportService.cs` at PR-open time.

## Non-Functional Requirements

### NFR-1: Performance
No runtime performance impact. Migration runtime depends on table row count; the columns are small (`bool` + `text`). If the `ImportedMarketingTransactions` table exceeds ~1M rows in production, the migration's locking behavior on column drops should be reviewed before deploy; otherwise it is expected to be fast.

### NFR-2: Security
No new auth surface, no secrets, no data-classification changes. The dropped columns contain only `false` and `NULL`.

### NFR-3: Reversibility
The Option B migration must include a working `Down` method so the columns can be restored if the FlexiBee sync workflow is later built and chooses to reuse this exact schema (unlikely, but cheap to preserve).

### NFR-4: Backward compatibility
There are no external consumers of `IsSynced`, `ErrorMessage`, or `GetUnsyncedAsync` (verified by the brief's grep finding). No HTTP API, OpenAPI contract, frontend code, or external integration is affected.

### NFR-5: Test coverage
Existing tests touching `MarketingInvoiceImportService` and `ImportedMarketingTransaction` must continue to pass after removal. No new tests are required — removing dead code does not introduce new behavior to cover.

## Data Model

**Current state** (`ImportedMarketingTransaction`):
- Business fields (Id, transaction identity, amounts, dates, platform linkage, etc.)
- `IsSynced : bool` — always `false`, never updated.
- `ErrorMessage : string?` — always `null`, never written.

**After this change:**
- Business fields only. `IsSynced` and `ErrorMessage` are removed from the entity class and the `ImportedMarketingTransactions` table.

No data backfill or preservation is required — the dropped columns contain only `false` and `NULL` values that carry no information.

## API / Interface Design

- **Domain interface change:** `IImportedMarketingTransactionRepository` shrinks by one method (`GetUnsyncedAsync` removed).
- **HTTP API:** no changes.
- **OpenAPI / generated TypeScript client:** no changes (this entity is not exposed through any DTO that includes these fields; verify during FR-5).
- **Frontend:** no changes.

## Dependencies

- EF Core migration infrastructure (already in place).
- Manual deployment step to apply the migration in each environment (per project facts: migrations are not automated). PR description must call this out.
- Merge ordering relative to PRs #1771 and #1766 (see FR-6).

## Out of Scope

- Building the FlexiBee sync workflow itself — explicitly deferred per epic #609 and the shared-core design doc. If/when it is built, it will be designed fresh following the `IssuedInvoice` precedent (sync-history rows, `LastSyncTime`, structured `ErrorType`), not by reviving these columns.
- Refactoring `MarketingInvoiceImportService` beyond removing the `IsSynced = false` line on insert.
- Reworking the existing import-error logging path (it already logs via `ILogger` and aggregates `result.Failed`).
- Any UI changes — there is no UI that exposes `IsSynced` or `ErrorMessage`.
- Backfilling or archiving data in the dropped columns (no information to preserve).
- Changes to the `IssuedInvoice` sync model (separate feature, separate module).

## Open Questions
None.

## Status: COMPLETE