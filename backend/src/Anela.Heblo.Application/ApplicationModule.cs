using Anela.Heblo.Application.Features.Weather;
using Anela.Heblo.Application.Features.Configuration;
using Anela.Heblo.Application.features.catalog;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application;

/// <summary>
/// Main application module registration
/// </summary>
public static class ApplicationModule
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        // Register MediatR
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(ApplicationModule).Assembly));

        // Register AutoMapper
        services.AddAutoMapper(typeof(ApplicationModule).Assembly);

        // Register all feature modules
        services.AddWeatherModule();
        services.AddConfigurationModule();
        services.AddCatalogModule();
        // services.AddOrdersModule();
        // services.AddInvoicesModule();
        // services.AddManufactureModule();
        // services.AddTransportModule();
        // services.AddPurchaseModule();

        return services;
    }
}