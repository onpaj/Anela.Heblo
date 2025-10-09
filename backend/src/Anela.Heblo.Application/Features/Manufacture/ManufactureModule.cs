using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Anela.Heblo.Application.Features.Manufacture.Dashboard;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Persistence.Manufacture;
using Anela.Heblo.Xcc.Services.Dashboard;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.Manufacture;

public static class ManufactureModule
{
    public static IServiceCollection AddManufactureModule(this IServiceCollection services, IConfiguration configuration)
    {
        // Register MediatR handlers - they will be automatically discovered

        // Register configuration options
        services.Configure<ManufactureAnalysisOptions>(options =>
        {
            configuration.GetSection("ManufactureAnalysis").Bind(options);
        });

        // Register domain services for manufacturing stock analysis
        services.AddScoped<ITimePeriodCalculator, TimePeriodCalculator>();
        services.AddScoped<IConsumptionRateCalculator, ConsumptionRateCalculator>();
        services.AddScoped<IProductionActivityAnalyzer, ProductionActivityAnalyzer>();
        services.AddScoped<IManufactureSeverityCalculator, ManufactureSeverityCalculator>();
        services.AddScoped<IManufactureAnalysisMapper, ManufactureAnalysisMapper>();
        services.AddScoped<IItemFilterService, ItemFilterService>();

        // Register batch planning services
        services.AddScoped<IBatchPlanningService, BatchPlanningService>();
        services.AddScoped<IBatchDistributionCalculator, BatchDistributionCalculator>();

        // Register repositories
        services.AddScoped<IManufactureOrderRepository, ManufactureOrderRepository>();

        // Register application services
        services.AddScoped<IManufactureOrderApplicationService, ManufactureOrderApplicationService>();
        services.AddScoped<IProductNameFormatter, ProductNameFormatter>();
        
        // Register dashboard tiles
        services.RegisterTile<TodayProductionTile>();
        services.RegisterTile<NextDayProductionTile>();

        return services;
    }
}