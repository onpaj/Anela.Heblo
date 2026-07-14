# Design: Eliminate N+1 Queries in RecurringJobConfiguration Seeding

## Component Design

`RecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync` (the only component touched) changes its internal query strategy but keeps its existing responsibility and public contract unchanged: given a set of discovered `IRecurringJob` instances, insert a default `RecurringJobConfiguration` row for any job that does not already have one.

- Load all existing `JobName` values with a single projection query (`Select(c => c.JobName)`), materialized via `ToListAsync` + `new HashSet<string>(...)` (EF Core 8.0.8 does not have `ToHashSetAsync`, per the architecture review).
- Filter the default configurations in-memory against that set, inserting only the missing ones and adding each newly queued name back into the set (guards against duplicate `JobName`s within the same discovered-job batch).
- Persist with a single `SaveChangesAsync` call, as today.

No new components, no new interfaces, no new files.

## Data Schemas

No schema changes. `RecurringJobConfiguration` entity and the `RecurringJobConfigurations` table are unchanged; only the read pattern used to seed them changes.
