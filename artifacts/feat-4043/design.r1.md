# Design: RecurringJobSeeder uses TimeProvider instead of DateTime.UtcNow

## Component Design

### `RecurringJobSeeder` (Application layer, `Features/BackgroundJobs/Services/RecurringJobSeeder.cs`)
Implements `IRecurringJobSeeder` (unchanged interface: `Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)`).

**Responsibility (unchanged):** at application startup, reconcile discovered `IRecurringJob` implementations against persisted `RecurringJobConfiguration` rows — create a row for any job that has none, and for jobs that already have a row, refresh developer-owned fields (`DisplayName`, `Description`, `TimeZoneId`) while preserving admin-owned fields (`CronExpression`, `IsEnabled`) exactly as stored.

**Constructor contract (changed):**
```csharp
public RecurringJobSeeder(
    IRecurringJobConfigurationRepository repository,
    TimeProvider timeProvider)
```
- `repository` — unchanged dependency, unchanged usage (`GetByJobNameAsync`, `AddAsync`, `UpdateAsync`).
- `timeProvider` — new dependency. Resolved automatically by DI from the existing application-wide `TimeProvider.System` singleton (`ServiceCollectionExtensions.cs:135`); no registration change needed in `BackgroundJobsModule.cs`.
- No constructor null-guards are added (matches this class's existing unguarded style; the guarded style used by sibling MediatR handlers is not adopted here — see arch-review.r1.md, "Interfaces and Contracts").

**Internal behavior (changed):**
- `SeedDefaultConfigurationsAsync` computes `var now = _timeProvider.GetUtcNow().UtcDateTime;` once, at the top of the method, before the `Select(...)` projection that builds `defaultConfigurations`.
- The `Select(...)` projection's `RecurringJobConfiguration(...)` constructor call uses `now` in place of `DateTime.UtcNow` for the `lastModifiedAt` argument.
- The `foreach` loop's `existing.UpdateConfiguration(...)` call uses `now` in place of `DateTime.UtcNow` for the `modifiedAt` argument.
- Every other line of method logic — job discovery via `jobs.Select(...)`, the `GetByJobNameAsync` existence check, the create/update branch, and the preserved `existing.CronExpression` on update — is unchanged.

### `RecurringJobSeederTests` (test project, `Features/BackgroundJobs/RecurringJobSeederTests.cs`)
- Test fixture constructor changes from `new RecurringJobSeeder(_repository)` to `new RecurringJobSeeder(_repository, <TimeProvider>)`.
- Uses `Microsoft.Extensions.Time.Testing.FakeTimeProvider` (already a `PackageReference` in `Anela.Heblo.Tests.csproj`, already used in this same pattern elsewhere in the suite, e.g. `SubmitManufactureHandlerTests`), constructed with a fixed `DateTimeOffset`.
- At least one test asserts `LastModifiedAt` on a seeded/updated `RecurringJobConfiguration` equals the fake clock's fixed value exactly — replacing the current gap where `LastModifiedAt` is never asserted.
- All four existing test methods keep their existing assertions (`DisplayName`, `Description`, preserved `CronExpression`/`IsEnabled`, `LastModifiedBy == "System"`) unmodified in intent.

No other component in the BackgroundJobs module (handlers, repository, dashboard tile, status checker) is touched.

## Data Schemas
No schema, DTO, request, response, or event payload changes. `RecurringJobConfiguration.LastModifiedAt` (existing `DateTime` column, populated via the entity's existing constructor and `UpdateConfiguration` method) is written with a value from a different source (`TimeProvider` instead of `DateTime.UtcNow`) but the same type, semantics, and persistence path.
