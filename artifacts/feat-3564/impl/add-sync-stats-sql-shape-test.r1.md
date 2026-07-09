# Implementation: add-sync-stats-sql-shape-test

## What was implemented
Added `IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs`, a Postgres/Testcontainers-backed
integration test class modeled on the existing `PhotobankRepositoryGetTagsSqlShapeTests`
convention. It uses `PostgresSharedContainerFixture` plus a local `DbCommandInterceptor`
(`CapturingCommandInterceptor`) to capture every SQL command sent to the server and asserts
`GetSyncStatsAsync` issues exactly one command, closing the gap the InMemory-provider test
suite cannot cover (round-trip count is invisible to InMemory).

The regression guard was proven non-tautological: the test was run against the pre-refactor
five-query implementation (`git show <pre-refactor-sha>:...IssuedInvoiceRepository.cs`,
temporarily swapped in, never committed) and failed as expected with exactly 5 commands
recorded. The collapsed implementation was then restored (verified byte-identical to the
already-committed version via `git diff`) and the test suite re-run to confirm green.

## Files created/modified
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs` — new file, 2 integration tests

## Tests
- `GetSyncStatsAsync_EmitsExactlyOneSqlCommand` — asserts exactly 1 SQL command is issued (the NFR-1 round-trip guard)
- `GetSyncStatsAsync_ReturnsCorrectStatsFromRealDatabase` — asserts the aggregate values are correct against a real Postgres instance with seeded rows spanning synced/unsynced/error/paired/out-of-range cases

## How to verify
```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests"
```
Requires Docker. Both tests pass (2/2). `dotnet build` succeeds with 0 errors;
`dotnet format --verify-no-changes` reports no changes needed for the new file.

## Notes
Requires Docker/Testcontainers to run — same requirement as the other `*SqlShapeTests` in
this suite, consistent with existing CI setup.

## PR Summary
Added a Postgres/Testcontainers SQL-shape test that asserts `GetSyncStatsAsync` issues exactly
one SQL command, following the existing `PhotobankRepositoryGetTagsSqlShapeTests` pattern.
Verified the test genuinely catches the 5-query regression by running it against the
pre-refactor implementation (failed with 5 commands recorded) before confirming it passes
against the collapsed implementation.

### Changes
- `backend/test/Anela.Heblo.Tests/Features/Invoices/IssuedInvoiceRepositoryGetSyncStatsSqlShapeTests.cs`

## Status
DONE
