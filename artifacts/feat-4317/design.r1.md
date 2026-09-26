# Design: Batch-load existing configurations in RecurringJobSeeder

## Component Design

### `RecurringJobSeeder` (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`)
Implements `IRecurringJobSeeder`. Responsibility is unchanged: given the set of discovered `IRecurringJob` implementations, ensure every job has a `RecurringJobConfiguration` row, creating missing rows and refreshing developer-owned fields on existing rows while preserving admin-owned fields.

**Internal restructuring of `SeedDefaultConfigurationsAsync`:**

- **Before:** for each of the N default configurations built from job metadata, call `_repository.GetByJobNameAsync(config.JobName, cancellationToken)` inline inside the loop → N sequential reads.
- **After:**
  1. Build `defaultConfigurations` from job metadata (unchanged).
  2. Call `_repository.GetAllAsync(cancellationToken)` once to get every existing `RecurringJobConfiguration` row.
  3. Project the result into `existingByName = existing.ToDictionary(c => c.JobName, StringComparer.Ordinal)`.
  4. For each entry in `defaultConfigurations`, replace the `GetByJobNameAsync` call with `existingByName.TryGetValue(config.JobName, out var existingConfig)`.
  5. Branch exactly as before: `existingConfig == null` → `AddAsync`; otherwise `existingConfig.UpdateConfiguration(...)` then `UpdateAsync`.

No new class, method, or public member is introduced. `_repository` and `_timeProvider` fields, the constructor, and the class's public surface (`IRecurringJobSeeder.SeedDefaultConfigurationsAsync`) are unchanged.

### Collaborators (unchanged, referenced for context only)
- `IRecurringJobConfigurationRepository` (`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs`) — no interface changes; `GetAllAsync` and `GetByJobNameAsync` both already exist. `GetByJobNameAsync` remains on the interface (still used elsewhere, e.g. by the test suite's assertions) but the seeder itself no longer calls it.
- `RecurringJobConfigurationRepository` (`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`) — no changes.
- `RecurringJobDiscoveryService` (`backend/src/Anela.Heblo.API/Infrastructure/Hangfire/RecurringJobDiscoveryService.cs`) — reference implementation only; not modified by this change.

## Data Schemas
No schema changes. No new or modified DTOs, request/response shapes, or event payloads. `RecurringJobConfiguration` (the EF Core entity, keyed by `JobName` as `Id`) is read and written exactly as before — only the read access pattern (one bulk read instead of N targeted reads) changes.
