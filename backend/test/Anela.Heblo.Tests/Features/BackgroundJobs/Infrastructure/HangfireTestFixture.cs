using Hangfire;
using Hangfire.MemoryStorage;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Anela.Heblo.Tests.Features.BackgroundJobs.Infrastructure;

/// <summary>
/// Test fixture for Hangfire tests that properly manages LoggerFactory lifecycle
/// to prevent ObjectDisposedException when tests run in bulk.
///
/// Uses a static LoggerFactory that persists for the entire test run,
/// ensuring Hangfire's global configuration always has access to a valid logger.
/// This fixture is shared across all test classes in the "Hangfire" collection
/// to ensure only one GlobalConfiguration setup occurs.
/// </summary>
public class HangfireTestFixture : IDisposable
{
    // Static LoggerFactory that lives for the entire test run
    // This prevents ObjectDisposedException when Hangfire's global configuration
    // tries to access the logger across multiple test classes
    private static readonly ILoggerFactory StaticLoggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Warning); // Reduce test noise
    });

    private static readonly object LockObject = new object();
    private static bool _isConfigured = false;

    public HangfireTestFixture()
    {
        // Thread-safe initialization - only configure Hangfire once for all tests
        lock (LockObject)
        {
            if (!_isConfigured)
            {
                // Disable Hangfire's default logging to prevent LoggerFactory disposal issues
                // Hangfire's AspNetCoreLogProvider tries to access a LoggerFactory that may not exist in test context
                Hangfire.Logging.LogProvider.SetCurrentLogProvider(null);

                // Configure Hangfire with in-memory storage
                // This sets up the global configuration that all tests will share
                GlobalConfiguration.Configuration
                    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
                    .UseSimpleAssemblyNameTypeSerializer()
                    .UseRecommendedSerializerSettings()
                    .UseMemoryStorage();

                _isConfigured = true;
            }
        }
    }

    public void Dispose()
    {
        // Do NOT dispose StaticLoggerFactory - it must remain alive for all tests
        // It will be cleaned up when the test process exits
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
