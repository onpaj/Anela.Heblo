using Anela.Heblo.Xcc.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Persistence;

/// <summary>
/// Extension methods for registering persistence services
/// </summary>
public static class PersistenceModule
{
    public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register DbContext
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString("Default");
            var useInMemory = bool.Parse(configuration["UseInMemoryDatabase"] ?? "false");

            if (useInMemory || string.IsNullOrEmpty(connectionString) || connectionString == "InMemory")
            {
                // For testing scenarios where no real database is needed
                options.UseInMemoryDatabase("TestDatabase");
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
        });

        // Register telemetry services
        services.AddScoped<ITelemetryService, NoOpTelemetryService>(); // Default to NoOp, can be overridden by API layer

        return services;
    }
}