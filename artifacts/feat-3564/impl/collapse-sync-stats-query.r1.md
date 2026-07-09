# Implementation: collapse-sync-stats-query

## What was implemented
Rewrote `IssuedInvoiceRepository.GetSyncStatsAsync` to replace five sequential
`CountAsync`/`MaxAsync` round-trips with a single `GroupBy(_ => 1).Select(...)`
aggregate projection, translated by EF Core into one SQL statement with multiple
`COUNT(CASE ...)`/`MAX` aggregates. Handled the empty-result case explicitly
(when no invoices fall in the date range, `FirstOrDefaultAsync` returns `null`
and the method now returns a zeroed `IssuedInvoiceSyncStats` instead of the
prior code path, which never had to worry about this because each `CountAsync`
independently returns `0` for an empty set).

## Files created/modified
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs` — `GetSyncStatsAsync` now issues one query instead of five
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs` — added regression coverage for `LastSyncTime` (mixed sync times, and the case where no in-range invoice has ever synced)

## Tests
- `IssuedInvoiceRepositoryTests.GetSyncStatsAsync_*` (12 tests total, all passing) — covers total/synced/unsynced counts, critical-error counting, and `LastSyncTime` aggregation including the null case

## How to verify
```bash
cd backend
dotnet build Anela.Heblo.sln
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IssuedInvoiceRepositoryTests"
```
All 12 tests pass; build has 0 errors (163 pre-existing warnings unrelated to this change).

## Notes
The EF Core InMemory provider used by these tests cannot verify the actual SQL
round-trip count — that is covered by task 2 (`add-sync-stats-sql-shape-test`),
which adds a Postgres/Testcontainers-based test asserting exactly one SQL
command is issued.

## PR Summary
Collapsed `IssuedInvoiceRepository.GetSyncStatsAsync`'s five sequential
`CountAsync`/`MaxAsync` calls into a single EF Core `GroupBy` aggregate
projection, cutting the sync-stats endpoint from five DB round-trips to one.
Added regression tests for the `LastSyncTime` aggregation, including the
never-synced case.

### Changes
- `backend/src/Anela.Heblo.Persistence/Invoices/IssuedInvoiceRepository.cs`
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryTests.cs`

## Status
DONE
