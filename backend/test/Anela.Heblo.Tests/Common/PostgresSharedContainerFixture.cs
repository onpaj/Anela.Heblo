using DotNet.Testcontainers.Configurations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Anela.Heblo.Tests.Common;

/// <summary>
/// Starts a single postgres:16 container once for the entire "PostgresIntegration" collection.
/// Each test class calls CreateDatabaseAsync to get its own isolated database, avoiding the
/// cost of spinning up a new container per test method.
/// </summary>
public sealed class PostgresSharedContainerFixture : IAsyncLifetime
{
    static PostgresSharedContainerFixture()
    {
        // Required on macOS with Podman: the Ryuk ResourceReaper container
        // cannot bind to the Docker socket and throws a NullReferenceException.
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16")
        .Build();

    public async Task InitializeAsync() => await _container.StartAsync();

    public async Task DisposeAsync() => await _container.DisposeAsync();

    /// <summary>
    /// Creates a fresh database in the shared container and returns its connection string.
    /// Each call produces a unique database, giving callers full isolation.
    /// </summary>
    public async Task<string> CreateDatabaseAsync(string nameHint)
    {
        var dbName = $"{nameHint}_{Guid.NewGuid():N}";
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE \"{dbName}\"";
        await cmd.ExecuteNonQueryAsync();

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = dbName
        }.ConnectionString;
    }
}
