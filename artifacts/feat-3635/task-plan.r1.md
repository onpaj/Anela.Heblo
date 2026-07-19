# Task Plan: Extract Recurring Job Seeding out of `IRecurringJobConfigurationRepository`

Source documents: `spec.r1.md`, `arch-review.r1.md` (Skip Design: true — pure backend refactor, no controller/DTO/OpenAPI/React changes), `design.r1.md`.

This is a pure structural extraction with no behavioral change. All work happens in the `backend/` solution. No frontend changes, no EF Core migration, no DI reordering needed (`AddBackgroundJobsModule` already runs before the startup seeding call).

Repo root for all paths below: `/home/user/worktrees/feature-3635-Arch-Review-Backgroundjobs-Seeddefaultconfiguratio`

General verification commands used throughout (run from repo root):
```bash
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs"
dotnet test Anela.Heblo.sln
dotnet format Anela.Heblo.sln
```
If `Anela.Heblo.sln` does not resolve in the environment, drop the explicit solution argument (`dotnet build`, `dotnet test`, `dotnet format`) — both forms build/test the same single solution in this repo.

---

### task: add-repository-add-async-and-recurring-job-seeder

**Goal:** Add a narrow `AddAsync` primitive to `IRecurringJobConfigurationRepository`/`RecurringJobConfigurationRepository`, then introduce the new `IRecurringJobSeeder`/`RecurringJobSeeder` pair in the Application layer that uses it. This step is purely additive — nothing existing is removed yet, so the old `SeedDefaultConfigurationsAsync` on the repository keeps working and all existing tests keep passing throughout.

#### Step 1: Add a failing test for `IRecurringJobConfigurationRepository.AddAsync`

Read the existing test file first: `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs`.

Insert a new test immediately after `UpdateAsync_WithDisableAction_UpdatesIsEnabled` (which ends just before the `SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations` test) using an Edit with this exact anchor:

Old text to match:
```csharp
        // Assert
        var updatedConfig = await _repository.GetByJobNameAsync("JobToDisable");
        Assert.NotNull(updatedConfig);
        Assert.False(updatedConfig.IsEnabled);
        Assert.Equal("DisablingUser", updatedConfig.LastModifiedBy);
    }

    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations()
```

New text (adds the new test before the existing seed test, keeping the seed test untouched for now):
```csharp
        // Assert
        var updatedConfig = await _repository.GetByJobNameAsync("JobToDisable");
        Assert.NotNull(updatedConfig);
        Assert.False(updatedConfig.IsEnabled);
        Assert.Equal("DisablingUser", updatedConfig.LastModifiedBy);
    }

    [Fact]
    public async Task AddAsync_WithNewConfiguration_PersistsAndIsRetrievable()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            "NewJob",
            "New Job",
            "Description for new job",
            "0 5 * * *",
            true,
            "System");

        // Act
        await _repository.AddAsync(config);

        // Assert
        var result = await _repository.GetByJobNameAsync("NewJob");
        Assert.NotNull(result);
        Assert.Equal("New Job", result.DisplayName);
        Assert.Equal("Description for new job", result.Description);
        Assert.Equal("0 5 * * *", result.CronExpression);
        Assert.True(result.IsEnabled);
    }

    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations()
```

Run the test to verify it fails (compile error, since `AddAsync` does not exist yet on `RecurringJobConfigurationRepository`):
```bash
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~RecurringJobConfigurationRepositoryTests.AddAsync_WithNewConfiguration_PersistsAndIsRetrievable"
```
Expect a build error: `'RecurringJobConfigurationRepository' does not contain a definition for 'AddAsync'`.

#### Step 2: Implement `AddAsync` on the interface and repository

Edit `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs`.

Old text:
```csharp
namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public interface IRecurringJobConfigurationRepository
{
    Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
    Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default);
}
```

New text (adds `AddAsync`, keeps `SeedDefaultConfigurationsAsync` for now — it is removed in the `remove-legacy-seed-method-and-clean-test-doubles` task once nothing depends on it):
```csharp
namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public interface IRecurringJobConfigurationRepository
{
    Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default);
    Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
    Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default);
}
```

