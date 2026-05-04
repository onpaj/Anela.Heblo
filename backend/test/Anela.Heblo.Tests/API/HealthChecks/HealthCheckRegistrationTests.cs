using System.Linq;
using Anela.Heblo.API.Extensions;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.API.HealthChecks;

public class HealthCheckRegistrationTests
{
    [Fact]
    public void DatabaseCheck_IsRegistered_AndResolvesSingletonNpgsqlDataSource()
    {
        // Arrange: build a service collection mirroring Program.cs order.
        var services = BuildServices(useInMemory: false);

        // Act
        using var provider = services.BuildServiceProvider();
        var ds1 = provider.GetRequiredService<NpgsqlDataSource>();
        var ds2 = provider.GetRequiredService<NpgsqlDataSource>();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.ToList();

        // Assert
        ds1.Should().BeSameAs(ds2, "NpgsqlDataSource must be a singleton shared by all consumers");
        registrations.Should().Contain(r => r.Name == ConfigurationConstants.DATABASE_HEALTH_CHECK);
    }

    [Fact]
    public void DatabaseCheck_IsNotRegistered_WhenUseInMemoryDatabaseIsTrue()
    {
        // Arrange
        var services = BuildServices(useInMemory: true);

        // Precondition: NpgsqlDataSource was not registered (PersistenceModule skipped it for InMemory).
        services.Any(d => d.ServiceType == typeof(NpgsqlDataSource)).Should().BeFalse();

        // Act
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.ToList();

        // Assert
        registrations.Should().NotContain(r => r.Name == ConfigurationConstants.DATABASE_HEALTH_CHECK);
    }

    [Fact]
    public void DataQualityAndDatabaseChecks_HaveFiveSecondTimeout_ByDefault()
    {
        // Arrange
        var services = BuildServices(useInMemory: false);

        // Act
        using var provider = services.BuildServiceProvider();
        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>()
            .Value.Registrations.ToList();

        // Assert
        // "data-quality-schema" is defined as ProbeName in DataQualitySchemaHealthCheck (private const).
        var dataQuality = registrations.Single(r => r.Name == "data-quality-schema");
        var database = registrations.Single(r => r.Name == ConfigurationConstants.DATABASE_HEALTH_CHECK);
        dataQuality.Timeout.Should().Be(TimeSpan.FromSeconds(5));
        database.Timeout.Should().Be(TimeSpan.FromSeconds(5));
    }

    private static IServiceCollection BuildServices(bool useInMemory)
    {
        var configValues = new Dictionary<string, string?>
        {
            ["UseInMemoryDatabase"] = useInMemory ? "true" : "false",
            // PersistenceModule reads ConnectionStrings:<EnvironmentName>
            ["ConnectionStrings:UnitTest"] = useInMemory
                ? "InMemory"
                : "Host=localhost;Database=heblo_test;Username=u;Password=p",
            // Default 5s probe timeout (no override)
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns("UnitTest");

        var services = new ServiceCollection();
        // Note: AddXccServices (which registers IBackgroundServiceReadinessTracker required by
        // BackgroundServicesReadyHealthCheck) is intentionally omitted — these tests only inspect
        // HealthCheckRegistration metadata, not resolve or invoke checks from the container.
        services.AddLogging(b => b.AddProvider(NullLoggerProvider.Instance));
        services.AddPersistenceServices(configuration, environment.Object);
        services.AddHealthCheckServices(configuration);
        return services;
    }
}
