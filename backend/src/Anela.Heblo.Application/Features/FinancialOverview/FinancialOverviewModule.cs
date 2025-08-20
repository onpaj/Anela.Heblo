using Anela.Heblo.Domain.Features.FinancialOverview;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.FinancialOverview;

public static class FinancialOverviewModule
{
    public static IServiceCollection AddFinancialOverviewModule(this IServiceCollection services)
    {
        // Register real implementation that uses IErpStockClient and IProductPriceErpClient
        services.AddScoped<IStockValueService, StockValueService>();
        
        return services;
    }
}