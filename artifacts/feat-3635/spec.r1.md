# Specification: Extract Recurring Job Seeding out of `IRecurringJobConfigurationRepository`

## Summary
`IRecurringJobConfigurationRepository` — a domain repository interface for `RecurringJobConfiguration` CRUD — currently also exposes `SeedDefaultConfigurationsAsync`, a startup-only initialisation operation that has nothing to do with runtime domain data access. This spec extracts that method into a new, narrowly-scoped `IRecurringJobSeeder` service in the Application layer, leaving the repository interface with pure CRUD responsibilities. This is a structural refactor with no behavioral or API change.

## Background
`SeedDefaultConfigurationsAsync` is called exactly once, from `ServiceCollectionExtensions.SeedRecurringJobConfigurationsAsync` (`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, line ~451-474), during application startup after `app.Build()`. It discovers all registered `IRecurringJob` implementations via DI, builds default `RecurringJobConfiguration` rows from their `Metadata`, and inserts any that don't already exist in the database. It is never invoked from any MediatR handler, controller, or other business-logic path.

Because it lives on `IRecurringJobConfigurationRepository`, every test double of that interface — even ones used purely to test domain CRUD behavior in handlers unrelated to seeding — must implement this method. Confirmed instances in the current test suite:
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs:168` — stub returns `Task.CompletedTask`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobDiscoveryServiceTests.cs:186` and `:222` — two more stubs returning `Task.CompletedTask`

This is a Single Responsibility Principle violation: the interface conflates "domain data access contract" with "system initialisation step." The fix is to move the seeding operation to a dedicated, narrow interface owned by the Application layer, and have startup code depend on that instead of reaching into the repository directly.

Note on the brief's suggested signature: the brief's suggested `IRecurringJobSeeder.SeedAsync` signature used `IEnumerable<RecurringJobConfiguration>`. The actual current signature (verified in code) is:
```csharp
Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default);
```
i.e. it takes discovered `IRecurringJob` instances (each carrying `Metadata`), not already-built `RecurringJobConfiguration` entities — the conversion from job metadata to `RecurringJobConfiguration` happens inside the seeding logic itself. This spec uses the verified signature as the source of truth.

## Functional Requirements

### FR-1: Introduce `IRecurringJobSeeder` in the Application layer
Add a new interface, `IRecurringJobSeeder`, in `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IRecurringJobSeeder.cs`:
```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public interface IRecurringJobSeeder
{
    Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default);
}
```
Keep the method name `SeedDefaultConfigurationsAsync` (rather than renaming to `SeedAsync`) to minimize incidental change and preserve call-site clarity; this is a pure extraction, not a rename exercise.

**Acceptance criteria:**
- `IRecurringJobSeeder` exists in `Anela.Heblo.Application/Features/BackgroundJobs/Services/` and takes a dependency only on domain types (`IRecurringJob`), not on EF Core or persistence types.
- The interface has exactly one member.

### FR-2: Implement `RecurringJobSeeder` and move the seeding logic
Add `RecurringJobSeeder` (implementing `IRecurringJobSeeder`) in the same `Services` folder. It depends on `IRecurringJobConfigurationRepository` (for `GetByJobNameAsync`) and on a way to persist new configurations.

Because `SeedDefaultConfigurationsAsync`'s current implementation directly calls `_context.RecurringJobConfigurations.AddAsync(...)` and `_context.SaveChangesAsync(...)` (EF Core, in `RecurringJobConfigurationRepository`), and `IRecurringJobConfigurationRepository` does not currently expose an "add" or "insert" method, the migration must also add a narrow `AddAsync` (or equivalent) method to `IRecurringJobConfigurationRepository` so `RecurringJobSeeder` can persist new configurations without EF Core dependencies leaking into the Application layer. This keeps `IRecurringJobConfigurationRepository` domain-CRUD-only (Get/Add/Update) while removing the startup-only orchestration method.

`RecurringJobSeeder.SeedDefaultConfigurationsAsync` logic (moved from `RecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync`, unchanged in behavior):
1. Build a `RecurringJobConfiguration` for each discovered `IRecurringJob`, from `job.Metadata` (`JobName`, `DisplayName`, `Description`, `CronExpression`, `DefaultIsEnabled`), with `createdBy: "System"`.
2. For each built configuration, check `IRecurringJobConfigurationRepository.GetByJobNameAsync(...)`; if none exists, persist it via the repository's new add method.
3. Preserve existing "no duplicate" semantics: a job whose `JobName` already has a configuration row is left untouched.

**Acceptance criteria:**
- `RecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync` is removed; `IRecurringJobConfigurationRepository` no longer declares it.
- `RecurringJobConfigurationRepository` gains an `AddAsync(RecurringJobConfiguration configuration, CancellationToken)` method (or equivalent) used only for inserting new rows; `SaveChangesAsync` is called once per added configuration or once per batch (behavior-preserving; either is acceptable as long as existing tests below still pass).
- `RecurringJobSeeder.SeedDefaultConfigurationsAsync` produces identical database state to the pre-refactor implementation for the same input: existing tests `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations` and `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate` (moved/adapted to target the new seeder) both pass with 9 configurations seeded and no duplicates.

### FR-3: Register `IRecurringJobSeeder` in DI
Register `IRecurringJobSeeder` → `RecurringJobSeeder` in `BackgroundJobsModule.AddBackgroundJobsModule` (`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`), alongside the existing `IRecurringJobConfigurationRepository` and `IRecurringJobStatusChecker` registrations, using the same lifetime (`Scoped`) as the repository it wraps.

**Acceptance criteria:**
- `services.AddScoped<IRecurringJobSeeder, RecurringJobSeeder>();` is added to `BackgroundJobsModule`.
- No new registration is needed in `ServiceCollectionExtensions.cs` beyond what `AddBackgroundJobsModule` already wires up (verify `AddBackgroundJobsModule` is called before `SeedRecurringJobConfigurationsAsync` executes at startup).

### FR-4: Update the startup call site
In `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`, `SeedRecurringJobConfigurationsAsync` (line ~451-474) currently resolves `IRecurringJobConfigurationRepository` and calls `repository.SeedDefaultConfigurationsAsync(discoveredJobs)`. Change it to resolve `IRecurringJobSeeder` instead and call `seeder.SeedDefaultConfigurationsAsync(discoveredJobs)`.

**Acceptance criteria:**
- `SeedRecurringJobConfigurationsAsync` resolves `IRecurringJobSeeder` from the scope instead of `IRecurringJobConfigurationRepository`.
- Existing logging behavior (success log with discovered-job count; error log + rethrow on failure) is unchanged.
- Application startup still seeds default recurring job configurations exactly as before (verified via existing integration/startup behavior, or the moved unit tests in FR-2).

### FR-5: Update all test doubles of `IRecurringJobConfigurationRepository`
Remove the now-unnecessary `SeedDefaultConfigurationsAsync` stub implementations from test doubles of `IRecurringJobConfigurationRepository`, since the interface no longer declares that method:
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs:168`
- `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobDiscoveryServiceTests.cs:186` and `:222`

