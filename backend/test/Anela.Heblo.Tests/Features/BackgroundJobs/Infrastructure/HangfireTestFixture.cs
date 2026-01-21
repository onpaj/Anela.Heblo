using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;

/// <summary>
/// Test fixture for Hangfire tests that properly manages LoggerFactory lifecycle
/// to prevent ObjectDisposedException when tests run in bulk.
///
/// This fixture is shared across all test classes in the "Hangfire" collection
/// to ensure only one GlobalConfiguration setup occurs.
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

/// <summary>
/// Collection definition for Hangfire tests.
/// All test classes that use [Collection("Hangfire")] will share the same HangfireTestFixture instance,
/// preventing race conditions when multiple test classes configure GlobalConfiguration in parallel.
/// </summary>
[CollectionDefinition("Hangfire")]
public class HangfireTestCollection : ICollectionFixture<HangfireTestFixture>
{
    // This class has no code, and is never created. Its purpose is simply
    // to be the place to apply [CollectionDefinition] and ICollectionFixture<>.
}
