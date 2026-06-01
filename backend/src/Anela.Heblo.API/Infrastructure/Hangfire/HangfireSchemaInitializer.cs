using Npgsql;

namespace Anela.Heblo.API.Infrastructure.Hangfire;

public class HangfireSchemaInitializer
{
    private readonly string _connectionString;
    private readonly ILogger<HangfireSchemaInitializer> _logger;
    private const string HANGFIRE_SCHEMA_NAME = "hangfire_heblo";

    public HangfireSchemaInitializer(string connectionString, ILogger<HangfireSchemaInitializer> logger)
    {
        _connectionString = connectionString ?? throw new ArgumentNullException(nameof(connectionString));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task EnsureSchemaExistsAsync()
    {
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            await connection.OpenAsync();

            // Check if schema exists
            const string checkSchemaQuery = @"
                SELECT COUNT(*) 
                FROM information_schema.schemata 
                WHERE schema_name = @schemaName";

            using var checkCommand = new NpgsqlCommand(checkSchemaQuery, connection);
            checkCommand.Parameters.AddWithValue("@schemaName", HANGFIRE_SCHEMA_NAME);

            var schemaExists = (long)await checkCommand.ExecuteScalarAsync() > 0;

            if (!schemaExists)
            {
                _logger.LogInformation("Creating Hangfire schema '{SchemaName}'", HANGFIRE_SCHEMA_NAME);

                // Create schema
                const string createSchemaQuery = $"CREATE SCHEMA {HANGFIRE_SCHEMA_NAME};";
                using var createCommand = new NpgsqlCommand(createSchemaQuery, connection);
                await createCommand.ExecuteNonQueryAsync();

                _logger.LogInformation("Successfully created Hangfire schema '{SchemaName}'", HANGFIRE_SCHEMA_NAME);
            }
            else
            {
                _logger.LogDebug("Hangfire schema '{SchemaName}' already exists", HANGFIRE_SCHEMA_NAME);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to ensure Hangfire schema '{SchemaName}' exists", HANGFIRE_SCHEMA_NAME);
            throw;
        }
    }
}