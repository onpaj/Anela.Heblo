using Anela.Heblo.API.Infrastructure.Hangfire;
using Anela.Heblo.Application.Features.BackgroundJobs.Services;
using Anela.Heblo.Domain.Features.BackgroundJobs;
using Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;
using Anela.Heblo.Xcc;
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
public class HangfireRecurringJobSchedulerTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider;

    public HangfireRecurringJobSchedulerTests(HangfireTestFixture fixture)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment { EnvironmentName = "Test" });
        services.AddScoped<IRecurringJob, ParityTestRecurringJob>();
        services.AddScoped<ParityTestRecurringJob>();
        services.AddSingleton<IRecurringJobConfigurationRepository, EmptyStubRepository>();
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void UpdateCronSchedule_WithUnknownJobName_LogsWarningAndReturns()
    {
        // Arrange
        var scheduler = new HangfireRecurringJobScheduler(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<HangfireRecurringJobScheduler>>());

        // Act
        scheduler.UpdateCronSchedule("does-not-exist", "0 0 * * *");

        // Assert — no job was created
        using var connection = JobStorage.Current.GetConnection();
        Assert.DoesNotContain(connection.GetRecurringJobs(), j => j.Id == "does-not-exist");
    }

    [Fact]
    public async Task UpdateCronSchedule_AfterDiscoveryRegistration_UpdatesCronInStorage()
    {
        // Arrange — first register via discovery service (startup path)
        const string newCron = "0 4 * * 2"; // differs from metadata default
        var hangfireOptions = Options.Create(new HangfireOptions { SchedulerEnabled = true });
        var discovery = new RecurringJobDiscoveryService(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<RecurringJobDiscoveryService>>(),
            _serviceProvider.GetRequiredService<IWebHostEnvironment>(),
            hangfireOptions);
        await discovery.StartAsync(CancellationToken.None);

        var scheduler = new HangfireRecurringJobScheduler(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<HangfireRecurringJobScheduler>>());

        // Act — update via the runtime scheduler path
        scheduler.UpdateCronSchedule("parity-test-job", newCron);

        // Assert
        using var connection = JobStorage.Current.GetConnection();
        var job = Assert.Single(connection.GetRecurringJobs(), j => j.Id == "parity-test-job");
        Assert.Equal(newCron, job.Cron);
        Assert.Equal("Europe/Prague", job.TimeZoneId);
    }

    [Fact]
    public async Task UpdateCronSchedule_ProducesIdenticalRecordStructureToDiscoveryRegistration()
    {
        // Arrange — register via the discovery path
        var hangfireOptions = Options.Create(new HangfireOptions { SchedulerEnabled = true });
        var discovery = new RecurringJobDiscoveryService(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<RecurringJobDiscoveryService>>(),
            _serviceProvider.GetRequiredService<IWebHostEnvironment>(),
            hangfireOptions);
        await discovery.StartAsync(CancellationToken.None);

        // Capture the discovery-registered record before updating
        string discoveredTimeZoneId;
        Type discoveredJobType;
        string discoveredMethodName;
        Type discoveredReturnType;

        using (var conn1 = JobStorage.Current.GetConnection())
        {
            var discovered = Assert.Single(conn1.GetRecurringJobs(), j => j.Id == "parity-test-job");
            discoveredTimeZoneId = discovered.TimeZoneId;
            discoveredJobType = discovered.Job.Type;
            discoveredMethodName = discovered.Job.Method.Name;
            discoveredReturnType = discovered.Job.Method.ReturnType;
        }

        // Act — re-register through the scheduler with a new CRON
        var scheduler = new HangfireRecurringJobScheduler(
            _serviceProvider,
            _serviceProvider.GetRequiredService<ILogger<HangfireRecurringJobScheduler>>());
        scheduler.UpdateCronSchedule("parity-test-job", "0 7 * * *");

        // Assert — re-fetch and compare structural metadata
        using (var conn2 = JobStorage.Current.GetConnection())
        {
            var afterUpdate = Assert.Single(conn2.GetRecurringJobs(), j => j.Id == "parity-test-job");

            Assert.Equal(discoveredReturnType, afterUpdate.Job.Method.ReturnType);
            Assert.Equal(typeof(Task), afterUpdate.Job.Method.ReturnType);
            Assert.Equal(discoveredTimeZoneId, afterUpdate.TimeZoneId);
            Assert.Equal(discoveredJobType, afterUpdate.Job.Type);
            Assert.Equal(discoveredMethodName, afterUpdate.Job.Method.Name);
        }
    }

    public void Dispose()
    {
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

    private class ParityTestRecurringJob : IRecurringJob
    {
        public RecurringJobMetadata Metadata { get; } = new()
        {
            JobName = "parity-test-job",
            DisplayName = "Parity Test Job",
            Description = "Used by HangfireRecurringJobSchedulerTests",
            CronExpression = "0 0 * * *",
            TimeZoneId = "Europe/Prague",
        };

        public Task ExecuteAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

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
