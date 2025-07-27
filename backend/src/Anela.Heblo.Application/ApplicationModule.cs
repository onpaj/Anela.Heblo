using Anela.Heblo.Application.Features.Weather;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application;

/// <summary>
/// Main application module registration
/// </summary>
public static class ApplicationModule
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register all feature modules
        services.AddWeatherModule();
        
        // Add more modules here as they are created:
        // services.AddCatalogModule();
        // services.AddOrdersModule();
        // services.AddInvoicesModule();
        // services.AddManufactureModule();
        // services.AddTransportModule();
        // services.AddPurchaseModule();
        
        return services;
    }
}