using Anela.Heblo.API.Extensions;
using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Hangfire;
using Hangfire.MemoryStorage;
using Hangfire.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Tests.Features.BackgroundJobs;

public class RecurringJobDiscoveryServiceTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public RecurringJobDiscoveryServiceTests()
    {
        var services = new ServiceCollection();

        // Add logging
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));

        // Add web host environment mock
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment { EnvironmentName = "Test" });

        // Configure Hangfire with in-memory storage for testing
        GlobalConfiguration.Configuration
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseMemoryStorage();

        // Register test recurring job
        services.AddScoped<IRecurringJob, TestAsyncRecurringJob>();
        services.AddScoped<TestAsyncRecurringJob>();

        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task StartAsync_WithSchedulerEnabled_RegistersRecurringJobs()
    {
        // Arrange
        var hangfireOptions = Options.Create(new HangfireOptions
        {
            SchedulerEnabled = true,
            QueueName = "test-queue"
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
    public async Task StartAsync_WithSchedulerDisabled_DoesNotRegisterJobs()
    {
        // Arrange
        var hangfireOptions = Options.Create(new HangfireOptions
        {
            SchedulerEnabled = false,
            QueueName = "test-queue"
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
}
