# Architecture Review: Batch SaveChangesAsync in MarketingInvoiceImportService

## Skip Design: true

Backend-only refactor of a job-invoked service. No UI components, screens, or visual design decisions.

## Architectural Fit Assessment

The change aligns cleanly with existing conventions. `IImportedMarketingTransactionRepository.SaveChangesAsync` delegates to `ApplicationDbContext.SaveChangesAsync` (via `BaseRepository`), which is a unit-of-work flush — calling it per record is indeed a misuse. `AddAsync` calls `DbSet.AddAsync`, which only stages the entity in EF Core change tracking, so deferring the flush is correct.

Integration points are narrow and well-contained:
- **Single caller surface:** `MarketingInvoiceImportService` is constructed directly inside `GoogleAdsInvoiceImportJob` and `MetaAdsInvoiceImportJob` (`new MarketingInvoiceImportService(...)`, not DI-resolved). Both jobs only read `MarketingImportResult` counts for logging — no caller depends on per-record commit timing. The contract that must hold is the count semantics, exactly as the spec states.
- **Scoped lifetime:** `IImportedMarketingTransactionRepository` is registered `AddScoped`. Each job execution resolves a fresh `ApplicationDbContext`, so the change tracker is per-run and does not leak staged entities across runs even on flush failure.
- **EF transactionality:** A single `SaveChangesAsync` over N `Added` entities is already wrapped in an implicit database transaction by EF Core. The spec's "all-or-nothing" goal is therefore satisfied **without** an explicit `IDbContextTransaction` — correctly listed as out of scope.

The spec is sound, but it **misses one genuine behavior regression** — see Decision 1 and Risks.

## Proposed Architecture

### Component Overview

```
GoogleAdsInvoiceImportJob / MetaAdsInvoiceImportJob   (twice-daily cron)
        │  new MarketingInvoiceImportService(source, repository, logger)
        ▼
MarketingInvoiceImportService.ImportAsync(from, to, ct)
        │
        ├─ source.GetTransactionsAsync(from, to, ct)
        │
        ├─ foreach transaction:                       ◄── per-record try/catch (unchanged)
        │     ├─ repository.ExistsAsync(...)           ── SQL query, sees DB only
        │     ├─ [NEW] in-batch dedup check            ── sees entities staged this run
        │     ├─ repository.AddAsync(entity, ct)       ── stages in change tracker only
        │     └─ stagedCount++
        │
        └─ if stagedCount > 0:                         ◄── single flush, NEW try/catch
              repository.SaveChangesAsync(ct)          ── one INSERT batch + COMMIT
              └─ on failure: move stagedCount → Failed, Imported = 0
```

### Key Design Decisions

#### Decision 1: Guard against intra-batch duplicate TransactionIds

**Options considered:**
- (A) Implement the spec as written — rely solely on `ExistsAsync` for duplicate detection.
- (B) Add an in-memory `HashSet<string>` of `TransactionId`s staged in the current run, and skip a transaction when it is in the set, alongside the existing `ExistsAsync` check.

**Chosen approach:** (B).

**Rationale:** This is the one real defect in the spec. `ExistsAsync` runs `DbSet.AnyAsync` — a SQL query against the database — so it **cannot see entities staged via `AddAsync` but not yet flushed**. Under the *current* per-record save, if the source returns the same `(Platform, TransactionId)` twice in one run, the first record is committed immediately and the second's `ExistsAsync` returns `true` → `Skipped++`. After batching, the first record is only staged; the second's `ExistsAsync` returns `false` → both are staged → the final `SaveChangesAsync` violates the unique index `IX_ImportedMarketingTransactions_Platform_TransactionId` → **the entire batch flush fails** and FR-3 reports *every* record as `Failed`.

