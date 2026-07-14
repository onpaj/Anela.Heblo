# Implementation: fix-recurring-job-seeding-n-plus-one

## What was implemented
Replaced the per-job `GetByJobNameAsync` lookup inside `SeedDefaultConfigurationsAsync`'s `foreach` loop with a single query that loads all existing `JobName` values into a `HashSet<string>`, then filters the default configurations in-memory before inserting only the missing ones. `SaveChangesAsync` is still called exactly once at the end.

## Files created/modified
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs` — `SeedDefaultConfigurationsAsync` now issues one `SELECT JobName` query instead of N `GetByJobNameAsync` calls; a `HashSet<string>` guard also prevents inserting duplicate `JobName`s if the discovered-job batch itself contains duplicates.

## Tests
`backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs` — no changes needed. These existing tests already pin the required behavior and passed unchanged:
- `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations`
- `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate`

## How to verify
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobConfigurationRepositoryTests"
# Passed! - Failed: 0, Passed: 8, Skipped: 0, Total: 8

dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
# Build succeeded. 0 Warning(s), 0 Error(s)

dotnet format backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj --verify-no-changes
# Exit 0, no formatting changes needed
```

## Notes
Per the architecture review (Decision 1), used `.Select(c => c.JobName).ToListAsync(cancellationToken)` wrapped in `new HashSet<string>(...)` rather than `ToHashSetAsync`, since EF Core 8.0.8 (this project's pinned version) does not provide `ToHashSetAsync` (added in EF Core 9.0). No interface, call-site, entity, or schema changes were needed.

## PR Summary
Fixed an N+1 query anti-pattern in `RecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync`, which ran one `GetByJobNameAsync` database round-trip per discovered recurring job at every application startup. Replaced it with a single query that loads all existing job names into a `HashSet<string>`, then filters the default configurations in-memory before a single `SaveChangesAsync`. Behavior is unchanged — only configurations for genuinely missing jobs are inserted — and the existing repository tests pass without modification.

### Changes
- `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs` — single-query + in-memory-filter seeding instead of per-job lookups

## Status
DONE
