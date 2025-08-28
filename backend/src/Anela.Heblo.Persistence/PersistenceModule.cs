using Anela.Heblo.Xcc.Infrastructure;
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

        // Register Unit of Work with unitfactory function
        services.AddScoped<IUnitOfWork>(provider =>
        {
            var context = provider.GetRequiredService<ApplicationDbContext>();
            // Create a factory function that can resolve repository instances
            Func<Type, object?> repositoryFactory = type => provider.GetService(type);
            return new UnitOfWork(context, repositoryFactory);
        });

        // Register telemetry services
        services.AddScoped<ITelemetryService, NoOpTelemetryService>(); // Default to NoOp, can be overridden by API layer

        return services;
    }
}