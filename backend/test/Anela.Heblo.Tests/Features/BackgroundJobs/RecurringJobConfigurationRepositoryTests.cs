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
            "Europe/Prague",
            true,
            "TestUser",
            DateTime.UtcNow);

        var config2 = new RecurringJobConfiguration(
            "TestJob2",
            "Test Job 2",
            "Description for test job 2",
            "0 12 * * *",
            "Europe/Prague",
            false,
            "TestUser",
            DateTime.UtcNow);

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
            "America/New_York",
            true,
            "TestUser",
            DateTime.UtcNow);

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
        Assert.Equal("America/New_York", result.TimeZoneId);
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
            "Europe/Prague",
            true,
            "OriginalUser",
            DateTime.UtcNow);

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
            "America/New_York",
            "UpdatedUser",
            DateTime.UtcNow);

        await _repository.UpdateAsync(loadedConfig);

        // Assert - verify changes persisted
        var updatedConfig = await _repository.GetByJobNameAsync("JobToUpdate");
        Assert.NotNull(updatedConfig);
        Assert.Equal("Updated Display Name", updatedConfig.DisplayName);
        Assert.Equal("Updated description", updatedConfig.Description);
        Assert.Equal("0 10 * * *", updatedConfig.CronExpression);
        Assert.Equal("America/New_York", updatedConfig.TimeZoneId);
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
            "Europe/Prague",
            true,
            "OriginalUser",
            DateTime.UtcNow);

        await _context.RecurringJobConfigurations.AddAsync(config);
        await _context.SaveChangesAsync();

        // Detach the entity
        _context.Entry(config).State = EntityState.Detached;

        // Load fresh entity
        var loadedConfig = await _repository.GetByJobNameAsync("JobToDisable");
        Assert.NotNull(loadedConfig);

        // Act
        loadedConfig.Disable("DisablingUser", DateTime.UtcNow);
        await _repository.UpdateAsync(loadedConfig);

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
            "Europe/Prague",
            true,
            "System",
            DateTime.UtcNow);

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

    public void Dispose()
    {
        _context.Dispose();
    }
}
