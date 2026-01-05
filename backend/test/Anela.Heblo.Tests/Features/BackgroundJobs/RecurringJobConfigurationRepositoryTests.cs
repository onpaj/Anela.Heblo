using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.BackgroundJobs;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobConfigurationRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly RecurringJobConfigurationRepository _repository;

    public RecurringJobConfigurationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"RecurringJobConfigurationTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new RecurringJobConfigurationRepository(_context);
    }

    [Fact]
    public async Task GetAllAsync_WhenNoConfigurations_ReturnsEmptyList()
    {
        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetAllAsync_WithConfigurations_ReturnsAllConfigurations()
    {
        // Arrange
        var config1 = new RecurringJobConfiguration(
            "TestJob1",
            "Test Job 1",
            "Description for test job 1",
            "0 0 * * *",
            true,
            "TestUser");

        var config2 = new RecurringJobConfiguration(
            "TestJob2",
            "Test Job 2",
            "Description for test job 2",
            "0 12 * * *",
            false,
            "TestUser");

        await _context.RecurringJobConfigurations.AddAsync(config1);
        await _context.RecurringJobConfigurations.AddAsync(config2);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        Assert.NotNull(result);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.JobName == "TestJob1");
        Assert.Contains(result, c => c.JobName == "TestJob2");
    }

    [Fact]
    public async Task GetByJobNameAsync_WhenConfigurationExists_ReturnsConfiguration()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            "ExistingJob",
            "Existing Job",
            "Description for existing job",
            "0 6 * * *",
            true,
            "TestUser");

        await _context.RecurringJobConfigurations.AddAsync(config);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByJobNameAsync("ExistingJob");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("ExistingJob", result.JobName);
        Assert.Equal("Existing Job", result.DisplayName);
        Assert.Equal("Description for existing job", result.Description);
        Assert.Equal("0 6 * * *", result.CronExpression);
        Assert.True(result.IsEnabled);
        Assert.Equal("TestUser", result.LastModifiedBy);
    }

    [Fact]
    public async Task GetByJobNameAsync_WhenConfigurationDoesNotExist_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByJobNameAsync("NonExistentJob");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task UpdateAsync_WithValidConfiguration_UpdatesSuccessfully()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            "JobToUpdate",
            "Original Display Name",
            "Original description",
            "0 8 * * *",
            true,
            "OriginalUser");

        await _context.RecurringJobConfigurations.AddAsync(config);
        await _context.SaveChangesAsync();

        // Detach the entity to simulate a fresh load
        _context.Entry(config).State = EntityState.Detached;

        // Load fresh entity
        var loadedConfig = await _repository.GetByJobNameAsync("JobToUpdate");
        Assert.NotNull(loadedConfig);

        // Act
        loadedConfig.UpdateConfiguration(
            "Updated Display Name",
            "Updated description",
            "0 10 * * *",
            "UpdatedUser");

        await _repository.UpdateAsync(loadedConfig);

        // Assert - verify changes persisted
        var updatedConfig = await _repository.GetByJobNameAsync("JobToUpdate");
        Assert.NotNull(updatedConfig);
        Assert.Equal("Updated Display Name", updatedConfig.DisplayName);
        Assert.Equal("Updated description", updatedConfig.Description);
        Assert.Equal("0 10 * * *", updatedConfig.CronExpression);
        Assert.Equal("UpdatedUser", updatedConfig.LastModifiedBy);
        Assert.True(updatedConfig.IsEnabled); // Should remain unchanged
    }

    [Fact]
    public async Task UpdateAsync_WithDisableAction_UpdatesIsEnabled()
    {
        // Arrange
        var config = new RecurringJobConfiguration(
            "JobToDisable",
            "Job to Disable",
            "Description",
            "0 8 * * *",
            true,
            "OriginalUser");

        await _context.RecurringJobConfigurations.AddAsync(config);
        await _context.SaveChangesAsync();

        // Detach the entity
        _context.Entry(config).State = EntityState.Detached;

        // Load fresh entity
        var loadedConfig = await _repository.GetByJobNameAsync("JobToDisable");
        Assert.NotNull(loadedConfig);

        // Act
        loadedConfig.Disable("DisablingUser");
        await _repository.UpdateAsync(loadedConfig);

        // Assert
        var updatedConfig = await _repository.GetByJobNameAsync("JobToDisable");
        Assert.NotNull(updatedConfig);
        Assert.False(updatedConfig.IsEnabled);
        Assert.Equal("DisablingUser", updatedConfig.LastModifiedBy);
    }

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