Edit `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`.

Old text:
```csharp
    public async Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _context.RecurringJobConfigurations.Update(configuration);
        await _context.SaveChangesAsync(cancellationToken);
    }
```

New text (adds `AddAsync` right before `UpdateAsync`, following the same self-committing convention: stage + `SaveChangesAsync` in the same call, per arch-review Decision 2):
```csharp
    public async Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
    {
        await _context.RecurringJobConfigurations.AddAsync(configuration, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _context.RecurringJobConfigurations.Update(configuration);
        await _context.SaveChangesAsync(cancellationToken);
    }
```

Run the test again to verify it now passes:
```bash
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~RecurringJobConfigurationRepositoryTests.AddAsync_WithNewConfiguration_PersistsAndIsRetrievable"
```
Expect: 1 passed.

#### Step 3: Add a failing test for the not-yet-existing `RecurringJobSeeder`

Create `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs`:

```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobSeederTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RecurringJobConfigurationRepository _repository;
    private readonly RecurringJobSeeder _seeder;

    public RecurringJobSeederTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"RecurringJobSeederTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new RecurringJobConfigurationRepository(_context);
        _seeder = new RecurringJobSeeder(_repository);
    }

    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations()
    {
        // Arrange - Create mock jobs
        var mockJobs = CreateMockJobs();

        // Act
        await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

        // Assert
        var configurations = await _repository.GetAllAsync();
        Assert.Equal(9, configurations.Count);

        // Verify specific jobs exist
        Assert.Contains(configurations, c => c.JobName == "purchase-price-recalculation");
        Assert.Contains(configurations, c => c.JobName == "product-export-download");
        Assert.Contains(configurations, c => c.JobName == "product-weight-recalculation");
        Assert.Contains(configurations, c => c.JobName == "invoice-classification");
        Assert.Contains(configurations, c => c.JobName == "daily-consumption-calculation");
        Assert.Contains(configurations, c => c.JobName == "daily-invoice-import-eur");
        Assert.Contains(configurations, c => c.JobName == "daily-invoice-import-czk");
        Assert.Contains(configurations, c => c.JobName == "daily-comgate-czk-import");
        Assert.Contains(configurations, c => c.JobName == "daily-comgate-eur-import");
    }

    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate()
    {
        // Arrange - add one default configuration manually
        var existingConfig = new RecurringJobConfiguration(
            "purchase-price-recalculation",
            "Purchase Price Recalculation",
            "Recalculates purchase prices for all materials and products",
            "0 2 * * *",
            true,
            "System");

        await _context.RecurringJobConfigurations.AddAsync(existingConfig);
        await _context.SaveChangesAsync();

        // Arrange - Create mock jobs
        var mockJobs = CreateMockJobs();

        // Act
        await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

        // Assert
        var configurations = await _repository.GetAllAsync();
        Assert.Equal(9, configurations.Count); // Should still have exactly 9 (not 10)

        // Verify the existing configuration was not duplicated
        var purchasePriceConfigs = configurations.Where(c => c.JobName == "purchase-price-recalculation").ToList();
        Assert.Single(purchasePriceConfigs);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>
    /// Creates mock IRecurringJob implementations for testing
    /// </summary>
    private static List<IRecurringJob> CreateMockJobs()
    {
        return new List<IRecurringJob>
        {
            new MockRecurringJob("purchase-price-recalculation", "Purchase Price Recalculation", "Recalculates purchase prices for all materials and products", "0 2 * * *"),
            new MockRecurringJob("product-export-download", "Product Export Download", "Downloads product export data from external systems", "0 2 * * *"),
            new MockRecurringJob("product-weight-recalculation", "Product Weight Recalculation", "Recalculates product weights based on current material composition", "0 2 * * *"),
            new MockRecurringJob("invoice-classification", "Invoice Classification", "Classifies and categorizes incoming invoices", "0 * * * *"),
            new MockRecurringJob("daily-consumption-calculation", "Daily Consumption Calculation", "Calculates daily consumption of packing materials", "0 3 * * *"),
            new MockRecurringJob("daily-invoice-import-eur", "Daily Invoice Import (EUR)", "Imports EUR invoices from Shoptet to ABRA Flexi", "0 4 * * *"),
            new MockRecurringJob("daily-invoice-import-czk", "Daily Invoice Import (CZK)", "Imports CZK invoices from Shoptet to ABRA Flexi", "15 4 * * *"),
            new MockRecurringJob("daily-comgate-czk-import", "Daily Comgate CZK Import", "Imports Comgate CZK payment statements from previous day", "30 4 * * *"),
            new MockRecurringJob("daily-comgate-eur-import", "Daily Comgate EUR Import", "Imports Comgate EUR payment statements from previous day", "40 4 * * *")
        };
    }

    /// <summary>
    /// Mock implementation of IRecurringJob for testing
    /// </summary>
    private class MockRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata { get; }

        public MockRecurringJob(string jobName, string displayName, string description, string cronExpression)
        {
            Metadata = new RecurringJobMetadata
            {
                JobName = jobName,
                DisplayName = displayName,
                Description = description,
                CronExpression = cronExpression,
                DefaultIsEnabled = true
            };
        }

        public Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
```

