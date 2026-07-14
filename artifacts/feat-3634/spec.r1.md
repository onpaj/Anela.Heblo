# Specification: Eliminate N+1 Queries in RecurringJobConfiguration Seeding

## Summary
`RecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync` currently issues one `SELECT` per discovered recurring job (an N+1 query pattern) to check for existing configurations before inserting missing ones. This spec defines replacing that per-job lookup with a single query that loads all existing job names once, then filters in-memory. The change is a behavior-preserving performance fix executed at every application startup; there is no user-facing surface and no schema change.

## Background
On application startup, `SeedRecurringJobConfigurationsAsync` (in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, line ~451) resolves all discovered `IRecurringJob` implementations from DI and calls `SeedDefaultConfigurationsAsync(discoveredJobs)`. The repository (`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`, lines 40–62) builds default `RecurringJobConfiguration` records from each job's metadata, then loops over them, calling `GetByJobNameAsync` (a `FirstOrDefaultAsync` against `RecurringJobConfigurations`) once per job to decide whether the configuration already exists. For N registered jobs this produces N `SELECT` round-trips plus one final `SaveChangesAsync`, i.e. N+1 database round-trips at every startup.

There are currently 9 registered jobs (confirmed by `RecurringJobConfigurationRepositoryTests`), so the present cost is modest, but it grows linearly with each new job and adds avoidable startup latency and connection-pool pressure. The architecture review flagged this as a well-known anti-pattern that is fixable at effectively zero cost while preserving the existing "insert only jobs that do not already exist" semantics.

The fix is to load the set of existing `JobName` values in a single query, then filter the default configurations in-memory before adding the missing ones and saving once.

## Functional Requirements

### FR-1: Single query to load existing job names
`SeedDefaultConfigurationsAsync` must load all existing `JobName` values from `RecurringJobConfigurations` in exactly one database query instead of one query per job.

**Acceptance criteria:**
- The method issues exactly one read query against `RecurringJobConfigurations` regardless of the number of discovered jobs (verifiable by inspection; the per-job `GetByJobNameAsync` call inside the loop is removed).
- The existing names are materialized into a set keyed by `JobName` (e.g. `HashSet<string>`) for O(1) membership checks.
- The query projects only `JobName` (`Select(c => c.JobName)`), not full entities.

### FR-2: Preserve insert-only-when-missing semantics
The method must insert a configuration if and only if no configuration with the same `JobName` already exists, exactly as today.

**Acceptance criteria:**
- Given an empty table and N discovered jobs, all N configurations are inserted (existing test `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations` passes unchanged, expecting 9 rows).
- Given a table that already contains a configuration for one of the discovered job names, that configuration is not duplicated and only the missing ones are inserted (existing test `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate` passes unchanged, expecting 9 rows total and a single `purchase-price-recalculation` row).
- Existing configurations are never updated, deleted, or otherwise mutated by this method; the "seed on first run only" behavior documented in the design docs is retained.

### FR-3: Single save
All inserts continue to be persisted with a single `SaveChangesAsync` call after the loop.

**Acceptance criteria:**
- `SaveChangesAsync` is invoked exactly once per call to `SeedDefaultConfigurationsAsync`.
- When there are no missing configurations, the method still completes successfully (a `SaveChangesAsync` with no tracked changes is a no-op and acceptable).

### FR-4: Deduplicate within the incoming job set (edge case)
If the discovered job collection contains two jobs with the same `JobName` and that name is not yet in the database, the method must not attempt to insert two rows with the same key in a single save.

**Acceptance criteria:**
- The in-memory filter guards against inserting a name that has already been queued for insertion during the same call, in addition to names already present in the database. (Assumption: duplicate `JobName`s in the discovered set are not expected in practice; see Open Questions. This criterion may be satisfied by tracking newly added names in the same set used for the existing-name check.)

### FR-5: Signature and behavioral contract unchanged
The public signature of `SeedDefaultConfigurationsAsync` and the `IRecurringJobConfigurationRepository` interface are unchanged. `cancellationToken` continues to be threaded through the query and save.