So a previously survivable, low-cost event (one duplicate → one skip) becomes a catastrophic one (whole run lost). Whether ad-platform billing APIs ever return a transaction twice within a 7-day lookback is uncertain — but the failure mode is severe enough that the architecture must not depend on the assumption. A `HashSet<string>` checked next to `ExistsAsync` restores the prior intra-batch dedup behavior at negligible cost and keeps the change inside the service with no contract change.

#### Decision 2: Use a local `stagedCount`, not `result.Imported`, as the flush guard

**Options considered:**
- (A) Spec's wording: guard the flush with `result.Imported > 0` and, on flush failure, set `result.Imported = 0` / `result.Failed += <old Imported>`.
- (B) Track a local `int stagedCount` incremented after `AddAsync`; set `result.Imported = stagedCount` only after a successful flush; on failure set `result.Failed += stagedCount`.

**Chosen approach:** (B).

**Rationale:** `result.Imported` is a *reporting* field. Reusing it as loop-control state and then mutating it back to `0` conflates "what happened" with "what to do next" and makes the FR-3 correction read as a fix-up rather than the intended semantics. With a local `stagedCount`, `result.Imported` only ever holds a true value: it stays `0` until the flush succeeds. This is functionally equivalent to the spec and produces identical counts for every acceptance criterion — it is purely a clarity improvement. Treat this as the recommended implementation of FR-1/FR-3 (see Specification Amendments).

#### Decision 3: Keep error isolation asymmetric — per-record catch + one flush catch

**Chosen approach:** Retain the existing per-transaction `try/catch` inside the loop and add a separate `try/catch` around the single post-loop `SaveChangesAsync`, neither rethrowing.

**Rationale:** Matches the spec and the established "error-isolation" style of this service and both import jobs (which log-and-continue). The flush `catch` should catch `Exception` for consistency with the loop, accepting that a cancellation mid-flush would be logged as an error — acceptable and pre-existing in spirit (low severity).

## Implementation Guidance

### Directory / Module Structure

No new files. Two files change:

- `backend/src/Anela.Heblo.Application/Features/MarketingInvoices/Services/MarketingInvoiceImportService.cs` — the refactor.
- `backend/test/Anela.Heblo.Tests/Features/MarketingInvoices/MarketingInvoiceImportServiceTests.cs` — updated and new tests.

No changes to `IImportedMarketingTransactionRepository`, the repository implementation, `MarketingImportResult`, the entity, the EF configuration, or `MarketingInvoicesModule`.

### Interfaces and Contracts

All public contracts are unchanged: `ImportAsync(DateTime, DateTime, CancellationToken)` signature, `MarketingImportResult`, and the repository interface. The change is entirely inside the `ImportAsync` method body.

Implementation shape inside `ImportAsync`:

```csharp
var result = new MarketingImportResult();
var stagedCount = 0;
var stagedIds = new HashSet<string>();   // intra-batch dedup (Decision 1)

foreach (var transaction in transactions)
{
    try
    {
        // duplicate: already in DB, OR already staged in this run
        if (stagedIds.Contains(transaction.TransactionId) ||
            await _repository.ExistsAsync(_source.Platform, transaction.TransactionId, ct))
        {
            _logger.LogDebug("Transaction {TransactionId} for {Platform} already imported — skipping",
                transaction.TransactionId, _source.Platform);
            result.Skipped++;
            continue;
        }

        var entity = new ImportedMarketingTransaction { /* unchanged mapping */ };
        await _repository.AddAsync(entity, ct);
        stagedIds.Add(transaction.TransactionId);
        stagedCount++;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to import transaction {TransactionId} for {Platform}",
            transaction.TransactionId, _source.Platform);
        result.Failed++;
    }
}

if (stagedCount > 0)
{
    try
    {
        await _repository.SaveChangesAsync(ct);
        result.Imported = stagedCount;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex,
            "Failed to persist {Count} marketing transactions for {Platform}",
            stagedCount, _source.Platform);
        result.Failed += stagedCount;
        // result.Imported stays 0
    }
}
// final completion log uses corrected result counts (unchanged log line)
```

