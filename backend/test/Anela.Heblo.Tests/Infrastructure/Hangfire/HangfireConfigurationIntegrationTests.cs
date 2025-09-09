using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;
using Anela.Heblo.API.Extensions;

namespace Anela.Heblo.Tests.Infrastructure.Hangfire;

public class HangfireConfigurationIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;

    public HangfireConfigurationIntegrationTests()
    {
        _container = new PostgreSqlBuilder()
            .WithDatabase("test_hangfire_integration")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .Build();
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    [Fact]
    public async Task HangfireServices_ConfiguresWithHangfireHebloSchema()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        var environment = CreateEnvironment("Production");

        // Add logging
        services.AddLogging(builder => builder.AddConsole());

        // Act
        services.AddHangfireServices(configuration, environment);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Verify Hangfire schema was created
        var connectionString = _container.GetConnectionString();
        using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();

        const string checkSchemaQuery = @"
            SELECT COUNT(*) 
            FROM information_schema.schemata 
            WHERE schema_name = @schemaName";

        using var command = new NpgsqlCommand(checkSchemaQuery, connection);
        command.Parameters.AddWithValue("@schemaName", "hangfire_heblo");

        var result = (long)await command.ExecuteScalarAsync();
        Assert.Equal(1, result);

        // Verify Hangfire tables are created in correct schema
        const string checkTablesQuery = @"
            SELECT COUNT(*) 
            FROM information_schema.tables 
            WHERE table_schema = @schemaName 
            AND table_name LIKE 'hangfire%'";

        using var tablesCommand = new NpgsqlCommand(checkTablesQuery, connection);
        tablesCommand.Parameters.AddWithValue("@schemaName", "hangfire_heblo");

        var tablesResult = (long)await tablesCommand.ExecuteScalarAsync();
        Assert.True(tablesResult > 0, "Hangfire tables should be created in hangfire_heblo schema");

        // Clean up
        await serviceProvider.DisposeAsync();
    }

    [Fact]
    public void HangfireServices_UsesMemoryStorageForTestEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = CreateConfiguration();
        var environment = CreateEnvironment("Test");

        // Add logging
        services.AddLogging(builder => builder.AddConsole());

        // Act
        services.AddHangfireServices(configuration, environment);
        var serviceProvider = services.BuildServiceProvider();

        // Assert - Should configure without issues (memory storage doesn't need schema)
        var backgroundJobClient = serviceProvider.GetService<IBackgroundJobClient>();
        Assert.NotNull(backgroundJobClient);

        // Clean up
        serviceProvider.Dispose();
    }

    [Fact]
    public void HangfireServices_ThrowsException_WhenConnectionStringMissing()
    {
        // Arrange
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build(); // Empty configuration
        var environment = CreateEnvironment("Production");

        // Add logging
        services.AddLogging(builder => builder.AddConsole());

        // Act & Assert
        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddHangfireServices(configuration, environment));

        Assert.Contains("Database connection string is required", exception.Message);
    }

    private IConfiguration CreateConfiguration()
    {
        var configData = new Dictionary<string, string>
        {
            ["ConnectionStrings:DefaultConnection"] = _container.GetConnectionString()
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private IWebHostEnvironment CreateEnvironment(string environmentName)
    {
        var environment = new Mock<IWebHostEnvironment>();
        environment.Setup(e => e.EnvironmentName).Returns(environmentName);
        return environment.Object;
    }
}