Run the new tests to verify they fail (compile error, since `IRecurringJobSeeder`/`RecurringJobSeeder` don't exist yet):
```bash
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~RecurringJobSeederTests"
```
Expect a build error: `The type or namespace name 'RecurringJobSeeder' could not be found`.

#### Step 4: Implement `IRecurringJobSeeder` and `RecurringJobSeeder`

Create `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IRecurringJobSeeder.cs`:
```csharp
namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public interface IRecurringJobSeeder
{
    Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default);
}
```

Create `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs`:
```csharp
using Anela.Heblo.Domain.Features.BackgroundJobs;

namespace Anela.Heblo.Application.Features.BackgroundJobs.Services;

public class RecurringJobSeeder : IRecurringJobSeeder
{
    private readonly IRecurringJobConfigurationRepository _repository;

    public RecurringJobSeeder(IRecurringJobConfigurationRepository repository)
    {
        _repository = repository;
    }

    /// <summary>
    /// Seeds database with configurations from discovered IRecurringJob implementations.
    /// Only creates configurations for jobs that don't already exist in the database.
    /// </summary>
    /// <param name="jobs">Collection of discovered recurring jobs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
    {
        // Create configurations from discovered job metadata
        var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
            job.Metadata.JobName,
            job.Metadata.DisplayName,
            job.Metadata.Description,
            job.Metadata.CronExpression,
            job.Metadata.DefaultIsEnabled,
            "System"
        )).ToArray();

        foreach (var config in defaultConfigurations)
        {
            var existing = await _repository.GetByJobNameAsync(config.JobName, cancellationToken);
            if (existing == null)
            {
                await _repository.AddAsync(config, cancellationToken);
            }
        }
    }
}
```

Note: both `IRecurringJobConfigurationRepository` (`Anela.Heblo.Domain`) and `IRecurringJob`/`RecurringJobMetadata`/`RecurringJobConfiguration` live in `Anela.Heblo.Domain.Features.BackgroundJobs`, already imported via the single `using` above — no additional imports needed. `ImplicitUsings` is enabled on `Anela.Heblo.Application.csproj`, so `System`, `System.Collections.Generic`, `System.Linq`, and `System.Threading.Tasks` do not need explicit `using` directives.

Run the tests again to verify they pass:
```bash
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~RecurringJobSeederTests"
```
Expect: 2 passed.

#### Step 5: Full verification and commit

```bash
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.BackgroundJobs"
```
Expect: build succeeds, all BackgroundJobs tests pass (existing repository/scheduler/discovery tests are untouched and still green; the new `AddAsync` test and both `RecurringJobSeederTests` pass).

Commit:
```bash
git add backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs \
        backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs \
        backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/IRecurringJobSeeder.cs \
        backend/src/Anela.Heblo.Application/Features/BackgroundJobs/Services/RecurringJobSeeder.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobSeederTests.cs
git commit -m "#3635: Add RecurringJobConfigurationRepository.AddAsync and introduce RecurringJobSeeder"
```

---

### task: wire-recurring-job-seeder-into-di-and-startup

**Goal:** Register `IRecurringJobSeeder` in the `BackgroundJobsModule` DI composition root and switch the startup seeding call site to depend on it instead of `IRecurringJobConfigurationRepository`. `IRecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync` still exists on the interface at the end of this task (it becomes dead code, removed in the next task) — this keeps every commit in this plan buildable and behavior-preserving.

#### Step 1: Register `IRecurringJobSeeder` in `BackgroundJobsModule`

Edit `backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs`.

Old text:
```csharp
        // MediatR handlers are automatically registered by MediatR scan
        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IRecurringJobConfigurationRepository, RecurringJobConfigurationRepository>();
        // Hangfire adapter implementations (IHangfireJobEnqueuer, IHangfireRecurringJobScheduler)
        // are registered in Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices
        // because their implementations live in the API project (Clean Architecture dependency rule).
```

New text:
```csharp
        // MediatR handlers are automatically registered by MediatR scan
        // Repository (implementation lives in the Persistence layer)
        services.AddScoped<IRecurringJobConfigurationRepository, RecurringJobConfigurationRepository>();
        // Startup-only seeding service (Application layer, wraps the repository)
        services.AddScoped<IRecurringJobSeeder, RecurringJobSeeder>();
        // Hangfire adapter implementations (IHangfireJobEnqueuer, IHangfireRecurringJobScheduler)
        // are registered in Anela.Heblo.API.Extensions.ServiceCollectionExtensions.AddHangfireServices
        // because their implementations live in the API project (Clean Architecture dependency rule).
```

`RecurringJobSeeder` lives in the same `Anela.Heblo.Application.Features.BackgroundJobs.Services` namespace as `BackgroundJobsModule`'s sibling registrations, and `IRecurringJobConfigurationRepository`/`RecurringJobConfigurationRepository` are already `using`-imported in this file (`Anela.Heblo.Domain.Features.BackgroundJobs` and `Anela.Heblo.Persistence.BackgroundJobs`) — no new `using` is needed since `IRecurringJobSeeder`/`RecurringJobSeeder` are in the file's own namespace (`Anela.Heblo.Application.Features.BackgroundJobs`, of which `.Services` is a child namespace reachable without an extra `using` only if the compiler resolves nested namespaces automatically — it does not. Add the `using` explicitly to be safe.)

Also add, near the top of the file, alongside the existing `using` directives:

Old text:
```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.BackgroundJobs;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;
```

New text:
```csharp
using Anela.Heblo.Application.Features.BackgroundJobs.DashboardTiles;
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence.BackgroundJobs;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.DependencyInjection;
```

#### Step 2: Update the startup call site

Edit `backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs`.

Old text:
```csharp
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                var repository = scope.ServiceProvider.GetRequiredService<IRecurringJobConfigurationRepository>();

                // Get all discovered IRecurringJob implementations
                var discoveredJobs = scope.ServiceProvider.GetServices<IRecurringJob>();

                await repository.SeedDefaultConfigurationsAsync(discoveredJobs);
```

New text:
```csharp
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
                var seeder = scope.ServiceProvider.GetRequiredService<IRecurringJobSeeder>();

                // Get all discovered IRecurringJob implementations
                var discoveredJobs = scope.ServiceProvider.GetServices<IRecurringJob>();

                await seeder.SeedDefaultConfigurationsAsync(discoveredJobs);
```

No new `using` is needed in this file: `using Anela.Heblo.Application.Features.BackgroundJobs.Services;` is already present (line 24, verified in the current file), which is where `IRecurringJobSeeder` lives.

Logging behavior (success log with discovered-job count; error log + rethrow on failure) is untouched — only the resolved type and local variable name (`repository` → `seeder`) change, per FR-4.

#### Step 3: Build and full test verification

```bash
dotnet build Anela.Heblo.sln
dotnet test Anela.Heblo.sln
```
Expect: build succeeds, full backend test suite passes (no test exercises `SeedRecurringJobConfigurationsAsync` directly today — it is startup-only glue code covered by FR-4's acceptance criteria via the moved unit tests in the previous task and manual/staging verification, consistent with the spec).

#### Step 4: Commit

```bash
git add backend/src/Anela.Heblo.Application/Features/BackgroundJobs/BackgroundJobsModule.cs \
        backend/src/Anela.Heblo.API/Extensions/ServiceCollectionExtensions.cs
git commit -m "#3635: Wire IRecurringJobSeeder into DI and switch startup seeding call site"
```

---

### task: remove-legacy-seed-method-and-clean-test-doubles

**Goal:** Remove `SeedDefaultConfigurationsAsync` from `IRecurringJobConfigurationRepository` and its EF Core implementation now that nothing calls it (the startup call site was switched to `IRecurringJobSeeder` in the previous task), and drop the now-unnecessary stub implementations and duplicated tests. This is the step where the interface narrows down to pure CRUD, matching spec FR-2/FR-5.

#### Step 1: Remove the method from the interface and repository (expect build breaks — this is the "verify fail" signal that every affected file has been located)

Edit `backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs`.

Old text:
```csharp
namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public interface IRecurringJobConfigurationRepository
{
    Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default);
    Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
    Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default);
}
```

New text:
```csharp
namespace Anela.Heblo.Domain.Features.BackgroundJobs;

public interface IRecurringJobConfigurationRepository
{
    Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default);
    Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
    Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default);
}
```

Edit `backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs`.

Old text:
```csharp
    public async Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _context.RecurringJobConfigurations.Update(configuration);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds database with configurations from discovered IRecurringJob implementations.
    /// Only creates configurations for jobs that don't already exist in the database.
    /// </summary>
    /// <param name="jobs">Collection of discovered recurring jobs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    public async Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
    {
        // Create configurations from discovered job metadata
        var defaultConfigurations = jobs.Select(job => new RecurringJobConfiguration(
            job.Metadata.JobName,
            job.Metadata.DisplayName,
            job.Metadata.Description,
            job.Metadata.CronExpression,
            job.Metadata.DefaultIsEnabled,
            "System"
        )).ToArray();

        foreach (var config in defaultConfigurations)
        {
            var existing = await GetByJobNameAsync(config.JobName, cancellationToken);
            if (existing == null)
            {
                await _context.RecurringJobConfigurations.AddAsync(config, cancellationToken);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

New text:
```csharp
    public async Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
    {
        _context.RecurringJobConfigurations.Update(configuration);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
```

Run a build to see the now-expected compile errors in the three affected test files (confirms every call site is accounted for before fixing them):
```bash
dotnet build Anela.Heblo.sln
```
Expect errors similar to:
- `RecurringJobConfigurationRepositoryTests.cs`: `'IRecurringJobConfigurationRepository' does not contain a definition for 'SeedDefaultConfigurationsAsync'` (2 call sites, in the two seed tests).
- `HangfireRecurringJobSchedulerTests.cs`: `'EmptyStubRepository' does not implement interface member 'IRecurringJobConfigurationRepository.SeedDefaultConfigurationsAsync(...)'` — actually the opposite: since the method no longer exists on the interface, the stub's override is now a plain extra method, which is legal C# (no compile error) but stale. The real compile error here is none — however this method is now dead code implementing nothing, so it must still be removed for FR-5 compliance (checked by review, not the compiler). Continue to Step 2 regardless of whether the build reports it.

#### Step 2: Remove the two seeding tests (and their now-unused helpers) from `RecurringJobConfigurationRepositoryTests.cs`

Edit `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs`.

Old text (removes both seed tests plus the `CreateMockJobs`/`MockRecurringJob` helpers that existed only to support them — this coverage now lives in `RecurringJobSeederTests.cs`):
```csharp
    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenEmpty_CreatesAllDefaultConfigurations()
    {
        // Arrange - Create mock jobs
        var mockJobs = CreateMockJobs();

        // Act
        await _repository.SeedDefaultConfigurationsAsync(mockJobs);

        // Assert
        var configurations = await _repository.GetAllAsync();
        Assert.Equal(9, configurations.Count);

        // Verify specific jobs exist
        Assert.Contains(configurations, c => c.JobName == "purchase-price-recalculation");
        Assert.Contains(configurations, c => c.JobName == "product-export-download");
        Assert.Contains(configurations, c => c.JobName == "product-weight-recalculation");
        Assert.Contains(configurations, c => c.JobName == "invoice-classification");
        Assert.Contains(configurations, c => c.JobName == "daily-consumption-calculation");
        Assert.Contains(configurations, c => c.JobName == "daily-invoice-import-eur");
        Assert.Contains(configurations, c => c.JobName == "daily-invoice-import-czk");
        Assert.Contains(configurations, c => c.JobName == "daily-comgate-czk-import");
        Assert.Contains(configurations, c => c.JobName == "daily-comgate-eur-import");
    }

    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenConfigurationsExist_DoesNotDuplicate()
    {
        // Arrange - add one default configuration manually
        var existingConfig = new RecurringJobConfiguration(
            "purchase-price-recalculation",
            "Purchase Price Recalculation",
            "Recalculates purchase prices for all materials and products",
            "0 2 * * *",
            true,
            "System");

        await _context.RecurringJobConfigurations.AddAsync(existingConfig);
        await _context.SaveChangesAsync();

        // Arrange - Create mock jobs
        var mockJobs = CreateMockJobs();

        // Act
        await _repository.SeedDefaultConfigurationsAsync(mockJobs);

        // Assert
        var configurations = await _repository.GetAllAsync();
        Assert.Equal(9, configurations.Count); // Should still have exactly 9 (not 10)

        // Verify the existing configuration was not duplicated
        var purchasePriceConfigs = configurations.Where(c => c.JobName == "purchase-price-recalculation").ToList();
        Assert.Single(purchasePriceConfigs);
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    /// <summary>
    /// Creates mock IRecurringJob implementations for testing
    /// </summary>
    private static List<IRecurringJob> CreateMockJobs()
    {
        return new List<IRecurringJob>
        {
            new MockRecurringJob("purchase-price-recalculation", "Purchase Price Recalculation", "Recalculates purchase prices for all materials and products", "0 2 * * *"),
            new MockRecurringJob("product-export-download", "Product Export Download", "Downloads product export data from external systems", "0 2 * * *"),
            new MockRecurringJob("product-weight-recalculation", "Product Weight Recalculation", "Recalculates product weights based on current material composition", "0 2 * * *"),
            new MockRecurringJob("invoice-classification", "Invoice Classification", "Classifies and categorizes incoming invoices", "0 * * * *"),
            new MockRecurringJob("daily-consumption-calculation", "Daily Consumption Calculation", "Calculates daily consumption of packing materials", "0 3 * * *"),
            new MockRecurringJob("daily-invoice-import-eur", "Daily Invoice Import (EUR)", "Imports EUR invoices from Shoptet to ABRA Flexi", "0 4 * * *"),
            new MockRecurringJob("daily-invoice-import-czk", "Daily Invoice Import (CZK)", "Imports CZK invoices from Shoptet to ABRA Flexi", "15 4 * * *"),
            new MockRecurringJob("daily-comgate-czk-import", "Daily Comgate CZK Import", "Imports Comgate CZK payment statements from previous day", "30 4 * * *"),
            new MockRecurringJob("daily-comgate-eur-import", "Daily Comgate EUR Import", "Imports Comgate EUR payment statements from previous day", "40 4 * * *")
        };
    }

    /// <summary>
    /// Mock implementation of IRecurringJob for testing
    /// </summary>
    private class MockRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata { get; }

        public MockRecurringJob(string jobName, string displayName, string description, string cronExpression)
        {
            Metadata = new RecurringJobMetadata
            {
                JobName = jobName,
                DisplayName = displayName,
                Description = description,
                CronExpression = cronExpression,
                DefaultIsEnabled = true
            };
        }

        public Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
```

New text:
```csharp
    public void Dispose()
    {
        _context.Dispose();
    }
}
```

#### Step 3: Remove the stale `SeedDefaultConfigurationsAsync` stub from `HangfireRecurringJobSchedulerTests.cs`

Edit `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs`.

Old text:
```csharp
    private class EmptyStubRepository : IRecurringJobConfigurationRepository
    {
        public Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<RecurringJobConfiguration>());

        public Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
            => Task.FromResult<RecurringJobConfiguration?>(null);

        public Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
```

New text:
```csharp
    private class EmptyStubRepository : IRecurringJobConfigurationRepository
    {
        public Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<RecurringJobConfiguration>());

        public Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
            => Task.FromResult<RecurringJobConfiguration?>(null);

        public Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
```

(`EmptyStubRepository` is a manual `IRecurringJobConfigurationRepository` implementer, so it must gain a trivial `AddAsync` no-op to satisfy the interface's new member, in addition to dropping the obsolete `SeedDefaultConfigurationsAsync` override.)

#### Step 4: Remove the stale `SeedDefaultConfigurationsAsync` stubs from `RecurringJobDiscoveryServiceTests.cs` (two stub classes)

Edit `backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobDiscoveryServiceTests.cs`.

Old text:
```csharp
    private class StubRecurringJobConfigurationRepository : IRecurringJobConfigurationRepository
    {
        public Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<RecurringJobConfiguration>());

        public Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
            => Task.FromResult<RecurringJobConfiguration?>(null);

        public Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Stub repository that returns a DB config with the specified CRON for the test job.
    /// Used to verify the service prefers the DB CRON over the metadata default.
    /// </summary>
    private class StubDbRecurringJobConfigurationRepository : IRecurringJobConfigurationRepository
    {
        private readonly string _cronExpression;

        public StubDbRecurringJobConfigurationRepository(string cronExpression)
        {
            _cronExpression = cronExpression;
        }

        public Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var config = new RecurringJobConfiguration(
                jobName: "test-async-job",
                displayName: "Test Async Recurring Job",
                description: "Test job for DB CRON path verification",
                cronExpression: _cronExpression,
                isEnabled: true,
                lastModifiedBy: "test");

            return Task.FromResult(new List<RecurringJobConfiguration> { config });
        }

        public Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
            => Task.FromResult<RecurringJobConfiguration?>(null);

        public Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
```

New text:
```csharp
    private class StubRecurringJobConfigurationRepository : IRecurringJobConfigurationRepository
    {
        public Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new List<RecurringJobConfiguration>());

        public Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
            => Task.FromResult<RecurringJobConfiguration?>(null);

        public Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    /// <summary>
    /// Stub repository that returns a DB config with the specified CRON for the test job.
    /// Used to verify the service prefers the DB CRON over the metadata default.
    /// </summary>
    private class StubDbRecurringJobConfigurationRepository : IRecurringJobConfigurationRepository
    {
        private readonly string _cronExpression;

        public StubDbRecurringJobConfigurationRepository(string cronExpression)
        {
            _cronExpression = cronExpression;
        }

        public Task<List<RecurringJobConfiguration>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            var config = new RecurringJobConfiguration(
                jobName: "test-async-job",
                displayName: "Test Async Recurring Job",
                description: "Test job for DB CRON path verification",
                cronExpression: _cronExpression,
                isEnabled: true,
                lastModifiedBy: "test");

            return Task.FromResult(new List<RecurringJobConfiguration> { config });
        }

        public Task<RecurringJobConfiguration?> GetByJobNameAsync(string jobName, CancellationToken cancellationToken = default)
            => Task.FromResult<RecurringJobConfiguration?>(null);

        public Task AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task UpdateAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}
```

#### Step 5: Full verification

```bash
dotnet build Anela.Heblo.sln
```
Expect: build succeeds with zero errors (this is the "verify pass" signal — every implementer of `IRecurringJobConfigurationRepository` now compiles against the narrowed, CRUD-only interface).

```bash
dotnet test Anela.Heblo.sln
```
Expect: full backend test suite passes, including:
- `RecurringJobConfigurationRepositoryTests` (CRUD tests + the new `AddAsync` test; no seed tests remain here).
- `RecurringJobSeederTests` (the two moved seed-behavior tests, now passing against `RecurringJobSeeder`, still asserting 9 configurations and no duplicates).
- `HangfireRecurringJobSchedulerTests` and `RecurringJobDiscoveryServiceTests` (unaffected behaviorally; their stubs just gained a no-op `AddAsync` and lost the dead `SeedDefaultConfigurationsAsync` override).

Run formatting check (CLAUDE.md validation gate):
```bash
dotnet format Anela.Heblo.sln --verify-no-changes
```
If this reports formatting differences, run `dotnet format Anela.Heblo.sln` to apply them, then re-run `dotnet build` and `dotnet test` to confirm nothing regressed, and include the formatting fix in this task's commit.

#### Step 6: Commit

```bash
git add backend/src/Anela.Heblo.Domain/Features/BackgroundJobs/IRecurringJobConfigurationRepository.cs \
        backend/src/Anela.Heblo.Persistence/BackgroundJobs/RecurringJobConfigurationRepository.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobConfigurationRepositoryTests.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/HangfireRecurringJobSchedulerTests.cs \
        backend/test/Anela.Heblo.Tests/Features/BackgroundJobs/RecurringJobDiscoveryServiceTests.cs
git commit -m "#3635: Remove SeedDefaultConfigurationsAsync from IRecurringJobConfigurationRepository and clean up test doubles"
```

---

## Self-review

**Spec coverage:**
- FR-1 (`IRecurringJobSeeder` interface, Application layer, domain-only dependency) — covered in `add-repository-add-async-and-recurring-job-seeder`, Step 4.
- FR-2 (`RecurringJobSeeder` implementation, moved logic, `AddAsync` added to repository, identical DB-state behavior verified by the moved tests asserting 9 configurations / no duplicates) — covered across Steps 1–4 of the first task.
- FR-3 (DI registration in `BackgroundJobsModule`, `Scoped` lifetime matching the repository) — covered in `wire-recurring-job-seeder-into-di-and-startup`, Step 1.
- FR-4 (startup call site resolves `IRecurringJobSeeder`, logging/error handling unchanged) — covered in `wire-recurring-job-seeder-into-di-and-startup`, Step 2.
- FR-5 (test doubles no longer implement seeding; seed tests moved to `RecurringJobSeederTests`; `AddAsync` gets direct coverage; `dotnet build`/tests pass) — covered in `add-repository-add-async-and-recurring-job-seeder` (Step 1's `AddAsync` test, Step 3's `RecurringJobSeederTests.cs`) and `remove-legacy-seed-method-and-clean-test-doubles` (Steps 2–5).
- NFR-1 (no material performance regression; startup-only, small job count) — addressed by design choice (Decision 2, per-call commit) inherited from `arch-review.r1.md`; no additional plan step needed since this is explicitly spec-permitted.
- NFR-2/NFR-3 (no security/API/schema surface change) — verified structurally: no controller, DTO, or migration file appears in any task's file list above.

**Placeholder scan:** no "TBD", "similar to Task N", or elided code blocks remain in any task above — every edit shows complete old/new text or full file content.

**Type/signature consistency:** `AddAsync(RecurringJobConfiguration configuration, CancellationToken cancellationToken = default)` and `SeedDefaultConfigurationsAsync(IEnumerable<IRecurringJob> jobs, CancellationToken cancellationToken = default)` are written identically everywhere they appear (interface, implementation, stubs, seeder) across all three tasks.

**Ordering safety:** each task leaves the solution in a buildable, fully-green state at its commit point — task 1 is purely additive, task 2 only swaps which interface the startup call site resolves (both interfaces still exist), task 3 performs the interface narrowing plus the required consumer/test fixes together so no intermediate commit has a dangling reference to the removed member.
