using Anela.Heblo.API.Infrastructure.Hangfire;
using Microsoft.Extensions.Logging;
using Moq;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Infrastructure.Hangfire;

public class HangfireSchemaInitializerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container;
    private readonly Mock<ILogger<HangfireSchemaInitializer>> _loggerMock;

    public HangfireSchemaInitializerTests()
    {
        _container = new PostgreSqlBuilder()
            .WithDatabase("test_hangfire")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .Build();

        _loggerMock = new Mock<ILogger<HangfireSchemaInitializer>>();
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
    public async Task EnsureSchemaExistsAsync_CreatesSchemaWhenNotExists()
    {
        // Arrange
        var connectionString = _container.GetConnectionString();
        var initializer = new HangfireSchemaInitializer(connectionString, _loggerMock.Object);

        // Act
        await initializer.EnsureSchemaExistsAsync();

        // Assert
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

        // Verify logging
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("Creating Hangfire schema")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task EnsureSchemaExistsAsync_DoesNotCreateSchemaWhenExists()
    {
        // Arrange
        var connectionString = _container.GetConnectionString();
        var initializer = new HangfireSchemaInitializer(connectionString, _loggerMock.Object);

        // Create schema first
        using (var connection = new NpgsqlConnection(connectionString))
        {
            await connection.OpenAsync();
            using var command = new NpgsqlCommand("CREATE SCHEMA hangfire_heblo;", connection);
            await command.ExecuteNonQueryAsync();
        }

        // Act
        await initializer.EnsureSchemaExistsAsync();

        // Assert - Schema should still exist (only 1, not 2)
        using var connection2 = new NpgsqlConnection(connectionString);
        await connection2.OpenAsync();

        const string checkSchemaQuery = @"
            SELECT COUNT(*) 
            FROM information_schema.schemata 
            WHERE schema_name = @schemaName";

        using var command2 = new NpgsqlCommand(checkSchemaQuery, connection2);
        command2.Parameters.AddWithValue("@schemaName", "hangfire_heblo");

        var result = (long)await command2.ExecuteScalarAsync();
        Assert.Equal(1, result);

        // Verify it logged that schema already exists
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString().Contains("already exists")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenConnectionStringIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HangfireSchemaInitializer(null, _loggerMock.Object));
    }

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenLoggerIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() =>
            new HangfireSchemaInitializer("connection", null));
    }
}