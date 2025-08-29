using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Services;

public interface IDatabaseSeeder
{
    Task SeedAsync(CancellationToken cancellationToken = default);
    Task TruncateAndSeedAsync(CancellationToken cancellationToken = default);
}

public class DatabaseSeeder : IDatabaseSeeder
{
    private readonly ApplicationDbContext _context;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseSeeder> _logger;
    private readonly string _environmentName;

    public DatabaseSeeder(
        ApplicationDbContext context,
        IConfiguration configuration,
        ILogger<DatabaseSeeder> logger)
    {
        _context = context;
        _configuration = configuration;
        _logger = logger;
        _environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting database seeding for {Environment} environment", _environmentName);

        // Check if database is already seeded
        if (await _context.TransportBoxes.AnyAsync(cancellationToken))
        {
            _logger.LogInformation("Database already contains data, skipping seeding");
            return;
        }

        await SeedDataAsync(cancellationToken);

        _logger.LogInformation("Database seeding completed successfully");
    }

    public async Task TruncateAndSeedAsync(CancellationToken cancellationToken = default)
    {
        // CRITICAL SAFETY CHECK: Only allow truncation for databases with TEST suffix
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Connection string is not configured");
        }

        // Extract database name from connection string
        var databaseName = ExtractDatabaseName(connectionString);

        // MANDATORY: Database name must end with "TEST" or "TST" to allow truncation
        if (!databaseName.EndsWith("TEST", StringComparison.OrdinalIgnoreCase) &&
            !databaseName.EndsWith("TST", StringComparison.OrdinalIgnoreCase) &&
            !databaseName.EndsWith("_TEST", StringComparison.OrdinalIgnoreCase) &&
            !databaseName.EndsWith("_TST", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"SAFETY CHECK FAILED: Database '{databaseName}' does not have TEST/TST suffix. " +
                "Truncation is only allowed for test databases to prevent accidental data loss in production.");
        }

        _logger.LogWarning("Starting database truncation for {DatabaseName} in {Environment} environment",
            databaseName, _environmentName);

        // Truncate all tables (preserve schema)
        await TruncateTablesAsync(cancellationToken);

        // Seed fresh data
        await SeedDataAsync(cancellationToken);

        _logger.LogInformation("Database truncation and seeding completed successfully");
    }

    private async Task TruncateTablesAsync(CancellationToken cancellationToken)
    {
        using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Disable foreign key constraints temporarily
            await _context.Database.ExecuteSqlRawAsync("SET session_replication_role = 'replica';", cancellationToken);

            // Truncate all tables
            await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"TransportBoxes\" CASCADE;", cancellationToken);
            await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"PurchaseOrders\" CASCADE;", cancellationToken);
            await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"PurchaseOrderLines\" CASCADE;", cancellationToken);
            await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"PurchaseOrderHistory\" CASCADE;", cancellationToken);
            await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"JournalEntries\" CASCADE;", cancellationToken);
            await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"JournalEntryProducts\" CASCADE;", cancellationToken);
            await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"JournalEntryTags\" CASCADE;", cancellationToken);
            await _context.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"JournalEntryTagAssignments\" CASCADE;", cancellationToken);

            // Re-enable foreign key constraints
            await _context.Database.ExecuteSqlRawAsync("SET session_replication_role = 'origin';", cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation("All tables truncated successfully");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Failed to truncate tables");
            throw;
        }
    }

    private async Task SeedDataAsync(CancellationToken cancellationToken)
    {
        // Simple seed data creation - minimal data for E2E testing
        _logger.LogInformation("Creating minimal seed data for staging environment");

        // For now, just ensure the tables exist and database connection works
        // Real seeding implementation can be added once domain models are stabilized
        await _context.Database.EnsureCreatedAsync(cancellationToken);

        _logger.LogInformation("Database schema ensured for staging environment");
    }

    private string ExtractDatabaseName(string connectionString)
    {
        // Parse PostgreSQL connection string to extract database name
        var parts = connectionString.Split(';');
        foreach (var part in parts)
        {
            var keyValue = part.Split('=');
            if (keyValue.Length == 2)
            {
                var key = keyValue[0].Trim().ToLowerInvariant();
                if (key == "database" || key == "initial catalog")
                {
                    return keyValue[1].Trim();
                }
            }
        }

        // Try to extract from URL-style connection string
        if (connectionString.Contains("://"))
        {
            var uri = new Uri(connectionString.Replace("postgresql://", "http://").Replace("postgres://", "http://"));
            var path = uri.AbsolutePath.TrimStart('/');
            if (!string.IsNullOrEmpty(path))
            {
                return path.Split('?')[0];
            }
        }

        throw new InvalidOperationException("Could not extract database name from connection string");
    }
}