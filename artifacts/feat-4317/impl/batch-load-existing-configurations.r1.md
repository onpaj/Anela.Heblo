# Implementation: batch-load-existing-configurations

## What was implemented

Replaced the per-job `GetByJobNameAsync` call inside `RecurringJobSeeder.SeedDefaultConfigurationsAsync`'s
`foreach` loop with a single `GetAllAsync` call up front, followed by an in-memory
dictionary lookup (`Dictionary<string, RecurringJobConfiguration>` keyed on `JobName`,
`StringComparer.Ordinal`). This eliminates the N+1 query pattern (previously one
`GetByJobNameAsync` DB read per discovered job) in favor of a single read followed
by O(1) in-memory lookups, mirroring the existing pattern already used in
`RecurringJobDiscoveryService.StartAsync`.

The create/update semantics are unchanged: a job with no existing row is added via
`AddAsync`; a job with an existing row has its `DisplayName`/`Description`/`TimeZoneId`
updated via `UpdateConfiguration` while the admin-owned `CronExpression` is read from
and preserved on the same dictionary-matched entity (`existingConfig`), exactly as the
original code preserved it from the freshly-fetched `existing` entity.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` —
  `SeedDefaultConfigurationsAsync` now calls `_repository.GetAllAsync(cancellationToken)`
  once, builds `existingByName` via `ToDictionary(c => c.JobName, StringComparer.Ordinal)`,
  and looks up each discovered job's existing configuration (if any) via
  `existingByName.TryGetValue(...)` instead of calling `GetByJobNameAsync` per job.

## Tests

No new test file was added by this task — it makes the pre-existing regression test
(added by the `add-query-count-regression-test` task,
`RecurringJobSeederTests.SeedDefaultConfigurationsAsync_IssuesSingleBatchReadInsteadOfPerJobLookups`)
pass, along with all other pre-existing `RecurringJobSeederTests`.

## How to verify

```bash
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests"
```

Expected: `Passed! - Failed: 0, Passed: 7, Skipped: 0, Total: 7` (the 6 pre-existing
tests plus the new batch-read regression test).

## Notes

- The task context's Step 4/Step 5 commands (`cd backend && dotnet build` / `dotnet test`)
  don't resolve directly, since `backend/` contains no `.sln` and multiple `.csproj`
  files live under `backend/test/`. I ran the equivalent commands against the repo-root
  solution instead: `dotnet build Anela.Heblo.sln` (0 errors, pre-existing warnings only,
  none new) and `dotnet format Anela.Heblo.sln --include <the changed file>` (clean, no
  reformatting needed).
- Ran the full `Anela.Heblo.Tests.csproj` suite: 7868 passed, 4 skipped, 111 failed.
  All 111 failures are `System.ArgumentException: Docker is either not running or
  misconfigured` from Testcontainers-backed integration/repository/SQL-shape tests
  (Leaflet, KnowledgeBase, Bank, Smartsupp, GridLayouts, MeetingTasks, GiftPackageManufacture,
  TransportBox, Photobank, InvoiceClassification, Invoices, Article, Purchase,
  MarketingPerformance, Catalog) — a documented pre-existing environment limitation
  (no Docker available in this sandbox; see `memory/context/state.md`), unrelated to
  this change. No failure references `RecurringJobSeeder` or `BackgroundJobs`.
- No interface, DI registration, or call site outside `RecurringJobSeeder.cs` was touched.

## PR Summary
Fixes the N+1 query pattern in `RecurringJobSeeder.SeedDefaultConfigurationsAsync`: instead of issuing one `GetByJobNameAsync` database read per discovered recurring job inside the seeding loop, the method now issues a single `GetAllAsync` read up front and looks up each job's existing configuration from an in-memory dictionary. This mirrors the pattern already used by `RecurringJobDiscoveryService.StartAsync` and reduces startup seeding from N+1 sequential DB reads to 1, with no change to create/update semantics or the admin-owned `CronExpression` override preservation.

### Changes
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs` — replaced the per-job `GetByJobNameAsync` loop body with a single `GetAllAsync` call plus dictionary lookup

## Status
DONE
