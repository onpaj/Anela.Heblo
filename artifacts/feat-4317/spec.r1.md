# Specification: Batch-load existing configurations in RecurringJobSeeder

## Summary
`RecurringJobSeeder.SeedDefaultConfigurationsAsync` currently issues one `GetByJobNameAsync` database round-trip per discovered recurring job inside a `foreach` loop (an N+1 query pattern), firing ~20-30 sequential SELECTs on every application startup. This change replaces the per-item lookup with a single `GetAllAsync` call and an in-memory dictionary lookup, mirroring the pattern already used by `RecurringJobDiscoveryService.StartAsync`. The change is behavior-preserving: it only alters how existing rows are located, not what is written or when.

## Background
`RecurringJobSeeder` runs at application startup to ensure every `IRecurringJob` implementation discovered via DI has a corresponding `RecurringJobConfiguration` row, creating missing rows and refreshing developer-owned fields (`DisplayName`, `Description`, `TimeZoneId`) on existing rows while preserving admin-owned fields (`CronExpression`, `IsEnabled`).

The current implementation (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`, lines 41-59) looks up each job's existing configuration individually inside the loop via `_repository.GetByJobNameAsync(...)`. With ~20-30 registered jobs, this means 20-30 sequential SELECT statements against the database on every process start, adding avoidable startup latency and DB load.

The sibling service `RecurringJobDiscoveryService.StartAsync` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs`, lines 56-59) already solves the identical problem by calling `repository.GetAllAsync(cancellationToken)` once and building a `Dictionary<string, RecurringJobConfiguration>` keyed by `JobName` for O(1) lookups. The seeder should adopt the same pattern.

The repository interface (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs`) already exposes both methods:
- `Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)`
- `Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)`

so no repository or domain changes are required — this is purely a call-site fix inside the seeder.

## Functional Requirements

### FR-1: Replace per-job lookup with a single batch load
`SeedDefaultConfigurationsAsync` must call `_repository.GetAllAsync(cancellationToken)` exactly once, before the `foreach` loop over `defaultConfigurations`, and build a `Dictionary<string, RecurringJobConfiguration>` keyed by `JobName` (using `StringComparer.Ordinal`, matching the discovery service's convention) for use inside the loop.

**Acceptance criteria:**
- The loop body no longer calls `_repository.GetByJobNameAsync` for existence checks.
- Exactly one call to `_repository.GetAllAsync` occurs per invocation of `SeedDefaultConfigurationsAsync`, regardless of the number of jobs passed in.
- Existing-configuration lookup inside the loop uses `TryGetValue` on the pre-built dictionary, not a repository call.

### FR-2: Preserve all existing create/update semantics exactly
The observable behavior of the method must be unchanged for every case already covered by `RecurringJobSeederTests`:
- A job with no existing row gets a new `RecurringJobConfiguration` created via `_repository.AddAsync`.
- A job with an existing row gets updated via `existing.UpdateConfiguration(...)` and `_repository.UpdateAsync`, with `DisplayName`, `Description`, and `TimeZoneId` refreshed from metadata, while `CronExpression` and `IsEnabled` are preserved from the existing row (not overwritten from metadata/defaults).
- `LastModifiedBy` is set to `"System"` and `LastModifiedAt` is set from `_timeProvider.GetUtcNow().UtcDateTime` on every update, exactly as before.

**Acceptance criteria:**
- All five existing tests in `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs` pass unmodified against the new implementation.

### FR-3: No change to the public method signature or call sites
`SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)` keeps its exact signature. `IRecurringJobSeeder` and all callers of `SeedDefaultConfigurationsAsync` remain unchanged.

**Acceptance criteria:**
- No changes to `IRecurringJobSeeder` or any file outside `RecurringJobSeeder.cs` (and, if added, its unit test) are required to satisfy this fix.

## Non-Functional Requirements

### NFR-1: Performance
Startup seeding must issue at most 2 database round-trips total (one `GetAllAsync` read, plus one write per job needing insert/update, as before) instead of N+1 (N `GetByJobNameAsync` reads + one write per job). This eliminates the N sequential read round-trips that scale with job count.

### NFR-2: Security
No change. No new inputs, no new data exposure; the fix only changes internal query shape.

## Data Model
No schema or entity changes. `RecurringJobConfiguration` and `IRecurringJobConfigurationRepository` are unchanged. The seeder consumes the already-existing `GetAllAsync` method.

## API / Interface Design
No public API, controller, or DTO changes. This is an internal implementation change to a single application-service method (`RecurringJobSeeder.SeedDefaultConfigurationsAsync`), invoked during host startup before Hangfire recurring-job registration.

## Dependencies
- `IRecurringJobConfigurationRepository.GetAllAsync` — already implemented and used by `RecurringJobDiscoveryService`; no changes needed.
- No external services or new libraries required.

## Out of Scope
- Changing `RecurringJobDiscoveryService` itself (it already uses the correct pattern; it is a reference implementation only).
- Any change to the `IRecurringJobConfigurationRepository` interface or its EF Core implementation.
- Any change to how jobs are discovered or registered with Hangfire.
- Adding caching, batching writes, or otherwise changing the write path (`AddAsync`/`UpdateAsync` per job remain per-job calls, consistent with the issue's suggested fix).

## Open Questions
None.

## Status: COMPLETE
