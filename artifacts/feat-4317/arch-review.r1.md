# Architecture Review: Batch-load existing configurations in RecurringJobSeeder

## Skip Design: true

## Architectural Fit Assessment
This is a pure backend performance fix confined to a single application-service method, with no new components, no schema change, and no public contract change. It aligns exactly with an existing, working pattern in the same codebase: `RecurringJobDiscoveryService.StartAsync` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs`, lines 56-59) already replaces per-item lookups with one `GetAllAsync` call plus a `Dictionary<string, RecurringJobConfiguration>` built by `JobName`. `RecurringJobSeeder` and `RecurringJobDiscoveryService` are siblings that both run at startup against the same `IRecurringJobConfigurationRepository`, so mirroring the discovery service's lookup pattern in the seeder is the natural, minimal-risk fix — no new abstraction is needed, and no existing abstraction needs to change.

One repository detail confirmed by reading `RecurringJobConfigurationRepository` (`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`): neither `GetAllAsync` nor `GetByJobNameAsync` uses `AsNoTracking()`, so entities returned by `GetAllAsync` remain tracked by the `ApplicationDbContext` in the same way entities returned by `GetByJobNameAsync` are today. This means switching the lookup source does not change EF Core tracking/update semantics — `existing.UpdateConfiguration(...)` followed by `_repository.UpdateAsync(existing, ...)` behaves identically whether `existing` came from `GetAllAsync` or `GetByJobNameAsync`.

## Proposed Architecture

### Component Overview
```
RecurringJobSeeder.SeedDefaultConfigurationsAsync(jobs)
    │
    ├─ 1. build defaultConfigurations[] from job metadata   (unchanged)
    │
    ├─ 2. NEW: existing = await _repository.GetAllAsync(ct)      -- single query
    │         existingByName = existing.ToDictionary(c => c.JobName, StringComparer.Ordinal)
    │
    └─ 3. foreach config in defaultConfigurations:
             existingByName.TryGetValue(config.JobName, out existingConfig)   -- in-memory, no query
             ├─ not found → _repository.AddAsync(config, ct)
             └─ found     → existingConfig.UpdateConfiguration(...); _repository.UpdateAsync(existingConfig, ct)
```
No change to `IRecurringJobConfigurationRepository`, `RecurringJobConfiguration`, or `IRecurringJobSeeder`. Only the body of `SeedDefaultConfigurationsAsync` changes.

### Key Design Decisions

#### Decision 1: Batch-load via `GetAllAsync` + dictionary, instead of introducing a new bulk-lookup repository method
**Options considered:**
- (a) Add a new repository method like `GetByJobNamesAsync(IEnumerable<string> jobNames)` that filters server-side by the exact set of job names.
- (b) Reuse the existing `GetAllAsync()` (already used by `RecurringJobDiscoveryService`) and do the name matching in memory.

**Chosen approach:** (b) — reuse `GetAllAsync()`.

**Rationale:** Job count is small (~20-30, bounded by the number of `IRecurringJob` implementations registered in DI, not by any external/user-controlled input), so loading the full table is cheap and this is what the codebase's own reference implementation (`RecurringJobDiscoveryService`) already does. Introducing a second repository method for the same table would duplicate the pattern the issue is explicitly asking to reuse, add an untested code path to the repository, and give two different ways of reading the same data for no benefit. Option (b) requires zero repository/domain changes, which keeps the fix minimal and consistent with the issue's own suggested fix.

#### Decision 2: Dictionary key comparer — `StringComparer.Ordinal`
**Options considered:**
- Default `Dictionary<string, T>` comparer (ordinal, case-sensitive) with no explicit comparer specified.
- Explicit `StringComparer.Ordinal`, matching `RecurringJobDiscoveryService`'s `dbConfigs.ToDictionary(c => c.JobName, c => c)` — note the discovery service actually relies on the *default* comparer (it doesn't pass one explicitly), which is ordinal for `string` keys.

**Chosen approach:** Use `StringComparer.Ordinal` explicitly in the seeder.

**Rationale:** `JobName` is also the table's primary key (`GetByJobNameAsync` matches `c.Id == jobName`), so comparisons must be exact and culture-invariant. Being explicit costs nothing and documents the intent at the call site; behavior is identical to the discovery service's implicit default either way, so there is no divergence in matching semantics between the two services.

## Implementation Guidance

### Directory / Module Structure
No new files. Edit only:
- `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`

Test file to update in place (no new test file needed — existing tests already cover the required behavior and must continue to pass unmodified):
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`

### Interfaces and Contracts
No interface changes. `IRecurringJobSeeder`, `IRecurringJobConfigurationRepository`, and `RecurringJobConfiguration` are all unchanged. `SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)` keeps its exact signature and remains the only public entry point.

### Data Flow
Startup host (`RecurringJobDiscoveryService` or whichever startup hook invokes the seeder) calls `SeedDefaultConfigurationsAsync(jobs)` once per process start:
1. Build in-memory `defaultConfigurations` from discovered `IRecurringJob.Metadata` (unchanged, no DB access).
2. One `GetAllAsync` call reads every row from `RecurringJobConfigurations` into memory; build a `JobName -> RecurringJobConfiguration` dictionary.
3. For each discovered job, look up in the dictionary (no DB access) and either `AddAsync` (insert) or mutate-then-`UpdateAsync` (update). Total DB writes are unchanged — still one write per job needing insert/update — only the N read round-trips collapse to 1.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Reusing a tracked entity from `GetAllAsync` for a subsequent `UpdateAsync` call behaves differently than one from `GetByJobNameAsync` | Low | Confirmed by reading `RecurringJobConfigurationRepository`: neither method uses `AsNoTracking()`, so tracking behavior is identical either way; `UpdateAsync`'s `_context.Update(configuration)` is a no-op state transition for an already-tracked, already-modified entity. No mitigation needed beyond this verification. |
| Regression in create/update semantics (e.g. accidentally reintroducing a second DB read, or breaking the admin-owned-field preservation logic) | Low | The five existing tests in `RecurringJobSeederTests.cs` already assert every behavior in scope (no duplication, DisplayName/Description/TimeZoneId resync, CronExpression/IsEnabled preservation, LastModifiedBy/LastModifiedAt correctness) and must pass unmodified; add one focused test asserting `GetAllAsync` is called at most once and `GetByJobNameAsync` is not called at all (e.g. via a spy/mock repository or an EF Core query-count assertion, consistent with existing query-count tests elsewhere in the test suite, e.g. `PackingMaterialsListQueryCountTests.cs`). |
| Empty `jobs` collection or empty existing table | Low | Both are already handled naturally: an empty `defaultConfigurations` loop body never touches `existingByName`; an empty table makes `GetAllAsync` return an empty list and every job takes the "not found → AddAsync" path, identical to current behavior. |

## Specification Amendments
None. The specification in `spec.r1.md` is implementable as written. One addition worth making explicit in the task plan: include a query-count regression test (see Risks table) so the N+1 fix is verified by an automated test, not just by code inspection, and cannot silently regress later.

## Prerequisites
None. No migrations, no config, no infrastructure changes — `GetAllAsync` and the underlying table already exist and are already exercised by `RecurringJobDiscoveryService` in production.
