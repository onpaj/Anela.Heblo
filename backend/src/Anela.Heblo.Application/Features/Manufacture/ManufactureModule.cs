using Anela.Heblo.Application.Features.Manufacture.Configuration;
using Anela.Heblo.Application.Features.Manufacture.DashboardTiles;
using Anela.Heblo.Application.Features.Manufacture.ErrorFilters;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Features.Manufacture.UseCases.GetManufactureProtocol;
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

        services.Configure<ManufactureErpOptions>(options =>
        {
            configuration.GetSection("ManufactureErp").Bind(options);
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
        services.AddScoped<IResidueDistributionCalculator, ResidueDistributionCalculator>();

        // Register repositories
        services.AddScoped<IManufactureOrderRepository, ManufactureOrderRepository>();

        // Register application services
        services.AddScoped<IManufactureOrderApplicationService, ManufactureOrderApplicationService>();
        services.AddScoped<IProductNameFormatter, ProductNameFormatter>();
        services.AddScoped<IManufactureNameBuilder, ManufactureNameBuilder>();
        services.AddScoped<IConfirmSemiProductManufactureWorkflow, ConfirmSemiProductManufactureWorkflow>();
        services.AddScoped<IConfirmProductCompletionWorkflow, ConfirmProductCompletionWorkflow>();

        // Register dashboard tiles
        services.RegisterTile<TodayProductionTile>();
        services.RegisterTile<NextDayProductionTile>();
        services.RegisterTile<ManualActionRequiredTile>();

        // Register protocol renderer placeholder (replaced by QuestPdfManufactureProtocolRenderer in Phase 6)
        services.AddScoped<IManufactureProtocolRenderer, NotImplementedManufactureProtocolRenderer>();

        // Register manufacture error transformation
        services.Scan(scan => scan
            .FromAssemblyOf<IManufactureErrorFilter>()
            .AddClasses(c => c.AssignableTo<IManufactureErrorFilter>()
                .InNamespaces("Anela.Heblo.Application.Features.Manufacture.ErrorFilters.Filters"))
            .AsImplementedInterfaces()
            .WithTransientLifetime());
        services.AddTransient<IManufactureErrorTransformer, ManufactureErrorTransformer>();

        return services;
    }
}