### Data Flow

1. **Happy path (N new, no intra-batch dupes):** N `AddAsync` calls stage N entities; one `SaveChangesAsync` issues one batched `INSERT` + `COMMIT`. `Imported = N`, `Skipped = 0`, `Failed = 0`.
2. **Mixed new/duplicate:** DB duplicates and intra-batch duplicates both hit the skip path → `Skipped`. New records → staged. One flush. Counts reflect true outcome.
3. **Per-transaction error:** Exception in `ExistsAsync`/`AddAsync` is caught → `Failed++`; loop continues; already-staged entities remain tracked and are flushed at the end.
4. **Flush failure (FR-3):** `SaveChangesAsync` throws → logged with platform + count; `Failed += stagedCount`; `Imported` remains `0`. Method returns normally. The scoped `DbContext` is disposed with the job scope, discarding the orphaned `Added` entities.
5. **Empty / all-skipped:** `stagedCount == 0` → `SaveChangesAsync` is never called.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Intra-batch duplicate `TransactionId` poisons the whole batch via unique-index violation on the single flush (regression vs. per-record save) | HIGH | Add in-run `HashSet<string>` dedup check alongside `ExistsAsync` (Decision 1). Add a test covering two source rows with the same `TransactionId` → one `Imported`, one `Skipped`. |
| A single bad record (e.g. constraint/data error not caught earlier) now fails the entire batch instead of one record | MEDIUM | Inherent and accepted trade-off of batching, explicitly desired by the brief ("all-or-nothing"). FR-3 ensures counts stay truthful; structured `LogError` keeps it observable. No further mitigation. |
| Reusing `result.Imported` as control-flow state obscures intent and risks an off-by-one during the FR-3 correction | LOW | Use a local `stagedCount`; set `result.Imported` only after a successful flush (Decision 2). |
| `OperationCanceledException` during the flush is caught and logged as an error rather than surfaced as cancellation | LOW | Acceptable — consistent with the existing catch-all loop style; out of scope to change. |

## Specification Amendments

1. **New requirement — intra-batch duplicate handling (insert as FR-1a or extend FR-2).** The service must skip a transaction whose `(Platform, TransactionId)` was already staged earlier in the *same* run, not only those already in the database. Rationale: `ExistsAsync` queries the DB and cannot see un-flushed staged entities; without this, a duplicated source row triggers a unique-index violation that fails the entire batched flush. Acceptance: given two source transactions with identical `TransactionId`, exactly one is staged and counted `Imported`, the other is counted `Skipped`, and the run does not fail.

2. **FR-4 — add a test for amendment 1.** Add a test: source returns the same `TransactionId` twice → `AddAsync` invoked `Times.Once`, `SaveChangesAsync` `Times.Once`, `Imported == 1`, `Skipped == 1`, `Failed == 0`.

3. **FR-1 / FR-3 wording refinement.** Replace the `result.Imported > 0` flush guard and the "move `Imported` to `Failed`" correction with a local `stagedCount`: guard the flush on `stagedCount > 0`; set `result.Imported = stagedCount` only after a successful flush; on flush failure set `result.Failed += stagedCount` and leave `result.Imported` at `0`. Functionally identical to the current wording for every stated acceptance criterion; clearer intent.

4. **Note on atomicity (informational).** State explicitly that the all-or-nothing guarantee comes from EF Core wrapping the single `SaveChangesAsync` in an implicit transaction — no explicit `IDbContextTransaction` is needed (consistent with Out of Scope).

## Prerequisites

None. No migration, configuration, or infrastructure change. The unique index `IX_ImportedMarketingTransactions_Platform_TransactionId` already exists and is unchanged. Validation per `CLAUDE.md`: `dotnet build`, `dotnet format`, and the `MarketingInvoiceImportServiceTests` suite must pass.