using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Persistence.Services;

public class DatabaseSeedingHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DatabaseSeedingHostedService> _logger;
    private readonly string _environmentName;

    public DatabaseSeedingHostedService(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<DatabaseSeedingHostedService> logger)
    {
        _serviceProvider = serviceProvider;
        _configuration = configuration;
        _logger = logger;
        _environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Only run in Staging environment
        if (_environmentName != "Staging")
        {
            _logger.LogDebug("Skipping database seeding - not in Staging environment (current: {Environment})",
                _environmentName);
            return;
        }

        var enableAutoSeed = _configuration["DatabaseSeeding:EnableAutoSeed"]?.ToLowerInvariant() == "true";
        if (!enableAutoSeed)
        {
            _logger.LogInformation("Database auto-seeding is disabled in configuration");
            return;
        }

        var truncateOnStartup = _configuration["DatabaseSeeding:TruncateOnStartup"]?.ToLowerInvariant() == "true";

        _logger.LogInformation("Starting database seeding service for Staging environment (Truncate: {Truncate})",
            truncateOnStartup);

        using var scope = _serviceProvider.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<IDatabaseSeeder>();

        try
        {
            if (truncateOnStartup)
            {
                await seeder.TruncateAndSeedAsync(cancellationToken);
            }
            else
            {
                await seeder.SeedAsync(cancellationToken);
            }

            _logger.LogInformation("Database seeding completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed database");
            // Don't throw - allow application to start even if seeding fails
            // This prevents startup failures in staging environment
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}