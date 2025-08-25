using Anela.Heblo.Application.Features.Manufacture.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Manufacture;

public static class ManufactureModule
{
    public static IServiceCollection AddManufactureModule(this IServiceCollection services)
    {
        // Register MediatR handlers - they will be automatically discovered
        
        // Register domain services for manufacturing stock analysis
        services.AddScoped<ITimePeriodCalculator, TimePeriodCalculator>();
        services.AddScoped<IManufactureAnalysisMapper, ManufactureAnalysisMapper>();
        services.AddScoped<IManufactureSeverityCalculator, ManufactureSeverityCalculator>();
        services.AddScoped<IItemFilterService, ItemFilterService>();

        return services;
    }
}