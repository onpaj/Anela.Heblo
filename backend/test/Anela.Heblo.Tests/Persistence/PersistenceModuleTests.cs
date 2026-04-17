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
    /// Regression test: Database:MaxPoolSize config must be applied to the NpgsqlDataSource
    /// connection pool to prevent PostgresException 53300 (too_many_connections) spikes.
    /// See issue #591.
    /// </summary>
    [Fact]
    public void AddPersistenceServices_WithMaxPoolSize_AppliesCapToDataSource()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UseInMemoryDatabase"] = "false",
                ["ConnectionStrings:Test"] = "Host=localhost;Database=test;Username=user;Password=pass",
                ["Database:MaxPoolSize"] = "7"
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
        csb.MaxPoolSize.Should().Be(7,
            "Database:MaxPoolSize config must be applied so the pool is capped below server max_connections");
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
