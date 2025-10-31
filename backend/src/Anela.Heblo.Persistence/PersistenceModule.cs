using Anela.Heblo.Domain.Features.InvoiceClassification;
using Anela.Heblo.Persistence.Dashboard;
using Anela.Heblo.Persistence.InvoiceClassification;
using Anela.Heblo.Xcc.Services.Dashboard;
using Anela.Heblo.Xcc.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Persistence;

/// <summary>
/// Extension methods for registering persistence services
/// </summary>
public static class PersistenceModule
{
    public static IServiceCollection AddPersistenceServices(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        // Register DbContext
        services.AddDbContext<ApplicationDbContext>(options =>
        {
            var connectionString = configuration.GetConnectionString(environment.EnvironmentName);

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new Exception($"No connection string '{environment.EnvironmentName}' found in configuration.");
            }

            var useInMemory = bool.Parse(configuration["UseInMemoryDatabase"] ?? "false");

            if (useInMemory || connectionString == "InMemory")
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

        // Register repositories
        services.AddScoped<IUserDashboardSettingsRepository, UserDashboardSettingsRepository>();
        
        // Invoice Classification repositories
        services.AddScoped<IClassificationRuleRepository, ClassificationRuleRepository>();
        services.AddScoped<IClassificationHistoryRepository, ClassificationHistoryRepository>();

        return services;
    }
}