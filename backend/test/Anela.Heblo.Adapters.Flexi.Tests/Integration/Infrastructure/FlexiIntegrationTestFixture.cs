using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Adapters.Flexi.Tests.Integration.Infrastructure;

public class FlexiIntegrationTestFixture : IDisposable
{
    public IServiceProvider ServiceProvider { get; }
    public IConfiguration Configuration { get; }

    /// <summary>
    /// Fixed reference date for deterministic testing (2025-06-01) in UTC.
    /// This represents midnight UTC, which corresponds to 2:00 AM in Prague (UTC+2).
    /// </summary>
    public static DateTime ReferenceDate { get; } = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Prague timezone for consistent test behavior
    /// </summary>
    public static TimeZoneInfo TestTimeZone { get; } = TimeZoneInfo.FindSystemTimeZoneById("Central Europe Standard Time");


    public FlexiIntegrationTestFixture()
    {
        // Set timezone to Prague/Central Europe for consistent test behavior across environments
        Environment.SetEnvironmentVariable("TZ", "Europe/Prague");
        var configBuilder = new ConfigurationBuilder()
            .AddUserSecrets<FlexiIntegrationTestFixture>()
            .AddEnvironmentVariables();

        Configuration = configBuilder.Build();

        var services = new ServiceCollection();

        // Add FlexiBee SDK
        services.AddFlexiAdapter(Configuration);

        // Add logging
        services.AddLogging();

        ServiceProvider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        if (ServiceProvider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}