# Specification: Collapse `IssuedInvoiceRepository.GetSyncStatsAsync` into a single aggregate query

## Summary
`IssuedInvoiceRepository.GetSyncStatsAsync` currently issues five sequential SQL round-trips against `issued_invoices` to compute a single stats object. This spec covers rewriting the method to compute all five aggregates (`TotalInvoices`, `SyncedInvoices`, `InvoicesWithErrors`, `CriticalErrors`, `LastSyncTime`) in one EF Core query, with `UnsyncedInvoices` remaining a derived, in-memory subtraction. Behavior and the public method signature are unchanged; only the query execution strategy changes.

## Background
`GetSyncStatsAsync` backs the `GetIssuedInvoiceSyncStats` use case, which the frontend's `useIssuedInvoiceSyncStats` hook polls (5-minute `staleTime`) from the InvoiceImportStatistics and sync-stats pages. Today the method builds one filtered `IQueryable` (`InvoiceDate` between `fromDate` and `toDate`) and then awaits it five times — `CountAsync()`, `CountAsync(x => x.IsSynced)`, `CountAsync(x => x.ErrorType.HasValue)`, `CountAsync(x => ... != InvoicePaired)`, and a `MaxAsync` on a further-filtered subquery for `LastSyncTime` — each re-scanning the same date-range slice of the table. As `issued_invoices` grows with each daily import, this becomes the slowest call in the Invoices module for no functional benefit: EF Core can express the same result as one grouped aggregate query, translated by the provider into a single `SELECT COUNT(*), COUNT(CASE ...), ..., MAX(...)` statement.

This is a pure performance/internal-implementation fix filed by the daily arch-review routine (2026-07-08). It does not change the API contract, the `IssuedInvoiceSyncStats` DTO, or any caller.

## Functional Requirements

### FR-1: Compute sync stats via a single grouped aggregate query
Rewrite `IssuedInvoiceRepository.GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken)` (`backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`, lines 35–58) so that `TotalInvoices`, `SyncedInvoices`, `InvoicesWithErrors`, `CriticalErrors`, and `LastSyncTime` are all produced by exactly one query executed against the database (one `await` that materializes results), instead of the current four `CountAsync` calls plus one `MaxAsync` call.

Use the same base predicate currently in place: `x.InvoiceDate >= fromDate.Date && x.InvoiceDate <= toDate.Date`.

