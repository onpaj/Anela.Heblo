using Anela.Heblo.Application.Features.ProductPricing.Services;
using Anela.Heblo.Domain.Features.ProductPricing;
using Anela.Heblo.Persistence.ProductPricing;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Features.ProductPricing;

public static class ProductPricingModule
{
    public static IServiceCollection AddProductPricingModule(this IServiceCollection services)
    {
        services.AddScoped<IPriceComparisonService, PriceComparisonService>();
        services.AddScoped<IProductPriceChangeLogRepository, ProductPriceChangeLogRepository>();

        // MediatR handlers are automatically registered by assembly scan.

        return services;
    }
}
