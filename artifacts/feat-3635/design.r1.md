# Design: Extract Recurring Job Seeding out of `IRecurringJobConfigurationRepository`

## Component Design

### `IRecurringJobConfigurationRepository` (Domain layer)
`backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs`

Responsibility narrows to pure CRUD data access for `RecurringJobConfiguration`. Loses `SeedDefaultConfigurationsAsync`; gains a narrow `AddAsync` primitive so callers outside the Persistence layer can insert a new row without EF Core.

```csharp
public interface IRecurringJobConfigurationRepository
{
    Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default);
    Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
}
```

- Consumers of `GetAllAsync` / `GetByJobNameAsync` / `UpdateAsync` (`RecurringJobDiscoveryService`, `GetRecurringJobHandler`, `GetRecurringJobsListHandler`, `UpdateRecurringJobCronHandler`, `UpdateRecurringJobStatusHandler`, `RecurringJobStatusChecker`) are unaffected.
- `AddAsync` follows the same unit-of-work convention as `UpdateAsync`: it stages the entity and calls `SaveChangesAsync()` within the same call — each public method on this interface is a complete, self-committing operation. No `SaveChangesAsync`/`IUnitOfWork` is exposed to callers.

### `RecurringJobConfigurationRepository` (Persistence layer, EF Core)
`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`

- Removes `SeedDefaultConfigurationsAsync`.
- Adds `AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken)`: calls `_context.RecurringJobConfigurations.AddAsync(configuration, cancellationToken)` then `_context.SaveChangesAsync(cancellationToken)`.
- No change to `GetAllAsync`, `GetByJobNameAsync`, `UpdateAsync`, or the EF Core mapping.

### `IRecurringJobSeeder` (new, Application layer)
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IRecurringJobSeeder.cs`

Single-purpose interface, one member, depends only on the domain type `IRecurringJob` — no EF Core / persistence types.

```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public interface IRecurringJobSeeder
{
    Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default);
}
```

### `RecurringJobSeeder` (new, Application layer)
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`

- Implements `IRecurringJobSeeder`.
- Depends only on `IRecurringJobConfigurationRepository` (both Domain-layer types) — no `ApplicationDbContext`, keeping Application → Domain the only dependency direction (Application never references Persistence/EF Core).
- Logic, moved unchanged from the old repository method:
  1. For each `IRecurringJob`, build a `RecurringJobConfiguration` from `job.Metadata` (`JobName`, `DisplayName`, `Description`, `CronExpression`, `DefaultIsEnabled`), with `createdBy: "System"`.
  2. Call `_repository.GetByJobNameAsync(config.JobName, cancellationToken)`.
  3. If `null`, call `_repository.AddAsync(config, cancellationToken)`. If a configuration already exists for that `JobName`, skip it (no update, no duplicate).
- Behavior-preserving: same set of seeded jobs, same defaults, same duplicate-avoidance semantics as the pre-refactor implementation. Only the commit granularity changes (up to one `SaveChangesAsync` per added row via `AddAsync`, instead of one batched `SaveChangesAsync` after the loop) — acceptable per spec NFR-1 for a 9-row, one-time startup operation.

### DI registration — `BackgroundJobsModule`
`backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`

Add, immediately after the existing repository registration, using the same `Scoped` lifetime:

```csharp
services.AddScoped<IRecurringJobConfigurationRepository, RecurringJobConfigurationRepository>();
services.AddScoped<IRecurringJobSeeder, RecurringJobSeeder>();
```

### Startup call site — `ServiceCollectionExtensions.SeedRecurringJobConfigurationsAsync`
`backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs` (~line 451-474)

Resolves `IRecurringJobSeeder` instead of `IRecurringJobConfigurationRepository`; everything else (scope creation, discovered-job resolution via `GetServices<IRecurringJob>()`, success/error logging, rethrow-on-failure) is unchanged.

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

`AddBackgroundJobsModule` already runs before this call at startup (`Program.cs`), so `IRecurringJobSeeder` is available in the container without any reordering.

### Test doubles

- `HangfireRecurringJobSchedulerTests.cs` (`EmptyStubRepository`) and `RecurringJobDiscoveryServiceTests.cs` (`StubRecurringJobConfigurationRepository`, `StubDbRecurringJobConfigurationRepository`): drop the now-nonexistent `SeedDefaultConfigurationsAsync` stub method; no other change, since these stubs don't implement the new `AddAsync` unless they need to (interface requires it — add a trivial `Task.CompletedTask`/no-op implementation if the stub is a full manual implementer of the interface).
- `RecurringJobConfigurationRepositoryTests.cs`: remove `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations` and `SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate`; add a minimal direct test for the new `AddAsync` (insert one configuration, assert it is retrievable via `GetByJobNameAsync`/`GetAllAsync`).
- New `RecurringJobSeederTests.cs` (same folder): moves/adapts the two seeding-behavior tests to target `IRecurringJobSeeder`/`RecurringJobSeeder`, using a real in-memory `ApplicationDbContext` and real `RecurringJobConfigurationRepository` wrapped by a real `RecurringJobSeeder` (no repository mocking), asserting final DB state (9 configurations seeded, no duplicates on re-run) exactly as today.
- The five Moq-based test files (`GetRecurringJobHandlerTests`, `GetRecurringJobsListHandlerTests`, `RecurringJobStatusCheckerTests`, `UpdateRecurringJobCronHandlerTests`, `UpdateRecurringJobStatusHandlerTests`) require no change — they use loose mocks of `IRecurringJobConfigurationRepository` and never reference the removed/added members.

## Data Schemas

No database schema change. `RecurringJobConfiguration` entity and its EF Core mapping (`backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationConfiguration.cs`) are unchanged — same table, same columns, no new migration.

The only new shape is the repository method signature:

```csharp
Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
```

Input: a fully-constructed `RecurringJobConfiguration` (built by `RecurringJobSeeder` from `IRecurringJob.Metadata`: `JobName`, `DisplayName`, `Description`, `CronExpression`, `DefaultIsEnabled`, `CreatedBy = "System"`). Output: `Task` (void on success; persistence/EF exceptions propagate to the caller, matching `UpdateAsync`'s existing error-handling shape).

No HTTP/API request or response shapes change — no controller, DTO, or OpenAPI surface is touched, so no TypeScript client regeneration is required. No event payloads are introduced or altered.