Move/adapt the two seeding-behavior tests currently in `RecurringJobConfigurationRepositoryTests.cs` (`SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations`, `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate`) to a new `RecurringJobSeederTests.cs`, targeting `IRecurringJobSeeder`/`RecurringJobSeeder` instead of the repository. If `RecurringJobConfigurationRepositoryTests.cs` gains a new `AddAsync` method under FR-2, add a minimal direct test for it there.

**Acceptance criteria:**
- No test double of `IRecurringJobConfigurationRepository` implements a seeding method.
- Seeding-behavior test coverage (empty-seed and no-duplicate cases) still exists, now against `IRecurringJobSeeder`.
- `dotnet build` and full backend test suite pass with no compiler errors from stale interface members in test doubles.

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected; this is a structural move of existing logic executed once at startup. Seeding must still complete within the existing application startup time budget (no explicit SLA beyond "does not materially slow down startup" — this is a cold-start, one-time operation over a small job list, currently 9 jobs).

### NFR-2: Security
No change. No new attack surface, no new secrets, no change to data sensitivity. `IRecurringJobSeeder` and `RecurringJobSeeder` are internal-only server-side types not exposed via any API surface.

### NFR-3: Backward compatibility
No API contract, DTO, database schema, or migration change. This is an internal C# interface/class reorganization confined to the Application, API, and Persistence projects' internal wiring; no OpenAPI-visible surface changes, so no TypeScript client regeneration is expected. No new EF Core migration is required — `RecurringJobConfiguration` persistence semantics (table, columns) are unchanged; only which class issues the `AddAsync`/`SaveChangesAsync` calls changes.

## Data Model
No changes to the `RecurringJobConfiguration` entity or its EF Core mapping (`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`). No new tables or columns. The only new member is a repository method for inserting a single `RecurringJobConfiguration` (name TBD by implementer, e.g. `AddAsync`), used solely by `RecurringJobSeeder`.

## API / Interface Design

**Before:**
```csharp
// Domain
public interface IRecurringJobConfigurationRepository
{
    Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
    Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default); // removed
}
```

**After:**
```csharp
// Domain — unchanged responsibility, gains a narrow Add primitive
public interface IRecurringJobConfigurationRepository
{
    Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default);
    Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default); // new
    Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
}

// Application/Features/BackgroundJobs/Services — new
public interface IRecurringJobSeeder
{
    Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default);
}
```

**Startup call site (`ServiceCollectionExtensions.cs`):**
```csharp
public static async Task SeedRecurringJobConfigurationsAsync(this WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var seeder = scope.ServiceProvider.GetRequiredService<IRecurringJobSeeder>(); // was: IRecurringJobConfigurationRepository
    var discoveredJobs = scope.ServiceProvider.GetServices<IRecurringJob>();
    await seeder.SeedDefaultConfigurationsAsync(discoveredJobs);
    // ...logging/error handling unchanged
}
```

No public HTTP API, controller, or frontend-visible contract changes.

## Dependencies
- Existing `IRecurringJob` / `RecurringJobMetadata` domain types (unchanged).
- Existing `RecurringJobConfiguration` entity and its EF Core mapping (unchanged).
- `BackgroundJobsModule` DI composition root (Application layer) — gains one new registration.
- No new external libraries or services.

## Out of Scope
- Any change to the seeding *logic* itself (which jobs get seeded, cron defaults, enable/disable defaults) — this is a pure structural extraction.
- Any change to `IRecurringJob`, `RecurringJobMetadata`, `RecurringJobConfiguration`, or the Hangfire scheduling/discovery services beyond updating their test doubles to drop the removed method.
- Database schema/migration changes.
- Renaming `SeedDefaultConfigurationsAsync` to a different method name (e.g. `SeedAsync`) — kept as-is per FR-1 rationale, though the implementer may rename if a strong convention argument arises; not required by this spec.
- Any broader review of other domain repository interfaces for similar SRP violations (out of scope for this specific finding).

## Open Questions

None.

## Status: COMPLETE
