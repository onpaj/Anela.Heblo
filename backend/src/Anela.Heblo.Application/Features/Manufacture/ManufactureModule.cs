using Anela.Heblo.Application.Features.Manufacture.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Manufacture;

public static class ManufactureModule
{
    public static IServiceCollection AddManufactureModule(this IServiceCollection services)
    {
        // Register MediatR handlers - they will be automatically discovered
        
        // Register domain services for manufacturing stock analysis
        services.AddScoped<IConsumptionRateCalculator, ConsumptionRateCalculator>();
        services.AddScoped<IProductionActivityAnalyzer, ProductionActivityAnalyzer>();
        services.AddScoped<IManufactureSeverityCalculator, ManufactureSeverityCalculator>();
        services.AddScoped<IManufactureAnalysisMapper, ManufactureAnalysisMapper>();

        return services;
    }
}