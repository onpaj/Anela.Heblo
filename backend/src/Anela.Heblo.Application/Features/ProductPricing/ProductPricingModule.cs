using Anela.Heblo.Application.Features.ProductPricing.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ProductPricing;

public static class ProductPricingModule
{
    public static IServiceCollection AddProductPricingModule(this IServiceCollection services)
    {
        services.AddScoped<IPriceDivergenceReportService, PriceDivergenceReportService>();

        // MediatR handlers are automatically registered by assembly scan.

        return services;
    }
}
