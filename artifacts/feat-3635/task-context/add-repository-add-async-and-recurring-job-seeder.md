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
