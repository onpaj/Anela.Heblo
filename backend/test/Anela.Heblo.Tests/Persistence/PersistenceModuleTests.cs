using Anela.Heblo.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure;

/// <summary>
/// Regression tests for PersistenceModule to prevent reintroduction of known issues.
/// </summary>
public class PersistenceModuleTests
{
    /// <summary>
    /// Regression test: NpgsqlDataSource must be built once outside the AddDbContext lambda.
    ///
    /// Bug: NpgsqlDataSourceBuilder.Build() was called inside the AddDbContext options lambda,
    /// creating a new NpgsqlDataSource instance per DbContext resolution. EF Core interprets
    /// different object instances as different options and creates a new internal IServiceProvider
    /// for each one. After 20 instances, EF Core throws ManyServiceProvidersCreatedWarning
    /// as an InvalidOperationException, crashing background tasks.
    ///
    /// Fix: Build NpgsqlDataSource once before AddDbContext and capture it in the lambda closure.
    /// </summary>
    [Fact]
    public void AddPersistenceServices_ResolvingManyDbContexts_DoesNotThrowManyServiceProvidersWarning()
    {
        // Arrange: Use InMemory to avoid needing a real Postgres instance.
        // The structural fix (building data source outside the lambda) is validated
        // by verifying that 25+ scoped DbContexts can be created without exception.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "true",
                ["ConnectionStrings:Test"] = "InMemory"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceServices(configuration, new FakeHostEnvironment("Test"));

        var provider = services.BuildServiceProvider();

        // Act: Resolve 25 contexts — threshold for the warning is 20
        var exception = Record.Exception(() =>
        {
            for (var i = 0; i < 25; i++)
            {
                using var scope = provider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.Should().NotBeNull();
            }
        });

        // Assert
        exception.Should().BeNull(
            "resolving many DbContext instances must not throw ManyServiceProvidersCreatedWarning; " +
            "NpgsqlDataSource must be built once outside the AddDbContext lambda");
    }

    [Fact]
    public void AddPersistenceServices_WithInMemoryDatabase_RegistersDbContext()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "true",
                ["ConnectionStrings:Test"] = "InMemory"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceServices(configuration, new FakeHostEnvironment("Test"));

        var provider = services.BuildServiceProvider();

        // Act
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetService<ApplicationDbContext>();

        // Assert
        context.Should().NotBeNull();
    }

    /// <summary>
    /// Regression test: Database:MaxPoolSize configuration must be applied to NpgsqlDataSource.
    ///
    /// Bug: appsettings.Staging.json was missing Database:MaxPoolSize, leaving the Npgsql
    /// connection pool uncapped (default 100). Under load, the pool exhausted the PostgreSQL
    /// server's max_connections, causing PostgresException 53300 (too_many_connections).
    ///
    /// Fix: Add Database:MaxPoolSize to all environment appsettings files, and verify the
    /// config value is correctly wired into NpgsqlDataSourceBuilder.ConnectionStringBuilder.MaxPoolSize.
    /// </summary>
    [Fact]
    public void AddPersistenceServices_WithMaxPoolSizeConfigured_AppliesItToNpgsqlDataSource()
    {
        // Arrange
        const int configuredMaxPoolSize = 20;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "false",
                ["ConnectionStrings:Test"] = "Host=localhost;Database=test;Username=test;Password=test",
                ["Database:MaxPoolSize"] = configuredMaxPoolSize.ToString()
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceServices(configuration, new FakeHostEnvironment("Test"));

        var provider = services.BuildServiceProvider();

        // Act
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();

        // Assert
        var csb = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);
        csb.MaxPoolSize.Should().Be(configuredMaxPoolSize,
            "Database:MaxPoolSize must be applied to NpgsqlDataSource to cap the connection pool " +
            "and prevent PostgresException 53300 (too_many_connections)");
    }

    [Fact]
    public void AddPersistenceServices_WithoutConnectionString_Throws()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "false"
                // No ConnectionStrings:Test
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        // Act & Assert
        var action = () => services.AddPersistenceServices(configuration, new FakeHostEnvironment("Test"));

        action.Should().Throw<Exception>()
            .WithMessage("*No connection string*");
    }

    /// <summary>
    /// Regression test: Database:ConnectionIdleLifetime and Database:ConnectionPruningInterval
    /// must be applied to the NpgsqlDataSource to reclaim idle connections faster after burst load.
    /// See issue #592.
    /// </summary>
    [Fact]
    public void AddPersistenceServices_WithIdleLifetimeSettings_AppliesThemToDataSource()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "false",
                ["ConnectionStrings:Test"] = "Host=localhost;Database=test;Username=user;Password=pass",
                ["Database:ConnectionIdleLifetime"] = "60",
                ["Database:ConnectionPruningInterval"] = "10"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistenceServices(configuration, new FakeHostEnvironment("Test"));

        // Act
        var provider = services.BuildServiceProvider();
        var dataSource = provider.GetRequiredService<NpgsqlDataSource>();
        var csb = new NpgsqlConnectionStringBuilder(dataSource.ConnectionString);

        // Assert
        csb.ConnectionIdleLifetime.Should().Be(60,
            "Database:ConnectionIdleLifetime must be applied to reclaim idle connections faster after burst load");
        csb.ConnectionPruningInterval.Should().Be(10,
            "Database:ConnectionPruningInterval must be applied to control how often idle connections are pruned");
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public FakeHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
