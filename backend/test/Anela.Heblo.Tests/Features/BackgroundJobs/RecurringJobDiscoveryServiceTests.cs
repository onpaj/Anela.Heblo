using Anela.Heblo.API.Extensions;
using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

[Collection("Hangfire")]
public class RecurringJobDiscoveryServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public RecurringJobDiscoveryServiceTests(HangfireTestFixture fixture)
    {
        // Hangfire is already initialized by the shared collection fixture
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // Add web host environment mock
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment { EnvironmentName = "Test" });

        // Register test recurring job
        services.AddScoped<IRecurringJob, TestAsyncRecurringJob>();
        services.AddScoped<TestAsyncRecurringJob>();

        // Register stub repository that returns empty config list (tests metadata fallback)
        services.AddSingleton<IRecurringJobConfigurationRepository, StubRecurringJobConfigurationRepository>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task StartAsync_WithSchedulerEnabled_RegistersRecurringJobs()
    {
        // Arrange
        var hangfireOptions = Options.Create(new HangfireOptions
        {
            SchedulerEnabled = true
        });

        var service = new RecurringJobDiscoveryService(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<RecurringJobDiscoveryService>>(),
            _serviceProvider.GetRequiredService<IWebHostEnvironment>(),
            hangfireOptions
        );

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        using var connection = JobStorage.Current.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();

        Assert.NotEmpty(recurringJobs);
        var testJob = Assert.Single(recurringJobs, job => job.Id == "test-async-job");
        Assert.NotNull(testJob);

        // Verify the job method signature is async (returns Task)
        Assert.Equal(typeof(Task), testJob.Job.Method.ReturnType);
    }

    [Fact]
    public async Task StartAsync_WhenDbConfigExists_UsesDbCronInsteadOfMetadataDefault()
    {
        // Arrange — separate service provider with a stub repo that returns a DB config
        // for the test job with a DIFFERENT cron than the metadata default ("0 0 * * *")
        const string dbCron = "0 3 * * 1"; // Every Monday at 03:00 — differs from metadata "0 0 * * *"

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment { EnvironmentName = "Test" });
        services.AddScoped<IRecurringJob, TestAsyncRecurringJob>();
        services.AddScoped<TestAsyncRecurringJob>();
        services.AddSingleton<IRecurringJobConfigurationRepository>(
            new StubDbRecurringJobConfigurationRepository(dbCron));

        using var sp = services.BuildServiceProvider();

        var hangfireOptions = Options.Create(new HangfireOptions { SchedulerEnabled = true });
        var service = new RecurringJobDiscoveryService(
            sp,
            sp.GetRequiredService<ILogger<RecurringJobDiscoveryService>>(),
            sp.GetRequiredService<IWebHostEnvironment>(),
            hangfireOptions
        );

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert — Hangfire storage must reflect the DB cron, not the metadata default
        using var connection = JobStorage.Current.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();

        var testJob = Assert.Single(recurringJobs, job => job.Id == "test-async-job");
        Assert.Equal(dbCron, testJob.Cron);
    }

    [Fact]
    public async Task StartAsync_WithSchedulerDisabled_DoesNotRegisterJobs()
    {
        // Arrange
        var hangfireOptions = Options.Create(new HangfireOptions
        {
            SchedulerEnabled = false
        });

        var service = new RecurringJobDiscoveryService(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<RecurringJobDiscoveryService>>(),
            _serviceProvider.GetRequiredService<IWebHostEnvironment>(),
            hangfireOptions
        );

        // Act
        await service.StartAsync(CancellationToken.None);

        // Assert
        using var connection = JobStorage.Current.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();

        Assert.Empty(recurringJobs);
    }

    public void Dispose()
    {
        // Clear all recurring jobs from in-memory storage
        using var connection = JobStorage.Current.GetConnection();
        foreach (var job in connection.GetRecurringJobs())
        {
            RecurringJob.RemoveIfExists(job.Id);
        }

        _serviceProvider?.Dispose();
    }

    private class TestWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "TestApp";
        public string ContentRootPath { get; set; } = "/test";
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
        public string WebRootPath { get; set; } = "/test/wwwroot";
        public IFileProvider WebRootFileProvider { get; set; } = null!;
    }

    private class TestAsyncRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata { get; } = new()
        {
            JobName = "test-async-job",
            DisplayName = "Test Async Recurring Job",
            Description = "Test job that returns Task to verify async overload registration",
            CronExpression = "0 0 * * *",
            DefaultIsEnabled = true,
            TimeZoneId = "Europe/Prague"
        };

        public Task ExecuteAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }

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
