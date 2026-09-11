### task: add-timeprovider-to-recurring-job-seeder

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs:9-12` (constructor), `:26-35` (create path), `:46-52` (update path)
- Test: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs:11-24` (fixture), and new assertions added to two existing test methods

- [ ] **Step 1: Update the test fixture to use a fixed `FakeTimeProvider` and add exact-timestamp assertions**

Edit `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`. Add the `Microsoft.Extensions.Time.Testing` using directive, add a `_fixedTime`/`_timeProvider` fixture field, and pass the provider into the seeder's constructor:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobSeederTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RecurringJobConfigurationRepository _repository;
    private readonly FakeTimeProvider _timeProvider;
    private readonly RecurringJobSeeder _seeder;
    private static readonly DateTimeOffset FixedTime = new(2025, 6, 1, 3, 0, 0, TimeSpan.Zero);

    public RecurringJobSeederTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"RecurringJobSeederTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new RecurringJobConfigurationRepository(_context);
        _timeProvider = new FakeTimeProvider(FixedTime);
        _seeder = new RecurringJobSeeder(_repository, _timeProvider);
    }
```

Only the `using Microsoft.Extensions.Time.Testing;` line and the `_timeProvider`/`FixedTime` fixture members are new — every other line above (including the pre-existing `using Microsoft.EntityFrameworkCore;`) matches the file as it stands today.

Then add an assertion to the end of `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations` (after the existing `nonDefaultTimeZoneJob` assertions), asserting every created row's timestamp matches the fixed clock exactly:

```csharp
        // Verify LastModifiedAt is sourced from the injected TimeProvider, not wall-clock time
        Assert.All(configurations, c => Assert.Equal(FixedTime.UtcDateTime, c.LastModifiedAt));
```

And add an assertion to the end of `SeedDefaultConfigurationsAsync_WhenConfigurationExists_UpdatesDisplayNameAndDescription` (after the existing `Description` assertion), asserting the updated row's timestamp matches the fixed clock exactly:

```csharp
        Assert.Equal(FixedTime.UtcDateTime, updated.LastModifiedAt);
```

- [ ] **Step 2: Run the tests to confirm they fail (compile error against the old 1-arg constructor)**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests"`
Expected: FAIL — build error, `CS1729: 'RecurringJobSeeder' does not contain a constructor that takes 1 arguments` (the test fixture in Step 1 now calls the two-argument constructor that doesn't exist yet).

- [ ] **Step 3: Add the `TimeProvider` dependency and replace both `DateTime.UtcNow` call sites in `RecurringJobSeeder`**

Edit `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`:

```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public class RecurringJobSeeder : IRecurringJobSeeder
{
    private readonly IRecurringJobConfigurationRepository _repository;
    private readonly TimeProvider _timeProvider;

    public RecurringJobSeeder(IRecurringJobConfigurationRepository repository, TimeProvider timeProvider)
    {
        _repository = repository;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Seeds database with configurations from discovered IRecurringJob implementations.
    /// Creates configurations for jobs that don't already exist in the database. For jobs
    /// that already have a configuration row, updates the developer-owned fields
    /// (DisplayName, Description) to match the current code, while preserving the
    /// admin-owned fields (CronExpression, IsEnabled) exactly as stored.
    /// </summary>
    /// <param name="jobs">Collection of discovered recurring jobs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;

        // Create configurations from discovered job metadata
        var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
            job.Metadata.JobName,
            job.Metadata.DisplayName,
            job.Metadata.Description,
            job.Metadata.CronExpression,
            job.Metadata.TimeZoneId,
            job.Metadata.DefaultIsEnabled,
            "System",
            now
        )).ToArray();

        foreach (var config in defaultConfigurations)
        {
            var existing = await _repository.GetByJobNameAsync(config.JobName, cancellationToken);
            if (existing == null)
            {
                await _repository.AddAsync(config, cancellationToken);
            }
            else
            {
                existing.UpdateConfiguration(
                    config.DisplayName,
                    config.Description,
                    existing.CronExpression,   // preserve admin override
                    config.TimeZoneId,
                    "System",
                    now);
                await _repository.UpdateAsync(existing, cancellationToken);
            }
        }
    }
}
```

No constructor null-guards are added (matches this class's existing unguarded style — do not adopt the `?? throw new ArgumentNullException(...)` pattern used by sibling MediatR handlers; that would be an out-of-scope style change per `arch-review.r1.md`).

No DI registration change is needed: `BackgroundJobsModule.cs`'s `services.AddScoped<IRecurringJobSeeder, RecurringJobSeeder>();` resolves the new `TimeProvider` parameter automatically from the existing `services.AddSingleton(TimeProvider.System);` registration in `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs:135`.

- [ ] **Step 4: Run the tests to confirm they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~RecurringJobSeederTests"`
Expected: PASS — all 5 tests in `RecurringJobSeederTests` green, including the two new `LastModifiedAt` assertions from Step 1.

- [ ] **Step 5: Run the full backend build and format check**

Run: `dotnet build backend/Anela.Heblo.sln`
Expected: Build succeeded, 0 errors.

Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes`
Expected: no formatting diffs. If it reports changes, run `dotnet format backend/Anela.Heblo.sln` and re-verify.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs
git commit -m "fix(background-jobs): use TimeProvider instead of DateTime.UtcNow in RecurringJobSeeder"
```

---

## Self-Review

**Spec coverage:**
- FR-1 (inject `TimeProvider` into constructor) — covered by Step 3.
- FR-2 (replace both `DateTime.UtcNow` call sites with a single `now` computed once per call) — covered by Step 3 (`var now = _timeProvider.GetUtcNow().UtcDateTime;` computed once, before the `Select`, reused in both the create projection and the update branch).
- FR-3 (update tests to use a controllable `TimeProvider` and assert the exact timestamp) — covered by Step 1 (`FakeTimeProvider` fixture + exact-timestamp assertions on both the create and update paths).
- NFR-1 (performance) — no action needed, confirmed as a like-for-like substitution; no task required.
- NFR-2 (security) — not applicable, confirmed; no task required.
- NFR-3 (testability) — covered by Step 1's exact assertions, replacing the prior lack of any `LastModifiedAt` coverage.
- Data Model / API sections of `spec.r1.md` — confirmed no changes needed; no task required.
- Out of Scope items (repository/interface/DI-registration changes) — confirmed untouched by this plan.

**Placeholder scan:** No "TBD"/"TODO" markers; all code blocks are complete, runnable diffs; commands include expected output.

**Type consistency:** `RecurringJobSeeder(IRecurringJobConfigurationRepository, TimeProvider)` constructor signature is identical between Step 1 (test fixture call) and Step 3 (production definition). `FixedTime` (`DateTimeOffset`) and its `.UtcDateTime` projection used in test assertions match the `DateTime` type of `RecurringJobConfiguration.LastModifiedAt`.