**Acceptance criteria:**
- No change to `IRecurringJobConfigurationRepository` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs`).
- No change to the call site in `ServiceCollectionExtensions.SeedRecurringJobConfigurationsAsync`.
- `cancellationToken` is passed to both the read query and `SaveChangesAsync`.

## Non-Functional Requirements

### NFR-1: Performance
Database round-trips for seeding drop from N+1 to 2 (one read, one write), independent of the number of registered jobs. This must measurably reduce startup query count; the improvement is O(N) → O(1) in read round-trips. No regression in overall startup time is acceptable.

### NFR-2: Security
No change to authentication, authorization, or data exposure. The method runs only at application startup within a DI scope and touches only the `RecurringJobConfigurations` table. No new data is logged; job names are non-sensitive configuration identifiers.

### NFR-3: Correctness under concurrency
Seeding runs once during single-instance startup and is not expected to race with other writers. The fix does not introduce a transaction boundary weaker than today's (today's per-job reads are already non-transactional relative to the final save). If two application instances start simultaneously against a shared database, a duplicate-key insert is theoretically possible under both the old and new code; this is pre-existing behavior and out of scope (see Open Questions).

### NFR-4: Maintainability
The resulting code should be shorter and clearer than the current loop, with no new dependencies. Must satisfy `dotnet build` and `dotnet format`.

## Data Model
No schema changes.

Entity `RecurringJobConfiguration` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/RecurringJobConfiguration.cs`), persisted to table `RecurringJobConfigurations`. Relevant field for this change:
- `JobName` (string) — unique business key used to determine whether a configuration already exists. Other fields (`DisplayName`, `Description`, `CronExpression`, `IsEnabled`/`DefaultIsEnabled`, `LastModifiedBy`, etc.) are populated from `IRecurringJob.Metadata` and are unaffected.

## API / Interface Design
No public API, endpoint, or event changes. This is an internal repository implementation change only.

Target method (proposed shape, per the review's suggested fix):

```csharp
public async Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
{
    var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
        job.Metadata.JobName,
        job.Metadata.DisplayName,
        job.Metadata.Description,
        job.Metadata.CronExpression,
        job.Metadata.DefaultIsEnabled,
        "System"
    )).ToArray();

    var existingNames = await _context.RecurringJobConfigurations
        .Select(c => c.JobName)
        .ToHashSetAsync(cancellationToken);

    foreach (var config in defaultConfigurations.Where(c => !existingNames.Contains(c.JobName)))
    {
        await _context.RecurringJobConfigurations.AddAsync(config, cancellationToken);
        existingNames.Add(config.JobName); // guards FR-4 in-set duplicates
    }

    await _context.SaveChangesAsync(cancellationToken);
}
```

Note: `ToHashSetAsync` requires `Microsoft.EntityFrameworkCore` (already imported in the file). Verify the installed EF Core version exposes `ToHashSetAsync`; if not, use `.ToListAsync(cancellationToken)` followed by `new HashSet<string>(...)`.

## Dependencies
- Entity Framework Core (`Microsoft.EntityFrameworkCore`) — already referenced; provides `ToHashSetAsync`/`ToListAsync`.
- `ApplicationDbContext` and the `RecurringJobConfigurations` `DbSet` — unchanged.
- Existing test project `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs`, which uses an in-memory/EF context and must continue to pass without modification.

## Out of Scope
- Any change to the discovery, registration, or scheduling of recurring jobs (`HangfireRecurringJobScheduler`, `RecurringJobDiscoveryService`).
- Adding a unique database constraint/index on `JobName` (would be a separate hardening task).
- Making seeding safe against concurrent multi-instance startup (distributed lock, upsert, retry on duplicate key).
- Batching/bulk-insert optimization of the `AddAsync`/`SaveChangesAsync` write path beyond the existing single save.
- Updating existing configurations when job metadata changes (seeding remains first-run-only, as designed).
- Any frontend, API surface, or documentation changes beyond incidental code comments.

## Open Questions
None.

## Status: COMPLETE
