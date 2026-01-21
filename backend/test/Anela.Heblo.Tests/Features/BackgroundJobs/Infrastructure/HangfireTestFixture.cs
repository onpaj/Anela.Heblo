using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;

/// <summary>
/// Test fixture for Hangfire tests that properly manages LoggerFactory lifecycle
/// to prevent ObjectDisposedException when tests run in bulk.
/// </summary>
public class HangfireTestFixture : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;

    public HangfireTestFixture()
    {
        // Create a LoggerFactory that will live for the duration of all tests
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning); // Reduce test noise
        });

        // Configure Hangfire with in-memory storage
        // This sets up the global configuration that all tests will share
        GlobalConfiguration.Configuration
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseMemoryStorage();
    }

    public void Dispose()
    {
        _loggerFactory?.Dispose();
    }
}
