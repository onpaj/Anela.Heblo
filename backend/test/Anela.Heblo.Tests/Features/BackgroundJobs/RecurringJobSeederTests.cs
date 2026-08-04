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

        // Verify TimeZoneId is seeded from job metadata, not just defaulted
        var defaultTimeZoneJob = configurations.Single(c => c.JobName == "purchase-price-recalculation");
        Assert.Equal(RecurringJobMetadata.DefaultTimeZoneId, defaultTimeZoneJob.TimeZoneId);

        var nonDefaultTimeZoneJob = configurations.Single(c => c.JobName == "invoice-classification");
        Assert.Equal("America/New_York", nonDefaultTimeZoneJob.TimeZoneId);
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
            "Europe/Prague",
            true,
            "System",
            DateTime.UtcNow);

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

    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenConfigurationExists_UpdatesDisplayNameAndDescription()
    {
        // Arrange - add an existing configuration with stale DisplayName/Description
        var existingConfig = new RecurringJobConfiguration(
            "purchase-price-recalculation",
            "Old Display Name",
            "Old description that no longer matches the code",
            "0 2 * * *",
            "Europe/Prague",
            true,
            "System",
            DateTime.UtcNow);

        await _context.RecurringJobConfigurations.AddAsync(existingConfig);
        await _context.SaveChangesAsync();

        var mockJobs = CreateMockJobs();

        // Act
        await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

        // Assert
        var updated = await _repository.GetByJobNameAsync("purchase-price-recalculation");
        Assert.NotNull(updated);
        Assert.Equal("Purchase Price Recalculation", updated!.DisplayName);
        Assert.Equal("Recalculates purchase prices for all materials and products", updated.Description);
    }

    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenConfigurationExists_PreservesCronExpressionAndIsEnabled()
    {
        // Arrange - add an existing configuration with an admin-customized CronExpression and IsEnabled
        var existingConfig = new RecurringJobConfiguration(
            "purchase-price-recalculation",
            "Purchase Price Recalculation",
            "Recalculates purchase prices for all materials and products",
            "0 0 * * *", // admin-customized cron, differs from mock job's "0 2 * * *"
            "Europe/Prague",
            false,       // admin-disabled, differs from mock job's DefaultIsEnabled: true
            "System",
            DateTime.UtcNow);

        await _context.RecurringJobConfigurations.AddAsync(existingConfig);
        await _context.SaveChangesAsync();

        var mockJobs = CreateMockJobs();

        // Act
        await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

        // Assert
        var updated = await _repository.GetByJobNameAsync("purchase-price-recalculation");
        Assert.NotNull(updated);
        Assert.Equal("0 0 * * *", updated!.CronExpression);
        Assert.False(updated.IsEnabled);
    }

    [Fact]
    public async Task SeedDefaultConfigurationsAsync_WhenConfigurationExists_SetsLastModifiedByToSystem()
    {
        // Arrange - add an existing configuration whose last modification was made by an admin
        var existingConfig = new RecurringJobConfiguration(
            "purchase-price-recalculation",
            "Purchase Price Recalculation",
            "Recalculates purchase prices for all materials and products",
            "0 2 * * *",
            "Europe/Prague",
            true,
            "Admin",
            DateTime.UtcNow);

        await _context.RecurringJobConfigurations.AddAsync(existingConfig);
        await _context.SaveChangesAsync();

        var mockJobs = CreateMockJobs();

        // Act
        await _seeder.SeedDefaultConfigurationsAsync(mockJobs);

        // Assert
        var updated = await _repository.GetByJobNameAsync("purchase-price-recalculation");
        Assert.NotNull(updated);
        Assert.Equal("System", updated!.LastModifiedBy);
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
            new MockRecurringJob("invoice-classification", "Invoice Classification", "Classifies and categorizes incoming invoices", "0 * * * *", "America/New_York"),
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

        public MockRecurringJob(string jobName, string displayName, string description, string cronExpression, string? timeZoneId = null)
        {
            Metadata = new RecurringJobMetadata
            {
                JobName = jobName,
                DisplayName = displayName,
                Description = description,
                CronExpression = cronExpression,
                DefaultIsEnabled = true,
                TimeZoneId = timeZoneId ?? RecurringJobMetadata.DefaultTimeZoneId
            };
        }

        public Task ExecuteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