Implementation approach (per the finding's suggested fix, adjusted to preserve existing null-handling semantics):
```csharp
public async Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)
{
    var query = DbSet.Where(x => x.InvoiceDate >= fromDate.Date && x.InvoiceDate <= toDate.Date);

    var stats = await query
        .GroupBy(_ => 1)
        .Select(g => new
        {
            Total = g.Count(),
            Synced = g.Count(x => x.IsSynced),
            WithErrors = g.Count(x => x.ErrorType.HasValue),
            Critical = g.Count(x => x.ErrorType.HasValue && x.ErrorType != IssuedInvoiceErrorType.InvoicePaired),
            LastSyncTime = g.Where(x => x.LastSyncTime.HasValue).Max(x => (DateTime?)x.LastSyncTime)
        })
        .FirstOrDefaultAsync(cancellationToken);

    var totalInvoices = stats?.Total ?? 0;
    var syncedInvoices = stats?.Synced ?? 0;

    return new IssuedInvoiceSyncStats
    {
        TotalInvoices = totalInvoices,
        SyncedInvoices = syncedInvoices,
        UnsyncedInvoices = totalInvoices - syncedInvoices,
        InvoicesWithErrors = stats?.WithErrors ?? 0,
        CriticalErrors = stats?.Critical ?? 0,
        LastSyncTime = stats?.LastSyncTime
    };
}
```
The `GroupBy(_ => 1)` produces zero groups (not one group with zero counts) when the filtered set is empty, so `FirstOrDefaultAsync` returns `null` in that case — the null-coalescing above must reproduce the current empty-result behavior (all counts `0`, `LastSyncTime` `null`), matching what the five-query version returns today when no rows are in range.

Do not change the method's public signature, the `IIssuedInvoiceRepository` interface, or the `IssuedInvoiceSyncStats` DTO shape.

**Acceptance criteria:**
- `GetSyncStatsAsync` issues exactly one query to the database for the given `(fromDate, toDate)` window (verified via query-count assertion or SQL-logging/interceptor in a test using a relational provider, since the existing test suite uses the EF Core InMemory provider which does not enforce or reveal round-trip count).
- For a date range with zero matching invoices, the method returns `TotalInvoices = 0`, `SyncedInvoices = 0`, `UnsyncedInvoices = 0`, `InvoicesWithErrors = 0`, `CriticalErrors = 0`, `LastSyncTime = null` (no `NullReferenceException` or unhandled exception from the empty `GroupBy` result).
- `UnsyncedInvoices` continues to equal `TotalInvoices - SyncedInvoices`.
- `CriticalErrors` counts invoices where `ErrorType.HasValue == true` and `ErrorType != IssuedInvoiceErrorType.InvoicePaired`, matching current semantics exactly (i.e., `InvoicePaired` errors are excluded from `CriticalErrors` but included in `InvoicesWithErrors`).
- `LastSyncTime` equals the maximum `LastSyncTime` among in-range invoices that have a non-null `LastSyncTime`; if none have a value, result is `null` (same as current behavior — the `Where(x => x.LastSyncTime.HasValue)` filter before `Max` must be preserved so rows without a sync time don't force `Max` to consider a `null`/default and don't throw).
- The existing test `GetSyncStatsAsync_WithVariousInvoices_ReturnsAccurateStats` in `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs` continues to pass unmodified, asserting `TotalInvoices = 4`, `SyncedInvoices = 1`, `UnsyncedInvoices = 3`, `InvoicesWithErrors = 2`, `CriticalErrors = 1` for the fixture it sets up.
- No caller of `GetSyncStatsAsync` (`GetIssuedInvoiceSyncStatsHandler`, `InvoicesController`, `InvoiceImportService` if applicable) requires any code change — the method's external behavior and contract are unchanged.

### FR-2: Add a regression test for `LastSyncTime` correctness
The current test (`GetSyncStatsAsync_WithVariousInvoices_ReturnsAccurateStats`) does not assert `stats.LastSyncTime`, so a correctness regression in that specific aggregate (the one most likely to be mishandled when folded into a single `GroupBy`/`Select`, since it needs its own null-filtered `Max`) would not be caught. Add or extend a test to assert `LastSyncTime` is computed correctly, covering both:
- A non-empty in-range set with a mix of invoices that do and do not have `LastSyncTime` set, asserting the returned value equals the maximum among those that have one.
- An in-range set where no invoice has `LastSyncTime` set, asserting the returned value is `null`.

**Acceptance criteria:**
- New/extended test(s) live in `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs`, following existing test conventions in that file (in-memory `ApplicationDbContext`, `CreateTestSyncData()` helper, `SyncSucceeded`/`SyncFailed` entity methods).
- Tests fail against the current (pre-fix) five-query implementation only if a bug is intentionally introduced — i.e., they exercise real behavior, not tautologies — and pass against the FR-1 implementation.

## Non-Functional Requirements

### NFR-1: Performance
- Round-trips: `GetSyncStatsAsync` must perform 1 database round-trip per invocation (down from 5).
- No behavioral change to response latency budget beyond the reduction itself; no new SLA is introduced by this fix. The change is expected to reduce this endpoint's latency roughly in proportion to the round-trip reduction, especially as `issued_invoices` grows.
- Must not introduce N+1 behavior or client-side (in-memory) evaluation of the full invoice set — the aggregation must be pushed to the database via EF Core's SQL translation (`GroupBy` + `Count`/`Max` projected in a single `Select`).

### NFR-2: Security
No change. `GetSyncStatsAsync` reads only aggregate counts and a max timestamp; no new data exposure, no change to authorization (existing endpoint auth on the `GetIssuedInvoiceSyncStats` use case/controller action is untouched).

### NFR-3: Compatibility
- `IIssuedInvoiceRepository.GetSyncStatsAsync` signature is unchanged (`Task<IssuedInvoiceSyncStats> GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)`), so no changes ripple to `GetIssuedInvoiceSyncStatsHandler`, `InvoicesController`, the OpenAPI contract, or the generated TypeScript client.
- `IssuedInvoiceSyncStats` DTO fields and computed `SyncSuccessRate` property are unchanged.

## Data Model
No schema or entity changes. Relevant existing types (unchanged):
- `IssuedInvoice` (queried via `DbSet`): includes `InvoiceDate` (DateTime), `IsSynced` (bool), `ErrorType` (nullable `IssuedInvoiceErrorType`), `LastSyncTime` (nullable DateTime).
- `IssuedInvoiceErrorType`: enum including at least `InvoicePaired` as a non-critical error value.
- `IssuedInvoiceSyncStats` (`backend/src/Anela.Heblo.Domain/Features/Invoices/IssuedInvoiceSyncStats.cs`): `TotalInvoices`, `SyncedInvoices`, `UnsyncedInvoices`, `InvoicesWithErrors`, `CriticalErrors` (all `int`), `LastSyncTime` (nullable `DateTime`), `SyncSuccessRate` (computed `decimal`, unchanged).

## API / Interface Design
No public API/contract change. Internal method body only:
- File: `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`
- Method: `GetSyncStatsAsync(DateTime fromDate, DateTime toDate, CancellationToken cancellationToken = default)` on `IssuedInvoiceRepository : BaseRepository<IssuedInvoice, string>, IIssuedInvoiceRepository`
- Interface: `IIssuedInvoiceRepository.GetSyncStatsAsync` (`backend/src/Anela.Heblo.Domain/Features/Invoices/IIssuedInvoiceRepository.cs`) — no change needed.
- Consumers (verify no changes required, do not modify unless a compile break occurs): `GetIssuedInvoiceSyncStatsHandler`, `InvoicesController` (`backend/src/Anela.Heblo.API/Controllers/InvoicesController.cs`), frontend `useIssuedInvoiceSyncStats` hook.

## Dependencies
- Entity Framework Core (`Microsoft.EntityFrameworkCore`) `GroupBy`/aggregate translation support for the project's configured relational provider (PostgreSQL via Npgsql, per `EF.Functions.ILike` usage elsewhere in the same file). Confirm the provider translates `GroupBy(_ => 1).Select(g => new { g.Count(), g.Count(predicate), g.Max(...) })` into a single SQL aggregate statement rather than silently falling back to client-side evaluation (EF Core throws by default on unsupported client evaluation in a `GroupBy` context, so a successful build/test run is a reasonable signal, but confirming via SQL logging in one manual run is recommended before merging).
- Existing test suite uses `Microsoft.EntityFrameworkCore.InMemory`, which does not enforce a single-round-trip constraint. FR-1's "single query" acceptance criterion cannot be fully verified by the InMemory-backed unit tests alone — verify separately against PostgreSQL (e.g., local Docker Postgres per `docs/development/setup.md`, or a relational integration test) before considering the fix validated, or accept correctness-only coverage from the InMemory suite and confirm round-trip reduction via manual `EXPLAIN`/query-log inspection.

## Out of Scope
- Any change to `GetPaginatedAsync`, `GetHeadersByDateAsync`, or other `IssuedInvoiceRepository` methods (not part of the finding).
- Any change to the `GetIssuedInvoiceSyncStats` use case's HTTP contract, request/response DTOs, or the frontend `useIssuedInvoiceSyncStats` hook's polling interval/`staleTime`.
- Adding caching (e.g., response caching, memoization) for sync stats — the finding is specifically about collapsing redundant round-trips within one call, not about reducing call frequency.
- Broader performance work elsewhere in the Invoices module.
- Database indexing changes on `issued_invoices` (e.g., adding an index on `InvoiceDate`) — out of scope unless a follow-up investigation shows the single aggregate query is still slow after this fix.

## Open Questions
None.

## Status: COMPLETE
